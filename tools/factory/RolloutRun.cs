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

    /// <summary>
    /// 공격을 고루 쓰는 몫 (이슈 #167) — 한 판에서 어느 공격을 처음 쓰는 결정에 이 값 / 명부 길이를 더한다. 없으면 0(파이터 · 옛 설정). 유저: "어떤
    /// 페이즈든 공격 자체는 전체 다 써야" — 엔트로피만으로는 sp-1 의 보스가 3연격 · 엇박 3연격을 버렸다(빠른 3연격에 밀린다).
    /// </summary>
    public double WVariety { get; init; }
}

/// <summary>일꾼의 보스 조종기 (설계 2026-10-01 조각5 §3) — 망은 경험을 적고, 규칙 · 무작위는 비교의 대조군이라 판의 결과만 낸다.</summary>
public enum RolloutController
{
    Net,
    Rule,
    Random,

    /// <summary>Q 표 보스 — 비교 관문의 대조군(설계 2026-10-01 조각8 §1).</summary>
    QTable,

    /// <summary>게임의 보스 — 형태마다의 망(<see cref="FormNetController"/> · 조각8 §2).</summary>
    Forms,
}

/// <summary>배우는 쪽 (설계 2026-10-01 조각6 §2).</summary>
public enum RolloutSide
{
    Boss,
    Fighter,
}

/// <summary>파이터의 종류 — 봇 함대 · 파이터 망.</summary>
public enum FighterKind
{
    Fleet,
    Net,
}

/// <summary>보스 쪽 — 조종기와 (망이면) 가중치. 가중치가 없는 망은 열린 칸에 같은 확률이다.</summary>
/// <param name="Kind">조종기.</param>
/// <param name="Net">망 — <see cref="RolloutController.Net"/>.</param>
/// <param name="Table">Q 표 — <see cref="RolloutController.QTable"/>.</param>
/// <param name="Epsilon">Q 표의 탐색 확률 — 학습 중만 0 보다 크다.</param>
/// <param name="Forms">형태마다의 망 — <see cref="RolloutController.Forms"/>.</param>
public sealed record BossSpec(RolloutController Kind, PolicyNet? Net, QTable? Table = null, double Epsilon = 0, IReadOnlyList<PolicyNet>? Forms = null);

/// <summary>파이터 쪽.</summary>
public sealed record FighterSpec(FighterKind Kind, PolicyNet? Net);

/// <summary>판 하나의 짝 — 배우는 쪽 · 보스 · 파이터.</summary>
public sealed record Matchup(RolloutSide Learner, BossSpec Boss, FighterSpec Fighter);

/// <summary><c>ml/rl/train.json</c> 중 일꾼이 읽는 칸 — 보스 · 파이터의 보상. PPO 수치는 학습기(파이썬)만 읽는다.</summary>
public sealed class TrainConfig
{
    public required RewardDef Reward { get; init; }

    /// <summary>
    /// 파이터 망의 보상 (설계 2026-10-01 조각6 §1.3) — 필수다. 없을 때 보스의 것으로 대신하던 때는 파이터가 시간 벌(스펙이 없앤)을 조용히 받았다(최종 리뷰).
    /// </summary>
    public required RewardDef FighterReward { get; init; }

    /// <summary>PPO 의 수치 중 Q 표가 같이 쓰는 할인(설계 2026-10-01 조각8 §1.3). 나머지 키는 학습기(파이썬)만 읽는다.</summary>
    public PpoShared? Ppo { get; init; }

    /// <summary>Q 표의 학습 (설계 2026-10-01 조각8 §1) — <c>--qtrain</c> 만 읽는다.</summary>
    public QTrainDef? Qtable { get; init; }
}

/// <summary><c>train.json</c> 의 <c>ppo</c> 중 Q 표가 같이 쓰는 칸.</summary>
public sealed class PpoShared
{
    public required double Gamma { get; init; }
}

/// <summary><c>train.json</c> 의 <c>qtable</c> (설계 2026-10-01 조각8 §1).</summary>
public sealed class QTrainDef
{
    public required ulong Seed { get; init; }

    public required int Iterations { get; init; }

