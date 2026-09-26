using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 패턴 고르기 (#72 · 설계 §4.4). <c>BattleSim.Begin</c> 이 부르는 자리 하나이고, 등록표(<see cref="PatternPickers"/>)가
/// <c>stages.json</c> 의 <c>picker</c> id 로 구현을 세운다. 3번 PR 에는 <c>uniform</c> 하나다 — 망은 나중에 구현 하나를 더한다.
/// </summary>
public class PatternPickerTests
{
    private static readonly string[] _roster = { "3연격", "점프 공격" };

    private static PickerInputs Inputs(ulong seed, IReadOnlyList<string>? roster = null) =>
        new(roster ?? _roster, Array.Empty<AttemptRecord>(), seed, 1);

    [Fact]
    public void Uniform_은_옛_뽑기와_비트까지_같다()
    {
        // 인터페이스를 끼우는 것만으로는 순서가 안 움직여야 한다 — 골든을 움직이는 것은 새 패턴이지 이 자리가 아니다.
        // 기준은 옛 BattleSim.Begin 의 한 줄이다: Det.RollInt(시드, PatternPick, 명부 수, k1: 몇 번째).
        foreach (ulong seed in new ulong[] { 0, 51, ulong.MaxValue, Det.Hash64(51, Det.Domain.Attempt, k1: 1) })
        {
            var picker = new UniformPicker(seed, _roster.Length);
            for (int draw = 0; draw < 500; draw++)
            {
                picker.Pick(draw).ShouldBe(Det.RollInt(seed, Det.Domain.PatternPick, _roster.Length, k1: draw));
            }
        }
    }

    [Fact]
    public void 등록표가_uniform_을_명부의_크기로_세운다()
    {
        IPatternPicker picker = PatternPickers.Create("uniform", Inputs(51, new[] { "a", "b", "c" })).ShouldNotBeNull();

        Enumerable.Range(0, 300).Select(picker.Pick).Distinct().Order().ShouldBe(new[] { 0, 1, 2 });
    }

    [Fact]
    public void 모르는_고르기는_세우지_않는다()
    {
        PatternPickers.Ids.ShouldContain("uniform");
        PatternPickers.Create("없는고르기", Inputs(51)).ShouldBeNull();
    }

    [Fact]
    public void 고르기를_안_주면_시드_위의_uniform_이다()
    {
        // BattleSetup 의 고르기 칸은 선택이다 — 비우면 (Seed, PatternIds) 위의 uniform. 그래서 고르기를 모르는 테스트의
        // BattleSetup 들이 그대로 선다. 실제 1단계 명부로 입력 없이 끝까지 돌려 패턴이 서는 순서를 견준다.
        List<string> Begins(IPatternPicker? picker)
        {
            var sim = new BattleSim(new BattleSetup
            {
                Arena = TestConfigs.Arena(),
                Fighter = TestConfigs.Fighter(),
                HitShapes = TestConfigs.HitShapes(),
                Boss = TestConfigs.Boss(),
                PatternIds = _roster,
                Patterns = TestConfigs.Patterns(),
                Seed = 51,
                Picker = picker,
                MaxTicks = TestConfigs.MaxTicks(),
            });

            var begins = new List<string>();
            string? last = null;
            while (sim.Tick(default) is null)
            {
                if (sim.Boss.CurrentPattern is { } id && id != last)
                {
                    begins.Add(id);
                }

                last = sim.Boss.CurrentPattern;
            }

            return begins;
        }

        List<string> none = Begins(null);
        none.Count.ShouldBeGreaterThan(3);
        none.ShouldBe(Begins(new UniformPicker(51, _roster.Length)));
    }

    [Fact]
    public void 명부_밖의_칸을_낸_고르기는_규칙_위반을_남기고_쉬었다_다시_고른다()
    {
        // 망이 들어오면 고르기는 데이터(기록)를 읽는다 — 명부 밖의 칸은 그쪽 버그다. 예외로 터지면 엔진의 ERROR 블록으로만
        // 나오므로(StageRoster 와 같은 이유) [E] 로 남기고, 간격을 다시 세어 매 틱 쏟지 않는다.
        using var log = new LogCapture();
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(),
            PatternIds = _roster,
            Patterns = TestConfigs.Patterns(),
            Seed = 51,
            Picker = new Fixed(5),
            MaxTicks = 200,
        });

        while (sim.Tick(default) is null)
        {
        }

        sim.Boss.CurrentPattern.ShouldBeNull();
        List<string> errors = log.Lines.Where(l => l.StartsWith("[boss][E] pick_out_of_range", StringComparison.Ordinal)).ToList();
        errors.Count.ShouldBe(4, "200틱에 간격 48틱마다 한 번 — 매 틱이 아니다");
        errors[0].ShouldBe("[boss][E] pick_out_of_range index=5 roster=2 draw=0 tick=48");
    }

    /// <summary>늘 같은 칸을 내는 고르기 — 명부 밖을 내게 해서 규칙 위반의 자리를 본다.</summary>
    private sealed class Fixed(int index) : IPatternPicker
    {
        public int Pick(int draw) => index;
    }
}
