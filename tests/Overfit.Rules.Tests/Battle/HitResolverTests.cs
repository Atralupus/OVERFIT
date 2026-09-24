using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class HitResolverTests
{
    private const double _dt = 1.0 / 60.0;
    private const double _bossX = 960;

    private static Fighter Spawn(double x) => new(TestConfigs.Fighter(), TestConfigs.Arena(), x);

    /// <summary>
    /// 기본 태그. 두 창은 파이터의 것보다 <b>넓게</b> 둔다 (대시 0.18 &gt; 0.14 · 패리 0.12 = 0.12) —
    /// 그래야 창을 따로 주지 않은 테스트는 전부 파이터 쪽이 결정한다.
    /// </summary>
    private static PatternTags Tags(bool parryable, double? dashWindow = null, double? parryWindow = null) => new()
    {
        DashWindow = dashWindow ?? 0.18,
        DashDirection = "out",
        Jumpable = false,
        AntiAir = false,
        Parryable = parryable,
        ParryWindow = parryWindow ?? (parryable ? 0.12 : 0),
        PunishGreed = false,
        Reach = "mid",
        Feint = false,
        MultiHit = 1,
        Tracking = false,
        HasGuardBreak = false,
    };

    /// <summary>
    /// <paramref name="ticks"/> 틱째의 파이터. 첫 틱에 <paramref name="start"/> 를 넣고
    /// 그 뒤로는 <b>아무것도 안 누른다</b> — 패리에 쓰면 "눌렀다 곧장 놓았다" 가 된다.
    /// 그래서 방어 자세는 한 틱 만에 풀리고 누름의 창만 남는다 (이슈 #53).
    /// </summary>
    private static Fighter Acting(InputFrame start, int ticks)
    {
        Fighter f = Spawn(_bossX + 100);
        f.Tick(start, _dt);
        for (int i = 1; i < ticks; i++)
        {
            f.Tick(default, _dt);
        }

        return f;
    }

    /// <summary>보스 발밑에서 오른쪽을 본다 — 파이터는 보스 오른쪽에 선다(Spawn(_bossX + …)).</summary>
    private static readonly Placement _at = new(_bossX, 0, 1);

    /// <summary>바닥에서 200 까지, 보스로부터 260 안쪽.</summary>
    private static HitBox Mid() => new(HitShape.Band(0, 260, 0, 200), 18);

    /// <summary>바닥에서 70 까지 — 점프로 넘는다.</summary>
    private static HitBox Low() => new(HitShape.Band(0, 520, 0, 70), 14);

    /// <summary>바닥에서 140 위 — 선 키(120)를 넘으므로 지상이 안전한 대공.</summary>
    private static HitBox High() => new(HitShape.Band(0, 300, 140, 420), 20);

    /// <summary>
    /// 안쪽 190px 이 비어 있는 판정 (II-끌기 의 마무리와 같은 모양 — 그쪽은 290px 이다).
    /// <b>이 박스가 있어야 "너무 가까워서 안 맞았다" 를 물어볼 수 있다</b> — 안쪽이 0 이면
    /// 그 갈래는 값으로 도달할 수 없는 자리라 테스트가 못 선다.
    /// </summary>
    private static HitBox Pocket() => new(HitShape.Band(190, 760, 0, 330), 14);

    private static Fighter Airborne(double x)
    {
        Fighter f = Spawn(x);
        f.Tick(new InputFrame(0, true, false, false, false), _dt);
        for (int i = 0; i < 20; i++)
        {
            f.Tick(default, _dt);
        }

        return f;
    }

    [Fact]
    public void 거리_밖이면_안_맞는다()
    {
        HitResolver.Resolve(Spawn(_bossX + 400), _at, Mid(), Tags(false)).ShouldBe(HitVerdict.MissedTooFar);
    }

    [Fact]
    public void 거리_안이면_맞는다()
    {
        HitResolver.Resolve(Spawn(_bossX + 100), _at, Mid(), Tags(false)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 좌우_어느_쪽이든_같은_거리면_같다()
    {
        HitResolver.Resolve(Spawn(_bossX - 100), _at, Mid(), Tags(false))
            .ShouldBe(HitResolver.Resolve(Spawn(_bossX + 100), _at, Mid(), Tags(false)));
    }

    [Fact]
    public void 대시_무적이면_피한다()
    {
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, true, false, false), _dt);
        f.Invulnerable.ShouldBeTrue();

        HitResolver.Resolve(f, _at, Mid(), Tags(false)).ShouldBe(HitVerdict.Dodged);
    }

    [Fact]
    public void 패리_창_안이고_패리_가능한_패턴이면_받아친다()
    {
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, false, true, false), _dt);
        f.Parrying.ShouldBeTrue();

        HitResolver.Resolve(f, _at, Mid(), Tags(parryable: true)).ShouldBe(HitVerdict.Parried);
    }

    [Fact]
    public void 탭도_창_안이면_받아친다()
    {
        // **창은 자세가 아니라 누름에 붙는다** (이슈 #53). 눌렀다 곧장 놓아도 그 누름의 창은
        // 끝까지 흐르므로 탭 패리는 여전히 받아친다 — 이 줄이 빠지면 "패리하려면 붙들고 있어야
        // 한다" 가 되고, 그건 이 이슈가 없앤 바로 그 지연이 이름만 바꿔 돌아온 것이다.
        Fighter f = Acting(new InputFrame(0, false, false, true, false), 5);

        f.Guarding.ShouldBeFalse("손을 뗐는데 자세가 남아 있다 — 이 테스트가 다른 갈래를 본다");
        HitResolver.Resolve(f, _at, Mid(), Tags(parryable: true)).ShouldBe(HitVerdict.Parried);
    }

    [Fact]
    public void 패리_불가_패턴은_패리해도_맞는다()
    {
        // 눌렀다 **놓은** 뒤라 자세가 없다. 붙들고 있었다면 가드가 받는다(아래 테스트) —
        // 그 둘이 갈리는 것이 이 이슈의 전부다 (이슈 #53).
        Fighter f = Acting(new InputFrame(0, false, false, true, false), 5);

        f.Parrying.ShouldBeTrue("창은 아직 열려 있어야 이 테스트가 parryable 갈래를 본다");
        HitResolver.Resolve(f, _at, Mid(), Tags(parryable: false)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 낮은_판정은_점프로_넘는다()
    {
        Fighter f = Airborne(_bossX + 100);

        f.Y.ShouldBeGreaterThan(70);
        HitResolver.Resolve(f, _at, Low(), Tags(false)).ShouldBe(HitVerdict.MissedByHeight);
    }

    [Fact]
    public void 대공은_지상이_안전하고_공중이_위험하다()
    {
        HitResolver.Resolve(Spawn(_bossX + 100), _at, High(), Tags(false)).ShouldBe(HitVerdict.MissedByHeight);
        HitResolver.Resolve(Airborne(_bossX + 100), _at, High(), Tags(false)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 안_맞은_이유를_거리와_높이로_나눠_말한다()
    {
        // 여기서 이유를 버리면 BattleSim 은 "그 순간 무슨 행동 중이었나" 로 추측할 수밖에 없다.
        // 그 추측이 실제로 틀렸다 — 점프로 넘긴 판정이 같이 눌러둔 패리의 공으로 기록됐다.
        HitResolver.Resolve(Spawn(_bossX + 600), _at, Low(), Tags(false)).ShouldBe(HitVerdict.MissedTooFar);
        HitResolver.Resolve(Airborne(_bossX + 100), _at, Low(), Tags(false)).ShouldBe(HitVerdict.MissedByHeight);
    }

    [Fact]
    public void 거리로_빗나간_것이_안인지_밖인지까지_말한다()
    {
        // 한 갈래(MissedByRange)였을 때는 **파고들어 피한 것과 도망쳐 피한 것이 같은 한 점**이었다.
        // 그 둘은 봉인할 것이 정반대라(안쪽 주머니를 덮는 변종 · 도주로를 덮는 변종),
        // 계측이 못 가르면 2단계가 정반대 변종을 뽑는다.
        HitResolver.Resolve(Spawn(_bossX + 100), _at, Pocket(), Tags(false))
            .ShouldBe(HitVerdict.MissedByGap);
        HitResolver.Resolve(Spawn(_bossX + 900), _at, Pocket(), Tags(false))
            .ShouldBe(HitVerdict.MissedTooFar);

        // 주머니와 사거리 사이는 그냥 맞는다 — 위 둘이 "거리면 무조건 빗나간다" 가 아니라는 증거다.
        HitResolver.Resolve(Spawn(_bossX + 400), _at, Pocket(), Tags(false)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 거리가_먼저_걸리면_높이는_안_본다()
    {
        // 둘 다 어긋났을 때 무엇이라 말하는가. 거리를 먼저 보므로 거리로 답한다 —
        // 순서를 박아두지 않으면 같은 상황이 판마다 다른 라벨을 내 학습 데이터가 흔들린다.
        HitResolver.Resolve(Airborne(_bossX + 600), _at, Low(), Tags(false)).ShouldBe(HitVerdict.MissedTooFar);
    }

    [Fact]
    public void 무적이_패리보다_먼저다()
    {
        // 지금은 행동이 하나뿐이라 겹칠 수 없지만, 순서를 박아두면
        // 나중에 무적 부여 수단이 늘어도 판정이 안 흔들린다.
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, true, false, false), _dt);

        HitResolver.Resolve(f, _at, Mid(), Tags(parryable: true)).ShouldBe(HitVerdict.Dodged);
    }

    // ── 패턴의 창이 실제로 문다 (이슈 #16) ─────────────────────────────────────
    //
    // 전에는 HitResolver 가 파이터의 창만 봤다. 패턴이 parry_window 0.10 을 선언해도
    // 파이터의 0.12 가 그대로 이겨서 **그 숫자는 아무것도 안 했다** — 그런데 망은 그 숫자를 배운다.
    // 거짓말하는 숫자는 없는 숫자보다 나쁘다. 이제 유효 창은 **둘 중 좁은 쪽**이다.

    [Fact]
    public void 패리는_패턴과_파이터_중_좁은_창을_따른다()
    {
        InputFrame parry = new(0, false, false, true, false);

        // 패턴 0.05 < 파이터 0.12. 3틱(0.05)이면 패턴 창은 이미 닫혔고 파이터 창은 열려 있다.
        HitResolver.Resolve(Acting(parry, 1), _at, Mid(), Tags(true, parryWindow: 0.05))
            .ShouldBe(HitVerdict.Parried);

        Fighter late = Acting(parry, 4);
        late.Parrying.ShouldBeTrue("파이터 창은 아직 열려 있어야 이 테스트가 좁은 쪽을 본다");

        // 패턴이 요구하는 정밀도를 못 맞췄다. **놓은 뒤라** 막을 것도 없으니 그냥 맞는다 —
        // 중간 단계(ParriedLate)가 있던 자리이고, 이슈 #53 이 그것을 가드로 바꿨다.
        HitResolver.Resolve(late, _at, Mid(), Tags(true, parryWindow: 0.05)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 좁은_패턴_창을_놓쳐도_붙들고_있으면_막는다()
    {
        // **실패한 패리도 막는다** (이슈 #53). 위 테스트와 유일하게 다른 것은 손을 안 뗐다는 것뿐이고,
        // 그 하나가 "그냥 맞았다" 를 "막았다" 로 바꾼다 — 이 이슈가 요청받은 것이 정확히 이것이다.
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, false, true, false, ParryHeld: true), _dt);
        for (int i = 1; i < 4; i++)
        {
            f.Tick(new InputFrame(0, false, false, false, false, ParryHeld: true), _dt);
        }

        f.Guarding.ShouldBeTrue();
        HitResolver.Resolve(f, _at, Mid(), Tags(true, parryWindow: 0.05)).ShouldBe(HitVerdict.Guarded);
    }

    [Fact]
    public void 패턴_창이_더_넓으면_파이터_창이_이긴다()
    {
        // 좁은 쪽이 이긴다는 것은 양방향이다. 패턴이 넉넉해도 파이터의 창이 닫혔으면
        // 패리가 아니다 — 안 그러면 패턴 태그가 캐릭터 차이를 지워 버린다.
        InputFrame parry = new(0, false, false, true, false);
        Fighter late = Acting(parry, 9);   // 0.15 > 기준 파이터의 창 0.133

        late.Parrying.ShouldBeFalse();
        HitResolver.Resolve(late, _at, Mid(), Tags(true, parryWindow: 0.30)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 대시_무적도_패턴과_파이터_중_좁은_창을_따른다()
    {
        InputFrame dash = new(0, false, true, false, false);

        HitResolver.Resolve(Acting(dash, 1), _at, Mid(), Tags(false, dashWindow: 0.05))
            .ShouldBe(HitVerdict.Dodged);

        Fighter late = Acting(dash, 4);
        late.Invulnerable.ShouldBeTrue("파이터 무적은 아직 돌아야 이 테스트가 좁은 쪽을 본다");
        HitResolver.Resolve(late, _at, Mid(), Tags(false, dashWindow: 0.05)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void Dash_window_0_은_길이가_0_인_창이_아니라_대시_불가다()
    {
        // 두 해석이 값으로는 같은 곳에 떨어지지만 뜻이 다르다. DodgeEvent.DashAvailable 이
        // dash_window > 0 으로 "대시가 가능했나" 를 싣고, 의존도 축의 분모가 그것이다 —
        // 0 을 "아주 짧은 창" 으로 읽으면 그 분모가 거짓이 된다.
        Fighter f = Acting(new InputFrame(0, false, true, false, false), 1);

        f.Invulnerable.ShouldBeTrue();
        HitResolver.Resolve(f, _at, Mid(), Tags(false, dashWindow: 0)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 판정기는_상태를_안_바꾼다()
    {
        Fighter f = Spawn(_bossX + 100);
        int before = f.Health;

        HitResolver.Resolve(f, _at, Mid(), Tags(false));
        HitResolver.Resolve(f, _at, Mid(), Tags(false));

        f.Health.ShouldBe(before);
    }

    // ── 방어 자세 (이슈 #47 · #53) ───────────────────────────────────────────

    /// <summary>
    /// 방어 자세로 서 있고 <b>패리 창은 이미 닫힌</b> 파이터. 창이 열린 채로 두면 아래 테스트가
    /// 전부 <c>Parried</c> 갈래로 떨어진다 — 그건 이 순서가 실제로 그렇게 서 있기 때문이다 (이슈 #53).
    /// 틱 수를 손으로 안 센다: 창의 길이는 데이터다.
    /// </summary>
    private static Fighter Guarding()
    {
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, false, true, false, ParryHeld: true), _dt);
        for (int i = 0; i < 120 && f.Parrying; i++)
        {
            f.Tick(new InputFrame(0, false, false, false, false, ParryHeld: true), _dt);
        }

        f.Guarding.ShouldBeTrue("자세가 안 섰다 — 아래 테스트들이 전부 다른 갈래를 본다");
        f.Parrying.ShouldBeFalse("창이 아직 열려 있다 — 아래 테스트들이 패리 갈래를 본다");
        return f;
    }

    /// <summary><c>guard_break</c> 가 붙은 판정. 마무리 한 대만 이것을 단다 (판정 단위다).</summary>
    private static HitBox Unguardable() => new(HitShape.Band(0, 260, 0, 200), 18, GuardBreak: true);

    [Fact]
    public void 창_밖에서_막고_있으면_깎여서_막는다()
    {
        HitResolver.Resolve(Guarding(), _at, Mid(), Tags(parryable: true)).ShouldBe(HitVerdict.Guarded);
    }

    [Fact]
    public void 패리_불가_패턴도_붙들고_있으면_막는다()
    {
        // 가드는 패리가 아니다. 크림슨(parryable:false)은 "받아치지 마라" 이지 "막지 마라" 가 아니라,
        // 가드 갈래는 그 태그를 안 본다 — 못 막게 하는 것은 판정 쪽의 guard_break 하나뿐이다.
        HitResolver.Resolve(Guarding(), _at, Mid(), Tags(parryable: false)).ShouldBe(HitVerdict.Guarded);
    }

    [Fact]
    public void 스태미나가_모자라면_방어가_깨진다()
    {
        Fighter f = Guarding();
        f.Spend(f.Stamina - 1);   // 1 남는다. Mid() 는 18피해라 32.4 가 든다

        HitResolver.Resolve(f, _at, Mid(), Tags(parryable: true)).ShouldBe(HitVerdict.GuardBroken);
    }

    [Fact]
    public void 가드_불가는_스태미나가_남아도_깨진다()
    {
        Fighter f = Guarding();

        f.Stamina.ShouldBeGreaterThan(f.GuardStaminaCost(Unguardable().Damage),
            "스태미나가 모자라 이 테스트가 고갈 갈래를 본다");
        HitResolver.Resolve(f, _at, Unguardable(), Tags(parryable: true)).ShouldBe(HitVerdict.GuardBroken);
    }

    [Fact]
    public void 가드_불가는_창_안이면_받아친다()
    {
        // ⚠ **이슈 #47 은 반대 순서를 박아 뒀다** — 가드가 패리보다 먼저였고, 그래야 가드 불가가
        // 늦은 패리로 먹히지 않았다. 지금은 갈래가 둘뿐이라 순서가 뒤집혔다 (이슈 #53):
        // 창 안이면 받아치고, 밖이면 깨진다. 그게 빨강이 말하는 "받아쳐라" 다.
        // 순서를 되돌리면 **3타를 받아치는 일이 한 번도 안 일어난다.**
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, false, true, false, ParryHeld: true), _dt);

        f.Guarding.ShouldBeTrue("자세는 서 있는데도 창이 이기는지를 보는 테스트다");
        HitResolver.Resolve(f, _at, Unguardable(), Tags(parryable: true)).ShouldBe(HitVerdict.Parried);
    }

    [Fact]
    public void 가드_불가도_아무것도_안_하면_평범한_판정이다()
    {
        HitResolver.Resolve(Spawn(_bossX + 100), _at, Unguardable(), Tags(parryable: false))
            .ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 방어는_거리와_높이보다_뒤다()
    {
        // 순서를 박아둔다. 안 닿은 판정까지 "막았다" 로 적으면 가드 개수가 실제로 막은 것보다
        // 부풀고, 그 개수가 곧 계측이다. **패리도 같다** — 창 안이라고 안 닿은 칼을 받아칠 수는 없다.
        var box = new HitBox(HitShape.Band(0, 10, 0, 200), 18);
        HitResolver.Resolve(Guarding(), _at, box, Tags(parryable: true)).ShouldBe(HitVerdict.MissedTooFar);

        Fighter parrying = Spawn(_bossX + 100);
        parrying.Tick(new InputFrame(0, false, false, true, false, ParryHeld: true), _dt);
        parrying.Parrying.ShouldBeTrue();
        HitResolver.Resolve(parrying, _at, box, Tags(parryable: true)).ShouldBe(HitVerdict.MissedTooFar);
    }

    [Fact]
    public void 왼쪽을_보는_보스의_모양은_왼쪽을_친다()
    {
        // 앞으로만 치는 모양. 옛 띠는 좌우 대칭이라 "보는 쪽" 이 판정에 실리는지를 한 번도 드러낸 적이 없다.
        var box = new HitBox(new HitShape(new[] { new HitRect(50, 300, 0, 200) }), 18);
        var facingLeft = new Placement(_bossX, 0, -1);

        HitResolver.Resolve(Spawn(_bossX - 150), facingLeft, box, Tags(false)).ShouldBe(HitVerdict.Hit);
        HitResolver.Resolve(Spawn(_bossX + 150), facingLeft, box, Tags(false))
            .ShouldBe(HitVerdict.MissedTooFar, "등 뒤를 쳤다 — 보는 쪽이 판정에 안 실렸다");
    }

    [Fact]
    public void 몸통이_닿으면_중심이_밖이어도_맞는다()
    {
        // 띠 [0, 260] · 파이터 중심 280 — 옛 판정(몸을 점으로 봤다)은 빗나감이었다. 몸 왼끝이 250 이라 닿는다.
        HitResolver.Resolve(Spawn(_bossX + 280), _at, Mid(), Tags(false)).ShouldBe(HitVerdict.Hit);
    }

}
