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
///
/// <para>
/// 다섯 축은 전체가 아니라 <b>부분집합</b>으로 계산된다 (대시·점프·패리 건만).
/// <see cref="Samples"/> 만 옆에 붙이면 "관측 10건" 이 "대시 3건으로 낸 분산" 까지
/// 보증하는 것처럼 보인다 — 가장 얇은 근거를 가장 크게 믿게 만드는 배치다.
/// 그래서 수단별 건수를 따로 싣는다. <b>축이 아니라 개수다</b> — 10축 계약은 그대로다.
/// </para>
/// </summary>
public sealed class PlayerAxes
{
    public double DashTimingBias { get; private init; }

    public double DashTimingVar { get; private init; }

    /// <summary>+1 에 가까울수록 안으로 파고들고 -1 에 가까울수록 밖으로 도망간다.</summary>
    public double DashDirectionBias { get; private init; }

    public double JumpTimingBias { get; private init; }

    /// <summary>
    /// <b>대시나 패리로도 피할 수 있었던 상황에서</b> 점프를 고른 비율 (스펙 8절).
    /// 그냥 사용 비율이 아니다 — 그러면 "점프에 의존한다" 와 "점프로만 피할 수 있는 패턴만
    /// 만났다" 가 같은 값이 되는데, 그 둘은 봉인할 것이 정반대다.
    /// </summary>
    public double JumpReliance { get; private init; }

    /// <summary>
    /// 판정이 선 순간 공중에 있었던 비율. <b>공중에 떠 있던 시간의 비율이 아니다</b> —
    /// 그 이름이었던 적이 있는데, 재는 것은 "액티브 프레임마다 공중이었나" 라서
    /// 전투의 90%를 땅에서 보낸 플레이어도 1.00 이 나왔다.
    /// 값을 바꾸지 않고 이름을 바꾼 이유는 <c>anti_air</c> 패턴이 아픈지를 예측하는 데는
    /// 체류 시간보다 <b>피격 순간의 고도</b>가 바로 그 답이기 때문이다.
    /// </summary>
    public double AirborneAtImpactRatio { get; private init; }

    /// <summary>
    /// 패리 <b>성공</b>률. 분자는 <b>정확</b> 패리뿐이다 — 부정확 패리는 절반을 내상으로 받고
    /// 굳으므로 "막았다" 로 세면 두 결과가 한 점이 된다. 부정확의 수는
    /// <see cref="ParryLateSamples"/> 가 따로 나른다 (축이 아니라 개수다).
    /// </summary>
    public double ParryRate { get; private init; }

    /// <summary><b>다른 수단이 있는데</b> 패리를 고른 비율 (스펙 8절). <c>JumpReliance</c> 와 같은 셈법이다.</summary>
    public double ParryReliance { get; private init; }

    public double Greed { get; private init; }

    public double DistanceBias { get; private init; }

    /// <summary>이 축들을 낸 관측 수. 축의 신뢰도가 여기 들어 있다.</summary>
    public int Samples { get; private init; }

    /// <summary>대시로 설명된 관측 수. <c>DashTimingBias</c> · <c>DashTimingVar</c> ·
    /// <c>DashDirectionBias</c> 는 이만큼의 근거로 나왔다.</summary>
    public int DashSamples { get; private init; }

    /// <summary>점프로 설명된 관측 수. <c>JumpTimingBias</c> 의 근거다.</summary>
    public int JumpSamples { get; private init; }

    /// <summary>패리로 설명된 관측 수. <c>ParryRate</c> 의 분모다.</summary>
    public int ParrySamples { get; private init; }

    /// <summary>
    /// 그중 <b>부정확</b> 패리로 받아낸 수. 정확(<c>ParryRate</c> 의 분자) · 부정확(여기) ·
    /// 무반응(둘 다 아님)이 셋으로 갈리는 자리다 — 전에는 뒤의 둘이 같은 점이었다(이슈 #27).
    /// <b>축이 아니라 개수다</b> — 10축 계약은 그대로다.
    /// </summary>
    public int ParryLateSamples { get; private init; }

    /// <summary>
    /// 점프가 가능했고 <b>다른 수단도 가능했던</b> 관측 수 — <c>JumpReliance</c> 의 분모다.
    /// 의존도는 부분집합의 부분집합이라 <c>Samples</c> 도 <c>JumpSamples</c> 도 이 얇기를 안 말해준다.
    /// </summary>
    public int JumpChoiceSamples { get; private init; }

    /// <summary>패리가 가능했고 다른 수단도 가능했던 관측 수 — <c>ParryReliance</c> 의 분모다.</summary>
    public int ParryChoiceSamples { get; private init; }

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
        int jumpChoices = 0, jumpChosen = 0, parryChoices = 0, parryChosen = 0, parriedLate = 0;
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

            // 의존도의 분모는 **진짜 선택이 있었던** 판정뿐이다 — 그 수단이 가능했고,
            // 다른 수단도 하나 이상 가능했던 자리. 고를 수 없었던 것을 "안 골랐다" 로 세면
            // 축이 플레이어의 성향이 아니라 보스의 패턴 구성을 재게 된다.
            if (e.JumpAvailable && (e.DashAvailable || e.ParryAvailable))
            {
                jumpChoices++;
                if (e.Verb == DodgeVerb.Jump)
                {
                    jumpChosen++;
                }
            }

            if (e.ParryAvailable && (e.DashAvailable || e.JumpAvailable))
            {
                parryChoices++;
                if (e.Verb == DodgeVerb.Parry)
                {
                    parryChosen++;
                }
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
                    else if (e.Verdict == HitVerdict.ParriedLate)
                    {
                        parriedLate++;
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
            JumpReliance = Ratio(jumpChosen, jumpChoices),
            AirborneAtImpactRatio = Ratio(airborne, events.Count),
            ParryRate = Ratio(parried, parries),
            ParryReliance = Ratio(parryChosen, parryChoices),
            Greed = Ratio(greedy, events.Count),
            DistanceBias = distance / events.Count,
            Samples = events.Count,
            DashSamples = dashes,
            JumpSamples = jumps,
            ParrySamples = parries,
            ParryLateSamples = parriedLate,
            JumpChoiceSamples = jumpChoices,
            ParryChoiceSamples = parryChoices,
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
