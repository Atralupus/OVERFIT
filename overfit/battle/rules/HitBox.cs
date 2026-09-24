namespace Overfit.Battle.Rules;

/// <summary>
/// 판정 하나. 거리는 <b>보스 중심으로부터의 절댓값</b>이고 높이는 바닥 기준이다.
/// 좌우를 안 가리는 이유는 프로토타입의 패턴이 전부 보스를 중심으로 대칭이기 때문이다 —
/// 한쪽만 치는 패턴이 필요해지면 부호 있는 구간으로 바꾼다.
/// </summary>
/// <param name="MinDistance">보스 중심에서 이 안쪽은 안 닿는다 (안전 주머니).</param>
/// <param name="MaxDistance">보스 중심에서 이 밖은 안 닿는다.</param>
/// <param name="LowHeight">판정의 아래끝(바닥 0).</param>
/// <param name="HighHeight">판정의 위끝.</param>
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
    double MinDistance,
    double MaxDistance,
    double LowHeight,
    double HighHeight,
    int Damage,
    bool GuardBreak = false,
    bool Finisher = false,
    double ActiveSeconds = 0);
