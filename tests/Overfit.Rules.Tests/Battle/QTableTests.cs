using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>Q 표 보스 (설계 2026-10-01 조각8 §1) — 망 없는 강화학습의 대조군.</summary>
public class QTableTests
{
    private static readonly QTableShape _shape = new([0.1, 0.2, 0.35], 0.05);

    private static FighterSnapshot Fighter(double x, double y, FighterAction action) => new(x, y, 1, action, false, 200, 5, 0.5, 0);

    private static double[] Obs(DecisionPoint point, double fighterX, double fighterY, FighterAction action, int moveIndex, int form) =>
        new BossObservation(7).Encode(new ObservationInput(
            point, 1920, BossX: 960, BossY: 0, BossFacing: -1, BossHealth: 900, BossMaxHealth: 1200, Form: form, FormCount: 3, PoiseRatio: 0,
            Exhausted: false, Shifting: false, Travel: TravelState.None, MoveIndex: moveIndex, MoveProgress: 0.5, TicksToCancel: -1,
            Fighter: Fighter(fighterX, fighterY, action), Recent: [], FighterMaxHealth: 220, Bombs: []));

    private static bool[] Open(params int[] open)
    {
        var m = new bool[13];
        foreach (int i in open)
        {
            m[i] = true;
        }

        return m;
    }

    [Fact]
    public void 칸_키는_지점_거리_높이_행동_동작_형태다()
    {
        var obs = new BossObservation(7);
        // 거리 |dx|/W: 960−700 = 260 → 0.135 → 둘째 칸(1) · 높이 30/300 = 0.1 > 0.05 → 떠 있다.
        QTable.Key(Obs(DecisionPoint.Cancel, 700, 30, FighterAction.Guard, 3, 2), obs, _shape)
            .ShouldBe($"{(int)DecisionPoint.Cancel}|1|1|{(int)FighterAction.Guard}|4|2");
        // 멀리(0.45 → 넷째 칸) · 땅 · 동작 없음(0) · 형태 3.
        QTable.Key(Obs(DecisionPoint.Rest, 96, 0, FighterAction.Idle, -1, 3), obs, _shape)
            .ShouldBe($"{(int)DecisionPoint.Rest}|3|0|{(int)FighterAction.Idle}|0|3");
        // 가까이(0.05 → 첫 칸) · 오른쪽에 있어도 같은 거리다.
        QTable.Key(Obs(DecisionPoint.Rest, 1056, 0, FighterAction.Idle, -1, 1), obs, _shape)[2..3].ShouldBe("0");
    }

    [Fact]
    public void 표에_있는_칸이면_배운_칸_중_Q_가_가장_큰_열린_칸이다()
    {
        var t = new QTable(_shape, 13);
        t.Set("k", action: 1, q: 0.5, visits: 3);
        t.Set("k", action: 4, q: 0.9, visits: 2);
        t.Set("k", action: 6, q: 2.0, visits: 1); // 가려진다
        t.Pick("k", Open(0, 1, 4, 5), seed: 1, number: 0, epsilon: 0).ShouldBe(4);

        // 안 배운 칸(Q 0 · 방문 0)은 음수 Q 보다 앞서지 않는다 — 보상이 대개 음수라 0 이 "좋아 보이는" 거짓말이 된다.
        var neg = new QTable(_shape, 13);
        neg.Set("k", action: 2, q: -0.4, visits: 5);
        neg.Pick("k", Open(0, 2, 3), seed: 1, number: 0, epsilon: 0).ShouldBe(2);

        // 같으면 앞의 칸.
        var tie = new QTable(_shape, 13);
        tie.Set("k", action: 3, q: 0.2, visits: 1);
        tie.Set("k", action: 1, q: 0.2, visits: 1);
        tie.Pick("k", Open(1, 3), seed: 1, number: 0, epsilon: 0).ShouldBe(1);
    }

