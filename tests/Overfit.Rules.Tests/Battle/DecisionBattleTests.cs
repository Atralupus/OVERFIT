using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 판의 결정 지점 (설계 2026-10-01 조각2 §1 · §2) — 자유로워짐 · 쉬기 12틱마다 · 다가가는 중과 닿은 틱 · 캔슬 지점. 판은 파이터 480 · 보스 1440 에서 선다.
/// 0.11 과 같은지는 리플레이 골든과 판의 테스트가 본다 — 여기서는 판이 언제 무엇을 묻는지.
/// </summary>
public class DecisionBattleTests
{
    private static readonly InputFrame _right = new(1, false, false, false, false);

    /// <summary>받은 결정을 적고 안에 넘긴다.</summary>
    private sealed class Recording(IBossController inner) : IBossController
    {
        public List<BossDecision> Seen { get; } = new();

        public IBossController Inner => inner;

        public bool ReactsToBombs => inner.ReactsToBombs;

        public int Decide(BossDecision decision)
        {
            Seen.Add(decision);
            return inner.Decide(decision);
        }
    }

    /// <summary>늘 같은 칸을 낸다 — 가려졌어도.</summary>
    private sealed class Stubborn(int action) : IBossController
    {
        public bool ReactsToBombs => false;

        public int Decide(BossDecision decision) => action;
    }

    private static IReadOnlyList<string> Roster => StageRoster.For(TestConfigs.Stages(), 1);

