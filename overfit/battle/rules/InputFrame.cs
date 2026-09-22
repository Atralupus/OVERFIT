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
/// <b>레벨은 둘뿐이다 — <see cref="Move"/> 와 <see cref="AttackHeld"/>.</b>
/// 차지(이슈 #40)는 "누르고 있는 <b>동안</b>" 을 알아야 해서 엣지만으로는 표현이 안 된다.
/// 그래서 <see cref="Attack"/> 을 레벨로 <b>바꾸지 않고</b> 누름 유지를 한 칸 더 실었다:
/// 바꿨다면 지금까지의 모든 입력 시퀀스(리플레이 골든 · 봇의 탭)가 "매 틱 공격" 이라는
/// 다른 뜻이 됐을 것이고, 나머지 셋과 계약이 갈려 "이 bool 은 엣지인가 레벨인가" 를
/// 칸마다 외워야 했을 것이다. 지금 규약은 한 줄로 말할 수 있다 —
/// <b>엣지가 시작하고, 레벨이 붙들고, 레벨이 꺼지는 것이 놓는 것이다.</b>
/// 점프 높이 조절처럼 길게 누르는 것이 또 필요해지면 같은 모양으로 <c>JumpHeld</c> 를 더한다.
/// </para>
/// </summary>
/// <param name="Move">-1 왼쪽 · +1 오른쪽 · 0 안 움직임. <b>레벨</b>이다.</param>
/// <param name="Jump">이 틱에 점프를 눌렀나(엣지).</param>
/// <param name="Dash">이 틱에 대시를 눌렀나(엣지).</param>
/// <param name="Parry">이 틱에 패리를 눌렀나(엣지).</param>
/// <param name="Attack">이 틱에 공격을 눌렀나(엣지). <b>차지를 시작하는 것도 이것</b>이다 —
/// 레벨만 보고 시작하면 앞 스윙이 끝나는 순간 아직 안 뗀 손가락이 저절로 다음 차지를 문다.</param>
/// <param name="AttackHeld">공격 키를 <b>지금 누르고 있나</b>(레벨). 차지가 이것으로 산다.
/// <b>기본값이 false 인 것은 계약이다</b> — 안 누르고 있는 것이 기본이라, 이 칸을 안 적은
/// 기존 입력(탭)은 "누르자마자 놓았다" 는 뜻 그대로 남는다. 눌린 틱에는 사람도 봇도
/// <see cref="Attack"/> 과 이것이 <b>같이</b> 참이고, 그때 차지가 선다.</param>
public readonly record struct InputFrame(
    sbyte Move,
    bool Jump,
    bool Dash,
    bool Parry,
    bool Attack,
    bool AttackHeld = false);
