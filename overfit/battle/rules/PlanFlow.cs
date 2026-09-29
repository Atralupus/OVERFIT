using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 계획의 흐름 (설계 2026-09-29 조각1 §3.2 · §3.3) — 쉬기 → 첫 동작 → [캔슬 지점에서 끊고] 잇는 동작 → 다음 계획. <see cref="BattleSim"/> 이
/// 계획이 끝날 때(<see cref="Choose"/>) · 쉬는 틱마다(<see cref="RestTick"/>) · 러너를 밀기 전마다(<see cref="CancelAt"/>) 묻고, 여기는 무엇을
/// 언제 세울지만 말한다. 동작을 세우고 걷는 것 · 돌아서는 것은 판이다.
///
/// <para>
/// <b>계획 하나에 캔슬 한 번</b>이고 잇는 동작은 지점이 있어도 끝까지 간다(§3.2). 탈진하면 계획이 끝난다 — 판이 <see cref="Choose"/> 를 불러 남은
/// 캔슬 · 잇는 동작을 버린다.
/// </para>
///
/// <para>
/// <b>틀린 계획은 버린다</b> (§4.1) — 쉬기가 1틱 아래 · 명부 밖 동작 · 정의가 없는 동작 · 지점과 잇는 동작 중 하나만 · 없는 지점 · 첫 동작과 같은
/// 잇는 동작. <c>[E] plan_invalid</c> 를 한 줄 남기고, 계획 없이 가장 짧은 쉬기를 센 뒤 다음 번호로 다시 고른다 — 매 틱 쏟지 않는다(옛
/// <c>pick_out_of_range</c> 와 같은 대우). 예외로 두면 엔진의 ERROR 블록으로만 나와 어느 고르기가 무엇을 냈는지가 안 남는다. 판은 끝까지 간다.
/// </para>
/// </summary>
public sealed class PlanFlow
{
    private readonly IPlanPicker _picker;
    private readonly IReadOnlyList<string> _roster;
    private readonly IReadOnlyDictionary<string, PatternDef> _patterns;
    private readonly int _shortestRest;

    /// <summary>계획 번호 → 고르기에 넘길 것. 판이 그 순간의 틱 · 관측 · 자리를 채운다.</summary>
    private readonly Func<int, PlanRequest> _request;

    private readonly List<BossPlan> _plans = new();

    /// <summary>지금 계획 — 버린 계획 뒤의 쉬기 동안은 null 이다.</summary>
    private BossPlan? _plan;

    private int _restLeft;
    private int _number;

    /// <summary>지금 도는 것이 첫 동작이고 계획의 캔슬이 아직 안 쓰였나.</summary>
    private bool _cancelPending;

    /// <param name="picker">계획 고르기.</param>
    /// <param name="roster">명부 — 계획의 칸이 이 인덱스다.</param>
    /// <param name="patterns">동작 정의 — 캔슬 지점을 읽는다.</param>
    /// <param name="restTicks">쉬는 길이들(틱) — 가장 짧은 것이 버린 계획 뒤의 쉬기다.</param>
    /// <param name="request">계획 번호 → 고르기에 넘길 것.</param>
    public PlanFlow(
        IPlanPicker picker, IReadOnlyList<string> roster, IReadOnlyDictionary<string, PatternDef> patterns, IReadOnlyList<int> restTicks,
        Func<int, PlanRequest> request)
    {
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(restTicks);
        ArgumentNullException.ThrowIfNull(request);
        if (restTicks.Count == 0)
        {
            throw new ArgumentException("쉬는 길이가 하나도 없다 — bosses.json 의 rest_seconds 가 비었다", nameof(restTicks));
        }

        _picker = picker;
        _roster = roster;
        _patterns = patterns;
        _shortestRest = Math.Max(1, restTicks.Min());
        _request = request;
    }

    /// <summary>고른 계획들, 고른 순서로 — 버린 계획은 빠진다. 시도 기록이 싣는다(6/8).</summary>
    public IReadOnlyList<BossPlan> Plans => _plans;

