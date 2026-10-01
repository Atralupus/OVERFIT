using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 보스의 계획 하나 (설계 2026-09-29 조각1 §3.3) — 다음을 고를 때 <b>통째로</b> 고르고, 고른 뒤에는 안 바꾼다(우산 §0 의 정직한 예고).
///
/// <code>
/// 흐름  쉬기(제자리 · 파이터 쪽으로 돌아서기만) → [달리기] → 첫 동작 → [캔슬 지점에서 끊고 돌아서서] 잇는 동작 → 다음 계획
/// </code>
///
/// <para>
/// <b>쉬기를 계획의 앞에 둔다.</b> 쉬는 길이는 뒤의 동작과 짝이다 — 빠른 3연격이 2연격을 벌하는지는 그 앞의 쉬기가 정한다(§2.2 · §3.4). 둘을
/// 한 번에 골라야 조각 4 의 규칙이 "짧게 쉬고 빠른 3연격" 을 한 수로 낸다. 달리기(§5)도 계획의 한 칸이라 조각 4 의 "멀리 서는 사람" 에게 고르기만
/// 바꾸면 된다 — 판은 안 연다.
/// </para>
/// </summary>
/// <param name="RestTicks">쉬는 틱 — 1 이상이다(<c>bosses.json</c> 의 <c>rest_seconds</c> 를 틱으로 바꾼 것 중 하나).</param>
/// <param name="Move">첫 동작 — 명부(<c>stages.json</c> 의 <c>patterns</c>)의 칸.</param>
/// <param name="CancelPoint">캔슬 지점 — 첫 동작의 <see cref="PatternDef.CancelPoints"/> 의 칸. 끊지 않으면 null.</param>
/// <param name="Next">잇는 동작 — 명부의 칸이고 첫 동작과 다르다(같은 것을 이으면 1타를 한 번 더 할 뿐이다 · §3.2). 끊지 않으면 null.</param>
/// <param name="Run">쉬기 뒤에 파이터 앞(<see cref="BossConfig.RunStop"/>)까지 달리나 (§5.2) — 이미 그 안이면 안 달린다.</param>
public sealed record BossPlan(int RestTicks, int Move, int? CancelPoint, int? Next, bool Run = false);

/// <summary>
/// 계획을 고를 때 판이 넘기는 것 (설계 2026-09-29 조각1 §4.1) — 판 안에서 일어난 일이 고르기에 드는 자리다. 조각 4 의 망이 여기서 읽는다.
/// 조각 1 의 무작위는 번호만 읽는다.
/// </summary>
/// <param name="Number">계획 번호 — 이 판에서 몇 번째로 고르나(0 부터). 무작위 고르기의 좌표(<c>k1</c>)이고, 버린 계획도 번호를 쓴다.</param>
/// <param name="Tick">판의 틱.</param>
/// <param name="Events">이 판의 관측 — <see cref="BattleSim.Events"/> 를 읽기 전용으로.</param>
/// <param name="BossX">보스의 자리.</param>
/// <param name="BossFacing">보스가 보는 쪽(+1 · −1).</param>
/// <param name="FighterX">파이터의 자리.</param>
/// <param name="Form">보스의 지금 형태 — 1 부터(설계 2026-10-01 조각1 §2.4). 조각 1 의 고르기는 안 읽는다 — 조각 7 의 망이 형태마다 가중치를 고르는 자리다.</param>
public readonly record struct PlanRequest(int Number, int Tick, IReadOnlyList<DodgeEvent> Events, double BossX, int BossFacing, double FighterX, int Form);

/// <summary>
/// 다음 계획을 고른다 (설계 2026-09-29 조각1 §4.1). 부르는 자리는 <see cref="BattleSim"/> 하나다 — 판이 설 때 · 계획이 끝날 때(탈진으로
/// 끊겨도). 명부 밖 · 없는 지점 · 첫 동작과 같은 잇는 동작을 내면 판이 <c>[E] plan_invalid</c> 를 남기고 그 계획을 버린다(<see cref="PlanFlow"/>).
/// 고르기를 하나 더할 때 등록표(<see cref="PatternPickers"/>)에 한 줄이다 — <see cref="BattleSim"/> 은 안 연다.
/// </summary>
public interface IPlanPicker
{
    BossPlan Next(PlanRequest request);
}

/// <summary>
/// 대본의 한 칸 (설계 2026-09-29 조각1 §4.2) — 계획의 다섯을 다 적는다. GIF · 스크린샷 · 씬 순회가 판을 고정하는 데 쓴다: GIF 의 틱이 쉬기에
/// 달려 있어 쉬는 길이까지 적어야 같은 장면이 선다. 동작은 id 로 적는다(명부의 칸 번호는 명부가 바뀌면 다른 동작을 가리킨다).
/// </summary>
/// <param name="RestSeconds">쉬는 길이(초) — 0 보다 크다.</param>
/// <param name="Move">첫 동작의 id.</param>
/// <param name="CancelPoint">캔슬 지점 — 첫 동작의 <see cref="PatternDef.CancelPoints"/> 의 칸. 끊지 않으면 null.</param>
/// <param name="Next">잇는 동작의 id — <paramref name="CancelPoint"/> 와 같이 있거나 같이 없다.</param>
/// <param name="Run">쉬기 뒤에 파이터 앞까지 달리나 (§5.2).</param>
public sealed record ScriptPlan(double RestSeconds, string Move, int? CancelPoint = null, string? Next = null, bool Run = false);

/// <summary>
/// 기록에 싣는 계획 하나 (설계 2026-09-29 조각1 §4.3) — <see cref="BossPlan"/> 을 사람이 읽는 모양으로: 칸 대신 id, 틱 대신 초. 명부의 칸 번호는
/// 명부가 바뀌면 다른 동작을 가리킨다. 대본으로 선 시도는 이것이 곧 대본이다(<see cref="Replay.Script"/>).
/// </summary>
/// <param name="Rest">쉬는 길이(초).</param>
/// <param name="Move">첫 동작의 id.</param>
/// <param name="Cancel">캔슬 지점의 시각(초 · <c>patterns.json</c> 의 <c>cancel_points[].t</c> 그대로). 끊지 않으면 null.</param>
/// <param name="Next">잇는 동작의 id. 끊지 않으면 null.</param>
/// <param name="Run">쉬기 뒤에 달리나 (§5.2). 6/8 의 줄에는 없다 — 그때는 보스가 안 달렸다(false).</param>
public sealed record PlanEntry(double Rest, string Move, double? Cancel, string? Next, bool Run = false);
