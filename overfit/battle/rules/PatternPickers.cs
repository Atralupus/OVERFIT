using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 고르기를 세우는 재료 (설계 2026-09-29 조각1 §4.1) — 판을 세울 때 <b>한 번</b>이다. 판 안의 일은 세울 때가 아니라 계획마다
/// <see cref="PlanRequest"/> 로 든다.
/// </summary>
/// <param name="Roster">명부 — <c>stages.json</c> 의 <c>patterns</c>. 계획의 동작 칸이 이 인덱스다.</param>
/// <param name="Patterns">
/// 동작 정의를 <b>통째로</b> 받는다 (§3.6) — 캔슬 지점을 여기서 읽고, 조각 4 가 동작에 <c>covers</c> 칸을 더해도 고르기의 모양이 그대로다.
/// </param>
/// <param name="RestTicks">쉬는 길이들(틱) — <c>bosses.json</c> 의 <c>rest_seconds</c> 를 <see cref="BattleSim.RestTicks"/> 로 바꾼 것.</param>
/// <param name="Knobs">고르기의 수치 — <c>balance.json</c> 의 <c>picker</c>.</param>
/// <param name="History">그때까지 끝난 시도들. 지금의 두 고르기는 안 읽는다 — 옛 망(태그 <c>v0.9.2</c>)이 읽던 자리이고, 조각 3 의 기억이 다시 채운다.</param>
/// <param name="Seed">시도 시드.</param>
/// <param name="Script">대본 — 계획의 목록. <c>script</c> 고르기만 읽는다. 대본을 넘긴 전투(<c>Game</c> 의 대본 칸)가 아니면 null 이다.</param>
public sealed record PickerInputs(
    IReadOnlyList<string> Roster,
    IReadOnlyDictionary<string, PatternDef> Patterns,
    IReadOnlyList<int> RestTicks,
    PickerBalance Knobs,
    IReadOnlyList<AttemptRecord> History,
    ulong Seed,
    IReadOnlyList<ScriptPlan>? Script = null);

/// <summary>
/// 무작위 — 계획의 결정마다 제 스트림에서 고르게 뽑는다 (설계 2026-09-29 조각1 §3.5). 계획 번호가 <c>k1</c> 이라 부른 순서와 무관하게 같은
/// 번호는 같은 계획이고, 한 결정의 칸 수가 바뀌어도(명부에 동작을 더해도) 다른 결정의 좌표가 안 밀린다.
///
/// <list type="table">
/// <item><term>첫 동작</term><description>명부에서 고르게 — <c>RollInt(시드, PatternPick, 명부 수, k1)</c>. 옛 uniform 과 같은 좌표다.</description></item>
/// <item><term>끊나</term><description>첫 동작에 지점이 있으면 <c>RollInt(시드, PlanCancel, 100, k1) &lt; cancel_percent</c>.</description></item>
/// <item><term>어디서</term><description>그 동작의 지점에서 고르게 — <c>RollInt(시드, PlanPoint, 지점 수, k1)</c>.</description></item>
/// <item><term>무엇으로</term><description>첫 동작을 뺀 명부에서 고르게 — <c>RollInt(시드, PlanNext, 명부 수 − 1, k1)</c> 이 첫 동작의 칸 이상이면 한 칸 민다.</description></item>
/// <item><term>쉬는 길이</term><description><c>rest_seconds</c> 에서 고르게 — <c>RollInt(시드, PlanRest, 쉬기 수, k1)</c>.</description></item>
/// </list>
///
/// <para>
/// 예측 망이 들어오면(조각 4) 이것이 <b>대조군</b>이다 — 같은 사람에게 무작위 보스와 망 보스를 붙여 비교하는 것 말고 망이 일하는지 증명할 길이 없다.
/// </para>
/// </summary>
public sealed class UniformPlanPicker : IPlanPicker
{
    private readonly ulong _seed;
    private readonly int _count;

    /// <summary>명부의 칸마다 캔슬 지점의 수 — 세울 때 한 번 센다. 정의가 없는 동작은 0 이다(판이 그 계획을 버린다).</summary>
    private readonly int[] _points;

