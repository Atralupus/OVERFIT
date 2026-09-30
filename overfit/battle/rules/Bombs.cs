using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>나는 폭탄 하나 — 뷰가 놓은 자리에서 보스의 지금 자리로 포물선을 그린다. 규칙은 이것을 안 읽는다.</summary>
/// <param name="FromX">놓은 자리의 x — 파이터의 발 중심.</param>
/// <param name="FromY">놓은 자리의 발바닥 높이.</param>
/// <param name="Progress">난 몫 — 놓은 틱이 0 이고 떨어지는 틱에 1 이다.</param>
public readonly record struct BombFlight(double FromX, double FromY, double Progress);

/// <summary>
/// 나는 폭탄들 (설계 2026-09-30 조각2 §1.3) — 놓은 폭탄을 세고, 날 시간이 다 되면 떨어뜨린다. <b>보스를 따라가 떨어지므로 자리를 안 잰다</b>:
/// 나는 동안 보스가 달려도 도약해도 맞는다. 피하는 길이 없어 보스가 막는 길은 놓기 전에 쳐서 끊는 것 하나다(§2).
///
/// <para>
/// <b>월드 물체가 아니다</b> — 보스의 판정(<see cref="BossSwings"/>)이 칠 대상이 아니므로 파이터만 아는 그 구조를 안 넓힌다(우산 §5 의 물음).
/// 판(<see cref="BattleSim"/>)이 파이터가 놓은 틱에 <see cref="Launch"/> 하고, 틱마다 <see cref="Tick"/> 이 낸 수에 <see cref="Damage"/> 를 곱해 보스에 싣는다.
/// </para>
/// </summary>
public sealed class Bombs
{
    private readonly int _flightTicks;
    private readonly int _damage;

    /// <summary>나는 폭탄 — (놓은 자리, 떨어지기까지 남은 틱). 한 판에 보통 하나다: 다음 폭탄은 선딜(1.5초)이 나는 시간(0.5초)보다 길어 앞의 것이 먼저 떨어진다.</summary>
    private readonly List<(double X, double Y, int Left)> _flying = new();

    public Bombs(BombDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        _flightTicks = BattleSim.TicksFor(def.FlightSeconds);
        _damage = def.Damage;
    }

    /// <summary>나는 폭탄들 — 뷰가 읽는다. 부를 때마다 새로 짓는다(봇이 수백만 판을 도는 동안은 안 부른다).</summary>
    public IReadOnlyList<BombFlight> InFlight
    {
        get
        {
            var flights = new List<BombFlight>(_flying.Count);
            foreach ((double x, double y, int left) in _flying)
            {
                flights.Add(new BombFlight(x, y, 1.0 - ((double)left / _flightTicks)));
            }

            return flights;
        }
    }

    /// <summary>폭탄 하나의 피해 — 떨어진 수에 곱해 보스에 싣는다(판).</summary>
    public int Damage => _damage;

    /// <summary>폭탄 하나를 날린다 — 파이터가 놓은 틱이다. 이 틱의 <see cref="Tick"/> 뒤에 불러야 놓은 틱 + 나는 틱에 떨어진다.</summary>
    public void Launch(double x, double y) => _flying.Add((x, y, _flightTicks));

    /// <summary>한 틱 — 날 시간이 다 된 폭탄을 떨어뜨리고 그 수를 낸다(없으면 0). 떨어진 폭탄은 빠진다.</summary>
    public int Tick()
    {
        int landed = 0;
        int kept = 0;
        for (int i = 0; i < _flying.Count; i++)
        {
            (double x, double y, int left) = _flying[i];
            if (--left > 0)
            {
                _flying[kept++] = (x, y, left);
            }
            else
            {
                landed++;
            }
        }

        _flying.RemoveRange(kept, _flying.Count - kept);
        return landed;
    }

    /// <summary>나는 폭탄을 다 버린다 — 판이 끝났다(누가 죽거나 시간 · §1.3). 끝난 판에 틱을 더 넣어도 안 떨어진다.</summary>
    public void Clear() => _flying.Clear();
}
