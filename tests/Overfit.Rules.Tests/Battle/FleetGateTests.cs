using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 봇 함대의 관문 — 계측이 성향을 되찾나 (#104 · 설계 2026-09-28 §3.4). <b>계측이 성향을 못 되찾으면 망도 못 배운다</b> — 여기서 멈춘다.
///
/// <para>
/// 기준 성향(<see cref="FleetPlay.Mid"/>)에서 성향 하나만 낮음 → 높음으로 바꾸고, 실제 1단계를 시드 여섯으로 돌려 관측을 모은 뒤 축을 견준다.
/// 판마다의 축을 평균 내지 않고 관측을 모아 한 번에 접는다 — 짧게 끝난 판의 몇 건이 평균을 흔들지 않게. 중간은 뺐다: 여섯 판으로는 가운데의
/// 순서가 잡음에 흔들리고, 관문이 묻는 것은 "성향이 축을 움직이나" 다. 선을 못 넘으면 선을 낮추지 않고 봇을 고친다(성향이 행동에 안 닿는 것이다).
/// </para>
/// </summary>
public class FleetGateTests
{
    private static readonly ulong[] _seeds = [1, 2, 3, 4, 5, 6];

    private static PlayerAxes Pooled(BotTraits traits)
    {
        var events = new List<DodgeEvent>();
        foreach (ulong seed in _seeds)
        {
            events.AddRange(FleetPlay.Play(traits, seed).Sim.Events);
        }

        return PlayerAxes.From(events);
    }

    /// <summary>
    /// 타이밍이 좋은 봇 — 수단의 비중을 볼 때의 바탕이다. 관측의 수단은 <b>결과를 만든 것</b>이라(<see cref="DodgeEvent.Verb"/>) 늦은 대시는 대시가
    /// 아니라 "안 피함" 으로 실린다 — 바닥 전체를 치는 착지(점프 공격)는 첫 틱에 닿아 흔들림 3틱이면 대시의 넷에 하나가 늦는다. 가운데 타이밍으로 재면
    /// 수단의 축이 선호가 아니라 솜씨를 잰다(재 봄 · #104: 대시 비중 0.8 인데 대시로 실린 관측이 20%). 선호가 축에 닿는지는 솜씨를 고정하고 본다.
    /// </summary>
    private static readonly BotTraits _skilled = FleetPlay.Mid with { ReactionSeconds = 0.2, JitterSeconds = 0.02, BiasSeconds = -0.03 };

    /// <summary>수단 하나의 비중을 <paramref name="w"/> 로, 나머지 셋이 남은 몫을 똑같이 나눈다 — 타이밍이 좋은 봇 위에서.</summary>
    private static BotTraits Weighted(DodgeVerb verb, double w)
    {
        double rest = (1 - w) / 3;
        return _skilled with
        {
            Dash = verb == DodgeVerb.Dash ? w : rest,
            Jump = verb == DodgeVerb.Jump ? w : rest,
            Parry = verb == DodgeVerb.Parry ? w : rest,
            Guard = verb == DodgeVerb.Guard ? w : rest,
        };
    }

    /// <summary>
    /// 대시만 하는 봇 — 타이밍 · 방향 축은 대시 건으로만 선다. 반응은 빠르게(0.15초) 둔다: 3연격의 2 · 3타는 선딜이 0.25초라 반응이 그보다 길면
    /// 이른 편향이 반응 지연에 잘려 편향의 차이가 축에 안 닿는다 — 편향을 볼 때 반응을 섞지 않는다.
    /// </summary>
    private static BotTraits Dasher => FleetPlay.Mid with { Dash = 1, Jump = 0, Parry = 0, Guard = 0, Greed = 0, ReactionSeconds = 0.15 };

    private static void ShouldRise(double low, double high, double margin, string axis) =>
        (high - low).ShouldBeGreaterThanOrEqualTo(margin, $"{axis}: 낮음 {low:0.000} → 높음 {high:0.000} — 성향이 축을 {margin} 만큼 못 올렸다");

    [Fact]
    public void 대시_비중이_대시_몫을_올린다()
    {
        static double Share(PlayerAxes a) => (double)a.DashSamples / a.Samples;
        ShouldRise(Share(Pooled(Weighted(DodgeVerb.Dash, 0.1))), Share(Pooled(Weighted(DodgeVerb.Dash, 0.8))), 0.2, "대시 몫");
    }

    [Fact]
    public void 점프_비중이_점프_의존도를_올린다()
    {
        ShouldRise(Pooled(Weighted(DodgeVerb.Jump, 0.1)).JumpReliance, Pooled(Weighted(DodgeVerb.Jump, 0.8)).JumpReliance, 0.2, "jump_reliance");
    }

    [Fact]
    public void 패리_비중이_패리_의존도를_올린다()
    {
        ShouldRise(Pooled(Weighted(DodgeVerb.Parry, 0.1)).ParryReliance, Pooled(Weighted(DodgeVerb.Parry, 0.8)).ParryReliance, 0.2, "parry_reliance");
    }

    [Fact]
    public void 가드_비중이_가드_비율을_올린다()
    {
        ShouldRise(Pooled(Weighted(DodgeVerb.Guard, 0.1)).GuardRate, Pooled(Weighted(DodgeVerb.Guard, 0.8)).GuardRate, 0.2, "guard_rate");
    }

    [Fact]
    public void 늦게_누르는_편향이_타이밍_편향을_올린다()
    {
        ShouldRise(
            Pooled(Dasher with { BiasSeconds = -0.08 }).DashTimingBias, Pooled(Dasher with { BiasSeconds = 0.04 }).DashTimingBias, 0.05, "dash_timing_bias");
    }

    [Fact]
    public void 흔들림이_타이밍_분산을_올린다()
    {
        double low = Pooled(Dasher with { JitterSeconds = 0.02 }).DashTimingVar;
        double high = Pooled(Dasher with { JitterSeconds = 0.10 }).DashTimingVar;

        high.ShouldBeGreaterThan(low * 2, $"dash_timing_var: 낮음 {low:0.00000} → 높음 {high:0.00000}");
    }

    [Fact]
    public void 안쪽_대시가_방향_편향을_올린다()
    {
        ShouldRise(
            Pooled(Dasher with { DashInward = 0 }).DashDirectionBias, Pooled(Dasher with { DashInward = 1 }).DashDirectionBias, 0.8, "dash_direction_bias");
    }

    [Fact]
    public void 거리가_거리_편향을_올린다()
    {
        ShouldRise(Pooled(FleetPlay.Mid with { RestGap = 0 }).DistanceBias, Pooled(FleetPlay.Mid with { RestGap = 400 }).DistanceBias, 50, "distance_bias");
    }

    [Fact]
    public void 욕심이_욕심_축을_올린다()
    {
        ShouldRise(Pooled(FleetPlay.Mid with { Greed = 0 }).Greed, Pooled(FleetPlay.Mid with { Greed = 0.6 }).Greed, 0.1, "greed");
    }

    /// <summary>패리 습관형 — 가드 · 욕심 없이 패리 0.9, 타이밍이 좋은 봇 위에서(눈으로 누르는 패리가 받아쳐야 박자의 차이가 보인다).</summary>
    private static BotTraits Parrier(double rhythm) =>
        _skilled with { Dash = 0.05, Jump = 0.05, Parry = 0.9, Guard = 0, Greed = 0, Rhythm = rhythm };

    /// <summary>2단계 대본 <paramref name="pattern"/> 만 도는 판에서, 그 패턴의 판정 중 맞은(맞음 · 붕괴 · 잡힘) 몫.</summary>
    private static double HitShare(BotTraits traits, string pattern)
    {
        int hits = 0, all = 0;
        foreach (ulong seed in _seeds)
        {
            foreach (DodgeEvent e in FleetPlay.Play(traits, seed, stage: 2, script: [pattern]).Sim.Events.Where(e => e.PatternId == pattern))
            {
                all++;
                if (e.Verdict is HitVerdict.Hit or HitVerdict.GuardBroken or HitVerdict.Grabbed)
                {
                    hits++;
                }
            }
        }

        all.ShouldBeGreaterThan(0, $"{pattern} 의 판정이 하나도 안 잡혔다");
        return (double)hits / all;
    }

    [Fact]
    public void 리듬형이_엇박에_더_맞는다()
    {
        // 엇박 3연격은 3연격과 여는 그림이 같고 타마다 선딜만 늦다 — 박자로 누르는 패리는 창(0.133초) 앞에서 헛쳐 커밋 안에서 맞는다(설계 2026-09-24 §4.9).
        ShouldRise(HitShare(Parrier(0), "엇박 3연격"), HitShare(Parrier(1), "엇박 3연격"), 0.2, "엇박에 맞는 몫");
    }

    [Fact]
    public void 리듬은_3연격에서는_차이가_없다()
    {
        // 대조 — 3연격은 제 박자가 곧 실제 판정이라 박자로 누르든 눈으로 누르든 같다. 여기서 갈리면 리듬이 박자 말고 다른 것을 건드린 것이다.
        double sight = HitShare(Parrier(0), "3연격");
        double rhythm = HitShare(Parrier(1), "3연격");

        Math.Abs(rhythm - sight).ShouldBeLessThan(0.1, $"3연격에 맞는 몫: 눈 {sight:0.000} · 박자 {rhythm:0.000}");
    }
}
