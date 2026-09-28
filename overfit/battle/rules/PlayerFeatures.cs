using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 망의 입력 — 이번 런의 기록 → 19칸 (#108 · 설계 2026-09-28 §4.7). <b>공장과 게임이 이 한 함수를 부른다</b>: 학습과 추론이 다른 코드로
/// 입력을 지으면 조용히 어긋난다 — 한쪽만 칸을 고치는 날 망은 틀린 칸을 읽고도 수를 낸다.
///
/// <para>
/// <b>열 순서가 계약이다</b> — <c>network.json</c> 의 <c>features</c> 가 <see cref="Names"/> 를 같은 순서로 싣고 불러올 때 대 본다. 11축 뒤에
/// 개수 여덟을 <b>날것 그대로</b> 싣는다: "3건으로 낸 0.5" 와 "300건으로 낸 0.5" 를 망이 가를 수 있어야 하고, 크기는 표준화(평균 · 편차는
/// 학습 데이터에서)가 맞춘다 — 변환 상수를 여기 또 두면 데이터가 둘이 된다.
/// </para>
///
/// <para>
/// 재료는 기록 전부(1단계 · 2단계, 붙인 순서)의 관측을 <b>이어 붙여 한 번에</b> 접은 것이다 — 기록마다 축을 내 평균하면 짧게 끝난 판의 몇 건이
/// 긴 판의 수백 건과 같은 무게를 얻는다. 패턴별로 나누지 않는다(설계 §13 — 입력이 두 배가 되고 칸마다 근거가 반이 된다).
/// </para>
/// </summary>
public static class PlayerFeatures
{
    /// <summary>칸의 이름들 — 설계 §4.7 의 표 그대로다. 11축(0 ~ 10) · 개수(11 ~ 18).</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "dash_timing_bias",
        "dash_timing_var",
        "dash_direction_bias",
        "jump_timing_bias",
        "jump_reliance",
        "airborne_at_impact",
        "parry_rate",
        "parry_reliance",
        "greed",
        "distance_bias",
        "guard_rate",
        "samples",
        "dash_samples",
        "jump_samples",
        "parry_samples",
        "guard_samples",
        "guard_broken_samples",
        "jump_choice_samples",
        "parry_choice_samples",
    ];

    /// <summary>축을 <see cref="Names"/> 의 순서로 싣는다.</summary>
    public static double[] From(PlayerAxes axes)
    {
        ArgumentNullException.ThrowIfNull(axes);
        return
        [
            axes.DashTimingBias,
            axes.DashTimingVar,
            axes.DashDirectionBias,
            axes.JumpTimingBias,
            axes.JumpReliance,
            axes.AirborneAtImpactRatio,
            axes.ParryRate,
            axes.ParryReliance,
            axes.Greed,
            axes.DistanceBias,
            axes.GuardRate,
            axes.Samples,
            axes.DashSamples,
            axes.JumpSamples,
            axes.ParrySamples,
            axes.GuardSamples,
            axes.GuardBrokenSamples,
            axes.JumpChoiceSamples,
            axes.ParryChoiceSamples,
        ];
    }

    /// <summary>
    /// 기록의 관측을 붙인 순서로 이어 한 번에 접는다. 기록이 없으면 전부 0 이다 — 첫 시도의 입력이다(<see cref="PlayerAxes"/> 가 NaN 을 안 낸다).
    /// </summary>
    public static double[] From(IReadOnlyList<AttemptRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var events = new List<DodgeEvent>();
        foreach (AttemptRecord record in records)
        {
            events.AddRange(record.Events);
        }

        return From(PlayerAxes.From(events));
    }
}
