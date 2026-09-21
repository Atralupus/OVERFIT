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

/// <summary>패턴 하나. <c>data/patterns.json</c> 의 값 부분이다.</summary>
public sealed class PatternDef
{
    public required PatternTags Tags { get; init; }

    public required List<PatternStep> Timeline { get; init; }

    /// <summary>마지막 단계의 시각. 여기를 지나면 패턴이 끝난다.</summary>
    public double Duration => Timeline.Count == 0 ? 0 : Timeline[^1].T;
}
