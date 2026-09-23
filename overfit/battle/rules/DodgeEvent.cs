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
    ///
    /// <para>
    /// 안(<see cref="HitVerdict.MissedTooClose"/>)과 밖(<see cref="HitVerdict.MissedTooFar"/>)이
    /// 여기서 한 값인 것은 <b>일부러다</b> (이슈 #46). 고른 수단은 둘 다 "자리" 이고, 어느 쪽 자리였나는
    /// <c>Verdict</c> 가 나른다 — <c>DistanceBias</c> 가 그 둘을 반대 부호로 읽는다.
    /// verb 를 갈라 놓으면 의존도 축의 셈법이 수단이 아니라 방향을 세게 된다.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>그 자리를 대시가 만들었으면 여기가 아니라 <see cref="Dash"/> 다</b> (이슈 #46).
    /// 대시로 사거리를 벗어난 판정은 무적이 보이기도 전에 거리에서 빠지는데, 그것까지 간격으로 적으면
    /// 대시 의존자가 간격 의존자로 기록된다. 판단은 <c>BattleSim.CreditDistance</c> 에 있다.
    /// </para>
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
/// <param name="Verdict">결과. 안 맞았으면 <b>높이 때문인지 · 너무 멀어서인지 · 너무 가까워서인지</b>까지
/// 구별된다 (이슈 #46). 마지막 둘이 한 값이던 때는 파고들어 피한 것과 도망쳐 피한 것이
/// 계측에서 같은 한 점이었고, 그 둘은 봉인할 것이 정반대다.</param>
/// <param name="TimingError">그 회피 행동이 판정보다 <b>얼마나 먼저</b> 시작됐나(초, 음수).
/// 0 은 행동이 없었거나(간격·무행동) 판정과 같은 틱에 시작했다는 뜻이다.
/// 지금 구조로는 양수가 나올 수 없다 — 판정이 서는 틱에 이미 시작된 행동만 보기 때문이다.
/// "늦어서 못 피했다"를 "아무것도 안 했다"와 가르려면 판정 뒤까지 기다렸다 내보내야 한다(이슈 #16).</param>
/// <param name="Direction">대시 방향. +1 보스 쪽(안) · -1 반대(밖) · 0 대시가 아님.</param>
/// <param name="Airborne">그 순간 공중에 있었나.</param>
/// <param name="Distance">보스와의 거리.</param>
/// <param name="GreedWindow">판정이 서는 그 순간 공격 중이었나 — 보스의 선딜을 욕심내 파고든 흔적이다.
/// <b>모으고 선 것(차지)도 여기 든다</b> (이슈 #40): 차지는 더 오래 서 있는 공격이라
/// 정확히 이 축의 이야기고, 빼면 새 기술이 생긴 자리에서 축만 눈을 감는다.</param>
/// <param name="ChargeTier">그 순간 들고 있던 차지 단계 (0 = 안 모았다).
/// <b>비율이 아니라 깊이를 나른다</b> — GreedWindow 만으로는 "휘두르다 맞았다" 와
/// "2초를 모으고 서 있다 맞았다" 가 한 점이 되는데, 그 둘은 건 것의 크기가 다르다.</param>
/// <param name="DashAvailable">이 판정을 대시로 피할 수 있었나 (<c>dash_window &gt; 0</c>).</param>
/// <param name="JumpAvailable">점프로 넘을 수 있었나 (<c>jumpable</c>).</param>
/// <param name="ParryAvailable">패리로 받을 수 있었나 (<c>parryable</c>).</param>
public readonly record struct DodgeEvent(
    string PatternId,
    DodgeVerb Verb,
    HitVerdict Verdict,
    double TimingError,
    int Direction,
    bool Airborne,
    double Distance,
    bool GreedWindow,
    int ChargeTier,

    // 무엇을 골랐나뿐 아니라 **무엇을 고를 수 있었나**를 같이 싣는다.
    // PlayerAxes.From 은 이벤트 목록만 받으므로 구조상 패턴 태그에 손이 안 닿는다 —
    // 여기 없으면 의존도 축은 사용 비율로밖에 못 만들어지고, 그건 "점프에 의존한다" 와
    // "점프로만 피할 수 있는 패턴만 만났다" 를 구별하지 못한다.
    bool DashAvailable,
    bool JumpAvailable,
    bool ParryAvailable);
