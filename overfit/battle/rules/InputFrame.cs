namespace Overfit.Battle.Rules;

/// <summary>
/// 한 틱의 입력. <b>뷰와 시뮬레이션 봇이 같은 것을 만든다</b> — 사람과 봇이 같은 전투 코드를 타야
/// 나중에 봇으로 만든 학습 데이터가 사람에게 의미를 갖는다.
///
/// <para>
/// <see cref="Jump"/> 아래 넷은 전부 <b>눌린 틱에만</b> true 다(엣지). 누르고 있는 상태(레벨)로 두면
/// 봇이 "계속 누르고 있기"로 공짜 성능을 얻고, 그 데이터는 사람의 판단을 담지 않는다.
/// 점프 높이 조절처럼 길게 누르는 것이 필요해지면 <c>JumpHeld</c> 를 따로 더한다 — 겸하지 않는다.
/// </para>
/// </summary>
public readonly record struct InputFrame(sbyte Move, bool Jump, bool Dash, bool Parry, bool Attack);
