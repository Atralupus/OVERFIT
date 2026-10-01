using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>보스의 움직임 — 점프 이동의 착지 쪽 · 물러서기 (설계 2026-10-01 조각3 §2). 파이터 없이 돈다.</summary>
public class TravelMotionTests
{
    /// <summary>실제 아레나(1920) · 보스 반폭 85 · 파이터 반폭 30 의 범위다.</summary>
    private static readonly MotionBounds _bounds = new(85, 1835, 115);

    private static IBossMotion Make(MotionDef def) =>
        BossMotions.Create(def, _bounds) ?? throw new Xunit.Sdk.XunitException($"{def.Id} 가 등록표에 없다");

    private static MotionStep Land(IBossMotion leap, double bossX, int facing, double fighterX)
    {
        MotionStep step = leap.Tick(new MotionContext(bossX, 0, facing, fighterX, 0));
        for (int t = 1; t <= 36 && !step.Finished; t++)
        {
            step = leap.Tick(new MotionContext(step.X, step.Y, step.Facing == 0 ? facing : step.Facing, fighterX, t));
        }

        return step;
    }

    [Fact]
    public void 넘어_뛰기는_파이터_너머_몸_간격에_내리고_착지_쪽을_본다()
    {
        // 보스 1440 · 파이터 480 — 너머는 480 − 115 = 365. 뛰는 틱에 착지 쪽(왼쪽)으로 돌아선다.
        MotionStep land = Land(Make(new MotionDef { Id = "leap", Height = 280, Air = 0.6, Land = "far" }), 1440, -1, 480);
        (land.Finished, land.X, land.Y).ShouldBe((true, 365.0, 0.0));
        land.Facing.ShouldBe(-1);
    }

    [Fact]
    public void 뒤로_뛰기는_파이터에게서_거리만큼_자기_쪽에_내리고_보는_쪽을_안_바꾼다()
    {
        // 보스 900 · 파이터 480 — 자기 쪽(오른쪽)으로 600 → 1080. 보는 쪽은 0(그대로) — 파이터를 본 채 뒤로 뛴다.
        IBossMotion leap = Make(new MotionDef { Id = "leap", Height = 280, Air = 0.6, Land = "away", Distance = 600 });
        MotionStep first = leap.Tick(new MotionContext(900, 0, -1, 480, 0));
        first.Facing.ShouldBe(0);
        Land(Make(new MotionDef { Id = "leap", Height = 280, Air = 0.6, Land = "away", Distance = 600 }), 900, -1, 480).X.ShouldBe(1080);
    }

    [Fact]
    public void 뒤로_뛰기의_자리는_아레나_안으로_자른다() =>
        Land(Make(new MotionDef { Id = "leap", Height = 280, Air = 0.6, Land = "away", Distance = 600 }), 1600, -1, 1400).X.ShouldBe(1835);

    [Fact]
    public void 착지_쪽을_안_적으면_지금의_도약이다() =>
        Land(Make(new MotionDef { Id = "leap", Height = 280, Air = 0.6 }), 1440, -1, 480).X.ShouldBe(595);

    [Fact]
    public void 물러서기는_틱마다_빠르기만큼_파이터에게서_멀어지고_보는_쪽을_안_바꾼다()
    {
        IBossMotion retreat = Make(new MotionDef { Id = "retreat", Speed = 840 });
        MotionStep step = retreat.Tick(new MotionContext(1000, 0, -1, 480, 0));
        (step.X, step.Facing, step.Finished, step.HoldClock).ShouldBe((1014.0, 0, false, false));
    }

    [Fact]
    public void 물러서기는_아레나_끝에서_끝난다()
    {
        IBossMotion retreat = Make(new MotionDef { Id = "retreat", Speed = 840 });
        MotionStep step = retreat.Tick(new MotionContext(1830, 0, -1, 480, 0));
        (step.X, step.Finished).ShouldBe((1835.0, true));
        retreat.Tick(new MotionContext(1835, 0, -1, 480, 1)).Finished.ShouldBeTrue();
    }
}
