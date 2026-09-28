using System;
using System.Collections.Generic;
using Overfit.Battle.Rules;

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

/// <summary>겨냥 표 한 줄의 원본 값 — 그 습관형의 그 칸 맞는 몫과 전체의 그 칸 기저율.</summary>
/// <param name="Row">겨냥 표의 줄.</param>
/// <param name="Bots">그 줄에 드는 봇 수(습관 · 리듬 문턱).</param>
/// <param name="Samples">그 봇들의 그 칸 사례 수.</param>
/// <param name="Hits">그중 맞은 수.</param>
/// <param name="Rate">그 봇들의 그 칸 맞는 몫.</param>
/// <param name="BaseRate">전체의 그 칸 기저율.</param>
public sealed record TargetingResult(TargetingRow Row, long Bots, long Samples, long Hits, double Rate, double BaseRate)
{
    /// <summary>기저율 위로 얼마나 — 양수가 겨냥이 보이는 것이다.</summary>
    public double Diff => Rate - BaseRate;

    /// <summary>기저율보다 높나 — 사례가 없으면 잴 것이 없어 넘지 못한다.</summary>
    public bool Above => Samples > 0 && Rate > BaseRate;
}

/// <summary>
/// 공장이 낸 원본을 센다 (#108 · 설계 2026-09-28 §4.4 · §4.6) — 칸마다의 기저율과 원본 겨냥 표. <b>학습 전에 데이터 자체를 본다</b>: 습관형 봇이 겨냥
/// 표의 패턴에 기저율보다 더 맞지 않으면 봇의 모형이 그 습관을 못 흉내 내거나 패턴이 설계대로 겨냥하지 않는 것이다 — 어느 쪽이든 망이 배울 것이 없다.
/// 묶음이 올 때마다 <see cref="Add"/> 하고 끝에 읽는다 — 봇 결과를 들고 있지 않는다.
/// </summary>
public sealed class FactoryStats
{
    private readonly IReadOnlyList<string> _roster;
    private readonly IReadOnlyList<TargetingRow> _rows;
    private readonly int[] _rowSlots;
    private readonly long[] _slotSamples;
    private readonly long[] _slotHits;
    private readonly long[] _rowBots;
    private readonly long[] _rowSamples;
    private readonly long[] _rowHits;

    /// <param name="roster">2단계 명부 — 칸의 순서.</param>
    /// <param name="targeting">겨냥 표 — 명부 밖의 패턴이면 세울 때 거절한다(그 줄은 잴 사례가 없다).</param>
    public FactoryStats(IReadOnlyList<string> roster, IReadOnlyList<TargetingRow> targeting)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(targeting);
        _roster = roster;
        _rows = targeting;
        _rowSlots = new int[targeting.Count];
        for (int r = 0; r < targeting.Count; r++)
        {
            _rowSlots[r] = IndexOf(roster, targeting[r].Pattern);
            if (_rowSlots[r] < 0)
            {
                throw new ArgumentException($"겨냥 표의 패턴 {targeting[r].Pattern} 이 2단계 명부에 없다", nameof(targeting));
            }
        }

        _slotSamples = new long[roster.Count];
        _slotHits = new long[roster.Count];
        _rowBots = new long[targeting.Count];
        _rowSamples = new long[targeting.Count];
        _rowHits = new long[targeting.Count];
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

        for (int r = 0; r < _rows.Count; r++)
        {
            if (result.Traits.Habit != _rows[r].Habit || result.Traits.Rhythm < _rows[r].MinRhythm)
            {
                continue;
            }

            _rowBots[r]++;
            foreach (FactorySample sample in result.Samples)
            {
                if (sample.Slot == _rowSlots[r])
                {
                    _rowSamples[r]++;
                    _rowHits[r] += sample.Hit ? 1 : 0;
                }
            }
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

    /// <summary>원본 겨냥 표 — 겨냥 표의 순서.</summary>
    public IReadOnlyList<TargetingResult> Targeting
    {
        get
        {
            var rows = new TargetingResult[_rows.Count];
            for (int r = 0; r < rows.Length; r++)
            {
                double rate = _rowSamples[r] == 0 ? 0 : (double)_rowHits[r] / _rowSamples[r];
                var slot = new SlotRate(_roster[_rowSlots[r]], _slotSamples[_rowSlots[r]], _slotHits[_rowSlots[r]]);
                rows[r] = new TargetingResult(_rows[r], _rowBots[r], _rowSamples[r], _rowHits[r], rate, slot.Rate);
            }

            return rows;
        }
    }

    private static int IndexOf(IReadOnlyList<string> roster, string id)
    {
        for (int i = 0; i < roster.Count; i++)
        {
            if (string.Equals(roster[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
