using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>결과 화면의 페이즈 줄 (설계 2026-10-01 조각1 §3) — 페이즈마다 걸린 시간. 전환(무적)은 뒤 페이즈의 시간에 든다.</summary>
public class FormReportTests
{
    [Fact]
    public void 끝까지_간_판은_세_페이즈의_시간을_적는다() =>
        // 전환 시작 2520(0:42) · 6720 · 끝 9000. 페이즈 2 는 2520 ~ 6720 = 4200틱 = 1:10, 3 은 6720 ~ 9000 = 2280틱 = 0:38.
        FormReport.Line([2520, 6720], 9000, 3).ShouldBe("페이즈 1 0:42 · 2 1:10 · 3 0:38");

    [Fact]
    public void 못_간_페이즈는_줄표다() =>
        FormReport.Line([2520], 4000, 3).ShouldBe("페이즈 1 0:42 · 2 0:24 · 3 —");

    [Fact]
    public void 첫_페이즈에서_끝나면_뒤는_다_줄표다() =>
        FormReport.Line([], 600, 3).ShouldBe("페이즈 1 0:10 · 2 — · 3 —");

    [Fact]
    public void 한_형태면_빈_줄이다() =>
        FormReport.Line([], 4000, 1).ShouldBe("");
}
