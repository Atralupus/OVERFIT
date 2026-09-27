using Overfit.Battle.View;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 보스 그림의 장 고르기 (#78 · #59 의 3/6 넘김) — <see cref="BossStepDraw"/>. 엔진 없이 결정만 본다. 장에 세우고 다시 돌리는 것은
/// <c>BossView</c> 이고, 그 그림은 스크린샷이 본다.
/// </summary>
public class BossStepDrawTests
{
    [Fact]
    public void 같은_애니메이션의_붙든_장_뒤에_이름_없는_장이_오면_그_장에서_다시_돈다()
    {
        // #59 의 3/6 넘김 — 붙든 장(idle f0) 뒤의 단계가 같은 애니메이션을 제 속도로 돌라면(장이 없다) 이름이 같아 새로 틀 것이 없다. 이것을 안
        // 가르던 때는 멈춘 장이 그대로 남았다. 지금 데이터에는 이 차례가 없다 — 데이터가 그렇게 되는 날을 여기서 막는다. 패턴이 끝나 뷰가 고른
        // idle 이 붙든 idle 뒤에 와도 같다.
        BossStepDraw.Next(heldAnim: "idle", draw: "idle", stepAnim: "idle", stepFrame: null).ShouldBe(StepDraw.Resume);
        BossStepDraw.Next(heldAnim: "idle", draw: "idle", stepAnim: null, stepFrame: null).ShouldBe(StepDraw.Resume, "패턴이 끝난 idle 이 붙든 장에 멈췄다");
    }

    [Fact]
    public void 단계가_장을_적으면_세우고_붙든_장이_다른_애니메이션이면_새로_튼다()
    {
        BossStepDraw.Next(null, "attack", "attack", 0).ShouldBe(StepDraw.Hold, "단계가 가리킨 장에 안 세웠다");
        BossStepDraw.Next("attack", "attack", "attack", 1).ShouldBe(StepDraw.Hold, "붙든 장 뒤의 다른 장에 안 세웠다");
        BossStepDraw.Next(null, "run", "run", null).ShouldBe(StepDraw.Play, "붙든 장이 없는데 다시 돈다고 한다");
        BossStepDraw.Next("attack", "idle", "idle", null).ShouldBe(StepDraw.Play, "다른 애니메이션의 붙든 장에서 이었다");

        // 탈진한 보스는 단계가 장을 적어도 take-hit(hit)을 그린다 — 뷰가 고른 그림이라 세울 장이 아니다.
        BossStepDraw.Next("attack", "hit", "attack", 0).ShouldBe(StepDraw.Play, "탈진의 그림을 단계의 장에 세웠다");
    }
}
