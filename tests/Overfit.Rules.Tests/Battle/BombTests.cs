using System.Collections.Generic;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 폭탄이 판 위에서 (설계 2026-09-30 조각2 §1.2 · §1.3) — 놓은 뒤 0.5초 날아 보스를 따라가 떨어지고, 같은 틱의 순서(파이터 → 보스)가 놓는
/// 틱에 닿은 판정을 늦게 만든다. 파이터 몸 하나의 규칙은 <c>FighterThrowTests</c> 가 본다.
/// </summary>
public class BombTests
{
    private const string _waitId = "기다림";

    private static readonly InputFrame _bomb = new(0, false, false, false, false, Bomb: true);

    private static BombDef Bomb => TestConfigs.Fighter().Bomb;

    /// <summary>놓는 틱 — 누른 틱부터 센 선딜의 마지막 틱.</summary>
    private static int ReleaseAfter => BattleSim.TicksFor(Bomb.ThrowSeconds) - 1;

    /// <summary>나는 틱 — 놓은 틱 뒤 이만큼 지난 틱의 끝에 떨어진다.</summary>
    private static int FlightTicks => BattleSim.TicksFor(Bomb.FlightSeconds);

    /// <summary>
    /// <paramref name="at"/> 초에 아레나 전체(띠 0 ~ 2000 · 높이 0 ~ 300)를 한 번 치는 패턴 — 파이터가 어디 서 있든 그 틱에 맞는다.
    /// </summary>
    private static PatternDef Waiting(double at) => new()
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
            new() { T = at, Kind = "active", Band = new double[] { 0, 2000, 0, 300 }, Damage = 5, ActiveSeconds = 0.125 },
            new() { T = at + 0.5, Kind = "end" },
        },
    };

    /// <summary>
    /// 보스가 판 내내 쉬는 판 — 동작이 안 선다(쉬기 1000초). 폭탄이 날고 떨어지는 것만 본다. 보스는 파이터 오른쪽 960 에 선다.
    /// </summary>
    private static BattleSim Resting(int bossHealth = 999_999, int maxTicks = 60 * 60, FighterConfig? fighter = null, double? exhaustSeconds = null) =>
        new(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = fighter ?? TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: bossHealth, rest: 1000, exhaustSeconds: exhaustSeconds),
            PatternIds = new[] { _waitId },
            Patterns = new Dictionary<string, PatternDef> { [_waitId] = Waiting(1.0) },
            Seed = 1,
            MaxTicks = maxTicks,
        });

    /// <summary>쉬기 0.2초 뒤 <see cref="Waiting"/> 가 3초에 아레나 전체를 치는 판 — 파이터가 맞는 틱이 늘 같다(파이터가 서 있으면).</summary>
    private static BattleSim Hitting() =>
        new(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, rest: 0.2),
            PatternIds = new[] { _waitId },
            Patterns = new Dictionary<string, PatternDef> { [_waitId] = Waiting(3.0) },
            Seed = 1,
            MaxTicks = 60 * 60,
        });

    [Fact]
    public void 놓고_30틱_뒤에_보스에게_떨어진다()
    {
        BattleSim sim = Resting();
        int hp = sim.Boss.Health;

        sim.Tick(_bomb);
        int release = sim.Ticks + ReleaseAfter;
        TestConfigs.UntilTick(sim, release);

        sim.Fighter.ThrowReleased.ShouldBeTrue();
        sim.BombsInFlight.Count.ShouldBe(1, "놓은 틱에 폭탄이 난다");

        TestConfigs.UntilTick(sim, release + FlightTicks - 1);
        sim.Boss.Health.ShouldBe(hp, "나는 동안에는 안 든다");
        sim.BombsInFlight.Count.ShouldBe(1);

        sim.Tick(default);

        sim.Boss.Health.ShouldBe(hp - Bomb.Damage, $"놓은 틱 + {FlightTicks} 에 떨어져야 한다");
        sim.BombsInFlight.ShouldBeEmpty();
        sim.BombsLanded.ShouldBe(1, "뷰가 터지는 불꽃을 세우는 수다");
    }

    [Fact]
    public void 나는_동안_달리는_보스에게도_떨어진다()
    {
        // 보스를 따라간다 (설계 2026-09-30 조각2 §1.3 · §0) — 던지기 시작할 때의 자리에 떨어지면 달려 나오기만 해도 빗나간다. 보스가 쉬기 1.5초
        // 뒤 돌진으로 나는 폭탄 밑을 가로지른다(90틱에 놓고 91틱부터 달린다).
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, rest: 1.5),
            PatternIds = new[] { "돌진" },
            Patterns = new Dictionary<string, PatternDef> { ["돌진"] = patterns["돌진"] },
            Seed = 1,
            MaxTicks = 60 * 60,
        });
        int hp = sim.Boss.Health;

        sim.Tick(_bomb);
        int release = sim.Ticks + ReleaseAfter;
        TestConfigs.UntilTick(sim, release);
        double from = sim.Boss.X;
        TestConfigs.UntilTick(sim, release + FlightTicks);

        sim.Boss.X.ShouldBeLessThan(from - 300, "보스가 나는 폭탄 밑을 안 달렸다 — 이 테스트가 아무것도 안 본다");
        sim.Boss.Health.ShouldBe(hp - Bomb.Damage);
    }

    [Fact]
    public void 폭탄은_경직_게이지를_안_채운다()
    {
        // 폭탄은 탈진의 도구가 아니다 (설계 2026-09-30 조각2 §1.3).
        BattleSim sim = Resting();
        int hp = sim.Boss.Health;

        sim.Tick(_bomb);
        TestConfigs.UntilTick(sim, 1 + ReleaseAfter + FlightTicks);

        sim.Boss.Health.ShouldBe(hp - Bomb.Damage, "폭탄이 안 떨어졌다");
        sim.Poise.Value.ShouldBe(0);
    }

    [Fact]
    public void 탈진한_보스에게도_든다()
    {
        // 1타 한 대로 게이지가 차는 파이터가 걸어가 무너뜨리고 곧장 던진다 — 탈진(3초)이 폭탄이 떨어질 때까지 간다.
        BattleSim sim = Resting(fighter: TestConfigs.Breaker(), exhaustSeconds: 3.0);
        double reach = sim.Boss.X - sim.Boss.HalfWidth - TestConfigs.TestSword().Bounds.X1 + 1;
        for (int i = 0; i < 600 && sim.Fighter.X < reach; i++)
        {
            sim.Tick(new InputFrame(1, false, false, false, false));
        }

        sim.Tick(new InputFrame(0, false, false, false, Attack: true));
        for (int i = 0; i < 60 && !sim.Boss.Exhausted; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.Exhausted.ShouldBeTrue("칼이 보스를 못 무너뜨렸다");
        for (int i = 0; i < 120 && sim.Fighter.Action != FighterAction.Idle; i++)
        {
            sim.Tick(default);
        }

        int hp = sim.Boss.Health;
        sim.Tick(_bomb);
        TestConfigs.UntilTick(sim, sim.Ticks + ReleaseAfter + FlightTicks);

        sim.Boss.Exhausted.ShouldBeTrue("폭탄이 떨어지기 전에 탈진이 풀렸다 — 이 테스트가 아무것도 안 본다");
        sim.Boss.Health.ShouldBe(hp - Bomb.Damage);
    }

    [Fact]
    public void 폭탄이_보스를_죽이면_이긴다()
    {
        BattleSim sim = Resting(bossHealth: Bomb.Damage);

        sim.Tick(_bomb);
        BattleOutcome? outcome = null;
        for (int i = 0; i < 600 && outcome is null; i++)
        {
            outcome = sim.Tick(default);
        }

        outcome.ShouldBe(BattleOutcome.Win);
        sim.Ticks.ShouldBe(1 + ReleaseAfter + FlightTicks);
    }

    [Fact]
    public void 판이_끝나면_나는_폭탄은_사라진다()
    {
        // 시간 초과가 폭탄이 나는 사이에 온다 — 끝난 판에 틱을 더 넣어도 안 떨어진다.
        int release = 1 + ReleaseAfter;
        BattleSim sim = Resting(maxTicks: release + 10);
        int hp = sim.Boss.Health;

        sim.Tick(_bomb);
        TestConfigs.UntilTick(sim, release + 10);

        sim.Result.ShouldBe(BattleOutcome.Lose);
        sim.BombsInFlight.ShouldBeEmpty();
        for (int i = 0; i < FlightTicks; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.Health.ShouldBe(hp);
    }

    [Fact]
    public void 던지는_틱에_보스_쪽으로_돌아선다()
    {
        // 파이터는 보스를 모른다 — 판이 돌려세운다(설계 2026-09-30 조각2 §1.1). 왼쪽으로 한 걸음 걸어 등을 보인 뒤 던진다.
        BattleSim sim = Resting();
        sim.Tick(new InputFrame(-1, false, false, false, false));
        sim.Fighter.Facing.ShouldBe(-1);

        sim.Tick(_bomb);

        sim.Fighter.Facing.ShouldBe(1, "보스(오른쪽)를 등지고 던졌다");
    }

    [Fact]
    public void 놓기_한_틱_전에_닿은_판정은_끊고_놓는_틱의_판정은_늦다()
    {
        // 같은 틱은 파이터 → 보스 순서다 (설계 2026-09-30 조각2 §1.2). 먼저 던지지 않은 판에서 판정이 닿는 틱을 재고, 그 틱이 누른 틱 + 88
        // (놓기 한 틱 전)인 판과 + 89(놓는 틱)인 판을 견준다. 파이터는 서 있어 두 판의 판정 틱이 같다.
        BattleSim probe = Hitting();
        int health = probe.Fighter.Health;
        int hit = 0;
        for (int i = 0; i < 600 && hit == 0; i++)
        {
            probe.Tick(default);
            hit = probe.Fighter.Health < health ? probe.Ticks : 0;
        }

        hit.ShouldBeGreaterThan(ReleaseAfter + 1, "판정이 너무 일러 던질 틱이 없다");

        BattleSim cut = Hitting();
        TestConfigs.UntilTick(cut, hit - ReleaseAfter);
        cut.Tick(_bomb);
        TestConfigs.UntilTick(cut, hit);

        cut.Fighter.ThrowLost.ShouldBeTrue($"누른 틱 + {ReleaseAfter - 1} 에 닿은 판정이 던지기를 못 끊었다");
        cut.BombsInFlight.ShouldBeEmpty();

        BattleSim late = Hitting();
        TestConfigs.UntilTick(late, hit - ReleaseAfter - 1);
        late.Tick(_bomb);
        TestConfigs.UntilTick(late, hit);

        late.Fighter.ThrowReleased.ShouldBeTrue();
        late.Fighter.ThrowLost.ShouldBeFalse($"누른 틱 + {ReleaseAfter} (놓는 틱)에 닿은 판정이 던지기를 끊었다");
        late.BombsInFlight.Count.ShouldBe(1);
    }
}
