using System;

namespace Overfit.Battle.Rules;

/// <summary>
/// 보스의 <b>경직 게이지</b> (#71 · 설계 §4.5). 파이터의 칼이 닿으면 그 칼질의 경직도만큼 차고, 마지막으로 맞은 뒤
/// 유예 동안은 그대로다가 그 뒤로 틱마다 같은 양이 빠진다. 끝까지 차면 <c>BattleSim</c> 이 보스를 탈진시킨다 —
/// 받아쳤을 때와 같은 탈진이다(설계 §4.3 · 루틴은 하나다).
///
/// <para>
/// <b>상태만 든다.</b> 언제 채우고 언제 줄이고 언제 비우는지(결정타 · 탈진 동안 · 탈진할 때)는 <c>BattleSim</c> 이 정한다 —
/// 그 셋은 보스가 탈진해 있나와 한 판의 승패에 달려 있고, 그것을 아는 곳이 거기다. 여기는 "채운다 · 한 틱 민다 · 비운다"
/// 셋의 산수만 한다. 그래야 게이지의 산수(유예 72틱 · 틱당 1/6)를 판 없이 잰다.
/// </para>
///
/// <para>
/// 난수가 없다. 채움은 정수이고 줄어듦은 늘 같은 상수라 같은 시드면 같은 값이다. 유예는 <b>틱으로</b> 센다 —
/// 1.2초 = 72틱이고 반올림은 <c>BattleSim.TicksFor</c> 한 곳이다.
/// </para>
/// </summary>
public sealed class PoiseGauge
{
    private readonly int _holdTicks;
    private readonly double _decayPerTick;

    /// <summary>남은 유예 틱. 0 이면 이 틱부터 줄어든다.</summary>
    private int _holdLeft;

    /// <param name="max">끝 — <c>poise_max</c>.</param>
    /// <param name="holdTicks">맞은 뒤 그대로인 틱 — <c>poise_decay_delay</c> 를 틱으로 바꾼 값.</param>
    /// <param name="decayPerTick">유예가 끝난 뒤 틱마다 빠지는 양 — <c>poise_decay_per_second</c> × 한 틱.</param>
    public PoiseGauge(double max, int holdTicks, double decayPerTick)
    {
        Max = max;
        _holdTicks = Math.Max(0, holdTicks);
        _decayPerTick = Math.Max(0, decayPerTick);
    }

    /// <summary>
    /// 이 보스의 게이지 — <c>bosses.json</c> 의 세 키로 세운다. 초를 틱으로 바꾸는 반올림은 <see cref="BattleSim.TicksFor"/> 다.
    /// </summary>
    public static PoiseGauge For(BossConfig boss)
    {
        ArgumentNullException.ThrowIfNull(boss);
        return new PoiseGauge(
            boss.PoiseMax, BattleSim.TicksFor(boss.PoiseDecayDelay), boss.PoiseDecayPerSecond * BattleSim.Dt);
    }

    /// <summary>끝. 칼질마다의 경직도(<c>fighters.json</c> 의 <c>combo[].poise</c>)가 이것의 백분율로 읽히게 100 이다.</summary>
    public double Max { get; }

    /// <summary>지금 찬 양 (0 ~ <see cref="Max"/>).</summary>
    public double Value { get; private set; }

    /// <summary>끝까지 찼나 — 찼으면 <c>BattleSim</c> 이 탈진시킨다.</summary>
    public bool Full => Value >= Max;

    /// <summary>
    /// 칼이 닿았다 — <paramref name="amount"/> 만큼 채우고 유예를 처음부터 다시 세운다. <b>끝에서 멈추고 넘치지 않는다.</b>
    /// 실제로 찬 양을 돌려준다 — 로그의 <c>poise=</c> 가 이것이다(설계 §4.5: 로그가 "왜" 를 틀리게 말하지 않는다).
    /// </summary>
    public double Fill(int amount)
    {
        double before = Value;
        Value = Math.Min(Max, Value + Math.Max(0, amount));
        _holdLeft = _holdTicks;
        return Value - before;
    }

    /// <summary>
    /// 한 틱 민다. 유예가 남았으면 유예만 줄고, 다 됐으면 같은 양이 빠진다(0 에서 멈춘다). 맞은 틱 뒤로 <c>holdTicks</c> 틱
    /// 동안 값이 그대로다 — 맞은 틱에 채우면서 유예를 세우고, 다음 틱부터 센다.
    /// </summary>
    public void Tick()
    {
        if (_holdLeft > 0)
        {
            _holdLeft--;
            return;
        }

        Value = Math.Max(0, Value - _decayPerTick);
    }

    /// <summary>
    /// 비운다 — 보스가 탈진할 때(원인이 패리든 게이지든). 남은 유예는 안 건드린다: 0 에서는 빠질 것이 없어 유예가 남든 말든 값이
    /// 같고, 다음 칼(<see cref="Fill"/>)이 유예를 처음부터 다시 세운다.
    /// </summary>
    public void Empty() => Value = 0;
}
