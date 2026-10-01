using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 패턴이 "무엇을 요구하는가". <b>나중에 망의 입력이 되므로 태그가 성능의 상한을 정한다.</b>
/// 사람이 손으로 달고, <c>PatternDataTests</c> 가 타임라인과 어긋나지 않는지 본다.
/// </summary>
public sealed class PatternTags
{
    /// <summary>대시로 피할 수 있는 창의 폭(초). 0 이면 대시로 못 피한다.</summary>
    public required double DashWindow { get; init; }

    /// <summary>"in" 파고들어야 안전 · "out" 물러나야 안전 · "either".</summary>
    public required string DashDirection { get; init; }

    /// <summary>
    /// 점프로 넘을 수 있는 판정이 <b>하나라도</b> 있나 — 서서는 맞는 자리에서 뛰어 넘을 수 있는 판정이다. 패턴의 요약(망의 입력)이다.
    /// 관측의 답은 판정마다 · 자리마다 창이 열릴 때 잰다(<see cref="JumpClearance"/> · #85 · 설계 §7.3). 둘이 같은 말을 하는지는
    /// <c>PatternDataTests</c> 가 본다.
    /// </summary>
    public required bool Jumpable { get; init; }

    /// <summary>대공인가 — <b>공중에 있는 쪽이 더 맞는다.</b> 점프 의존 플레이어를 봉인하는 재료다.</summary>
    public required bool AntiAir { get; init; }

    public required bool Parryable { get; init; }

    public required double ParryWindow { get; init; }

    /// <summary>선딜이 짧아 욕심내면 맞는가.</summary>
    public required bool PunishGreed { get; init; }

    /// <summary>"close" · "mid" · "far".</summary>
    public required string Reach { get; init; }

    public required int MultiHit { get; init; }

    public required bool Tracking { get; init; }
}

/// <summary>
/// 타임라인 한 단계 (설계 §8.1). <c>kind</c> 는 windup · active · recover · end 넷이다.
///
/// <para>
/// 단계는 <b>그림의 한 장</b>을 가리킨다(<see cref="Anim"/> · <see cref="Frame"/>) — 뷰는 그 장을 그대로 붙들어 그리고,
/// 규칙은 시각 · 판정 · 움직임만 읽는다. 그래서 그림과 판정이 같은 틱에 바뀐다(설계 §6).
/// </para>
///
/// <para>
/// <b>옛 판정 종류는 걷었다</b> (#72 · 설계 §8 「지우는 것」): 가드 불가(<c>guard_break</c>) · 헛스윙(<c>kind: "feint"</c>) ·
/// 마무리 · 예고 표지. 옛 변종 아홉과 같이 없어졌다 — 새 두 단계에는 헛스윙도 빨간 마무리도 없고, 남겨 두면 아무도 안 쓰는
/// 갈래가 규칙 층에서 테스트만 붙든 채 산다. 잡기(#78)의 가드 불가는 붕괴가 아니라 잡힘이라 옛 깃발로 흉내 내지 않는다 — 판정의 답
/// (<see cref="Guard"/>)과 붙드는 시간(<see cref="GrabHoldSeconds"/>)이 대신한다.
/// </para>
/// </summary>
public sealed class PatternStep
{
    /// <summary>단계에 드는 시각(초 · 패턴이 선 뒤). 러너가 세울 때 한 번 틱으로 바꾼다(설계 §3.6 ⑤).</summary>
    public required double T { get; init; }

    public required string Kind { get; init; }

    /// <summary>
    /// 이 단계의 그림 — 보스 팩 <c>.tres</c> 의 애니메이션 이름. <b>규칙은 안 읽는다</b> — 뷰가 그리고, 데이터 테스트가 팩에 있는
    /// 이름인지 대 본다(<c>JsonData</c> 는 모르는 키를 조용히 버리므로 오타가 빌드를 그냥 지나간다). <c>end</c> 는 없다.
    /// </summary>
    public string? Anim { get; init; }

    /// <summary>
    /// 이 단계가 붙드는 장(0부터). 뷰가 그 장에 세우고 멈춘다 — 장을 제 속도로 흘려 보내면 그림이 규칙의 창보다 먼저
    /// 지나간다(설계 §6). null 이면 그 애니메이션을 제 속도로 돈다(돌진의 <c>run</c> · 잡기의 <c>idle</c> — #78).
    /// </summary>
    public int? Frame { get; init; }

    /// <summary>
    /// 판정 모양 — <c>hitboxes.json</c> 의 id(<c>팩/애니메이션/장</c> · 그림의 흰 궤적에서 뽑았다 · 설계 §3.3). active 에서
    /// <see cref="Band"/> · <see cref="Rects"/> 와 셋 중 <b>꼭 하나</b>만 갖는다(데이터 테스트가 막는다). 모양은 판을 세울 때 한 번 찾는다(<c>BossHits</c>).
    /// </summary>
    public string? Hitbox { get; init; }

