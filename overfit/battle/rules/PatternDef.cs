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
}

/// <summary>타임라인 한 단계. <c>kind</c> 는 windup · active · recover · end.</summary>
public sealed class PatternStep
{
    public required double T { get; init; }

    public required string Kind { get; init; }

    /// <summary>active 일 때 [최소, 최대] 거리. 다른 kind 면 null.</summary>
    public IReadOnlyList<double>? Distance { get; init; }

    /// <summary>active 일 때 [아래, 위] 높이. 다른 kind 면 null.</summary>
    public IReadOnlyList<double>? Height { get; init; }

    public int Damage { get; init; }
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
