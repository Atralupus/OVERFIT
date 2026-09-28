using System.Collections.Generic;
using Overfit.Battle.Rules;
using Overfit.Factory;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>
/// 사례 가르기와 라벨 (#108 · 설계 2026-09-28 §4.3). 판을 안 돌리고 틱마다의 (지금 패턴, 관측 수)와 관측 목록을 손으로 지어 넣는다 — 규칙이
/// 판의 흐름이 아니라 이 둘에만 기대는 것을 보이려는 것이다.
/// </summary>
public class InstanceTrackerTests
{
    private static DodgeEvent Event(string pattern, HitVerdict verdict) =>
        new(pattern, DodgeVerb.None, verdict, 0, 0, false, 100, false, true, true, true, true);

    /// <summary>틱마다의 관측을 흉내 낸다 — 관측이 나면 목록에 붙이고, 틱이 끝난 자리의 (패턴, 관측 수)를 넘긴다.</summary>
    private sealed class Script
    {
        public List<DodgeEvent> Events { get; } = new();

        public InstanceTracker Tracker { get; } = new();

        public Script Tick(string? pattern, params DodgeEvent[] emitted)
        {
            Events.AddRange(emitted);
            Tracker.Observe(pattern, Events.Count);
            return this;
        }

        public List<PatternInstance> Finish() => Tracker.Finish(Events);
    }

    [Fact]
    public void 연달아_같은_패턴은_두_사례다()
    {
        // uniform 은 같은 칸을 이어 뽑는다 — 패턴 사이의 간격(null)이 둘을 가른다.
        List<PatternInstance> instances = new Script()
            .Tick(null)
            .Tick("3연격")
            .Tick("3연격", Event("3연격", HitVerdict.Dodged))
            .Tick(null)
            .Tick("3연격")
            .Tick("3연격", Event("3연격", HitVerdict.Hit))
            .Tick(null)
            .Finish();

        instances.ShouldBe(new[] { new PatternInstance("3연격", false), new PatternInstance("3연격", true) });
    }

    [Theory]
    [InlineData(HitVerdict.Hit)]
    [InlineData(HitVerdict.GuardBroken)]
    [InlineData(HitVerdict.Grabbed)]
    public void 맞음_붕괴_잡힘은_맞았다(HitVerdict verdict)
    {
        // 한 건이라도 있으면 그 사례는 맞았다 — 앞의 두 타를 피하고 셋째에 맞아도 1 이다.
        List<PatternInstance> instances = new Script()
            .Tick("3연격", Event("3연격", HitVerdict.Dodged))
            .Tick("3연격", Event("3연격", verdict))
            .Tick("3연격", Event("3연격", HitVerdict.Dodged))
            .Tick(null)
            .Finish();

        instances.ShouldBe(new[] { new PatternInstance("3연격", true) });
    }

    [Theory]
    [InlineData(HitVerdict.Dodged)]
    [InlineData(HitVerdict.Parried)]
    [InlineData(HitVerdict.Guarded)]
    [InlineData(HitVerdict.MissedTooFar)]
    [InlineData(HitVerdict.MissedByGap)]
    [InlineData(HitVerdict.MissedByHeight)]
    public void 피함_받아침_막음_빗나감은_안_맞았다(HitVerdict verdict)
    {
        // 막아 낸 가드(칩 피해)는 막은 것이다 — 가드로 버티던 사람이 무너진 것(GuardBroken)만 맞은 것이다.
        List<PatternInstance> instances = new Script()
            .Tick("점프 3연속", Event("점프 3연속", verdict))
            .Tick(null)
            .Finish();

        instances.ShouldBe(new[] { new PatternInstance("점프 3연속", false) });
    }

    [Fact]
    public void 끝나지_않고_안_맞은_사례는_버린다()
    {
        // 판이 그 사례 도중에 끝났다(보스가 쓰러짐 · 시간 초과) — "끝까지 버텼나" 를 모르므로 0 으로 적지 않는다.
        List<PatternInstance> instances = new Script()
            .Tick("3연격", Event("3연격", HitVerdict.Dodged))
            .Tick(null)
            .Tick("1타 잡기", Event("1타 잡기", HitVerdict.Dodged))
            .Finish();

        instances.ShouldBe(new[] { new PatternInstance("3연격", false) });
    }

    [Fact]
    public void 끝나지_않았어도_맞았으면_남는다()
    {
        // 맞아서 쓰러진 사례 — 판을 끝낸 그 한 대가 곧 답이다.
        List<PatternInstance> instances = new Script()
            .Tick("1타 잡기", Event("1타 잡기", HitVerdict.Grabbed))
            .Finish();

        instances.ShouldBe(new[] { new PatternInstance("1타 잡기", true) });
    }

    [Fact]
    public void 보스가_탈진해_끊긴_사례는_끝난_것이다()
    {
        // 받아쳐 무너뜨리면 패턴이 그 자리에서 끊기고 null 로 돌아간다 — 파이터가 이긴 교환이라 0 으로 남는다(판이 끝나 모르는 것과 다르다).
        List<PatternInstance> instances = new Script()
            .Tick("엇박 3연격", Event("엇박 3연격", HitVerdict.Parried))
            .Tick(null)
            .Tick(null)
            .Finish();

        instances.ShouldBe(new[] { new PatternInstance("엇박 3연격", false) });
    }

    [Fact]
    public void 다른_패턴의_관측은_안_센다()
    {
        // 앞 사례의 늦은 관측이 다음 사례의 구간에 떨어져도 id 가 가른다.
        List<PatternInstance> instances = new Script()
            .Tick("3연격", Event("3연격", HitVerdict.Dodged))
            .Tick(null)
            .Tick("1타 돌진")
            .Tick("1타 돌진", Event("3연격", HitVerdict.Hit), Event("1타 돌진", HitVerdict.Dodged))
            .Tick(null)
            .Finish();

        instances.ShouldBe(new[] { new PatternInstance("3연격", false), new PatternInstance("1타 돌진", false) });
    }

    [Fact]
    public void 패턴이_간격_없이_바뀌어도_갈린다()
    {
        // 판은 늘 간격을 두지만(패턴 사이 쉬는 틱이 1 이상) 가르기가 그 가정에 기대지 않는다.
        List<PatternInstance> instances = new Script()
            .Tick("3연격", Event("3연격", HitVerdict.Hit))
            .Tick("1타 돌진", Event("1타 돌진", HitVerdict.Dodged))
            .Tick(null)
            .Finish();

        instances.ShouldBe(new[] { new PatternInstance("3연격", true), new PatternInstance("1타 돌진", false) });
    }

    [Fact]
    public void 패턴이_없으면_사례도_없다()
    {
        new Script().Tick(null).Tick(null).Finish().ShouldBeEmpty();
    }
}
