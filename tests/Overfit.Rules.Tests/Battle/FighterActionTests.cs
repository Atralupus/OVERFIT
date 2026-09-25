using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class FighterActionTests
{
    private const double _dt = 1.0 / 60.0;

    private static readonly InputFrame _dash = new(0, false, true, false, false);
    private static readonly InputFrame _parry = new(0, false, false, true, false);
    private static readonly InputFrame _attack = new(0, false, false, false, true);

    /// <summary>↓ 를 누르고 있는 틱 (설계 §5.2 — 가드는 누르고 있는 동안이다).</summary>
    private static readonly InputFrame _guard = new(0, false, false, false, false, GuardHeld: true);

    private static Fighter Spawn(double x = 960) => new(TestConfigs.Fighter(), TestConfigs.Arena(), x);

    [Fact]
    public void 대시는_스태미나를_쓰고_무적을_준다()
    {
        Fighter f = Spawn();
        f.Tick(_dash, _dt);

        f.Action.ShouldBe(FighterAction.Dash);
        f.Stamina.ShouldBeLessThan(100);
        f.Invulnerable.ShouldBeTrue();
    }

    [Fact]
    public void 무적은_대시보다_먼저_끝난다()
    {
        // 무적 창(0.14)이 대시 지속(0.18)보다 짧다 — 끝자락에 맞을 수 있어야 타이밍이 의미를 갖는다
        Fighter f = Spawn();
        f.Tick(_dash, _dt);
        for (int i = 0; i < 8; i++)
        {
            f.Tick(default, _dt);
        }

        f.Action.ShouldBe(FighterAction.Dash);
        f.Invulnerable.ShouldBeFalse();
    }

    [Fact]
    public void 대시는_바라보는_쪽으로_간다()
    {
        Fighter f = Spawn();
        f.Tick(new InputFrame(-1, false, false, false, false), _dt);
        double before = f.X;

        f.Tick(_dash, _dt);
        for (int i = 0; i < 9; i++)
        {
            f.Tick(default, _dt);
        }

        f.X.ShouldBeLessThan(before);
    }

    [Fact]
    public void 스태미나가_모자라면_대시가_안_된다()
    {
        Fighter f = Spawn();
        f.Spend(90);   // 10 남는다. 대시는 25

        f.Tick(_dash, _dt);

        f.Action.ShouldBe(FighterAction.Idle);
    }

    [Fact]
    public void 행동_중에는_다른_행동을_못_시작한다()
    {
        // 대시는 커밋이다 — 가드도 대시를 못 끊는다(이 계획이 정한 것 2: 가드는 커밋된 행동이 없는 틱에만 선다).
        // ↓ 는 엣지가 아니라 레벨이라 따로 본다: 끊을 수 있으면 무적이 풀린 대시의 끝자락을 가드로 덮어 대시에 값이 없다.
        // 이 단언이 없을 때는 ↓ 가 대시를 끊게 바꿔도 스위트 전체가 초록이었다(최종 리뷰 I2 · 변이 M2).
        Fighter f = Spawn();
        f.Tick(_dash, _dt);
        f.Tick(_parry, _dt);

        f.Action.ShouldBe(FighterAction.Dash);

        f.Tick(_guard, _dt);

        f.Action.ShouldBe(FighterAction.Dash, "대시 중에 ↓ 가 가드를 세웠다 — 대시는 커밋이다");
        f.Guarding.ShouldBeFalse();
    }

    [Fact]
    public void 행동_중에는_점프도_못_한다()
    {
        Fighter f = Spawn();
        f.Tick(_dash, _dt);
        f.Tick(new InputFrame(0, true, false, false, false), _dt);

        f.Y.ShouldBe(0);
        f.Grounded.ShouldBeTrue();
    }

    /// <summary><paramref name="ticks"/> 틱 동안 아무것도 안 하고 흘려보낸다.</summary>
    private static void Idle(Fighter f, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            f.Tick(default, _dt);
        }
    }

    [Fact]
    public void 공중_대시는_한_번뿐이고_패리가_되돌린다()
    {
        // 나인 솔즈의 보상 구조다 — 잘 받아내면 다시 움직일 수 있다.
        // 몸 충돌이 없어져(이슈 #27) 공중이 안전지대가 됐으므로, 무제한 공중 대시는
        // "떠서 계속 무적" 이라는 답 하나로 모든 패턴을 지운다.
        Fighter f = Spawn();
        f.Tick(new InputFrame(0, true, false, false, false), _dt);
        f.Tick(_dash, _dt);
        f.Action.ShouldBe(FighterAction.Dash);
        f.AirDashSpent.ShouldBeTrue();

        Idle(f, 12);   // 대시가 끝나기를 기다린다 (0.18초)
        f.Action.ShouldBe(FighterAction.Idle);
        f.Grounded.ShouldBeFalse("아직 공중이어야 이 테스트가 공중 대시를 본다");
        f.Tick(_dash, _dt);
        f.Action.ShouldBe(FighterAction.Idle, "공중에서 두 번째 대시가 나갔다");

        f.ParryPrecise();
        f.Tick(_dash, _dt);
        f.Action.ShouldBe(FighterAction.Dash, "패리가 공중 대시를 안 돌려줬다");
    }

    [Fact]
    public void 착지하면_공중_대시가_돌아온다()
    {
        Fighter f = Spawn();
        f.Tick(new InputFrame(0, true, false, false, false), _dt);
        f.Tick(_dash, _dt);
        f.AirDashSpent.ShouldBeTrue();

        while (!f.Grounded)
        {
            f.Tick(default, _dt);
        }

        f.AirDashSpent.ShouldBeFalse();
    }

    [Fact]
    public void 공격은_선딜_뒤에_판정이_선다()
    {
        Fighter f = Spawn();
        f.Tick(_attack, _dt);
        f.AttackActive.ShouldBeFalse();   // 선딜 0.08

        for (int i = 0; i < 5; i++)
        {
            f.Tick(default, _dt);
        }

        f.AttackActive.ShouldBeTrue();
    }

    [Fact]
    public void 공격_판정은_후딜에_꺼진다()
    {
        Fighter f = Spawn();
        f.Tick(_attack, _dt);
        for (int i = 0; i < 12; i++)   // 0.2167초 — windup+active(0.14)를 지났다
        {
            f.Tick(default, _dt);
        }

        f.Action.ShouldBe(FighterAction.Attack);
        f.AttackActive.ShouldBeFalse();
    }


    // ── 2연격 (설계 §5.1) ────────────────────────────────────────────────────

    /// <summary>1타를 누르고, <paramref name="queueAfter"/> 틱째에 한 번 더 누른다 — 1타 도중이다.</summary>
    private static Fighter TwoPresses(int queueAfter = 2)
    {
        Fighter f = Spawn();
        f.Tick(_attack, _dt);
        Idle(f, queueAfter - 2);
        f.Tick(_attack, _dt);
        return f;
    }

    /// <summary>2타가 이어지는 틱까지 민다. 이어진 뒤라 <c>ComboStep</c> 이 1 이다.</summary>
    private static void UntilSecond(Fighter f)
    {
        for (int i = 0; i < 120 && f.ComboStep == 0 && f.Action == FighterAction.Attack; i++)
        {
            f.Tick(default, _dt);
        }

        f.ComboStep.ShouldBe(1, "2타가 안 이어졌다 — 이 테스트가 2타를 안 본다");
    }

    [Fact]
    public void 누르면_곧장_1타를_휘두른다()
    {
        Fighter f = Spawn();
        f.Tick(_attack, _dt);

        f.Action.ShouldBe(FighterAction.Attack);
        f.ComboStep.ShouldBe(0);
        f.AttackDamage.ShouldBe(TestConfigs.Fighter().Combo[0].Damage);
    }

    [Fact]
    public void 연격_1타_도중_또_누르면_1타가_끝나는_틱에_2타가_이어진다()
    {
        // 설계 §5.1 — 2타는 눌러 둔 순간이 아니라 1타가 끝나는 틱에 선다. 1타는 끝까지 커밋이다.
        // "끝나는 틱" 을 숫자로 안 적는다: 한 번만 누른 1타가 서는(Idle 이 되는) 틱과 견준다.
        Fighter single = Spawn();
        single.Tick(_attack, _dt);
        int end = 1;
        while (single.Action == FighterAction.Attack)
        {
            single.Tick(default, _dt);
            end++;
        }

        Fighter combo = TwoPresses();
        combo.ComboStep.ShouldBe(0, "눌러 둔 순간 2타가 섰다 — 1타가 잘렸다");
        combo.ComboQueued.ShouldBeTrue();

        int chained = 2;
        while (combo.ComboStep == 0 && combo.Action == FighterAction.Attack)
        {
            combo.Tick(default, _dt);
            chained++;
        }

        combo.ComboStep.ShouldBe(1);
        combo.Action.ShouldBe(FighterAction.Attack, "1타가 끝나고 서 버렸다");
        chained.ShouldBe(end, "2타가 1타가 끝나는 틱이 아닌 때에 섰다");
    }

    [Fact]
    public void 안_누르면_1타로_끝난다()
    {
        Fighter f = Spawn();
        f.Tick(_attack, _dt);
        Idle(f, 60);

        f.Action.ShouldBe(FighterAction.Idle);
        f.ComboStep.ShouldBe(0);
    }

    [Fact]
    public void 연격_2타는_2타의_시간과_피해로_돈다()
    {
        // 2타는 attack2 를 반속으로 도는 무거운 칼이다 — 선딜도 피해도 2타 칸의 것이어야 한다.
        ComboStepDef second = TestConfigs.Fighter().Combo[1];
        Fighter f = TwoPresses();
        UntilSecond(f);

        f.AttackDamage.ShouldBe(second.Damage);
        int ticks = 0;
        while (!f.AttackActive && ticks < 600)
        {
            f.Tick(default, _dt);
            ticks++;
        }

        (ticks * _dt).ShouldBe(second.Windup, 1.5 * _dt, "2타의 선딜이 2타 칸의 것이 아니다");
    }

    [Fact]
    public void 연격_2타_뒤에는_이어_칠_것이_없다()
    {
        Fighter f = TwoPresses();
        UntilSecond(f);
        f.Tick(_attack, _dt);   // 2타 도중 또 누른다

        f.ComboQueued.ShouldBeFalse("2타 뒤에 셋째를 눌러 뒀다 — 2연격이다");
        while (f.Action == FighterAction.Attack)
        {
            f.Tick(default, _dt);
        }

        f.ComboStep.ShouldBe(0, "칼질이 끝났는데 다음 칼이 2타로 시작한다");
    }

    [Fact]
    public void 연격_2타는_이을_때_값을_낸다()
    {
        // 스태미나는 타마다 낸다 (설계 §5.1). 눌러 둘 때가 아니라 이을 때 낸다 — 이 계획이 정한 것 4.
        FighterConfig c = TestConfigs.Fighter();
        Fighter f = TwoPresses();
        f.Stamina.ShouldBe(c.MaxStamina - c.AttackCost, 1e-9, "눌러 둔 순간 2타 값을 냈다");

        UntilSecond(f);
        f.Stamina.ShouldBe(c.MaxStamina - (2 * c.AttackCost), 1e-9);
    }

    [Fact]
    public void 연격_2타_값이_모자라면_잇지_않고_선다()
    {
        // Review Focus 2 — 1타 도중 스태미나가 바닥났으면 2타는 안 서고 1타로 끝난다. 음수로 가지 않는다.
        //
        // ⚠ 칼질이 **끝난 뒤**의 값은 증인이 못 된다: 끝나면 ComboStep 은 언제나 0 으로 돌아오고, Spend 는 0 에서
        // 멈춰 스태미나가 음수가 될 수도 없다 — 그 둘을 끝에서 보던 이 테스트는 값 검사를 통째로 지워도 초록이었다
        // (리뷰가 변이로 확인했다). 그래서 **도는 동안**을 본다: 1타가 끝나는 틱에 서는가(한 번만 누른 1타와
        // 견준다 — …1타가_끝나는_틱에_2타가_이어진다 와 같은 방법), 그리고 도는 내내 값이 1타 하나만큼인가.
        FighterConfig c = TestConfigs.Fighter();
        Fighter single = Spawn();
        single.Tick(_attack, _dt);
        int end = 1;
        while (single.Action == FighterAction.Attack)
        {
            single.Tick(default, _dt);
            end++;
        }

        Fighter f = Spawn();
        f.Spend(c.MaxStamina - c.AttackCost - 1);   // 1타 값 + 1 만 남긴다
        f.Tick(_attack, _dt);
        f.Tick(_attack, _dt);                        // 1타 도중 — 2타를 눌러 둔다
        f.ComboQueued.ShouldBeTrue("눌러 두지도 않았다 — 이 테스트가 모자란 값을 안 본다");

        int stood = 2;
        while (f.Action == FighterAction.Attack)
        {
            // 이었다면 잇는 틱에 값을 한 번 더 내 0 으로 깎이고(Spend 가 0 에서 멈춘다) 칼질은 2타로 계속 돈다.
            f.Stamina.ShouldBe(1, 1e-9, "이을 수 없는 2타가 값을 냈다");
            f.Tick(default, _dt);
            stood++;
        }

        stood.ShouldBe(end, "값이 모자라는데 2타가 섰다 — 1타가 끝나는 틱에 안 섰다");
    }

    [Fact]
    public void 칼질_중에는_다른_것을_못_한다()
    {
        // 끝까지 커밋 (설계 §5.1). 2타는 1초짜리라 그 사이 무엇도 못 하는 것이 2타의 값이다.
        // ↓ 도 같이 누른다 — "공격 중 가드 전환 불가" 는 유저가 말한 것이다(설계 §1). 칼질의 갈래는 다른 행동이 쓰는 공통 검사
        // 앞에서 따로 돌아가므로 그 검사가 막아 주지 않는다: ↓ 를 빼 두었을 때는 칼질 중에 가드로 바꿔도 초록이었다
        // (최종 리뷰 I2 · 변이 M1). 되면 1초짜리 2타를 가드로 끊어 2타의 값이 없어진다.
        Fighter f = Spawn();
        f.Tick(_attack, _dt);
        double x = f.X, stamina = f.Stamina;

        f.Tick(new InputFrame(1, Jump: true, Dash: true, Parry: true, Attack: false, GuardHeld: true), _dt);

        f.Action.ShouldBe(FighterAction.Attack);
        f.Guarding.ShouldBeFalse("칼질 중에 가드로 바뀌었다");
        f.X.ShouldBe(x, 1e-9, "칼질 중에 걸었다");
        f.Grounded.ShouldBeTrue("칼질 중에 뛰었다");
        f.Stamina.ShouldBe(stamina, 1e-9, "버린 입력이 값을 냈다");
    }

    [Fact]
    public void 맞아도_칼질은_안_끊긴다()
    {
        // 이 계획이 정한 것 5 — 끝까지 커밋이다. 맞으면 끊기던 것은 차지였다.
        Fighter f = Spawn();
        f.Tick(_attack, _dt);
        f.TakeDamage(9);

        f.Action.ShouldBe(FighterAction.Attack);
    }

    [Fact]
    public void 스태미나는_행동이_끝나면_회복한다()
    {
        Fighter f = Spawn();
        f.Tick(_dash, _dt);
        double low = f.Stamina;

        for (int i = 0; i < 120; i++)
        {
            f.Tick(default, _dt);
        }

        f.Stamina.ShouldBeGreaterThan(low);
        f.Stamina.ShouldBeLessThanOrEqualTo(100);
    }

    [Fact]
    public void 대시_중에는_방향_입력을_받아도_안_돌아선다()
    {
        // 보스의 잠금(이슈 #36)과 같은 규칙이 파이터에도 선다. 여기서는 그림만의 문제가 아니다 —
        // Facing 이 대시 이동에 곱해지므로, 도중에 뒤집히면 대시가 <b>가던 길을 되돌아온다.</b>
        Fighter f = Spawn();
        f.Tick(_dash, _dt);
        double after = f.X;

        for (int i = 0; i < 6; i++)
        {
            f.Tick(new InputFrame(-1, false, false, false, false), _dt);
        }

        f.Action.ShouldBe(FighterAction.Dash);
        f.Facing.ShouldBe(1);
        f.X.ShouldBeGreaterThan(after);
    }

    [Fact]
    public void 공격_중에는_방향_입력을_받아도_안_돌아선다()
    {
        // 휘두르던 칼이 도중에 반대쪽을 향하면 그림이 거짓말이 된다.
        Fighter f = Spawn();
        f.Tick(_attack, _dt);

        for (int i = 0; i < 6; i++)
        {
            f.Tick(new InputFrame(-1, false, false, false, false), _dt);
        }

        f.Action.ShouldBe(FighterAction.Attack);
        f.Facing.ShouldBe(1);
    }

    [Fact]
    public void 피해를_받으면_체력이_줄고_0_아래로_안_간다()
    {
        Fighter f = Spawn();
        f.TakeDamage(30);
        f.Health.ShouldBe(70);
        f.Alive.ShouldBeTrue();

        f.TakeDamage(999);
        f.Health.ShouldBe(0);
        f.Alive.ShouldBeFalse();
    }

    // ── 패리 (설계 §5.3) ─────────────────────────────────────────────────────

    [Fact]
    public void 패리는_누르면_커밋하고_앞쪽만_창이다()
    {
        // 누르면 0.333초 커밋이고 앞 0.133초가 창이다. 창이 닫혀도 커밋은 끝까지 간다 — 누를 때마다 60% 는
        // 무방비로 서 있는 것이 스펙이 연타 징벌을 지운 근거다.
        //
        // 두 경계를 **양쪽에서** 못박는다 — 창의 마지막 틱과 첫 바깥 틱, 커밋의 마지막 틱과 끝난 틱. 한쪽만 볼 때는
        // 창이 한 틱 넓어져도 모든 스위트가 초록이었고, 커밋이 한 틱 짧아져도 골든만 빨개졌다(리뷰가 변이로 확인했다).
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        f.Action.ShouldBe(FighterAction.Parry);
        f.Parrying.ShouldBeTrue("누른 틱에 창이 안 열렸다");

        Idle(f, 6);   // 7틱 = 0.1167초 — 창(0.133)의 마지막 틱
        f.Parrying.ShouldBeTrue("창이 한 틱 일찍 닫혔다");

        Idle(f, 1);   // 8틱 = 0.1333초 — 창 밖의 첫 틱
        f.Parrying.ShouldBeFalse("창(0.133초)이 제때 안 닫혔다 — 창이 데이터보다 넓다");
        f.Action.ShouldBe(FighterAction.Parry, "창이 닫히면서 커밋까지 풀렸다 — 누를 때의 값이 없다");

        Idle(f, 11);   // 19틱 = 0.3167초 — 커밋(0.3333)의 마지막 틱
        f.Action.ShouldBe(FighterAction.Parry, "커밋이 한 틱 일찍 끝났다");

        Idle(f, 1);   // 20틱 = 0.3333초 — 커밋이 끝났다
        f.Action.ShouldBe(FighterAction.Idle);
    }

    [Fact]
    public void 패리는_공중에서도_선다()
    {
        // 이 계획이 정한 것 3 — 스펙은 "땅에서만" 을 가드에만 적었다. 공중 패리를 막으면 받아쳐 공중 대시를
        // 되돌려 받는 보상(ParryPrecise)이 설 자리가 없다.
        Fighter f = Spawn();
        f.Tick(new InputFrame(0, Jump: true, false, false, false), _dt);
        f.Tick(_parry, _dt);

        f.Grounded.ShouldBeFalse("아직 공중이어야 이 테스트가 공중 패리를 본다");
        f.Action.ShouldBe(FighterAction.Parry, "공중에서 누른 패리가 안 섰다");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void 패리_커밋_내내_가드_패리_대시_이동을_못_하고_못_받아쳤으면_J_도_버린다(bool landed)
    {
        // 커밋이 막는 것은 가드 · 패리 · 대시 · 이동이다 (설계 §1 · §5.1 의 목록 · §5.3: "그동안 커밋") — 받아쳤든
        // 못 받아쳤든 커밋 **내내**다. 공격은 그 목록에 없다: 받아친 패리의 J 만은 곧장 1타가 된다(아래 되받아치기).
        // 못 받아친 패리(헛쳤거나 아직 기다리는)는 J 까지 버린다 — 난사의 값은 커밋 전체다.
        //
        // 옛 테스트는 누른 다음 한 틱만 봤다 — 그 뒤 틱에 커밋이 풀려도 몰랐다. 커밋의 마지막 틱(19틱 —
        // 패리는_누르면_커밋하고_앞쪽만_창이다)까지 매 틱 전부 누른다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        if (landed)
        {
            f.ParryPrecise();   // 누른 틱에 받아쳤다 — BattleSim 이 판정 뒤에 부르는 자리다
        }

        double x = f.X, stamina = f.Stamina;

        // 받아친 패리에는 J 를 안 섞는다 — 그건 되받아치기다.
        var everything = new InputFrame(1, Jump: true, Dash: true, Parry: true, Attack: !landed, GuardHeld: true);
        for (int tick = 2; tick <= 19; tick++)
        {
            f.Tick(everything, _dt);

            f.Action.ShouldBe(FighterAction.Parry, $"{tick}틱: 패리 커밋 중에 {f.Action} 이(가) 섰다");
            f.X.ShouldBe(x, 1e-9, $"{tick}틱: 패리 커밋 중에 걸었다");
            f.Grounded.ShouldBeTrue($"{tick}틱: 패리 커밋 중에 뛰었다");
            f.Stamina.ShouldBe(stamina, 1e-9, $"{tick}틱: 버린 입력이 값을 냈다");
        }
    }

    // ── 되받아치기 — 받아친 패리의 커밋 안의 J (판정 13 · 설계 §4.3) ─────────

    [Fact]
    public void 받아친_패리의_커밋_중에_누른_J_는_곧장_1타다()
    {
        // 설계 §4.3 의 타임라인은 받아치고 ~0.2초 반응해 누른 J 가 1타로 닿는다. 받아치는 것은 창(0.133) 안이라 그 J 는
        // 언제나 커밋(0.333) 안에 떨어진다 — 버리면 그 타임라인이 설 자리가 없고, 누른 J 는 아무 표시 없이 사라진다.
        // 받는 J 는 Idle 에서 누른 J 와 **같다**: 1타(0칸)부터 · 1타 값을 내고 · 시계는 0 에서.
        FighterConfig c = TestConfigs.Fighter();
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        Idle(f, 2);
        f.ParryPrecise();   // 3틱 — 창 안에서 받아쳤다
        Idle(f, 12);        // 반응 0.2초 — 15틱 = 0.25초, 커밋(0.333) 안이다
        f.Action.ShouldBe(FighterAction.Parry, "커밋이 벌써 끝났다 — 이 테스트가 커밋 안의 J 를 안 본다");
        double stamina = f.Stamina;

        f.Tick(_attack, _dt);

        f.Action.ShouldBe(FighterAction.Attack, "받아친 패리의 커밋 안에서 누른 J 가 버려졌다");
        f.ComboStep.ShouldBe(0, "되받아치기가 1타가 아닌 칸에서 시작했다");
        f.ActionElapsed.ShouldBe(_dt, 1e-9, "패리의 시계를 이어받았다 — 1타의 선딜이 잘린다");
        f.Stamina.ShouldBe(stamina - c.AttackCost, 1e-9, "되받아치기가 1타 값을 안 냈다");
    }

    [Fact]
    public void 받아친_패리라도_1타_값이_모자라면_J_를_버리고_커밋을_끝까지_간다()
    {
        // 못 하는 행동은 안 누른 것과 같다(CanStart) — 되받아치기도 같은 규칙이다. 값이 모자라 못 나간 J 가
        // 커밋을 풀거나 음수 값으로 1타를 세우면 받아친 사람이 공짜로 칼을 얻는다.
        FighterConfig c = TestConfigs.Fighter();
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        f.ParryPrecise();
        f.Spend(f.Stamina - (c.AttackCost - 1));   // 1타 값에 1 모자라게 남긴다
        double stamina = f.Stamina;

        for (int tick = 2; tick <= 19; tick++)
        {
            f.Tick(_attack, _dt);

            f.Action.ShouldBe(FighterAction.Parry, $"{tick}틱: 값이 모자란 되받아치기가 섰다");
            f.Stamina.ShouldBe(stamina, 1e-9, $"{tick}틱: 버린 J 가 값을 냈다");
        }

        f.Tick(_attack, _dt);   // 20틱 — 커밋이 끝나는 틱이다
        f.Action.ShouldBe(FighterAction.Idle, "버린 J 가 커밋의 길이를 바꿨다");
    }

    [Fact]
    public void 받아친_것은_그_패리의_것이라_다음_패리로_안_넘어간다()
    {
        // 되받아치기의 조건은 **이번** 패리가 받아쳤나다. 새 행동이 시작될 때 그 표시를 안 지우면, 한 번 받아친 뒤로는
        // 헛친 패리도 J 를 받는다 — 난사가 커밋의 값을 안 낸다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        f.ParryPrecise();
        Idle(f, 19);   // 받아친 채 J 없이 커밋이 끝난다
        f.Action.ShouldBe(FighterAction.Idle, "커밋이 안 끝났다 — 이 테스트가 다음 패리를 못 누른다");

        f.Tick(_parry, _dt);   // 새 패리 — 이번에는 아무것도 안 받아친다
        f.Tick(_attack, _dt);

        f.Action.ShouldBe(FighterAction.Parry, "앞 패리의 받아침이 이번 패리로 넘어와 J 를 받았다");
    }

    [Fact]
    public void 패리는_누를_때_값을_낸다()
    {
        Fighter f = Spawn();
        f.Tick(_parry, _dt);

        f.Stamina.ShouldBe(100 - 15, 1e-9);
    }

    [Fact]
    public void 연달아_눌러도_창이_좁아지지_않는다()
    {
        // 연타 징벌은 걷었다 (설계 §5.3). 난사는 커밋이 이미 벌한다 — 여기서 보는 것은 **벌이 두 번 오지 않는** 것이다:
        // 커밋이 끝나자마자 다시 누른 패리도 온전한 창을 가진다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        Idle(f, 19);
        f.Action.ShouldBe(FighterAction.Idle, "커밋이 안 끝났다 — 이 테스트가 두 번째 누름을 못 한다");

        f.Tick(_parry, _dt);
        Idle(f, 6);   // 7틱 = 0.117초 — 창 안

        f.Parrying.ShouldBeTrue("두 번째 누름의 창이 좁아졌다 — 연타 징벌이 남아 있다");
    }

    [Fact]
    public void 커밋_중의_패리는_버리고_끝난_뒤의_패리는_선다()
    {
        // Review Focus 4. 2타(1초 커밋) 도중 K 는 버린다 — 기억해 뒀다 끝나자마자 세우면 사람이 누른 시각과 창이
        // 어긋난다. 2타가 끝난 뒤 누른 K 는 곧장 패리다.
        Fighter f = Spawn();
        f.Tick(_attack, _dt);
        f.Tick(_attack, _dt);   // 1타 도중 — 2타를 눌러 둔다
        while (f.ComboStep == 0 && f.Action == FighterAction.Attack)
        {
            f.Tick(default, _dt);
        }

        f.ComboStep.ShouldBe(1, "2타가 안 이어졌다 — 이 테스트가 2타 커밋을 안 본다");

        f.Tick(_parry, _dt);
        f.Action.ShouldBe(FighterAction.Attack, "2타 도중에 패리가 섰다 — 칼질은 끝까지 커밋이다");

        while (f.Action == FighterAction.Attack)
        {
            f.Tick(default, _dt);
        }

        f.Tick(_parry, _dt);
        f.Action.ShouldBe(FighterAction.Parry, "2타가 끝난 뒤 누른 패리가 안 섰다");
        f.Parrying.ShouldBeTrue();
    }

    // ── 가드 (설계 §5.2) ─────────────────────────────────────────────────────

    /// <summary>↓ 를 누른 채 한 틱 — 가드로 선 파이터.</summary>
    private static Fighter Guarding()
    {
        Fighter f = Spawn();
        f.Tick(_guard, _dt);
        f.Guarding.ShouldBeTrue("가드가 안 섰다 — 아래 테스트들이 전부 다른 갈래를 본다");
        return f;
    }

    [Fact]
    public void 방어_비용은_피해에_비례한다()
    {
        // **무거운 한 방이 가드를 깨는 것이 가드 퍼니쉬의 레버다.** 정액이면 연타든 마무리든
        // 같은 값이라 "무엇을 가드할까" 라는 판단이 통째로 사라진다.
        Fighter f = Spawn();

        f.GuardStaminaCost(10).ShouldBe(18, 1e-9);
        f.GuardStaminaCost(26).ShouldBe(46.8, 1e-9);
    }

    [Fact]
    public void 방어는_피해의_일부만_받고_값을_스태미나로_낸다()
    {
        Fighter f = Guarding();
        double stamina = f.Stamina;

        f.GuardChip(fullDamage: 20);

        f.Health.ShouldBe(100 - 5, "20 의 0.25 는 5 다");
        f.Stamina.ShouldBe(stamina - 36, 1e-9, "20 × 1.8 = 36");
        f.Action.ShouldBe(FighterAction.Guard, "받아낸 가드가 저절로 풀렸다");
    }

    [Fact]
    public void 가드가_깨지면_전액을_맞고_굳는다()
    {
        Fighter f = Guarding();
        double stamina = f.Stamina;

        f.GuardBreak(fullDamage: 20);

        f.Health.ShouldBe(100 - 20, "깨진 가드는 전액이다");
        f.Stamina.ShouldBe(stamina, 1e-9, "막은 것이 없는데 값을 냈다");
        f.Locked.ShouldBeTrue();
        f.Action.ShouldBe(FighterAction.Idle);
        f.Guarding.ShouldBeFalse();

        // 굳은 동안에는 아무것도 못 한다 — 그게 붕괴의 값이다. (부정확 패리가 지던 이 검사가
        // 이슈 #53 으로 여기 왔다: 이제 굳는 길은 가드 붕괴 하나뿐이다.)
        f.Tick(_dash, _dt);
        f.Action.ShouldBe(FighterAction.Idle, "굳었는데 대시가 나갔다");
        double x = f.X;
        f.Tick(new InputFrame(1, false, false, false, false), _dt);
        f.X.ShouldBe(x, 1e-9, "굳었는데 걸었다");

        Idle(f, 63);   // 합쳐 1.083초 — 붕괴 고정(1.1 · 이슈 #54 전에는 0.9)이 아직 안 풀렸다
        f.Locked.ShouldBeTrue();
        Idle(f, 2);
        f.Locked.ShouldBeFalse();
        f.Tick(_dash, _dt);
        f.Action.ShouldBe(FighterAction.Dash, "고정이 안 풀렸다");
    }

    [Fact]
    public void 굳은_동안에는_다시_못_막는다()
    {
        // 붕괴의 값은 **남은 타격을 그대로 맞는 길이**다. 곧장 다시 설 수 있으면 그 값이 없다.
        Fighter f = Guarding();
        f.GuardBreak(fullDamage: 20);

        f.Tick(_guard, _dt);
        f.Action.ShouldBe(FighterAction.Idle, "굳었는데 가드가 섰다");
    }

    [Fact]
    public void 가드는_누르고_있는_동안이다()
    {
        // 가드는 ↓ 를 누르고 있는 동안이다 — 시간이 끝내지 않고 손가락이 끝낸다. 5초를 버텨도 그대로다.
        Fighter f = Guarding();
        for (int i = 0; i < 300; i++)
        {
            f.Tick(_guard, _dt);
        }

        f.Action.ShouldBe(FighterAction.Guard, "5초가 가드를 끝냈다 — 끝내는 것은 손가락이어야 한다");

        f.Tick(default, _dt);
        f.Action.ShouldBe(FighterAction.Idle, "놓았는데 가드가 남았다");
    }

    [Fact]
    public void 가드를_드는_값은_없다()
    {
        // 이 계획이 정한 것 1 — 스펙은 칩과 피해 비례 스태미나만 적었다. 방패를 드는 것은 공짜고 막는 것이 값이다.
        Guarding().Stamina.ShouldBe(100, 1e-9);
    }

    [Fact]
    public void 가드는_땅에서만_선다()
    {
        Fighter f = Spawn();
        f.Tick(new InputFrame(0, Jump: true, false, false, false), _dt);
        f.Tick(_guard, _dt);

        f.Grounded.ShouldBeFalse("아직 공중이어야 이 테스트가 공중 가드를 본다");
        f.Guarding.ShouldBeFalse("공중에서 가드가 섰다");
    }

    [Fact]
    public void 공중에서_누른_가드는_착지하면_선다()
    {
        // Review Focus 3 — 공중이라 무시했던 ↓ 가 영영 죽지 않는다. 누르고 있는 동안이 가드이므로 땅에 닿은 다음 틱부터다
        // (Begin 이 Fall 보다 먼저라, 착지한 틱의 Begin 은 아직 공중을 본다).
        Fighter f = Spawn();
        f.Tick(new InputFrame(0, Jump: true, false, false, false), _dt);
        for (int i = 0; i < 600 && !f.Grounded; i++)
        {
            f.Tick(_guard, _dt);
        }

        f.Tick(_guard, _dt);
        f.Guarding.ShouldBeTrue("착지했는데 누르고 있던 가드가 안 섰다");
    }

    [Fact]
    public void 가드_중에는_못_움직이고_못_뛴다()
    {
        // 가드와 간격이 **배타적**이어야 둘 중 하나를 고르는 것이 판단이 된다.
        Fighter f = Guarding();
        double x = f.X;

        f.Tick(new InputFrame(1, Jump: true, false, false, false, GuardHeld: true), _dt);

        f.X.ShouldBe(x, 1e-9, "가드 중에 걸었다");
        f.Grounded.ShouldBeTrue("가드 중에 뛰었다");
        f.Action.ShouldBe(FighterAction.Guard);
    }

    [Fact]
    public void 가드_중에는_스태미나가_안_찬다()
    {
        // 회복은 Idle 일 때만 돈다 (설계 §5.2). 가드 중에 차면 버티는 것에 값이 없어진다.
        Fighter f = Guarding();
        f.Spend(40);
        double low = f.Stamina;

        for (int i = 0; i < 60; i++)
        {
            f.Tick(_guard, _dt);
        }

        f.Stamina.ShouldBe(low, 1e-9);
    }

    [Fact]
    public void 가드에서_바로_패리_공격_대시로_넘어간다()
    {
        // 설계 §5.2 — 막고 있다가 받아치고 치는 것이 이 게임의 고리라, 가드를 내리는 틱이 따로 없다.
        foreach ((InputFrame press, FighterAction then) in new[]
        {
            (new InputFrame(0, false, false, Parry: true, false, GuardHeld: true), FighterAction.Parry),
            (new InputFrame(0, false, false, false, Attack: true, GuardHeld: true), FighterAction.Attack),
            (new InputFrame(0, false, Dash: true, false, false, GuardHeld: true), FighterAction.Dash),
        })
        {
            Fighter f = Guarding();
            f.Tick(press, _dt);
            f.Action.ShouldBe(then, $"가드에서 {then} 로 바로 못 넘어갔다");
        }
    }

    [Fact]
    public void 행동이_끝나도_누르고_있으면_다시_가드다()
    {
        // Review Focus 1 — 사람은 ↓ 를 뗀 적이 없다. 패리 · 칼질 · 대시 중 어느 커밋이 끝나도 곧장 다시 막고 있어야 한다
        // (이 계획이 정한 것 2). 셋이 같은 길(다음 틱의 Begin)을 타지만, 패리 하나만 보던 때는 칼질이나 대시가 끝난 뒤
        // ↓ 를 다시 눌러야 서게 바꿔도 초록이었다(최종 리뷰 m3).
        foreach ((InputFrame press, FighterAction action) in new[]
        {
            (new InputFrame(0, false, false, Parry: true, false, GuardHeld: true), FighterAction.Parry),
            (new InputFrame(0, false, false, false, Attack: true, GuardHeld: true), FighterAction.Attack),
            (new InputFrame(0, false, Dash: true, false, false, GuardHeld: true), FighterAction.Dash),
        })
        {
            Fighter f = Guarding();
            f.Tick(press, _dt);
            f.Action.ShouldBe(action, $"가드에서 {action} 이(가) 안 섰다 — 이 테스트가 그 끝을 안 본다");
            for (int i = 0; i < 120 && f.Action == action; i++)
            {
                f.Tick(_guard, _dt);
            }

            f.Tick(_guard, _dt);
            f.Guarding.ShouldBeTrue($"{action} 이(가) 끝났는데 누르고 있던 가드가 안 돌아왔다");
        }
    }

    [Fact]
    public void 못_하는_행동은_가드를_안_내린다()
    {
        // 스태미나가 모자라 대시가 안 나가면 대시를 누른 것은 없던 일이다 — 가드는 그 틱에도 그대로 막고 있어야 한다.
        Fighter f = Guarding();
        f.Spend(90);   // 10 남는다. 대시는 25

        f.Tick(new InputFrame(0, false, Dash: true, false, false, GuardHeld: true), _dt);

        f.Action.ShouldBe(FighterAction.Guard, "못 나간 대시가 가드를 내렸다");
    }

    [Fact]
    public void 굳음이_풀리면_누르고_있던_가드가_선다()
    {
        // 가드는 누르고 있는 동안이라(이 계획이 정한 것 2) 붕괴 고정이 풀리는 틱부터 다시 선다 — 고정(1.1초)이
        // 붕괴의 값이고, 그 뒤까지 손을 떼고 다시 누르게 하는 것은 값이 아니라 조작의 마찰이다.
        // (이슈 #53 은 반대를 못박았다 — 그때 가드는 K 의 엣지로 섰다.)
        Fighter f = Guarding();
        f.GuardBreak(fullDamage: 20);
        for (int i = 0; i < 90; i++)
        {
            f.Tick(_guard, _dt);
        }

        f.Locked.ShouldBeFalse("고정이 안 풀렸다 — 아래 단언이 다른 것을 본다");
        f.Guarding.ShouldBeTrue("고정이 풀렸는데 누르고 있던 가드가 안 섰다");
    }
}