    public required int Episodes { get; init; }

    public required double Alpha { get; init; }

    public required double EpsilonStart { get; init; }

    public required double EpsilonEnd { get; init; }

    public required IReadOnlyList<double> DistanceEdges { get; init; }

    public required double Air { get; init; }
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

    /// <summary>조각 4 · 5 의 판 — 배우는 쪽이 보스이고 상대가 봇 함대다.</summary>
    public static Episode Play(
        FactoryTables tables, PolicyNet? net, RewardDef reward, ulong seed, int episode, RolloutController kind = RolloutController.Net) =>
        Play(tables, new Matchup(RolloutSide.Boss, new BossSpec(kind, net), new FighterSpec(FighterKind.Fleet, null)), reward, reward, seed, episode);

    /// <summary>판 e 의 상대 칸 — 상대 <paramref name="count"/> 중 하나를 시드와 판으로 고른다(설계 2026-10-01 조각6 §2).</summary>
    public static int Opponent(ulong seed, int episode, int count) => count <= 1 ? 0 : Det.RollInt(seed, Det.Domain.Rollout, count, k1: episode, k2: 2);

    /// <summary>
    /// 판 하나 (설계 2026-10-01 조각6 §2) — 배우는 쪽(보스 · 파이터)의 결정만 적는다. 보스의 보상은 <paramref name="reward"/>, 파이터의 보상은
    /// <paramref name="fighterReward"/>(시간 벌 없음 · 이기면 파이터가 이긴 것 — 같은 틱에 둘 다 쓰러지면 게임대로 파이터의 승리다).
    /// </summary>
    public static Episode Play(FactoryTables tables, Matchup matchup, RewardDef reward, RewardDef fighterReward, ulong seed, int episode)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(matchup);
        ArgumentNullException.ThrowIfNull(reward);
        ArgumentNullException.ThrowIfNull(fighterReward);
        BotTraits traits = BotTraits.Sample(seed, episode, tables.Fleet);
        ulong battleSeed = Det.Hash64(seed, Det.Domain.Rollout, k1: episode, k2: 0);
        ulong pickSeed = Det.Hash64(seed, Det.Domain.Rollout, k1: episode, k2: 1);
        ulong fighterSeed = Det.Hash64(seed, Det.Domain.Rollout, k1: episode, k2: 3);
        IReadOnlyList<string> roster = StageRoster.For(tables.Stages, _stage);
        var netController = matchup.Boss.Kind == RolloutController.Net ? new NetController(matchup.Boss.Net, pickSeed) : null;
        IBossController controller = matchup.Boss.Kind switch
        {
            RolloutController.Rule => new RuleController(
                new UniformPlanPicker(new PickerInputs(roster, tables.Patterns, tables.RestTicks, tables.Knobs, Array.Empty<AttemptRecord>(), battleSeed)),
                new BossActions(roster), roster, tables.Patterns, tables.RestTicks),
            RolloutController.Random => new RandomController(pickSeed),
            RolloutController.QTable => new QTableController(
                matchup.Boss.Table ?? throw new ArgumentException("Q 표가 없다", nameof(matchup)), new BossObservation(roster.Count), pickSeed, matchup.Boss.Epsilon),
            RolloutController.Forms => new FormNetController(
                matchup.Boss.Forms ?? throw new ArgumentException("형태의 망이 없다", nameof(matchup)), new BossActions(roster), pickSeed),
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
        FleetBot? bot = matchup.Fighter.Kind == FighterKind.Fleet ? new FleetBot(traits, battleSeed, tables.Beats, tables.Fighter) : null;
        FighterNetDriver? driver = matchup.Fighter.Kind == FighterKind.Net
            ? new FighterNetDriver(matchup.Fighter.Net, fighterSeed, roster, tables.Arena.Width)
            : null;

        // 틱마다의 체력 — [t] 는 t 틱이 끝난 뒤, [0] 은 판이 선 때.
        var bossHp = new List<int> { sim.Boss.Health };
        var fighterHp = new List<int> { sim.Fighter.Health };
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(driver?.Next(sim) ?? bot!.Next(sim));
            bossHp.Add(sim.Boss.Health);
            fighterHp.Add(sim.Fighter.Health);
        }