    private readonly int[] _rest;
    private readonly int _cancelPercent;

    public UniformPlanPicker(PickerInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputs.Roster.Count, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(inputs.RestTicks.Count, 1);
        _seed = inputs.Seed;
        _count = inputs.Roster.Count;
        _points = new int[_count];
        for (int k = 0; k < _count; k++)
        {
            _points[k] = inputs.Patterns.TryGetValue(inputs.Roster[k], out PatternDef? def) ? def.CancelPoints?.Count ?? 0 : 0;
        }

        _rest = [.. inputs.RestTicks];
        _cancelPercent = inputs.Knobs.CancelPercent;
    }

    public BossPlan Next(PlanRequest request)
    {
        int k = request.Number;
        int move = Det.RollInt(_seed, Det.Domain.PatternPick, _count, k1: k);
        int rest = _rest[Det.RollInt(_seed, Det.Domain.PlanRest, _rest.Length, k1: k)];

        // 끊으려면 지점이 있고 이을 다른 동작이 있어야 한다. 주사위는 그때만 굴린다 — 좌표가 번호에 매여 있어 안 굴린 번호가 다음 계획을 안 민다.
        if (_points[move] == 0 || _count < 2 || Det.RollInt(_seed, Det.Domain.PlanCancel, 100, k1: k) >= _cancelPercent)
        {
            return new BossPlan(rest, move, null, null);
        }

        int point = Det.RollInt(_seed, Det.Domain.PlanPoint, _points[move], k1: k);
        int next = Det.RollInt(_seed, Det.Domain.PlanNext, _count - 1, k1: k);
        return new BossPlan(rest, move, point, next >= move ? next + 1 : next);
    }
}

/// <summary>
/// 대본 — 정한 계획들(<see cref="PickerInputs.Script"/>)을 차례로 돌고, 끝나면 처음부터 다시 돈다 (#78 · 설계 2026-09-29 조각1 §4.2). GIF 도구 ·
/// 스크린샷 · 씬 순회가 판을 고정하는 데 쓴다 — 무엇이 올지 알아야 "그 동작이 왔을 때 그 사람이 어떻게 되는가" 를 찍는다. <b>데이터의 단계에는
/// 안 쓴다</b>(<c>StageRosterTests</c> 가 막는다): 전투에 닿는 길은 <c>Game</c> 의 다음 전투 한 칸뿐이다.
///
/// <para>
/// 틀린 칸은 <b>세울 때</b> 거절한다 — 명부 밖 동작 · 없는 지점 · 첫 동작과 같은 잇는 동작 · 지점과 잇는 동작 중 하나만 적은 칸 · 0 이하의 쉬기.
/// 틀린 것을 전부 싣는다. 대본은 사람이 손으로 쓰는 것이라 틀리면 그 판을 세우는 자리에서 바로 멈춰야 한다 — 이 예외를
/// <c>StageRoster.Setup</c> 이 받아 <c>[E] script_rejected</c> 로 바꾸고 판을 세우지 않는다. uniform 과 같이 상태가 없다 — 계획 번호로 조회만 한다.
/// </para>
/// </summary>
public sealed class ScriptPlanPicker : IPlanPicker
{
    private readonly BossPlan[] _plans;

    public ScriptPlanPicker(IReadOnlyList<string> roster, IReadOnlyDictionary<string, PatternDef> patterns, IReadOnlyList<ScriptPlan> script)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(script);
        if (script.Count == 0)
        {
            throw new ArgumentException("대본이 비었다 — 고를 계획이 없다", nameof(script));
        }

