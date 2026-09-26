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
/// <param name="Finisher">이 패턴의 <b>마지막</b> 판정인가 (이슈 #53) — 러너가 타임라인의 자리에서 뽑는다.
///
/// <para>
/// ⚠ <b>규칙은 이제 이 칸을 안 읽는다</b> (#72 · 설계 §4.3). 전에는 받아치면 보스가 굳는 자리였고, 이제는 어느 타를
/// 받아쳐도 보스가 탈진한다. 관측(<c>DodgeEvent.Finisher</c>)으로만 실려 옛 변종의 테스트와 리플레이 골든이 읽는다 —
/// 3번 PR 이 옛 변종을 걷는 커밋에서 이 칸도 같이 걷는다(설계 §7.2).
/// </para></param>
/// <param name="ActiveSeconds">이 판정이 살아 있는 초 (이슈 #59). 0 이면 한 틱이다 —
/// <see cref="PatternStep.ActiveSeconds"/> 를 그대로 싣는다. 틱으로 바꾸는 것은 <c>BattleSim.TicksFor</c> 다.</param>
/// <param name="Jumpable">점프 한 번으로 창 내내 발이 이 모양 위에 있을 수 있나 (#72 · 설계 §7.3). 판을 세울 때 이 판의 파이터로
/// 잰다(<c>BossHits.TicksAbove</c>). 관측의 <c>JumpAvailable</c> 이 이것이다 — 패턴 태그(<c>jumpable</c>)는 요약일 뿐이다.</param>
public readonly record struct HitBox(
    HitShape Shape,
    int Damage,
    bool GuardBreak = false,
    bool Finisher = false,
    double ActiveSeconds = 0,
    bool Jumpable = false);