    /// <summary>
    /// 좌우 대칭 띠 — [안쪽, 바깥쪽, 아래, 위] (보스 중심에서 · 발바닥에서, px · 설계 §3.3). 그림에서 뽑지 않는 모양이다:
    /// 바닥 전체를 치는 착지 같은 것. 옛 거리 띠 · 높이 띠(<c>distance</c> · <c>height</c>)를 한 칸에 모았다 —
    /// 둘이 늘 짝이었고, 모양으로는 <see cref="HitShape.Band"/> 한 가지다.
    /// </summary>
    public IReadOnlyList<double>? Band { get; init; }

    /// <summary>
    /// 손으로 적은 판정 모양 — 사각형마다 <c>[x0, x1, y0, y1]</c> (공격자 기준 · <c>hitboxes.json</c> 과 같은 좌표 — x 는 보는 쪽이 +, y 는 발바닥에서 위로).
    /// 그림의 궤적과 다르게 잡아야 하는 판정의 자리다 — 올려베기가 첫 쓰임이다(설계 2026-09-29 조각1 §2.1: 그림은 attack2 의 궤적을 뒤집어 그리되,
    /// 판정은 점프를 못 넘게 크게 잡는다). <see cref="Hitbox"/> · <see cref="Band"/> 와 셋 중 꼭 하나다.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<double>>? Rects { get; init; }

    /// <summary>
    /// 이 단계의 그림을 <b>보는 쪽의 반대로</b> 그린다 — <b>뷰만 읽는다</b>(<see cref="Anim"/> · <see cref="Frame"/> 과 같다). 규칙은 안 읽는다: 판정은
    /// 모양이 정하고, 그림에서 뽑은 모양(<see cref="Hitbox"/>)을 뒤집어 싣는 길은 없다. 올려베기가 attack2 를 뒤집어 그리는 자리다 — attack2 의 높은
    /// 궤적은 보스 등 뒤라 그대로 그리면 앞의 공중을 치는 그림이 안 된다(설계 2026-09-29 조각1 §2.1).
    /// </summary>
    public bool Mirror { get; init; }

    public int Damage { get; init; }

    /// <summary>
    /// 판정의 답 — 이 판정을 <b>대시 무적</b>으로 흘릴 수 있나 (#78 · 설계 §7.3 · §8.1). 없으면(null) 패턴 태그(<c>dash_window</c>)대로 받고,
    /// <c>false</c> 면 이 판정만 좁힌다: 무적 창 안이어도 맨몸이다. 규칙(<see cref="HitResolver"/>)과 관측(<c>DashAvailable</c>)이 같은 값을 읽는다 —
    /// 판을 세울 때 판정에 싣는다(<c>BossHits</c> · <see cref="HitBox.Dashable"/>). 잡기가 "대시중에도 잡히는" 자리다(설계 §4.7).
    /// </summary>
    public bool? Dash { get; init; }

    /// <summary>판정의 답 — <b>가드</b>로 막을 수 있나 (<see cref="Dash"/> 와 같은 규약). <c>false</c> 면 가드 중이어도 맨몸이다 — 붕괴가 아니다.</summary>
    public bool? Guard { get; init; }

    /// <summary>판정의 답 — <b>패리</b>로 받아칠 수 있나 (<see cref="Dash"/> 와 같은 규약). 태그(<c>parryable</c>)가 되는 패턴 안에서 이 판정만 막는다.</summary>
    public bool? Parry { get; init; }

    /// <summary>
    /// 붙드는 시간(초) — 0 보다 크면 <b>붙드는 판정</b>이다 (#78 · 설계 §4.7). 맨몸에 닿은 결과가 맞음이 아니라 잡힘(<see cref="HitVerdict.Grabbed"/>)이고,
    /// 파이터는 피해를 받고 이만큼 붙들린다(<c>Fighter.Held</c>). 없으면 0 — 붙들지 않는다. 가르는 것은 이 깃발과 결과다 — 패턴 이름이 아니다(CLAUDE.md §2).
    /// </summary>
    public double GrabHoldSeconds { get; init; }

    /// <summary>
    /// active 가 <b>몇 초 동안</b> 살아 있나 (이슈 #59 · 설계 §3.5). 0 이면 한 틱이다.
    ///
    /// <para>
    /// 판정이 한 틱이면 흰 궤적이 눈앞에 떠 있는데 그 뒤 틱에 걸어 들어간 사람이 안 맞는다. 그림의 궤적
    /// 한 장(8fps = 0.125초) 동안 판정이 살아 있어야 보이는 것이 곧 맞는 것이 된다. 초를 틱으로 바꾸는
    /// 반올림은 <c>BattleSim.TicksFor</c> 한 곳이다.
    /// </para>
    /// </summary>
    public double ActiveSeconds { get; init; }

