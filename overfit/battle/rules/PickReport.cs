using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 2단계 결과 화면의 패턴 리포트 (#122) — 이번 시도의 패턴을 어떻게 골랐고, 무엇이 몇 번 나와 몇 번 맞았나. 씬(<c>Battle</c>)은 줄을 받아 그리기만 한다.
///
/// <para>
/// <b>"어떤 회피 때문에" 는 따로 추정하지 않는다</b>(유저 결정 · #122). 망은 패턴마다 맞을 확률만 내므로 리포트도 "맞을 확률 → 고른 패턴" 으로만 말한다.
/// 회피 횟수는 망의 입력이 된 기록을 그대로 센 사실이다 — 둘을 나란히 두되 인과로 잇지 않는다. 가드와 패리는 망이 설계 의도(README 의 표)와 다른
/// 패턴에 이었다(설계 2026-09-28 §7.1) — 의도를 표로 박아 이유로 적으면 리포트가 망과 다른 말을 한다.
/// </para>
///
/// <para>
/// 확률은 로짓의 시그모이드다. <c>Math.Exp</c> 를 쓰지만 결정 경로가 아니라 보이는 글이라 사칙연산 규칙(설계 §8)의 밖이다 — 고르기는 로짓을 그대로 견준다.
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

    /// <summary>
    /// 리포트의 줄들. 동전이 없는 단계(<see cref="StageSetup.Arm"/> 이 null — 1단계)는 빈 목록이다.
    /// </summary>
    /// <param name="setup">이 시도를 세운 것 — 갈래 · 결정 · 명부.</param>
    /// <param name="network">망과 고르기의 수치 — 기저율과 동전의 몫 · 겨냥 수를 읽는다.</param>
    /// <param name="prior">이 시도 <b>전까지</b>의 기록 — 고르기가 읽은 그 기록이다. 끝난 이 시도를 넣으면 고른 뒤의 회피까지 센다.</param>
    /// <param name="drawn">이 판에 선 패턴들(<c>BattleSim.Drawn</c>) — 플레이어가 본 횟수다.</param>
    /// <param name="instances">이 판의 사례들(<see cref="InstanceTracker"/>) — 맞은 횟수를 센다.</param>
    public static IReadOnlyList<string> Lines(
        StageSetup setup, NetworkContext network, IReadOnlyList<AttemptRecord> prior, IReadOnlyList<string> drawn, IReadOnlyList<PatternInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(instances);
        if (setup.Arm is null)
        {
            return [];
        }

        IReadOnlyList<string> roster = setup.PatternIds;
        PickDecision? decision = setup.Decision;
        var lines = new List<string> { Headline(setup.Arm, decision, roster.Count, network.Knobs), Dodges(prior) };
        PickDecision? narrowed = decision?.Mode == PickDecision.ModeNarrowed ? decision : null;
        if (narrowed is not null)
        {
            lines.Add($"겨냥: 평균보다 확실히 더 맞을 패턴(최대 {network.Knobs.MaxTargeted}개) · 숨통: 겨냥 밖에서 가장 덜 맞을 패턴");
        }

        for (int i = 0; i < roster.Count; i++)
        {
            string id = roster[i];
            string seen = Seen(id, drawn, instances);

            // 망을 안 돌린 시도(무작위 갈래 · 근거가 얇다)는 확률이 없다 — 횟수만 적는다.
            if (decision?.Logits is not { } logits)
            {
                lines.Add($"{id} — {seen}");
                continue;
            }

            string probability = $"맞을 확률 {Percent(logits[i])}% (평균 {Percent(network.Net.Baseline[i])}%)";
            if (narrowed is null)
            {
                lines.Add($"{id} — {probability} · {seen}");
            }
            else if (!narrowed.Narrowed.Contains(i))
            {
                lines.Add($"{id} — {probability} → 안 씀");
            }
            else
            {
                lines.Add($"{id} — {probability} → {(narrowed.Breathing == i ? "숨통" : "겨냥")} · {seen}");
            }
        }

        return lines;
    }

    private static string Headline(string arm, PickDecision? decision, int patterns, PickerBalance knobs)
    {
        if (arm != NetworkPicker.Id || decision is null)
        {
            return $"이번 시도는 무작위로 골랐습니다 (비교용 {100 - knobs.NetworkSharePercent}%)";
        }

        return decision.Reason switch
        {
            PickDecision.ReasonThin => $"회피 기록이 {decision.Samples}건뿐이라({knobs.MinSamples}건 미만) {patterns}개 패턴을 모두 썼습니다",
            PickDecision.ReasonNoHabit => $"평균보다 두드러지게 맞을 패턴이 없어 {patterns}개 패턴을 모두 썼습니다",
            _ => "보스가 회피 기록을 읽고 패턴을 골랐습니다",
        };
    }

    /// <summary>수단별 횟수, 많은 순 — 0 인 수단은 뺀다.</summary>
    private static string Dodges(IReadOnlyList<AttemptRecord> prior)
    {
        int total = prior.Sum(r => r.Events.Count);
        if (total == 0)
        {
            return "이 시도 전까지의 회피 기록이 없습니다";
        }

        var counts = _verbs
            .Select((v, order) => (v.Name, Count: prior.Sum(r => r.Events.Count(e => e.Verb == v.Verb)), order))
            .Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.order);
        return $"이 시도 전까지의 회피 {total}건: " + string.Join(" · ", counts.Select(c => $"{c.Name} {c.Count}"));
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

    private static string Percent(double logit) =>
        Math.Round(100 / (1 + Math.Exp(-logit)), MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture);
}
