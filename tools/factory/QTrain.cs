using System;
using System.Collections.Generic;
using Overfit.Battle.Rules;

namespace Overfit.Factory;

/// <summary>Q 표의 한 바퀴의 결과.</summary>
public sealed record QIteration(int Episodes, int BossWins, int Rows, double Return);

/// <summary>
/// Q 표의 학습 (설계 2026-10-01 조각8 §1.3) — 바퀴마다 판들을 <b>얼린 표</b>로 병렬로 돌고, 다 끝난 뒤 판 번호 순으로 배운다. 판 도중에 표를 고치면 어느
/// 스레드가 먼저 끝났나가 표에 섞인다 — 스레드 수와 무관하게 같은 표여야 같은 시드의 학습을 다시 낼 수 있다.
/// </summary>
public static class QTrain
{
    /// <summary>빈 표 — 자르는 자는 <c>train.json</c> 의 <c>qtable</c> 이다.</summary>
    public static QTable NewTable(FactoryTables tables, QTrainDef def)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(def);
        return new QTable(new QTableShape(def.DistanceEdges, def.Air), new BossActions(tables.Roster).Count);
    }

    /// <summary>바퀴 <paramref name="iteration"/> 의 탐색 확률 — 처음에서 끝으로 곧게 준다.</summary>
    public static double Epsilon(QTrainDef def, int iteration)
    {
        ArgumentNullException.ThrowIfNull(def);
        return def.Iterations <= 1 ? def.EpsilonStart : def.EpsilonStart + ((def.EpsilonEnd - def.EpsilonStart) * iteration / (def.Iterations - 1));
    }

    /// <summary>
    /// 한 바퀴 — 상대(<paramref name="opponents"/>)를 판마다 시드로 골라 <see cref="QTrainDef.Episodes"/> 판을 돌고 배운다. 바퀴의 시드는
    /// <c>seed × 1,000,000 + 바퀴</c>(PPO 와 같은 꼴) · 할인은 PPO 의 γ 를 보스의 결정 간격으로 거듭제곱한다.
    /// </summary>
    public static QIteration Iterate(
        FactoryTables tables, QTable table, QTrainDef def, int iteration, IReadOnlyList<FighterSpec> opponents, RewardDef reward, double gamma, int threads)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(opponents);
        ulong seed = (def.Seed * 1_000_000) + (ulong)iteration;
        var boss = new BossSpec(RolloutController.QTable, null, table, Epsilon(def, iteration));
        var matchups = new Matchup[opponents.Count];
        for (int i = 0; i < matchups.Length; i++)
        {
            matchups[i] = new Matchup(RolloutSide.Boss, boss, opponents[i]);
        }

        var episodes = new List<Episode>(def.Episodes);
        FactoryBatch.Run(0, def.Episodes, threads, e => RolloutRun.Play(tables, matchups[RolloutRun.Opponent(seed, e, matchups.Length)], reward, reward, seed, e),
            episodes.AddRange, chunk: def.Episodes);

        var layout = new BossObservation(tables.Roster.Count);
        int decideTicks = BattleSim.TicksFor(tables.Boss.DecideSeconds);
        int wins = 0, rows = 0;
        double total = 0;
        foreach (Episode e in episodes)
        {
            var samples = new QSample[e.Steps.Count];
            for (int i = 0; i < samples.Length; i++)
            {
                RolloutStep s = e.Steps[i];
                samples[i] = new QSample(QTable.Key(s.Observation, layout, table.Shape), s.Action, s.Reward, s.Span);
                total += s.Reward;
            }

            table.Learn(samples, gamma, decideTicks, def.Alpha);
            wins += e.BossWon ? 1 : 0;
            rows += samples.Length;
        }

        return new QIteration(episodes.Count, wins, rows, episodes.Count == 0 ? 0 : total / episodes.Count);
    }
}
