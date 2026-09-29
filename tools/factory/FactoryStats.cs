using System;
using System.Collections.Generic;

namespace Overfit.Factory;

/// <summary>칸 하나의 원본 기저율 — 그 칸의 사례 수와 맞은 수.</summary>
/// <param name="Pattern">2단계 명부의 그 칸의 패턴.</param>
/// <param name="Samples">사례 수.</param>
/// <param name="Hits">맞은 사례 수.</param>
public readonly record struct SlotRate(string Pattern, long Samples, long Hits)
{
    /// <summary>맞은 몫 — 사례가 없으면 0(매니페스트에 NaN 을 안 싣는다).</summary>
    public double Rate => Samples == 0 ? 0 : (double)Hits / Samples;
}

/// <summary>
/// 공장이 낸 원본을 센다 (#108 · 설계 2026-09-28 §4.4) — 칸마다의 기저율. 옛 망의 원본 겨냥 표(습관형 × 겨냥 패턴 · §4.6)는 옛 망과 같이 걷었다
/// (설계 2026-09-29 조각1 §6) — 조각 4 가 새 예측 어휘로 다시 세운다. 묶음이 올 때마다 <see cref="Add"/> 하고 끝에 읽는다 — 봇 결과를 들고 있지 않는다.
/// </summary>
public sealed class FactoryStats
{
    private readonly IReadOnlyList<string> _roster;
    private readonly long[] _slotSamples;
    private readonly long[] _slotHits;

    /// <param name="roster">명부 — 칸의 순서.</param>
    public FactoryStats(IReadOnlyList<string> roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        _roster = roster;
        _slotSamples = new long[roster.Count];
        _slotHits = new long[roster.Count];
    }

    public long Bots { get; private set; }

    public long ReachedStage2 { get; private set; }

    public long WonStage2 { get; private set; }

    public long Ticks { get; private set; }

    public long Samples { get; private set; }

    /// <summary>봇 한 대를 센다.</summary>
    public void Add(BotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Bots++;
        ReachedStage2 += result.ReachedStage2 ? 1 : 0;
        WonStage2 += result.WonStage2 ? 1 : 0;
        Ticks += result.Ticks;
        Samples += result.Samples.Count;
        foreach (FactorySample sample in result.Samples)
        {
            _slotSamples[sample.Slot]++;
            _slotHits[sample.Slot] += sample.Hit ? 1 : 0;
        }
    }

    /// <summary>칸마다의 기저율 — 명부 순서.</summary>
    public IReadOnlyList<SlotRate> Slots
    {
        get
        {
            var slots = new SlotRate[_roster.Count];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new SlotRate(_roster[i], _slotSamples[i], _slotHits[i]);
            }

            return slots;
        }
    }
}
