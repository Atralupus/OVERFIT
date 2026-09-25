namespace Overfit.Battle.Rules;

/// <summary>
/// 한 틱의 입력. <b>뷰와 시뮬레이션 봇이 같은 것을 만든다</b> — 사람과 봇이 같은 전투 코드를 타야
/// 나중에 봇으로 만든 학습 데이터가 사람에게 의미를 갖는다.
///
/// <para>
/// <see cref="Jump"/> · <see cref="Dash"/> · <see cref="Parry"/> · <see cref="Attack"/> 넷은
/// 전부 <b>눌린 틱에만</b> true 다(엣지). 누르고 있는 상태(레벨)로 두면 봇이 "계속 누르고 있기" 로
/// 공짜 성능을 얻고, 그 데이터는 사람의 판단을 담지 않는다.
/// </para>
///
/// <para>
/// <b>레벨은 둘뿐이다 — <see cref="Move"/> · <see cref="GuardHeld"/>.</b> 가드는 누르는 순간이 아니라
/// <b>누르고 있는 동안</b>이 전부라(설계 §5.2) 엣지 칸이 없다 — 드는 값이 없으니 엣지로 시작할 이유도 없다.
/// 차지의 누름 유지(<c>AttackHeld</c>)는 차지와 같이 없어졌고, 패리의 누름 유지(<c>ParryHeld</c>)는
/// 패리가 가드와 갈라지며 <see cref="GuardHeld"/> 가 됐다 (설계 §5.4 — 이름과 키가 바뀐다).
/// </para>
/// </summary>
/// <param name="Move">-1 왼쪽 · +1 오른쪽 · 0 안 움직임. <b>레벨</b>이다.</param>
/// <param name="Jump">이 틱에 점프를 눌렀나(엣지).</param>
/// <param name="Dash">이 틱에 대시를 눌렀나(엣지).</param>
/// <param name="Parry">이 틱에 패리를 눌렀나(엣지). 누르면 0.333초 커밋이고 앞 0.133초가 창이다 (설계 §5.3) —
/// 붙들 것이 없다.</param>
/// <param name="Attack">이 틱에 공격을 눌렀나(엣지). 칼질 도중에 누르면 다음 칼을 <b>눌러 둔다</b> —
/// 2연격도 엣지 두 번이다 (설계 §5.1 · §5.4).</param>
/// <param name="GuardHeld">↓ (또는 S) 를 <b>지금 누르고 있나</b>(레벨). 누르고 있고 땅에 서 있고 커밋된 행동이
/// 없으면 그 틱이 가드다 (설계 §5.2). <b>기본값 false 가 계약이다</b> — 이 칸을 안 적은 입력은 가드를 안 든다.</param>
public readonly record struct InputFrame(
    sbyte Move,
    bool Jump,
    bool Dash,
    bool Parry,
    bool Attack,
    bool GuardHeld = false);
