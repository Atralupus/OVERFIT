using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 보스가 끊으려 한다 (설계 2026-09-30 조각2 §2). 던지기를 보면 반응 지연(18틱) 뒤부터 알고, 가장 가까운 끊을 자리(쉬기 · 달리기 중에는 그 틱 · 동작
/// 중에는 그 동작의 다음 캔슬 지점 · 지점이 없으면 동작이 끝난 뒤의 쉬기)에서 끊고, 멈칫(15틱) 뒤 반응의 동작(돌진)을 세운다. 실제 캐릭터 · 실제 보스 ·
/// 실제 동작에 대본(<see cref="ScriptPlanPicker"/>)으로 계획을 고정한다.
///
/// <para>
/// 판은 파이터 480 · 보스 1440 에서 선다(960 떨어져 3연격 계열의 칼은 파이터에 안 닿는다). 틱: 누른 틱 P 가 본 틱이고, P + 18 에 안다. 끊은 틱 R 부터
/// R + 14 까지 멈칫하고 R + 15 에 반응의 동작이 선다. 동작의 틱은 러너의 틱이다 — 동작이 선 판의 틱이 B 면 러너의 p 틱은 판의 B + p 다.
/// </para>
/// </summary>
public class BombReactionTests
{
    private const string _waitId = "기다림";

    private static readonly InputFrame _bomb = new(0, false, false, false, false, Bomb: true);
    private static readonly InputFrame _right = new(1, false, false, false, false);
    private static readonly InputFrame _attack = new(0, false, false, false, Attack: true);

    private static FighterConfig Real() => TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter];

    private static BombReactionDef Data => TestConfigs.Boss().BombReaction;

    /// <summary>반응 지연(틱) — 18.</summary>
    private static int Delay => BattleSim.TicksFor(Data.DelaySeconds);

    /// <summary>멈칫(틱) — 15.</summary>
    private static int Hesitate => BattleSim.TicksFor(Data.HesitateSeconds);

    /// <summary>누른 틱 + 이만큼이 놓는 틱이다 — 89.</summary>
    private static int ReleaseAfter => BattleSim.TicksFor(Real().Bomb.ThrowSeconds) - 1;

    /// <summary>
    /// 명부 <paramref name="roster"/>(없으면 3연격 · 돌진) 위에 대본 <paramref name="script"/> 를 얹은 판 — 보스는 안 죽는다. 동작 표는 실제
    /// <c>patterns.json</c> 에 <paramref name="extra"/> 를 더한 것이다(<paramref name="drop"/> 은 뺀다).
    /// </summary>
    private static BattleSim Sim(
        ScriptPlan[] script,
        string[]? roster = null,
        BossConfig? boss = null,
        Arena? arena = null,
        FighterConfig? fighter = null,
        Dictionary<string, PatternDef>? extra = null,
        string? drop = null)
    {
        string[] ids = roster ?? ["3연격", "돌진"];
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        foreach ((string id, PatternDef def) in extra ?? [])
        {
            patterns[id] = def;
        }

        if (drop is not null)
        {
            patterns.Remove(drop);
        }

        return new BattleSim(new BattleSetup
        {
            Arena = arena ?? TestConfigs.Arena(),
            Fighter = fighter ?? Real(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = boss ?? TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = ids,
            Patterns = patterns,
            Seed = 51,
            Picker = new ScriptPlanPicker(ids, patterns, script),
            MaxTicks = TestConfigs.MaxTicks(),
        });
    }

    /// <summary>3초 동안 아무것도 안 치는 동작 — 캔슬 지점이 없다. 반응할 자리가 동작 끝뿐인 판을 만든다.</summary>
    private static PatternDef Waiting() => new()
    {
        Tags = new PatternTags
        {
            DashWindow = 0,
            DashDirection = "out",
            Jumpable = false,
            AntiAir = false,
            Parryable = false,
            ParryWindow = 0,
            PunishGreed = false,
            Reach = "far",
            MultiHit = 1,
            Tracking = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0, Kind = "windup" },
            new() { T = 3.0, Kind = "end" },
        },
    };

    /// <summary><paramref name="pattern"/> 이 설 때까지 민다 — 선 판의 틱(B)을 돌려준다.</summary>
    private static int UntilBegins(BattleSim sim, string pattern, InputFrame input = default)
    {
        for (int i = 0; i < 60 * 60 && sim.Boss.CurrentPattern != pattern; i++)
        {
            sim.Tick(input);
        }

        sim.Boss.CurrentPattern.ShouldBe(pattern, $"{pattern} 이 안 섰다");
        return sim.Ticks;
    }

    /// <summary>판의 <paramref name="tick"/> 틱에 L 을 누르도록 그 앞 틱까지 민다.</summary>
    private static void ThrowAt(BattleSim sim, int tick)
    {
        TestConfigs.UntilTick(sim, tick - 1);
        sim.Tick(_bomb);
        sim.Fighter.Throwing.ShouldBeTrue($"{tick}틱에 던지기가 안 섰다");
    }

    /// <summary>로그에서 <c>bomb_react</c> 줄들.</summary>
    private static List<string> Reacts(LogCapture log) => [.. log.Lines.Where(l => l.StartsWith("[boss][D] bomb_react ", StringComparison.Ordinal))];

    [Fact]
    public void 던진_틱_18틱_뒤에_알고_쉬는_보스는_그_틱에_끊는다()
    {
        BattleSim sim = Sim([new ScriptPlan(3.0, "3연격")]);
        using var log = new LogCapture();

        ThrowAt(sim, 1);
        TestConfigs.UntilTick(sim, 1 + Delay - 1);
        sim.BossAlert.ShouldBeFalse("반응 지연 전에 알았다");
        sim.BossHesitating.ShouldBeFalse();

        sim.Tick(default);

        sim.BossAlert.ShouldBeTrue("던진 틱 + 18 에 알아야 한다");
        sim.BossHesitating.ShouldBeTrue("쉬는 보스는 아는 그 틱에 끊는다");
        log.Lines.ShouldContain($"[boss][D] bomb_seen throw=1 tick={1 + Delay}");
        Reacts(log).ShouldBe([$"[boss][D] bomb_react from=rest tick={1 + Delay}"]);
    }

    [Fact]
    public void 멈칫_15틱_뒤에_반응의_동작을_세운다()
    {
        BattleSim sim = Sim([new ScriptPlan(3.0, "3연격")]);
        ThrowAt(sim, 1);
        int reacted = 1 + Delay;
        TestConfigs.UntilTick(sim, reacted);

        for (int t = reacted; t < reacted + Hesitate; t++)
        {
            sim.Boss.CurrentPattern.ShouldBeNull($"멈칫 중({t}틱)에 동작이 섰다");
            sim.BossHesitating.ShouldBeTrue($"{t}틱에 멈칫이 끝났다");
            sim.Tick(default);
        }

        sim.Ticks.ShouldBe(reacted + Hesitate);
        sim.Boss.CurrentPattern.ShouldBe(Data.Move, "멈칫 뒤에 반응의 동작이 서야 한다");
        sim.BossHesitating.ShouldBeFalse();
        sim.BossAlert.ShouldBeTrue("던지기가 안 끝났다 — 알아챔은 그대로다");
    }

    [Fact]
    public void 달리는_보스도_아는_틱에_끊는다()
    {
        // 쉬기 0.4초(24틱) 뒤 파이터 앞 280 까지 달린다(49틱) — 20틱에 던지면 38틱에 달리는 중이다.
        BattleSim sim = Sim([new ScriptPlan(0.4, "3연격", Run: true)]);
        using var log = new LogCapture();
        ThrowAt(sim, 20);
        TestConfigs.UntilTick(sim, 20 + Delay - 1);
        sim.BossRunning.ShouldBeTrue("던진 걸 알 때 보스가 달리는 중이어야 이 테스트가 뜻이 있다");

        sim.Tick(default);

        sim.BossRunning.ShouldBeFalse("끊으면 달리기도 걷힌다");
        sim.BossHesitating.ShouldBeTrue();
        Reacts(log).ShouldBe([$"[boss][D] bomb_react from=run tick={20 + Delay}"]);
    }

    [Fact]
    public void 동작_중이면_계획이_안_고른_다음_캔슬_지점에서_끊는다()
    {
        // 계획은 안 끊는다 — 3연격의 캔슬 지점(78 · 144)은 계획이 고르지 않았어도 반응이 쓴다. 러너 30 에 던지면 48 에 알고, 다음 지점 78 에서 끊는다.
        BattleSim sim = Sim([new ScriptPlan(0.8, "3연격")]);
        int begun = UntilBegins(sim, "3연격");
        using var log = new LogCapture();
        ThrowAt(sim, begun + 30);
        TestConfigs.UntilTick(sim, begun + 77);
        sim.BossHesitating.ShouldBeFalse("캔슬 지점 전에 끊었다 — 끊는 것은 지점에서만이다(우산 §11)");
        sim.Boss.CurrentPattern.ShouldBe("3연격");

        sim.Tick(default);

        sim.BossHesitating.ShouldBeTrue("다음 캔슬 지점(78)에서 끊어야 한다");
        sim.Boss.CurrentPattern.ShouldBeNull();
        Reacts(log).ShouldBe([$"[boss][D] bomb_react from=3연격 at=78 tick={begun + 78}"]);
    }

    [Fact]
    public void 끊으면_던지는_쪽으로_돌아선다()
    {
        // 동작 중에는 방향이 잠긴다 — 파이터가 쉬기부터 오른쪽으로 걸어 3연격 도중 보스를 지나 등 뒤로 가서 던진다(칼 두 대는 맞고 지나간다 — 맞아도
        // 안 멈춘다). 러너 126 전에 던지면 다음 지점 144 에서 끊는다. 끊는 틱(동작 사이)에 돌아서서 던지는 쪽을 본다(§2.3).
        BattleSim sim = Sim([new ScriptPlan(0.8, "3연격")]);
        int begun = UntilBegins(sim, "3연격", _right);
        sim.Boss.Facing.ShouldBe(-1);
        for (int i = 0; i < 300 && sim.Fighter.X < sim.Boss.X + sim.Boss.HalfWidth + 40; i++)
        {
            sim.Tick(_right);
        }

        sim.Boss.CurrentPattern.ShouldBe("3연격", "등 뒤로 가기 전에 3연격이 끝났다");
        (sim.Ticks - begun).ShouldBeLessThan(144 - Delay, "등 뒤로 간 것이 늦어 다음 지점(144) 전에 못 안다 — 이 테스트가 아무것도 안 본다");
        sim.Boss.Facing.ShouldBe(-1, "동작 중에 돌아섰다");
        ThrowAt(sim, sim.Ticks + 1);
        for (int i = 0; i < 200 && !sim.BossHesitating; i++)
        {
            sim.Tick(default);
        }

        sim.BossHesitating.ShouldBeTrue();
        sim.Ticks.ShouldBe(begun + 144, "등 뒤로 간 뒤의 다음 캔슬 지점(144)에서 끊어야 한다");
        sim.Boss.Facing.ShouldBe(1, "끊으면 던지는 쪽으로 돌아서야 한다");
    }

    [Fact]
    public void 잇는_동작의_캔슬_지점에서도_끊는다()
    {
        // 계획의 캔슬 한 번은 이미 썼다(빠른 3연격 → 36 에서 3연격). 잇는 동작은 계획으로는 끝까지 가지만 반응은 그 지점(78)을 쓴다.
        string[] roster = ["빠른 3연격", "3연격", "돌진"];
        BattleSim sim = Sim([new ScriptPlan(0.8, "빠른 3연격", 0, "3연격")], roster);
        int follow = UntilBegins(sim, "3연격");
        using var log = new LogCapture();
        ThrowAt(sim, follow + 30);
        for (int i = 0; i < 200 && !sim.BossHesitating; i++)
        {
            sim.Tick(default);
        }

        Reacts(log).ShouldBe([$"[boss][D] bomb_react from=3연격 at=78 tick={follow + 78}"]);
    }

    [Fact]
    public void 계획한_캔슬과_같은_틱이면_반응이_이긴다()
    {
        // 계획은 78 에서 끊고 잡기로 잇는다. 같은 틱에 반응이 끊으면 계획의 잇는 동작은 버린다 — 멈칫 뒤 돌진이다.
        string[] roster = ["3연격", "잡기", "돌진"];
        BattleSim sim = Sim([new ScriptPlan(0.8, "3연격", 0, "잡기")], roster);
        int begun = UntilBegins(sim, "3연격");
        using var log = new LogCapture();
        ThrowAt(sim, begun + 30);
        TestConfigs.UntilTick(sim, begun + 78 + Hesitate);

        Reacts(log).ShouldBe([$"[boss][D] bomb_react from=3연격 at=78 tick={begun + 78}"]);
        log.Lines.ShouldNotContain(l => l.StartsWith("[boss][D] cancel ", StringComparison.Ordinal), "계획한 캔슬이 반응을 이겼다");
        log.Lines.ShouldNotContain(l => l.StartsWith("[boss][D] pattern_begin id=잡기", StringComparison.Ordinal));
        sim.Boss.CurrentPattern.ShouldBe(Data.Move);
    }

    [Fact]
    public void 반응이_끝낸_계획의_캔슬은_반응의_동작을_안_끊는다()
    {
        // 계획은 3연격을 144 에서 끊고 잡기로 잇는다. 반응이 78 에서 끊어 계획이 끝나면 남은 캔슬도 버린다 — 안 버리면 반응의 동작이 러너 144 에
        // 옛 계획의 지점으로 끊긴다. 반응의 동작을 길게(엇박 3연격 · 240틱) 두어야 그 지점에 닿는다(돌진은 78틱이라 이 버그를 못 본다).
        BombReactionDef slow = new() { DelaySeconds = Data.DelaySeconds, HesitateSeconds = Data.HesitateSeconds, Move = "엇박 3연격" };
        string[] roster = ["3연격", "잡기", "엇박 3연격"];
        BattleSim sim = Sim([new ScriptPlan(0.8, "3연격", 1, "잡기")], roster, TestConfigs.Boss(maxHealth: 999_999, reaction: slow));
        int begun = UntilBegins(sim, "3연격");
        using var log = new LogCapture();
        ThrowAt(sim, begun + 30);
        int reaction = UntilBegins(sim, "엇박 3연격");
        for (int i = 0; i < 400 && sim.Boss.CurrentPattern == "엇박 3연격"; i++)
        {
            sim.Tick(default);
        }

        reaction.ShouldBe(begun + 78 + Hesitate);
        log.Lines.ShouldContain(l => l.StartsWith("[boss][D] pattern_end id=엇박 3연격", StringComparison.Ordinal), "반응의 동작이 끝까지 안 갔다");
        log.Lines.ShouldNotContain(l => l.StartsWith("[boss][D] cancel ", StringComparison.Ordinal), "버린 계획의 캔슬이 반응의 동작을 끊었다");
        log.Lines.ShouldNotContain(l => l.StartsWith("[boss][D] pattern_begin id=잡기", StringComparison.Ordinal));
    }

    [Fact]
    public void 캔슬_지점이_없는_동작은_끝난_뒤_쉬기에서_끊는다()
    {
        // 올려베기(105틱)는 캔슬 지점이 없다 — 끝나는 틱에 다음 계획이 서고, 그 다음 틱(쉬기)에 끊는다. 30틱에 던져야 놓기(119) 전에 그 자리(106)가 온다.
        string[] roster = ["올려베기", "돌진"];
        BattleSim sim = Sim([new ScriptPlan(0.8, "올려베기")], roster);
        int begun = UntilBegins(sim, "올려베기");
        using var log = new LogCapture();
        ThrowAt(sim, begun + 30);
        TestConfigs.UntilTick(sim, begun + 105);
        sim.BossHesitating.ShouldBeFalse("동작 중에 끊었다 — 올려베기는 지점이 없다");

        sim.Tick(default);

        Reacts(log).ShouldBe([$"[boss][D] bomb_react from=rest tick={begun + 106}"]);
    }

    [Fact]
    public void 탈진한_보스는_풀린_뒤의_쉬기에서_끊는다()
    {
        // 1타 한 대로 게이지가 차는 파이터가 걸어가 무너뜨리고 곧장 던진다. 탈진 중에도 보지만(알아챔) 못 움직인다 — 풀린 첫 틱(쉬기)에 끊는다.
        BattleSim sim = Sim([new ScriptPlan(1000, "3연격")], fighter: TestConfigs.Breaker());
        double reach = sim.Boss.X - sim.Boss.HalfWidth - TestConfigs.TestSword().Bounds.X1 + 1;
        for (int i = 0; i < 600 && sim.Fighter.X < reach; i++)
        {
            sim.Tick(_right);
        }

        sim.Tick(_attack);
        for (int i = 0; i < 60 && !sim.Boss.Exhausted; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.Exhausted.ShouldBeTrue("칼이 보스를 못 무너뜨렸다");
        for (int i = 0; i < 60 && sim.Fighter.Action != FighterAction.Idle; i++)
        {
            sim.Tick(default);
        }

        using var log = new LogCapture();
        ThrowAt(sim, sim.Ticks + 1);
        int thrown = sim.Ticks;
        int recovered = 0;
        for (int i = 0; i < 200 && recovered == 0; i++)
        {
            sim.Tick(default);
            if (sim.Boss.Exhausted)
            {
                sim.BossHesitating.ShouldBeFalse("탈진한 보스가 끊었다");
            }
            else
            {
                recovered = sim.Ticks;
            }
        }

        (recovered - thrown).ShouldBeGreaterThan(Delay, "탈진이 알기 전에 풀렸다 — 이 테스트가 아무것도 안 본다");
        (recovered - thrown).ShouldBeLessThan(ReleaseAfter, "놓은 뒤에 풀렸다 — 이 테스트가 아무것도 안 본다");
        Reacts(log).ShouldBe([$"[boss][D] bomb_react from=rest tick={recovered}"]);
    }

    [Fact]
    public void 반응_중에는_새로_안_끊는다()
    {
        // 쉬는 보스가 끊고 돌진으로 첫 던지기를 끊는다. 파이터가 곧장 또 던져도 보스는 돌진이 끝날 때까지 못 끊는다 — 끝난 뒤의 쉬기에서 끊는다.
        BattleSim sim = Sim([new ScriptPlan(3.0, "3연격")]);
        using var log = new LogCapture();
        ThrowAt(sim, 1);
        for (int i = 0; i < 200 && !sim.Fighter.ThrowLost; i++)
        {
            sim.Tick(default);
        }

        sim.Fighter.ThrowLost.ShouldBeTrue("반응의 돌진이 첫 던지기를 못 끊었다");
        sim.Boss.CurrentPattern.ShouldBe(Data.Move);
        sim.Tick(_bomb);
        sim.Fighter.Throwing.ShouldBeTrue();
        int second = sim.Ticks;
        int ended = 0;
        for (int i = 0; i < 200 && ended == 0; i++)
        {
            sim.Tick(default);
            ended = sim.Boss.CurrentPattern is null ? sim.Ticks : 0;
        }

        (ended - second).ShouldBeGreaterThan(Delay, "반응의 돌진이 둘째 던지기를 알기 전에 끝났다 — 이 테스트가 아무것도 안 본다");
        sim.Tick(default);

        // 돌진이 끝나는 틱에 다음 계획이 서고, 그 다음 틱(쉬기)에 끊는다 — 알기는 돌진 도중에 알았다.
        List<string> reacts = Reacts(log);
        reacts.Count.ShouldBe(2);
        reacts[1].ShouldBe($"[boss][D] bomb_react from=rest tick={ended + 1}", "반응의 돌진 도중에 새로 끊었다");
    }

    [Fact]
    public void 놓기_전에_끊을_자리가_안_오면_못_끊는다()
    {
        // 3초짜리 동작은 캔슬 지점이 없다 — 던지고 90틱 안에 끝나지 않으므로 끊을 자리가 안 온다. 폭탄은 그대로 날아가 떨어진다. 로그의 다음 자리는
        // 동작이 끝나는 러너 틱(180)이다(§2.6).
        string[] roster = [_waitId, "돌진"];
        BattleSim sim = Sim([new ScriptPlan(0.2, _waitId)], roster, extra: new() { [_waitId] = Waiting() });
        int begun = UntilBegins(sim, _waitId);
        int hp = sim.Boss.Health;
        using var log = new LogCapture();
        ThrowAt(sim, begun + 1);
        int release = begun + 1 + ReleaseAfter;
        TestConfigs.UntilTick(sim, release);

        sim.Fighter.ThrowReleased.ShouldBeTrue();
        sim.BossAlert.ShouldBeFalse("던지기가 끝나면 알아챔이 사라진다");
        Reacts(log).ShouldBeEmpty();
        log.Lines.ShouldContain($"[boss][D] bomb_late next={BattleSim.TicksFor(3.0)} release={release} tick={release}");
        TestConfigs.UntilTick(sim, release + BattleSim.TicksFor(Real().Bomb.FlightSeconds));
        sim.Boss.Health.ShouldBe(hp - Real().Bomb.Damage);
    }

    [Fact]
    public void 제때_못_닿아도_끊으려_한다()
    {
        // 3연격이 서자마자 던진다 — 첫 캔슬 지점(78)에서 끊지만 멈칫 · 달림 · 선딜을 더하면 놓는 틱(90) 뒤에야 닿는다. 끊는 것이 노력이고 닿는지는
        // 거리와 지점이 정한다(§2.2) — 폭탄은 달려오는 보스에게 떨어진다.
        BattleSim sim = Sim([new ScriptPlan(0.8, "3연격")]);
        int begun = UntilBegins(sim, "3연격");
        int hp = sim.Boss.Health;
        using var log = new LogCapture();
        ThrowAt(sim, begun + 1);
        TestConfigs.UntilTick(sim, begun + 1 + ReleaseAfter + BattleSim.TicksFor(Real().Bomb.FlightSeconds));

        Reacts(log).ShouldBe([$"[boss][D] bomb_react from=3연격 at=78 tick={begun + 78}"]);
        log.Lines.ShouldNotContain(l => l.StartsWith("[fighter][D] bomb_lost", StringComparison.Ordinal));
        sim.Boss.Health.ShouldBe(hp - Real().Bomb.Damage, "끊으려 했지만 늦었다 — 폭탄은 떨어져야 한다");
    }

    [Fact]
    public void 쉬는_보스_앞의_던지기는_가장_먼_거리에서도_놓기_전에_끊긴다()
    {
        // 설계 §1.4 — throw_seconds 1.5 의 근거. 중심 사이 1805(아레나 1920 의 가장 먼 거리)에서도 반응 18 + 멈칫 15 + 달림 26 + 돌진 선딜 24 가
        // 누른 틱 + 88 안이다. 폭을 3610 으로 늘려 판이 설 때의 거리(폭의 절반)를 1805 로 만든다.
        BattleSim sim = Sim([new ScriptPlan(3.0, "3연격")], arena: new Arena(3610));
        (sim.Boss.X - sim.Fighter.X).ShouldBe(1805);
        using var log = new LogCapture();
        ThrowAt(sim, 1);
        int lost = 0;
        for (int i = 0; i < ReleaseAfter && lost == 0; i++)
        {
            sim.Tick(default);
            lost = sim.Fighter.ThrowLost ? sim.Ticks : 0;
        }

        lost.ShouldBeGreaterThan(0, "놓기 전에 반응의 돌진이 못 닿았다");
        lost.ShouldBeLessThanOrEqualTo(1 + ReleaseAfter - 1);
        log.Lines.ShouldContain($"[fighter][D] bomb_lost by={Data.Move} react=1 tick={lost}");
        sim.BombsInFlight.ShouldBeEmpty();
    }

    [Fact]
    public void 반응의_동작이_끝나면_다음_계획을_고른다()
    {
        BattleSim sim = Sim([new ScriptPlan(3.0, "3연격")]);
        ThrowAt(sim, 1);
        int reaction = UntilBegins(sim, Data.Move);
        int plans = sim.Plans.Count;
        using var log = new LogCapture();
        for (int i = 0; i < 200 && sim.Boss.CurrentPattern == Data.Move; i++)
        {
            sim.Tick(default);
        }

        reaction.ShouldBe(1 + Delay + Hesitate);
        sim.Plans.Count.ShouldBe(plans + 1, "반응의 동작이 끝나면 다음 계획을 골라야 한다");
        log.Lines.ShouldContain(l => l.StartsWith($"[boss][D] pattern_end id={Data.Move}", StringComparison.Ordinal));
        log.Lines.ShouldContain(l => l.StartsWith("[boss][D] plan n=1 ", StringComparison.Ordinal));
        sim.Drawn.ShouldNotContain("3연격", "끊긴 계획의 첫 동작이 섰다");
    }

    [Fact]
    public void 멈칫_중에_탈진하면_반응의_동작이_안_선다()
    {
        // 멈칫을 3초로 늘린 보스 — 던지기를 놓은 파이터가 멈칫 중인 보스를 쳐서 무너뜨린다(1타 한 대로 차는 파이터). 탈진이 반응도 끝낸다.
        BombReactionDef long_ = new() { DelaySeconds = Data.DelaySeconds, HesitateSeconds = 3.0, Move = Data.Move };
        BattleSim sim = Sim([new ScriptPlan(1000, "3연격")], boss: TestConfigs.Boss(maxHealth: 999_999, reaction: long_), fighter: TestConfigs.Breaker());
        double reach = sim.Boss.X - sim.Boss.HalfWidth - TestConfigs.TestSword().Bounds.X1 + 1;
        for (int i = 0; i < 600 && sim.Fighter.X < reach; i++)
        {
            sim.Tick(_right);
        }

        ThrowAt(sim, sim.Ticks + 1);
        for (int i = 0; i < 200 && sim.Fighter.Action != FighterAction.Idle; i++)
        {
            sim.Tick(default);
        }

        sim.BossHesitating.ShouldBeTrue("던지기가 끝났을 때 보스가 멈칫 중이어야 이 테스트가 뜻이 있다");
        sim.Tick(_attack);
        for (int i = 0; i < 60 && !sim.Boss.Exhausted; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.Exhausted.ShouldBeTrue("칼이 멈칫 중인 보스를 못 무너뜨렸다");
        sim.BossHesitating.ShouldBeFalse("탈진이 멈칫을 끝내야 한다");
        for (int i = 0; i < 300; i++)
        {
            sim.Tick(default);
            sim.Boss.CurrentPattern.ShouldNotBe(Data.Move, "탈진이 끝낸 반응의 동작이 섰다");
        }
    }

    [Fact]
    public void 반응의_동작이_데이터에_없으면_E_를_한_번_남기고_안_끊는다()
    {
        // 움직임의 motion_missing 과 같은 대우다 — 데이터 테스트(BossDataTests)가 먼저 막는다. 쉬는 동안은 매 틱이 끊을 자리라 한 번만 남긴다.
        BattleSim sim = Sim([new ScriptPlan(1.0, "3연격")], ["3연격"], drop: "돌진");
        using var log = new LogCapture();
        ThrowAt(sim, 1);
        UntilBegins(sim, "3연격");

        log.Lines.Count(l => l.StartsWith($"[boss][E] bomb_reaction_missing id={Data.Move}", StringComparison.Ordinal)).ShouldBe(1);
        Reacts(log).ShouldBeEmpty();
    }

    [Fact]
    public void 기록_쉬는_보스가_끊은_던지기는_cut_과_끊은_틱이다()
    {
        // 설계 2026-09-30 조각2 §4 — 던지기마다 (던진 틱, 그때의 동작, 결과, 보스가 끊은 틱). 쉬는 보스라 동작이 없고, 반응의 돌진이 놓기 전에 끊는다.
        BattleSim sim = Sim([new ScriptPlan(3.0, "3연격")]);
        ThrowAt(sim, 1);
        for (int i = 0; i < 200 && !sim.Fighter.ThrowLost; i++)
        {
            sim.Tick(default);
        }

        sim.BombRecords.ShouldBe([new BombRecord(1, null, BombOutcome.Cut, 1 + Delay)]);
    }

    [Fact]
    public void 기록_끊으려_했지만_늦은_던지기는_landed_와_끊은_틱이다()
    {
        // 3연격이 서자마자 던진다 — 보스는 첫 캔슬 지점(78)에서 끊지만 놓기 전에 못 닿는다(제때_못_닿아도_끊으려_한다). landed 인데 끊은 틱이 있다.
        BattleSim sim = Sim([new ScriptPlan(0.8, "3연격")]);
        int begun = UntilBegins(sim, "3연격");
        ThrowAt(sim, begun + 1);
        TestConfigs.UntilTick(sim, begun + 1 + ReleaseAfter + BattleSim.TicksFor(Real().Bomb.FlightSeconds));

        sim.BombRecords.ShouldBe([new BombRecord(begun + 1, "3연격", BombOutcome.Landed, begun + 78)]);
    }

    [Fact]
    public void 기록_끊을_자리가_안_온_던지기는_landed_와_null_이다()
    {
        string[] roster = [_waitId, "돌진"];
        BattleSim sim = Sim([new ScriptPlan(0.2, _waitId)], roster, extra: new() { [_waitId] = Waiting() });
        int begun = UntilBegins(sim, _waitId);
        ThrowAt(sim, begun + 1);
        TestConfigs.UntilTick(sim, begun + 1 + ReleaseAfter + BattleSim.TicksFor(Real().Bomb.FlightSeconds));

        sim.BombRecords.ShouldBe([new BombRecord(begun + 1, _waitId, BombOutcome.Landed, null)]);
    }

    [Fact]
    public void 기록_동작의_판정에_잃은_던지기는_hit_이다()
    {
        // 잡기(0.6초에 바닥 전체를 붙든다 · 캔슬 지점이 없다)가 서자마자 던진다 — 보스는 18틱 뒤에 알지만 잡기가 끝나야 끊을 자리가 온다. 그 전에 잡기가
        // 던지는 손을 붙든다. 보스가 끊지 않았으니 끊은 틱이 없다.
        string[] roster = ["잡기", "돌진"];
        BattleSim sim = Sim([new ScriptPlan(0.8, "잡기")], roster);
        int begun = UntilBegins(sim, "잡기");
        ThrowAt(sim, begun + 1);
        for (int i = 0; i < 200 && !sim.Fighter.ThrowLost; i++)
        {
            sim.Tick(default);
        }

        sim.Fighter.ThrowLost.ShouldBeTrue("잡기가 던지기를 못 끊었다");
        sim.BombRecords.ShouldBe([new BombRecord(begun + 1, "잡기", BombOutcome.Hit, null)]);
    }

    [Fact]
    public void 반응의_동작은_명부에_없어도_선다()
    {
        BattleSim sim = Sim([new ScriptPlan(3.0, "3연격")], ["3연격"]);
        ThrowAt(sim, 1);

        int reaction = UntilBegins(sim, Data.Move);

        reaction.ShouldBe(1 + Delay + Hesitate);
    }
    [Fact]
    public void GIF_bombcut_3연격_50틱의_던지기는_첫_캔슬_지점에서_끊겨_놓기_전에_잃는다()
    {
        // GifRunner 의 bombcut 대본(#147) — 960 떨어져 3연격 50틱에 던지면 68 에 알고 첫 캔슬 지점(78)에서 끊어, 멈칫 · 달림 · 돌진 선딜 뒤 놓는
        // 틱(139) 전에 닿는다.
        BattleSim sim = Sim([new ScriptPlan(0.8, "3연격")]);
        int begun = UntilBegins(sim, "3연격");
        using var log = new LogCapture();
        ThrowAt(sim, begun + 50);
        TestConfigs.UntilTick(sim, begun + 50 + ReleaseAfter);

        Reacts(log).ShouldBe([$"[boss][D] bomb_react from=3연격 at=78 tick={begun + 78}"]);
        log.Lines.ShouldContain(l => l.StartsWith($"[fighter][D] bomb_lost by={Data.Move} react=1 ", StringComparison.Ordinal));
    }

    [Fact]
    public void GIF_bomb_3연격_2틱의_던지기는_첫_캔슬_지점에서_끊겨도_늦어_떨어진다()
    {
        // GifRunner 의 bomb 대본(#147) — 위의 「제때_못_닿아도_끊으려_한다」 를 대본이 누를 수 있는 첫 틱(2)으로.
        BattleSim sim = Sim([new ScriptPlan(0.8, "3연격")]);
        int begun = UntilBegins(sim, "3연격");
        int hp = sim.Boss.Health;
        using var log = new LogCapture();
        ThrowAt(sim, begun + 2);
        TestConfigs.UntilTick(sim, begun + 2 + ReleaseAfter + BattleSim.TicksFor(Real().Bomb.FlightSeconds));

        Reacts(log).ShouldBe([$"[boss][D] bomb_react from=3연격 at=78 tick={begun + 78}"]);
        sim.Boss.Health.ShouldBe(hp - Real().Bomb.Damage, "끊으려 했지만 늦었다 — 폭탄은 떨어져야 한다");
    }
}