    private static BattleSim Sim(IBossController controller) => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter],
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(maxHealth: 999_999),
        PatternIds = Roster,
        Patterns = TestConfigs.Patterns(),
        Seed = 51,
        Controller = controller,
        MaxTicks = TestConfigs.MaxTicks(),
    });

    private static Recording Rule(params ScriptPlan[] plans)
    {
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        return new Recording(new RuleController(
            new ScriptPlanPicker(Roster, patterns, plans), new BossActions(Roster), Roster, patterns, BattleSim.RestTicks(TestConfigs.Boss())));
    }

    [Fact]
    public void 판이_서면_자유로워짐이고_쉬기는_12틱마다_묻고_찬_결정의_틱에_동작이_선다()
    {
        Recording c = Rule(new ScriptPlan(1.2, "3연격"));
        BattleSim sim = Sim(c);
        TestConfigs.UntilTick(sim, 72);

        c.Seen.Select(d => (d.Point, d.Sight.Tick)).ShouldBe(new[]
        {
            (DecisionPoint.Freed, 0),
            (DecisionPoint.Rest, 12), (DecisionPoint.Rest, 24), (DecisionPoint.Rest, 36),
            (DecisionPoint.Rest, 48), (DecisionPoint.Rest, 60), (DecisionPoint.Rest, 72),
        });
        c.Seen[0].Mask.Count(m => m).ShouldBe(1, "자유로워짐에 기다리기 말고 열린 칸이 있다");
        sim.Boss.CurrentPattern.ShouldBe("3연격", "72틱의 결정에 동작이 안 섰다");
    }

    [Fact]
    public void 다가가는_중에는_12틱마다_묻고_닿은_틱에_묻고_그_틱에_동작이_선다()
    {
        Recording c = Rule(new ScriptPlan(0.4, "3연격", Run: true));
        BattleSim sim = Sim(c);
        for (int i = 0; i < 300 && sim.Boss.CurrentPattern is null; i++)
        {
            sim.Tick(default);
        }

        int begun = sim.Ticks;
        BossDecision last = c.Seen[^1];
        (last.Point, last.Sight.Tick).ShouldBe((DecisionPoint.Arrived, begun));
        c.Seen.Where(d => d.Point == DecisionPoint.Approach).Select(d => d.Sight.Tick).ShouldBe(
            Enumerable.Range(1, (begun - 24 - 1) / 12).Select(k => 24 + (12 * k)));
    }

    [Fact]
    public void 캔슬_지점마다_지금_동작과_지점의_칸을_싣고_묻는다()
    {
        Recording c = Rule(new ScriptPlan(0.4, "3연격"));
        BattleSim sim = Sim(c);
        TestConfigs.UntilTick(sim, 24 + 150);

        c.Seen.Where(d => d.Point == DecisionPoint.Cancel).Select(d => (d.Current, d.CancelPoint, d.Sight.Tick)).ShouldBe(new (string?, int?, int)[]
        {
            ("3연격", 0, 24 + 78),
            ("3연격", 1, 24 + 144),
        });
    }

    [Fact]
    public void 무작위_조종기는_판을_E_없이_끝까지_간다()
    {
        using var log = new LogCapture();
        BattleSim sim = Sim(new RandomController(51));
        BattleOutcome? outcome = null;
        for (int i = 0; i < 60 * 60 * 10 && outcome is null; i++)
        {
            outcome = sim.Tick(default);
        }

        outcome.ShouldNotBeNull();
        log.Lines.ShouldNotContain(l => l.Contains("][E]", StringComparison.Ordinal));
        sim.Plans.ShouldBeEmpty("무작위 조종기의 판에 계획이 있다");
        sim.Drawn.ShouldNotBeEmpty("무작위 조종기가 동작을 하나도 안 냈다");
    }

    [Fact]
    public void 가려진_칸을_내면_E_를_남기고_기다린다()
    {
        using var log = new LogCapture();
        BattleSim sim = Sim(new Stubborn(BossActions.Continue));
        TestConfigs.UntilTick(sim, 30);

        log.Lines.ShouldContain(l => l.StartsWith("[boss][E] action_masked act=continue at=freed ", StringComparison.Ordinal));
        log.Lines.ShouldContain(l => l.StartsWith("[boss][E] action_masked act=continue at=rest ", StringComparison.Ordinal));
        sim.Boss.CurrentPattern.ShouldBeNull();
    }

    [Fact]
    public void 결정이_보는_파이터는_18틱_앞의_모습이다()
    {
        Recording c = Rule(new ScriptPlan(1.2, "3연격"));
        BattleSim sim = Sim(c);
        var xs = new Dictionary<int, double>();
        for (int t = 1; t <= 24; t++)
        {
            sim.Tick(_right);
            xs[t] = sim.Fighter.X;
        }

        BossDecision at24 = c.Seen.Single(d => d.Sight.Tick == 24);
        at24.Sight.Fighter.X.ShouldBe(xs[6]);
        c.Seen.Single(d => d.Sight.Tick == 12).Sight.Fighter.X.ShouldBe(sim.Fighter.X - (24 * 7), 1e-9, "판 초반은 첫 모습이 아니다");
    }

    /// <summary>열려 있으면 늘 다가가기, 아니면 기다리기.</summary>
    private sealed class AlwaysApproach : IBossController
    {
        public bool ReactsToBombs => false;

        public int Decide(BossDecision decision) => decision.Mask[BossActions.Approach] ? BossActions.Approach : BossActions.Wait;
    }

    [Fact]
    public void 닿은_틱의_결정은_다가가기를_가린다()
    {
        // 최종 리뷰가 밟았다 — 닿은 틱에 다가가기를 다시 고르면 달리기가 같은 틱에 또 닿아 Arrived 를 또 묻고, 끝없이 되불러 프로세스가 죽었다(.NET 의
        // 스택 넘침은 못 잡는다). 닿은 결정은 다가가기를 가린다 — 다음 다가가기는 다음 결정(12틱 뒤)이다.
        var c = new Recording(new AlwaysApproach());
        BattleSim sim = Sim(c);
        TestConfigs.UntilTick(sim, 300);

        BossDecision arrived = c.Seen.First(d => d.Point == DecisionPoint.Arrived);
        arrived.Mask[BossActions.Approach].ShouldBeFalse();
        c.Seen.Count(d => d.Point == DecisionPoint.Arrived && d.Sight.Tick == arrived.Sight.Tick).ShouldBe(1, "같은 틱에 Arrived 를 또 물었다");
    }

    /// <summary>관측을 원하는 조종기 — 기다리기만.</summary>
    private sealed class Watching : IBossController
    {
        public List<BossDecision> Seen { get; } = new();

        public bool ReactsToBombs => false;

        public bool WantsObservation => true;

        public int Decide(BossDecision decision)
        {
            Seen.Add(decision);
            return BossActions.Wait;
        }
    }

    [Fact]
    public void 관측은_원하는_조종기에게만_짓는다()
    {
        // 설계 2026-10-01 조각4 §2 — 규칙 · 무작위 조종기의 판은 할당이 안 는다.
        Recording rule = Rule(new ScriptPlan(0.8, "3연격"));
        TestConfigs.UntilTick(Sim(rule), 24);
        rule.Seen.ShouldAllBe(d => d.Observation == null);

        var watching = new Watching();
        TestConfigs.UntilTick(Sim(watching), 24);
        int size = new BossObservation(Roster.Count).Size;
        watching.Seen.ShouldAllBe(d => d.Observation != null && d.Observation.Count == size);
        watching.Seen[^1].Observation![1].ShouldBe(1, "쉬기 결정의 지점 칸이 아니다");
    }

    /// <summary>규칙 조종기에 관측을 원하게 덧씌운다.</summary>
    private sealed class RuleWatching(IBossController inner) : IBossController
    {
        public List<BossDecision> Seen { get; } = new();

        public bool ReactsToBombs => inner.ReactsToBombs;

        public bool WantsObservation => true;

        public int Decide(BossDecision decision)
        {
            Seen.Add(decision);
            return inner.Decide(decision);
        }
    }

    [Fact]
    public void 캔슬_결정의_관측은_지금_지점_다음의_캔슬_지점까지를_싣는다()
    {
        // 최종 리뷰가 밟았다 — 지금 묻는 지점을 "다음" 으로 세면 캔슬 결정에서는 늘 1틱(1/60)이라 "더 기다리면 또 끊을 자리가 오나" 가 안 보였다.
        // 3연격의 첫 지점(78)에서 다음은 144 — 결정의 틱에 러너는 77 이라(지점의 단계에 들기 전에 묻는다) (144 − 77)/60. 둘째 지점(144)에서는 더 없으니 0.
        var c = new RuleWatching(Rule(new ScriptPlan(0.4, "3연격")).Inner);
        BattleSim sim = Sim(c);
        TestConfigs.UntilTick(sim, 24 + 150);
        int at = new BossObservation(Roster.Count).Size - (BossObservation.Items * 3) - (BossObservation.Recent * Enum.GetValues<FighterAction>().Length)
            - (8 + Enum.GetValues<FighterAction>().Length) - 1;
        BossDecision[] cancels = [.. c.Seen.Where(d => d.Point == DecisionPoint.Cancel)];
        cancels.Length.ShouldBe(2);
        cancels[0].Observation![at].ShouldBe((144 - 77) / 60.0, 1e-12);
        cancels[1].Observation![at].ShouldBe(0);
    }
}
