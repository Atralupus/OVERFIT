using System;
using System.Collections.Generic;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Factory;

/// <summary>2단계 사례 하나 — 학습 표본의 한 줄(<c>samples.csv</c>).</summary>
/// <param name="Bot">봇 번호.</param>
/// <param name="Attempt">그 봇의 시도 번호(<see cref="RunHistory"/> — 1단계부터 이어 센다). 같은 시도의 사례는 입력이 같다.</param>
/// <param name="Slot">2단계 명부의 칸 — 망의 머리 순서다.</param>
/// <param name="Hit">라벨 — 그 사례에 맞았나(<see cref="InstanceTracker"/>).</param>
/// <param name="Features">그 시도를 시작할 때의 입력(<see cref="PlayerFeatures"/>). 같은 시도의 사례들이 한 배열을 같이 쥔다.</param>
public sealed record FactorySample(int Bot, int Attempt, int Slot, bool Hit, IReadOnlyList<double> Features);

/// <summary>봇 한 대의 결과 — <c>bots.csv</c> 의 한 줄과 그 봇의 표본.</summary>
/// <param name="Bot">봇 번호.</param>
/// <param name="Traits">성향 — 망의 입력이 아니다. 검증이 봇을 성향별로 자르는 데 쓴다(설계 §4.4).</param>
/// <param name="Stage1Attempts">1단계를 몇 번 쳤나.</param>
/// <param name="ReachedStage2">1단계를 이겼나 — 못 이긴 봇은 2단계 표본이 없다.</param>
/// <param name="Stage2Attempts">2단계를 몇 번 쳤나.</param>
/// <param name="WonStage2">2단계를 이겼나.</param>
/// <param name="Ticks">이 봇이 돈 틱의 합 — 처리량을 잰다.</param>
/// <param name="Samples">2단계 사례들 — 시도 순서 · 그 안에서 선 순서.</param>
public sealed record BotResult(
    int Bot, BotTraits Traits, int Stage1Attempts, bool ReachedStage2, int Stage2Attempts, bool WonStage2, long Ticks,
    IReadOnlyList<FactorySample> Samples);

