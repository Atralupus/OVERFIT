using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>결정의 칸 배치 (설계 2026-10-01 조각2 §1.1) — [기다리기, 다가가기, 계속하기, 동작 0 … 동작 N−1].</summary>
public class BossDecisionTests
{
    [Fact]
    public void 칸_배치는_기다리기_다가가기_계속하기_다음에_명부다()
    {
        var a = new BossActions(["3연격", "돌진"]);
        (a.Count, BossActions.Move(0), BossActions.Move(1)).ShouldBe((5, 3, 4));
        (a.Name(0), a.Name(1), a.Name(2), a.Name(4)).ShouldBe(("wait", "approach", "continue", "돌진"));
        (a.RosterIndex(4), a.RosterIndex(1)).ShouldBe(((int?)1, (int?)null));
    }
}
