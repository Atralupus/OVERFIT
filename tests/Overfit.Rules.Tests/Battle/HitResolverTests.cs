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
    };

    /// <summary><paramref name="ticks"/> 틱째의 파이터. 1틱째면 행동 경과가 정확히 한 틱이다.</summary>
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

    /// <summary>바닥에서 200 까지, 보스로부터 260 안쪽.</summary>
    private static HitBox Mid() => new(0, 260, 0, 200, 18);

    /// <summary>바닥에서 70 까지 — 점프로 넘는다.</summary>
    private static HitBox Low() => new(0, 520, 0, 70, 14);

    /// <summary>바닥에서 140 위 — 선 키(120)를 넘으므로 지상이 안전한 대공.</summary>
    private static HitBox High() => new(0, 300, 140, 420, 20);

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
        HitResolver.Resolve(Spawn(_bossX + 400), _bossX, Mid(), Tags(false)).ShouldBe(HitVerdict.MissedByRange);
    }

    [Fact]
    public void 거리_안이면_맞는다()
    {
        HitResolver.Resolve(Spawn(_bossX + 100), _bossX, Mid(), Tags(false)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 좌우_어느_쪽이든_같은_거리면_같다()
    {
        HitResolver.Resolve(Spawn(_bossX - 100), _bossX, Mid(), Tags(false))
            .ShouldBe(HitResolver.Resolve(Spawn(_bossX + 100), _bossX, Mid(), Tags(false)));
    }

    [Fact]
    public void 대시_무적이면_피한다()
    {
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, true, false, false), _dt);
        f.Invulnerable.ShouldBeTrue();

        HitResolver.Resolve(f, _bossX, Mid(), Tags(false)).ShouldBe(HitVerdict.Dodged);
    }

    [Fact]
    public void 패리_창_안이고_패리_가능한_패턴이면_받아친다()
    {
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, false, true, false), _dt);
        f.Parrying.ShouldBeTrue();

        HitResolver.Resolve(f, _bossX, Mid(), Tags(parryable: true)).ShouldBe(HitVerdict.Parried);
    }

    [Fact]
    public void 패리_불가_패턴은_패리해도_맞는다()
    {
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, false, true, false), _dt);

        HitResolver.Resolve(f, _bossX, Mid(), Tags(parryable: false)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 낮은_판정은_점프로_넘는다()
    {
        Fighter f = Airborne(_bossX + 100);

        f.Y.ShouldBeGreaterThan(70);
        HitResolver.Resolve(f, _bossX, Low(), Tags(false)).ShouldBe(HitVerdict.MissedByHeight);
    }

    [Fact]
    public void 대공은_지상이_안전하고_공중이_위험하다()
    {
        HitResolver.Resolve(Spawn(_bossX + 100), _bossX, High(), Tags(false)).ShouldBe(HitVerdict.MissedByHeight);
        HitResolver.Resolve(Airborne(_bossX + 100), _bossX, High(), Tags(false)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 안_맞은_이유를_거리와_높이로_나눠_말한다()
    {
        // 여기서 이유를 버리면 BattleSim 은 "그 순간 무슨 행동 중이었나" 로 추측할 수밖에 없다.
        // 그 추측이 실제로 틀렸다 — 점프로 넘긴 판정이 같이 눌러둔 패리의 공으로 기록됐다.
        HitResolver.Resolve(Spawn(_bossX + 600), _bossX, Low(), Tags(false)).ShouldBe(HitVerdict.MissedByRange);
        HitResolver.Resolve(Airborne(_bossX + 100), _bossX, Low(), Tags(false)).ShouldBe(HitVerdict.MissedByHeight);
    }

    [Fact]
    public void 거리가_먼저_걸리면_높이는_안_본다()
    {
        // 둘 다 어긋났을 때 무엇이라 말하는가. 거리를 먼저 보므로 거리로 답한다 —
        // 순서를 박아두지 않으면 같은 상황이 판마다 다른 라벨을 내 학습 데이터가 흔들린다.
        HitResolver.Resolve(Airborne(_bossX + 600), _bossX, Low(), Tags(false)).ShouldBe(HitVerdict.MissedByRange);
    }

    [Fact]
    public void 무적이_패리보다_먼저다()
    {
        // 지금은 행동이 하나뿐이라 겹칠 수 없지만, 순서를 박아두면
        // 나중에 무적 부여 수단이 늘어도 판정이 안 흔들린다.
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, true, false, false), _dt);

        HitResolver.Resolve(f, _bossX, Mid(), Tags(parryable: true)).ShouldBe(HitVerdict.Dodged);
    }

    // ── 패턴의 창이 실제로 문다 (이슈 #16) ─────────────────────────────────────
    //
    // 전에는 HitResolver 가 파이터의 창만 봤다. 대공찌르기가 parry_window 0.10 을 선언해도
    // 중검의 0.12 가 그대로 이겨서 **그 숫자는 아무것도 안 했다** — 그런데 망은 그 숫자를 배운다.
    // 거짓말하는 숫자는 없는 숫자보다 나쁘다. 이제 유효 창은 **둘 중 좁은 쪽**이다.

    [Fact]
    public void 패리는_패턴과_파이터_중_좁은_창을_따른다()
    {
        InputFrame parry = new(0, false, false, true, false);

        // 패턴 0.05 < 파이터 0.12. 3틱(0.05)이면 패턴 창은 이미 닫혔고 파이터 창은 열려 있다.
        HitResolver.Resolve(Acting(parry, 1), _bossX, Mid(), Tags(true, parryWindow: 0.05))
            .ShouldBe(HitVerdict.Parried);

        Fighter late = Acting(parry, 4);
        late.Parrying.ShouldBeTrue("파이터 창은 아직 열려 있어야 이 테스트가 좁은 쪽을 본다");

        // **정확**을 잃는다. 패턴이 요구하는 정밀도를 못 맞췄으니 정확 패리는 아니고,
        // 누른 지 0.5초를 안 넘겼으니 부정확 패리로 받아낸다(이슈 #27) — 전에는 그냥 맞았다.
        HitResolver.Resolve(late, _bossX, Mid(), Tags(true, parryWindow: 0.05)).ShouldBe(HitVerdict.ParriedLate);
    }

    [Fact]
    public void 패턴_창이_더_넓으면_파이터_창이_이긴다()
    {
        // 좁은 쪽이 이긴다는 것은 양방향이다. 패턴이 넉넉해도 파이터의 정확 창이 닫혔으면
        // 정확 패리가 아니다 — 안 그러면 패턴 태그가 캐릭터 차이를 지워 버린다.
        InputFrame parry = new(0, false, false, true, false);
        Fighter late = Acting(parry, 9);   // 0.15 > 기준 파이터의 정확 창 0.133

        late.Parrying.ShouldBeFalse();
        HitResolver.Resolve(late, _bossX, Mid(), Tags(true, parryWindow: 0.30)).ShouldBe(HitVerdict.ParriedLate);

        // 부정확 창(0.5초)까지 넘기면 그때야 그냥 맞는다. 이 줄이 없으면 "늦으면 늘 받아낸다" 가
        // 되어 패리에 실패가 없어진다.
        Fighter tooLate = Acting(parry, 31);   // 0.5167 > 0.5
        HitResolver.Resolve(tooLate, _bossX, Mid(), Tags(true, parryWindow: 0.30)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 대시_무적도_패턴과_파이터_중_좁은_창을_따른다()
    {
        InputFrame dash = new(0, false, true, false, false);

        HitResolver.Resolve(Acting(dash, 1), _bossX, Mid(), Tags(false, dashWindow: 0.05))
            .ShouldBe(HitVerdict.Dodged);

        Fighter late = Acting(dash, 4);
        late.Invulnerable.ShouldBeTrue("파이터 무적은 아직 돌아야 이 테스트가 좁은 쪽을 본다");
        HitResolver.Resolve(late, _bossX, Mid(), Tags(false, dashWindow: 0.05)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void Dash_window_0_은_길이가_0_인_창이_아니라_대시_불가다()
    {
        // 두 해석이 값으로는 같은 곳에 떨어지지만 뜻이 다르다. DodgeEvent.DashAvailable 이
        // dash_window > 0 으로 "대시가 가능했나" 를 싣고, 의존도 축의 분모가 그것이다 —
        // 0 을 "아주 짧은 창" 으로 읽으면 그 분모가 거짓이 된다.
        Fighter f = Acting(new InputFrame(0, false, true, false, false), 1);

        f.Invulnerable.ShouldBeTrue();
        HitResolver.Resolve(f, _bossX, Mid(), Tags(false, dashWindow: 0)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 판정기는_상태를_안_바꾼다()
    {
        Fighter f = Spawn(_bossX + 100);
        int before = f.Health;

        HitResolver.Resolve(f, _bossX, Mid(), Tags(false));
        HitResolver.Resolve(f, _bossX, Mid(), Tags(false));

        f.Health.ShouldBe(before);
    }
}
