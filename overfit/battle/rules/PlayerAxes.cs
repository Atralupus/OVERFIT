using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 플레이어가 어떻게 싸우는가, 10개 숫자로. <see cref="DodgeEvent"/> 목록만 받으므로
/// <b>전투를 안 돌려도 테스트된다.</b>
///
/// <para>
/// 근거가 없는 축은 <b>0</b> 이다 — NaN 을 내면 나중에 망 입력에 섞여 조용히 학습을 망친다.
/// 그래서 <see cref="Samples"/> 를 같이 들고 다닌다: "3건으로 낸 0.5" 와 "300건으로 낸 0.5" 는 다르고,
/// 망이 그 차이를 알아야 한다.
/// </para>
/// </summary>
public sealed class PlayerAxes
{
    public double DashTimingBias { get; private init; }

    public double DashTimingVar { get; private init; }

    /// <summary>+1 에 가까울수록 안으로 파고들고 -1 에 가까울수록 밖으로 도망간다.</summary>
    public double DashDirectionBias { get; private init; }

    public double JumpTimingBias { get; private init; }

    public double JumpReliance { get; private init; }

    public double AirTimeRatio { get; private init; }

    public double ParryRate { get; private init; }

    public double ParryReliance { get; private init; }

    public double Greed { get; private init; }

    public double DistanceBias { get; private init; }

    /// <summary>이 축들을 낸 관측 수. 축의 신뢰도가 여기 들어 있다.</summary>
    public int Samples { get; private init; }

    public static PlayerAxes From(IReadOnlyList<DodgeEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return new PlayerAxes();
        }

        var dashErrors = new List<double>();
        var jumpErrors = new List<double>();
        int dashes = 0, jumps = 0, parries = 0, parried = 0, inward = 0, outward = 0, airborne = 0, greedy = 0;
        double distance = 0;

        foreach (DodgeEvent e in events)
        {
            distance += e.Distance;
            if (e.Airborne)
            {
                airborne++;
            }

            if (e.GreedWindow)
            {
                greedy++;
            }

            switch (e.Verb)
            {
                case DodgeVerb.Dash:
                    dashes++;
                    dashErrors.Add(e.TimingError);
                    if (e.Direction > 0)
                    {
                        inward++;
                    }
                    else if (e.Direction < 0)
                    {
                        outward++;
                    }

                    break;
                case DodgeVerb.Jump:
                    jumps++;
                    jumpErrors.Add(e.TimingError);
                    break;
                case DodgeVerb.Parry:
                    parries++;
                    if (e.Verdict == HitVerdict.Parried)
                    {
                        parried++;
                    }

                    break;
                default:
                    break;
            }
        }

        return new PlayerAxes
        {
            DashTimingBias = Mean(dashErrors),
            DashTimingVar = Variance(dashErrors),
            DashDirectionBias = Ratio(inward - outward, inward + outward),
            JumpTimingBias = Mean(jumpErrors),
            JumpReliance = Ratio(jumps, events.Count),
            AirTimeRatio = Ratio(airborne, events.Count),
            ParryRate = Ratio(parried, parries),
            ParryReliance = Ratio(parries, events.Count),
            Greed = Ratio(greedy, events.Count),
            DistanceBias = distance / events.Count,
            Samples = events.Count,
        };
    }

    /// <summary>근거가 없으면 0. NaN 을 내지 않는다.</summary>
    private static double Ratio(int part, int whole) => whole == 0 ? 0 : (double)part / whole;

    private static double Mean(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (double v in values)
        {
            sum += v;
        }

        return sum / values.Count;
    }

    private static double Variance(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double mean = Mean(values);
        double sum = 0;
        foreach (double v in values)
        {
            sum += (v - mean) * (v - mean);
        }

        return sum / values.Count;
    }
}
