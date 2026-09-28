using System;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Factory;
using Overfit.Rules.Tests.Battle;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>
/// 원본의 기저율과 겨냥 표 (#108 · 설계 2026-09-28 §4.6) — 2번 PR 의 관문이 읽는 수다. 손으로 지은 봇 결과로 센다.
/// </summary>
public class FactoryStatsTests
{
    private static readonly string[] _roster = ["3연격", "점프 3연속", "1타 돌진", "1타 잡기", "엇박 3연격"];

    private static readonly TargetingRow[] _rows =
    [
        new() { Habit = BotHabit.Dash, Pattern = "1타 잡기" },
        new() { Habit = BotHabit.Parry, Pattern = "엇박 3연격", MinRhythm = 0.5 },
    ];

    private static readonly double[] _features = new double[19];

    /// <summary>봇 하나 — <paramref name="labels"/> 는 (칸, 맞았나) 들이다.</summary>
    private static BotResult Bot(int bot, BotHabit habit, double rhythm, params (int Slot, bool Hit)[] labels) =>
        new(
            bot, FleetPlay.Mid with { Habit = habit, Rhythm = rhythm }, 1, labels.Length > 0, labels.Length > 0 ? 1 : 0, false, 100,
            labels.Select(l => new FactorySample(bot, 2, l.Slot, l.Hit, _features)).ToList());

    [Fact]
    public void 칸마다_기저율을_센다()
    {
        var stats = new FactoryStats(_roster, _rows);
        stats.Add(Bot(0, BotHabit.Mixed, 0, (0, true), (0, false), (3, true)));
        stats.Add(Bot(1, BotHabit.Mixed, 0, (0, false), (3, true)));
        stats.Add(Bot(2, BotHabit.Mixed, 0));

        stats.Slots.Select(s => s.Pattern).ShouldBe(_roster);
        stats.Slots[0].Samples.ShouldBe(3);
        stats.Slots[0].Hits.ShouldBe(1);
        stats.Slots[0].Rate.ShouldBe(1.0 / 3);
        stats.Slots[3].Rate.ShouldBe(1.0);
        stats.Slots[1].Samples.ShouldBe(0);
        stats.Slots[1].Rate.ShouldBe(0, "사례가 없는 칸은 0 이다 — NaN 을 매니페스트에 안 싣는다");
        (stats.Bots, stats.ReachedStage2, stats.Samples, stats.Ticks).ShouldBe((3, 2, 5L, 300L));
    }

    [Fact]
    public void 겨냥은_그_습관형의_그_칸만_센다()
    {
        // 대시 습관형의 1타 잡기 — 셋 중 둘 맞음. 전체의 1타 잡기 — 다섯 중 둘. 대시 습관형의 다른 칸 · 다른 습관형의 1타 잡기는 겨냥에 안 든다.
        var stats = new FactoryStats(_roster, _rows);
        stats.Add(Bot(0, BotHabit.Dash, 0, (3, true), (3, false), (0, true)));
        stats.Add(Bot(1, BotHabit.Dash, 0, (3, true)));
        stats.Add(Bot(2, BotHabit.Guard, 0, (3, false), (3, false)));

        TargetingResult dash = stats.Targeting[0];
        (dash.Bots, dash.Samples, dash.Hits).ShouldBe((2, 3, 2));
        dash.Rate.ShouldBe(2.0 / 3);
        dash.BaseRate.ShouldBe(2.0 / 5);
        dash.Diff.ShouldBe((2.0 / 3) - (2.0 / 5));
        dash.Above.ShouldBeTrue();
    }

    [Fact]
    public void 패리의_겨냥은_리듬이_문턱_이상인_봇만_센다()
    {
        // 눈으로 누르는 패리(리듬 0.2)는 엇박에 안 속는다 — 그 봇의 엇박 사례는 겨냥 줄에 안 든다(기저율에는 든다).
        var stats = new FactoryStats(_roster, _rows);
        stats.Add(Bot(0, BotHabit.Parry, 0.2, (4, false), (4, false)));
        stats.Add(Bot(1, BotHabit.Parry, 0.5, (4, true)));

        TargetingResult parry = stats.Targeting[1];
        (parry.Bots, parry.Samples, parry.Hits).ShouldBe((1, 1, 1));
        parry.BaseRate.ShouldBe(1.0 / 3);
    }

    [Fact]
    public void 사례가_없는_줄은_넘지_못한다()
    {
        // 잴 것이 없으면 "기저율보다 높다" 가 안 선다 — 관문은 그것을 통과로 치지 않는다.
        var stats = new FactoryStats(_roster, _rows);
        stats.Add(Bot(0, BotHabit.Mixed, 0, (3, false)));

        stats.Targeting[0].Samples.ShouldBe(0);
        stats.Targeting[0].Above.ShouldBeFalse();
    }

    [Fact]
    public void 명부_밖의_겨냥은_세울_때_거절한다()
    {
        TargetingRow[] rows = [new() { Habit = BotHabit.Dash, Pattern = "없는 패턴" }];

        Should.Throw<ArgumentException>(() => new FactoryStats(_roster, rows)).Message.ShouldContain("없는 패턴");
    }
}
