using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 판 위의 형태 전환 (설계 2026-10-01 조각1 §2). 폭탄(60)이나 칼로 문턱을 넘긴다 — 보스는 3초 동안 아무것도 안 치고 캔슬 지점도 없는 동작(<c>기다림</c>)을
/// 돌아 끊을 자리가 안 온다(폭탄이 산다). 판은 파이터 480 · 보스 1440 에서 선다.
/// </summary>
public class BossFormBattleTests
{
    private const string _waitId = "기다림";
    private static readonly InputFrame _bomb = new(0, false, false, false, Bomb: true);
    private static readonly InputFrame _attack = new(0, false, false, Attack: true);
    private static readonly InputFrame _right = new(1, false, false, false);

    private static FighterConfig Real() => TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter];

    /// <summary>누른 틱 + 이만큼이 놓는 틱 — 89.</summary>
    private static int Release => BattleSim.TicksFor(Real().Bomb.ThrowSeconds) - 1;

    private static int Flight => BattleSim.TicksFor(Real().Bomb.FlightSeconds);

    private static int Shift => BattleSim.TicksFor(TestConfigs.Boss().Forms.ShiftSeconds);

    private static PatternDef Waiting() => new()
    {
        Tags = new PatternTags
        {
            DashWindow = 0,
            DashDirection = "out",
            Jumpable = false,
            AntiAir = false,
            PunishGreed = false,
            Reach = "far",
            MultiHit = 1,
            Tracking = false,
        },
        Timeline = new List<PatternStep> { new() { T = 0, Kind = "windup" }, new() { T = 3.0, Kind = "end" } },
    };

    /// <summary>시작 체력 <paramref name="start"/> · 명부 [기다림, 돌진] · 대본 <paramref name="script"/>(없으면 0.2초 쉬고 기다림).</summary>
    private static BattleSim Sim(int start, params ScriptPlan[] script)
    {
        string[] ids = [_waitId, "돌진"];
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        patterns[_waitId] = Waiting();
        ScriptPlan[] plans = script.Length > 0 ? script : [new ScriptPlan(0.2, _waitId)];
        return new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = Real(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(),
            BossStartHealth = start,
            PatternIds = ids,
            Patterns = patterns,
            Seed = 51,
            Picker = new ScriptPlanPicker(ids, patterns, plans),
            MaxTicks = TestConfigs.MaxTicks(),
        });
    }

    /// <summary>기다림이 선 틱 B 를 돌려준다 — 그 다음 틱(B + 1)에 던진다. B + 1 + 89 에 놓고 + 30 에 떨어진다.</summary>
    private static int ThrowIntoWait(BattleSim sim)
    {
        for (int i = 0; i < 600 && sim.Boss.CurrentPattern != _waitId; i++)
        {
            sim.Tick(default);
        }

        int begun = sim.Ticks;
        sim.Tick(_bomb);
        sim.Fighter.Throwing.ShouldBeTrue("던지기가 안 섰다");
        return begun;
    }

    /// <summary>보스 앞 150 안으로 걸어가 <paramref name="every"/> 틱마다 J 를 누르며 전환이 설 때까지 민다.</summary>
    private static void StrikeUntilShift(BattleSim sim, int every)
    {
        for (int i = 0; i < 3000 && !sim.Forms.Shifting; i++)
        {
            bool near = sim.Boss.X - sim.Fighter.X < 150;
            sim.Tick(near ? (i % every == 0 ? _attack : default) : _right);
        }

        sim.Forms.Shifting.ShouldBeTrue("칼로 문턱에 못 닿았다");
    }

    [Fact]
    public void 문턱을_넘는_폭탄은_문턱에서_멈추고_그_틱에_전환이_선다()
    {
        BattleSim sim = Sim(630);
        int begun = ThrowIntoWait(sim);
        int land = begun + 1 + Release + Flight;
        using var log = new LogCapture();
        TestConfigs.UntilTick(sim, land);

        sim.Boss.Health.ShouldBe(600, "60 이 문턱 600 아래로 깎았다");
        (sim.Forms.Shifting, sim.Forms.Form).ShouldBe((true, 1));
        sim.Forms.Shifts.ShouldBe(new[] { land });
        sim.Boss.CurrentPattern.ShouldBeNull("전환이 하던 동작을 안 걷었다");
        log.Lines.ShouldContain($"[boss][I] form_shift from=1 to=2 hp=600 tick={land}");
    }

    [Fact]
    public void 전환은_90틱_뒤에_끝나_형태_2_바닥_400_이고_쉬기는_그_뒤부터다()
    {
        BattleSim sim = Sim(630, new ScriptPlan(0.2, _waitId), new ScriptPlan(1.0, _waitId));
        int begun = ThrowIntoWait(sim);
        int land = begun + 1 + Release + Flight;
        TestConfigs.UntilTick(sim, land);
        using var log = new LogCapture();
        TestConfigs.UntilTick(sim, land + Shift - 1);
        sim.Forms.Shifting.ShouldBeTrue("90틱 전에 끝났다");

        TestConfigs.UntilTick(sim, land + Shift);
        (sim.Forms.Shifting, sim.Forms.Form, sim.Forms.Floor).ShouldBe((false, 2, 400));
        log.Lines.ShouldContain($"[boss][I] form=2 tick={land + Shift}");

        // 다음 계획(1.0초 쉬기 = 60틱)은 전환이 끝난 뒤부터 센다.
        TestConfigs.UntilTick(sim, land + Shift + 59);
        sim.Boss.CurrentPattern.ShouldBeNull("쉬기가 전환 동안에도 셌다");
        TestConfigs.UntilTick(sim, land + Shift + 60);
        sim.Boss.CurrentPattern.ShouldBe(_waitId);
    }

    [Fact]
    public void 전환을_세운_폭탄의_기록은_landed_다()
    {
        // 무적이 막는 폭탄은 판 위에서 거의 안 생긴다 — 던지기(89) · 경직(15) · 비행(30)이 전환(90)보다 길어, 첫 폭탄이 세운 전환 안에 둘째가 못 떨어진다.
        // 막는 길은 칼과 같은 한 자리(BattleSim.DamageBoss)라 칼 테스트가 본다. 여기서는 전환을 세운 폭탄이 기록에 landed 로 남는지만 본다.
        BattleSim sim = Sim(605);
        int begun = ThrowIntoWait(sim);
        TestConfigs.UntilTick(sim, begun + 1 + Release + Flight);
        sim.Forms.Shifting.ShouldBeTrue();
        sim.BombRecords.Single().Outcome.ShouldBe(BombOutcome.Landed);
    }

    [Fact]
    public void 전환_중의_칼은_피해도_경직_채움도_없다()
    {
        // 시작 체력 605 · 첫 칼(10)이 600 에 멈추며 전환을 세우고, 전환 중의 칼은 막힌다.
        BattleSim sim = Sim(605, new ScriptPlan(3.0, _waitId));
        using var log = new LogCapture();
        StrikeUntilShift(sim, 20);
        sim.Boss.Health.ShouldBe(600);
        int shiftAt = sim.Ticks;
        for (int i = 0; i < Shift - 1; i++)
        {
            sim.Tick(i % 20 == 0 ? _attack : default);
        }

        sim.Ticks.ShouldBe(shiftAt + Shift - 1);
        sim.Forms.Shifting.ShouldBeTrue();
        sim.Boss.Health.ShouldBe(600, "무적 동안 체력이 깎였다");
        sim.Poise.Value.ShouldBe(0, "무적 동안 게이지가 찼다");
        log.Lines.ShouldContain(l => l.StartsWith("[boss][D] shielded src=strike ", StringComparison.Ordinal));
    }

    [Fact]
    public void 전환_중에_본_던지기는_전환이_끝난_뒤에_끊는다()
    {
        // 칼로 전환을 세우고 전환 중에 던진다. 보스는 던진 틱 + 18 에 알지만 전환 동안은 아무것도 안 하고, 전환이 끝나면 쉬는 중이라 곧 끊는다(§2.3).
        BattleSim sim = Sim(605, new ScriptPlan(3.0, _waitId));
        StrikeUntilShift(sim, 20);
        int shiftAt = sim.Forms.Shifts.Single();
        using var log = new LogCapture();
        for (int i = 0; i < 60 && !sim.Fighter.Throwing; i++)
        {
            sim.Tick(_bomb);
        }

        sim.Fighter.Throwing.ShouldBeTrue("전환 중에 못 던졌다");
        sim.Forms.Shifting.ShouldBeTrue("던지기가 전환 뒤에 섰다 — 이 테스트가 전환 중의 던지기를 안 본다");
        TestConfigs.UntilTick(sim, shiftAt + Shift + 2);
        string react = log.Lines.Single(l => l.StartsWith("[boss][D] bomb_react ", StringComparison.Ordinal));
        int at = int.Parse(react[(react.LastIndexOf("tick=", StringComparison.Ordinal) + 5)..], CultureInfo.InvariantCulture);
        at.ShouldBeInRange(shiftAt + Shift, shiftAt + Shift + 1, "전환 중에 끊었거나 끝난 뒤에도 안 끊었다");
    }

    [Fact]
    public void 마지막_형태는_0_까지_깎여_이긴다()
    {
        BattleSim sim = Sim(50);
        sim.Forms.Form.ShouldBe(3);
        ThrowIntoWait(sim);
        BattleOutcome? outcome = null;
        for (int i = 0; i < Release + Flight + 2 && outcome is null; i++)
        {
            outcome = sim.Tick(default);
        }

        outcome.ShouldBe(BattleOutcome.Win);
        sim.Forms.Shifts.ShouldBeEmpty();
    }

    [Fact]
    public void 문턱과_같은_시작_체력은_판을_세울_때_거절한다() =>
        Should.Throw<ArgumentException>(() => Sim(600));

    [Fact]
    public void 같은_틱에_게이지로_무너져도_전환이_이기고_탈진은_풀린다()
    {
        // 2연격 둘(10 · 30 · 10 · 30 · 경직 10 · 45 · 10 · 45)이면 넷째 칼에 게이지가 찬다(100). 시작 체력을 600 + 80 − 1 로 두면 넷째 칼이 문턱도 넘는다 —
        // 같은 틱에 탈진(cause=poise)과 전환이 선다. 전환이 이겨 탈진이 풀리고 게이지가 빈다.
        BattleSim sim = Sim(600 + 10 + 30 + 10 + 30 - 1, new ScriptPlan(3.0, _waitId));
        using var log = new LogCapture();
        StrikeUntilShift(sim, 10);
        int shiftAt = sim.Forms.Shifts.Single();
        log.Lines.ShouldContain(l => l.StartsWith("[boss][D] exhaust cause=poise ", StringComparison.Ordinal) && l.EndsWith($" tick={shiftAt}", StringComparison.Ordinal),
            "이 테스트가 같은 틱의 탈진을 안 본다");
        sim.Boss.Exhausted.ShouldBeFalse("전환이 탈진을 안 풀었다");
        sim.Poise.Value.ShouldBe(0);
    }

    [Fact]
    public void 공중에서_전환하면_높이만_따라_내리고_착지는_없다()
    {
        // 점프 공격이 뜬 동안(도약 24 ~ 60틱) 폭탄이 떨어져 문턱을 넘긴다. 보스가 던지기를 보고 끊으면 점프 공격이 안 서므로 반응 지연을 판보다 길게 둔다.
        // 첫 판으로 점프 공격이 서는 틱 J 를 재고, 둘째 판에서 J − 80 에 던져 J + 39(정점 근처)에 떨어지게 한다.
        BattleSim Leap()
        {
            string[] ids = ["점프 공격"];
            Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
            return new BattleSim(new BattleSetup
            {
                Arena = TestConfigs.Arena(),
                Fighter = Real(),
                HitShapes = TestConfigs.HitShapes(),
                Boss = TestConfigs.Boss(reaction: new BombReactionDef { DelaySeconds = 100, HesitateSeconds = 0.25, Move = "돌진" }),
                BossStartHealth = 630,
                PatternIds = ids,
                Patterns = patterns,
                Seed = 51,
                Picker = new ScriptPlanPicker(ids, patterns, [new ScriptPlan(3.0, "점프 공격")]),
                MaxTicks = TestConfigs.MaxTicks(),
            });
        }

        BattleSim probe = Leap();
        for (int i = 0; i < 600 && probe.Boss.CurrentPattern is null; i++)
        {
            probe.Tick(default);
        }

        int leap = probe.Ticks;
        BattleSim sim = Leap();
        TestConfigs.UntilTick(sim, leap - 81);
        sim.Tick(_bomb);
        sim.Fighter.Throwing.ShouldBeTrue();
        int land = leap - 80 + Release + Flight;
        TestConfigs.UntilTick(sim, land);

        sim.Forms.Shifting.ShouldBeTrue("폭탄이 문턱을 안 넘겼다");
        sim.Boss.Y.ShouldBeGreaterThan(0, "땅에서 전환했다 — 이 테스트가 공중을 안 본다");
        for (int i = 0; i < 60 && sim.Boss.Y > 0; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.Y.ShouldBe(0, "전환 안에 안 내렸다");
        TestConfigs.UntilTick(sim, land + Shift - 1);
        sim.Events.ShouldBeEmpty("끊긴 도약의 착지가 판정을 남겼다");
    }

    [Fact]
    public void GIF_form_605_에서_걸어_들어가_친_1타가_600_에_멈추며_전환을_세운다()
    {
        // GifRunner 의 form 대본 — 보스전의 명부 위에 3초 쉬고 3연격. 판이 선 뒤 115틱 걸어(805px · 보스 앞 155) 118틱에 J 를 누른다. 1타(10)가
        // 605 → 600 에 멈추고 그 틱에 전환이 선다 — 보스는 아직 쉬는 중이다(180틱 전).
        IReadOnlyList<string> roster = StageRoster.For(TestConfigs.Stages(), 1);
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = Real(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(),
            BossStartHealth = 605,
            PatternIds = roster,
            Patterns = patterns,
            Seed = 51,
            Picker = new ScriptPlanPicker(roster, patterns, [new ScriptPlan(3.0, "3연격")]),
            MaxTicks = TestConfigs.MaxTicks(),
        });
        using var log = new LogCapture();
        for (int t = 1; t <= 140 && !sim.Forms.Shifting; t++)
        {
            sim.Tick(t <= 115 ? _right : t == 118 ? _attack : default);
        }

        sim.Forms.Shifting.ShouldBeTrue("1타가 안 닿았다");
        sim.Boss.Health.ShouldBe(600);
        sim.Boss.CurrentPattern.ShouldBeNull("보스가 쉬기 전에 3연격을 열었다");
        log.Lines.ShouldContain($"[boss][I] form_shift from=1 to=2 hp=600 tick={sim.Ticks}");
        sim.Ticks.ShouldBe(122, "1타의 창이 닿는 틱 — GifRunner 의 form 대본이 이 틱을 둘러 잡는다");
    }

    /// <summary>고르기가 받은 요청을 적어 두고 대본에 넘긴다 — 판이 고르기에 무엇을 넘기는지 본다.</summary>
    private sealed class Recording(IPlanPicker inner) : IPlanPicker
    {
        public List<PlanRequest> Requests { get; } = new();

        public BossPlan Next(PlanRequest request)
        {
            Requests.Add(request);
            return inner.Next(request);
        }
    }

    [Fact]
    public void 전환_뒤의_첫_계획은_새_형태의_번호로_전환이_끝나는_틱에_고른다()
    {
        // 설계 2026-10-01 조각1 §2.2 · §2.4 (최종 리뷰가 밟았다) — 전환을 시작할 때 고르면 형태 2 에서 도는 첫 계획이 Form=1 로 골라진다. 조각 7 의
        // 고르기는 형태마다 다른 망이라, 형태 2 의 첫 수를 형태 1 의 망이 고르게 된다. 그래서 전환이 끝나는 틱에 새 번호로 고른다.
        string[] ids = [_waitId, "돌진"];
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        patterns[_waitId] = Waiting();
        var picker = new Recording(new ScriptPlanPicker(ids, patterns, [new ScriptPlan(0.2, _waitId)]));
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = Real(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(),
            BossStartHealth = 630,
            PatternIds = ids,
            Patterns = patterns,
            Seed = 51,
            Picker = picker,
            MaxTicks = TestConfigs.MaxTicks(),
        });
        int begun = ThrowIntoWait(sim);
        int land = begun + 1 + Release + Flight;
        TestConfigs.UntilTick(sim, land + Shift);

        PlanRequest last = picker.Requests[^1];
        (last.Form, last.Tick).ShouldBe((2, land + Shift), "전환 뒤의 첫 계획을 옛 형태로 · 전환을 시작할 때 골랐다");
        picker.Requests.ShouldNotContain(r => r.Tick == land, "전환을 시작한 틱에 계획을 골랐다");
    }
}
