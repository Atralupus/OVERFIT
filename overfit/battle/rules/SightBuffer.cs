using System;

namespace Overfit.Battle.Rules;

/// <summary>
/// 파이터 모습의 고리 — 늦은 관측 (설계 2026-10-01 조각2 §2). 판이 틱마다 한 번 쌓고, 결정은 늦춤만큼 앞의 모습을 읽는다. 판 초반(늦춤보다 적게 쌓였을
/// 때)은 첫 모습이다 — 판이 서기 전의 파이터는 없다.
/// </summary>
public sealed class SightBuffer
{
    private readonly FighterSnapshot[] _ring;
    private int _head;
    private int _count;

    /// <param name="delayTicks">늦춤(틱) — 0 이면 지금이다.</param>
    public SightBuffer(int delayTicks)
    {
        if (delayTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayTicks), delayTicks, "늦춤은 0 이상이다");
        }

        _ring = new FighterSnapshot[delayTicks + 1];
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

    /// <summary>늦춤만큼 앞의 모습 — 모자라면 첫 모습이다.</summary>
    public FighterSnapshot Delayed => _count < _ring.Length ? _ring[(_head - _count + 1 + _ring.Length) % _ring.Length] : _ring[(_head + 1) % _ring.Length];
}
