using System;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 파이터의 탈진 (#71 · 설계 §5.5) — 유저: "아 유저도 스테미나 다쓰면 탈진해야합니다." 마지막 한 번은 할 수 있고, 0 에 닿으면
/// 탈진한다. 탈진 동안(1.1초 = 66틱) 행동 · 이동 · 점프 · 가드가 전부 막힌다. 가드 붕괴도 탈진이다.
///
/// <para>
/// 판이나 파이터를 미는 기다림은 전부 틱 수로 묶고 뒤에서 그 조건을 단언한다 — 판은 결과가 난 뒤에도 틱을 받아, 탈진이 안 들거나
/// 안 풀리는 날 묶지 않은 기다림은 실패하지 않고 게이트(<c>tools/build.sh check</c>)를 멈춰 세운다.
/// </para>
/// </summary>
public class FighterExhaustTests
{
    private const double _dt = BattleSim.Dt;

    private static readonly InputFrame _dash = new(0, false, true, false, false);
    private static readonly InputFrame _parry = new(0, false, false, true, false);
    private static readonly InputFrame _attack = new(0, false, false, false, true);
    private static readonly InputFrame _guard = new(0, false, false, false, false, GuardHeld: true);

    private static Fighter Spawn() => new(TestConfigs.Fighter(), TestConfigs.Arena(), 960);

    private static InputFrame Press(FighterAction action) => action switch
    {
        FighterAction.Dash => _dash,
        FighterAction.Parry => _parry,
        _ => _attack,
    };

    /// <summary>누른 행동이 도는 틱 수 — 누른 틱부터 행동이 끝나 Idle 이 되는 틱 앞까지.</summary>
    private static int Run(Fighter f, FighterAction action)
    {
        f.Tick(Press(action), _dt);
        f.Action.ShouldBe(action, "행동이 안 섰다");
        int ticks = 1;
        while (f.Action == action && ticks < 120)
        {
            f.Tick(default, _dt);
            ticks++;
        }

        return ticks;
    }

    [Theory]
    [InlineData(FighterAction.Dash)]
    [InlineData(FighterAction.Parry)]
    [InlineData(FighterAction.Attack)]
    public void 마지막_한_번은_값보다_모자라도_끝까지_하고_끝나는_틱에_탈진한다(FighterAction action)
    {
        // 설계 §5.5 — 스태미나가 0 보다 많으면 값보다 모자라도 시작하고 값은 0 에서 멈춘다(소울라이크). 그 행동은 **끝까지** 한다 —
        // 마지막 칼은 들어간다 — 그리고 끝나는 틱에 탈진한다. 전에는 모자라면 안 나가 행동으로는 0 에 안 닿았다(100 − 14 × 7 = 2).
        int full = Run(Spawn(), action);

        Fighter f = Spawn();
        f.Spend(f.Stamina - 5);
        f.Tick(Press(action), _dt);
        f.Action.ShouldBe(action, "5 남은 스태미나로 마지막 한 번이 안 나갔다");
        f.Stamina.ShouldBe(0, "값이 0 에서 안 멈췄다");

        bool bladeOut = f.AttackActive;
        int ticks = 1;
        while (f.Action == action && ticks < 120)
        {
            f.Exhausted.ShouldBeFalse($"{ticks}틱: 행동이 끝나기 전에 탈진했다 — 마지막 한 번이 잘렸다");
            f.Tick(default, _dt);
            bladeOut |= f.AttackActive;
            ticks++;
        }

        ticks.ShouldBe(full, "마지막 한 번이 제 길이만큼 안 돌았다");
        f.Exhausted.ShouldBeTrue("값으로 0 이 된 행동이 끝났는데 탈진하지 않았다");
        if (action == FighterAction.Attack)
        {
            bladeOut.ShouldBeTrue("마지막 칼이 안 섰다");
        }
    }

    [Fact]
    public void 스태미나가_0_이면_아무_행동도_못_시작한다()
    {
        // 0 에서는 마지막 한 번도 없다 — 값이 있는 행동(대시 · 패리 · 칼질)은 스태미나가 0 보다 많아야 선다.
        foreach (FighterAction action in new[] { FighterAction.Dash, FighterAction.Parry, FighterAction.Attack })
        {
            Fighter f = Spawn();
            f.Spend(f.Stamina);

            f.Tick(Press(action), _dt);
            f.Action.ShouldBe(FighterAction.Idle, $"스태미나 0 에서 {action} 이(가) 섰다");
        }
    }

    [Fact]
    public void 연격_2타는_모자라도_남아_있으면_잇고_0_에서_멈춘다()
    {
        // 설계 §5.1 · §5.5 — 2타는 이을 때 값을 낸다. 스태미나가 남아 있으면 값보다 모자라도 잇는다(마지막 한 번). 2번 PR 의 결정 4
        // ("모자라면 잇지 않고 선다")는 이것으로 바뀌었다 — 0 에서만 잇지 않는다(FighterActionTests.연격_2타는_스태미나가_0_이면_잇지_않고_선다).
        FighterConfig c = TestConfigs.Fighter();
        Fighter f = Spawn();
        f.Spend(c.MaxStamina - c.AttackCost - 1);   // 1타 값 + 1 만 남긴다
        f.Tick(_attack, _dt);
        f.Tick(_attack, _dt);
        f.Stamina.ShouldBe(1, 1e-9);

        for (int i = 0; i < 120 && f.ComboStep == 0 && f.Action == FighterAction.Attack; i++)
        {
            f.Tick(default, _dt);
        }

        f.ComboStep.ShouldBe(1, "1 남은 스태미나로 2타를 안 이었다");
        f.Stamina.ShouldBe(0);

        for (int i = 0; i < 120 && f.Action == FighterAction.Attack; i++)
        {
            f.Tick(default, _dt);
        }

        f.Exhausted.ShouldBeTrue("마지막 2타가 끝났는데 탈진하지 않았다");
    }

    [Fact]
    public void 받아친_패리의_되받아치기도_마지막_한_번은_나간다()
    {
        // 되받아치기(받아친 패리의 커밋 안의 J)는 Idle 에서 누른 J 와 같은 규칙이다 — 마지막 한 번도 같다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        f.ParryPrecise();
        f.Spend(f.Stamina - 5);

        f.Tick(_attack, _dt);

        f.Action.ShouldBe(FighterAction.Attack, "5 남은 스태미나로 되받아치기가 안 나갔다");
        f.Stamina.ShouldBe(0);
    }

    [Fact]
    public void 가드로_막다가_딱_0_이_되면_칩을_받고_곧장_탈진한다()
    {
        // 설계 §5.2 · §5.5 — 값이 남은 스태미나와 딱 같으면 막은 것이다(칩을 받는다). 그리고 다 썼으니 **곧장** 탈진한다 —
        // 행동이 아니라 끝날 틱이 없다. 가드도 내린다: 탈진 동안은 가드가 막힌다. 막음과 붕괴를 가르는 것은 판정기다(HitResolver) —
        // 그것을 거쳐야 "딱 같으면 막았다" 를 본다. 피해 20 의 값은 20 × 1.8 = 36 으로 2진수에서도 딱 떨어진다.
        Fighter f = Spawn();
        f.Tick(_guard, _dt);
        f.Spend(f.Stamina - f.GuardStaminaCost(20));
        f.Stamina.ShouldBe(f.GuardStaminaCost(20), "스태미나를 값과 딱 같게 못 맞췄다 — 이 테스트가 경계를 안 본다");

        var box = new HitBox(HitShape.Band(0, 260, 0, 200), 20);
        HitResolver.Resolve(f, new Placement(f.X - 100, 0, 1), box, TestConfigs.Patterns()["3연격"].Tags)
            .ShouldBe(HitVerdict.Guarded, "값이 남은 스태미나와 딱 같은데 판정기가 붕괴로 봤다");
        f.GuardChip(fullDamage: 20);

        f.Health.ShouldBe(100 - 5, "딱 0 이면 막은 것이다 — 칩(20 의 0.25)만 받는다");
        f.Stamina.ShouldBe(0, 1e-9);
        f.Exhausted.ShouldBeTrue("가드로 스태미나를 다 썼는데 탈진하지 않았다");
        f.Guarding.ShouldBeFalse("탈진했는데 가드가 서 있다");
    }

    [Fact]
    public void 탈진은_66틱_동안_행동_이동_점프_가드를_전부_막고_그동안_스태미나가_찬다()
    {
        // 설계 §5.5 — exhaust_seconds 1.1초를 틱으로 센다(반올림은 BattleSim.TicksFor 한 곳): 66틱 동안 **정확히** 아무것도 못 한다.
        // 1.1 은 3연격의 2타 → 3타 간격(93틱 → 159틱)이다(PatternDataTests). 스태미나는 Idle 이라 지금처럼 찬다 — 탈진한 틱부터다.
        // 누름 여섯을 하나씩 **66틱 내내** 넣고, 67번째 틱에 같은 누름이 선다. 틱마다 돌려 넣었을 때는 66번째 틱에 대시만 눌러, 그 틱에
        // 걷고 뛰던 것을 못 봤다(Advance 가 Move · Fall 보다 먼저 굳음을 센다 — Fighter.Tick 의 주석).
        FighterConfig c = TestConfigs.Fighter();
        BattleSim.TicksFor(c.ExhaustSeconds).ShouldBe(66);

        var tries = new (string Name, InputFrame Press, Func<Fighter, double, bool> Took)[]
        {
            ("대시", _dash, (f, _) => f.Action == FighterAction.Dash),
            ("패리", _parry, (f, _) => f.Action == FighterAction.Parry),
            ("칼질", _attack, (f, _) => f.Action == FighterAction.Attack),
            ("점프", new(0, Jump: true, false, false, false), (f, _) => f.Y > 0),
            ("걷기", new(1, false, false, false, false), (f, x) => f.X > x),
            ("가드", _guard, (f, _) => f.Guarding),
        };
        foreach ((string name, InputFrame press, Func<Fighter, double, bool> took) in tries)
        {
            Fighter f = Spawn();
            f.Spend(f.Stamina - 5);
            Run(f, FighterAction.Dash);
            f.Exhausted.ShouldBeTrue();
            double x = f.X;

            for (int t = 1; t <= 66; t++)
            {
                f.Tick(press, _dt);
                f.Action.ShouldBe(FighterAction.Idle, $"{name}: 탈진 {t}틱째에 행동이 섰다");
                f.X.ShouldBe(x, $"{name}: 탈진 {t}틱째에 움직였다");
                f.Y.ShouldBe(0, $"{name}: 탈진 {t}틱째에 뛰었다");
            }

            f.Exhausted.ShouldBeFalse($"{name}: 66틱이 지났는데 안 풀렸다");
            f.Stamina.ShouldBe(67 * c.StaminaRegen * _dt, 1e-9, $"{name}: 탈진한 틱부터 67틱 × 초당 40 이 안 찼다");
            f.Tick(press, _dt);
            took(f, x).ShouldBeTrue($"{name}: 풀린 다음 틱에 안 섰다");
        }
    }

    [Fact]
    public void 파이터가_탈진하면_무엇이_바닥냈는지를_로그로_남긴다()
    {
        // CLAUDE.md §5 — 판단과 전이는 [D] 로 남긴다. 탈진은 두 길이다(설계 §5.5): 행동의 값(cause=action)과 막다가(cause=guard ·
        // 붕괴 포함). 관측 줄([dodge])은 막다가 난 탈진만 싣고, 행동의 값으로 난 탈진은 이 줄이 유일한 흔적이다.
        var still = new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 1000),
            PatternIds = new[] { "3연격" },
            Patterns = TestConfigs.Patterns(),
            Seed = 1,
            MaxTicks = 60 * 60,
        };

        var dashed = new BattleSim(still);
        dashed.Fighter.Spend(dashed.Fighter.Stamina - 5);
        using (var log = new LogCapture())
        {
            dashed.Tick(_dash);
            for (int i = 0; i < 60 && !dashed.Fighter.Exhausted; i++)
            {
                dashed.Tick(default);
            }

            log.Lines.ShouldContain($"[fighter][D] exhaust cause=action tick={dashed.Ticks}");
        }

        // 보스가 다가와(960 − 115 = 845px · 초당 160 · 5.3초) 3연격을 연다 — 붙든 가드에 1타가 닿는다.
        still.Boss = TestConfigs.Boss(maxHealth: 999_999, patternGap: 6.0);
        var guarded = new BattleSim(still);
        guarded.Fighter.Spend(guarded.Fighter.Stamina - 5);   // 3연격 1타(8)의 값 14.4 에 모자라다 — 붕괴다
        using (var log = new LogCapture())
        {
            for (int i = 0; i < 600 && !guarded.Fighter.Exhausted; i++)
            {
                guarded.Tick(_guard);
            }

            log.Lines.ShouldContain($"[fighter][D] exhaust cause=guard tick={guarded.Ticks}");
        }
    }

    [Fact]
    public void 판정이_닿는_틱에_든_가드가_깨져도_로그의_원인은_가드다()
    {
        // 원인은 보스 판정이 본 가드로 가른다(BattleSim.LogFighterExhaust). 판정이 닿는 그 틱에 처음 ↓ 를 눌러도 가드는 파이터를 미는
        // 동안 서고, 판정은 그 가드를 깬다 — 틱 시작에서 가드를 잡았을 때는 이것이 cause=action 으로 적혀 로그가 거짓말을 했다.
        // 서 있는 파이터에게 1타가 닿는 틱을 먼저 재고, 같은 판을 그 앞 틱까지 가드 없이 민 뒤 그 틱에만 ↓ 를 누른다.
        var setup = new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, patternGap: 6.0),
            PatternIds = new[] { "3연격" },
            Patterns = TestConfigs.Patterns(),
            Seed = 1,
            MaxTicks = 60 * 60,
        };

        var probe = new BattleSim(setup);
        for (int i = 0; i < 600 && probe.Events.Count == 0; i++)
        {
            probe.Tick(default);
        }

        probe.Events.ShouldNotBeEmpty("서 있는 파이터에게 1타가 안 왔다");
        probe.Events[0].Verdict.ShouldBe(HitVerdict.Hit);
        int hit = probe.Ticks;

        var sim = new BattleSim(setup);
        while (sim.Ticks < hit - 1)
        {
            sim.Tick(default);
        }

        sim.Fighter.Guarding.ShouldBeFalse();
        sim.Fighter.Spend(sim.Fighter.Stamina - 5);   // 3연격 1타(8)의 값 14.4 에 모자라다 — 붕괴다
        using var log = new LogCapture();
        sim.Tick(_guard);

        sim.Events.ShouldHaveSingleItem().Verdict.ShouldBe(HitVerdict.GuardBroken, "그 틱에 든 가드가 판정을 안 받았다");
        sim.Fighter.Exhausted.ShouldBeTrue();
        log.Lines.ShouldContain($"[fighter][D] exhaust cause=guard tick={hit}");
    }

    [Fact]
    public void 탈진한_채_맞으면_그대로_맞고_탈진은_제_틱에_풀린다()
    {
        // Review Focus 1 — 설계 §5.5: 탈진 동안 맞으면 그대로 맞는다. ↓ 를 붙들어도 가드가 안 서고, 맞았다고 탈진이 끊기거나 늘지도
        // 않는다 — 1.1초가 "남은 타격을 그대로 맞는 값" 인 까닭이다. 3연격이 서고 조금 뒤 마지막 칼(1타)을 휘둘러 탈진에 들고, 그 탈진
        // 안에 보스의 1타(8)가 온다.
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 3.0),
            PatternIds = new[] { "3연격" },
            Patterns = TestConfigs.Patterns(),
            Seed = 1,
            MaxTicks = 60 * 60,
        });
        double standoff = sim.Boss.HalfWidth + sim.Fighter.HalfWidth;
        for (int i = 0; i < 600 && sim.Boss.X - sim.Fighter.X > standoff; i++)
        {
            sim.Tick(new InputFrame(1, false, false, false, false));
        }

        (sim.Boss.X - sim.Fighter.X).ShouldBeLessThanOrEqualTo(standoff, "파이터가 보스 앞까지 못 걸어갔다");
        for (int i = 0; i < 600 && sim.Boss.CurrentPattern is null; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.CurrentPattern.ShouldBe("3연격", "3연격이 안 섰다");
        for (int i = 0; i < 20; i++)
        {
            sim.Tick(default);
        }

        sim.Fighter.Spend(sim.Fighter.Stamina - 5);
        sim.Tick(_attack);
        for (int i = 0; i < 120 && !sim.Fighter.Exhausted; i++)
        {
            sim.Tick(default);
        }

        sim.Fighter.Exhausted.ShouldBeTrue("마지막 칼이 끝났는데 탈진하지 않았다 — 이 테스트가 탈진한 채 맞는 것을 못 본다");
        int start = sim.Ticks;
        int health = sim.Fighter.Health;
        for (int i = 0; i < 120 && sim.Events.Count == 0; i++)
        {
            sim.Tick(_guard);
        }

        sim.Events.ShouldNotBeEmpty("탈진한 동안 보스의 1타가 안 왔다");
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Hit, "탈진한 파이터가 판정을 막거나 피했다");
        sim.Fighter.Health.ShouldBe(health - 8, "탈진한 채 맞은 3연격 1타가 전액이 아니다");
        sim.Fighter.Exhausted.ShouldBeTrue("맞았다고 탈진이 끊겼다");

        for (int i = 0; i < 120 && sim.Fighter.Exhausted; i++)
        {
            sim.Tick(_guard);
        }

        (sim.Ticks - start).ShouldBe(66, "맞은 것이 탈진의 길이를 바꿨다");
    }

    [Fact]
    public void 공중에서_탈진해도_그_자리에서_떨어져_땅에_선다()
    {
        // Review Focus 5 — 공중 대시(한 번뿐이다)로 마지막 스태미나를 쓰면 대시가 공중에서 끝나고 거기서 탈진한다. 탈진은 행동 · 이동 ·
        // 점프를 막지 중력을 막지 않는다 — 떠 있는 채 굳으면 떨어지지도 못하는 파이터가 된다. 가로로도 안 흐르고(이동이 막혔다),
        // 눌러도 다시 안 뛴다. 뛰던 기세는 남아 정점까지 조금 더 오를 수 있다 — 중력이 틱마다 그것을 깎는다.
        Fighter f = Spawn();
        f.Tick(new InputFrame(0, Jump: true, false, false, false), _dt);
        for (int i = 0; i < 8; i++)
        {
            f.Tick(default, _dt);
        }

        f.Spend(f.Stamina - 5);
        Run(f, FighterAction.Dash);
        f.Exhausted.ShouldBeTrue("공중 대시가 마지막 스태미나로 끝났는데 탈진하지 않았다");
        f.Grounded.ShouldBeFalse("대시가 땅에서 끝났다 — 공중 탈진을 못 본다");

        double x = f.X;
        double vy = f.VelocityY;
        for (int i = 0; i < 120 && !f.Grounded; i++)
        {
            f.Tick(new InputFrame(1, Jump: true, false, false, false), _dt);
            f.X.ShouldBe(x, "공중에서 탈진한 파이터가 가로로 움직였다");
            if (!f.Grounded)
            {
                f.VelocityY.ShouldBeLessThan(vy, "공중에서 탈진한 파이터에게 중력이 안 걸렸다");
            }

            vy = f.VelocityY;
            f.Exhausted.ShouldBeTrue("땅에 닿기 전에 탈진이 풀렸다 — 이 테스트가 공중 탈진을 끝까지 못 본다");
        }

        f.Y.ShouldBe(0);
        f.AirDashSpent.ShouldBeFalse("착지했는데 공중 대시가 안 돌아왔다");
    }
}
