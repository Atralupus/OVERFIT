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
///
/// <para>
/// <b>시계는 틱을 센다</b> (#72 · 설계 §3.6 ⑤). 단계의 시각 T 는 세울 때 한 번 틱으로 바꾸고
/// (<see cref="BattleSim.TicksFor"/> — 반올림은 거기 한 곳이다), 시계가 그 틱에 닿는 틱에 단계에 든다.
/// T = 0 인 첫 단계는 러너의 첫 틱이다. 전에는 1/60 을 더해 가서 1.55초 판정이 93틱이 아니라 94틱에,
/// 2.65초가 159틱이 아니라 160틱에 섰다 — 부동소수 누적이 판정을 한 틱씩 밀었다.
/// </para>
/// </summary>
public sealed class PatternRunner
{
    private static readonly HitBox[] _none = Array.Empty<HitBox>();

    private readonly PatternDef _def;

    /// <summary>타임라인 칸별 판정 — 판을 세울 때 지었다(<see cref="BossHits"/>). active 가 아닌 칸은 null.</summary>
    private readonly IReadOnlyList<HitBox?> _hits;

    /// <summary>
    /// 마지막 <c>active</c> 단계의 자리 — <b>마무리</b>다 (이슈 #53). 판정이 없으면 -1.
    /// 데이터의 깃발이 아니라 여기서 뽑는 이유는 <see cref="HitBox.Finisher"/> 에 적어 두었다:
    /// 손으로 단 표는 판정을 하나 끼워 넣는 날 옛 마무리에 남는다.
    /// </summary>
    private readonly int _finisher;

    /// <summary>단계마다 드는 틱 — 패턴의 첫 틱이 1 이다. 세울 때 한 번 바꾼다.</summary>
    private readonly int[] _at;

    private int _next;

    /// <param name="def">패턴.</param>
    /// <param name="hits">칸별 판정 — <see cref="BossHits"/> 가 지었다. 타임라인과 길이가 같다.</param>
    public PatternRunner(PatternDef def, IReadOnlyList<HitBox?> hits)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(hits);
        if (hits.Count != def.Timeline.Count)
        {
            throw new ArgumentException($"판정 칸 {hits.Count} 이 타임라인 {def.Timeline.Count} 칸과 다르다", nameof(hits));
        }

        _def = def;
        _hits = hits;
        _finisher = def.Timeline.FindLastIndex(s => s.Kind == "active");
        _at = new int[def.Timeline.Count];
        for (int i = 0; i < _at.Length; i++)
        {
            _at[i] = BattleSim.TicksFor(def.Timeline[i].T);
        }
    }

    /// <summary>
    /// 패턴 시계 — 지금까지 민 틱 수. <b>세운 틱(<c>holdClock</c>)에는 안 는다</b> — 돌진(5번 PR)처럼 도착 시각이
    /// 파이터 자리에 달린 움직임이 도는 동안 뒤 단계를 기다리게 하는 자리다(설계 §8.1).
    /// </summary>
    public int Ticks { get; private set; }

    /// <summary>마지막 단계(<c>end</c>)에 들었다.</summary>
    public bool Finished => _next >= _def.Timeline.Count;

    /// <summary>지금 들어 있는 단계 — 마지막으로 든 것. 아직 한 틱도 안 돌았으면 null.</summary>
    public PatternStep? Step { get; private set; }

    /// <summary>
    /// <b>이번 틱에</b> 든 단계가 단 움직임 (설계 §8.1). 없으면 null. 러너는 움직임을 돌리지 않는다 — 파이터의 자리가
    /// 있어야 도는데 러너는 플레이어를 모른다. 돌리는 것은 <c>BattleSim</c> 이고, 러너는 그 결과의 <c>HoldClock</c> 만 따른다.
    /// </summary>
    public MotionDef? StartedMotion { get; private set; }

    /// <summary>
    /// 한 틱 민다. 이 틱에 새로 선 판정을 돌려준다 — 없으면 빈 목록.
    ///
    /// <para>
    /// <paramref name="holdClock"/> 이 참이면 <b>아무것도 안 한다</b> — 시계도 안 밀고 단계에도 안 든다.
    /// 움직임을 단 단계에 들면 그 틱에는 거기서 멈춘다: 그 뒤의 단계는 T 가 같아도 다음 틱부터 든다 —
    /// 시계를 세우는 움직임이면 그것이 끝난 다음 틱부터다(설계 §4.6: "A + 1 부터 시계가 다시 간다").
    /// </para>
    /// </summary>
    /// <param name="holdClock">지난 틱의 움직임이 이 틱의 시계를 세웠나 (<c>MotionStep.HoldClock</c>).</param>
    public IReadOnlyList<HitBox> Tick(bool holdClock = false)
    {
        StartedMotion = null;
        if (Finished || holdClock)
        {
            return _none;
        }

        Ticks++;

        List<HitBox>? hits = null;
        while (_next < _def.Timeline.Count && _at[_next] <= Ticks)
        {
            PatternStep step = _def.Timeline[_next];
            _next++;
            Step = step;

            // 헛스윙은 **판정이 아니라 박자**다 (이슈 #48). 여기서 HitBox 를 하나도 안 내는 것이
            // 요점이라, damage 0 판정으로 흉내 내지 않는다 — 그러면 BattleSim 이 관측을 한 건
            // 남기고, 일어난 적 없는 판정이 "안 맞았다" 로 계측에 쌓인다.
            if (step.Kind == "feint")
            {
                Feints++;
                continue;
            }

            if (_hits[_next - 1] is { } hit)
            {
                hits ??= new List<HitBox>();
                hits.Add(hit with { Finisher = _next - 1 == _finisher });
            }

            if (step.Motion is { } motion)
            {
                StartedMotion = motion;
                break;
            }
        }

        return (IReadOnlyList<HitBox>?)hits ?? _none;
    }

    /// <summary>
    /// 아직 안 든 첫 <c>active</c> 단계 — <b>다음 판정</b>이다. 없으면 null.
    /// 판정이 선 틱에 그 단계는 "다음" 이기를 그친다 — 창이 살아 있는지는 <c>BattleSim</c> 이 따로 안다.
    /// </summary>
    public PatternStep? NextActive => NextActiveAt() is int k ? _def.Timeline[k] : null;

    /// <summary>다음 판정이 칠 모양 — 디버그 표시의 "다음 판정" 이 러너가 낼 바로 그 판정을 그린다. 없으면 null.</summary>
    public HitBox? NextHit => NextActiveAt() is int k ? _hits[k] : null;

    /// <summary>다음 판정까지 남은 시간(초) — 그 단계의 틱에서 지금 시계를 뺀 것. 없으면 null.</summary>
    public double? NextActiveIn => NextActiveAt() is int k ? (_at[k] - Ticks) * BattleSim.Dt : null;

    private int? NextActiveAt()
    {
        for (int k = _next; k < _def.Timeline.Count; k++)
        {
            if (_def.Timeline[k].Kind == "active")
            {
                return k;
            }
        }

        return null;
    }

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
}
