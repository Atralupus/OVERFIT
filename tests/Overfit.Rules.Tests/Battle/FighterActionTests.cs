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
