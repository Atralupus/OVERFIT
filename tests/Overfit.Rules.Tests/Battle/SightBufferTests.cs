using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>늦은 관측의 고리 (설계 2026-10-01 조각2 §2) — 보스가 보는 파이터는 늦춤만큼 앞의 모습이다.</summary>
public class SightBufferTests
{
    private static FighterSnapshot At(double x) => new(x, 0, 1, FighterAction.Idle, false, 220, 10);

    [Fact]
    public void 늦춤만큼_앞의_모습을_낸다()
    {
        var b = new SightBuffer(18);
        for (int t = 0; t <= 30; t++)
        {
            b.Push(At(t));
        }

        b.Delayed.X.ShouldBe(12, "30 에서 18 앞은 12 다");
    }

    [Fact]
    public void 모자라면_첫_모습이다()
    {
        var b = new SightBuffer(18);
        b.Push(At(100));
        b.Push(At(101));
        b.Delayed.X.ShouldBe(100);
    }

    [Fact]
    public void 늦춤이_0_이면_지금이다()
    {
        var b = new SightBuffer(0);
        b.Push(At(5));
        b.Push(At(6));
        b.Delayed.X.ShouldBe(6);
    }
}
