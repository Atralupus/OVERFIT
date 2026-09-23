using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 플레이어가 어떻게 싸우는가, 10개 숫자로. <b>열이라는 것이 계약이다</b> — 망의 입력 모양이라
/// 늘리는 것은 수치 하나를 고치는 것과 다른 종류의 변경이고, 새 기술의 신호는 축이 아니라
/// <b>개수</b>로 실린다 (<see cref="ParryLateSamples"/> · <see cref="ChargedGreedSamples"/> ·
/// <see cref="GuardSamples"/>). <see cref="DodgeEvent"/> 목록만 받으므로
/// <b>전투를 안 돌려도 테스트된다.</b>
///
/// <para>
/// 근거가 없는 축은 <b>0</b> 이다 — NaN 을 내면 나중에 망 입력에 섞여 조용히 학습을 망친다.
/// 그래서 <see cref="Samples"/> 를 같이 들고 다닌다: "3건으로 낸 0.5" 와 "300건으로 낸 0.5" 는 다르고,
/// 망이 그 차이를 알아야 한다.
/// </para>
///
/// <para>
/// ⚠ <b>지금 축 셋은 근거가 얇거나 없다</b> (이슈 #48). 보스가 내려찍기 <b>한 계열</b>로 줄면서
/// 점프로 넘을 판정과 대공 판정이 데이터에서 사라졌다:
/// <see cref="JumpReliance"/> 는 분모가 구조적으로 0 이고(<c>jumpable</c> 패턴이 없다),
/// <see cref="JumpTimingBias"/> 는 <b>실패한 점프</b>만 모으며,
/// <see cref="AirborneAtImpactRatio"/> 는 값이 움직이긴 하지만 그것을 벌하는 패턴이 없어
/// 아무것도 예측하지 않는다.
/// <b>그래도 지우지 않는다</b> — 근거가 없으면 0 이고 <see cref="Samples"/> 가 그 얇기를 같이 나르며,
/// 계열이 돌아오면 셋은 데이터 한 줄로 다시 살아난다. 축을 뺐다 넣는 것은 망의 입력 <b>모양</b>을
/// 두 번 바꾸는 일이라 그 편이 훨씬 비싸다. 반대로 <see cref="DistanceBias"/> 와
/// <see cref="DashDirectionBias"/> 는 주인이 바뀌었을 뿐 살아 있다 — 쇄도(사거리)와 끌기(안쪽 주머니)가
/// 점프 강타에게서 그 일을 물려받았다.
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

    /// <summary>
    /// 대시 타이밍 오차의 분산.
    ///
    /// <para>
    /// ⚠ <b>표본은 "판정이 설 때까지 살아 있던 대시" 뿐이다</b> (확인함 · 이슈 #46). 시작 시각은
    /// 대시 행동이 끝나는 순간 지워지므로, <b>너무 일찍 시작해 무적이 이미 닫힌 채 맞은 대시</b>는
    /// 이 분산에 안 들어간다 — 그 판정은 <c>Verb=None</c> 으로 기록된다. 즉 이 값은
    /// "대시가 판정에 겹쳤다는 조건 아래의 분산" 이라 실제보다 <b>작게</b> 나온다.
    /// </para>
    ///
    /// <para>
    /// 일부러 이대로 둔다. 고치려면 관측을 판정 시점이 아니라 <b>판정 뒤까지 기다렸다</b> 내보내야
    /// 하는데(이슈 #16 이 같은 구멍을 다른 각도에서 적어 뒀다 — "늦어서 못 피했다" 와
    /// "아무것도 안 했다" 가 한 점이 되는 것), 그건 계측의 방출 구조를 바꾸는 일이라 이 이슈의 범위가
    /// 아니다. 대신 <see cref="DashSamples"/> 가 몇 건으로 낸 값인지를 같이 나른다.
    /// </para>
    ///
    /// <para>
    /// 경계는 <c>BattleSim.CreditDistance</c> 와 <b>같은 규칙</b>이다: 대시의 공은 대시 행동이
    /// 끝나는 곳까지다. 둘이 다른 경계를 쓰면 같은 대시가 축마다 다른 개수로 세어진다.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// 평균 교전 거리(px). <b>안쪽 주머니로 파고들어 피한 판정만 부호가 반대다</b> (이슈 #46) —
    /// <c>MissedTooClose</c> 는 "너무 가까워서 안 맞았다" 라, 그것을 +로 쌓으면 파고들수록
    /// "멀리서 싸운다" 가 커진다. 축이 뭉개는 것이 아니라 <b>거꾸로 말하는</b> 자리였다.
    ///
    /// <para>
    /// ⚠ <b>정규화가 없는 평균이라 개막 접근 구간이 이 값을 크게 끌어올린다</b> (재 봄 · 이슈 #46).
    /// 파이터는 아레나 1/4 · 보스는 3/4 에서 시작해 교전 거리 <b>960px</b> 으로 출발하고, 붙기 전에
    /// 서는 판정은 전부 그 큰 거리로 쌓인다. 실측(데모 시드 51 · 23건)으로 <b>개막 3건이 496px</b>
    /// 이고 나머지 20건은 5~252px 인데, 그 3건(전체의 13%)이 축을 <b>72.5 → 127.7</b> 로 올린다 —
    /// 실제 교전 거리의 <b>1.76배</b>다. 작은 오염이 아니라 이 축의 절반쯤이 개막 걸음이라는 뜻이고,
    /// 판이 짧을수록(관측이 적을수록) 몫이 더 커진다.
    /// </para>
    ///
    /// <para>
    /// 그래도 이 이슈에서는 <b>재고 적어만 둔다.</b> 고치려면 "언제부터 교전인가" 를 정해야 하는데
    /// (첫 접촉? 첫 판정? 몇 초?) 그건 데이터에 없는 새 수치이고, 그 수치가 곧 축의 정의를 바꾼다 —
    /// 계측을 고치러 온 자리에서 축의 뜻을 말없이 바꾸면 이 이슈가 고치려던 것과 같은 사고가 난다.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// <b>가드로 버틴</b> 관측 수 (이슈 #47). <b>축이 아니라 개수다</b> — 10축 계약은 그대로다.
    ///
    /// <para>
    /// <b>왜 11번째 축이 아닌가.</b> 10축은 망의 <b>입력 모양</b>이라, 하나 늘리는 것은 지금까지의
    /// 모든 입력 벡터를 다른 길이로 만드는 일이다. 그럴 만한 값이 지금 가드에는 없다:
    /// 의존도 축(<see cref="JumpReliance"/> · <see cref="ParryReliance"/>)의 셈법은
    /// "그 수단이 가능했고 <b>다른 수단도</b> 가능했던" 판정을 분모로 삼는데, 가드는
    /// <c>guard_break</c> 가 아닌 <b>모든</b> 판정에서 가능하다 — 분모가 사실상 전부라
    /// "가드 의존도" 는 그냥 사용 비율이 되고, 그건 이 두 축이 피하려고 만들어진 바로 그 값이다.
    /// </para>
    ///
    /// <para>
    /// <b>그 대조는 이제 생겼다</b> (이슈 #48): 3단계 변종 다섯은 마무리가 전부 가드 불가이고
    /// 1·2단계에는 하나도 없어, "가드가 되는 판정" 과 "안 되는 판정" 이 실제로 갈린다.
    /// 그런데도 축으로 안 올리는 것은 <b>그것이 별개의 결정</b>이기 때문이다 — 10축은 망의 입력
    /// 모양이고 지금 이 저장소에는 그 망이 아직 없다(변종은 무작위로 뽑힌다). 입력 모양은
    /// 망을 세우는 자리에서 한 번에 정하는 것이 맞고, 그때까지 신호는 개수로 안전하게 쌓인다.
    /// </para>
    ///
    /// <para>
    /// 그동안에는 <see cref="ParryLateSamples"/> · <see cref="ChargedGreedSamples"/> 와 같은 자리에
    /// 개수로 싣는다. 잃는 것도 적다 — 가드는 이미 다른 축을 움직인다:
    /// 가드로 받은 판정은 <see cref="ParryReliance"/> 의 분모에 들어가되 분자에는 안 들어가고
    /// ("패리 말고 다른 것을 골랐다"), 거리는 <see cref="DistanceBias"/> 에 그대로 쌓인다.
    /// </para>
    /// </summary>
    public int GuardSamples { get; private init; }

    /// <summary>
    /// 그중 <b>깨진</b> 가드의 수 (이슈 #47). <see cref="ParryLateSamples"/> 와 같은 자리다 —
    /// 개수 하나가 없으면 "버텨냈다" 와 "버티다 무너졌다" 가 한 점이 되는데, 그 둘은 결과가 정반대다
    /// (흘린 피해 0.25 · 자세 유지 ↔ 전액 · 0.9초 고정). 무엇이 깼는지(고갈 · 가드 불가)는
    /// 여기서 안 가른다: 고른 것도 겪은 것도 같은 "깨졌다" 이고, 가르려면 축이 아니라 이벤트를 본다.
    /// </summary>
    public int GuardBrokenSamples { get; private init; }

    /// <summary>
    /// <c>Greed</c> 로 센 것 중 <b>모아 둔 칼을 들고 있던</b> 관측 수 (이슈 #40).
    /// <b>축이 아니라 개수다</b> — 10축 계약은 그대로다.
    ///
    /// <para>
    /// 비율 하나로는 "휘두르다 맞았다"(0.5초)와 "2초를 모으고 서 있다 맞았다"(2.08초)가
    /// 한 점이 된다. 욕심의 <b>깊이</b>가 여기 있고, <c>ParryLateSamples</c> 와 같은 자리다.
    /// </para>
    /// </summary>
    public int ChargedGreedSamples { get; private init; }

    /// <summary>
    /// 관측들을 축으로 접는다.
    ///
    /// <para>
    /// ⚠ <b><see cref="DodgeEvent.PatternId"/> 를 안 읽는다</b> — 열 축 전부가 모든 패턴을 뭉갠
    /// 값이다 (확인함 · 이슈 #46). 계열이 여럿이면 치명적이다: "내려찍기는 패리하고 올려베기는
    /// 점프한다" 는 사람이 "패리 반 점프 반" 한 명으로 읽혀, 봉인할 것을 못 고른다.
    ///
    /// <para>
    /// <b>지금은 뭉갤 것이 없다</b> (이슈 #48). 보스가 내려찍기 한 계열이고 변종 아홉은 전부
    /// <b>같은 네 답</b>(대시 · 가드 · 패리 · 간격)을 묻는다 — 변종별로 갈라 봐야 같은 성향을
    /// 아홉 조각으로 나누는 것뿐이고, 조각마다 표본이 얇아져 오히려 나빠진다.
    /// <b>계열이 둘이 되는 날 이 줄이 다시 빚이 된다</b> — 그때 계열별 집계를 만든다.
    /// </para>
    /// </para>
    /// </summary>
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
        int jumpChoices = 0, jumpChosen = 0, parryChoices = 0, parryChosen = 0, parriedLate = 0, chargedGreed = 0;
        int guards = 0, guardsBroken = 0;
        double distance = 0;

        foreach (DodgeEvent e in events)
        {
            // 안쪽 주머니로 피한 것은 **반대 부호**다 (이슈 #46). 거리 자체는 양수이므로
            // 여기서 뒤집지 않으면 "파고들어 피했다" 가 "멀리 떨어져 있었다" 와 같은 방향으로 쌓인다.
            distance += e.Verdict == HitVerdict.MissedTooClose ? -e.Distance : e.Distance;
            if (e.Airborne)
            {
                airborne++;
            }

            if (e.GreedWindow)
            {
                greedy++;
            }

            if (e.ChargeTier > 0)
            {
                chargedGreed++;
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
                case DodgeVerb.Guard:
                    // 막아냈든 깨졌든 **고른 것은 가드**다. 둘의 차이는 verb 가 아니라 Verdict 가 나른다 —
                    // 정확·부정확 패리를 한 verb 로 둔 것과 같은 규약이다.
                    guards++;
                    if (e.Verdict == HitVerdict.GuardBroken)
                    {
                        guardsBroken++;
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
            GuardSamples = guards,
            GuardBrokenSamples = guardsBroken,
            JumpChoiceSamples = jumpChoices,
            ParryChoiceSamples = parryChoices,
            ChargedGreedSamples = chargedGreed,
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
