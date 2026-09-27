namespace Overfit.Battle.View;

/// <summary>보스 그림이 이번 프레임에 단계의 장을 어떻게 다루나 — <see cref="BossStepDraw.Next"/> 의 답.</summary>
public enum StepDraw
{
    /// <summary>단계가 가리킨 장에 세운다.</summary>
    Hold,

    /// <summary>그 애니메이션을 제 속도로 돈다 — 이름이 바뀌었으면 처음부터, 같으면 도는 그대로.</summary>
    Play,

    /// <summary>붙든 장에서 제 속도로 다시 돈다.</summary>
    Resume,
}

/// <summary>
/// 보스 그림의 장 고르기 (#78 · #59 의 3/6 넘김) — 그리는 노드(<c>BossView</c>)의 순수 절반이다. Godot 을 모르므로 규칙 테스트 프로젝트가 링크해
/// 돌린다(<c>BossStepDrawTests</c>) — <see cref="FloorWave"/> 와 같은 가름이다.
///
/// <para>
/// <b>붙든 장과 같은 애니메이션을 제 속도로 그려야 하면 그 장에서 다시 돈다.</b> 단계가 장(<c>frame</c>)을 적으면 뷰가 그 장에 세운다(붙든다).
/// 뒤의 단계가 같은 애니메이션을 장 없이(제 속도로) 적으면 이름이 같아 새로 틀 것이 없다 — 이것을 안 가르던 때는 아무것도 안 해 멈춘 장이
/// 그대로 남았다(#59 의 3/6 넘김). 패턴이 끝나 뷰가 고른 <c>idle</c> 이 붙든 <c>idle</c> 뒤에 와도 같다. 다른 애니메이션이면 새로 튼다 —
/// 이름이 바뀌면 붙든 장도 걷힌다.
/// </para>
/// </summary>
public static class BossStepDraw
{
    /// <param name="heldAnim">지금 붙든 장의 애니메이션 — 붙든 장이 없으면 null.</param>
    /// <param name="draw">이번에 그릴 애니메이션 — 단계의 것이거나 뷰가 고른 것(탈진 <c>hit</c> · 죽음 <c>death</c> · 이름 없는 단계의 <c>idle</c>).</param>
    /// <param name="stepAnim">규칙의 단계가 가리킨 애니메이션. 없으면 null.</param>
    /// <param name="stepFrame">규칙의 단계가 가리킨 장. 없으면 제 속도로 돈다.</param>
    public static StepDraw Next(string? heldAnim, string draw, string? stepAnim, int? stepFrame) =>
        draw == stepAnim && stepFrame is not null ? StepDraw.Hold
        : heldAnim == draw ? StepDraw.Resume
        : StepDraw.Play;
}
