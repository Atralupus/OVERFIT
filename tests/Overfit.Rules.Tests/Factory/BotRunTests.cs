using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Factory;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>
/// 봇 한 대의 흐름 (#108 · 설계 2026-09-28 §4.2) — 게임과 같은 순서로 보스전을 이길 때까지(많아야 몇 번) 친다. 보스전이 하나라(설계 2026-09-29
/// 조각1 §1) 판마다 사례가 선다. 실제 데이터 · 실제 함대 설정으로 돈다.
/// </summary>
public class BotRunTests
{
    private const ulong _fleetSeed = 51;

    private static readonly Lazy<FactoryTables> _tables = new(() => FactoryTables.Load("data", "fleet.json"));

    private static FactoryTables Tables => _tables.Value;

    [Fact]
    public void 표본은_판마다의_사례이고_칸은_명부_안이다()
    {
        // 칸 = 명부의 인덱스다. 판마다 사례가 서므로 봇 여덟 대면 명부의 칸을 다 덮는다(uniform).
        int roster = StageRoster.For(Tables.Stages, 1).Count;
        var slots = new HashSet<int>();
        for (int bot = 0; bot < 8; bot++)
        {
            var records = new List<AttemptRecord>();
            BotResult result = BotRun.Run(_fleetSeed, bot, Tables, 3, records.Add);
            result.Samples.ShouldNotBeEmpty($"봇 {bot} 의 판에서 사례가 하나도 안 섰다");
            foreach (FactorySample sample in result.Samples)
            {
                sample.Bot.ShouldBe(bot);
                sample.Slot.ShouldBeInRange(0, roster - 1);
                records.ShouldContain(r => r.Number == sample.Attempt);
                slots.Add(sample.Slot);
            }
        }

        slots.Count.ShouldBe(roster, "봇 여덟 대의 사례가 명부의 칸을 다 덮어야 한다(uniform)");
    }

    [Fact]
    public void 기록은_시도마다_하나이고_이기면_멈춘다()
    {
        var records = new List<AttemptRecord>();
        BotResult result = BotRun.Run(_fleetSeed, 3, Tables, 5, records.Add);

        records.Count.ShouldBe(result.Attempts);
        records.Select(r => r.Number).ShouldBe(Enumerable.Range(1, records.Count));
        records.ShouldAllBe(r => r.Stage == 1);
        result.Won.ShouldBe(records.Any(r => r.Outcome == BattleOutcome.Win));
        records.Take(records.Count - 1).ShouldAllBe(r => r.Outcome == BattleOutcome.Lose, "이긴 뒤에 시도를 또 열었다");
        (result.Won || result.Attempts == 5).ShouldBeTrue("지고도 상한 전에 멈췄다");
        result.Traits.ShouldBe(BotTraits.Sample(_fleetSeed, 3, Tables.Fleet));
        result.Ticks.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 같은_봇은_같은_결과다()
    {
        BotResult a = BotRun.Run(_fleetSeed, 5, Tables, 3);
        BotResult b = BotRun.Run(_fleetSeed, 5, Tables, 3);

        (a.Attempts, a.Won, a.Ticks).ShouldBe((b.Attempts, b.Won, b.Ticks));
        a.Samples.ShouldBe(b.Samples);
    }
}
