using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 봇 함대의 성향 범위 — <c>tools/factory/fleet.json</c> 의 모양 (#104 · 설계 2026-09-28 §3.3). 게임의 수치가 아니라 <b>실험의 조건</b>이라
/// <c>overfit/data</c> 밖에 있고 게임은 안 읽는다. 범위는 [낮음, 높음] 두 칸이다. 키가 빠지면 <c>JsonData</c> 가 빠진 키를 전부 나열한다.
/// </summary>
public sealed class FleetConfig
{
    /// <summary>습관형의 몫 — 나머지가 혼합형이다.</summary>
    public required double HabitShare { get; init; }

    /// <summary>습관형(대시 · 점프 · 패리 · 가드)의 주된 수단의 비중.</summary>
    public required IReadOnlyList<double> HabitDominant { get; init; }

    /// <summary>반응 지연(초) — 선딜이 시작하고 누를 수 있게 되기까지.</summary>
    public required IReadOnlyList<double> Reaction { get; init; }

    /// <summary>누르는 시각의 편차(초).</summary>
    public required IReadOnlyList<double> Jitter { get; init; }

    /// <summary>누르는 시각의 편향(초) — 음수가 먼저다.</summary>
    public required IReadOnlyList<double> Bias { get; init; }

    /// <summary>판정을 눈으로 안 보고 기준 패턴의 박자로 누르는 몫.</summary>
    public required IReadOnlyList<double> Rhythm { get; init; }

    /// <summary>대시가 보스 쪽(안)인 몫.</summary>
    public required IReadOnlyList<double> DashInward { get; init; }

    /// <summary>쉬는 동안 칼 사거리 너머 기다리는 간격(px) — 간격 습관형이 아닌 봇.</summary>
    public required IReadOnlyList<double> RestGap { get; init; }

    /// <summary>간격 습관형의 기다리는 간격(px).</summary>
    public required IReadOnlyList<double> SpacingRestGap { get; init; }

    /// <summary>판정이 오는데도 안 피하고 칼을 넣는 몫.</summary>
    public required IReadOnlyList<double> Greed { get; init; }

    /// <summary>1타를 누를 때 2타를 이을 작정인 몫.</summary>
    public required IReadOnlyList<double> Chain { get; init; }

    /// <summary>점프를 판정 앞 몇 몫(솟는 시간의)에 누르나.</summary>
    public required IReadOnlyList<double> JumpLead { get; init; }

    /// <summary>리듬형이 따르는 기준 패턴들 — 여는 그림이 같은 패턴에서 그 기준의 판정 시각표를 쓴다(<see cref="BeatTable"/>).</summary>
    public required IReadOnlyList<string> RhythmReferences { get; init; }
}
