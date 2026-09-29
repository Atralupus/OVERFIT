using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 잡힘과 붙들림 (#78 · 설계 §4.7) — 유저: "잡기는 대시중에도 잡히는 공격이고 … 가드도 패리도 안되고 점프로만 회피 가능하면 됩니다."
/// 붙드는 판정(<c>grab_hold_seconds</c> &gt; 0)이 맨몸에 닿으면 결과는 <see cref="HitVerdict.Grabbed"/> 이고, 파이터는 피해를 받고
/// 붙들린다(<see cref="Fighter.Held"/>). 붙들림은 탈진과 <b>같은 고정</b>(<see cref="Fighter.Locked"/>)을 쓰는 <b>다른 상태</b>다 —
/// 겹치면 고정은 남은 것과 새 것 중 긴 쪽이다.
///
/// <para>
/// 판이나 파이터를 미는 기다림은 전부 틱 수로 묶고 뒤에서 그 조건을 단언한다 (#71 계획 결정 27 — 묶지 않은 기다림은 규칙이 깨진 날
/// 실패하지 않고 게이트를 멈춰 세운다).
/// </para>
/// </summary>
public class FighterHeldTests
{
    private const double _dt = BattleSim.Dt;

    /// <summary>붙드는 틱 — 1.0초(설계 §4.7). 규칙은 이 값을 판정에서 받는다(<c>grab_hold_seconds</c>).</summary>
    private const int _hold = 60;

    /// <summary>잡기의 피해 — 25(설계 §4.7). 여기서는 붙들림의 산수를 볼 뿐이라 값 자체를 재지 않는다.</summary>
    private const int _damage = 25;

    private static readonly InputFrame _dash = new(0, false, true, false, false);
    private static readonly InputFrame _parry = new(0, false, false, true, false);
    private static readonly InputFrame _attack = new(0, false, false, false, true);
    private static readonly InputFrame _guard = new(0, false, false, false, false, GuardHeld: true);
    private static readonly InputFrame _jump = new(0, true, false, false, false);

    /// <summary>할 수 있는 것을 전부 누른다 — 걷기 · 점프 · 대시 · 패리 · 칼질 · 가드. 고정이 하나라도 안 막으면 무언가가 선다.</summary>
    private static readonly InputFrame _everything = new(1, true, true, true, true, GuardHeld: true);

    private static Fighter Spawn() => new(TestConfigs.Fighter(), TestConfigs.Arena(), 960);

    /// <summary>고정이 풀릴 때까지 모든 것을 누르며 민다 — 굳어 있던 틱 수. 풀린 틱에는 무언가가 선다.</summary>
    private static int LockedTicks(Fighter f)
    {
        int ticks = 0;
        double x = f.X;
        for (int i = 0; i < 200 && f.Locked; i++)
        {
            f.Tick(_everything, _dt);
            ticks++;
            if (f.Locked)
            {
                f.Action.ShouldBe(FighterAction.Idle, $"{ticks}틱: 굳어 있는데 {f.Action} 이(가) 섰다");
                f.X.ShouldBe(x, $"{ticks}틱: 굳어 있는데 걸었다");
            }
        }

        return ticks;
    }

    [Fact]
    public void 잡히면_피해를_받고_다음_틱부터_붙드는_시간_동안_아무것도_못_한다()
    {
        // 설계 §4.7 — 잡히면 피해 25 · 1.0초(60틱) 동안 붙들린다: 행동 · 이동 · 점프 · 가드가 다 막힌다. 탈진과 같은 고정이라 세는 법도
        // 같다(#71 계획 결정 7) — 든 다음 틱부터 60틱 동안 막히고 61번째 틱에 풀린다. 풀리면 그대로 선다.
        Fighter f = Spawn();
        int hp = f.Health;
        f.Grab(_damage, _hold);

        f.Health.ShouldBe(hp - _damage);
        f.Held.ShouldBeTrue("잡혔는데 붙들리지 않았다");
        f.Locked.ShouldBeTrue("붙들렸는데 고정이 아니다");
        f.Exhausted.ShouldBeFalse("스태미나가 가득한데 탈진했다 — 붙들림과 탈진은 다른 상태다");

        int locked = 0;
        double x = f.X;
        for (int i = 0; i < 200 && f.Held; i++)
        {
            f.Tick(_everything, _dt);
            locked++;
            f.X.ShouldBe(x, $"{locked}틱: 붙들렸는데 걸었다");
            f.Y.ShouldBe(0, $"{locked}틱: 붙들렸는데 뛰었다");
            f.Action.ShouldBe(FighterAction.Idle, $"{locked}틱: 붙들렸는데 {f.Action} 이(가) 섰다");
        }

        locked.ShouldBe(_hold, "붙드는 틱이 판정의 값과 다르다");
        f.Locked.ShouldBeFalse("붙들림이 풀렸는데 여전히 굳어 있다");
        f.Tick(_dash, _dt);
        f.Action.ShouldBe(FighterAction.Dash, "풀린 다음 틱에 대시가 안 섰다");
    }

    [Theory]
    [InlineData(FighterAction.Dash)]
    [InlineData(FighterAction.Parry)]
    [InlineData(FighterAction.Attack)]
    [InlineData(FighterAction.Guard)]
    public void 잡기는_하던_행동을_그_자리에서_끝낸다(FighterAction action)
    {
        // 설계 §4.7 — 하던 행동(칼질 · 대시 · 패리 · 가드)이 그 자리에서 끝난다. 칼질 칸과 눌러 둔 칼도 지운다: 안 지우면 풀린 뒤 누른 한
        // 대가 2타로 선다(탈진이 지우는 것과 같은 자리다 · Fighter.Exhaust).
        Fighter f = Spawn();
        InputFrame press = action switch
        {
            FighterAction.Dash => _dash,
            FighterAction.Parry => _parry,
            FighterAction.Attack => _attack,
            _ => _guard,
        };
        f.Tick(press, _dt);
        if (action == FighterAction.Attack)
        {
            f.Tick(_attack, _dt);
            f.ComboQueued.ShouldBeTrue("2타를 눌러 두지 못했다 — 이 테스트가 눌러 둔 칼을 안 본다");
        }

        f.Action.ShouldBe(action, "행동이 안 섰다");
        f.Grab(_damage, _hold);

        f.Action.ShouldBe(FighterAction.Idle, "잡혔는데 하던 행동이 이어진다");
        f.ComboStep.ShouldBe(0);
        f.ComboQueued.ShouldBeFalse("눌러 둔 2타가 남았다");
    }

    [Theory]
    [InlineData(FighterAction.Attack)]
    [InlineData(FighterAction.Dash)]
    [InlineData(FighterAction.Parry)]
    public void 행동_뒤_경직_중에_잡히면_경직도_같이_끝나고_풀린_다음_틱에_곧장_선다(FighterAction action)
    {
        // Review Focus 1 — 1타만 치고 빠지는 사람(돌진이 겨냥한다)과 대시로만 피하는 사람(잡기가 겨냥한다)은 행동 뒤 경직(#82) 중에 잡히기
        // 쉽다. 경직은 그 행동의 끝자락이라(행동이 그대로다) 잡기가 행동과 같이 끝낸다: 붙들림이 풀린 다음 틱에 곧장 새 행동이 선다 — 남은
        // 경직이 붙들림 뒤에 이어지면 풀려도 서 있다. 붙들린 동안 누른 J 는 버린다(1타의 경직 중 J 가 2타를 잇는 갈래로 새지 않는다).
        Fighter f = Spawn();
        f.Tick(action switch { FighterAction.Attack => _attack, FighterAction.Dash => _dash, _ => _parry }, _dt);
        for (int i = 0; i < 60 && !f.Stiff; i++)
        {
            f.Tick(default, _dt);
        }

        f.Stiff.ShouldBeTrue("경직에 안 들었다 — 이 테스트가 경직을 안 본다");
        f.Action.ShouldBe(action);
        f.Grab(_damage, _hold);

        f.Stiff.ShouldBeFalse("잡혔는데 경직이 남았다");
        f.Action.ShouldBe(FighterAction.Idle);
        for (int i = 0; i < _hold; i++)
        {
            f.Tick(_attack, _dt);
            f.Action.ShouldBe(FighterAction.Idle, $"{i + 1}틱: 붙들린 채 {f.Action} 이(가) 섰다");
        }

        f.Held.ShouldBeFalse("붙들림이 제 틱에 안 풀렸다");
        f.Tick(_dash, _dt);
        f.Action.ShouldBe(FighterAction.Dash, "풀린 다음 틱에 대시가 안 섰다 — 경직이 붙들림 뒤에 남았다");
    }

    [Fact]
    public void 공중에서_잡히면_그_자리에서_떨어져_땅에_선다()
    {
        // 설계 §4.7 — 공중에서 잡혔으면 그대로 떨어진다. 굳음이 막는 것은 뛰기와 걷기이고 중력은 그대로다(탈진과 같다) — 떠 있는 채
        // 붙들리는 파이터가 없다. 붙드는 1.0초 안에 땅에 닿는다.
        Fighter f = Spawn();
        f.Tick(_jump, _dt);
        for (int i = 0; i < 10; i++)
        {
            f.Tick(default, _dt);
        }

        f.Grounded.ShouldBeFalse("뜨지 않았다 — 이 테스트가 공중을 안 본다");
        double x = f.X;
        f.Grab(_damage, _hold);

        for (int i = 0; i < _hold && !f.Grounded; i++)
        {
            f.Tick(_everything, _dt);
            f.X.ShouldBe(x, "붙들린 채 가로로 흘렀다");
        }

        f.Grounded.ShouldBeTrue("붙드는 동안 땅에 안 닿았다");
        f.Held.ShouldBeTrue("땅에 닿기 전에 풀렸다 — 이 테스트가 붙들린 채 떨어지는 것을 안 본다");
    }

    [Fact]
    public void 붙들린_동안_스태미나가_찬다()
    {
        // 설계 §4.7 — 스태미나는 Idle 이라 찬다(붕괴 고정 · 탈진과 같다). 초당 40 이라 60틱이면 40 이다.
        FighterConfig c = TestConfigs.Fighter();
        Fighter f = Spawn();
        f.Spend(50);
        f.Grab(_damage, _hold);
        for (int i = 0; i < _hold; i++)
        {
            f.Tick(default, _dt);
        }

        f.Stamina.ShouldBe(50 + (c.StaminaRegen * _hold * _dt), 1e-9);
    }

    [Fact]
    public void 탈진한_채_잡히면_고정은_남은_것과_새_것_중_긴_쪽이다()
    {
        // 설계 §4.7 「붙들림과 탈진이 겹치면」 — 옛 1타 잡기(#78)에서 1타(51틱)에 가드가 깨져 117틱까지 탈진인 사람은 못 뛰어 102틱에 잡혔다: 탈진이
        // 15틱 남았을 때 60틱 잡힌다 — 고정은 max(15, 60) = 60틱이다. 지금은 캔슬로 같은 겹침이 선다(3연격 1타 뒤 78틱에 끊고 잡기 · 설계 2026-09-29
        // 조각1 §3.2 — 창은 114틱이라 탈진이 3틱 남는다). 덮어쓰기(옛 Fighter.Exhaust)면 새 것만 남아 같은 값이지만, 남은 것이
        // 길면(아래 테스트) 일찍 풀린다. 상태는 따로 센다: 탈진은 15틱 뒤 풀리고 붙들림은 그대로다.
        Fighter f = Spawn();
        f.GuardBreak(8);
        f.Exhausted.ShouldBeTrue("가드 붕괴가 탈진이 아니다");
        for (int i = 0; i < 51; i++)
        {
            f.Tick(default, _dt);
        }

        f.Exhausted.ShouldBeTrue("붕괴한 뒤 51틱에 탈진이 풀렸다 — 이 테스트가 겹침을 안 본다");
        f.Grab(_damage, _hold);

        for (int i = 0; i < 15; i++)
        {
            f.Tick(_everything, _dt);
        }

        f.Exhausted.ShouldBeFalse("탈진이 제 틱(66)에 안 풀렸다 — 붙들림이 탈진을 늘였다");
        f.Held.ShouldBeTrue("탈진이 풀리며 붙들림도 풀렸다");
        (15 + LockedTicks(f)).ShouldBe(_hold, "고정이 긴 쪽(붙들림 60틱)이 아니다");
    }

    [Fact]
    public void 잡기가_마지막_스태미나의_행동을_끊으면_같은_틱에_탈진도_들고_고정은_긴_쪽이다()
    {
        // 설계 §4.7 · §5.5 — 잡기가 끊은 행동도 끝난 행동이다. 그 행동의 값으로 0 이 됐으면 같은 틱에 탈진도 든다: 고정은 max(60, 66) =
        // 66틱, 60틱 붙들림 뒤 6틱 탈진이다. 덮어쓰면(옛 Fighter.Exhaust) 늦게 든 쪽만 남는다. 스태미나를 남긴 행동은 탈진이 없다.
        FighterConfig c = TestConfigs.Fighter();
        Fighter spent = Spawn();
        spent.Spend(spent.Stamina - 5);
        spent.Tick(_dash, _dt);
        spent.Stamina.ShouldBe(0, "마지막 대시가 0 까지 안 썼다");
        spent.Grab(_damage, _hold);

        spent.Held.ShouldBeTrue();
        spent.Exhausted.ShouldBeTrue("마지막 스태미나의 대시를 끊었는데 탈진이 안 들었다");
        int exhaust = BattleSim.TicksFor(c.ExhaustSeconds);
        for (int i = 0; i < _hold; i++)
        {
            spent.Tick(_everything, _dt);
        }

        spent.Held.ShouldBeFalse("붙들림이 제 틱에 안 풀렸다");
        spent.Exhausted.ShouldBeTrue("붙들림이 풀리며 탈진도 풀렸다 — 긴 쪽이 아니다");
        (_hold + LockedTicks(spent)).ShouldBe(exhaust, "고정이 긴 쪽(탈진 66틱)이 아니다");

        Fighter rested = Spawn();
        rested.Tick(_dash, _dt);
        rested.Stamina.ShouldBeGreaterThan(0);
        rested.Grab(_damage, _hold);
        rested.Exhausted.ShouldBeFalse("스태미나가 남은 대시를 끊었는데 탈진했다");

        // 칼질 · 패리도 값이 있는 행동이다 — 대시만 보면 끊긴 행동의 종류에서 둘을 빼먹은 판단이 산다(Fighter.Grab 의 spent).
        foreach (InputFrame last in new[] { _attack, _parry })
        {
            Fighter f = Spawn();
            f.Spend(f.Stamina - 5);
            f.Tick(last, _dt);
            f.Stamina.ShouldBe(0, "마지막 행동이 0 까지 안 썼다");
            f.Grab(_damage, _hold);
            f.Exhausted.ShouldBeTrue($"마지막 스태미나의 {(last.Attack ? "칼질을" : "패리를")} 끊었는데 탈진이 안 들었다");
        }
    }

    [Fact]
    public void 붙들림이_풀리는_틱에_누르고_있던_가드가_선다()
    {
        // Review Focus 2 — 가드로 버티는 사람이 잡힌다(가드 중이어도 맨몸이다 · 설계 §4.7). ↓ 를 놓지 않은 채 붙들림이 풀리면 그 틱에 곧장
        // 가드가 선다: 탈진이 풀릴 때와 같다(FighterActionTests.굳음이_풀리면_누르고_있던_가드가_선다). 풀린 뒤 ↓ 를 다시 눌러야 선다면
        // 붙들림이 한 틱 더 긴 셈이다 — 점프 ×3 을 가드로 받던 사람이 잡기를 만나는 자리다.
        Fighter f = Spawn();
        f.Tick(_guard, _dt);
        f.Guarding.ShouldBeTrue("가드가 안 섰다 — 이 테스트가 가드를 안 본다");
        f.Grab(_damage, _hold);
        f.Guarding.ShouldBeFalse("잡혔는데 가드가 남았다");

        for (int i = 0; i < _hold; i++)
        {
            f.Tick(_guard, _dt);
            f.Guarding.ShouldBeFalse($"{i + 1}틱: 붙들린 채 가드가 섰다");
        }

        f.Tick(_guard, _dt);
        f.Guarding.ShouldBeTrue("풀린 틱에 누르고 있던 가드가 안 섰다");
    }

    [Fact]
    public void 고정의_남은_몫은_다시_들어도_줄지_않는다()
    {
        // 설계 §4.7 — 고정의 길이는 남은 것과 새 것 중 긴 쪽이다. 붙들림과 탈진은 따로 세므로 둘이 겹치면 저절로 긴 쪽이고(위 두 테스트),
        // 붙들린 채 더 짧게 다시 잡혀도 남은 붙들림이 줄지 않는다 — 붙드는 시간은 판정의 값이라 판정마다 다를 수 있다. 지금 규칙에는 그 길이
        // 없다(붙드는 판정은 패턴마다 하나다) — 몸의 약속을 여기서 못박는다. 탈진한 채 짧게 잡혀도 남은 탈진이 고정을 잡고 있다.
        FighterConfig c = TestConfigs.Fighter();
        Fighter held = Spawn();
        held.Grab(_damage, _hold);
        for (int i = 0; i < 10; i++)
        {
            held.Tick(default, _dt);
        }

        held.Grab(_damage, 5);
        (10 + LockedTicks(held)).ShouldBe(_hold, "더 짧게 다시 잡혀 남은 붙들림이 줄었다");

        int exhaust = BattleSim.TicksFor(c.ExhaustSeconds);
        Fighter broken = Spawn();
        broken.GuardBreak(8);
        for (int i = 0; i < 10; i++)
        {
            broken.Tick(default, _dt);
        }

        broken.Grab(_damage, 1);
        (10 + LockedTicks(broken)).ShouldBe(exhaust, "짧은 붙들림이 남은 탈진을 줄였다");
    }

    /// <summary>붙드는 판정 하나짜리 시험 패턴 — 태그로는 대시 · 패리가 되는데 판정의 답이 셋 다 막는다(설계 §4.7).</summary>
    private static PatternDef Grab() => new()
    {
        Tags = new PatternTags
        {
            DashWindow = 0.2,
            DashDirection = "either",
            Jumpable = true,
            AntiAir = false,
            Parryable = true,
            ParryWindow = 0.18,
            PunishGreed = false,
            Reach = "far",
            MultiHit = 1,
            Tracking = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0.0, Kind = "windup" },
            new()
            {
                T = 0.5, Kind = "active", Band = new double[] { 0, 1920, 0, 60 }, Damage = _damage, ActiveSeconds = 0.125,
                Dash = false, Guard = false, Parry = false, GrabHoldSeconds = 1.0,
            },
            new() { T = 2.0, Kind = "end" },
        },
    };

    private static BattleSim GrabSim() => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighter(),
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, rest: 0.2),
        PatternIds = new[] { "잡기" },
        Patterns = new Dictionary<string, PatternDef> { ["잡기"] = Grab() },
        Seed = 1,
        MaxTicks = 60 * 30,
    });

    /// <summary>판정이 서기 <paramref name="lead"/> 틱 앞까지 민다 — 다음 틱의 누름이 창이 열리기 <paramref name="lead"/> 틱 앞이다.</summary>
    private static void Until(BattleSim sim, int lead)
    {
        TestConfigs.UntilWindup(sim);
        for (int i = 0; i < 600 && sim.NextActiveIn is { } left && left > (lead + 1) * _dt; i++)
        {
            sim.Tick(default);
        }

        sim.NextActiveIn.ShouldNotBeNull("판정 앞에 패턴이 안 섰다");
        sim.NextActiveIn.Value.ShouldBe((lead + 1) * _dt, 1e-9, "판정 앞 그 틱에 못 섰다");
    }

    /// <summary>관측이 하나 설 때까지 <paramref name="hold"/> 를 붙든 채 민다.</summary>
    private static DodgeEvent UntilEvent(BattleSim sim, InputFrame hold)
    {
        for (int i = 0; i < 120 && sim.Events.Count == 0; i++)
        {
            sim.Tick(hold);
        }

        sim.Events.Count.ShouldBe(1, "잡기 창이 관측을 안 남겼다");
        return sim.Events[0];
    }

    [Theory]
    [InlineData(FighterAction.Dash)]
    [InlineData(FighterAction.Guard)]
    [InlineData(FighterAction.Parry)]
    public void 잡기는_대시_무적도_가드도_패리도_안_받고_그_수단으로_잡힌다(FighterAction action)
    {
        // 설계 §4.7 — 대시 무적을 안 받는다 · 가드 중이어도 맨몸이다(붕괴가 아니다) · 패리 창 안이어도 맨몸이다. 창이 열리기 한 틱 앞에
        // 눌러 창의 첫 틱이 무적 · 패리 창 · 가드 한가운데다. 결과는 잡힘이고 수단은 그 순간 하던 것 중 가장 최근에 시작한 것이다 —
        // 가드도 든다(설계 §12 「잡힘」: 가드로 버티다 잡힌 기록이 "아무것도 안 함" 이 되지 않게). 고를 수 있던 것은 점프뿐이다(설계 §7.3).
        BattleSim sim = GrabSim();
        Until(sim, 1);
        InputFrame press = action switch
        {
            FighterAction.Dash => _dash,
            FighterAction.Parry => _parry,
            _ => _guard,
        };
        sim.Tick(press);
        sim.Fighter.Action.ShouldBe(action, "누른 수단이 안 섰다");

        DodgeEvent e = UntilEvent(sim, action == FighterAction.Guard ? _guard : default);

        e.Verdict.ShouldBe(HitVerdict.Grabbed);
        e.Verb.ShouldBe(action switch
        {
            FighterAction.Dash => DodgeVerb.Dash,
            FighterAction.Parry => DodgeVerb.Parry,
            _ => DodgeVerb.Guard,
        });
        (e.DashAvailable, e.GuardAvailable, e.ParryAvailable, e.JumpAvailable).ShouldBe((false, false, false, true));
        sim.Fighter.Held.ShouldBeTrue("잡혔는데 붙들리지 않았다");
        sim.Fighter.Action.ShouldBe(FighterAction.Idle, "잡혔는데 하던 것이 이어진다");
    }

    [Fact]
    public void 잡기는_점프로_넘는다()
    {
        // 설계 §4.7 — 높이 60 띠라 점프로만 넘는다. 창이 열리기 스무 틱 앞에 뛴 기준 파이터(jump_velocity 940)는 창 8틱 내내 발이 60 위다.
        BattleSim sim = GrabSim();
        Until(sim, 20);
        sim.Tick(_jump);

        DodgeEvent e = UntilEvent(sim, default);

        e.Verdict.ShouldBe(HitVerdict.MissedByHeight);
        e.Verb.ShouldBe(DodgeVerb.Jump);
        sim.Fighter.Held.ShouldBeFalse("뛰어넘었는데 붙들렸다");
    }

    [Fact]
    public void 칼질_중에_잡히면_욕심으로_남는다()
    {
        // 관측은 결과를 몸에 싣기 **전**의 몸으로 짓는다(BossSwings.Land) — 잡힘은 하던 행동을 끝내므로 실은 뒤에 지으면 칼질 중에 잡힌
        // 사람의 욕심(GreedWindow · 설계 §7.2)이 지워진다. 창이 열리기 세 틱 앞에 누른 1타(기준 17틱)가 창의 첫 틱에 돈다.
        BattleSim sim = GrabSim();
        Until(sim, 3);
        sim.Tick(_attack);

        DodgeEvent e = UntilEvent(sim, default);

        e.Verdict.ShouldBe(HitVerdict.Grabbed);
        e.GreedWindow.ShouldBeTrue("칼질 중에 잡혔는데 욕심으로 안 남았다 — 관측을 행동을 끝낸 뒤에 지었다");
    }

    [Fact]
    public void 잡히면_그_틱에_붙들림을_로그로_남긴다()
    {
        // CLAUDE.md §5 — 전이는 [D] 로 남긴다. 붙들린 채 탈진이 겹쳤는지(설계 §4.7)를 줄 하나로 읽는다. 붙들린 동안 매 틱 적지 않는다 —
        // 붙들림(60틱)이 풀릴 때까지 민 뒤에 센다.
        BattleSim sim = GrabSim();
        Until(sim, 1);
        using var log = new LogCapture();
        DodgeEvent e = UntilEvent(sim, default);
        int grabbed = sim.Ticks;
        for (int i = 0; i < _hold + 5; i++)
        {
            sim.Tick(default);
        }

        e.Verdict.ShouldBe(HitVerdict.Grabbed);
        sim.Fighter.Held.ShouldBeFalse("붙들림이 안 풀렸다 — 이 테스트가 붙들린 동안을 다 안 봤다");
        log.Lines.ShouldContain($"[fighter][D] held exhausted=False tick={grabbed}");
        log.Lines.Count(l => l.StartsWith("[fighter][D] held ")).ShouldBe(1, "붙들린 동안 매 틱 적었다");
    }

    [Fact]
    public void 마지막_스태미나의_행동을_끊은_잡기는_붙들림과_탈진을_같은_틱에_잇달아_적는다()
    {
        // 「이 계획이 정한 것」 10 — 잡기가 마지막 스태미나의 행동을 끊으면 붙들린 틱에 탈진도 든다(설계 §4.7 · §5.5). 로그는 그 틱에 두 줄이다:
        // 붙들림(exhausted=True)이 먼저고, 탈진의 원인(action — 가드가 아니다)이 잇는다. 창이 열리기 한 틱 앞에 남은 5 로 대시를 누른다 —
        // 값(25)이 모자라도 마지막 한 번은 나가 0 이 되고(설계 §5.5), 창의 첫 틱이 그 대시를 끊는다.
        BattleSim sim = GrabSim();
        Until(sim, 1);
        sim.Fighter.Spend(sim.Fighter.Stamina - 5);
        sim.Tick(_dash);
        sim.Fighter.Action.ShouldBe(FighterAction.Dash, "대시가 안 섰다");
        sim.Fighter.Stamina.ShouldBe(0, "마지막 대시가 0 까지 안 썼다 — 이 테스트가 탈진을 안 본다");

        using var log = new LogCapture();
        DodgeEvent e = UntilEvent(sim, default);

        e.Verdict.ShouldBe(HitVerdict.Grabbed);
        sim.Fighter.Exhausted.ShouldBeTrue("마지막 스태미나의 대시를 끊었는데 탈진이 안 들었다");
        log.Lines.Where(l => l.StartsWith("[fighter][D] ")).ShouldBe(new[]
        {
            $"[fighter][D] held exhausted=True tick={sim.Ticks}",
            $"[fighter][D] exhaust cause=action tick={sim.Ticks}",
        }, "붙들림과 탈진이 그 틱에 잇달아 안 적혔다");
    }
}