    [Fact]
    public void 표에_없는_칸이면_열린_칸에서_무작위이고_가려진_칸은_안_고른다()
    {
        var t = new QTable(_shape, 13);
        bool[] mask = Open(0, 2, 7, 9);
        int[] picks = Enumerable.Range(0, 400).Select(n => t.Pick("없는칸", mask, seed: 9, number: n, epsilon: 0)).ToArray();
        picks.ShouldAllBe(a => mask[a]);
        picks.Distinct().Count().ShouldBe(4);
        Enumerable.Range(0, 400).Select(n => t.Pick("없는칸", mask, seed: 9, number: n, epsilon: 0)).ShouldBe(picks, "같은 시드 같은 칸이 아니다");
    }

    [Fact]
    public void 엡실론이면_배운_칸에서도_가끔_무작위다()
    {
        var t = new QTable(_shape, 13);
        t.Set("k", action: 2, q: 1, visits: 1);
        bool[] mask = Open(0, 2, 5);
        int[] picks = Enumerable.Range(0, 1000).Select(n => t.Pick("k", mask, seed: 3, number: n, epsilon: 0.3)).ToArray();
        picks.ShouldAllBe(a => mask[a]);
        // 무작위가 0.3 이고 그중 1/3 은 2 다 — 2 가 아닌 것은 0.2 쯤.
        (picks.Count(a => a != 2) / 1000.0).ShouldBeInRange(0.15, 0.25);
    }

    [Fact]
    public void 배우기는_결정의_길이로_할인한_보상으로_Q_를_옮긴다()
    {
        var t = new QTable(_shape, 13);
        // G2 = −1 · G1 = 0.5 + γ^(24/12)·G2 · G0 = 0 + γ^(12/12)·G1.
        QSample[] episode = [new("a", 1, 0.0, 12), new("b", 2, 0.5, 24), new("a", 3, -1.0, 6)];
        t.Learn(episode, gamma: 0.9, decideTicks: 12, alpha: 0.5);
        double g2 = -1.0;
        double g1 = 0.5 + (0.81 * g2);
        double g0 = 0.9 * g1;
        t.Q("a", 3).ShouldBe(0.5 * g2, 1e-12);
        t.Q("b", 2).ShouldBe(0.5 * g1, 1e-12);
        t.Q("a", 1).ShouldBe(0.5 * g0, 1e-12);
        (t.Visits("a", 1), t.Visits("a", 0), t.States).ShouldBe((1, 0, 2));
    }

    [Fact]
    public void 표는_JSON_으로_쓰고_읽어도_같다()
    {
        var t = new QTable(_shape, 13);
        t.Set("2|1|0|3|0|1", action: 4, q: -0.123456789012345, visits: 7);
        t.Set("1|0|0|0|0|1", action: 0, q: 0.5, visits: 1);
        string json = t.ToJson();
        QTable back = QTable.Parse(json, "t", 13);
        back.ToJson().ShouldBe(json);
        back.Q("2|1|0|3|0|1", 4).ShouldBe(-0.123456789012345);
        back.Shape.ShouldBe(_shape);
    }

    [Fact]
    public void Q_표_조종기의_판은_E_없이_끝까지_가고_결정을_적는다()
    {
        IReadOnlyList<string> roster = StageRoster.For(TestConfigs.Stages(), 1);
        var c = new QTableController(new QTable(_shape, new BossActions(roster).Count), new BossObservation(roster.Count), seed: 5, epsilon: 0.1);
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter],
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(),
            PatternIds = roster,
            Patterns = TestConfigs.Patterns(),
            Seed = 51,
            Controller = c,
            MaxTicks = TestConfigs.MaxTicks(),
        });
        using var log = new LogCapture();
        var bot = new BotPolicy(51);
        BattleOutcome? o = null;
        while (o is null)
        {
            o = sim.Tick(bot.Next(sim));
        }

        log.Lines.ShouldNotContain(l => l.Contains("][E]", StringComparison.Ordinal));
        c.Steps.ShouldNotBeEmpty();
        c.Steps.ShouldAllBe(s => s.Mask[s.Action] && s.Mask.Count(m => m) > 1);
        c.Keys.Count.ShouldBe(c.Steps.Count);
    }
}
