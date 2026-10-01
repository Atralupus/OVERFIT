using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 규칙 조종기 (설계 2026-10-01 조각2 §3.1) — 0.11 의 계획을 결정의 칸으로 낸다. 계획은 대본 고르기로 못박고 결정을 손으로 흘린다. 판 위에서 0.11 과 같은지는
/// 리플레이 골든과 판의 테스트가 본다 — 여기서는 칸의 흐름만.
/// </summary>
public class RuleControllerTests
{
    private static readonly string[] _roster = ["3연격", "돌진"];
    private static readonly bool[] _freed = [true, false, false, false, false];
    private static readonly bool[] _rest = [true, true, false, true, true];
    private static readonly bool[] _cancel = [false, false, true, false, true];

    private static RuleController Make(IPlanPicker picker) =>
        new(picker, new BossActions(_roster), _roster, TestConfigs.Patterns(), [24, 48, 72]);

    private static RuleController Make(params ScriptPlan[] plans) => Make(new ScriptPlanPicker(_roster, TestConfigs.Patterns(), plans));

    private static BossDecision D(DecisionPoint p, int elapsed, bool[] mask, int? point = null) => new(p, 0, elapsed, mask,
        new BossSight(0, 1, 1440, -1, 1200, new FighterSnapshot(480, 0, 1, FighterAction.Idle, false, 220, 10), []),
        point is null ? null : "3연격", point);

    [Fact]
    public void 쉬기가_찰_때까지_기다리고_찬_결정에서_첫_동작이다()
    {
        RuleController c = Make(new ScriptPlan(0.8, "3연격"));
        c.Decide(D(DecisionPoint.Freed, 0, _freed)).ShouldBe(BossActions.Wait);
        c.Decide(D(DecisionPoint.Rest, 12, _rest)).ShouldBe(BossActions.Wait);
        c.Decide(D(DecisionPoint.Rest, 36, _rest)).ShouldBe(BossActions.Wait);
        c.Decide(D(DecisionPoint.Rest, 48, _rest)).ShouldBe(BossActions.FirstMove);
        c.Plans.Count.ShouldBe(1);
        c.ReactsToBombs.ShouldBeTrue();
    }

    [Fact]
    public void 달리는_계획은_다가가고_닿으면_첫_동작이다()
    {
        RuleController c = Make(new ScriptPlan(0.4, "돌진", Run: true));
        c.Decide(D(DecisionPoint.Freed, 0, _freed));
        c.Decide(D(DecisionPoint.Rest, 24, _rest)).ShouldBe(BossActions.Approach);
        c.Decide(D(DecisionPoint.Approach, 24, _rest)).ShouldBe(BossActions.Approach);
        c.Decide(D(DecisionPoint.Arrived, 24, _rest)).ShouldBe(BossActions.FirstMove + 1);
    }

    [Fact]
    public void 계획한_지점에서만_잇는_동작이고_다른_지점은_계속이다()
    {
        RuleController c = Make(new ScriptPlan(0.4, "3연격", CancelPoint: 1, Next: "돌진"));
        c.Decide(D(DecisionPoint.Freed, 0, _freed));
        c.Decide(D(DecisionPoint.Rest, 24, _rest)).ShouldBe(BossActions.FirstMove);
        c.Decide(D(DecisionPoint.Cancel, 24, _cancel, point: 0)).ShouldBe(BossActions.Continue);
        c.Decide(D(DecisionPoint.Cancel, 24, _cancel, point: 1)).ShouldBe(BossActions.FirstMove + 1);
    }

    [Fact]
    public void 끊지_않는_계획은_어느_지점이든_계속이다()
    {
        RuleController c = Make(new ScriptPlan(0.4, "3연격"));
        c.Decide(D(DecisionPoint.Freed, 0, _freed));
        c.Decide(D(DecisionPoint.Rest, 24, _rest));
        c.Decide(D(DecisionPoint.Cancel, 24, _cancel, point: 0)).ShouldBe(BossActions.Continue);
        c.Decide(D(DecisionPoint.Cancel, 24, _cancel, point: 1)).ShouldBe(BossActions.Continue);
    }

    /// <summary>처음 한 번 명부 밖 동작을 내고, 그 뒤는 0.8초 쉬고 3연격.</summary>
    private sealed class BadFirst : IPlanPicker
    {
        public BossPlan Next(PlanRequest request) => request.Number == 0 ? new BossPlan(48, 9, null, null) : new BossPlan(48, 0, null, null);
    }

    [Fact]
    public void 틀린_계획은_E_를_남기고_가장_짧은_쉬기_뒤_다시_고른다()
    {
        // 0.11 그대로(옛 PlanFlow) — 틀린 계획은 버리고 가장 짧은 쉬기(24) 뒤 번호 + 1 로 다시 고른다. 다시 고른 계획의 쉬기는 그 결정부터 센다.
        RuleController c = Make(new BadFirst());
        using var log = new LogCapture();
        c.Decide(D(DecisionPoint.Freed, 0, _freed)).ShouldBe(BossActions.Wait);
        log.Lines.ShouldContain(l => l.StartsWith("[boss][E] plan_invalid n=0 reason=move_out_of_range ", System.StringComparison.Ordinal));
        c.Decide(D(DecisionPoint.Rest, 12, _rest)).ShouldBe(BossActions.Wait);
        c.Decide(D(DecisionPoint.Rest, 24, _rest)).ShouldBe(BossActions.Wait, "다시 고른 결정에서 곧장 동작을 냈다");
        c.Plans.Count.ShouldBe(1);
        c.Decide(D(DecisionPoint.Rest, 60, _rest)).ShouldBe(BossActions.Wait);
        c.Decide(D(DecisionPoint.Rest, 72, _rest)).ShouldBe(BossActions.FirstMove, "다시 고른 계획의 쉬기(48)가 24 에서부터 안 셌다");
    }
}
