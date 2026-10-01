using System;

namespace Overfit.Battle.Rules;

/// <summary>
/// 물러서기 (설계 2026-10-01 조각3 §0) — 파이터에게서 멀어지는 쪽으로 틱마다 빠르기만큼 간다. 보는 쪽은 안 바꾼다(0) — 판이 틱마다 파이터 쪽으로
/// 돌려세워 파이터를 본 채 뒤로 달린다. 아레나 끝에 닿으면 끝난다(갈 데가 없다). 스스로 멈추지 않는다 — 판이 결정 간격마다 다시 묻는다.
/// </summary>
public sealed class RetreatMotion : IBossMotion
{
    private readonly double _step;
    private readonly MotionBounds _bounds;

    public RetreatMotion(MotionDef def, MotionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(def);
        _step = Math.Max(0, def.Speed) * BattleSim.Dt;
        _bounds = bounds;
    }

    public MotionStep Tick(MotionContext context)
    {
        int away = Math.Sign(context.BossX - context.FighterX);
        if (away == 0)
        {
            away = -context.Facing;
        }

        double x = Math.Clamp(context.BossX + (away * _step), _bounds.MinX, _bounds.MaxX);
        bool stuck = Math.Abs(x - context.BossX) < _step;
        return new MotionStep(x, 0, 0, Finished: stuck, HoldClock: false, GoalX: x);
    }
}
