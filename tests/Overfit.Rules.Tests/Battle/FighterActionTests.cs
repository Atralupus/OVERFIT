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

    /// <summary>공격 키를 <b>누르는 순간</b>. 엣지와 누름 유지가 같이 참이다 — 사람이 누르면 늘 이 모양이다.</summary>
    private static readonly InputFrame _attackPress = new(0, false, false, false, true, AttackHeld: true);

    /// <summary>공격 키를 <b>누르고 있는</b> 틱. 엣지는 이미 지났다.</summary>
    private static readonly InputFrame _attackHold = new(0, false, false, false, false, AttackHeld: true);

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
        Fighter f = Spawn();
        f.Tick(_dash, _dt);
        f.Tick(_parry, _dt);

        f.Action.ShouldBe(FighterAction.Dash);
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

    [Fact]
    public void 패리_창은_지속보다_짧다()
    {
        // 패리는 늦게 풀리지만 막아주는 것은 앞부분뿐이다 — 실패가 비싸야 의존도가 축이 된다
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        f.Parrying.ShouldBeTrue();

        for (int i = 0; i < 8; i++)
        {
            f.Tick(default, _dt);
        }

        f.Action.ShouldBe(FighterAction.Parry);
        f.Parrying.ShouldBeFalse();
    }

    // ── 2단계 패리 (이슈 #27) ────────────────────────────────────────────────

    /// <summary><paramref name="ticks"/> 틱 동안 아무것도 안 하고 흘려보낸다.</summary>
    private static void Idle(Fighter f, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            f.Tick(default, _dt);
        }
    }

    [Fact]
    public void 패리_시계는_행동이_끝나도_계속_돈다()
    {
        // 부정확 창(0.5초)이 패리 행동(0.30초)보다 길다. 행동과 같은 시계를 쓰면 행동이 끝나는
        // 순간 시계가 0 으로 돌아가고, 그 뒤에 오는 판정은 "아무것도 안 했다" 와 같은 점이 된다 —
        // 그 붕괴를 없애려고 만든 것이 부정확 단계라 여기서 못박는다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        Idle(f, 24);   // 0.4167초 — 행동(0.30)은 끝났고 부정확 창(0.5)은 아직이다

        f.Action.ShouldBe(FighterAction.Idle);
        f.SinceParryPress.ShouldBe(25 * _dt, 1e-9);
        f.SinceParryPress.ShouldBeLessThan(f.ImpreciseParryWindow);
    }

    [Fact]
    public void 연타하면_정확_창이_좁아지다_사라진다()
    {
        // 공격이 안 오는데 난사하면 규칙이 벌을 준다. 이건 감각만이 아니라 **학습 데이터 품질**이다 —
        // 매 틱 회피를 고르는 봇이 난사로 공짜 성능을 얻으면 그 데이터는 사람의 판단을 안 담는다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        f.PreciseParryWindow.ShouldBe(0.133, 1e-9, "첫 누름은 온전한 정확 창이다");

        // 행동이 끝나자마자(0.30초) 다시 누른다 — 앞 누름의 부정확 창(0.5초) 안이라 연타다.
        Idle(f, 18);
        f.Tick(_parry, _dt);
        f.ParryChain.ShouldBe(2);
        f.PreciseParryWindow.ShouldBe(0.1, 1e-9, "두 번째 연타인데 창이 안 좁아졌다");

        Idle(f, 18);
        f.Tick(_parry, _dt);
        f.ParryChain.ShouldBe(3);
        f.PreciseParryWindow.ShouldBe(0, "세 번째부터는 정확 패리가 아예 없어야 한다");
    }

    [Fact]
    public void 사이를_비우면_연타가_아니다()
    {
        // 징벌의 기준은 "얼마나 많이 눌렀나" 가 아니라 "앞 누름이 아직 살아 있는데 또 눌렀나" 다.
        // 노리고 누르는 사람은 아무리 여러 번 눌러도 벌을 안 받아야 한다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        Idle(f, 40);   // 0.667초 — 부정확 창(0.5)이 이미 닫혔다
        f.Tick(_parry, _dt);

        f.ParryChain.ShouldBe(1);
        f.PreciseParryWindow.ShouldBe(0.133, 1e-9);
    }

    [Fact]
    public void 받아낸_패리는_연타로_안_센다()
    {
        // 파이터는 보스를 모르므로 "공격이 안 오는데 눌렀나" 를 직접 못 본다. 대신 받아낸 것이
        // 있으면 사슬이 풀린다 — 허공에 연달아 누른 것만 벌을 받는 규칙이 그렇게 선다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        f.ParryPrecise();
        Idle(f, 18);
        f.Tick(_parry, _dt);

        f.ParryChain.ShouldBe(1);
        f.PreciseParryWindow.ShouldBe(0.133, 1e-9);
    }

    [Fact]
    public void 부정확_패리는_절반을_내상으로_받고_지상에서_굳는다()
    {
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        f.ParryImprecise(fullDamage: 21);

        f.Health.ShouldBe(100 - 11, "21 의 절반은 반올림해 11 이다");
        f.InternalDamage.ShouldBe(11);
        f.Qi.ShouldBe(1);
        f.Locked.ShouldBeTrue();
        f.Action.ShouldBe(FighterAction.Idle, "굳으면 돌던 패리도 끊긴다");

        // 굳은 동안에는 아무것도 못 한다 — 그게 이 단계의 값이다.
        f.Tick(_dash, _dt);
        f.Action.ShouldBe(FighterAction.Idle);
        f.Tick(new InputFrame(1, false, false, false, false), _dt);
        f.X.ShouldBe(960, 1e-9, "굳었는데 걸었다");

        Idle(f, 36);   // 0.6초
        f.Locked.ShouldBeFalse();
        f.Tick(_dash, _dt);
        f.Action.ShouldBe(FighterAction.Dash, "고정이 안 풀렸다");
    }

    [Fact]
    public void 공중에서는_부정확_패리에_안_굳는다()
    {
        // 공중에서 굳는 것은 그냥 떨어지는 것과 같아서 벌이 아니라 버그로 읽힌다 (나인 솔즈).
        Fighter f = Spawn();
        f.Tick(new InputFrame(0, true, false, false, false), _dt);
        f.Grounded.ShouldBeFalse();

        f.ParryImprecise(fullDamage: 20);

        f.Locked.ShouldBeFalse();
        f.Health.ShouldBe(90);
    }

    [Fact]
    public void 공중_대시는_한_번뿐이고_정확_패리가_되돌린다()
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
        f.Action.ShouldBe(FighterAction.Dash, "정확 패리가 공중 대시를 안 돌려줬다");
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


    // ── 차지 공격 (이슈 #40) ─────────────────────────────────────────────────

    /// <summary>차지를 <paramref name="ticks"/> 틱 동안 붙들고 있는다.</summary>
    private static void Hold(Fighter f, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            f.Tick(_attackHold, _dt);
        }
    }

    /// <summary><paramref name="tier"/> 단계에 닿을 때까지 붙들고 있는다. <b>틱 수를 세지 않는다</b> —
    /// 단계 시간은 데이터고, 여기서 세면 그 값을 손으로 베낀 사본이 된다.</summary>
    private static void HoldToTier(Fighter f, int tier)
    {
        f.Tick(_attackPress, _dt);
        int guard = 0;
        while (f.ChargeTier < tier && guard++ < 600)
        {
            f.Tick(_attackHold, _dt);
        }
    }

    [Fact]
    public void 그냥_누르면_예전처럼_곧장_휘두른다()
    {
        // 누름 유지가 없는 입력(봇의 탭 · 옛 입력 시퀀스)은 **한 틱도 안 늘어나야 한다** —
        // 늘어나면 지금까지의 모든 리플레이가 한 틱씩 밀린다.
        Fighter f = Spawn();
        f.Tick(_attack, _dt);

        f.Action.ShouldBe(FighterAction.Attack);
        f.Charging.ShouldBeFalse();
        f.ChargeTier.ShouldBe(0);
        f.AttackDamage.ShouldBe(8, "0단계는 배수 1 이다");
    }

    [Fact]
    public void 누르고_있으면_차지에_들어간다()
    {
        Fighter f = Spawn();
        f.Tick(_attackPress, _dt);

        f.Action.ShouldBe(FighterAction.Charge);
        f.Charging.ShouldBeTrue();
        f.AttackActive.ShouldBeFalse("차지 중에는 판정이 없다");
        f.Stamina.ShouldBe(100 - 12, "값은 누를 때 한 번 낸다");
    }

    [Fact]
    public void 차지는_놓을_때까지_안_끝난다()
    {
        // 차지를 끝내는 것은 시간이 아니라 **손가락**이다. 최대에 닿아도 저절로 안 나간다 —
        // 저절로 나가면 "언제 놓을까" 가 사라져 이 기술에 판단이 없어진다.
        Fighter f = Spawn();
        f.Tick(_attackPress, _dt);
        Hold(f, 180);   // 3초 — 최대(1.0초)를 한참 지났다

        f.Action.ShouldBe(FighterAction.Charge);
        f.ChargeTier.ShouldBe(2);
        f.ChargeProgress.ShouldBe(1.0, 1e-9, "진행도는 1 을 안 넘는다");
    }

    [Fact]
    public void 놓으면_모은_만큼의_배수로_휘두른다()
    {
        Fighter f = Spawn();
        HoldToTier(f, 2);
        f.Tick(default, _dt);   // 놓았다

        f.Action.ShouldBe(FighterAction.Attack);
        f.Charging.ShouldBeFalse();
        f.ChargeTier.ShouldBe(2);
        f.AttackDamage.ShouldBe(24, "8 × 3");
    }

    [Fact]
    public void 중간에_놓으면_중간_단계다()
    {
        Fighter f = Spawn();
        HoldToTier(f, 1);
        f.Tick(default, _dt);

        f.Action.ShouldBe(FighterAction.Attack);
        f.ChargeTier.ShouldBe(1);
        f.AttackDamage.ShouldBe(16, "8 × 2");
    }

    [Fact]
    public void 스윙이_끝나면_단계가_0_으로_돌아온다()
    {
        Fighter f = Spawn();
        HoldToTier(f, 2);
        f.Tick(default, _dt);
        Idle(f, 30);   // 공격(0.28초)이 끝나고도 남는다

        f.Action.ShouldBe(FighterAction.Idle);
        f.ChargeTier.ShouldBe(0, "다음 탭이 지난 스윙의 배수를 물려받으면 안 된다");
        f.AttackDamage.ShouldBe(8);
    }

    [Fact]
    public void 붙들고_있는_시간이_곧_선딜이다()
    {
        // **차지는 선딜 앞에 붙는 것이 아니라 선딜 그 자체다** (이슈 #40). 그림이 먼저 그렇게 말하고
        // 있었다 — 차지 자세는 attack 시트의 선딜 마지막 장(칼을 끝까지 뒤로 뺀 그림)이라,
        // 놓은 뒤에 선딜을 처음부터 또 기다리면 화면에서 **같은 동작을 두 번** 감는 셈이다.
        // 그래서 놓는 순간 칼이 곧장 나간다 — 선딜은 붙들고 있는 동안 이미 다 지났다.
        Fighter f = Spawn();
        HoldToTier(f, 2);

        f.Tick(default, _dt);   // 놓았다

        f.Action.ShouldBe(FighterAction.Attack);
        f.AttackActive.ShouldBeTrue("붙들고 있었는데 선딜을 또 기다린다");
    }

    [Fact]
    public void 짧게_붙들면_남은_선딜만큼만_기다린다()
    {
        // 경계가 계단이 아니라 연속이어야 한다. 선딜(0.08)보다 짧게 붙들었으면 남은 만큼만
        // 더 기다린다 — 안 그러면 "0.07초 붙들기" 가 그냥 누르기보다 느려지는 구멍이 생긴다.
        Fighter f = Spawn();
        f.Tick(_attackPress, _dt);
        f.Tick(_attackHold, _dt);   // 두 틱(0.0333초) 붙들었다 — 선딜 0.08 의 절반쯤
        f.Tick(default, _dt);       // 놓았다

        f.AttackActive.ShouldBeFalse("남은 선딜이 있는데 칼이 나갔다");

        f.Tick(default, _dt);
        f.Tick(default, _dt);       // 0.05초 — 남은 선딜(0.0467)을 지났다
        f.AttackActive.ShouldBeTrue("남은 선딜보다 오래 기다렸다");
    }

    [Fact]
    public void 붙들어도_그냥_누른_것보다_빨라지지_않는다()
    {
        // 위 둘의 당연한 따름이지만 못박아 둔다. 칼이 닿기까지는 **max(붙든 시간, 선딜) + 판정**이라
        // 붙드는 것으로 공짜 속도를 얻을 수 없다 — 얻을 수 있으면 아무도 그냥 안 누른다.
        Fighter tap = Spawn();
        tap.Tick(_attack, _dt);
        int tapTicks = 1;
        while (!tap.AttackActive)
        {
            tap.Tick(default, _dt);
            tapTicks++;
        }

        Fighter held = Spawn();
        held.Tick(_attackPress, _dt);
        int heldTicks = 1;
        for (int i = 0; i < 4; i++)   // 0.0667초 — 선딜(0.08)보다 짧게 붙든다
        {
            held.Tick(_attackHold, _dt);
            heldTicks++;
        }

        held.Tick(default, _dt);
        heldTicks++;
        while (!held.AttackActive)
        {
            held.Tick(default, _dt);
            heldTicks++;
        }

        heldTicks.ShouldBeGreaterThanOrEqualTo(tapTicks, "붙드는 것이 그냥 누르는 것보다 빠르다");
    }

    [Fact]
    public void 차지_중에는_움직이지도_뛰지도_못한다()
    {
        // 차지의 값은 **아무것도 못 한다는 것**이다. 걸으면서 모을 수 있으면 위험이 없고,
        // 위험이 없으면 greed 축이 재는 것이 사라진다.
        Fighter f = Spawn();
        f.Tick(_attackPress, _dt);
        double x = f.X;

        f.Tick(new InputFrame(1, Jump: true, false, false, false, AttackHeld: true), _dt);

        f.X.ShouldBe(x, 1e-9, "차지 중에 걸었다");
        f.Grounded.ShouldBeTrue("차지 중에 뛰었다");
        f.Action.ShouldBe(FighterAction.Charge);
    }

    [Fact]
    public void 차지_중에_맞으면_모은_것이_전부_날아간다()
    {
        // 끊기 대신 유지를 고르면 "패턴 위에 겹쳐 모으는 것" 이 가장 좋은 수가 되고,
        // 그러면 보스의 패턴이 이 기술의 판단에서 통째로 빠진다. 값은 이미 냈으므로 돌려받지도 않는다.
        Fighter f = Spawn();
        HoldToTier(f, 2);
        double paid = f.Stamina;

        f.TakeDamage(9);

        f.Action.ShouldBe(FighterAction.Idle);
        f.Charging.ShouldBeFalse();
        f.ChargeTier.ShouldBe(0);
        f.Stamina.ShouldBe(paid, 1e-9, "끊겼다고 스태미나를 돌려주지 않는다");
    }

    [Fact]
    public void 차지_중에는_스태미나가_안_찬다()
    {
        // 차지의 진짜 값이 여기 있다 — 회복은 Idle 일 때만 도므로 2초를 모으는 것은
        // 그동안의 회복(40/s)을 통째로 포기하는 것이다. 그래서 차지에 값을 더 안 매긴다.
        Fighter f = Spawn();
        f.Tick(_attackPress, _dt);
        double low = f.Stamina;
        Hold(f, 60);

        f.Stamina.ShouldBe(low, 1e-9);
    }

    [Fact]
    public void 누름_유지만으로는_차지가_안_시작된다()
    {
        // 차지는 **엣지**에서만 시작한다. 레벨만 보고 시작하면 앞 스윙이 끝나는 순간
        // 키를 놓지 않은 손가락이 저절로 다음 차지를 물고, 그건 누른 적 없는 입력이다.
        Fighter f = Spawn();
        f.Tick(_attackHold, _dt);

        f.Action.ShouldBe(FighterAction.Idle);
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
}
