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

    /// <summary>패리 키를 <b>누르는 순간</b>. 엣지와 누름 유지가 같이 참이다 — 사람이 누르면 늘 이 모양이다.</summary>
    private static readonly InputFrame _parryPress = new(0, false, false, true, false, ParryHeld: true);

    /// <summary>패리 키를 <b>누르고 있는</b> 틱. 엣지는 이미 지났다.</summary>
    private static readonly InputFrame _parryHold = new(0, false, false, false, false, ParryHeld: true);

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
    public void 자세는_손가락이_끝내고_창은_시간이_끝낸다()
    {
        // **이 둘의 차이가 패리와 가드를 가른다** (이슈 #53). 누르면 그 틱부터 자세이고,
        // 창은 그 안에서 혼자 닫힌다 — 창이 닫힌 뒤에도 자세는 그대로라 거기 오는 판정은
        // 가드가 받는다. 그것이 "실패한 패리도 막는다" 의 전부다.
        Fighter f = Spawn();
        f.Tick(_parryPress, _dt);
        f.Guarding.ShouldBeTrue("누르는 그 틱부터 막고 있어야 한다 — 0.30초를 기다리면 안 된다");
        f.Parrying.ShouldBeTrue();

        for (int i = 0; i < 8; i++)
        {
            f.Tick(_parryHold, _dt);
        }

        f.Parrying.ShouldBeFalse("창(0.133초)이 안 닫혔다 — 그러면 패리에 실패가 없다");
        f.Guarding.ShouldBeTrue("창이 닫히면서 자세까지 풀렸다 — 늦은 패리도 막아야 한다");
    }

    // ── 연타 징벌 (이슈 #27) ─────────────────────────────────────────────────

    /// <summary><paramref name="ticks"/> 틱 동안 아무것도 안 하고 흘려보낸다.</summary>
    private static void Idle(Fighter f, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            f.Tick(default, _dt);
        }
    }

    [Fact]
    public void 누름_시계는_손을_뗀_뒤에도_계속_돈다()
    {
        // 자세가 풀렸다고 시계가 멈추면 연타 사슬이 안 서고, 계측이 "눌렀다 놓쳤다" 를
        // "아무것도 안 했다" 와 같은 점으로 적는다 — 그 둘을 가르는 것이 이 시계 하나다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        Idle(f, 24);   // 0.4167초 — 자세는 첫 틱에 풀렸고 기억 창(0.5)은 아직이다

        f.Action.ShouldBe(FighterAction.Idle);
        f.SinceParryPress.ShouldBe(25 * _dt, 1e-9);
        f.SinceParryPress.ShouldBeLessThan(f.ParryMemoryWindow);
    }

    [Fact]
    public void 연타하면_정확_창이_좁아지다_사라진다()
    {
        // 공격이 안 오는데 난사하면 규칙이 벌을 준다. 이건 감각만이 아니라 **학습 데이터 품질**이다 —
        // 매 틱 회피를 고르는 봇이 난사로 공짜 성능을 얻으면 그 데이터는 사람의 판단을 안 담는다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        f.PreciseParryWindow.ShouldBe(0.133, 1e-9, "첫 누름은 온전한 정확 창이다");

        // 0.30초 뒤에 다시 누른다 — 앞 누름의 기억 창(0.5초) 안이라 연타다.
        Idle(f, 18);
        f.Tick(_parry, _dt);
        f.ParryChain.ShouldBe(2);
        f.PreciseParryWindow.ShouldBe(0.1, 1e-9, "두 번째 연타인데 창이 안 좁아졌다");

        Idle(f, 18);
        f.Tick(_parry, _dt);
        f.ParryChain.ShouldBe(3);
        f.PreciseParryWindow.ShouldBe(0, "세 번째부터는 패리 창이 아예 없어야 한다");
    }

    [Fact]
    public void 사이를_비우면_연타가_아니다()
    {
        // 징벌의 기준은 "얼마나 많이 눌렀나" 가 아니라 "앞 누름이 아직 살아 있는데 또 눌렀나" 다.
        // 노리고 누르는 사람은 아무리 여러 번 눌러도 벌을 안 받아야 한다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        Idle(f, 40);   // 0.667초 — 기억 창(0.5)이 이미 닫혔다
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
        FighterConfig c = TestConfigs.Fighter();
        Fighter f = Spawn();
        f.Spend(c.MaxStamina - c.AttackCost - 1);   // 1타 값 + 1 만 남긴다
        f.Tick(_attack, _dt);
        f.Tick(_attack, _dt);
        while (f.Action == FighterAction.Attack)
        {
            f.Tick(default, _dt);
        }

        f.ComboStep.ShouldBe(0, "값이 모자라는데 2타가 섰다");
        f.Stamina.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void 칼질_중에는_다른_것을_못_한다()
    {
        // 끝까지 커밋 (설계 §5.1). 2타는 1초짜리라 그 사이 무엇도 못 하는 것이 2타의 값이다.
        Fighter f = Spawn();
        f.Tick(_attack, _dt);
        double x = f.X, stamina = f.Stamina;

        f.Tick(new InputFrame(1, Jump: true, Dash: true, Parry: true, Attack: false), _dt);

        f.Action.ShouldBe(FighterAction.Attack);
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

    // ── 방어 자세 (이슈 #47 · #53) ───────────────────────────────────────────

    /// <summary>방어 자세로 서 있는 파이터. 누르는 그 틱에 선다 (이슈 #53).</summary>
    private static Fighter Guarding()
    {
        Fighter f = Spawn();
        f.Tick(_parryPress, _dt);
        f.Guarding.ShouldBeTrue("자세가 안 섰다 — 아래 테스트들이 전부 다른 갈래를 본다");
        return f;
    }

    [Fact]
    public void 누르는_그_틱부터_방어다()
    {
        // **이 이슈가 없앤 것이 여기 있다** (이슈 #53). 전에는 누름이 0.30초짜리 패리 행동을
        // 세우고 가드는 그 뒤에 붙었다 — 그 0.30초 동안 늦게 지른 패리는 아무것도 안 막았고,
        // 유저가 겪은 것은 "방어를 골랐는데 왜 안 막나" 였다.
        //
        // 그러면서도 **패리를 느리게 만들지 않는 것은 그대로 계약이다**: 붙들었는지 보고
        // 시작하면 창(0.133초)이 통째로 밀린다. 그래서 누른 첫 틱의 상태는 탭과 한 값도 다르면 안 된다.
        Fighter held = Spawn();
        held.Tick(_parryPress, _dt);
        Fighter tapped = Spawn();
        tapped.Tick(_parry, _dt);

        held.Action.ShouldBe(FighterAction.Guard);
        held.Parrying.ShouldBeTrue();
        held.PreciseParryWindow.ShouldBe(tapped.PreciseParryWindow, 1e-9);
        held.SinceParryPress.ShouldBe(tapped.SinceParryPress, 1e-9);
        held.Stamina.ShouldBe(tapped.Stamina, 1e-9, "붙들었다고 값이 더 들면 그건 다른 기술이다");
        tapped.Guarding.ShouldBeTrue("탭도 그 틱에는 막고 있다 — 갈리는 것은 다음 틱이다");
    }

    [Fact]
    public void 놓으면_그_틱에_풀린다()
    {
        // 탭은 **한 틱짜리 자세**다. 그래도 그 누름의 창은 끝까지 흐르므로(위 테스트) 탭 패리는
        // 여전히 받아친다 — 자세와 창이 서로 다른 시계를 타는 것이 이 설계의 요점이다.
        Fighter f = Spawn();
        f.Tick(_parry, _dt);
        f.Guarding.ShouldBeTrue();

        f.Tick(default, _dt);

        f.Action.ShouldBe(FighterAction.Idle);
        f.Guarding.ShouldBeFalse();
        f.Parrying.ShouldBeTrue("놓았다고 창까지 닫혔다 — 창은 자세가 아니라 누름에 붙는다");
    }

    [Fact]
    public void 자세는_시간이_아니라_손가락이_끝낸다()
    {
        Fighter f = Guarding();
        for (int i = 0; i < 300; i++)
        {
            f.Tick(_parryHold, _dt);
        }

        f.Action.ShouldBe(FighterAction.Guard, "5초가 자세를 끝냈다 — 끝내는 것은 손가락이어야 한다");

        f.Tick(default, _dt);
        f.Action.ShouldBe(FighterAction.Idle);
        f.Guarding.ShouldBeFalse();
    }

    [Fact]
    public void 방어_중에는_못_움직이고_못_뛴다()
    {
        // 방어와 간격이 **배타적**이어야 둘 중 하나를 고르는 것이 판단이 된다.
        Fighter f = Guarding();
        double x = f.X;

        f.Tick(new InputFrame(1, Jump: true, false, false, false, ParryHeld: true), _dt);

        f.X.ShouldBe(x, 1e-9, "방어 중에 걸었다");
        f.Grounded.ShouldBeTrue("방어 중에 뛰었다");
        f.Action.ShouldBe(FighterAction.Guard);
    }

    [Fact]
    public void 방어_중에는_스태미나가_안_찬다()
    {
        // 회복은 Idle 일 때만 돈다. 방어 중에 차면 버티는 것에 값이 없어져
        // "계속 들고 있기" 가 언제나 최선이 되고, 그러면 방어에 판단이 사라진다.
        Fighter f = Guarding();
        f.Spend(40);
        double low = f.Stamina;

        for (int i = 0; i < 60; i++)
        {
            f.Tick(_parryHold, _dt);
        }

        f.Stamina.ShouldBe(low, 1e-9);
    }

    [Fact]
    public void 자세_안에서_창이_흐른다()
    {
        // ⚠ **이슈 #47 은 정확히 반대를 못박아 뒀다** — 가드에 들어가는 순간 누름 시계를
        // 무한대로 끝냈다. 그때는 그래야 했다: 가드가 패리 **뒤에** 서서 두 창이 겹쳤고,
        // 겹친 채로 두면 가드 불가 판정이 늦은 패리로 먹혀 "가드로는 못 막는다" 가
        // 한 번도 안 일어났다.
        //
        // 지금은 겹치는 것이 **설계**다 (이슈 #53). 하나의 자세 안에서 창이 흐르고,
        // 그 창의 안팎이 패리와 가드를 가른다 — 여기서 시계를 끊으면 패리가 통째로 사라진다.
        Fighter f = Guarding();

        f.SinceParryPress.ShouldBe(_dt, 1e-9);
        f.Parrying.ShouldBeTrue();

        for (int i = 0; i < 8; i++)
        {
            f.Tick(_parryHold, _dt);
        }

        f.SinceParryPress.ShouldBe(9 * _dt, 1e-9, "자세 안에서 시계가 멈췄다");
        f.Parrying.ShouldBeFalse();
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
    public void 굳는_동안_붙들고만_있으면_자세가_다시_안_선다()
    {
        // 자세를 세우는 것은 **엣지**다 (InputFrame 의 계약). 붕괴로 굳은 동안 계속 누르고
        // 있었다면 그 엣지는 이미 지났으므로, 고정이 풀려도 자세는 저절로 안 돌아온다 —
        // 다시 눌러야 한다. 그게 맞다: 무너진 방어가 손을 안 뗐다는 이유로 저절로 서면
        // guard_break_lock 이 무는 것이 없다.
        Fighter f = Guarding();
        f.GuardBreak(fullDamage: 20);

        for (int i = 0; i < 90; i++)
        {
            f.Tick(_parryHold, _dt);
        }

        f.Locked.ShouldBeFalse("고정이 안 풀렸다 — 아래 단언이 다른 것을 본다");
        f.Guarding.ShouldBeFalse("붙들고만 있었는데 자세가 다시 섰다");

        f.Tick(_parryPress, _dt);
        f.Guarding.ShouldBeTrue("다시 눌렀는데 자세가 안 섰다");
    }

    [Fact]
    public void 굳은_동안에는_다시_못_막는다()
    {
        // 붕괴의 값은 **남은 타격을 그대로 맞는 길이**다. 곧장 다시 설 수 있으면 그 값이 없다.
        Fighter f = Guarding();
        f.GuardBreak(fullDamage: 20);

        f.Tick(_parryPress, _dt);
        f.Action.ShouldBe(FighterAction.Idle, "굳었는데 방어 자세가 섰다");
    }

}
