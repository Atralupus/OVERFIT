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
    /// 안(<see cref="HitVerdict.MissedByGap"/>)과 밖(<see cref="HitVerdict.MissedTooFar"/>)이
    /// 여기서 한 값인 것은 <b>일부러다</b> (이슈 #46). 고른 수단은 둘 다 "자리" 이고, 어느 쪽 자리였나는
    /// <c>Verdict</c> 가 나른다 — <c>DistanceBias</c> 가 그 둘을 반대 부호로 읽는다.
    /// verb 를 갈라 놓으면 의존도 축의 셈법이 수단이 아니라 방향을 세게 된다.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>그 자리를 대시가 만들었으면 여기가 아니라 <see cref="Dash"/> 다</b> (이슈 #46).
    /// 대시로 사거리를 벗어난 판정은 무적이 보이기도 전에 거리에서 빠지는데, 그것까지 간격으로 적으면
    /// 대시 의존자가 간격 의존자로 기록된다. 판단은 <c>DodgeCredit</c> 의 거리 갈래(<c>CreditDistance</c>)에 있다.
    /// </para>
    /// </summary>
    Spacing,

    /// <summary>
    /// <b>가드로 버텼다</b> (이슈 #47). 막아냈는지 깨졌는지는 verb 가 아니라
    /// <c>Verdict</c>(<see cref="HitVerdict.Guarded"/> · <see cref="HitVerdict.GuardBroken"/>)가 나른다 —
    /// 고른 것은 같고 결과가 다르다.
    ///
    /// <para>
    /// <see cref="Parry"/> 와 다시 다른 키 · 다른 행동이다 (설계 §5.2 · §5.3) — 셋(패리 · 가드 · 무반응)이 갈려야
    /// "이 사람이 얼마나 정확한가" 가 남는다.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>11번째 축을 만들지 않는다.</b> 가드의 개수는 <c>PlayerAxes.GuardSamples</c> ·
    /// <c>GuardBrokenSamples</c> 가 나르고 10축 계약은 그대로다 — 이유는 그 두 프로퍼티의 주석에 있다.
    /// </para>
    /// </summary>
    Guard,
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
/// <param name="Verdict">결과. 안 맞았으면 <b>높이 때문인지 · 너무 멀어서인지 · 모양의 빈 곳(안쪽 주머니)에 서서인지</b>까지
/// 구별된다 (이슈 #46 · #59). 마지막 둘이 한 값이던 때는 파고들어 피한 것과 도망쳐 피한 것이
/// 계측에서 같은 한 점이었고, 그 둘은 봉인할 것이 정반대다.</param>
/// <param name="TimingError">그 회피 행동이 판정보다 <b>얼마나 먼저</b> 시작됐나(초, 음수).
/// 0 은 행동이 없었거나(간격·무행동) 판정과 같은 틱에 시작했다는 뜻이다.
/// 지금 구조로는 양수가 나올 수 없다 — 판정이 서는 틱에 이미 시작된 행동만 보기 때문이다.
/// "늦어서 못 피했다"를 "아무것도 안 했다"와 가르려면 판정 뒤까지 기다렸다 내보내야 한다(이슈 #16).</param>
/// <param name="Direction">대시 방향. +1 보스 쪽(안) · -1 반대(밖) · 0 대시가 아님.</param>
/// <param name="Airborne">그 순간 공중에 있었나.</param>
/// <param name="Distance">보스와의 거리.</param>
/// <param name="GreedWindow">판정이 서는 그 순간 공격 중이었나 — 보스의 선딜을 욕심내 파고든 흔적이다.
/// 1타든 2타든 칼질 중이면 여기 든다 — 2타는 1초를 서 있는 칼이라 정확히 이 축의 이야기다 (설계 §7.2).</param>
/// <param name="DashAvailable">이 판정을 대시로 피할 수 있었나 (<c>dash_window &gt; 0</c>).</param>
/// <param name="JumpAvailable">점프로 넘을 수 있었나 (<c>jumpable</c>).</param>
/// <param name="ParryAvailable">패리로 받을 수 있었나 (<c>parryable</c>).</param>
/// <param name="GuardAvailable">가드로 막을 수 있었나 — <c>guard_break</c> 가 <b>아닌</b> 판정이다 (이슈 #53).
///
/// <para>
/// 앞의 셋과 같은 자리다: <b>무엇을 골랐나</b>뿐 아니라 <b>무엇을 고를 수 있었나</b>를 같이 싣는다.
/// 계열 하나에 변종 아홉인 지금, 모든 패턴의 마무리가 이 값이 false 라(이슈 #53 · 빨강 = 가드 불가)
/// 같은 패턴 안에서 "막을 수 있는 판정" 과 "받아칠 수밖에 없는 판정" 이 관측에서 실제로 갈린다.
/// </para>
///
/// <para>
/// 스태미나 고갈로 깨지는 것은 여기 안 든다. 이 칸은 <b>판정의 성질</b>이지 그 순간 플레이어의
/// 상태가 아니다 — 섞으면 같은 판정이 남은 스태미나에 따라 다른 값으로 실린다.
/// </para>
///
/// <para>
/// 뷰도 이 값을 읽는다: <c>危</c> 표지가 걸리는 자리가 여기다. 규칙 층에 뷰용 콜백을 달지
/// 않으므로 그 사실이 관측에 실려 있어야 한다.
/// </para></param>
/// <param name="Finisher">이 패턴의 <b>마지막</b> 판정이었나 (이슈 #53).
///
/// <para>
/// ⚠ <b>뜻을 잃은 칸이다</b> (#72 · 설계 §7.2). 마무리를 받아친 것만 보스를 굳히던 때는 "무엇을 건 판정이었나" 를 실었고
/// 뷰의 히트스톱도 이 칸에 걸렸다. 이제 어느 타를 받아쳐도 보스가 탈진하고, 히트스톱은 보스가 탈진에 드는 틱에 건다 —
/// 규칙도 뷰도 이 칸을 안 읽는다. 옛 변종의 테스트와 리플레이 골든의 다이제스트만 읽고, 3번 PR 이 옛 변종을 걷는
/// 커밋에서 같이 걷는다.
/// </para></param>
public readonly record struct DodgeEvent(
    string PatternId,
    DodgeVerb Verb,
    HitVerdict Verdict,
    double TimingError,
    int Direction,
    bool Airborne,
    double Distance,
    bool GreedWindow,

    // 무엇을 골랐나뿐 아니라 **무엇을 고를 수 있었나**를 같이 싣는다.
    // PlayerAxes.From 은 이벤트 목록만 받으므로 구조상 패턴 태그에 손이 안 닿는다 —
    // 여기 없으면 의존도 축은 사용 비율로밖에 못 만들어지고, 그건 "점프에 의존한다" 와
    // "점프로만 피할 수 있는 패턴만 만났다" 를 구별하지 못한다.
    bool DashAvailable,
    bool JumpAvailable,
    bool ParryAvailable,
    bool GuardAvailable,
    bool Finisher);
