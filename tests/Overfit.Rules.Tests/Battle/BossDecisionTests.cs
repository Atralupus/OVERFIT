using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>결정의 칸 배치 (설계 2026-10-01 조각2 §1.1 · 조각3 §0) — [기다리기, 다가가기, 물러서기, 넘어 뛰기, 뒤로 뛰기, 계속하기, 동작 0 … 동작 N−1].</summary>
public class BossDecisionTests
{
    [Fact]
    public void 칸_배치는_움직임_다섯과_계속하기_다음에_명부다()
    {
        var a = new BossActions(["3연격", "돌진"]);
        (a.Count, BossActions.Move(0), BossActions.Move(1)).ShouldBe((8, 6, 7));
        (a.Name(0), a.Name(1), a.Name(2), a.Name(3), a.Name(4), a.Name(5), a.Name(7))
            .ShouldBe(("wait", "approach", "retreat", "leap_over", "leap_back", "continue", "돌진"));
        (a.RosterIndex(7), a.RosterIndex(5)).ShouldBe(((int?)1, (int?)null));
        (a.Index("leap_back"), a.Index("돌진"), a.Index("없다")).ShouldBe(((int?)4, (int?)7, (int?)null));
    }
}
