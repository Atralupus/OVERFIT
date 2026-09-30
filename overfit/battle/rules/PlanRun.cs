using System;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 계획의 달리기 (설계 2026-09-29 조각1 §5.2) — 쉬기가 끝나고 계획이 달리기를 골랐을 때 서고(<see cref="Start"/>), 닿거나 상한을 넘기면 끝나 첫
/// 동작의 칸을 낸다(<see cref="Step"/>). 돌진과 같은 움직임(등록표의 <c>run</c>)에 <c>bosses.json</c> 의 빠르기 · 멈출 거리를 싣는다.
///
/// <para>
/// <see cref="BattleSim"/> 에서 떼어 냈다 (설계 2026-09-30 조각2 · CLAUDE.md §7) — 판이 주석 빼고 400줄을 넘어 폭탄과 보스의 반응을 얹을 자리가
/// 없었다. 보스를 옮기는 것까지가 여기고, 돌아서는 것(<c>BattleSim.FaceFighter</c>)과 동작을 세우는 것은 판이다 — 동작 사이의 틱마다 판이 하던 일이다.
/// </para>
/// </summary>
public sealed class PlanRun
{
    private readonly BossConfig _config;

    /// <summary>달리기의 상한(틱) — <see cref="BossConfig.RunMaxSeconds"/> 를 세울 때 한 번 바꾼다.</summary>
    private readonly int _maxTicks;

    /// <summary>도는 달리기 — 달리지 않으면 null.</summary>
    private IBossMotion? _motion;

    /// <summary>달리기가 끝나면 세울 첫 동작의 칸.</summary>
    private int _move;

    /// <summary>달린 틱 — 쉬기가 끝난 틱이 1 이다. 상한과 견준다.</summary>
    private int _ticks;

    /// <summary>달리기를 선 자리(x) — 로그의 <c>moved=</c>(간 거리)를 잰다. 이미 멈출 거리 안이면 0 이다.</summary>
    private double _from;

    public PlanRun(BossConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _maxTicks = BattleSim.TicksFor(config.RunMaxSeconds);
    }

    /// <summary>달리는 중인가 — 뷰가 <c>run</c> 을 돈다(<c>BattleSim.BossRunning</c>). 닿는 틱에는 거짓이다(그 틱에 첫 동작이 선다).</summary>
    public bool Active => _motion is not null;

    /// <summary>
    /// 달리기를 세운다 — 쉬기가 끝난 틱이다. 판은 곧이어 이 틱의 <see cref="Step"/> 을 부른다: 이미 멈출 거리 안이면 그 걸음이 곧 끝이라 같은 틱에 첫 동작이
    /// 선다(안 달린다). 등록표에서 <c>run</c> 이 빠졌으면 규칙 위반이다(돌진의 <c>motion_missing</c> 과 같은 대우) — <c>[E]</c> 를 남기고 거짓이다: 판이
    /// 달리지 않고 첫 동작을 세운다.
    /// </summary>
    /// <param name="move">달리기가 끝나면 세울 첫 동작의 칸.</param>
    /// <param name="boss">보스 — 선 자리를 잰다.</param>
    /// <param name="fighterX">파이터의 x — 로그의 <c>d=</c>.</param>
    /// <param name="bounds">보스가 설 수 있는 범위와 몸 간격.</param>
    /// <param name="tick">판의 틱 — 로그.</param>
    public bool Start(int move, Boss boss, double fighterX, MotionBounds bounds, int tick)
    {
        ArgumentNullException.ThrowIfNull(boss);
        var def = new MotionDef { Id = "run", Speed = _config.RunSpeed, Stop = _config.RunStop };
        _motion = BossMotions.Create(def, bounds);
        _move = move;
        _ticks = 0;
        _from = boss.X;
        if (_motion is null)
        {
            Log.Error("boss", $"motion_missing id=run tick={tick}");
            return false;
        }

        if (Log.IsEnabled(LogLevel.Debug))
        {
            Log.Debug("boss", $"run_begin d={Math.Abs(fighterX - boss.X):0} tick={tick}");
        }

        return true;
    }

    /// <summary>
    /// 달리기 한 걸음 — 앞으로만 간다(판이 먼저 파이터 쪽으로 돌려세운다 · 동작 사이라 잠금이 없다). 닿으면 첫 동작의 칸을 낸다. 상한
    /// (<see cref="BossConfig.RunMaxSeconds"/>)까지 못 닿으면 <c>[W] run_timeout</c> 을 남기고 그 자리에서 낸다 — 대시로 계속 도망가면 넘을 수 있다
    /// (안전장치다). 멈출 자리가 설 수 있는 범위 밖이면 움직임이 경계에서 끝낸다(<see cref="RushMotion"/>). 아직 달리면 null.
    /// </summary>
    /// <remarks>로그는 즉시 오버로드다 — 이 메서드는 달리는 동안 틱마다 불려, 인자를 붙잡는 람다면 클로저가 매 틱 메서드 입구에서 만들어진다.</remarks>
    public int? Step(Boss boss, double fighterX, int tick)
    {
        ArgumentNullException.ThrowIfNull(boss);
        MotionStep step = _motion!.Tick(new MotionContext(boss.X, boss.Y, boss.Facing, fighterX, _ticks++));
        boss.Move(step.X, step.Y, 0);
        if (step.Finished)
        {
            // 이미 멈출 거리 안이었으면 ticks=1 · moved=0 이다 — 계획은 달리기를 골랐지만 안 달렸다.
            if (Log.IsEnabled(LogLevel.Debug))
            {
                Log.Debug("boss", $"run_end ticks={_ticks} moved={Math.Abs(boss.X - _from):0} x={boss.X:0} tick={tick}");
            }
        }
        else if (_ticks >= _maxTicks)
        {
            Log.Warn("boss", $"run_timeout ticks={_ticks} d={Math.Abs(fighterX - boss.X):0} x={boss.X:0} tick={tick}");
        }
        else
        {
            return null;
        }

        _motion = null;
        return _move;
    }

    /// <summary>달리기를 버린다 — 동작을 걷을 때(<c>BattleSim.ClearPattern</c> · 달리는 동안 탈진하면 계획이 끝난다).</summary>
    public void Clear() => _motion = null;
}
