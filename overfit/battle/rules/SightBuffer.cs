using System;

namespace Overfit.Battle.Rules;

/// <summary>
/// 파이터 모습의 고리 — 늦은 관측 (설계 2026-10-01 조각2 §2 · 조각4 §2). 판이 틱마다 한 번 쌓고, 결정은 늦춤만큼 앞의 모습(<see cref="Delayed"/>)과 그보다 더 앞의
/// 모습(<see cref="At"/> — 관측의 최근 K 모습)을 읽는다. 모자라면 가장 오래된 모습이다 — 판이 서기 전의 파이터는 없다.
/// </summary>
public sealed class SightBuffer
{
    private readonly FighterSnapshot[] _ring;
    private readonly int _delay;
    private int _head;
    private int _count;

    /// <param name="delayTicks">늦춤(틱) — 0 이면 지금이다.</param>
    /// <param name="extraTicks">늦춤보다 더 앞을 얼마나 들고 있나(틱) — 관측의 최근 모습이 쓴다.</param>
    public SightBuffer(int delayTicks, int extraTicks = 0)
    {
        if (delayTicks < 0 || extraTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayTicks), $"늦춤 {delayTicks} · 여유 {extraTicks} 는 0 이상이다");
        }

        _delay = delayTicks;
        _ring = new FighterSnapshot[delayTicks + extraTicks + 1];
    }

    /// <summary>지금 틱의 모습을 쌓는다 — 틱마다 한 번.</summary>
    public void Push(FighterSnapshot now)
    {
        _head = (_head + 1) % _ring.Length;
        _ring[_head] = now;
        if (_count < _ring.Length)
        {
            _count++;
        }
    }

    /// <summary>늦춤만큼 앞의 모습 — 모자라면 가장 오래된 모습이다.</summary>
    public FighterSnapshot Delayed => At(0);

    /// <summary>늦춤보다 <paramref name="back"/> 틱 더 앞의 모습 — 모자라면 가장 오래된 모습이다.</summary>
    public FighterSnapshot At(int back)
    {
        int age = Math.Min(_delay + Math.Max(0, back), Math.Max(0, _count - 1));
        return _ring[((_head - age) % _ring.Length + _ring.Length) % _ring.Length];
    }
}
