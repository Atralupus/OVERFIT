using System;
using System.Collections.Generic;
using System.Linq;

namespace Overfit.Battle.Rules;

/// <summary>되살린 판이 기록과 같은가 (설계 2026-09-29 조각1 §4.4) — 데모가 로그의 레벨로 옮긴다.</summary>
public enum ReplayVerdict
{
    /// <summary>같다 — 계획 전부 · 틱 수 · 결과 · 관측이. <c>[I] replay_match</c>.</summary>
    Match,

    /// <summary>다르고 데이터의 지문은 같다 — 결정론이 깨졌다. <c>[E] replay_mismatch</c>.</summary>
    Mismatch,

    /// <summary>다르고 지문도 다르다 — 데이터가 바뀌어 다른 판일 수 있다. <c>[W] replay_data_changed</c>.</summary>
    DataChanged,

    /// <summary>줄에 입력이 없다 — 5/8 까지의 게임이 남긴 줄이라 판을 되살릴 수 없다. <c>[E] replay_no_inputs</c>.</summary>
    NoInputs,
}

/// <summary>
/// 되살리기 (설계 2026-09-29 조각1 §4.4) — 시도 기록 한 줄의 입력을 봇 대신 틱마다 넣어 판 <b>전체</b>를 다시 세우고 기록과 견준다. 판을 세우는
/// 재료(그 줄의 시드 · 같은 런의 앞 기록 · 대본)는 부르는 쪽(<c>BattleDemo</c>)이 게임과 같은 자리(<see cref="StageRoster.Setup"/>)에서 세운다 —
/// 여기는 입력을 넣고 견줄 뿐이다.
///
/// <para>
/// 전에는(#112 ~ 5/8) 고르기만 다시 세우고 봇이 싸워 "뽑힌 순서의 앞머리" 만 견줬다 — 판 안의 일이 고르기에 들기 시작하면(조각 4 의 망이
/// <see cref="PlanRequest"/> 의 관측을 읽는다) 봇의 판은 다른 계획을 부르니 앞머리조차 못 견준다. 입력이 있으면 판이 통째로 선다.
/// </para>
/// </summary>
public static class Replay
{
    /// <summary>
    /// 저장한 입력으로 판을 민다 — 판이 끝나거나(<see cref="BattleSim.Result"/>) 입력이 다할 때까지. 끝까지 간 기록의 입력은 판이 끝나는 틱에 꼭
    /// 다한다(게임이 틱마다 하나씩 모았다).
    /// </summary>
    /// <exception cref="ArgumentException">입력에 틀린 칸이 있다(<see cref="InputTape.Problem"/>) — 한 틱도 밀기 전에 멈춘다.</exception>
    public static BattleSim Run(BattleSetup setup, IReadOnlyList<int[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(setup);
        IEnumerable<InputFrame> frames = InputTape.Play(inputs);
        var sim = new BattleSim(setup);
        foreach (InputFrame input in frames)
        {
            if (sim.Tick(input) is not null)
            {
                break;
            }
        }

        return sim;
    }

    /// <summary>
    /// 대본으로 선 시도의 대본 — 기록된 계획이 곧 대본이다(§4.4). 게임의 대본은 돌며 되풀이되지만 기록은 그 판에서 고른 계획을 <b>고른 만큼</b>
    /// 싣는다: 같은 판이면 같은 번호에서 같은 계획을 고르므로 기록 한 바퀴로 판 전체가 선다. 캔슬 지점은 시각(초)으로 적혀 있어 지금 정의의 칸으로
    /// 되찾는다.
    /// </summary>
    /// <param name="plans">기록의 계획들(<see cref="AttemptEntry.Plans"/>).</param>
    /// <param name="roster">그 단계의 명부 — 대본은 명부 안에서만 선다.</param>
    /// <param name="patterns">지금의 동작 정의.</param>
    /// <param name="problem">
    /// 못 세운 까닭 — 계획이 없거나, 기록의 동작 · 지점이 지금 정의에 없거나, 대본을 세우는 자리(<see cref="ScriptPlanPicker"/>)가 거절한 것.
    /// 데이터가 바뀌었을 수 있어 예외로 멈추지 않고 돌려준다 — 그 판정(<c>[W]</c> · <c>[E]</c>)은 지문을 든 쪽이 한다.
    /// </param>
    /// <returns>대본. 못 세우면 null.</returns>
    public static IReadOnlyList<ScriptPlan>? Script(
        IReadOnlyList<PlanEntry> plans, IReadOnlyList<string> roster, IReadOnlyDictionary<string, PatternDef> patterns, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(patterns);
        if (plans.Count == 0)
        {
            problem = "기록에 계획이 없다";
            return null;
        }

        var script = new ScriptPlan[plans.Count];
        for (int i = 0; i < plans.Count; i++)
        {
            PlanEntry plan = plans[i];
            int? point = null;
            if (plan.Cancel is double t)
            {
                point = PointAt(patterns, plan.Move, t);
                if (point is null)
                {
                    problem = $"{i}번 계획 — {plan.Move} 에 {t}초 캔슬 지점이 없다";
                    return null;
                }
            }

            script[i] = new ScriptPlan(plan.Rest, plan.Move, point, plan.Next);
        }

        // 명부 밖 · 정의가 없는 동작 · 첫 동작과 같은 잇는 동작 — 대본을 세우는 자리가 보는 것을 그대로 본다. 거기서 거절하면 판을 세우는 자리가
        // [E] script_rejected 를 내는데, 되살리기에서는 데이터가 바뀐 것일 수 있어 까닭으로 돌려준다.
        try
        {
            _ = new ScriptPlanPicker(roster, patterns, script);
        }
        catch (ArgumentException e)
        {
            problem = e.Message;
            return null;
        }

        problem = null;
        return script;
    }

    /// <summary>
    /// 되살린 판이 기록과 같은가 — 틱 수 · 결과 · 입력의 길이 · 계획 전부 · 관측 전부. 다르면 지문(<paramref name="dataSha256"/> — 지금 데이터의
    /// <c>DataDigest</c>)이 까닭을 가른다: 같으면 결정론이 깨졌고, 다르면 데이터가 바뀌었을 수 있다. 입력이 없는 줄은 판이 우연히 같아 보여도
    /// <see cref="ReplayVerdict.NoInputs"/> 다 — 입력 없이 선 판은 그 줄의 판이 아니다.
    /// </summary>
    public static ReplayVerdict Verdict(AttemptEntry logged, BattleSim sim, string dataSha256)
    {
        ArgumentNullException.ThrowIfNull(logged);
        ArgumentNullException.ThrowIfNull(sim);
        if (logged.Inputs is not { } inputs)
        {
            return ReplayVerdict.NoInputs;
        }

        bool same = sim.Ticks == logged.Ticks
            && TapeTicks(inputs) == sim.Ticks
            && sim.Result == logged.Record.Outcome
            && sim.PlanEntries.SequenceEqual(logged.Plans)
            && sim.Events.SequenceEqual(logged.Record.Events);
        if (same)
        {
            return ReplayVerdict.Match;
        }

        return string.Equals(logged.DataSha256, dataSha256, StringComparison.Ordinal) ? ReplayVerdict.Mismatch : ReplayVerdict.DataChanged;
    }

    /// <summary>
    /// 기록과 되살린 판을 나란히 — 로그의 <c>기록/지금</c> 값들. 계획 · 관측은 처음 갈린 칸(<c>plan_diff</c> · <c>event_diff</c>)까지 적는다: 몇 번째
    /// 계획에서 갈렸는지가 어디부터 볼지를 말한다.
    /// </summary>
    public static string Compare(AttemptEntry logged, BattleSim sim)
    {
        ArgumentNullException.ThrowIfNull(logged);
        ArgumentNullException.ThrowIfNull(sim);
        IReadOnlyList<PlanEntry> plans = sim.PlanEntries;
        return $"ticks={logged.Ticks}/{sim.Ticks} outcome={logged.Record.Outcome}/{sim.Result?.ToString() ?? "none"}"
            + $" plans={logged.Plans.Count}/{plans.Count}{Diff("plan_diff", logged.Plans, plans)}"
            + $" events={logged.Record.Events.Count}/{sim.Events.Count}{Diff("event_diff", logged.Record.Events, sim.Events)}";
    }

    private static int TapeTicks(IReadOnlyList<int[]> inputs)
    {
        int ticks = 0;
        foreach (int[] run in inputs)
        {
            ticks += run[1];
        }

        return ticks;
    }

    /// <summary>두 목록이 처음 갈린 칸 — 같으면 빈 글. 한쪽이 다른 쪽의 앞머리면 짧은 쪽의 길이다.</summary>
    private static string Diff<T>(string key, IReadOnlyList<T> logged, IReadOnlyList<T> now)
    {
        int common = Math.Min(logged.Count, now.Count);
        for (int i = 0; i < common; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(logged[i], now[i]))
            {
                return $" {key}={i}";
            }
        }

        return logged.Count == now.Count ? "" : $" {key}={common}";
    }

    /// <summary><paramref name="move"/> 의 캔슬 지점 중 시각이 <paramref name="t"/> 인 칸 — 기록은 데이터의 값을 그대로 싣고 비트까지 되읽으므로 같음으로 찾는다.</summary>
    private static int? PointAt(IReadOnlyDictionary<string, PatternDef> patterns, string move, double t)
    {
        if (!patterns.TryGetValue(move, out PatternDef? def) || def.CancelPoints is not { } points)
        {
            return null;
        }

        for (int k = 0; k < points.Count; k++)
        {
            if (points[k].T.Equals(t))
            {
                return k;
            }
        }

        return null;
    }
}
