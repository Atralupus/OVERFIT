using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Factory;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>
/// 봇 한 대의 흐름 (#108 · 설계 2026-09-28 §4.2) — 게임과 같은 순서: 1단계를 이길 때까지(많아야 몇 번) · 2단계를 이길 때까지(많아야 몇 번) ·
/// 2단계의 시도마다 그 앞의 기록으로 입력을 한 번 짓는다. 실제 데이터 · 실제 함대 설정으로 돈다.
/// </summary>
public class BotRunTests
{
    private const ulong _fleetSeed = 51;

    private static readonly Lazy<FactoryTables> _tables = new(() => FactoryTables.Load("data", "fleet.json"));

    private static FactoryTables Tables => _tables.Value;

    /// <summary>봇 번호 0 부터 <paramref name="wanted"/> 를 만족하는 첫 봇 — 64 대 안에 없으면 실패다(찾는 봇이 드물면 테스트가 말해야 한다).</summary>
    private static (BotResult Result, List<AttemptRecord> Records) First(int stage1Tries, int stage2Tries, Func<BotResult, bool> wanted)
    {
        for (int bot = 0; bot < 64; bot++)
        {
            var records = new List<AttemptRecord>();
            BotResult result = BotRun.Run(_fleetSeed, bot, Tables, stage1Tries, stage2Tries, records.Add);
            if (wanted(result))
            {
                return (result, records);
            }
        }

        throw new InvalidOperationException("봇 64 대 안에 찾는 봇이 없다");
    }

    [Fact]
    public void 일단계를_못_이긴_봇은_2단계_표본이_없다()
    {
        // 사람도 1단계를 못 넘으면 2단계에 못 간다 — 추론의 모집단과 같게, 수만 센다(설계 §12).
        (BotResult result, List<AttemptRecord> records) = First(stage1Tries: 1, stage2Tries: 5, r => !r.ReachedStage2);

        result.Stage1Attempts.ShouldBe(1);
        result.Stage2Attempts.ShouldBe(0);
        result.WonStage2.ShouldBeFalse();
        result.Samples.ShouldBeEmpty();
        records.ShouldHaveSingleItem().Outcome.ShouldBe(BattleOutcome.Lose);
    }

    [Fact]
    public void 이단계의_입력은_그_시도_앞의_기록이다()
    {
        // 게임의 고르기가 서는 그 자리 · 그 재료다 — 시도를 시작할 때 그때까지 끝난 시도 전부(1단계 · 앞선 2단계). 둘째 2단계 시도까지 본다:
        // 첫 시도는 1단계 기록만, 둘째는 첫 2단계 시도의 기록까지 든다.
        (BotResult result, List<AttemptRecord> records) = First(5, 5, r => r.ReachedStage2 && r.Stage2Attempts >= 2);

        int[] attempts = result.Samples.Select(s => s.Attempt).Distinct().ToArray();
        attempts.Length.ShouldBeGreaterThanOrEqualTo(2, "2단계 시도 둘 이상에서 표본이 나와야 한다");
        foreach (int attempt in attempts)
        {
            double[] expected = PlayerFeatures.From(records.Where(r => r.Number < attempt).ToList());
            foreach (FactorySample sample in result.Samples.Where(s => s.Attempt == attempt))
            {
                sample.Features.ShouldBe(expected, $"시도 {attempt} 의 입력");
            }

            records.Single(r => r.Number == attempt).Stage.ShouldBe(2);
        }
    }

    [Fact]
    public void 표본은_2단계의_사례뿐이고_칸은_명부_안이다()
    {
        // 칸 = 2단계 명부의 인덱스 — 망의 머리가 그 순서다(설계 §5.2). 1단계는 재는 자리라 표본이 없다.
        int roster = StageRoster.For(Tables.Stages, 2).Count;
        var slots = new HashSet<int>();
        foreach (int bot in FleetBots.ReachStage2.Take(8))
        {
            var records = new List<AttemptRecord>();
            BotResult result = BotRun.Run(_fleetSeed, bot, Tables, 5, 5, records.Add);
            foreach (FactorySample sample in result.Samples)
            {
                sample.Bot.ShouldBe(bot);
                sample.Slot.ShouldBeInRange(0, roster - 1);
                records.Single(r => r.Number == sample.Attempt).Stage.ShouldBe(2);
                slots.Add(sample.Slot);
            }
        }

        slots.Count.ShouldBe(roster, "2단계에 가는 봇 여덟 대의 사례가 명부의 칸을 다 덮어야 한다(uniform)");
    }

    [Fact]
    public void 기록은_시도마다_하나이고_수가_맞는다()
    {
        var records = new List<AttemptRecord>();
        BotResult result = BotRun.Run(_fleetSeed, 3, Tables, 5, 5, records.Add);

        records.Count.ShouldBe(result.Stage1Attempts + result.Stage2Attempts);
        records.Select(r => r.Number).ShouldBe(Enumerable.Range(1, records.Count));
        records.Count(r => r.Stage == 1).ShouldBe(result.Stage1Attempts);
        result.ReachedStage2.ShouldBe(records.Any(r => r.Stage == 1 && r.Outcome == BattleOutcome.Win));
        result.WonStage2.ShouldBe(records.Any(r => r.Stage == 2 && r.Outcome == BattleOutcome.Win));
        result.Traits.ShouldBe(BotTraits.Sample(_fleetSeed, 3, Tables.Fleet));
        result.Ticks.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 같은_봇은_같은_결과다()
    {
        BotResult a = BotRun.Run(_fleetSeed, 5, Tables, 5, 5);
        BotResult b = BotRun.Run(_fleetSeed, 5, Tables, 5, 5);

        (a.Stage1Attempts, a.ReachedStage2, a.Stage2Attempts, a.WonStage2, a.Ticks)
            .ShouldBe((b.Stage1Attempts, b.ReachedStage2, b.Stage2Attempts, b.WonStage2, b.Ticks));
        a.Samples.Count.ShouldBe(b.Samples.Count);
        for (int i = 0; i < a.Samples.Count; i++)
        {
            (a.Samples[i].Attempt, a.Samples[i].Slot, a.Samples[i].Hit).ShouldBe((b.Samples[i].Attempt, b.Samples[i].Slot, b.Samples[i].Hit));
            a.Samples[i].Features.ShouldBe(b.Samples[i].Features);
        }
    }
}