/// <summary>
/// 봇 한 대를 게임과 같은 순서로 돌린다 (#108 · 설계 2026-09-28 §4.2). 봇의 세션 시드(<see cref="BotTraits.SessionSeed"/>) 위에 <see cref="RunHistory"/>
/// 를 세워 시도 시드를 받는다 — 게임이 시도를 여는 길 그대로다. 봇은 서로 독립이다(제 판 · 제 봇 · 제 기록) — 여러 스레드가 같이 불러도 된다.
///
/// <list type="number">
/// <item><b>1단계</b> — <see cref="StageRoster.Setup"/> 으로 세워(게임 · 데모와 같은 자리) 이길 때까지, 많아야 <c>stage1Tries</c> 번. 못 이긴 봇은 2단계에
/// 못 간다 — 사람도 그렇다.</item>
/// <item><b>2단계</b> — 이길 때까지, 많아야 <c>stage2Tries</c> 번. 입력은 시도를 시작할 때 그때까지의 기록으로 한 번 짓는다(게임의 고르기가 서는 그 자리 ·
/// 그 재료). 명부는 <see cref="StageRoster.For"/> 에서 읽고 고르기는 <see cref="UniformPicker"/> 로 <b>고정한다</b> — <c>stages.json</c> 의 2단계가 망이어도
/// 공장은 무작위라야 다섯 칸이 고르게 덮이고 라벨이 고르기에 안 기운다.</item>
/// </list>
/// 판을 끝내는 규칙(승패 · <c>max_ticks</c>)은 게임과 같다. 판이 끝나면 기록을 붙인다 — 이긴 판도.
/// </summary>
public static class BotRun
{
    /// <param name="fleetSeed">함대 시드.</param>
    /// <param name="bot">봇 번호.</param>
    /// <param name="tables">같이 쓰는 표.</param>
    /// <param name="stage1Tries">1단계 시도 상한.</param>
    /// <param name="stage2Tries">2단계 시도 상한.</param>
    /// <param name="recorded">기록을 붙일 때마다 부른다 — 테스트가 입력의 재료를 대 보는 자리다.</param>
    public static BotResult Run(
        ulong fleetSeed, int bot, FactoryTables tables, int stage1Tries, int stage2Tries, Action<AttemptRecord>? recorded = null)
    {
        ArgumentNullException.ThrowIfNull(tables);
        BotTraits traits = BotTraits.Sample(fleetSeed, bot, tables.Fleet);
        var history = new RunHistory(BotTraits.SessionSeed(fleetSeed, bot));
        long ticks = 0;

        int stage1 = 0;
        bool reached = false;
        while (!reached && stage1 < stage1Tries)
        {
            stage1++;
            (int number, ulong seed) = history.Open();
            StageSetup setup = StageRoster.Setup(tables.Stages, 1, seed, history.Records)
                ?? throw new InvalidOperationException("1단계가 안 선다 — [stage][E] 를 보라");
            (BattleOutcome outcome, BattleSim sim) = Play(tables, traits, seed, setup.PatternIds, setup.Picker, tracker: null);
            ticks += sim.Ticks;
            Record(history, recorded, new AttemptRecord(number, 1, seed, outcome, [.. sim.Events]));
            reached = outcome == BattleOutcome.Win;
        }

        var samples = new List<FactorySample>();
        int stage2 = 0;
        bool won = false;
        if (reached)
        {
            IReadOnlyList<string> roster = StageRoster.For(tables.Stages, 2);
            while (!won && stage2 < stage2Tries)
            {
                stage2++;
                (int number, ulong seed) = history.Open();
                double[] features = PlayerFeatures.From(history.Records);
                var tracker = new InstanceTracker();
                (BattleOutcome outcome, BattleSim sim) = Play(tables, traits, seed, roster, new UniformPicker(seed, roster.Count), tracker);
                ticks += sim.Ticks;
                foreach (PatternInstance instance in tracker.Finish(sim.Events))
                {
                    samples.Add(new FactorySample(bot, number, Slot(roster, instance.PatternId), instance.Hit, features));
                }

                Record(history, recorded, new AttemptRecord(number, 2, seed, outcome, [.. sim.Events]));
                won = outcome == BattleOutcome.Win;
            }
        }

        Log.Debug("factory", () => $"bot={bot} habit={traits.Habit} stage1={stage1} reached={reached} stage2={stage2} won={won}"
            + $" samples={samples.Count} ticks={ticks}");
        return new BotResult(bot, traits, stage1, reached, stage2, won, ticks, samples);
    }

    /// <summary>한 판을 끝까지 민다 — 사례를 가를 때는 틱마다 지금 패턴과 관측 수를 넘긴다.</summary>
    private static (BattleOutcome Outcome, BattleSim Sim) Play(
        FactoryTables tables, BotTraits traits, ulong seed, IReadOnlyList<string> roster, IPatternPicker picker, InstanceTracker? tracker)
    {
        var sim = new BattleSim(new BattleSetup
        {
            Arena = tables.Arena,
            Fighter = tables.Fighter,
            HitShapes = tables.HitShapes,
            Boss = tables.Boss,
            PatternIds = roster,
            Patterns = tables.Patterns,
            Seed = seed,
            Picker = picker,
            MaxTicks = tables.MaxTicks,
        });
        var fleetBot = new FleetBot(traits, seed, tables.Beats, tables.Fighter);
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(fleetBot.Next(sim));
            tracker?.Observe(sim.Boss.CurrentPattern, sim.Events.Count);
        }

        return (outcome.Value, sim);
    }

    private static void Record(RunHistory history, Action<AttemptRecord>? recorded, AttemptRecord record)
    {
        history.Record(record);
        recorded?.Invoke(record);
        Log.Debug("factory", () => $"attempt n={record.Number} stage={record.Stage} outcome={record.Outcome} events={record.Events.Count}");
    }

    /// <summary>명부의 칸 — 판이 명부에서 뽑은 id 라 늘 있다. 없으면 판이 명부 밖을 낸 것이다(규칙 위반).</summary>
    private static int Slot(IReadOnlyList<string> roster, string id)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            if (string.Equals(roster[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidOperationException($"사례의 패턴 {id} 가 2단계 명부에 없다");
    }
}
