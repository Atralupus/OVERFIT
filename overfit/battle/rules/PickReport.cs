using System;
using System.Collections.Generic;
using System.Linq;

namespace Overfit.Battle.Rules;

/// <summary>
/// 결과 화면의 리포트 (#122 · 설계 2026-09-29 조각1 §4.5) — 보스가 이 판의 계획을 어떻게 골랐고(몇 개 · 몇 번 끊었나), 파이터가 어떻게 피하고
/// 폭탄이 어떻게 됐고(설계 2026-09-30 조각2 §4), 무엇이 몇 번 나와 몇 번 맞았고, 무엇을 무엇으로 끊었나. 씬(<c>Battle</c>)은 줄을 받아 그리기만 한다.
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

    /// <summary>리포트의 줄들 — 머리 · 이 판의 회피 · 폭탄 · 명부 순서로 동작마다 한 줄 · 끊은 짝마다 한 줄.</summary>
    /// <param name="pickerId">이 판을 세운 고르기 — 대본(<c>script</c>)으로 선 판은 그렇다고 적는다.</param>
    /// <param name="roster">이 판의 명부 — 줄의 순서다.</param>
    /// <param name="plans">이 판에서 고른 계획의 수(<c>BattleSim.Plans</c>) — 끝나지 않은 마지막 계획도 든다.</param>
    /// <param name="cancels">이 판에서 실제로 끊은 캔슬들(<c>BattleSim.Cancels</c>) — 탈진으로 못 쓴 캔슬은 안 든다.</param>
    /// <param name="events">이 판의 회피 관측(<c>BattleSim.Events</c>).</param>
    /// <param name="bombs">이 판의 던지기들(<c>BattleSim.BombRecords</c>).</param>
    /// <param name="drawn">이 판에 선 동작들(<c>BattleSim.Drawn</c>) — 잇는 동작도 든다. 플레이어가 본 횟수다.</param>
    /// <param name="instances">이 판의 사례들(<see cref="InstanceTracker"/>) — 맞은 횟수를 센다.</param>
    public static IReadOnlyList<string> Lines(
        string pickerId, IReadOnlyList<string> roster, int plans, IReadOnlyList<(string From, string To)> cancels, IReadOnlyList<DodgeEvent> events,
        IReadOnlyList<BombRecord> bombs, IReadOnlyList<string> drawn, IReadOnlyList<PatternInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(cancels);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(bombs);
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(instances);

        string how = pickerId == "script" ? "보스가 정해진 대본대로 골랐습니다" : "보스가 계획을 무작위로 골랐습니다";
        var lines = new List<string>
        {
            $"{how} — 계획 {plans}개 · 캔슬 {cancels.Count}번",
            Dodges(events),
            Bombs(bombs),
        };
        foreach (string id in roster)
        {
            lines.Add($"{id} — {Seen(id, drawn, instances)}");
        }

        lines.AddRange(Cancels(roster, cancels));
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

    /// <summary>
    /// 던진 수와 결과마다의 수 (설계 2026-09-30 조각2 §4) — 보스가 끊으려 했지만 늦어 떨어진 것도 맞힘이다. 판이 먼저 끝난 던지기가 있으면 그 수를
    /// 덧붙인다 — 안 붙이면 셋의 합이 던진 수와 안 맞는다.
    /// </summary>
    private static string Bombs(IReadOnlyList<BombRecord> bombs)
    {
        if (bombs.Count == 0)
        {
            return "폭탄을 안 던졌습니다";
        }

        int ended = bombs.Count(b => b.Outcome == BombOutcome.End);
        return $"폭탄 {bombs.Count}개 — 맞힘 {bombs.Count(b => b.Outcome == BombOutcome.Landed)}"
            + $" · 보스가 끊음 {bombs.Count(b => b.Outcome == BombOutcome.Cut)}"
            + $" · 다른 공격에 잃음 {bombs.Count(b => b.Outcome == BombOutcome.Hit)}"
            + (ended > 0 ? $" · 판이 먼저 끝남 {ended}" : "");
    }

    /// <summary>
    /// 끊은 짝마다 한 줄 — 많은 순, 같으면 명부 순(끊은 동작 · 이은 동작). 명부 순으로 가르는 까닭은 같은 판을 두 번 봐도 줄이 같은 자리에
    /// 서야 해서다(나온 순서로 두면 수가 같은 두 짝이 판마다 자리를 바꾼다).
    /// </summary>
    private static IEnumerable<string> Cancels(IReadOnlyList<string> roster, IReadOnlyList<(string From, string To)> cancels) =>
        cancels
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => Order(roster, g.Key.From))
            .ThenBy(g => Order(roster, g.Key.To))
            .Select(g => $"{g.Key.From} → {g.Key.To} — {g.Count()}번");

    /// <summary>명부의 칸 — 명부 밖이면 맨 뒤.</summary>
    private static int Order(IReadOnlyList<string> roster, string id)
    {
        for (int k = 0; k < roster.Count; k++)
        {
            if (string.Equals(roster[k], id, StringComparison.Ordinal))
            {
                return k;
            }
        }

        return int.MaxValue;
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