    /// <summary>
    /// 이 단계에 들 때 시작하는 <b>움직임</b> (설계 §8.1) — 도약(<c>leap</c>) 같은 것. 없으면 null.
    /// 판정이 아닌 단계에만 단다: 보스는 판정 창 동안 안 움직인다(설계 §3.5 6 — 데이터 테스트가 막는다).
    /// </summary>
    public MotionDef? Motion { get; init; }
}

/// <summary>
/// 타임라인 단계가 다는 움직임 하나 (설계 §8.1). <see cref="Id"/> 가 등록표(<see cref="BossMotions"/>)의 열쇠이고
/// 나머지는 그 구현이 읽는 수치다 — 움직임을 하나 더할 때 러너도 <c>BattleSim</c> 도 안 연다(CLAUDE.md §2).
/// </summary>
public sealed class MotionDef
{
    /// <summary>등록표의 id — <c>leap</c> · <c>rush</c>.</summary>
    public required string Id { get; init; }

    /// <summary><c>leap</c>: 포물선의 정점 높이(px, 발바닥 기준).</summary>
    public double Height { get; init; }

    /// <summary><c>leap</c>: 뜬 시간(초) — 도약하는 틱부터 내리는 틱까지. 틱으로는 <c>BattleSim.TicksFor</c> 로 바꾼다.</summary>
    public double Air { get; init; }

    /// <summary><c>rush</c>: 달리는 빠르기(px/s · #78 · 설계 §4.6). 3600 이면 틱당 정확히 60px 다.</summary>
    public double Speed { get; init; }

    /// <summary><c>rush</c>: 파이터 앞 몇 px 에서 멈추나 — 앞쪽 거리 d 가 이 안이면 돌진이 끝난다(설계 §4.6).</summary>
    public double Stop { get; init; }

    /// <summary>
    /// <c>leap</c>: 착지의 쪽 (설계 2026-10-01 조각3 §2) — <c>near</c>(없으면 · 파이터 앞 몸 간격 · 점프 공격) · <c>far</c>(파이터 너머 몸 간격 · 넘어
    /// 뛰기) · <c>away</c>(파이터에게서 <see cref="Distance"/> 만큼 자기 쪽 · 뒤로 뛰기 · 보는 쪽을 안 바꾼다).
    /// </summary>
    public string? Land { get; init; }

    /// <summary><c>leap</c> 의 <c>away</c>: 파이터에게서 착지 자리까지의 거리(px).</summary>
    public double Distance { get; init; }
}

/// <summary>
/// 캔슬 지점 하나 (설계 2026-09-29 조각1 §3.1) — 다음 타의 선딜이 서는 시각이다. <b>수가 아니라 객체로 적는다</b>(<c>{ "t": 1.30 }</c>) —
/// 동작과 캔슬 조합마다의 칸(조각 4 의 <c>covers</c> · 우산 §4)을 더해도 옛 데이터의 모양이 안 깨진다(§3.6). 그 칸은 지금 안 만든다 —
/// 아무도 안 읽는 칸은 데이터 테스트만 붙든 채 산다.
/// </summary>
public sealed class CancelPointDef
{
    /// <summary>시각(초 · 패턴 시계) — 러너가 이 틱의 단계에 들지 않고 끊는다. 정수 틱이고 단계의 경계다(<c>PatternDataTests</c>).</summary>
    public required double T { get; init; }
}

/// <summary>패턴 하나. <c>data/patterns.json</c> 의 값 부분이다.</summary>
public sealed class PatternDef
{
    public required PatternTags Tags { get; init; }

    public required List<PatternStep> Timeline { get; init; }

    /// <summary>
    /// 캔슬 지점들 — 시간순 (설계 2026-09-29 조각1 §3.1). 없으면 null 이고 그 동작은 끊기지 않는다. 계획은 이 목록의 칸 번호로 지점을 고른다
    /// (<see cref="BossPlan.CancelPoint"/>) — 지점을 하나 더하면 그 동작의 계획에 캔슬이 하나 늘 뿐 코드를 안 연다(§3.3).
    /// </summary>
    public IReadOnlyList<CancelPointDef>? CancelPoints { get; init; }

    /// <summary>마지막 단계의 시각. 여기를 지나면 패턴이 끝난다.</summary>
    public double Duration => Timeline.Count == 0 ? 0 : Timeline[^1].T;
}
