using System;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Factory;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>Q 표의 학습 (설계 2026-10-01 조각8 §1.3).</summary>
public class QTrainTests
{
    private static readonly Lazy<FactoryTables> _tables = new(() => FactoryTables.Load("data", "fleet.json"));

    private static readonly RewardDef _reward = new() { WDealt = 1, WTaken = 1, WTime = 0.002, WWin = 1 };

    private static QTrainDef Def(int iterations) => new()
    {
        Seed = 4,
        Iterations = iterations,
        Episodes = 6,
        Alpha = 0.1,
        EpsilonStart = 0.3,
        EpsilonEnd = 0.05,
        DistanceEdges = [0.1, 0.2, 0.35],
        Air = 0.05,
    };

    [Fact]
    public void 바퀴는_스레드_수와_무관하게_같은_표를_낸다()
    {
        string Run(int threads)
        {
            QTable t = QTrain.NewTable(_tables.Value, Def(2));
            for (int it = 0; it < 2; it++)
            {
                QTrain.Iterate(_tables.Value, t, Def(2), it, [new FighterSpec(FighterKind.Fleet, null)], _reward, gamma: 0.99, threads);
            }

            return t.ToJson();
        }

        string one = Run(1);
        Run(4).ShouldBe(one);
        QTable.Parse(one, "t", new BossActions(_tables.Value.Roster).Count).States.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 엡실론은_처음에서_끝으로_곧게_준다()
    {
        QTrainDef d = Def(5);
        Enumerable.Range(0, 5).Select(i => QTrain.Epsilon(d, i)).ToArray().ShouldBe([0.3, 0.2375, 0.175, 0.1125, 0.05], 1e-12);
        QTrain.Epsilon(Def(1), 0).ShouldBe(0.3);
    }

    [Fact(Skip = "#168: 패리를 걷어 관측이 바뀌었다 — 망을 다시 배우면 되살린다")]
    public void 일꾼의_게임_보스는_형태마다의_망으로_돌고_결정을_안_적는다()
    {
        // 관문의 game 열(설계 2026-10-01 조각8 §2) — 사람이 만나는 보스와 같은 FormNetController 다.
        FactoryTables t = _tables.Value;
        PolicyNet[] forms = [.. Enumerable.Range(1, 3).Select(i => PolicyNet.Parse(
            System.IO.File.ReadAllText(System.IO.Path.Combine("data", "boss_net", $"form{i}.json")), $"form{i}",
            new BossObservation(t.Roster.Count).Size, new BossActions(t.Roster).Count, t.Roster))];
        Episode e = RolloutRun.Play(t, new Matchup(RolloutSide.Boss, new BossSpec(RolloutController.Forms, null, Forms: forms),
            new FighterSpec(FighterKind.Fleet, null)), _reward, _reward, seed: 3, episode: 0);
        e.Ticks.ShouldBeGreaterThan(0);
        e.Steps.ShouldBeEmpty();
    }
}
