using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Factory;

/// <summary>평가의 사례 하나 — 2단계 명부의 칸과 라벨(<see cref="InstanceTracker"/>).</summary>
public readonly record struct EvalInstance(int Slot, bool Hit);

/// <summary>평가의 2단계 시도 하나 (#114 · 설계 2026-09-28 §7.1).</summary>
/// <param name="Number">시도 번호 — 두 갈래가 같은 번호 · 같은 시드에서 갈라진다.</param>
/// <param name="Seed">시도 시드.</param>
/// <param name="Samples">시도를 시작할 때의 관측 수(<see cref="PlayerFeatures.SamplesIndex"/>) — 망이 <c>min_samples</c> 와 견주는 값.</param>
/// <param name="Logits">
/// 시도를 시작할 때의 입력으로 낸 로짓 — <b>두 갈래 모두</b> 셈한다. 보정(ECE)은 뽑힌 사례마다 그 칸의 예측이 있어야 하고, 무작위 갈래는 다섯 칸을
/// 고르게 덮는다.
/// </param>
/// <param name="Decision">망 갈래의 결정(좁힌 명부 · 숨통 · 까닭). 무작위 갈래는 null.</param>
/// <param name="Outcome">결과.</param>
/// <param name="Ticks">판의 길이.</param>
/// <param name="Instances">사례들 — 선 순서.</param>
public sealed record EvalAttempt(
    int Number, ulong Seed, int Samples, IReadOnlyList<double> Logits, PickDecision? Decision, BattleOutcome Outcome, int Ticks,
    IReadOnlyList<EvalInstance> Instances);

/// <summary>한 갈래 — 그 갈래로 친 2단계 시도들과 이겼나.</summary>
public sealed record EvalArm(string Arm, IReadOnlyList<EvalAttempt> Attempts, bool Won);

/// <summary>봇 한 대의 평가 — 1단계 한 번 · 2단계 두 갈래.</summary>
public sealed record EvalResult(int Bot, BotTraits Traits, int Stage1Attempts, bool ReachedStage2, EvalArm Network, EvalArm Uniform, long Ticks);

/// <summary>
/// 같은 봇에게 망 보스와 무작위 보스 (#114 · 설계 2026-09-28 §7.1). 봇 한 대가 1단계를 <b>한 번</b> 치고(<see cref="BotRun.Stage1"/> — 공장과 같은
/// 자리), 이겼으면 2단계를 망 갈래로 한 번 · 무작위 갈래로 한 번 친다. 두 갈래는 같은 1단계 기록 · 같은 시도 번호 · 같은 시도 시드에서 갈라진다 —
/// 짝지은 비교라 봇마다의 차이가 지워진다. 각 갈래 안에서는 게임처럼 앞의 2단계 시도가 다음 시도의 기록이 된다.
///
/// <para>
/// 판은 <see cref="StageRoster.Setup"/> 이 갈래를 정해 받아 세운다 — 평가만의 길로 망 갈래를 세우면 게임과 다른 고르기를 잰다. 그래서 무작위 갈래는
/// 공장(<see cref="BotRun"/>)의 2단계와 같은 판이다: 같은 시드 · 같은 <see cref="UniformPicker"/> · 같은 봇.
/// </para>
/// </summary>
public static class EvalRun
{
    /// <param name="fleetSeed">함대 시드.</param>
    /// <param name="bot">봇 번호 — 학습에 안 쓴 번호여야 한다(평가 모드가 막는다).</param>
    /// <param name="tables">같이 쓰는 표.</param>
    /// <param name="network">망과 고르기의 수치 — 게임이 부팅 때 읽는 그것.</param>
    /// <param name="stage1Tries">1단계 시도 상한.</param>
    /// <param name="stage2Tries">갈래마다의 2단계 시도 상한.</param>
    public static EvalResult Run(ulong fleetSeed, int bot, FactoryTables tables, NetworkContext network, int stage1Tries, int stage2Tries)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(network);
        BotTraits traits = BotTraits.Sample(fleetSeed, bot, tables.Fleet);
        var history = new RunHistory(BotTraits.SessionSeed(fleetSeed, bot));
        (int stage1, bool reached, long ticks) = BotRun.Stage1(tables, traits, history, stage1Tries, recorded: null);

        EvalArm net = reached ? Stage2(tables, traits, history, network, NetworkPicker.Id, stage2Tries) : new EvalArm(NetworkPicker.Id, [], false);
        EvalArm uniform = reached ? Stage2(tables, traits, history, network, StageRoster.UniformArm, stage2Tries) : new EvalArm(StageRoster.UniformArm, [], false);
        ticks += net.Attempts.Sum(a => (long)a.Ticks) + uniform.Attempts.Sum(a => (long)a.Ticks);
        Log.Debug("factory", () => $"eval bot={bot} habit={traits.Habit} stage1={stage1} reached={reached}"
            + $" network={net.Attempts.Count}/{net.Won} uniform={uniform.Attempts.Count}/{uniform.Won}");
        return new EvalResult(bot, traits, stage1, reached, net, uniform, ticks);
    }

    /// <summary>
    /// 한 갈래의 2단계 — 1단계 기록의 사본 위에서 이길 때까지, 많아야 <paramref name="tries"/> 번. <paramref name="history"/> 는 안 고친다(두 갈래가 같은
    /// 1단계에서 갈라져야 한다) — 번호와 시드는 거기서 이어 센다.
    /// </summary>
    private static EvalArm Stage2(FactoryTables tables, BotTraits traits, RunHistory history, NetworkContext network, string arm, int tries)
    {
        var records = new List<AttemptRecord>(history.Records);
        var attempts = new List<EvalAttempt>();
        int number = history.Attempts;
        bool won = false;
        while (!won && attempts.Count < tries)
        {
            number++;
            ulong seed = history.SeedOf(number);
            double[] features = PlayerFeatures.From(records);
            double[] logits = network.Net.Logits(features);
            StageSetup setup = StageRoster.Setup(tables.Stages, 2, seed, records, script: null, network, arm)
                ?? throw new InvalidOperationException($"2단계가 {arm} 갈래로 안 선다 — [stage][E] 를 보라");
            var tracker = new InstanceTracker();
            (BattleOutcome outcome, BattleSim sim) = BotRun.Play(tables, traits, seed, setup.PatternIds, setup.Picker, tracker);
            EvalInstance[] instances = [.. tracker.Finish(sim.Events).Select(i => new EvalInstance(BotRun.Slot(setup.PatternIds, i.PatternId), i.Hit))];
            records.Add(new AttemptRecord(number, setup.Stage, seed, outcome, [.. sim.Events], setup.Arm));
            attempts.Add(new EvalAttempt(number, seed, (int)features[PlayerFeatures.SamplesIndex], logits, setup.Decision, outcome, sim.Ticks, instances));
            won = outcome == BattleOutcome.Win;
        }

        return new EvalArm(arm, attempts, won);
    }
}
