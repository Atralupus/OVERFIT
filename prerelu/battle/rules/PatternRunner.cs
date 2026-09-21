using System;
using System.Collections.Generic;

namespace PreReLU.Battle.Rules;

/// <summary>
/// 타임라인을 틱으로 돌린다. <b>플레이어를 모른다</b> — 어디에 판정이 서는지만 말하고,
/// 그게 누구에게 닿는지는 <c>BattleSim</c> 이 정한다.
///
/// <para>
/// 판정은 구간 내내 켜져 있지 않고 <b>단계에 들어선 틱에 한 번</b> 난다.
/// 그래야 <c>multi_hit</c> 을 타임라인의 active 개수로 셀 수 있고, 한 번 휘두른 칼에
/// 여러 번 맞는 일이 없다.
/// </para>
/// </summary>
public sealed class PatternRunner
{
    private static readonly HitBox[] _none = Array.Empty<HitBox>();

    private readonly PatternDef _def;
    private int _next;

    public PatternRunner(PatternDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        _def = def;
    }

    public double Elapsed { get; private set; }

    public bool Finished => Elapsed >= _def.Duration;

    /// <summary>한 틱 민다. 이 틱에 새로 선 판정을 돌려준다 — 없으면 빈 목록.</summary>
    public IReadOnlyList<HitBox> Tick(double dt)
    {
        if (Finished)
        {
            return _none;
        }

        Elapsed += dt;

        List<HitBox>? hits = null;
        while (_next < _def.Timeline.Count && _def.Timeline[_next].T <= Elapsed)
        {
            PatternStep step = _def.Timeline[_next];
            _next++;
            if (step.Kind != "active" || step.Distance is null || step.Height is null)
            {
                continue;
            }

            hits ??= new List<HitBox>();
            hits.Add(new HitBox(step.Distance[0], step.Distance[1], step.Height[0], step.Height[1], step.Damage));
        }

        return (IReadOnlyList<HitBox>?)hits ?? _none;
    }
}
