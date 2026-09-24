namespace Overfit.Battle.Rules;

/// <summary>
/// 판정 하나 — <b>모양</b>과 피해 (이슈 #59 · 설계 §3). 모양은 공격자 기준이고, 어디에 놓을지(보스의 발 ·
/// 보는 쪽)는 판정할 때 <see cref="Placement"/> 로 준다. 옛 판정은 "보스 중심에서 거리 몇~몇 · 높이 몇~몇" 인
/// 좌우 대칭 띠였고, 그 띠는 이제 <see cref="HitShape.Band"/> 두 장으로 같은 통로를 탄다.
/// </summary>
/// <param name="Shape">판정 모양 (공격자 기준 사각형들).</param>
/// <param name="Damage">막지 않았을 때의 피해.</param>
/// <param name="GuardBreak">가드로는 못 막나 (이슈 #47). <b>판정 단위다</b> — 계열의 마지막 한 대에만
/// 붙으므로 패턴 단위로 두면 앞의 연타까지 못 막게 된다. 태그의 <c>has_guard_break</c> 는 이것의
/// 요약일 뿐이고, 둘이 같은 말을 하는지는 <c>PatternDataTests</c> 가 본다.</param>
/// <param name="Finisher">이 패턴의 <b>마지막</b> 판정인가 (이슈 #53). 받아치면 보스가 굳는 자리이고,
/// 그 경직 하나에 최대 차지 한 방이 들어간다 — "받아쳤다 → 제일 센 걸 꽂는다" 가 한 동작이 되는 값이다.
///
/// <para>
/// <b>데이터에서는 <see cref="GuardBreak"/> 와 언제나 같은 대다</b> (이슈 #53 · 유저 결정). 마무리는
/// 아홉 변종 전부 가드 불가이고 — 빨강은 한 가지 뜻, "가드로 못 막는다 = 받아쳐라" —
/// <c>PatternDataTests</c> 가 그 일치를 양쪽(마무리 ⟹ 가드 불가 · 가드 불가 ⟹ 마무리)에서 못박는다.
/// 그런데도 칸을 따로 두는 이유는 <b>읽는 쪽의 뜻이 다르기</b> 때문이다: 화면의 빨강은 "못 막는다" 를
/// 말하므로 <see cref="GuardBreak"/> 를 읽고, 규칙의 상(경직)은 "패턴이 끝나는 대" 에 걸리므로 이 칸을
/// 읽는다. 둘이 갈라지는 날이 와도 각자 자기 말을 계속한다.
/// </para>
///
/// <para>
/// <b>데이터에 손으로 안 적는다.</b> 타임라인의 마지막 active 가 곧 이것이다 —
/// <c>finisher: true</c> 를 사람이 달면 판정을 하나 끼워 넣는 날 옛 마무리에 그 표가 남고,
/// 그 거짓말은 테스트가 아니라 플레이 중에만 보인다.
/// </para></param>
/// <param name="ActiveSeconds">이 판정이 살아 있는 초 (이슈 #59). 0 이면 한 틱이다 —
/// <see cref="PatternStep.ActiveSeconds"/> 를 그대로 싣는다. 틱으로 바꾸는 것은 <c>BattleSim.TicksFor</c> 다.</param>
public readonly record struct HitBox(
    HitShape Shape,
    int Damage,
    bool GuardBreak = false,
    bool Finisher = false,
    double ActiveSeconds = 0);
