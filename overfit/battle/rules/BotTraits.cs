using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>함대 봇의 습관 — 수단 하나가 도드라진 봇과 고른 봇(<see cref="Mixed"/>)을 가른다. 겨냥 표를 따라 자르는 열쇠다.</summary>
public enum BotHabit
{
    Mixed,
    Dash,
    Jump,
    Guard,
    Spacing,
}

/// <summary>
/// 함대 봇 한 대의 성향 (#104 · 설계 2026-09-28 §3.3). <see cref="FleetBot"/> 이 이것만 보고 싸운다.
/// </summary>
/// <param name="Habit">습관 — 뽑은 방식의 기록이다. 봇은 안 읽고 공장과 검증이 봇을 자르는 데 쓴다.</param>
/// <param name="Dash">판정이 오면 대시로 받는 비중.</param>
/// <param name="Jump">점프로 받는 비중.</param>
/// <param name="Guard">패턴을 가드로 받는 비중 — 셋의 합은 1 이다. 패리로 받는 비중(<c>Parry</c>)은 패리와 같이 걷었다(#168).</param>
/// <param name="ReactionSeconds">선딜이 시작하고 누를 수 있게 되기까지(초).</param>
/// <param name="JitterSeconds">누르는 시각의 편차(초).</param>
/// <param name="BiasSeconds">누르는 시각의 편향(초) — 음수가 먼저다.</param>
/// <param name="Rhythm">판정을 눈으로 안 보고 기준 패턴의 박자로 누르는 몫.</param>
/// <param name="DashInward">대시가 보스 쪽(안)인 몫.</param>
/// <param name="RestGap">쉬는 동안 칼 사거리 너머 기다리는 간격(px) — 0 이면 붙어서 친다.</param>
/// <param name="Greed">판정이 오는데도 안 피하고 칼을 넣는 몫.</param>
/// <param name="Chain">1타를 누를 때 2타를 이을 작정인 몫.</param>
/// <param name="JumpLead">점프를 판정 앞 몇 몫(솟는 시간의)에 누르나.</param>
public sealed record BotTraits(
    BotHabit Habit,
    double Dash,
    double Jump,
    double Guard,
    double ReactionSeconds,
    double JitterSeconds,
    double BiasSeconds,
    double Rhythm,
    double DashInward,
    double RestGap,
    double Greed,
    double Chain,
    double JumpLead)
{
    /// <summary>봇 한 대의 세션 시드 — 공장이 이 위에 <see cref="RunHistory"/> 를 세워 게임과 같은 시도 시드를 낸다.</summary>
    public static ulong SessionSeed(ulong fleetSeed, int bot) => Det.Hash64(fleetSeed, Det.Domain.FleetBot, k1: bot, k2: 0);

    /// <summary>
    /// (함대 시드, 봇 번호) → 성향. 성향 j 의 좌표는 <c>Roll01(함대 시드, FleetBot, k1: 봇, k2: 1 + j)</c> 다 — 세션 시드(<c>k2: 0</c>)와 안 겹친다.
    /// </summary>
    public static BotTraits Sample(ulong fleetSeed, int bot, FleetConfig fleet)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        double U(int j) => Det.Roll01(fleetSeed, Det.Domain.FleetBot, k1: bot, k2: 1 + j);

        BotHabit habit = U(0) < fleet.HabitShare ? (BotHabit)(1 + (int)(U(1) * 4)) : BotHabit.Mixed;

        double[] w;
        if (habit is BotHabit.Dash or BotHabit.Jump or BotHabit.Guard)
        {
            // 주된 수단이 habit_dominant 를 갖고 나머지 둘이 남은 몫을 똑같이 나눈다 — 가드가 0.9 여도 대시 · 점프가 0 이 안 된다.
            double dominant = Lerp(fleet.HabitDominant, U(2));
            double rest = (1 - dominant) / 2;
            w = [rest, rest, rest];
            w[(int)habit - 1] = dominant;
        }
        else
        {
            // 혼합형과 간격 습관형 — 간격의 습관은 수단이 아니라 서는 자리(RestGap)다. 좌표 5 는 패리의 몫이었다(#168) — 비워 두고
            // 뒤의 좌표를 안 당긴다: 당기면 남은 성향들이 한꺼번에 다른 값으로 뽑힌다.
            w = [U(2), U(3), U(4)];
            double sum = w[0] + w[1] + w[2];
            for (int i = 0; i < 3; i++)
            {
                w[i] = sum > 0 ? w[i] / sum : 1.0 / 3;
            }
        }

        return new BotTraits(
            habit,
            w[0],
            w[1],
            w[2],
            ReactionSeconds: Lerp(fleet.Reaction, U(6)),
            JitterSeconds: Lerp(fleet.Jitter, U(7)),
            BiasSeconds: Lerp(fleet.Bias, U(8)),
            Rhythm: Lerp(fleet.Rhythm, U(9)),
            DashInward: Lerp(fleet.DashInward, U(10)),
            RestGap: Lerp(habit == BotHabit.Spacing ? fleet.SpacingRestGap : fleet.RestGap, U(11)),
            Greed: Lerp(fleet.Greed, U(12)),
            Chain: Lerp(fleet.Chain, U(13)),
            JumpLead: Lerp(fleet.JumpLead, U(14)));
    }

    private static double Lerp(IReadOnlyList<double> range, double u) => range[0] + ((range[1] - range[0]) * u);
}

/// <summary>
/// 함대 봇의 타이밍 잡음 (#104 · 설계 2026-09-28 §3.1 · §8). 균등 난수 넷의 합(Irwin–Hall)을 편차에 맞춰 늘인다 — 평균 0 · 편차 <c>sd</c>.
/// 정규분포 변환(박스–뮬러)을 안 쓰는 까닭은 그 log · sqrt 가 결정 경로에 들어오기 때문이다 — 여기는 사칙연산뿐이다.
/// </summary>
public static class BotNoise
{
    /// <summary>√3 — 균등 난수 넷의 합의 편차(1/√3)를 1 로 늘이는 몫. 상수로 둔다(<c>Math.Sqrt</c> 를 안 부른다).</summary>
    private const double _sqrtThree = 1.7320508075688772;

    /// <summary><c>key</c>(보통 봇의 계획 번호)의 잡음 — 같은 (시드, 키)는 같은 값이다. 좌표는 <c>Roll01(시드, FleetTiming, k1: key, k2: 0..3)</c>.</summary>
    public static double Sample(ulong seed, long key, double sd)
    {
        double sum = 0;
        for (int k = 0; k < 4; k++)
        {
            sum += Det.Roll01(seed, Det.Domain.FleetTiming, k1: key, k2: k);
        }

        return (sum - 2) * sd * _sqrtThree;
    }
}
