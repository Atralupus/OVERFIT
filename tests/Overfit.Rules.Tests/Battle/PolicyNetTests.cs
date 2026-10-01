using System;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>정책망 (설계 2026-10-01 조각4 §3) — 가중치 JSON · 순전파(ReLU) · 칸 수가 틀리면 거절.</summary>
public class PolicyNetTests
{
    /// <summary>관측 2 → 은닉 2(ReLU) → 칸 3 · 가치는 관측 2 → 1.</summary>
    internal const string Tiny = """
        {
          "obs": 2, "actions": 3, "roster": ["3연격"],
          "policy": [
            { "w": [[1, -1], [0.5, 2]], "b": [0, -1] },
            { "w": [[1, 0], [0, 1], [1, 1]], "b": [0, 0, 0.5] }
          ],
          "value": [ { "w": [[2, 3]], "b": [1] } ]
        }
        """;

    [Fact]
    public void 순전파는_은닉에_ReLU_마지막은_선형이다()
    {
        // x = (1, 2): 은닉 = relu(1 − 2, 0.5 + 4 − 1) = (0, 3.5). 로짓 = (0, 3.5, 0 + 3.5 + 0.5). 가치 = 2 + 6 + 1.
        PolicyNet net = PolicyNet.Parse(Tiny, "시험", obs: 2, actions: 3, roster: ["3연격"]);
        (double[] logits, double value) = net.Forward([1, 2]);
        logits.ShouldBe(new[] { 0, 3.5, 4.0 });
        value.ShouldBe(9);
    }

    [Theory]
    [InlineData(3, 3, "3연격")]
    [InlineData(2, 4, "3연격")]
    [InlineData(2, 3, "돌진")]
    public void 관측_칸_수나_칸_배치나_명부가_판과_다르면_거절한다(int obs, int actions, string roster) =>
        Should.Throw<DataException>(() => PolicyNet.Parse(Tiny, "시험", obs, actions, [roster]));

    [Fact]
    public void 층의_모양이_이어지지_않으면_거절한다()
    {
        string broken = Tiny.Replace("[[1, 0], [0, 1], [1, 1]]", "[[1, 0, 9], [0, 1, 9], [1, 1, 9]]", StringComparison.Ordinal);
        Should.Throw<DataException>(() => PolicyNet.Parse(broken, "시험", 2, 3, ["3연격"]));
    }
}
