namespace Overfit.Battle.Rules;

/// <summary>
/// 판정 하나 — <b>모양</b>과 피해 (이슈 #59 · 설계 §3). 모양은 공격자 기준이고, 어디에 놓을지(보스의 발 ·
/// 보는 쪽)는 판정할 때 <see cref="Placement"/> 로 준다. 옛 판정은 "보스 중심에서 거리 몇~몇 · 높이 몇~몇" 인
/// 좌우 대칭 띠였고, 그 띠는 이제 <see cref="HitShape.Band"/> 두 장으로 같은 통로를 탄다.
///
/// <para>
/// <b>점프로 넘을 수 있나는 여기 없다</b> (#85). 판을 세울 때 모양의 윗끝 하나로 재 두던 칸(<c>Jumpable</c>)이었는데, 넘을 수 있나는
/// 판정이 아니라 <b>그 판정이 선 자리</b>의 것이다 — 창이 열린 틱의 첫 판정이 선 자리에서 잰다(<see cref="JumpClearance"/> · <c>BossSwings.Step</c>).
/// </para>
/// </summary>
/// <param name="Shape">판정 모양 (공격자 기준 사각형들).</param>
/// <param name="Damage">막지 않았을 때의 피해.</param>
/// <param name="ActiveSeconds">이 판정이 살아 있는 초 (이슈 #59). 0 이면 한 틱이다 —
/// <see cref="PatternStep.ActiveSeconds"/> 를 그대로 싣는다. 틱으로 바꾸는 것은 <c>BattleSim.TicksFor</c> 다.</param>
/// <param name="Dashable">판정의 답 — 대시 무적을 받나 (#78 · 설계 §7.3). 단계의 <c>dash</c> 이고 없으면 참이다. 태그를 <b>좁히기만</b>
/// 한다: 참이어도 태그의 대시 창(<c>dash_window</c>)이 0 이면 못 흘린다. 규칙(<see cref="HitResolver.Effective"/>)과 관측이 같이 읽는다.</param>
/// <param name="Guardable">판정의 답 — 가드로 막나. 단계의 <c>guard</c> 이고 없으면 참이다. 거짓이면 가드 중이어도 맨몸이다.</param>
/// <param name="Parryable">판정의 답 — 패리로 받아치나. 단계의 <c>parry</c> 이고 없으면 참이다. 태그의 <c>parryable</c> 을 좁히기만 한다.</param>
public readonly record struct HitBox(
    HitShape Shape,
    int Damage,
    double ActiveSeconds = 0,
    bool Dashable = true,
    bool Guardable = true,
    bool Parryable = true);
