using System;
using System.Collections.Generic;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Factory;

/// <summary>
/// 사례 하나 — 표본의 한 줄(<c>samples.csv</c>). 옛 망의 입력 19칸(<c>PlayerFeatures</c>)은 옛 망과 같이 걷었다(설계 2026-09-29 조각1 §6) —
/// 조각 4 의 망은 입력이 다르다(최근 흐름 + 누적 성향).
/// </summary>
/// <param name="Bot">봇 번호.</param>
/// <param name="Attempt">그 봇의 시도 번호(<see cref="RunHistory"/>).</param>
/// <param name="Slot">명부의 칸.</param>
/// <param name="Hit">라벨 — 그 사례에 맞았나(<see cref="InstanceTracker"/>).</param>
public sealed record FactorySample(int Bot, int Attempt, int Slot, bool Hit);

/// <summary>봇 한 대의 결과 — <c>bots.csv</c> 의 한 줄과 그 봇의 표본.</summary>
/// <param name="Bot">봇 번호.</param>
/// <param name="Traits">성향 — 분석이 봇을 성향별로 자르는 데 쓴다(설계 2026-09-28 §4.4).</param>
/// <param name="Attempts">보스전을 몇 번 쳤나.</param>
/// <param name="Won">이겼나 — 이기면 멈춘다.</param>
/// <param name="Ticks">이 봇이 돈 틱의 합 — 처리량을 잰다.</param>
/// <param name="Samples">사례들 — 시도 순서 · 그 안에서 선 순서.</param>
public sealed record BotResult(int Bot, BotTraits Traits, int Attempts, bool Won, long Ticks, IReadOnlyList<FactorySample> Samples);

/// <summary>
/// 봇 한 대를 게임과 같은 순서로 돌린다 (#108 · 설계 2026-09-28 §4.2). 봇의 세션 시드(<see cref="BotTraits.SessionSeed"/>) 위에 <see cref="RunHistory"/>
/// 를 세워 시도 시드를 받는다 — 게임이 시도를 여는 길 그대로다. 봇은 서로 독립이다(제 판 · 제 봇 · 제 기록) — 여러 스레드가 같이 불러도 된다.
///
/// <para>
/// 보스전이 하나다(설계 2026-09-29 조각1 §1) — 이길 때까지, 많아야 <c>tries</c> 번 치고 판마다 사례를 싣는다. 옛 공장은 1단계를 넘은 봇만 2단계의 사례를
/// 실었다. 명부는 <see cref="StageRoster.For"/> 에서 읽고 고르기는 <see cref="UniformPlanPicker"/> 로 <b>고정한다</b> — 데이터의 고르기가 바뀌어도(조각 4 의
/// 망) 공장은 무작위라야 칸이 고르게 덮이고 라벨이 고르기에 안 기운다. 끊는 몫 · 쉬는 길이는 게임과 같다(<c>balance.json</c> 의 <c>picker</c> ·
/// 보스의 <c>rest_seconds</c> · 설계 2026-09-29 조각1 §3.5) — 잇는 동작도 사례다. 판을 끝내는 규칙(승패 · <c>max_ticks</c>)은 게임과 같다. 판이 끝나면 기록을
/// 붙인다 — 이긴 판도.
/// </para>
/// </summary>
public static class BotRun
{
    /// <summary>보스전의 단계 — <c>stages.json</c> 의 유일한 키다(설계 2026-09-29 조각1 §1).</summary>
    private const int _stage = 1;

    /// <param name="fleetSeed">함대 시드.</param>
    /// <param name="bot">봇 번호.</param>
    /// <param name="tables">같이 쓰는 표.</param>
    /// <param name="tries">시도 상한.</param>
    /// <param name="recorded">기록을 붙일 때마다 부른다 — 테스트가 기록을 대 보는 자리다.</param>
    public static BotResult Run(ulong fleetSeed, int bot, FactoryTables tables, int tries, Action<AttemptRecord>? recorded = null)
    {
        ArgumentNullException.ThrowIfNull(tables);
        BotTraits traits = BotTraits.Sample(fleetSeed, bot, tables.Fleet);
        var history = new RunHistory(BotTraits.SessionSeed(fleetSeed, bot));
        IReadOnlyList<string> roster = StageRoster.For(tables.Stages, _stage);
        var samples = new List<FactorySample>();
        int attempts = 0;
        bool won = false;
        long ticks = 0;
        while (!won && attempts < tries)
        {
            attempts++;
            (int number, ulong seed) = history.Open();
            var tracker = new InstanceTracker();
            var picker = new UniformPlanPicker(
                new PickerInputs(roster, tables.Patterns, tables.RestTicks, tables.Knobs, Array.Empty<AttemptRecord>(), seed));
            (BattleOutcome outcome, BattleSim sim) = Play(tables, traits, seed, roster, picker, tracker);
            ticks += sim.Ticks;
            foreach (PatternInstance instance in tracker.Finish(sim.Events))
            {
                samples.Add(new FactorySample(bot, number, Slot(roster, instance.PatternId), instance.Hit));
            }

            Record(history, recorded, new AttemptRecord(number, _stage, seed, outcome, [.. sim.Events]));
            won = outcome == BattleOutcome.Win;
        }

        Log.Debug("factory", () => $"bot={bot} habit={traits.Habit} attempts={attempts} won={won} samples={samples.Count} ticks={ticks}");
        return new BotResult(bot, traits, attempts, won, ticks, samples);
    }

    /// <summary>한 판을 끝까지 민다 — 틱마다 지금 패턴과 관측 수를 넘겨 사례를 가른다.</summary>
    private static (BattleOutcome Outcome, BattleSim Sim) Play(
        FactoryTables tables, BotTraits traits, ulong seed, IReadOnlyList<string> roster, IPlanPicker picker, InstanceTracker tracker)
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
            tracker.Observe(sim.Boss.CurrentPattern, sim.Events.Count);
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

        throw new InvalidOperationException($"사례의 패턴 {id} 가 명부에 없다");
    }
}
