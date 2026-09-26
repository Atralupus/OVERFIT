namespace Overfit.Battle.Rules;

/// <summary>
/// 판정 하나 — <b>모양</b>과 피해 (이슈 #59 · 설계 §3). 모양은 공격자 기준이고, 어디에 놓을지(보스의 발 ·
/// 보는 쪽)는 판정할 때 <see cref="Placement"/> 로 준다. 옛 판정은 "보스 중심에서 거리 몇~몇 · 높이 몇~몇" 인
/// 좌우 대칭 띠였고, 그 띠는 이제 <see cref="HitShape.Band"/> 두 장으로 같은 통로를 탄다.
/// </summary>
/// <param name="Shape">판정 모양 (공격자 기준 사각형들).</param>
/// <param name="Damage">막지 않았을 때의 피해.</param>
/// <param name="ActiveSeconds">이 판정이 살아 있는 초 (이슈 #59). 0 이면 한 틱이다 —
/// <see cref="PatternStep.ActiveSeconds"/> 를 그대로 싣는다. 틱으로 바꾸는 것은 <c>BattleSim.TicksFor</c> 다.</param>
/// <param name="Jumpable">점프 한 번으로 창 내내 발이 이 모양 위에 있을 수 있나 (#72 · 설계 §7.3). 판을 세울 때 이 판의 파이터로
/// 잰다(<c>BossHits.TicksAbove</c>). 관측의 <c>JumpAvailable</c> 이 이것이다 — 패턴 태그(<c>jumpable</c>)는 요약일 뿐이다.</param>
public readonly record struct HitBox(
    HitShape Shape,
    int Damage,
    double ActiveSeconds = 0,
    bool Jumpable = false);
