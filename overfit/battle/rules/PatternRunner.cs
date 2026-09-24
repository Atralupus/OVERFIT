using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 타임라인을 틱으로 돌린다. <b>플레이어를 모른다</b> — 어디에 판정이 서는지만 말하고,
/// 그게 누구에게 닿는지는 <c>BattleSim</c> 이 정한다.
///
/// <para>
/// 판정은 <b>단계에 들어선 틱에 한 번</b> 나고, 그 판정이 몇 틱 동안 살아 있을지는 싣기만 한다
/// (<see cref="HitBox.ActiveSeconds"/>) — 살아 있는 동안 몸에 대 보는 것은 <c>BattleSim</c> 이다.
/// 그래서 <c>multi_hit</c> 을 타임라인의 active 개수로 셀 수 있고, 한 번 휘두른 칼에 여러 번 맞는 일이 없다.
/// </para>
/// </summary>
public sealed class PatternRunner
{
    private static readonly HitBox[] _none = Array.Empty<HitBox>();

    private readonly PatternDef _def;

    /// <summary>
    /// 마지막 <c>active</c> 단계의 자리 — <b>마무리</b>다 (이슈 #53). 판정이 없으면 -1.
    /// 데이터의 깃발이 아니라 여기서 뽑는 이유는 <see cref="HitBox.Finisher"/> 에 적어 두었다:
    /// 손으로 단 표는 판정을 하나 끼워 넣는 날 옛 마무리에 남는다.
    /// </summary>
    private readonly int _finisher;

    private int _next;

    public PatternRunner(PatternDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        _def = def;
        _finisher = def.Timeline.FindLastIndex(s => s.Kind == "active");
    }

    public double Elapsed { get; private set; }

    public bool Finished => Elapsed >= _def.Duration;

    /// <summary>
    /// 지금까지 지나간 <b>헛스윙</b>(<c>feint</c> 단계) 수 — 이슈 #48.
    ///
    /// <para>
    /// 헛스윙은 판정을 안 내지만 <b>화면에는 있어야 한다.</b> 안 보이는 헛스윙은 미끼가 아니다 —
    /// 칼이 지나가는 것이 보여야 거기에 패리를 지르고, 그 습관을 <c>III-역린</c> 이 벌한다.
    /// 규칙 층은 뷰를 모르므로(콜백을 두면 헤드리스 봇이 그 콜백을 들고 다닌다) 관측 수와
    /// 같은 규약으로 <b>개수</b>만 싣는다: 값이 는 틱이 곧 "칼이 지나갔다" 다.
    /// </para>
    /// </summary>
    public int Feints { get; private set; }

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

            // 헛스윙은 **판정이 아니라 박자**다 (이슈 #48). 여기서 HitBox 를 하나도 안 내는 것이
            // 요점이라, damage 0 판정으로 흉내 내지 않는다 — 그러면 BattleSim 이 관측을 한 건
            // 남기고, 일어난 적 없는 판정이 "안 맞았다" 로 계측에 쌓인다.
            if (step.Kind == "feint")
            {
                Feints++;
                continue;
            }

            if (step.Kind != "active" || step.Distance is null || step.Height is null)
            {
                continue;
            }

            hits ??= new List<HitBox>();
            hits.Add(new HitBox(
                ShapeOf(step), step.Damage, step.GuardBreak, Finisher: _next - 1 == _finisher,
                ActiveSeconds: step.ActiveSeconds));
        }

        return (IReadOnlyList<HitBox>?)hits ?? _none;
    }

    /// <summary>
    /// 타임라인 한 단계의 판정 모양. <b>모양을 짓는 곳은 여기 하나다</b> — 러너가 판정을 낼 때와
    /// 디버그 표시가 "다음 판정" 을 그릴 때가 같은 함수를 불러야 둘이 같은 자리를 가리킨다.
    /// 지금은 옛 거리 띠 · 높이 띠를 좌우 대칭 두 장으로 옮긴다 (<see cref="HitShape.Band"/>).
    /// </summary>
    public static HitShape ShapeOf(PatternStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Distance is null || step.Height is null)
        {
            throw new ArgumentException($"판정이 아닌 단계다 — kind={step.Kind}", nameof(step));
        }

        return HitShape.Band(step.Distance[0], step.Distance[1], step.Height[0], step.Height[1]);
    }
}
