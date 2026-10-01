using System;
using System.Collections.Generic;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Factory;

/// <summary>보상의 가중치 (설계 2026-10-01 조각4 §5) — <c>ml/rl/train.json</c> 의 <c>reward</c>.</summary>
public sealed class RewardDef
{
    public required double WDealt { get; init; }

    public required double WTaken { get; init; }

    public required double WTime { get; init; }

    public required double WWin { get; init; }
}

/// <summary>일꾼의 보스 조종기 (설계 2026-10-01 조각5 §3) — 망은 경험을 적고, 규칙 · 무작위는 비교의 대조군이라 판의 결과만 낸다.</summary>
public enum RolloutController
{
    Net,
    Rule,
    Random,
}

/// <summary>경험의 줄 하나 — 망 조종기의 결정에 보상과 끝을 붙인 것.</summary>
/// <remarks><c>Span</c> 은 이 결정에서 다음 적은 결정(또는 판의 끝 다음 틱)까지의 틱이다 — 학습기가 시간으로 할인한다(γ^(틱/12)).</remarks>
public sealed record RolloutStep(
    int Tick, IReadOnlyList<double> Observation, IReadOnlyList<bool> Mask, int Action, double LogProb, double Value, double Reward, bool Done, int Span);

/// <summary>판 하나의 경험.</summary>
/// <param name="Index">판 번호.</param>
/// <param name="Habit">상대 봇의 버릇 — <c>episodes.csv</c> 에 싣는다.</param>
/// <param name="Outcome">판의 결과 — 게임의 말이라 파이터가 이기면 Win 이다.</param>
/// <param name="BossWon">보스가 이겼나 — 파이터가 쓰러졌다. 시간 초과는 보스의 짐이다(판을 끄는 것을 막는다).</param>
/// <param name="Ticks">판의 길이.</param>
/// <param name="BossLost">보스가 잃은 체력.</param>
/// <param name="FighterLost">파이터가 잃은 체력.</param>
/// <param name="Steps">적은 결정들.</param>
public sealed record Episode(
    int Index, string Habit, BattleOutcome Outcome, bool BossWon, int Ticks, int BossLost, int FighterLost, IReadOnlyList<RolloutStep> Steps);

/// <summary>
/// 학습의 일꾼이 판 하나를 돈다 (설계 2026-10-01 조각4 §5 · §6). 보스는 망 조종기(<see cref="NetController"/>), 상대는 봇 함대의 봇 하나 — 판 번호로 성향과
/// 시드가 정해진다(<c>Det.Domain.Rollout</c>). 같은 (시드, 판)이면 같은 경험이다.
///
/// <para>
/// <b>보상</b>: 결정 i 의 보상은 그 결정의 틱부터 다음 결정의 틱 앞까지 생긴 일이다 — 결정은 판의 틱 안(보스를 미는 자리)에서 묻고 칼 · 폭탄은 그 뒤에
/// 닿으므로, 결정의 틱에 닿은 피해는 그 결정의 것이다. 첫 결정 앞의 피해(판이 선 뒤 12틱)도 첫 결정에 붙여 합이 판의 체력 변화와 같게 한다. 판의 끝(이김 ·
/// 짐 · 시간 초과)은 마지막 결정에 붙는다.
/// </para>
/// </summary>
public static class RolloutRun
{
    private const int _stage = 1;

    /// <summary>
    /// 보스가 이겼나 — 판의 결과가 짐(게임의 말 · 파이터 쪽)이고 파이터가 쓰러졌다. 같은 틱에 둘 다 쓰러지면 게임은 파이터의 승리다(<c>BattleSim.Outcome</c> 이
    /// 보스의 죽음을 먼저 본다) — 그 판을 보스의 승리로 치면 가장 큰 보상의 부호가 뒤집힌다(최종 리뷰가 밟았다). 시간 초과는 보스의 짐이다.
    /// </summary>
    public static bool BossWon(BattleOutcome outcome, bool fighterAlive) => outcome == BattleOutcome.Lose && !fighterAlive;

    public static Episode Play(
        FactoryTables tables, PolicyNet? net, RewardDef reward, ulong seed, int episode, RolloutController kind = RolloutController.Net)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(reward);
        BotTraits traits = BotTraits.Sample(seed, episode, tables.Fleet);
        ulong battleSeed = Det.Hash64(seed, Det.Domain.Rollout, k1: episode, k2: 0);
        ulong pickSeed = Det.Hash64(seed, Det.Domain.Rollout, k1: episode, k2: 1);
        IReadOnlyList<string> roster = StageRoster.For(tables.Stages, _stage);
        var netController = kind == RolloutController.Net ? new NetController(net, pickSeed) : null;
        IBossController controller = kind switch
        {
            RolloutController.Rule => new RuleController(
                new UniformPlanPicker(new PickerInputs(roster, tables.Patterns, tables.RestTicks, tables.Knobs, Array.Empty<AttemptRecord>(), battleSeed)),
                new BossActions(roster), roster, tables.Patterns, tables.RestTicks),
            RolloutController.Random => new RandomController(pickSeed),
            _ => netController!,
        };
        var sim = new BattleSim(new BattleSetup
        {
            Arena = tables.Arena,
            Fighter = tables.Fighter,
            HitShapes = tables.HitShapes,
            Boss = tables.Boss,
            PatternIds = roster,
            Patterns = tables.Patterns,
            Seed = battleSeed,
            Controller = controller,
            MaxTicks = tables.MaxTicks,
        });
        var bot = new FleetBot(traits, battleSeed, tables.Beats, tables.Fighter);

        // 틱마다의 체력 — [t] 는 t 틱이 끝난 뒤, [0] 은 판이 선 때.
        var bossHp = new List<int> { sim.Boss.Health };
        var fighterHp = new List<int> { sim.Fighter.Health };
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(bot.Next(sim));
            bossHp.Add(sim.Boss.Health);
            fighterHp.Add(sim.Fighter.Health);
        }

        bool bossWon = BossWon(outcome.Value, sim.Fighter.Alive);
        IReadOnlyList<NetStep> raw = netController?.Steps ?? (IReadOnlyList<NetStep>)Array.Empty<NetStep>();
        var steps = new RolloutStep[raw.Count];
        for (int i = 0; i < raw.Count; i++)
        {
            int from = i == 0 ? 0 : raw[i].Tick - 1;
            int to = i + 1 < raw.Count ? raw[i + 1].Tick - 1 : sim.Ticks;
            int span = (i + 1 < raw.Count ? raw[i + 1].Tick : sim.Ticks + 1) - raw[i].Tick;
            double r = (reward.WDealt * (fighterHp[from] - fighterHp[to]) / tables.Fighter.MaxHealth)
                - (reward.WTaken * (bossHp[from] - bossHp[to]) / tables.Boss.MaxHealth)
                - (reward.WTime * span / 60.0);
            bool done = i == raw.Count - 1;
            if (done)
            {
                r += bossWon ? reward.WWin : -reward.WWin;
            }

            NetStep s = raw[i];
            steps[i] = new RolloutStep(s.Tick, s.Observation, s.Mask, s.Action, s.LogProb, s.Value, r, done, span);
        }

        return new Episode(episode, traits.Habit.ToString(), outcome.Value, bossWon, sim.Ticks, bossHp[0] - sim.Boss.Health, fighterHp[0] - sim.Fighter.Health, steps);
    }
}
