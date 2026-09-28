using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class PlayerAxesTests
{
    /// <summary>
    /// 기본값은 <b>세 수단이 다 있었던</b> 판정이다 — 의존도 축의 분모에 들어가는 자리다.
    /// 수단 유무를 안 적은 테스트는 그 축을 보는 테스트가 아니므로 이 기본이 맞다.
    /// </summary>
    private static DodgeEvent Event(
        DodgeVerb verb = DodgeVerb.Dash,
        HitVerdict verdict = HitVerdict.Dodged,
        double timingError = 0,
        int direction = 1,
        bool airborne = false,
        double distance = 200,
        bool greedWindow = false,
        bool dashAvailable = true,
        bool jumpAvailable = true,
        bool parryAvailable = true,
        bool guardAvailable = true) =>
        new("3연격", verb, verdict, timingError, direction, airborne, distance, greedWindow,
            dashAvailable, jumpAvailable, parryAvailable, guardAvailable);

    [Fact]
    public void 이벤트가_없으면_축이_전부_0_이다()
    {
        // 판단할 근거가 없을 때 0 을 주는 것이 중요하다 — NaN 이 나오면
        // 나중에 망 입력에 섞여 조용히 학습을 망친다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>());

        axes.DashTimingBias.ShouldBe(0);
        axes.DashTimingVar.ShouldBe(0);
        axes.ParryRate.ShouldBe(0);
        axes.GuardRate.ShouldBe(0);
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
    public void 의존도는_선택지가_있었을_때_그_수단을_고른_비율이다()
    {
        // 스펙 8절의 정의다 — "대시로도 피할 수 있는 상황에서 점프를 고른 비율".
        // 그냥 사용 비율(jumps / 전체)로 두면 "점프에 의존한다" 와 "점프로만 피할 수 있는
        // 패턴만 만났다" 를 구별하지 못한다. 그 둘은 봉인할 것이 정반대다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Parry), Event(verb: DodgeVerb.Parry),
            Event(verb: DodgeVerb.Jump),
            Event(verb: DodgeVerb.Dash),
        });

        // 넷 다 세 수단이 있었다 — 넷 전부가 분모다.
        axes.ParryReliance.ShouldBe(0.5, 0.001);
        axes.JumpReliance.ShouldBe(0.25, 0.001);
    }

    [Fact]
    public void 다른_수단이_없었으면_의존도의_분모에_안_들어간다()
    {
        // 점프 말고 답이 없는 패턴만 만난 사람은 "점프에 의존하는" 사람이 아니다.
        // 사용 비율이던 때는 이것이 1.00 이었다 — 망은 봉인할 이유가 없는 것을 봉인하려 든다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Jump, dashAvailable: false, jumpAvailable: true, parryAvailable: false),
            Event(verb: DodgeVerb.Jump, dashAvailable: false, jumpAvailable: true, parryAvailable: false),
            Event(verb: DodgeVerb.Jump, dashAvailable: false, jumpAvailable: true, parryAvailable: false),
        });

        axes.JumpReliance.ShouldBe(0);
        axes.JumpChoiceSamples.ShouldBe(0, "선택지가 없던 판정이 분모에 들어갔다");
    }

    [Fact]
    public void 선택지가_있던_판정만_의존도를_만든다()
    {
        // 둘은 대시로도 피할 수 있었고(선택지 있음) 둘은 점프뿐이었다(선택지 없음).
        // 선택지가 있던 둘 중 하나만 점프를 골랐으므로 0.5 다 — 사용 비율이면 4건 중 3건, 0.75 였다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Jump, dashAvailable: true, jumpAvailable: true, parryAvailable: false),
            Event(verb: DodgeVerb.Dash, dashAvailable: true, jumpAvailable: true, parryAvailable: false),
            Event(verb: DodgeVerb.Jump, dashAvailable: false, jumpAvailable: true, parryAvailable: false),
            Event(verb: DodgeVerb.Jump, dashAvailable: false, jumpAvailable: true, parryAvailable: false),
        });

        axes.JumpReliance.ShouldBe(0.5, 0.001);
        axes.JumpChoiceSamples.ShouldBe(2);
    }

    [Fact]
    public void 그_수단_자체가_없던_판정도_분모에_안_들어간다()
    {
        // 패리 불가 패턴에서 대시로 피한 것은 "패리를 안 골랐다" 가 아니다 — 고를 수가 없었다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Dash, dashAvailable: true, jumpAvailable: true, parryAvailable: false),
            Event(verb: DodgeVerb.Dash, dashAvailable: true, jumpAvailable: true, parryAvailable: false),
        });

        axes.ParryReliance.ShouldBe(0);
        axes.ParryChoiceSamples.ShouldBe(0);
    }

    [Fact]
    public void 피격_순간_고도는_판정이_선_순간을_센다()
    {
        // 이름이 air_time_ratio 였을 때는 "공중에 떠 있던 시간" 으로 읽혔는데 재는 것은 이것이다.
        // 전투의 90%를 땅에서 보내도 액티브 프레임마다 공중이면 1.00 이 나온다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(airborne: true), Event(airborne: true), Event(airborne: false), Event(airborne: false),
        });

        axes.AirborneAtImpactRatio.ShouldBe(0.5, 0.001);
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
    public void 거리_성향은_안으로_피한_것을_반대_부호로_읽는다()
    {
        // 한 갈래(MissedByRange)였을 때는 **도망쳐 피한 것과 파고들어 피한 것이 같은 부호**로
        // 쌓였다 (이슈 #46). 둘은 정반대 성향이고 봉인할 것도 정반대라, 같은 부호면 축이
        // 뭉개는 것이 아니라 **거꾸로 말한다** — 파고들수록 "멀리서 싸운다" 가 커진다.
        PlayerAxes outward = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Spacing, verdict: HitVerdict.MissedTooFar, distance: 400),
        });
        PlayerAxes inward = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Spacing, verdict: HitVerdict.MissedByGap, distance: 400),
        });

        outward.DistanceBias.ShouldBe(400, 0.001);
        inward.DistanceBias.ShouldBe(-400, 0.001, "안쪽 주머니로 피한 것이 '멀리서 싸운다' 로 쌓였다");

        // 같은 거리에서 하나씩이면 서로를 지운다 — 둘 다 쓰는 사람은 한쪽으로 치우치지 않았다.
        PlayerAxes both = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Spacing, verdict: HitVerdict.MissedTooFar, distance: 400),
            Event(verb: DodgeVerb.Spacing, verdict: HitVerdict.MissedByGap, distance: 400),
        });

        both.DistanceBias.ShouldBe(0, 0.001);
    }

    [Fact]
    public void 표본_수를_같이_들고_다닌다()
    {
        // 축만 보면 "3건으로 낸 0.5" 와 "300건으로 낸 0.5" 를 구별할 수 없다.
        // 나중에 망이 그 차이를 알아야 하므로 축과 함께 옮긴다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent> { Event(), Event(), Event() });

        axes.Samples.ShouldBe(3);
    }

    [Fact]
    public void 수단별_표본_수를_따로_들고_다닌다()
    {
        // Samples 하나만 붙이면 "관측 10건" 이 "대시 3건으로 낸 분산" 까지 보증하는 것처럼 보인다 —
        // 가장 얇은 근거를 가장 크게 믿게 만드는 배치다. 축이 아니라 개수라 축의 계약(수와 순서)을 안 건드린다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Dash), Event(verb: DodgeVerb.Dash), Event(verb: DodgeVerb.Dash),
            Event(verb: DodgeVerb.Jump),
            Event(verb: DodgeVerb.Parry), Event(verb: DodgeVerb.Parry),
            Event(verb: DodgeVerb.Spacing),
            Event(verb: DodgeVerb.None),
        });

        axes.Samples.ShouldBe(8);
        axes.DashSamples.ShouldBe(3);
        axes.JumpSamples.ShouldBe(1);
        axes.ParrySamples.ShouldBe(2);

        // 의존도 축은 부분집합의 부분집합이다 — 그 수단이 있었고 **다른 수단도 있었던** 판정만
        // 분모다. 그 얇기를 축만 보고는 알 수 없으므로 개수를 같이 싣는다.
        axes.JumpChoiceSamples.ShouldBe(8);
        axes.ParryChoiceSamples.ShouldBe(8);
    }

    [Fact]
    public void 간격으로_피한_것은_어떤_수단에도_안_들어간다()
    {
        // Spacing 은 행동이 아니라 서 있던 자리다. 이것을 수단으로 세면 의존도 축이 오염된다 —
        // 거리 성향은 DistanceBias 가 이미 재고 있으므로 간격의 축을 따로 만들지 않는다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Spacing, timingError: 0, direction: 0),
            Event(verb: DodgeVerb.Spacing, timingError: 0, direction: 0),
            Event(verb: DodgeVerb.Jump),
            Event(verb: DodgeVerb.Parry),
        });

        axes.DashSamples.ShouldBe(0);
        axes.DashDirectionBias.ShouldBe(0);
        axes.JumpReliance.ShouldBe(0.25, 0.001);
        axes.ParryReliance.ShouldBe(0.25, 0.001);
    }

    [Fact]
    public void 가드는_축과_개수로_같이_실린다()
    {
        // 가드는 11번째 축(GuardRate · 사용 비율)이고 개수(GuardSamples · GuardBrokenSamples)도 그대로 싣는다 (#104 · 설계 2026-09-28 §3.2).
        // 가드는 잡기 하나를 빼면 모든 판정에서 가능해 "고를 수 있었는데 골랐나" 의 분모가 거의 전부다 — 그러니 사용 비율이 곧
        // 정직한 성향이다. 의존도 축이 피하려던 혼동(가능했던 수단이 드문 패턴만 만났다)이 가드에는 없다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Guard, verdict: HitVerdict.Guarded),
            Event(verb: DodgeVerb.Guard, verdict: HitVerdict.Guarded),
            Event(verb: DodgeVerb.Guard, verdict: HitVerdict.GuardBroken),
            Event(verb: DodgeVerb.Parry, verdict: HitVerdict.Parried),
        });

        axes.GuardRate.ShouldBe(0.75, 0.001);
        axes.GuardSamples.ShouldBe(3);
        axes.GuardBrokenSamples.ShouldBe(1, "깨진 가드가 막아낸 가드와 한 점이 됐다");

        // 가드는 **다른 수단을 안 고른 것**으로도 세어진다 — 의존도의 분모는 그대로다.
        axes.ParryChoiceSamples.ShouldBe(4);
        axes.ParryReliance.ShouldBe(0.25, 0.001);
        axes.DashSamples.ShouldBe(0);
        axes.JumpSamples.ShouldBe(0);
        axes.ParrySamples.ShouldBe(1);
    }

    [Fact]
    public void 가드_중에_잡힌_관측은_버티지_못한_가드로_센다()
    {
        // 설계 §7.3 — 잡힘은 실패다: 맞음과 같이 아무 축도 성공으로 안 센다. 가드로 버티다 잡힌 관측(수단 Guard · 설계 §4.7)은 가드를 고른
        // 것이라 가드 개수에 들고, 버텨 내지 못했으니 "버티다 무너진" 쪽에 든다 — 막아 낸 가드(Guarded)와 한 점이 되면 가드 개수에서
        // 막은 수를 뺀 값이 거짓말한다. 잡기는 점프만 되는 판정이라(대시 · 패리 불가) 의존도의 분모에는 안 든다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Guard, verdict: HitVerdict.Guarded),
            Event(verb: DodgeVerb.Guard, verdict: HitVerdict.Grabbed,
                dashAvailable: false, parryAvailable: false, guardAvailable: false),
        });

        axes.GuardSamples.ShouldBe(2);
        axes.GuardBrokenSamples.ShouldBe(1, "가드 중에 잡힌 것을 막아 낸 가드로 셌다");
        axes.JumpChoiceSamples.ShouldBe(1, "잡기가 점프 의존도의 분모에 들었다 — 점프만 되는 판정이다");
        axes.ParryChoiceSamples.ShouldBe(1);
    }

    [Fact]
    public void 가드_비율은_가드를_고른_관측의_몫이다()
    {
        // 잡힌 가드도 가드다 — 고른 것은 같고 결과가 다르다(DodgeVerb.Guard). 가드를 안 받는 판정(잡기)이라도 ↓ 를 붙들고 있었으면
        // 가드를 고른 것이다: 분모를 "가드할 수 있었던 판정" 으로 좁히면 잡기에 붙들린 가드가 분자에만 남아 1 을 넘을 수 있다.
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent>
        {
            Event(verb: DodgeVerb.Guard, verdict: HitVerdict.Guarded),
            Event(verb: DodgeVerb.Guard, verdict: HitVerdict.GuardBroken),
            Event(verb: DodgeVerb.Guard, verdict: HitVerdict.Grabbed,
                dashAvailable: false, parryAvailable: false, guardAvailable: false),
            Event(verb: DodgeVerb.Parry, verdict: HitVerdict.Parried),
        });

        axes.GuardRate.ShouldBe(0.75, 0.001);
    }

    [Fact]
    public void 가드가_없으면_가드_비율은_0_이다()
    {
        PlayerAxes axes = PlayerAxes.From(new List<DodgeEvent> { Event(verb: DodgeVerb.Dash), Event(verb: DodgeVerb.Dash) });

        axes.GuardRate.ShouldBe(0);
    }

    [Fact]
    public void 축은_열한_개다()
    {
        // 축의 계약은 **망의 입력 모양**이다. 축을 하나 늘리면 지금까지의 입력 벡터가 전부
        // 다른 길이가 되므로, 수치 하나를 고치는 것과 다른 종류의 변경이다. 10 → 11(GuardRate)은 망을 세우는 자리에서
        // 한 번에 정했다(#104 · 설계 2026-09-28 §3.2) — GuardSamples 의 주석이 미뤄 둔 바로 그 결정이다.
        // 이름 목록으로 세지 않는 이유는 그러면 축을 더하면서 목록을 같이 고치는 것이
        // "계약을 지켰다" 로 보이기 때문이다 — 리플렉션이 그 손을 막는다.
        typeof(PlayerAxes).GetProperties().Count(p => p.PropertyType == typeof(double))
            .ShouldBe(11, "축의 수가 바뀌었다 — 축의 계약은 가볍게 못 바꾼다");
    }

}
