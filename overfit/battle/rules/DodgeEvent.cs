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
/// <param name="TimingError">그 회피 행동이 <b>칼이 선 틱</b>(창이 열린 틱)보다 얼마나 먼저 시작됐나(초) — 음수가 먼저다.
/// 0 은 행동이 없었거나(간격·무행동) 칼이 선 그 틱에 시작했다는 뜻이다.
/// 기준은 결과와 무관하게 창이 열린 틱이다 (#72 · 설계 §3.6 ①) — 닿은 틱이나 닫힌 틱을 기준으로 하면 대시 · 점프 · 패리의
/// 오차가 서로 다른 자로 잰 값이 된다. 창이 여러 틱을 살므로 <b>양수가 뜻을 갖는다</b>: 칼이 선 뒤에 누른 것이다
/// (창이 한 틱이던 때는 구조상 양수가 없었다).</param>
/// <param name="Direction">대시 방향. +1 보스 쪽(안) · -1 반대(밖) · 0 대시가 아님.</param>
/// <param name="Airborne"><b>결과를 가른 틱</b>에 공중에 있었나 — 닿았으면 닿은 틱, 무적이 먹었으면 처음 먹은 틱,
/// 빗나갔으면 창이 열린 틱이다 (#72 · 설계 §3.6 ①). 창이 여러 틱을 살므로 "판정이 선 순간" 과 다를 수 있다 — 빗나감을
/// 닫히는 틱에 재면 창 안에서 대시가 끝난 사람이 <c>Spacing</c> 으로 적힌다(#46 의 편향).</param>
/// <param name="Distance">보스와의 거리 — 공중과 같은 틱(결과를 가른 틱)의 값이다.</param>
/// <param name="GreedWindow">결과를 가른 틱(공중과 같은 틱)에 공격 중이었나 — 보스의 선딜을 욕심내 파고든 흔적이다.
/// 1타든 2타든 칼질 중이면 여기 든다 — 2타는 1초를 서 있는 칼이라 정확히 이 축의 이야기다 (설계 §7.2). 칼질 뒤 경직(#82)도 칼질이다.</param>
/// <param name="DashAvailable">이 판정을 대시로 피할 수 있었나 (<c>dash_window &gt; 0</c>).</param>
/// <param name="JumpAvailable">점프로 넘을 수 있었나 — <b>판정 단위</b>다 (#72 · 설계 §7.3): 그 판정의
/// <see cref="HitBox.Jumpable"/>, 곧 판을 세울 때 모양의 윗끝과 이 판의 파이터 점프로 잰 값이다(<c>BossHits.TicksAbove</c>).
/// 패턴 태그 <c>jumpable</c> 이 아니다 — 태그는 패턴의 요약이라, 실으면 3연격의 2 · 3타까지 "점프도 됐다" 로 실려 점프 의존도의
/// 분모가 부푼다. 앞뒤의 대시 · 패리는 아직 태그에서 온다(판정 단위의 답은 5번 PR).</param>
/// <param name="ParryAvailable">패리로 받을 수 있었나 (<c>parryable</c>).</param>
/// <param name="GuardAvailable">가드로 막을 수 있었나 (이슈 #53).
///
/// <para>
/// 앞의 셋과 같은 자리다: <b>무엇을 골랐나</b>뿐 아니라 <b>무엇을 고를 수 있었나</b>를 같이 싣는다.
/// 3 · 4번 PR 에서는 <b>모든 판정이 참</b>이다 — 가드 불가 판정(옛 빨간 마무리 · <c>guard_break</c>)을 걷었다(#72 · 설계 §7.2).
/// 5번 PR 의 잡기가 처음으로 거짓을 싣는다(판정 단위의 답 · 설계 §7.3). 칸을 남기는 것은 그래서다 — 뺐다 넣으면 관측의
/// 모양이 두 번 바뀐다.
/// </para>
///
/// <para>
/// 스태미나 고갈로 깨지는 것은 여기 안 든다. 이 칸은 <b>판정의 성질</b>이지 그 순간 플레이어의
/// 상태가 아니다 — 섞으면 같은 판정이 남은 스태미나에 따라 다른 값으로 실린다.
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
    bool GuardAvailable);
