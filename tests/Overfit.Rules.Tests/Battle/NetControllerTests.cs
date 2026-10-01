using System;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>망 조종기 (설계 2026-10-01 조각4 §4) — 열린 칸의 소프트맥스에서 시드로 뽑고, 고를 것이 있던 결정만 적는다.</summary>
public class NetControllerTests
{
    private static readonly BossSight _sight = new(0, 1, 1440, -1, 1200, new FighterSnapshot(480, 0, 1, FighterAction.Idle, false, 220, 10), []);

    private static BossDecision D(int n, bool[] mask, double[]? obs = null) =>
        new(DecisionPoint.Rest, n, 12, mask, _sight, null, null, obs ?? [1, 2]);

    [Fact]
    public void 가중치가_없으면_열린_칸에_같은_확률이고_가려진_칸은_안_뽑는다()
    {
        var c = new NetController(null, 51);
        bool[] mask = [true, false, true, true];
        var counts = new int[4];
        for (int n = 0; n < 3000; n++)
        {
            counts[c.Decide(D(n, mask))]++;
        }

        counts[1].ShouldBe(0);
        counts.Where((_, i) => mask[i]).ShouldAllBe(k => k > 900 && k < 1100);
        c.Steps.Count.ShouldBe(3000);
        c.Steps[0].LogProb.ShouldBe(Math.Log(1.0 / 3), 1e-12);
        c.Steps[0].Value.ShouldBe(0);
        (c.WantsObservation, c.ReactsToBombs).ShouldBe((true, false));
    }

    [Fact]
    public void 확률은_열린_칸의_로짓의_소프트맥스다()
    {
        // 작은 망의 로짓 (0, 3.5, 4.0) — 칸 1 을 가리면 (0, 4) 의 소프트맥스.
        PolicyNet net = PolicyNet.Parse(PolicyNetTests.Tiny, "시험", 2, 3, ["3연격"]);
        var c = new NetController(net, 7);
        bool[] mask = [true, false, true];
        var counts = new int[3];
        for (int n = 0; n < 4000; n++)
        {
            counts[c.Decide(D(n, mask))]++;
        }

        double p2 = Math.Exp(4) / (1 + Math.Exp(4));
        ((double)counts[2] / 4000).ShouldBe(p2, 0.02);
        NetStep last = c.Steps[^1];
        last.LogProb.ShouldBe(Math.Log(last.Action == 2 ? p2 : 1 - p2), 1e-12);
        last.Value.ShouldBe(9);
    }

    [Fact]
    public void 같은_시드_같은_번호면_같은_칸이다()
    {
        bool[] mask = [true, true, true, true];
        for (int n = 0; n < 20; n++)
        {
            new NetController(null, 3).Decide(D(n, mask)).ShouldBe(new NetController(null, 3).Decide(D(n, mask)));
        }
    }

    [Fact]
    public void 열린_칸이_하나면_적지_않는다()
    {
        var c = new NetController(null, 1);
        c.Decide(D(0, [true, false, false])).ShouldBe(0);
        c.Steps.ShouldBeEmpty();
    }
}
