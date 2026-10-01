using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>무작위 조종기 (설계 2026-10-01 조각2 §3.2) — 열린 칸 중 하나를 시드 난수로.</summary>
public class RandomControllerTests
{
    private static BossDecision D(int n, bool[] mask) => new(DecisionPoint.Rest, n, 12, mask,
        new BossSight(0, 1, 1440, -1, 1200, new FighterSnapshot(480, 0, 1, FighterAction.Idle, false, 220, 10), []), null, null);

    [Fact]
    public void 열린_칸만_고른다()
    {
        var c = new RandomController(51);
        bool[] mask = [false, true, false, false, true];
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int n = 0; n < 200; n++)
        {
            int act = c.Decide(D(n, mask));
            mask[act].ShouldBeTrue($"{n}: 가려진 칸 {act}");
            seen.Add(act);
        }

        seen.Count.ShouldBe(2, "열린 칸 둘을 다 안 썼다");
    }

    [Fact]
    public void 같은_시드_같은_번호면_같은_칸이다()
    {
        bool[] mask = [true, true, false, true, true];
        for (int n = 0; n < 20; n++)
        {
            new RandomController(7).Decide(D(n, mask)).ShouldBe(new RandomController(7).Decide(D(n, mask)));
        }
    }

    [Fact]
    public void 폭탄에_반응하지_않는다() => new RandomController(1).ReactsToBombs.ShouldBeFalse();
}
