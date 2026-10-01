using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 대본 조종기 (설계 2026-10-01 조각3 §4) — 결정마다 칸 이름을 차례로 낸다(<c>wait</c> · <c>retreat</c> · <c>leap_over</c> · 동작 id …). GIF · 스크린샷 ·
/// 테스트가 움직임을 못박는 데 쓴다 — 규칙 조종기의 계획 대본(<see cref="ScriptPlanPicker"/>)은 움직임을 못 고른다. 게임의 단계에는 안 쓴다.
/// 자유로워짐은 기다리기 · 캔슬 지점은 계속하기라 대본은 쉬기 · 움직임의 결정만 센다. 다 쓰면 기다린다. 열리지 않은 칸이면 판이 <c>[E]</c> 다.
/// </summary>
public sealed class ScriptActions : IBossController
{
    private readonly BossActions _actions;
    private readonly IReadOnlyList<string> _script;
    private int _next;

    /// <param name="actions">칸 배치 — 이름을 칸으로 바꾼다.</param>
    /// <param name="script">칸 이름들, 쓸 순서로.</param>
    public ScriptActions(BossActions actions, IReadOnlyList<string> script)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(script);
        _actions = actions;
        _script = script;
    }

    public bool ReactsToBombs => false;

    public int Decide(BossDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Point == DecisionPoint.Freed)
        {
            return BossActions.Wait;
        }

        if (decision.Point == DecisionPoint.Cancel)
        {
            return BossActions.Continue;
        }

        if (_next >= _script.Count)
        {
            return BossActions.Wait;
        }

        string name = _script[_next++];
        if (_actions.Index(name) is int action)
        {
            return action;
        }

        Log.Error("boss", $"script_action_unknown name={name}");
        return BossActions.Wait;
    }
}
