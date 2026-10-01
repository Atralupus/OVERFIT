using System;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>보스의 형태 — 세 페이즈의 상태 (설계 2026-10-01 조각1 §2): 형태 · 바닥 · 전환 시계.</summary>
public class BossFormsTests
{
    private static BossForms Make(int start = 1200) => new([900, 400], 1200, start, 90);

    [Fact]
    public void 처음은_형태_1_바닥_900_이고_전환_중이_아니다()
    {
        BossForms f = Make();
        (f.Form, f.Count, f.Floor, f.Shifting).ShouldBe((1, 3, 900, false));
        f.Shifts.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(905, 1, 900)]
    [InlineData(899, 2, 400)]
    [InlineData(399, 3, 0)]
    public void 시작_체력이_든_칸이_시작_형태다(int start, int form, int floor)
    {
        BossForms f = Make(start);
        (f.Form, f.Floor).ShouldBe((form, floor));
    }

    [Theory]
    [InlineData(900)]
    [InlineData(400)]
    public void 시작_체력이_문턱과_같으면_거절한다(int start) =>
        Should.Throw<ArgumentException>(() => Make(start));

    [Theory]
    [InlineData(new[] { 400, 900 })]
    [InlineData(new[] { 900, 900 })]
    [InlineData(new[] { 1200 })]
    [InlineData(new[] { 0 })]
    public void 문턱은_내려가고_0_과_최대_사이다(int[] thresholds) =>
        Should.Throw<ArgumentException>(() => new BossForms(thresholds, 1200, 1100, 90));

    [Fact]
    public void 전환은_한_틱_이상이다() =>
        Should.Throw<ArgumentException>(() => new BossForms([900], 1200, 1200, 0));

    [Fact]
    public void 바닥에_닿으면_전환하고_시계가_끝나는_틱에_다음_형태다()
    {
        BossForms f = Make();
        f.Reached(901).ShouldBeFalse();
        f.Reached(900).ShouldBeTrue();
        f.Begin(500);
        (f.Shifting, f.ShiftLeft, f.Form).ShouldBe((true, 1.0, 1));
        f.Reached(900).ShouldBeFalse("전환 중에 또 전환한다");
        for (int i = 0; i < 89; i++)
        {
            f.Tick().ShouldBeFalse($"{i + 1}번째 틱에 끝났다");
        }

        f.Tick().ShouldBeTrue("90번째 틱에 안 끝났다");
        (f.Shifting, f.Form, f.Floor).ShouldBe((false, 2, 400));
        f.Shifts.ShouldBe(new[] { 500 });
    }

    [Fact]
    public void 마지막_형태는_바닥이_0_이라_다시_전환하지_않는다()
    {
        BossForms f = Make(300);
        (f.Form, f.Floor).ShouldBe((3, 0));
        f.Reached(0).ShouldBeFalse();
    }

    [Fact]
    public void 문턱이_없으면_한_형태다()
    {
        BossForms f = new([], 50, 50, 90);
        (f.Form, f.Count, f.Floor).ShouldBe((1, 1, 0));
    }
}
