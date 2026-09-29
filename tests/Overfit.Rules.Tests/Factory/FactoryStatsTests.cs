using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Factory;
using Overfit.Rules.Tests.Battle;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>
/// 원본의 기저율 (#108 · 설계 2026-09-28 §4.4) — 손으로 지은 봇 결과로 센다. 옛 망의 겨냥 표는 옛 망과 같이 걷었다(설계 2026-09-29 조각1 §6).
/// </summary>
public class FactoryStatsTests
{
    private static readonly string[] _roster = ["3연격", "점프 3연속", "1타 돌진", "1타 잡기", "엇박 3연격"];

    /// <summary>봇 하나 — <paramref name="labels"/> 는 (칸, 맞았나) 들이다.</summary>
    private static BotResult Bot(int bot, BotHabit habit, double rhythm, params (int Slot, bool Hit)[] labels) =>
        new(
            bot, FleetPlay.Mid with { Habit = habit, Rhythm = rhythm }, 1, labels.Length > 0, labels.Length > 0 ? 1 : 0, false, 100,
            labels.Select(l => new FactorySample(bot, 2, l.Slot, l.Hit)).ToList());

    [Fact]
    public void 칸마다_기저율을_센다()
    {
        var stats = new FactoryStats(_roster);
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
}
