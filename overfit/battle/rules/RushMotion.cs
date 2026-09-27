using System;

namespace Overfit.Battle.Rules;

/// <summary>
/// 돌진 — 1타 돌진의 움직임 (#78 · 설계 §4.6). 유저: "1타 공격까지만하고 run 애니메이션을 재생해서 유저에게 아주 빠르게 다가가서 3타
/// 공격을 하는겁니다." 매 틱 파이터를 다시 재고, 파이터 앞 <c>stop</c> 에 닿을 때까지 보는 쪽으로 <c>speed</c> 로 간다 — 닿을 때까지
/// <b>패턴 시계를 세운다</b>(<see cref="MotionStep.HoldClock"/>). 도착 시각이 파이터 자리에 달려 있어서다: 그 뒤의 단계(3타의 선딜)는 T 가
/// 같아도 도착한 다음 틱부터 든다.
///
/// <para>
/// <b>매 틱</b> 앞쪽 거리 d = (파이터 X − 보스 X) × 보는 쪽 을 잰다 — 파이터가 이번 틱을 먼저 움직인 뒤다(<c>BattleSim</c> 이 넣는다).
/// d ≤ stop 이면 그 틱에 끝난다(움직이지 않는다). 아니면 앞으로 min(speed / 60, d − stop) 간다 — 닿으면 d = stop 으로 끝난다.
/// <b>뒤로는 안 간다</b> — 1타 동안 보스를 뚫고 지나가 등 뒤에 선 사람(d &lt; 0)에게는 0틱이고 3타는 앞으로 헛친다(설계 §4.6).
/// <b>보는 쪽은 그대로다</b>(패턴 중 잠금) — 도약과 달리 돌아서지 않는다.
/// </para>
///
/// <para>
/// <b>목표를 보스가 설 수 있는 범위로 자른다.</b> 멈출 자리가 범위 밖이면(파이터가 벽에 붙고 <c>stop</c> 이 두 몸의 반폭 차 85 − 30 보다
/// 짧으면) 보스는 경계까지 가서 끝난다. 안 자르면 <c>Boss.Move</c> 가 자리를 경계로 되돌리는 동안 이 움직임은 매 틱 "아직 멀다" 를 내고,
/// 시계가 영영 선다 — 패턴이 끝나지 않는다(#59 의 3/6 넘김). 실제 수치(3600 · 280)에서는 멈출 자리가 늘 310 ~ 1610 이라 안 걸린다.
/// </para>
/// </summary>
public sealed class RushMotion : IBossMotion
{
    /// <summary>한 틱에 가는 거리(px) — <c>speed</c> × 1/60. 3600 이면 정확히 60 이라 틱마다 같은 거리다.</summary>
    private readonly double _step;

    private readonly double _stop;
    private readonly MotionBounds _bounds;

    public RushMotion(MotionDef def, MotionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(def);
        _step = def.Speed * BattleSim.Dt;
        _stop = def.Stop;
        _bounds = bounds;
    }

    public MotionStep Tick(MotionContext context)
    {
        double goal = Math.Clamp(context.FighterX - (context.Facing * _stop), _bounds.MinX, _bounds.MaxX);

        // 목표까지 앞으로 남은 거리 — d − stop 이다(목표가 잘리지 않았으면). 0 이하면 이미 닿았거나 목표가 등 뒤다: 움직이지 않고 끝난다.
        double ahead = (goal - context.BossX) * context.Facing;
        if (ahead <= 0 || _step <= 0)
        {
            return new MotionStep(context.BossX, 0, 0, Finished: true, HoldClock: false, GoalX: context.BossX);
        }

        double move = Math.Min(_step, ahead);
        bool arrived = move >= ahead;

        // 도착까지 남은 틱 — 지금 자리에서 잰 추정이다(파이터가 움직이면 바뀐다). 다음 판정까지 남은 시간이 이것을 더한다(설계 §4.6).
        int left = arrived ? 0 : (int)Math.Ceiling((ahead - move) / _step);
        return new MotionStep(
            context.BossX + (context.Facing * move), 0, 0, Finished: arrived, HoldClock: !arrived, HoldTicks: left, GoalX: goal);
    }
}