    /// <summary>
    /// 지금 동작이 끊길 러너 틱 — 첫 동작이 돌고 계획에 캔슬이 남았을 때만. 러너의 시계가 이 틱에 닿기 <b>전에</b> 판이 끊는다: 그 틱의 단계에는
    /// 안 든다(§3.2).
    /// </summary>
    public int? CancelAt => _cancelPending && _plan is { CancelPoint: int k } plan
        ? BattleSim.TicksFor(_patterns[_roster[plan.Move]].CancelPoints![k].T)
        : null;

    /// <summary>
    /// 다음 계획을 고른다 — 판이 설 때 · 계획이 끝날 때(끝까지 돌았든 탈진으로 끊겼든). 쉬기는 여기서부터 센다(탈진 동안은 판이 안 센다).
    /// </summary>
    public void Choose()
    {
        int number = _number++;
        PlanRequest request = _request(number);
        BossPlan plan = _picker.Next(request);
        _cancelPending = false;
        if (Problem(plan) is { } problem)
        {
            _plan = null;
            _restLeft = _shortestRest;
            Log.Error("boss", $"plan_invalid n={number} {problem} tick={request.Tick}");
            return;
        }

        _plan = plan;
        _plans.Add(plan);
        _restLeft = plan.RestTicks;
        Log.Debug("boss", () => $"plan n={number} rest={plan.RestTicks * BattleSim.Dt:0.##} move={_roster[plan.Move]}"
            + $" cancel={(plan.CancelPoint is int k ? $"{_patterns[_roster[plan.Move]].CancelPoints![k].T:0.##}" : "-")}"
            + $" next={(plan.Next is int next ? _roster[next] : "-")} tick={request.Tick}");
    }

    /// <summary>
    /// 쉬는 틱 하나. 쉬기가 끝나는 틱이면 첫 동작의 칸 — 판이 이 틱에 세운다. 버린 계획 뒤의 쉬기가 끝나면 다음 번호로 다시 고르고 null 이다.
    /// </summary>
    public int? RestTick()
    {
        if (--_restLeft > 0)
        {
            return null;
        }

        if (_plan is not { } plan)
        {
            Choose();
            return null;
        }

        _cancelPending = plan.CancelPoint is not null;
        return plan.Move;
    }

    /// <summary>끊는다 — 잇는 동작의 칸. 계획의 캔슬은 여기서 쓰인다(잇는 동작은 끝까지 간다).</summary>
    /// <exception cref="InvalidOperationException">남은 캔슬이 없다 — <see cref="CancelAt"/> 이 null 인데 불렀다.</exception>
    public int Cancel()
    {
        if (!_cancelPending || _plan is not { Next: int next })
        {
            throw new InvalidOperationException("끊을 캔슬이 없다 — CancelAt 이 null 이다");
        }

        _cancelPending = false;
        return next;
    }

    /// <summary>계획의 틀린 곳 — 로그의 <c>reason=</c> 과 그 값들. 없으면 null.</summary>
    private string? Problem(BossPlan plan)
    {
        if (plan.RestTicks < 1)
        {
            return $"reason=rest rest={plan.RestTicks}";
        }

        if ((uint)plan.Move >= (uint)_roster.Count)
        {
            return $"reason=move_out_of_range move={plan.Move} roster={_roster.Count}";
        }

        if (!_patterns.TryGetValue(_roster[plan.Move], out PatternDef? def))
        {
            return $"reason=pattern_missing id={_roster[plan.Move]}";
        }

        if ((plan.CancelPoint is null) != (plan.Next is null))
        {
            return $"reason=half_cancel cancel={plan.CancelPoint?.ToString(CultureInfo.InvariantCulture) ?? "-"}"
                + $" next={plan.Next?.ToString(CultureInfo.InvariantCulture) ?? "-"}";
        }

        if (plan is not { CancelPoint: int k, Next: int next })
        {
            return null;
        }

        int points = def.CancelPoints?.Count ?? 0;
        if (k < 0 || k >= points)
        {
            return $"reason=point_missing id={_roster[plan.Move]} point={k} points={points}";
        }

        if ((uint)next >= (uint)_roster.Count)
        {
            return $"reason=next_out_of_range next={next} roster={_roster.Count}";
        }

        if (next == plan.Move)
        {
            return $"reason=next_same id={_roster[next]}";
        }

        return _patterns.ContainsKey(_roster[next]) ? null : $"reason=pattern_missing id={_roster[next]}";
    }
}
