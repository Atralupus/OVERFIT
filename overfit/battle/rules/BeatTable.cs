using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 리듬형 봇이 따르는 판정 시각표 (#104 · 설계 2026-09-28 §3.3). 패턴마다, 그 <b>여는 그림</b>(첫 단계의 anim · frame)이 기준 패턴 하나와 같으면
/// 그 기준의 <c>active</c> 단계 시각(초 · 패턴이 선 뒤)들을 준다.
///
/// <para>
/// 왜 여는 그림인가: 사람은 여는 동작이 같은 공격을 박자로 구별하지 못한다. 엇박 3연격은 3연격과 여는 그림이 같고 타마다 선딜만 늦다 —
/// 칼을 보고 누르는 사람은 받아치고, 3연격의 박자로 누르는 사람은 커밋 안에서 맞는다(설계 2026-09-24 §4.9). 봇이 전부 눈으로 누르면 엇박은
/// 누구에게도 더 어렵지 않고 망은 "패리 의존 → 엇박" 을 못 배운다. 기준 id 는 <c>fleet.json</c> 의 <c>rhythm_references</c> 다 — 봇이 패턴
/// 이름으로 가르지 않는다(CLAUDE.md §2).
/// </para>
/// </summary>
public sealed class BeatTable
{
    private readonly Dictionary<string, IReadOnlyList<double>> _beats = new(StringComparer.Ordinal);

    /// <summary>
    /// 기준마다 그 여는 그림과 판정 시각을 모아 두고, 명부의 패턴마다 여는 그림이 같은 첫 기준을 붙인다. 데이터에 없는 기준은
    /// <see cref="ArgumentException"/> — 사람이 적는 목록이라 세우는 자리에서 멈춘다.
    /// </summary>
    public BeatTable(IReadOnlyDictionary<string, PatternDef> patterns, IReadOnlyList<string> references)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(references);

        var openings = new List<(string Anim, int? Frame, IReadOnlyList<double> Times)>();
        foreach (string id in references)
        {
            if (!patterns.TryGetValue(id, out PatternDef? reference))
            {
                throw new ArgumentException($"리듬 기준 패턴이 데이터에 없다 — {id}", nameof(references));
            }

            if (Opening(reference) is { } open)
            {
                openings.Add((open.Anim, open.Frame, ActiveTimes(reference)));
            }
        }

        foreach ((string id, PatternDef def) in patterns)
        {
            if (Opening(def) is not { } open)
            {
                continue;
            }

            foreach ((string anim, int? frame, IReadOnlyList<double> times) in openings)
            {
                if (string.Equals(anim, open.Anim, StringComparison.Ordinal) && frame == open.Frame)
                {
                    _beats[id] = times;
                    break;
                }
            }
        }
    }

    /// <summary>그 패턴이 따를 시각표 — 여는 그림이 어느 기준과도 다르거나 모르는 id 면 null(그 패턴은 눈으로 잰다).</summary>
    public IReadOnlyList<double>? For(string patternId) => _beats.TryGetValue(patternId, out IReadOnlyList<double>? times) ? times : null;

    /// <summary>첫 단계의 그림 — 그림이 없는 패턴(시험 패턴)은 어느 기준과도 같지 않다.</summary>
    private static (string Anim, int? Frame)? Opening(PatternDef def) =>
        def.Timeline.Count > 0 && def.Timeline[0].Anim is { } anim ? (anim, def.Timeline[0].Frame) : null;

    private static List<double> ActiveTimes(PatternDef def)
    {
        var times = new List<double>();
        foreach (PatternStep step in def.Timeline)
        {
            if (step.Kind == "active")
            {
                times.Add(step.T);
            }
        }

        return times;
    }
}
