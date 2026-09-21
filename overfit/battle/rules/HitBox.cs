namespace Overfit.Battle.Rules;

/// <summary>
/// 판정 하나의 기하. 거리는 <b>보스 중심으로부터의 절댓값</b>이고 높이는 바닥 기준이다.
/// 좌우를 안 가리는 이유는 프로토타입의 패턴이 전부 보스를 중심으로 대칭이기 때문이다 —
/// 한쪽만 치는 패턴이 필요해지면 부호 있는 구간으로 바꾼다.
/// </summary>
public readonly record struct HitBox(
    double MinDistance,
    double MaxDistance,
    double LowHeight,
    double HighHeight,
    int Damage);
