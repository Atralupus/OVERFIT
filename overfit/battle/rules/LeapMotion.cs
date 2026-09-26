using System;

namespace Overfit.Battle.Rules;

/// <summary>
/// 도약 — 점프 공격의 움직임 (설계 §4.2). 뛰는 틱에 파이터를 한 번 보고 착지 자리를 정한 뒤, 정해진 시간 동안
/// <b>식으로 그린 포물선</b>을 따라간다. 그 뒤로는 파이터를 안 따라간다.
///
/// <para>
/// <b>물리 적분이 아니라 식인 이유:</b> 착지가 정확히 정해진 틱이어야 한다 — 착지 판정이 그 틱에 선다. 적분하면
/// 반올림이 착지를 한 틱씩 흔든다. 뜬 동안 s = 틱 / 뜬 틱 수, 높이 = 4H·s(1−s), 가로는 s 에 비례해 곧게 간다.
/// </para>
///
/// <para>
/// <b>착지 자리</b>는 뛰는 틱의 파이터 X 에서 보스 쪽으로 몸 둘 폭(보스 반폭 + 파이터 반폭)만큼 앞이고, 보스가 설 수
/// 있는 범위로 자른다. 둘이 같은 X 면 보던 쪽을 쓴다 — 보스는 파이터를 보고 있으니 보스 쪽은 보는 쪽의 반대다.
/// </para>
///
/// <para>
/// <b>뛰는 틱에 착지 자리 쪽으로 돌아선다</b> — 패턴 중에 방향이 바뀌는 유일한 자리다(설계 §4.2). 착지 판정이 좌우
/// 대칭인 바닥 전체라 방향이 판정을 안 바꾸고, 안 돌면 등 뒤로 날아가는 그림이 된다.
/// </para>
/// </summary>
public sealed class LeapMotion : IBossMotion
{
    private readonly double _height;
    private readonly int _airTicks;
    private readonly MotionBounds _bounds;

    private double _fromX;
    private double _toX;
    private int _facing;

    public LeapMotion(MotionDef def, MotionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(def);
        _height = def.Height;
        _airTicks = BattleSim.TicksFor(def.Air);
        _bounds = bounds;
    }

    public MotionStep Tick(MotionContext context)
    {
        if (context.Tick == 0)
        {
            _fromX = context.BossX;
            int side = Math.Sign(context.BossX - context.FighterX);
            if (side == 0)
            {
                side = -context.Facing;
            }

            _toX = Math.Clamp(context.FighterX + (side * _bounds.Standoff), _bounds.MinX, _bounds.MaxX);
            int toward = Math.Sign(_toX - _fromX);
            _facing = toward != 0 ? toward : context.Facing;
        }

        if (context.Tick >= _airTicks)
        {
            return new MotionStep(_toX, 0, _facing, Finished: true, HoldClock: false);
        }

        double s = (double)context.Tick / _airTicks;
        return new MotionStep(
            _fromX + ((_toX - _fromX) * s), 4 * _height * s * (1 - s), _facing, Finished: false, HoldClock: false);
    }
}