        _plans = new BossPlan[script.Count];
        var problems = new List<string>();
        for (int i = 0; i < script.Count; i++)
        {
            ScriptPlan plan = script[i];
            int move = IndexOf(roster, plan.Move);
            int next = plan.Next is { } id ? IndexOf(roster, id) : -1;
            if (Problem(plan, move, next, patterns) is { } problem)
            {
                problems.Add($"{i}번 칸 — {problem}");
                continue;
            }

            _plans[i] = new BossPlan(BattleSim.TicksFor(plan.RestSeconds), move, plan.CancelPoint, next >= 0 ? next : null);
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException($"대본이 틀렸다 — {string.Join(" · ", problems)}", nameof(script));
        }
    }

    public BossPlan Next(PlanRequest request) => _plans[request.Number % _plans.Length];

    /// <summary>대본 한 칸의 틀린 곳 — 없으면 null. <paramref name="move"/> · <paramref name="next"/> 는 명부의 칸이고 없으면 −1 이다.</summary>
    private static string? Problem(ScriptPlan plan, int move, int next, IReadOnlyDictionary<string, PatternDef> patterns)
    {
        if (plan.RestSeconds <= 0)
        {
            return $"쉬기 {plan.RestSeconds}초";
        }

        if (move < 0 || !patterns.TryGetValue(plan.Move, out PatternDef? def))
        {
            return $"{plan.Move} 가 명부나 정의에 없다";
        }

        if ((plan.CancelPoint is null) != (plan.Next is null))
        {
            return "지점과 잇는 동작 중 하나만 적었다";
        }

        if (plan.CancelPoint is not int k)
        {
            return null;
        }

        if (next < 0 || !patterns.ContainsKey(plan.Next!))
        {
            return $"{plan.Next} 가 명부나 정의에 없다";
        }

        if (next == move)
        {
            return $"잇는 동작이 첫 동작({plan.Move})과 같다";
        }

        return k >= 0 && k < (def.CancelPoints?.Count ?? 0) ? null : $"{plan.Move} 에 캔슬 지점 {k} 이 없다";
    }

    private static int IndexOf(IReadOnlyList<string> roster, string id)
    {
        for (int k = 0; k < roster.Count; k++)
        {
            if (string.Equals(roster[k], id, StringComparison.Ordinal))
            {
                return k;
            }
        }

        return -1;
    }
}

/// <summary>
/// 고르기 등록표 — <c>stages.json</c> 의 <c>picker</c> id → 구현 (CLAUDE.md §2 · 설계 §4.4). 고르기를 하나 더할 때 이 표에
/// 한 줄을 더한다 — <c>BattleSim</c> 은 안 연다. <c>uniform</c>(계획마다 무작위 · 조각1 §3.5) · <c>script</c>(대본 · #78)가 있다. 옛 망
/// 고르기(<c>network</c> · #112)는 조각 1 에서 걷었다(설계 2026-09-29 조각1 §6) — 예측 망은 조각 4 에서 새로 들어온다.
/// </summary>
public static class PatternPickers
{
    private static readonly Dictionary<string, Func<PickerInputs, IPlanPicker>> _table = new(StringComparer.Ordinal)
    {
        ["uniform"] = inputs => new UniformPlanPicker(inputs),
        ["script"] = inputs => new ScriptPlanPicker(
            inputs.Roster, inputs.Patterns, inputs.Script ?? throw new ArgumentException("script 고르기에 대본이 없다", nameof(inputs))),
    };

    /// <summary>등록된 id 들 — 데이터 테스트가 <c>stages.json</c> 의 <c>picker</c> 를 여기와 대 본다.</summary>
    public static IReadOnlyCollection<string> Ids => _table.Keys;

    /// <summary>
    /// 고르기 하나를 세운다. 모르는 id 면 null — 부르는 쪽이 <c>[E]</c> 를 남긴다. 재료가 그 고르기에 안 맞으면(대본이 틀렸거나 · <c>script</c> 인데
    /// 대본이 없음) <see cref="ArgumentException"/> 을 던진다 — <c>StageRoster.Setup</c> 이 받아 <c>[E] script_rejected</c> 를 남기고 null 로 바꾼다.
    /// </summary>
    public static IPlanPicker? Create(string id, PickerInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return _table.TryGetValue(id, out Func<PickerInputs, IPlanPicker>? make) ? make(inputs) : null;
    }
}
