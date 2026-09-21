namespace Overfit.Battle.Rules;

/// <summary>회피를 무엇으로 시도했나. <see cref="None"/> 은 아무것도 안 하고 서 있었다는 뜻이다.</summary>
public enum DodgeVerb
{
    None,
    Dash,
    Jump,
    Parry,

    /// <summary>
    /// 거리가 안 닿아 그냥 빗나갔다. 행동이 아니라 <b>서 있던 자리</b>가 피하게 한 것이라
    /// 타이밍도 방향도 없다. 축으로는 <c>DistanceBias</c> 가 이미 이것을 잰다 —
    /// 그래서 11번째 축을 만들지 않고 이 값만 남긴다.
    /// </summary>
    Spacing,
}

/// <summary>
/// 보스 판정 하나에 플레이어가 어떻게 반응했나. <b>이 줄들의 집계가 곧 플레이어 축이다.</b>
///
/// <para>
/// 판정마다 한 건씩 남는다 — 맞았든 피했든. 피한 것만 남기면 "무엇을 못 피하나"를 알 수 없다.
/// </para>
/// </summary>
/// <param name="PatternId">어느 패턴의 판정인가.</param>
/// <param name="Verb">무엇이 이 결과를 만들었나. <b>판정 결과에서 끌어낸다</b> — 그 순간 돌고 있던
/// 행동을 그냥 적으면, 점프로 넘긴 판정이 같이 눌러둔 패리의 공으로 기록된다.</param>
/// <param name="Verdict">결과. 안 맞았으면 거리 때문인지 높이 때문인지까지 구별된다.</param>
/// <param name="TimingError">그 회피 행동이 판정보다 <b>얼마나 먼저</b> 시작됐나(초, 음수).
/// 0 은 행동이 없었거나(간격·무행동) 판정과 같은 틱에 시작했다는 뜻이다.
/// 지금 구조로는 양수가 나올 수 없다 — 판정이 서는 틱에 이미 시작된 행동만 보기 때문이다.
/// "늦어서 못 피했다"를 "아무것도 안 했다"와 가르려면 판정 뒤까지 기다렸다 내보내야 한다(이슈 #16).</param>
/// <param name="Direction">대시 방향. +1 보스 쪽(안) · -1 반대(밖) · 0 대시가 아님.</param>
/// <param name="Airborne">그 순간 공중에 있었나.</param>
/// <param name="Distance">보스와의 거리.</param>
/// <param name="GreedWindow">판정이 서는 그 순간 공격 중이었나 — 보스의 선딜을 욕심내 파고든 흔적이다.</param>
public readonly record struct DodgeEvent(
    string PatternId,
    DodgeVerb Verb,
    HitVerdict Verdict,
    double TimingError,
    int Direction,
    bool Airborne,
    double Distance,
    bool GreedWindow);
