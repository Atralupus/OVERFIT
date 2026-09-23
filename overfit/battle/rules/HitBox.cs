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
public readonly record struct HitBox(
    double MinDistance,
    double MaxDistance,
    double LowHeight,
    double HighHeight,
    int Damage,
    bool GuardBreak = false);
