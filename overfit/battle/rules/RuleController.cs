using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 규칙 조종기 (설계 2026-10-01 조각2 §3.1) — 0.11 의 보스. 계획(쉬기 → [달리기] → 첫 동작 → [캔슬] → 잇는 동작)을 고르기(<see cref="IPlanPicker"/> — 무작위 ·
/// 대본)가 통째로 고르고, 이 조종기가 그 계획을 결정 지점의 칸으로 옮긴다. 옛 <c>PlanFlow</c> 의 일이다 — 고르는 틱 · 번호 · 동작이 서는 틱이 0.11 과
/// 같아야 한다(리플레이 골든 · 규칙 테스트가 본다).
///
/// <para>
/// <b>계획 하나에 캔슬 한 번</b>이고 잇는 동작은 지점이 있어도 끝까지 간다. 탈진 · 반응 · 전환으로 계획이 끝나면 판이 다음 자유로워짐을 묻고, 여기서 새 계획을
/// 고른다. 폭탄 반응은 판의 장치이고 이 조종기가 켠다(<see cref="ReactsToBombs"/>).
/// </para>
///
/// <para>
/// <b>틀린 계획은 버린다</b> — 쉬기가 1틱 아래 · 명부 밖 동작 · 정의가 없는 동작 · 지점과 잇는 동작 중 하나만 · 없는 지점 · 첫 동작과 같은 잇는 동작.
/// <c>[E] plan_invalid</c> 를 한 줄 남기고, 가장 짧은 쉬기를 센 뒤 다음 번호로 다시 고른다(0.11 그대로). 판은 끝까지 간다.
/// </para>
/// </summary>
public sealed class RuleController : IBossController
{
    private readonly IPlanPicker _picker;
    private readonly IReadOnlyList<string> _roster;
    private readonly IReadOnlyDictionary<string, PatternDef> _patterns;
    private readonly int _shortestRest;
    private readonly List<BossPlan> _plans = new();

    /// <summary>지금 계획 — 틀린 계획 뒤의 쉬기 동안은 null 이다.</summary>
    private BossPlan? _plan;

    /// <summary>지금 계획의 쉬기를 센 기준 — 고른 결정의 <see cref="BossDecision.Elapsed"/>.</summary>
    private int _base;

    private int _number;

    /// <summary>첫 동작이 돌고 계획의 캔슬이 아직 안 쓰였나.</summary>
    private bool _cancelPending;

    /// <param name="picker">계획 고르기.</param>
    /// <param name="actions">칸 배치 — 동작 칸을 짓는다.</param>
    /// <param name="roster">명부 — 계획의 칸이 이 인덱스다.</param>
    /// <param name="patterns">동작 정의 — 캔슬 지점을 읽는다.</param>
    /// <param name="restTicks">쉬는 길이들(틱) — 가장 짧은 것이 틀린 계획 뒤의 쉬기다.</param>
    public RuleController(
        IPlanPicker picker, BossActions actions, IReadOnlyList<string> roster, IReadOnlyDictionary<string, PatternDef> patterns, IReadOnlyList<int> restTicks)
    {
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(restTicks);
        if (restTicks.Count == 0)
        {
            throw new ArgumentException("쉬는 길이가 하나도 없다 — bosses.json 의 rest_seconds 가 비었다", nameof(restTicks));
        }

        _picker = picker;
        _roster = roster;
        _patterns = patterns;
        _shortestRest = Math.Max(1, restTicks.Min());
    }

    public bool ReactsToBombs => true;

    /// <summary>고른 계획들, 고른 순서로 — 버린 계획은 빠진다.</summary>
    public IReadOnlyList<BossPlan> Plans => _plans;

    /// <summary>
    /// <see cref="Plans"/> 를 기록의 모양으로 — 칸 대신 id, 틱 대신 초, 캔슬 지점 대신 그 시각. 시도 기록이 싣고 되살리기가 견준다. 부를 때마다 새로 짓는다.
    /// </summary>
    public IReadOnlyList<PlanEntry> Entries => [.. _plans.Select(Entry)];

    public int Decide(BossDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        switch (decision.Point)
        {
            case DecisionPoint.Freed:
                _cancelPending = false;
                Choose(decision);
                return BossActions.Wait;

            case DecisionPoint.Rest:
                return Rest(decision);

            case DecisionPoint.Approach:
                return BossActions.Approach;

            case DecisionPoint.Arrived:
                return First();

            case DecisionPoint.Cancel:
                if (_cancelPending && _plan is { CancelPoint: int k, Next: int next } && decision.CancelPoint == k)
                {
                    _cancelPending = false;
                    return BossActions.Move(next);
                }

                return BossActions.Continue;

            default:
                return BossActions.Wait;
        }
    }

    /// <summary>쉬는 결정 — 쉬기가 찼으면 달리기면 다가가기, 아니면 첫 동작. 틀린 계획 뒤면 가장 짧은 쉬기가 찬 결정에서 다시 고른다.</summary>
    private int Rest(BossDecision d)
    {
        int rested = d.Elapsed - _base;
        if (_plan is not { } plan)
        {
            if (rested >= _shortestRest)
            {
                Choose(d);
            }

            return BossActions.Wait;
        }

        if (rested < plan.RestTicks)
        {
            return BossActions.Wait;
        }

        return plan.Run ? BossActions.Approach : First();
    }

    /// <summary>첫 동작 — 계획에 캔슬이 있으면 이제부터 남는다.</summary>
    private int First()
    {
        if (_plan is not { } plan)
        {
            return BossActions.Wait;
        }

        _cancelPending = plan.CancelPoint is not null;
        return BossActions.Move(plan.Move);
    }

    /// <summary>다음 계획을 고른다 — 자유로워진 결정 · 틀린 계획 뒤 가장 짧은 쉬기가 찬 결정. 쉬기는 이 결정부터 센다.</summary>
    private void Choose(BossDecision d)
    {
        int number = _number++;
        BossSight s = d.Sight;
        var request = new PlanRequest(number, s.Tick, s.Events, s.BossX, s.BossFacing, s.Fighter.X, s.Form);
        _base = d.Elapsed;
        BossPlan plan = _picker.Next(request);
        _cancelPending = false;
        if (Problem(plan) is { } problem)
        {
            _plan = null;
            Log.Error("boss", $"plan_invalid n={number} {problem} tick={request.Tick}");
            return;
        }

        _plan = plan;
        _plans.Add(plan);
        Log.Debug("boss", () => $"plan n={number} rest={plan.RestTicks * BattleSim.Dt:0.##} run={(plan.Run ? 1 : 0)} move={_roster[plan.Move]}"
            + $" cancel={(plan.CancelPoint is int k ? $"{_patterns[_roster[plan.Move]].CancelPoints![k].T:0.##}" : "-")}"
            + $" next={(plan.Next is int next ? _roster[next] : "-")} tick={request.Tick}");
    }

    private PlanEntry Entry(BossPlan plan)
    {
        string move = _roster[plan.Move];
        return new PlanEntry(
            plan.RestTicks * BattleSim.Dt,
            move,
            plan.CancelPoint is int k ? _patterns[move].CancelPoints![k].T : null,
            plan.Next is int next ? _roster[next] : null,
            plan.Run);
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
