using System.Collections.Generic;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class PlayerAxesTests
{
    private static DodgeEvent Event(
        DodgeVerb verb = DodgeVerb.Dash,
        HitVerdict verdict = HitVerdict.Dodged,
        double timingError = 0,
        int direction = 1,
        bool airborne = false,
        double distance = 200,
        bool greedWindow = false) =>
        new("횡베기", verb, verdict, timingError, direction, airborne, distance, greedWindow);

    [Fact]
    public void 이벤트가_없으면_축이_전부_0_이다()
    {
        // 판단할 근거가 없을 때 0 을 주는 것이 중요하다 — NaN 이 나오면
        // 나중에 망 입력에 섞여 조용히 학습을 망친다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>());

        axes.DashTimingBias.ShouldBe(0);
        axes.DashTimingVar.ShouldBe(0);
        axes.ParryRate.ShouldBe(0);
        axes.Samples.ShouldBe(0);
    }

    [Fact]
    public void 대시_타이밍_편향은_평균_오차다()
    {
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(timingError: -0.05),
            Event(timingError: -0.03),
            Event(timingError: -0.04),
        });

        axes.DashTimingBias.ShouldBe(-0.04, 0.001);
    }

    [Fact]
    public void 일관된_실수와_들쭉날쭉한_실수를_분산이_가른다()
    {
        PlayerAxes steady = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(timingError: -0.04), Event(timingError: -0.04), Event(timingError: -0.04),
        });
        PlayerAxes erratic = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(timingError: -0.12), Event(timingError: 0.04), Event(timingError: -0.04),
        });

        steady.DashTimingVar.ShouldBeLessThan(erratic.DashTimingVar);
    }

    [Fact]
    public void 대시_방향_편향은_안과_밖의_비율이다()
    {
        // +1 이 안으로 · -1 이 밖으로. 셋 중 둘이 안이면 +1/3
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(direction: 1), Event(direction: 1), Event(direction: -1),
        });

        axes.DashDirectionBias.ShouldBe(1.0 / 3.0, 0.001);
    }

    [Fact]
    public void 패리_성공률은_패리_시도_중_받아친_비율이다()
    {
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Parry, verdict: HitVerdict.Parried),
            Event(verb: DodgeVerb.Parry, verdict: HitVerdict.Hit),
            Event(verb: DodgeVerb.Dash, verdict: HitVerdict.Dodged),   // 패리가 아니라 세지 않는다
        });

        axes.ParryRate.ShouldBe(0.5, 0.001);
    }

    [Fact]
    public void 의존도는_전체_회피_중_그_수단의_비율이다()
    {
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Parry), Event(verb: DodgeVerb.Parry),
            Event(verb: DodgeVerb.Jump),
            Event(verb: DodgeVerb.Dash),
        });

        axes.ParryReliance.ShouldBe(0.5, 0.001);
        axes.JumpReliance.ShouldBe(0.25, 0.001);
    }

    [Fact]
    public void 공중_체류_비율은_공중에서_맞은_판정의_비율이다()
    {
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(airborne: true), Event(airborne: true), Event(airborne: false), Event(airborne: false),
        });

        axes.AirTimeRatio.ShouldBe(0.5, 0.001);
    }

    [Fact]
    public void 욕심은_선딜_중_공격한_비율이다()
    {
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(greedWindow: true), Event(greedWindow: false), Event(greedWindow: false), Event(greedWindow: false),
        });

        axes.Greed.ShouldBe(0.25, 0.001);
    }

    [Fact]
    public void 거리_성향은_평균_교전_거리다()
    {
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(distance: 100), Event(distance: 300),
        });

        axes.DistanceBias.ShouldBe(200, 0.001);
    }

    [Fact]
    public void 표본_수를_같이_들고_다닌다()
    {
        // 축만 보면 "3건으로 낸 0.5" 와 "300건으로 낸 0.5" 를 구별할 수 없다.
        // 나중에 망이 그 차이를 알아야 하므로 축과 함께 옮긴다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent> { Event(), Event(), Event() });

        axes.Samples.ShouldBe(3);
    }
}