        bool bossWon = BossWon(outcome.Value, sim.Fighter.Alive);
        RolloutStep[] steps = matchup.Learner == RolloutSide.Boss
            ? BossSteps(netController?.Steps ?? (controller as QTableController)?.Steps ?? Array.Empty<NetStep>(), tables, reward, bossHp, fighterHp, sim.Ticks, bossWon)
            : FighterSteps(driver?.Steps ?? Array.Empty<NetStep>(), tables, fighterReward, bossHp, fighterHp, sim.Ticks, outcome == BattleOutcome.Win);
        string habit = bot is null ? "net" : traits.Habit.ToString();
        return new Episode(episode, habit, outcome.Value, bossWon, sim.Ticks, bossHp[0] - sim.Boss.Health, fighterHp[0] - sim.Fighter.Health, steps);
    }

    /// <summary>보스의 결정에 보상 — 결정은 판의 틱 안에서 묻고 칼 · 폭탄은 그 뒤에 닿으므로 결정의 틱에 닿은 피해는 그 결정의 것이다.</summary>
    private static RolloutStep[] BossSteps(
        IReadOnlyList<NetStep> raw, FactoryTables tables, RewardDef reward, List<int> bossHp, List<int> fighterHp, int ticks, bool bossWon)
    {
        var steps = new RolloutStep[raw.Count];
        int roster = tables.Roster.Count;
        int firstMove = new BossActions(tables.Roster).Count - roster;
        var used = new bool[roster];
        for (int i = 0; i < raw.Count; i++)
        {
            int from = i == 0 ? 0 : raw[i].Tick - 1;
            int to = i + 1 < raw.Count ? raw[i + 1].Tick - 1 : ticks;
            int span = (i + 1 < raw.Count ? raw[i + 1].Tick : ticks + 1) - raw[i].Tick;
            double r = (reward.WDealt * (fighterHp[from] - fighterHp[to]) / tables.Fighter.MaxHealth)
                - (reward.WTaken * (bossHp[from] - bossHp[to]) / tables.Boss.MaxHealth)
                - (reward.WTime * span / 60.0);
            int move = raw[i].Action - firstMove;
            if (move >= 0 && !used[move])
            {
                used[move] = true;
                r += reward.WVariety / roster;
            }

            bool done = i == raw.Count - 1;
            if (done)
            {
                r += bossWon ? reward.WWin : -reward.WWin;
            }

            NetStep s = raw[i];
            steps[i] = new RolloutStep(s.Tick, s.Observation, s.Mask, s.Action, s.LogProb, s.Value, r, done, span);
        }

        return steps;
    }

    /// <summary>
    /// 파이터의 결정에 보상 — 파이터는 틱을 밀기 <b>전에</b> 고르므로(<see cref="FighterNetDriver.Next"/>) 결정 t 의 몫은 t 틱이 끝난 뒤부터 다음 결정의 틱이
    /// 끝날 때까지다.
    /// </summary>
    private static RolloutStep[] FighterSteps(
        IReadOnlyList<NetStep> raw, FactoryTables tables, RewardDef reward, List<int> bossHp, List<int> fighterHp, int ticks, bool fighterWon)
    {
        var steps = new RolloutStep[raw.Count];
        for (int i = 0; i < raw.Count; i++)
        {
            int from = raw[i].Tick;
            int to = i + 1 < raw.Count ? raw[i + 1].Tick : ticks;
            double r = (reward.WDealt * (bossHp[from] - bossHp[to]) / tables.Boss.MaxHealth)
                - (reward.WTaken * (fighterHp[from] - fighterHp[to]) / tables.Fighter.MaxHealth)
                - (reward.WTime * (to - from) / 60.0);
            bool done = i == raw.Count - 1;
            if (done)
            {
                r += fighterWon ? reward.WWin : -reward.WWin;
            }

            NetStep s = raw[i];
            steps[i] = new RolloutStep(s.Tick, s.Observation, s.Mask, s.Action, s.LogProb, s.Value, r, done, to - from);
        }

        return steps;
    }
}
