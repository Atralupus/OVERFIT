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
    /// 점프로 넘을 수 있는 판정이 <b>하나라도</b> 있나 — 패턴의 요약(망의 입력)이다. 판정마다의 답은 모양과 캐릭터의 점프로
    /// 판을 세울 때 잰다(<c>HitBox.Jumpable</c> · 설계 §7.3). 둘이 같은 말을 하는지는 <c>PatternDataTests</c> 가 본다.
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
/// 갈래가 규칙 층에서 테스트만 붙든 채 산다. 잡기(5번 PR)의 가드 불가는 붕괴가 아니라 잡힘이라 옛 깃발로 흉내 내지 않는다.
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
    /// 지나간다(설계 §6). null 이면 그 애니메이션을 제 속도로 돈다(<c>run</c> · <c>idle</c> — 5번 PR).
    /// </summary>
    public int? Frame { get; init; }

    /// <summary>
    /// 판정 모양 — <c>hitboxes.json</c> 의 id(<c>팩/애니메이션/장</c> · 그림의 흰 궤적에서 뽑았다 · 설계 §3.3). active 에서
    /// <see cref="Band"/> 와 <b>꼭 하나</b>만 갖는다(데이터 테스트가 막는다). 모양은 판을 세울 때 한 번 찾는다(<c>BossHits</c>).
    /// </summary>
    public string? Hitbox { get; init; }

    /// <summary>
    /// 좌우 대칭 띠 — [안쪽, 바깥쪽, 아래, 위] (보스 중심에서 · 발바닥에서, px · 설계 §3.3). 그림에서 뽑지 않는 모양이다:
    /// 바닥 전체를 치는 착지 같은 것. 옛 거리 띠 · 높이 띠(<c>distance</c> · <c>height</c>)를 한 칸에 모았다 —
    /// 둘이 늘 짝이었고, 모양으로는 <see cref="HitShape.Band"/> 한 가지다.
    /// </summary>
    public IReadOnlyList<double>? Band { get; init; }

    public int Damage { get; init; }

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
    /// <summary>등록표의 id — <c>leap</c>.</summary>
    public required string Id { get; init; }

    /// <summary><c>leap</c>: 포물선의 정점 높이(px, 발바닥 기준).</summary>
    public double Height { get; init; }

    /// <summary><c>leap</c>: 뜬 시간(초) — 도약하는 틱부터 내리는 틱까지. 틱으로는 <c>BattleSim.TicksFor</c> 로 바꾼다.</summary>
    public double Air { get; init; }
}

/// <summary>패턴 하나. <c>data/patterns.json</c> 의 값 부분이다.</summary>
public sealed class PatternDef
{
    public required PatternTags Tags { get; init; }

    public required List<PatternStep> Timeline { get; init; }

    /// <summary>마지막 단계의 시각. 여기를 지나면 패턴이 끝난다.</summary>
    public double Duration => Timeline.Count == 0 ? 0 : Timeline[^1].T;
}
