using PreReLU.Battle.Rules;
using Shouldly;
using Xunit;

namespace PreReLU.Rules.Tests.Battle;

public class HitResolverTests
{
    private const double _dt = 1.0 / 60.0;
    private const double _bossX = 960;

    private static Fighter Spawn(double x) => new(TestConfigs.Fighter(), new Arena(1920), x);

    private static PatternTags Tags(bool parryable) => new()
    {
        DashWindow = 0.18,
        DashDirection = "out",
        Jumpable = false,
        AntiAir = false,
        Parryable = parryable,
        ParryWindow = parryable ? 0.12 : 0,
        PunishGreed = false,
        Reach = "mid",
        Feint = false,
        MultiHit = 1,
        Tracking = false,
    };

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
        HitResolver.Resolve(Spawn(_bossX + 400), _bossX, Mid(), Tags(false)).ShouldBe(HitVerdict.Miss);
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
        HitResolver.Resolve(f, _bossX, Low(), Tags(false)).ShouldBe(HitVerdict.Miss);
    }

    [Fact]
    public void 대공은_지상이_안전하고_공중이_위험하다()
    {
        HitResolver.Resolve(Spawn(_bossX + 100), _bossX, High(), Tags(false)).ShouldBe(HitVerdict.Miss);
        HitResolver.Resolve(Airborne(_bossX + 100), _bossX, High(), Tags(false)).ShouldBe(HitVerdict.Hit);
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
