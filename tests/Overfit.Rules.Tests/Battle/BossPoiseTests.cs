using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 경직 게이지가 판 위에서 도는가 (#71 · 설계 §4.3 · §4.5) — 칼이 채우고, 끝까지 차면 받아쳤을 때와 <b>같은</b> 탈진에 든다.
/// 게이지의 산수 자체는 <c>PoiseGaugeTests</c> 가 본다.
/// </summary>
public class BossPoiseTests
{
    private const string _waitId = "기다림";

    private static readonly InputFrame _attack = new(0, false, false, false, Attack: true);

    /// <summary>보스 반폭 + 파이터 반폭 — 이 거리에 서면 기준 파이터의 칼(±90)이 보스 몸(±85)에 닿는다.</summary>
    private static double Standoff() => TestConfigs.Boss().HalfWidth + TestConfigs.Fighter().HalfWidth;

    /// <summary>
    /// 기준 파이터 — 칼질마다의 경직도만 바꾼다. 게이지로 무너지는 순간을 한 대로 만들어야 하는 테스트가 <c>100</c> 을 준다.
    /// </summary>
    private static FighterConfig Fighter(int first, int second)
    {
        FighterConfig c = TestConfigs.Fighter();
        c.Combo[0] = Step(c.Combo[0], first);
        c.Combo[1] = Step(c.Combo[1], second);
        return c;
    }

    private static ComboStepDef Step(ComboStepDef s, int poise) => new()
    {
        Anim = s.Anim,
        Fps = s.Fps,
        Frames = s.Frames,
        StartFrame = s.StartFrame,
        BladeFrame = s.BladeFrame,
        Windup = s.Windup,
        Active = s.Active,
        Recover = s.Recover,
        Stiff = s.Stiff,
        Damage = s.Damage,
        Hitbox = s.Hitbox,
        Poise = poise,
    };

    /// <summary>
    /// 선딜이 긴 패턴 하나 — <paramref name="at"/> 초에 판정 하나가 선다(패리를 받는다). 보스가 그동안 "하던 것" 이 있어야
    /// 무너질 때 끊기는지를 본다. 판정은 보스 중심에서 <paramref name="reach"/> 까지 · 창 <paramref name="window"/> 초다(0 이면 한 틱).
    /// </summary>
    private static PatternDef Waiting(double at, double reach = 2000, double window = 0) => new()
    {
        Tags = new PatternTags
        {
            DashWindow = 0,
            DashDirection = "out",
            Jumpable = false,
            AntiAir = false,
            Parryable = true,
            ParryWindow = 0.12,
            PunishGreed = false,
            Reach = "far",
            MultiHit = 1,
            Tracking = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0, Kind = "windup" },
            new() { T = at, Kind = "active", Band = new double[] { 0, reach, 0, 300 }, Damage = 5, ActiveSeconds = window },
            new() { T = at + 0.5 + window, Kind = "end" },
        },
    };

    /// <summary>
    /// 보스는 안 움직이고(속도 0) 패턴 <see cref="Waiting"/> 하나만 돈다. 파이터가 걸어가 칼이 닿는 자리에 선 틱에 돌려준다.
    /// <paramref name="gap"/> 이 크면 패턴이 안 선다 — 게이지의 산수만 볼 때다. 판을 미는 기다림은 이 파일에서 전부 틱 수로 묶는다:
    /// 판은 결과가 난 뒤에도 틱을 받아, 규칙이 깨진 날 묶지 않은 기다림은 실패하지 않고 게이트를 멈춰 세운다.
    /// </summary>
    private static BattleSim Beside(
        FighterConfig fighter, double gap = 1000, double at = 6.0, int? bossHealth = null, double reach = 2000, double window = 0)
    {
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = fighter,
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: bossHealth ?? 999_999, moveSpeed: 0, patternGap: gap),
            PatternIds = new[] { _waitId },
            Patterns = new Dictionary<string, PatternDef> { [_waitId] = Waiting(at, reach, window) },
            Seed = 1,
            MaxTicks = 60 * 60,
        });

        for (int i = 0; i < 600 && sim.Boss.X - sim.Fighter.X > Standoff(); i++)
        {
            sim.Tick(new InputFrame(1, false, false, false, false));
        }

        (sim.Boss.X - sim.Fighter.X).ShouldBeLessThanOrEqualTo(Standoff(), "파이터가 칼이 닿는 자리까지 못 걸어갔다");
        return sim;
    }

    /// <summary>J 를 누른다. <paramref name="chain"/> 이면 다음 틱(1타 도중)에 한 번 더 눌러 2타를 눌러 둔다.</summary>
    private static void Press(BattleSim sim, bool chain = false)
    {
        sim.Tick(_attack);
        if (chain)
        {
            sim.Tick(_attack);
        }
    }

    /// <summary>칼이 보스에 닿을 때까지(체력이 준 틱까지) 민다.</summary>
    private static void UntilHit(BattleSim sim)
    {
        int before = sim.Boss.Health;
        for (int i = 0; i < 120 && sim.Boss.Health == before; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.Health.ShouldBeLessThan(before, "칼이 안 닿았다 — 이 테스트가 게이지를 안 본다");
    }

    /// <summary>1타를 누르고 닿을 때까지 민다.</summary>
    private static void Strike(BattleSim sim)
    {
        Press(sim);
        UntilHit(sim);
    }

    /// <summary>칼질이 끝나 파이터가 설 때까지 민다.</summary>
    private static void UntilIdle(BattleSim sim)
    {
        for (int i = 0; i < 120 && sim.Fighter.Action != FighterAction.Idle; i++)
        {
            sim.Tick(default);
        }
    }

    [Fact]
    public void 연격을_두_번_연달아_넣으면_두_번째_2타에_무너진다()
    {
        // 유저: "유저의 두번째 2타공격은 더 큰 경직도를 쌓도록 해주세요." 1타 10 · 2타 45 · 끝 100 (설계 §4.5) — 한 번은 55 로
        // 안 무너지고, 연달아 두 번이면 10 → 55 → 65 → 110 에서 두 번째 2타에 무너진다.
        BattleSim sim = Beside(TestConfigs.Fighter());
        using var log = new LogCapture();

        Press(sim, chain: true);
        UntilHit(sim);
        sim.Poise.Value.ShouldBe(10);
        UntilHit(sim);
        sim.Poise.Value.ShouldBe(55);
        sim.Boss.Exhausted.ShouldBeFalse("2연격 한 번(55)에 무너졌다");
        UntilIdle(sim);

        Press(sim, chain: true);
        UntilHit(sim);
        sim.Poise.Value.ShouldBe(65);
        sim.Boss.Exhausted.ShouldBeFalse("세 번째 칼(65)에 무너졌다");
        UntilHit(sim);

        sim.Boss.Exhausted.ShouldBeTrue("두 번째 2타가 게이지를 끝까지 채웠는데 안 무너졌다");
        sim.Poise.Value.ShouldBe(0, "무너졌는데 게이지를 안 비웠다");
        log.Lines.ShouldContain($"[boss][D] exhaust cause=poise id=- tick={sim.Ticks}");
        log.Lines.ShouldContain(l => l.StartsWith("[strike][D] hit ") && l.Contains(" poise=35 "),
            "넘친 몫까지 찼다고 적었다 — 65 에서 끝까지는 35 다");
    }

    [Fact]
    public void 게이지로_무너지면_하던_패턴이_끊기고_받아쳤을_때와_같은_90틱_탈진이다()
    {
        // 설계 §4.3 — 원인은 둘이고 루틴은 하나다. 보스가 무엇을 하고 있었든(여기서는 선딜) 그 자리에서 끊기고, 탈진은 1.5초 =
        // 90틱이다. 끊긴 패턴의 판정은 탈진이 풀린 뒤에도 안 온다.
        BattleSim sim = Beside(Fighter(100, 100), gap: 0.2);
        sim.Boss.CurrentPattern.ShouldBe(_waitId, "파이터가 닿기 전에 패턴이 안 섰다 — 끊기는 것을 못 본다");
        using var log = new LogCapture();

        Strike(sim);

        sim.Boss.Exhausted.ShouldBeTrue();
        sim.Boss.CurrentPattern.ShouldBeNull("게이지로 무너졌는데 패턴이 안 끊겼다");
        log.Lines.ShouldContain($"[boss][D] exhaust cause=poise id={_waitId} tick={sim.Ticks}");

        int ticks = 0;
        while (sim.Boss.Exhausted && ticks < 600)
        {
            sim.Tick(default);
            ticks++;
        }

        ticks.ShouldBe(BattleSim.TicksFor(TestConfigs.Boss().ExhaustSeconds));
        sim.Events.ShouldBeEmpty("끊긴 패턴의 판정이 관측을 남겼다");
    }

    [Fact]
    public void 탈진_동안_맞으면_피해만_들어가고_게이지는_안_찬다()
    {
        // 설계 §4.3 · §4.5 — 게이지는 무너질 때 비고 탈진 동안에는 안 찬다: 풀리자마자 다시 무너지는 연속 탈진이 없다. 로그의
        // poise= 는 실제로 찬 양이라 0 이다.
        BattleSim sim = Beside(Fighter(100, 100));
        Strike(sim);
        sim.Boss.Exhausted.ShouldBeTrue();
        UntilIdle(sim);
        using var log = new LogCapture();

        int before = sim.Boss.Health;
        Strike(sim);

        sim.Boss.Health.ShouldBe(before - TestConfigs.Fighter().Combo[0].Damage);
        sim.Poise.Value.ShouldBe(0, "탈진한 보스의 게이지가 찼다");
        log.Lines.ShouldContain(l => l.StartsWith("[strike][D] hit ") && l.Contains(" poise=0 "));
        log.Lines.ShouldNotContain(l => l.StartsWith("[boss][E]"), "탈진 중에 다시 무너지려 했다");
    }

    [Fact]
    public void 결정타는_게이지를_안_채운다()
    {
        // 설계 §4.5 — 이긴 판에 탈진은 뜻이 없다. 채우면 판이 끝나는 틱에 탈진 로그와 히트스톱이 같이 걸린다.
        FighterConfig fighter = Fighter(100, 100);
        BattleSim sim = Beside(fighter, bossHealth: fighter.Combo[0].Damage);
        using var log = new LogCapture();

        sim.Tick(_attack);
        BattleOutcome? outcome = null;
        for (int i = 0; i < 60 && outcome is null; i++)
        {
            outcome = sim.Tick(default);
        }

        outcome.ShouldBe(BattleOutcome.Win);
        sim.Poise.Value.ShouldBe(0);
        sim.Boss.Exhausted.ShouldBeFalse();
        log.Lines.ShouldNotContain(l => l.Contains("exhaust cause="));
    }

    [Fact]
    public void 판_위에서도_맞은_뒤_72틱은_그대로이고_그_뒤로_한_틱에_한_몫씩_준다()
    {
        // 게이지는 BattleSim 이 틱마다 **한 번** 민다 — 두 번 밀면 "서서히" 가 두 배로 빠르고, 칼보다 먼저 밀면 맞은 틱의 채움이 유예
        // 한 틱을 잃는다. 판정은 산수 테스트(PoiseGaugeTests)와 같은 값이어야 한다.
        BattleSim sim = Beside(TestConfigs.Fighter());
        Strike(sim);
        sim.Poise.Value.ShouldBe(10);

        for (int t = 1; t <= 72; t++)
        {
            sim.Tick(default);
            sim.Poise.Value.ShouldBe(10, $"맞은 뒤 {t}틱째에 벌써 빠졌다");
        }

        sim.Tick(default);
        sim.Poise.Value.ShouldBe(10 - (10 * BattleSim.Dt), 1e-9);
    }

    [Fact]
    public void 받아쳐_무너져도_게이지를_비운다()
    {
        // 탈진은 하나다(설계 §4.3) — 원인이 패리여도 게이지를 비운다. 안 비우면 반쯤 찬 게이지가 탈진이 풀리자마자 한 대에 무너진다.
        BattleSim sim = Beside(TestConfigs.Fighter(), gap: 0.2, at: 4.0);
        Strike(sim);
        sim.Poise.Value.ShouldBe(10);
        UntilIdle(sim);

        double before = 0;
        for (int i = 0; i < 600 && sim.Events.Count == 0; i++)
        {
            before = sim.Poise.Value;
            bool press = sim.NextActiveIn is <= 4 * BattleSim.Dt && sim.Fighter.Action == FighterAction.Idle;
            sim.Tick(new InputFrame(0, false, false, Parry: press, false));
        }

        sim.Events.Single().Verdict.ShouldBe(HitVerdict.Parried);
        before.ShouldBeGreaterThan(0, "받아치기 전에 게이지가 이미 비었다 — 이 테스트가 비우는 것을 못 본다");
        sim.Boss.Exhausted.ShouldBeTrue();
        sim.Poise.Value.ShouldBe(0, "받아쳐 무너졌는데 게이지가 남았다");
    }

    [Fact]
    public void 게이지를_깬_틱에_보스의_칼이_먼저_닿았으면_파이터는_맞고_보스는_무너진다()
    {
        // Review Focus 2 — 칼을 맞바꾸는 사람. 설계 §3.5 5 — 같은 틱의 순서는 보스 판정 → 파이터의 칼 → 끊기다: 파이터가 보스를
        // 무너뜨린 틱에 보스의 칼이 닿았으면 파이터는 맞는다. 무너짐이 그 틱의 보스 칼을 지우면 "먼저 친 쪽이 이긴다" 가 칼끝 한 틱에
        // 걸린다. 누르는 틱을 한 틱씩 당기며 두 칼이 같은 틱에 닿는 판을 찾는다 — 칼질의 선딜이 데이터라 틱을 박지 않는다.
        FighterConfig fighter = Fighter(100, 100);
        bool found = false;
        for (int lead = 1; lead <= 12 && !found; lead++)
        {
            BattleSim sim = Beside(fighter, gap: 0.2, at: 4.0);
            for (int i = 0; i < 600 && sim.NextActiveIn is { } left && left > lead * BattleSim.Dt; i++)
            {
                sim.Tick(default);
            }

            int health = sim.Fighter.Health;
            sim.Tick(_attack);
            for (int i = 0; i < 30 && !sim.Boss.Exhausted && sim.Events.Count == 0; i++)
            {
                sim.Tick(default);
            }

            if (!sim.Boss.Exhausted || sim.Events.Count == 0)
            {
                continue;
            }

            found = true;
            sim.Events.Single().Verdict.ShouldBe(HitVerdict.Hit, "같은 틱에 무너진 보스의 칼이 파이터에게 안 닿았다");
            sim.Fighter.Health.ShouldBe(health - 5);
        }

        found.ShouldBeTrue("두 칼이 같은 틱에 닿는 판을 못 찾았다 — 이 테스트가 같은 틱의 순서를 안 본다");
    }

    [Fact]
    public void 창이_열린_채_게이지로_무너지면_그_창은_관측_없이_로그만_남긴다()
    {
        // Review Focus 3 — 설계 §3.5 5 · §4.3: 보스가 무엇을 하고 있었든(창이 열려 있든) 게이지로 무너지면 열린 창은 결과가 없다.
        // 지어내면 창이 열린 틱의 빗나간 이유(여기서는 거리)가 관측으로 나가 시도 기록에 들어간다. 판정은 짧게(50) 두어 파이터(115)에
        // 안 닿고, 창은 길게(0.5초) 두어 그 안에 칼을 넣는다.
        BattleSim sim = Beside(Fighter(100, 100), gap: 0.2, at: 4.0, reach: 50, window: 0.5);
        for (int i = 0; i < 600 && sim.BossTestedRects.Count == 0; i++)
        {
            sim.Tick(default);
        }

        sim.BossTestedRects.ShouldNotBeEmpty("보스의 판정 창이 안 열렸다 — 이 테스트가 열린 창을 못 본다");
        using var log = new LogCapture();
        Strike(sim);

        sim.Boss.Exhausted.ShouldBeTrue();
        log.Lines.ShouldContain($"[boss][D] cut_swing id={_waitId} tick={sim.Ticks} reason=exhaust");
        for (int i = 0; i < 60; i++)
        {
            sim.Tick(default);
        }

        sim.Events.ShouldBeEmpty("게이지로 끊긴 창이 관측을 남겼다");
    }

    [Fact]
    public void 쉬는_보스가_게이지로_무너지면_풀린_뒤_간격을_처음부터_센다()
    {
        // Review Focus 4 — 설계 §4.3: 쉬는 중에 무너져도 간격을 처음부터 센다. 남은 간격을 이어 세면 풀리자마자 패턴이 서 반격 창이
        // 사라진다. 판이 처음 설 때와 같은 규약이다 — 탈진이 풀리는 틱이 간격의 첫 틱이다.
        BattleSim sim = Beside(Fighter(100, 100), gap: 3.0);
        sim.Boss.CurrentPattern.ShouldBeNull("걸어가는 사이에 패턴이 섰다 — 쉬는 보스를 못 본다");
        Strike(sim);
        sim.Boss.Exhausted.ShouldBeTrue();

        for (int i = 0; i < 600 && sim.Boss.Exhausted; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.Exhausted.ShouldBeFalse("탈진이 안 풀렸다");
        int free = sim.Ticks;
        while (sim.Boss.CurrentPattern is null && sim.Ticks < free + 600)
        {
            sim.Tick(default);
        }

        (sim.Ticks - free + 1).ShouldBe(BattleSim.TicksFor(3.0), "풀린 뒤 간격을 처음부터 안 셌다");
    }
}
