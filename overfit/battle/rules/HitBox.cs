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
/// <b><see cref="GuardBreak"/> 와 따로 두는 것이 이 이슈의 결정이다.</b> 둘을 한 깃발로 묶으면
/// 유저가 실제로 하고 있는 1단계에 그 고리가 통째로 없다 — 가드 불가는 3단계 다섯 변종의
/// 마무리에만 붙기 때문이다. 넓혀서 묶는 길도 막혀 있다: 아홉 변종 전부를 가드 불가로 만들면
/// <c>II-쐐기</c> 와 <c>III-쐐기</c> 가 글자 하나 안 다른 같은 패턴이 되고, 세 쌍(끌기 · 쇄도 · 쐐기)이
/// 화면에서 갈리던 유일한 표지(危)도 같이 사라진다. 그래서 <b>상은 마무리에</b> 걸고,
/// 가드 불가는 그 위에 얹는 한 겹("막을 수조차 없다")으로 남겼다.
/// </para>
///
/// <para>
/// <b>데이터에 손으로 안 적는다.</b> 타임라인의 마지막 active 가 곧 이것이다 —
/// <c>finisher: true</c> 를 사람이 달면 판정을 하나 끼워 넣는 날 옛 마무리에 그 표가 남고,
/// 그 거짓말은 테스트가 아니라 플레이 중에만 보인다.
/// </para></param>
public readonly record struct HitBox(
    double MinDistance,
    double MaxDistance,
    double LowHeight,
    double HighHeight,
    int Damage,
    bool GuardBreak = false,
    bool Finisher = false);
