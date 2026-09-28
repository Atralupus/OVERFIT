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

    /// <summary>
    /// 겨냥 표 (#108 · 설계 2026-09-28 §4.6) — 패턴의 판정이 스스로 막는 수단: 그 습관형 봇은 그 패턴에 기저율보다 더 맞아야 한다. 공장(원본) ·
    /// 학습(망의 들어 올림) · 검증(망 보스)이 <b>이 표 하나</b>를 읽는다 — 셋이 따로 적으면 한쪽만 고친 날 관문이 다른 것을 잰다. 처음에는 README 의
    /// GIF 표를 옮겼는데 원본에 한 줄만 서서 2번 PR 이 재서 바꿨다(까닭은 설계 §4.6).
    /// </summary>
    public required IReadOnlyList<TargetingRow> Targeting { get; init; }
}

/// <summary>겨냥 표의 한 줄 — 이 습관형은 이 패턴에 더 맞아야 한다.</summary>
public sealed class TargetingRow
{
    /// <summary>습관형 — 혼합형(<see cref="BotHabit.Mixed"/>)은 겨냥 표에 없다.</summary>
    public required BotHabit Habit { get; init; }

    /// <summary>2단계 명부의 패턴 id.</summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// 이 리듬 이상인 봇만 센다 — 없으면 0(전부). 패리 습관형 중 눈으로 누르는 봇은 엇박에 안 속는다: 엇박이 노리는 것은 패리가 아니라
    /// <b>박자로 누르는</b> 패리다(설계 2026-09-24 §4.9).
    /// </summary>
    public double MinRhythm { get; init; }
}
