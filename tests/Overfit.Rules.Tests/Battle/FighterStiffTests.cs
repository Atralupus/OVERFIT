using System;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 행동 뒤 경직 (#82 · 설계 §5.1 · §5.6) — 유저(2026-09-26): "캐릭터에 공격 후 경직이 너무 없네요 대시 후 경직 살짝, 1타공격 후 경직,
/// 2타는 2타까지 공격후에는 좀더 오래 경직이 있게 해주세요." 칼질이 제 시간을 다 돌고 이어지는 칼이 없으면 그 칼질의 <c>stiff</c> 만큼,
/// 대시가 끝나면 <c>dash_recover</c> 만큼 더 커밋한다.
///
/// <para>
/// <b>경직의 틱 수를 숫자로 적지 않는다</b> — 늘 설정에서 <see cref="BattleSim.TicksFor"/> 로 센다. "정확히 그만큼" 은 경직이 0 인 같은
/// 파이터와 견줘 잰다(<see cref="TestConfigs.Fighter"/> 의 대조군): 경직이 없던 때의 규칙 위에 정확히 그 틱만 얹혔는지를 본다.
/// 경직 길이를 몇 가지로 바꿔 돌려, 규칙이 그 길이를 데이터에서 읽는지도 같이 본다.
/// </para>
/// </summary>
public class FighterStiffTests
{
    private const double _dt = BattleSim.Dt;

    private static readonly InputFrame _attack = new(0, false, false, false, true);
    private static readonly InputFrame _dash = new(0, false, true, false, false);

    /// <summary>경직 동안 막혀야 하는 누름 — 칼질 뒤에는 J 를 뺀다(1타의 경직 중 J 는 2타를 세우는 자리라 따로 본다).</summary>
    private static readonly (string Name, InputFrame Press, Func<Fighter, double, bool> Took)[] _presses =
    {
        ("대시", _dash, (f, _) => f.Action == FighterAction.Dash && f.ActionElapsed < 1.5 * _dt),
        ("패리", new(0, false, false, Parry: true, false), (f, _) => f.Action == FighterAction.Parry),
        ("가드", new(0, false, false, false, false, GuardHeld: true), (f, _) => f.Guarding),
        ("점프", new(0, Jump: true, false, false, false), (f, _) => f.Y > 0),
        // 걸음은 **대시의 이동과 가른다** — 대시 중의 X 변화는 걸음이 아니다.
        ("걷기", new(1, false, false, false, false), (f, x) => f.Action != FighterAction.Dash && f.X != x),
    };

    private static Fighter Spawn(FighterConfig? c = null) => new(c ?? TestConfigs.Fighter(), TestConfigs.Arena(), 960);

    /// <summary>경직이 몇 틱인가 — 규칙과 같은 반올림(<see cref="BattleSim.TicksFor"/>)이고, 0 이면 없다.</summary>
    private static int Ticks(double seconds) => seconds > 0 ? BattleSim.TicksFor(seconds) : 0;

    /// <summary><paramref name="ticks"/> 틱 동안 아무것도 안 누른다.</summary>
    private static void Idle(Fighter f, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            f.Tick(default, _dt);
        }
    }

    /// <summary>
    /// 칼질이나 대시가 제 시간을 다 돌고 경직에 든 틱까지 민다 — 이 호출이 끝나면 <see cref="Fighter.Stiff"/> 가 막 참이 됐다.
    /// 기다림은 틱 수로 묶는다: 경직이 안 서는 날 게이트를 멈춰 세우지 않고 실패한다.
    /// </summary>
    private static void UntilStiff(Fighter f, InputFrame hold = default)
    {
        for (int i = 0; i < 240 && !f.Stiff && f.Action != FighterAction.Idle; i++)
        {
            f.Tick(hold, _dt);
        }

        f.Stiff.ShouldBeTrue($"{f.Action} 이(가) 끝났는데 경직에 안 들었다");
    }

    /// <summary>
    /// <paramref name="first"/> 를 누른 틱부터 <paramref name="press"/> 를 붙들어, 그 누름이 먹는 틱(<paramref name="took"/>)이 몇 번째인가.
    /// 누른 틱이 1번째다.
    /// </summary>
    private static int FirstTook(FighterConfig c, InputFrame first, InputFrame press, Func<Fighter, double, bool> took)
    {
        Fighter f = Spawn(c);
        f.Tick(first, _dt);
        f.Action.ShouldNotBe(FighterAction.Idle, "첫 행동이 안 섰다");
        for (int tick = 2; tick < 240; tick++)
        {
            double x = f.X;
            f.Tick(press, _dt);
            if (took(f, x))
            {
                return tick;
            }
        }

        throw new InvalidOperationException("4초가 지나도 누름이 안 먹었다");
    }

    // ── 칼질 뒤 경직 ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(0.25)]
    [InlineData(0.05)]
    public void 한_번만_친_1타는_경직_틱만큼_모든_것을_늦춘다(double? stiff)
    {
        // 칼질의 커밋이 그만큼 길어진 것이다 — 가드 · 패리 · 대시 · 걸음 · 점프가 **경직이 없던 때보다 정확히 경직 틱만큼** 늦게 먹는다.
        // 길이를 바꿔 돌린다: 규칙이 경직을 숫자로 박았으면 둘 중 하나가 빨개진다.
        FighterConfig c = TestConfigs.Fighter(firstStiff: stiff);
        FighterConfig none = TestConfigs.Fighter(firstStiff: 0);
        int n = Ticks(c.Combo[0].Stiff);
        n.ShouldBeGreaterThan(0, "경직이 없다 — 이 테스트가 아무것도 안 본다");

        foreach ((string name, InputFrame press, Func<Fighter, double, bool> took) in _presses)
        {
            int late = FirstTook(c, _attack, press, took) - FirstTook(none, _attack, press, took);
            late.ShouldBe(n, $"{name}: 1타 뒤 경직({c.Combo[0].Stiff}초 = {n}틱)만큼 늦지 않다");
        }
    }

    [Fact]
    public void 연격_1타_도중_누른_J_는_경직이_없던_때와_같은_틱에_2타를_잇는다()
    {
        // 2타는 지금처럼 1타가 끝나는 틱에 선다(설계 §5.1) — 경직은 이어지는 칼이 없을 때만 붙는다. 그래서 되받아치기 2연격의 산수
        // (약 1.1초 · BossDataTests)가 그대로다. "끝나는 틱" 을 숫자로 안 적는다: 경직이 없는 같은 파이터의 1타가 서는 틱과 견준다.
        static int Chained(FighterConfig c)
        {
            Fighter f = Spawn(c);
            f.Tick(_attack, _dt);
            f.Tick(_attack, _dt);   // 1타 도중 — 2타를 눌러 둔다
            int ticks = 2;
            while (f.ComboStep == 0 && f.Action == FighterAction.Attack && ticks < 240)
            {
                f.Tick(default, _dt);
                ticks++;
            }

            f.ComboStep.ShouldBe(1, "2타가 안 이어졌다");
            return ticks;
        }

        Fighter single = Spawn(TestConfigs.Fighter(firstStiff: 0));
        single.Tick(_attack, _dt);
        int end = 1;
        while (single.Action == FighterAction.Attack && end < 240)
        {
            single.Tick(default, _dt);
            end++;
        }

        Chained(TestConfigs.Fighter(firstStiff: 0)).ShouldBe(end, "경직이 없는 파이터의 2타가 1타가 끝나는 틱에 안 섰다 — 대조가 무너졌다");
        Chained(TestConfigs.Fighter()).ShouldBe(end, "1타 뒤 경직이 2타를 늦췄다 — 이어 치는 사람까지 선다");
    }

    [Fact]
    public void 연격_1타_경직_중에_누른_J_는_그_틱에_2타를_세우고_경직이_끝나면_새_1타다()
    {
        // 잇는 창은 너그럽게 남는다 — 1타가 끝난 뒤 경직 중에 누른 J 도 **그 틱에** 2타가 된다(눌러 둔 것처럼 기다리지 않는다).
        // 2타의 시계는 누른 틱부터 돈다(Idle 에서 누른 J 와 같다). 경직의 틱마다 누르고, 경직이 끝난 다음 틱의 J 는 새 1타임을 본다 —
        // 창이 한 틱 짧거나 길면 양 끝의 한쪽이 빨개진다.
        FighterConfig c = TestConfigs.Fighter();
        int n = Ticks(c.Combo[0].Stiff);

        for (int k = 1; k <= n + 1; k++)
        {
            Fighter f = Spawn(c);
            f.Tick(_attack, _dt);
            UntilStiff(f);
            Idle(f, k - 1);
            double stamina = f.Stamina;

            f.Tick(_attack, _dt);

            f.Action.ShouldBe(FighterAction.Attack, $"경직 {k}틱째의 J 가 안 섰다");
            f.ActionElapsed.ShouldBe(_dt, 1e-9, $"경직 {k}틱째의 J: 새 칼의 시계가 누른 틱부터 안 돈다");
            f.Stiff.ShouldBeFalse($"경직 {k}틱째의 J: 칼이 섰는데 경직이 남았다");
            f.Stamina.ShouldBe(stamina - c.AttackCost, 1e-9, $"경직 {k}틱째의 J: 칼 값을 안 냈다");
            f.ComboStep.ShouldBe(k <= n ? 1 : 0,
                k <= n ? $"1타 경직 {k}틱째의 J 가 2타가 아니다" : "경직이 끝난 뒤의 J 가 2타가 됐다 — 경직이 데이터보다 길다");
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.45)]
    [InlineData(0.7)]
    public void 연격_2타_뒤에는_언제나_경직이_붙고_J_로도_못_끊는다(double? stiff)
    {
        // 2타 뒤에는 이을 칼이 없다 — 경직이 언제나 붙고, 1타 뒤보다 길다(유저: "2타까지 공격후에는 좀더 오래"). 그동안 J 를 눌러도
        // 새 1타가 안 서고 값도 안 낸다. 경직의 마지막 틱에 행동이 끝나고(Idle), 다음 틱의 J 가 새 1타다. 길이를 바꿔 돌린다 — 2타의 경직을
        // 숫자로 박으면(0.50) 셋 중 둘이 빨개진다. 기준값 하나로만 돌리던 때는 2타 쪽 숫자를 박아도 초록이었다(변이로 확인했다).
        FighterConfig c = TestConfigs.Fighter(secondStiff: stiff);
        int n = Ticks(c.Combo[1].Stiff);
        n.ShouldBeGreaterThan(Ticks(c.Combo[0].Stiff), "기준 파이터의 2타 경직이 1타보다 길지 않다");

        Fighter f = Spawn(c);
        f.Tick(_attack, _dt);
        f.Tick(_attack, _dt);
        for (int i = 0; i < 240 && f.ComboStep == 0; i++)
        {
            f.Tick(default, _dt);
        }

        f.ComboStep.ShouldBe(1, "2타가 안 이어졌다 — 이 테스트가 2타 뒤를 안 본다");
        UntilStiff(f);
        double stamina = f.Stamina;

        for (int t = 1; t < n; t++)
        {
            f.Tick(_attack, _dt);
            f.Action.ShouldBe(FighterAction.Attack, $"2타 경직 {t}틱째에 J 가 경직을 끊었다");
            f.ComboStep.ShouldBe(1, $"2타 경직 {t}틱째에 J 가 새 칼을 세웠다");
            f.Stiff.ShouldBeTrue($"2타 경직이 {t}틱째에 끝났다 — 데이터보다 짧다");
            f.Stamina.ShouldBe(stamina, 1e-9, $"2타 경직 {t}틱째: 버린 J 가 값을 냈다");
        }

        f.Tick(_attack, _dt);
        f.Action.ShouldBe(FighterAction.Idle, "2타 경직이 제 틱에 안 끝났다 — 데이터보다 길다");

        f.Tick(_attack, _dt);
        f.Action.ShouldBe(FighterAction.Attack, "2타 경직이 끝났는데 J 가 안 섰다");
        f.ComboStep.ShouldBe(0, "2타 뒤의 새 칼이 1타가 아니다");
    }

    // ── 대시 뒤 경직 ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(0.2)]
    [InlineData(0.05)]
    public void 대시_뒤_경직은_그_틱만큼_모든_것을_늦추고_그동안_제자리다(double? recover)
    {
        // 대시가 끝난 뒤 dash_recover 동안 행동 · 걸음 · 점프 · 가드가 막힌다 — 경직이 없던 때보다 정확히 그 틱만큼 늦게 먹는다. 대시 뒤에는
        // J 도 막힌다(이을 것이 없다). 그리고 **제자리다**: 대시의 이동은 대시가 끝날 때 끝난다 — 경직 동안에도 대시 속도로 흘러가면
        // 경직이 대시를 늘인 것이 된다(늦춤만 보면 그것도 초록이라 따로 본다).
        FighterConfig c = TestConfigs.Fighter(dashRecover: recover);
        FighterConfig none = TestConfigs.Fighter(dashRecover: 0);
        int n = Ticks(c.DashRecover);
        n.ShouldBeGreaterThan(0, "경직이 없다 — 이 테스트가 아무것도 안 본다");

        var presses = _presses.Append(("칼질", _attack, (f, _) => f.Action == FighterAction.Attack));
        foreach ((string name, InputFrame press, Func<Fighter, double, bool> took) in presses)
        {
            int late = FirstTook(c, _dash, press, took) - FirstTook(none, _dash, press, took);
            late.ShouldBe(n, $"{name}: 대시 뒤 경직({c.DashRecover}초 = {n}틱)만큼 늦지 않다");
        }

        Fighter f = Spawn(c);
        f.Tick(_dash, _dt);
        UntilStiff(f);
        double x = f.X;
        for (int t = 1; t < n; t++)
        {
            f.Tick(new InputFrame(1, false, false, false, false), _dt);
            f.Action.ShouldBe(FighterAction.Dash, $"대시 경직 {t}틱째에 대시가 끝났다");
            f.X.ShouldBe(x, 1e-9, $"대시 경직 {t}틱째에 움직였다");
        }

        f.Invulnerable.ShouldBeFalse("대시 경직 중에 무적이다 — 무적은 대시 안의 앞쪽뿐이다");
    }

    [Fact]
    public void 경직을_더해도_대시가_가는_거리는_그대로다()
    {
        // 경직은 대시가 **끝난 뒤**다 — 대시의 사거리(FighterDataTests · 보스 몸을 한 번에 지난다)가 안 움직인다.
        static double Travel(FighterConfig c)
        {
            Fighter f = Spawn(c);
            double start = f.X;
            f.Tick(_dash, _dt);
            for (int i = 0; i < 240 && f.Action == FighterAction.Dash; i++)
            {
                f.Tick(default, _dt);
            }

            f.Action.ShouldBe(FighterAction.Idle, "대시가 4초 안에 안 끝났다");
            return Math.Abs(f.X - start);
        }

        Travel(TestConfigs.Fighter()).ShouldBe(Travel(TestConfigs.Fighter(dashRecover: 0)), 1e-9);
    }

    // ── 스태미나 · 탈진 ──────────────────────────────────────────────────────

    [Fact]
    public void 경직은_Idle_이_아니라_그동안_스태미나가_안_찬다()
    {
        // 회복은 Idle 에서만이다(설계 §5.2) — **경직은 행동의 일부라 Idle 이 아니다**(#82). 경직 동안 차면 0 에 닿은 칼질이 경직 동안
        // 차 올라 탈진하지 않는다(아래). 경직이 끝나는 틱은 행동이 끝나는 틱이라 그 틱부터 찬다 — 경직이 없던 때 행동이 끝나는 틱과 같다.
        FighterConfig c = TestConfigs.Fighter();
        int n = Ticks(c.Combo[0].Stiff);

        Fighter f = Spawn(c);
        f.Tick(_attack, _dt);
        UntilStiff(f);
        double stamina = f.Stamina;

        for (int t = 1; t < n; t++)
        {
            f.Tick(default, _dt);
            f.Stamina.ShouldBe(stamina, 1e-9, $"경직 {t}틱째에 스태미나가 찼다");
        }

        f.Tick(default, _dt);
        f.Action.ShouldBe(FighterAction.Idle);
        f.Stamina.ShouldBe(stamina + (c.StaminaRegen * _dt), 1e-9, "경직이 끝나는 틱에 한 틱어치가 안 찼다");
    }

    [Fact]
    public void 스태미나_0_에서_끝난_칼질과_대시는_경직이_끝나는_틱에_탈진한다()
    {
        // 설계 §5.5 — 행동의 값으로 0 이 되면 그 행동이 **끝나는 틱**에 탈진한다. 이제 "끝나는 틱" 은 경직까지 끝나는 틱이다(#82):
        // 경직 동안은 칼질 · 대시가 아직 도는 것이라 탈진이 안 들고, 스태미나는 0 그대로다(Idle 이 아니다).
        FighterConfig c = TestConfigs.Fighter();
        foreach ((string name, InputFrame press, double stiff) in new[]
        {
            ("칼질", _attack, c.Combo[0].Stiff),
            ("대시", _dash, c.DashRecover),
        })
        {
            int n = Ticks(stiff);
            Fighter f = Spawn(c);
            f.Spend(f.Stamina - 5);
            f.Tick(press, _dt);
            f.Stamina.ShouldBe(0, $"{name}: 마지막 한 번이 0 까지 안 깎았다");

            UntilStiff(f);
            f.Exhausted.ShouldBeFalse($"{name}: 경직에 들며 탈진했다 — 경직 앞에서 끝났다고 봤다");
            for (int t = 1; t < n; t++)
            {
                f.Tick(default, _dt);
                f.Exhausted.ShouldBeFalse($"{name}: 경직 {t}틱째에 탈진했다");
                f.Stamina.ShouldBe(0, $"{name}: 경직 {t}틱째에 스태미나가 찼다");
            }

            f.Tick(default, _dt);
            f.Exhausted.ShouldBeTrue($"{name}: 경직이 끝나는 틱에 탈진하지 않았다");
            f.Action.ShouldBe(FighterAction.Idle);
        }
    }

    // ── 계측 — 경직 중에 맞은 판정은 누구의 것인가 ─────────────────────────────

    /// <summary>
    /// 파이터가 보스를 등진 채 <paramref name="press"/> 를 누르고, 그 행동이 경직에 든 뒤 <paramref name="intoStiff"/> 틱째에 판정 창이 열리는 판.
    /// 첫 관측이 난 틱까지 민다. 시험 판정은 사거리 <paramref name="reach"/> 까지 · 높이 0 ~ 5000 · 창 0.5초다(<see cref="TestConfigs.Sweep"/>).
    /// 경직은 <b>창이 열린 틱</b>에 본다 — 빗나감의 관측은 창이 닫히는 틱에 나오지만 그 이유와 수단은 열린 틱의 것이다(설계 §3.6 ①).
    /// </summary>
    private static (BattleSim Sim, int Lead, FighterAction AtOpen) StiffWhenOpened(InputFrame press, double reach, int intoStiff)
    {
        // 누른 틱에서 경직에 드는 틱까지 — 같은 파이터를 따로 돌려 잰다(부동소수 누산을 손으로 옮겨 적지 않는다).
        Fighter probe = Spawn();
        probe.Tick(press, _dt);
        int toStiff = 0;
        while (!probe.Stiff && toStiff < 240)
        {
            probe.Tick(default, _dt);
            toStiff++;
        }

        int lead = toStiff + intoStiff;
        BattleSim sim = TestConfigs.SweepSim(maxDistance: reach, activeSeconds: 0.5);
        sim.Tick(new InputFrame(-1, false, false, false, false));   // 보스를 등진다
        TestConfigs.UntilWindup(sim);
        while (sim.NextActiveIn is { } left && left > (lead + 1) * _dt)
        {
            sim.Tick(default);
        }

        sim.Tick(press);   // 창이 열리기 lead 틱 전
        for (int i = 0; i < 120 && sim.NextActiveIn is not null; i++)
        {
            sim.Tick(default);
        }

        sim.NextActiveIn.ShouldBeNull("창이 안 열렸다");
        sim.Fighter.Stiff.ShouldBeTrue("창이 경직 밖에서 열렸다 — 셋업이 움직였다");
        FighterAction atOpen = sim.Fighter.Action;
        for (int i = 0; i < 120 && sim.Events.Count == 0; i++)
        {
            sim.Tick(default);
        }

        sim.Events.ShouldNotBeEmpty("판정의 관측이 안 났다");
        return (sim, lead, atOpen);
    }

    [Theory]
    [InlineData(2000, HitVerdict.Hit)]
    [InlineData(1000, HitVerdict.MissedTooFar)]
    public void 대시_뒤_경직_중에_선_판정은_대시의_것이다(double reach, HitVerdict verdict)
    {
        // DodgeCredit 의 경계는 대시 행동이 끝나는 자리다 — 이제 대시 행동은 경직까지다(#82). 경직 동안 파이터는 서 있기로 한 것이
        // 아니라 **못 움직이는** 것이라, 그 자리는 대시가 만든 것이고 거기서 맞은 것은 대시가 실패한 것이다. 맞으면 "무엇을 시도했다
        // 실패했나" 가 대시이고, 거리로 빗나갔으면 대시가 빼낸 것이다(대시 시작 자리에서는 닿았다). 무적은 이미 끝나 Dodged 는 아니다.
        (BattleSim sim, int lead, FighterAction atOpen) = StiffWhenOpened(_dash, reach, intoStiff: 3);

        atOpen.ShouldBe(FighterAction.Dash);
        DodgeEvent e = sim.Events.Single();
        e.Verdict.ShouldBe(verdict);
        e.Verb.ShouldBe(DodgeVerb.Dash, "대시 뒤 경직 중의 판정이 대시의 공이 아니다");
        e.TimingError.ShouldBe(-lead * _dt, 1e-9, "공의 시각이 대시를 누른 틱이 아니다");
    }

    [Fact]
    public void 칼질_뒤_경직_중에_맞으면_욕심이다()
    {
        // GreedWindow 는 "칼질 중에 맞았나" 다(설계 §7.2) — 칼질 뒤 경직도 칼질이다(#82). 휘두른 값으로 서 있다가 맞은 것이 정확히 이
        // 축의 이야기다: 경직이 없던 때는 같은 틱에 서 있었을 뿐 칼질이 아니었다.
        (BattleSim sim, _, FighterAction atOpen) = StiffWhenOpened(_attack, reach: 2000, intoStiff: 12);

        atOpen.ShouldBe(FighterAction.Attack);
        DodgeEvent e = sim.Events.Single();
        e.Verdict.ShouldBe(HitVerdict.Hit);
        e.GreedWindow.ShouldBeTrue("칼질 뒤 경직 중에 맞은 것이 욕심으로 안 실렸다");
    }
}
