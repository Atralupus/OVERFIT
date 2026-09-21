namespace Overfit.Battle.Rules;

/// <summary>회피를 무엇으로 시도했나. <see cref="None"/> 은 아무것도 안 하고 서 있었다는 뜻이다.</summary>
public enum DodgeVerb
{
    None,
    Dash,
    Jump,
    Parry,
}

/// <summary>
/// 보스 판정 하나에 플레이어가 어떻게 반응했나. <b>이 줄들의 집계가 곧 플레이어 축이다.</b>
///
/// <para>
/// 판정마다 한 건씩 남는다 — 맞았든 피했든. 피한 것만 남기면 "무엇을 못 피하나"를 알 수 없다.
/// </para>
/// </summary>
/// <param name="PatternId">어느 패턴의 판정인가.</param>
/// <param name="Verb">그 순간 무엇을 하고 있었나.</param>
/// <param name="Verdict">결과. <c>Miss</c> 는 위치·점프로 피한 것이고 <c>Dodged</c> 는 대시 무적이다.</param>
/// <param name="TimingError">회피 행동 시작 시각 − 판정 시각. 음수면 이르다. 행동이 없었으면 0.</param>
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
