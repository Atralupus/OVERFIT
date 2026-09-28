using System;
using System.Collections.Generic;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 리듬형이 따르는 시각표 (#104 · 설계 2026-09-28 §3.3). 여는 그림(첫 단계의 anim · frame)이 기준 패턴과 같으면 그 기준의 판정 시각을 쓴다 —
/// 사람은 여는 동작이 같은 공격을 박자로 구별하지 못한다. 엇박 3연격이 3연격과 여는 그림이 같고 박자만 늦은 것이 바로 그 자리다.
/// </summary>
public class BeatTableTests
{
    private static readonly string[] _references = ["3연격", "점프 공격"];

    private static BeatTable Real() => new(TestConfigs.Patterns(), _references);

    [Theory]
    [InlineData("엇박 3연격")]
    [InlineData("1타 잡기")]
    [InlineData("1타 돌진")]
    [InlineData("3연격")]
    public void 여는_그림이_3연격과_같으면_3연격의_시각표를_받는다(string id)
    {
        // 엇박의 실제 판정은 1.00 · 1.85 · 3.10 이다 — 박자로 누르는 사람은 타마다 0.15 · 0.30 · 0.45 초 이르다.
        Real().For(id).ShouldBe(new[] { 0.85, 1.55, 2.65 });
    }

    [Fact]
    public void 점프_3연속은_점프_공격의_시각표를_받는다()
    {
        // 기준은 착지 하나다 — 둘째 · 셋째 착지는 봇이 눈으로 잰다(시각표가 모자란 칸).
        Real().For("점프 3연속").ShouldBe(new[] { 1.0 });
    }

    [Fact]
    public void 여는_그림이_다르면_없다()
    {
        var patterns = new Dictionary<string, PatternDef>(TestConfigs.Patterns()) { ["시험"] = TestConfigs.Sweep(300, 0.1) };

        new BeatTable(patterns, _references).For("시험").ShouldBeNull();
    }

    [Fact]
    public void 명부에_없는_패턴은_없다()
    {
        Real().For("없는 패턴").ShouldBeNull();
    }

    [Fact]
    public void 모르는_기준은_세울_때_거절한다()
    {
        // fleet.json 은 사람이 적는다 — 오타는 세우는 자리에서 멈춘다. 조용히 빈 시각표로 서면 리듬 성향이 아무 일도 안 하는 함대가 된다.
        Should.Throw<ArgumentException>(() => new BeatTable(TestConfigs.Patterns(), ["없는 기준"]))
            .Message.ShouldContain("없는 기준");
    }
}
