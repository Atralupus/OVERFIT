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

        // 끝은 아레나 끝까지 남은 거리로 잰다 — "움직인 거리 < 한 걸음" 은 소수 자리의 뺄셈이 2 의 거듭제곱(1024 …)을 건너면 한 걸음보다 조금 작게
        // 나와 벽에서 먼 데서 끝났다고 했다(최종 리뷰가 밟았다). 돌진(RushMotion)이 남은 거리(ahead)로 재는 것과 같다.
        double room = away > 0 ? _bounds.MaxX - context.BossX : context.BossX - _bounds.MinX;
        double x = Math.Clamp(context.BossX + (away * _step), _bounds.MinX, _bounds.MaxX);
        return new MotionStep(x, 0, 0, Finished: room <= _step, HoldClock: false, GoalX: x);
    }
}
