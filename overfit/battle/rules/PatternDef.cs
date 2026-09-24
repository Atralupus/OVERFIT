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

    /// <summary>점프로 넘을 수 있는 낮은 판정인가.</summary>
    public required bool Jumpable { get; init; }

    /// <summary>대공인가 — <b>공중에 있는 쪽이 더 맞는다.</b> 점프 의존 플레이어를 봉인하는 재료다.</summary>
    public required bool AntiAir { get; init; }

    public required bool Parryable { get; init; }

    public required double ParryWindow { get; init; }

    /// <summary>선딜이 짧아 욕심내면 맞는가.</summary>
    public required bool PunishGreed { get; init; }

    /// <summary>"close" · "mid" · "far".</summary>
    public required string Reach { get; init; }

    public required bool Feint { get; init; }

    public required int MultiHit { get; init; }

    public required bool Tracking { get; init; }

    /// <summary>
    /// 이 패턴에 <b>가드 불가 판정이 하나라도</b> 있나 (이슈 #47). 망의 입력용 <b>요약</b>이고,
    /// 진실은 타임라인 쪽의 <see cref="PatternStep.GuardBreak"/> 다 — 가드 불가는 판정 단위라
    /// 계열의 마지막 한 대에만 붙는다. 둘이 같은 말을 하는지는 <c>PatternDataTests</c> 가 본다
    /// (<c>multi_hit</c> ↔ active 개수와 같은 규약이다).
    ///
    /// <para>
    /// 요약을 따로 두는 이유는 <b>예고</b> 때문이기도 하다. 화면은 선딜에 "이번 것은 못 막는다" 를
    /// 말해야 하는데, 그때는 아직 어느 판정이 올지가 아니라 <b>무엇이 오는가</b>만 정해져 있다.
    /// </para>
    /// </summary>
    public required bool HasGuardBreak { get; init; }
}

/// <summary>
/// 타임라인 한 단계. <c>kind</c> 는 windup · active · <b>feint</b> · recover · end.
///
/// <para>
/// <b><c>feint</c> 는 판정이 없는 박자다</b> (이슈 #48) — 칼은 지나가는데 아무것도 안 닿는다.
/// <c>damage: 0</c> 짜리 active 로 흉내 내지 않는 이유는 그것이 <b>계측을 오염시키기</b> 때문이다:
/// 0 짜리도 판정이라 <c>DodgeEvent</c> 가 한 건 남고, 일어난 적 없는 판정에 대한
/// "안 맞았다"(거리 · verb · 가능했던 수단)가 학습 데이터에 그대로 실린다.
/// 회피 기록이 곧 변종 선택의 입력이므로 그 한 줄이 정반대 변종을 뽑는다.
/// </para>
/// </summary>
public sealed class PatternStep
{
    public required double T { get; init; }

    public required string Kind { get; init; }

    /// <summary>active 일 때 [최소, 최대] 거리. 다른 kind 면 null.</summary>
    public IReadOnlyList<double>? Distance { get; init; }

    /// <summary>active 일 때 [아래, 위] 높이. 다른 kind 면 null.</summary>
    public IReadOnlyList<double>? Height { get; init; }

    public int Damage { get; init; }

    /// <summary>
    /// 이 판정을 <b>가드로는 못 막나</b> (이슈 #47). 스태미나가 남아 있어도 가드가 깨지고 피해는 전액이다.
    /// <b>판정 단위인 것이 설계다</b> — 계열의 마지막 한 대에만 붙으므로, 패턴 단위로 두면
    /// 앞의 연타까지 못 막게 되어 "버티다 마지막에 받아쳐라" 라는 이 기술의 문장이 사라진다.
    /// </summary>
    public bool GuardBreak { get; init; }

    /// <summary>
    /// active 가 <b>몇 초 동안</b> 살아 있나 (이슈 #59 · 설계 §3.5). 0 이면 한 틱이다 — 옛 패턴은 전부 그렇다.
    ///
    /// <para>
    /// 판정이 한 틱이면 흰 궤적이 눈앞에 떠 있는데 그 뒤 틱에 걸어 들어간 사람이 안 맞는다. 그림의 궤적
    /// 한 장(8fps = 0.125초) 동안 판정이 살아 있어야 보이는 것이 곧 맞는 것이 된다. 초를 틱으로 바꾸는
    /// 반올림은 <c>BattleSim.TicksFor</c> 한 곳이다.
    /// </para>
    /// </summary>
    public double ActiveSeconds { get; init; }
}

/// <summary>
/// 이 패턴이 <b>무엇인지</b>를 선딜에 알리는 표지. 선딜 링은 "뭔가 온다" 까지만 말하고
/// 종류는 안 말한다 — 백장의 두 패턴은 <b>칼이 땅에 있나 떠 있나</b> 로만 갈리므로(이슈 #28)
/// 그 차이가 화면에 없으면 두 패턴은 플레이어에게 같은 공격이다.
///
/// <para>
/// <b>여기 있는 것은 수치와 id 뿐이고 그리는 법은 뷰가 안다.</b> <see cref="Id"/> 는 뷰의
/// 등록표(모양 id → 구현)를 조회하는 열쇠다 — 그래서 네 번째 패턴이 기존 모양을 쓰면
/// C# 을 한 줄도 안 연다. 규칙 층은 이 값을 읽지 않는다: 전투 결과는 예고가 어떻게 생겼는지와
/// 무관해야 하고, 그래야 헤드리스 판이 화면 없이도 같은 판이다.
/// </para>
/// </summary>
public sealed class PatternTell
{
    /// <summary>예고 모양 id. 뷰의 등록표가 이것으로 구현을 찾는다.</summary>
    public required string Id { get; init; }

    /// <summary>선딜에 재생할 보스 애니메이션 이름 (<c>attack</c> · <c>attack2</c> · <c>attack3</c>).
    /// <b>이름이 틀려도 빌드는 통과한다</b> — 뷰가 <c>HasAnimation</c> 으로 막고 <c>[W]</c> 를 남긴다.</summary>
    public required string Anim { get; init; }

    /// <summary>표지의 가로 위치. 보스 중심에서의 오프셋(px)이고 <b>음수는 등 뒤</b>다.</summary>
    public required double X { get; init; }

    /// <summary>표지의 높이(px, 바닥 0). <b>이 숫자 하나가 "칼이 땅에 있나" 를 말한다.</b></summary>
    public required double Y { get; init; }

    /// <summary>표지의 주된 크기(px). 모양마다 뜻이 다르다 — 칼이면 날 길이, 고리면 반지름이다.</summary>
    public required double Length { get; init; }
}

/// <summary>패턴 하나. <c>data/patterns.json</c> 의 값 부분이다.</summary>
public sealed class PatternDef
{
    public required PatternTags Tags { get; init; }

    /// <summary>선딜에 "무엇이 오는가" 를 말하는 표지.</summary>
    public required PatternTell Tell { get; init; }

    public required List<PatternStep> Timeline { get; init; }

    /// <summary>마지막 단계의 시각. 여기를 지나면 패턴이 끝난다.</summary>
    public double Duration => Timeline.Count == 0 ? 0 : Timeline[^1].T;
}
