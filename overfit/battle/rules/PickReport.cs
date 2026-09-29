using System;
using System.Collections.Generic;
using System.Linq;

namespace Overfit.Battle.Rules;

/// <summary>
/// 결과 화면의 패턴 리포트 (#122) — 보스가 이 판의 패턴을 어떻게 골랐고, 무엇이 몇 번 나와 몇 번 맞았나. 씬(<c>Battle</c>)은 줄을 받아 그리기만 한다.
///
/// <para>
/// <b>망이 없으니 확률 줄이 없다</b>(설계 2026-09-29 조각1 §4.5 · §6). 옛 리포트는 2단계의 망이 패턴마다 낸 맞을 확률과 좁힌 명부를 적었다 — 그 망은
/// 걷었다. 회피 횟수는 <b>이 판</b>의 것이다: 옛 리포트는 망이 읽은 앞 시도의 기록을 셌지만, 지금 고르기는 기록을 안 읽으므로 "무엇을 했나" 를 이
/// 판에서 보여 주는 것이 사실에 맞다. 조각 4 가 "보스가 무엇을 읽고 무엇을 골랐나" 를 여기에 더한다.
/// </para>
/// </summary>
public static class PickReport
{
    /// <summary>회피 수단의 이름과 같은 수일 때의 순서 — 몸을 쓰는 수단(대시 · 점프 · 패리 · 가드)이 앞, 자리와 무대응이 뒤다.</summary>
    private static readonly (DodgeVerb Verb, string Name)[] _verbs =
    [
        (DodgeVerb.Dash, "대시"),
        (DodgeVerb.Jump, "점프"),
        (DodgeVerb.Parry, "패리"),
        (DodgeVerb.Guard, "가드"),
        (DodgeVerb.Spacing, "거리"),
        (DodgeVerb.None, "무대응"),
    ];

    /// <summary>리포트의 줄들 — 머리 · 이 판의 회피 · 명부 순서로 패턴마다 한 줄.</summary>
    /// <param name="pickerId">이 판을 세운 고르기 — 대본(<c>script</c>)으로 선 판은 그렇다고 적는다.</param>
    /// <param name="roster">이 판의 명부 — 줄의 순서다.</param>
    /// <param name="events">이 판의 회피 관측(<c>BattleSim.Events</c>).</param>
    /// <param name="drawn">이 판에 선 패턴들(<c>BattleSim.Drawn</c>) — 플레이어가 본 횟수다.</param>
    /// <param name="instances">이 판의 사례들(<see cref="InstanceTracker"/>) — 맞은 횟수를 센다.</param>
    public static IReadOnlyList<string> Lines(
        string pickerId, IReadOnlyList<string> roster, IReadOnlyList<DodgeEvent> events, IReadOnlyList<string> drawn,
        IReadOnlyList<PatternInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(instances);

        var lines = new List<string>
        {
            pickerId == "script" ? "보스가 정해진 대본대로 골랐습니다" : "보스가 패턴을 무작위로 골랐습니다",
            Dodges(events),
        };
        foreach (string id in roster)
        {
            lines.Add($"{id} — {Seen(id, drawn, instances)}");
        }

        return lines;
    }

    /// <summary>수단별 횟수, 많은 순 — 0 인 수단은 뺀다.</summary>
    private static string Dodges(IReadOnlyList<DodgeEvent> events)
    {
        if (events.Count == 0)
        {
            return "이 판의 회피가 없습니다";
        }

        var counts = _verbs
            .Select((v, order) => (v.Name, Count: events.Count(e => e.Verb == v.Verb), order))
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.order);
        return $"이 판의 회피 {events.Count}건: " + string.Join(" · ", counts.Select(c => $"{c.Name} {c.Count}"));
    }

    private static string Seen(string id, IReadOnlyList<string> drawn, IReadOnlyList<PatternInstance> instances)
    {
        int shown = drawn.Count(d => string.Equals(d, id, StringComparison.Ordinal));
        if (shown == 0)
        {
            return "안 나옴";
        }

        int hits = instances.Count(p => p.Hit && string.Equals(p.PatternId, id, StringComparison.Ordinal));
        return $"{shown}번 나옴 · {hits}번 맞음";
    }
}
