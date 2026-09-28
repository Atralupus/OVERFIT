using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 봇 함대의 성향 (#104 · 설계 2026-09-28 §3.3). 한 봇의 성향은 (함대 시드, 봇 번호)로 뽑힌다 — 봇 번호 하나로 그 봇이 되살아나야
/// 공장의 표본을 봇 단위로 다시 짓고 나눌 수 있다. 범위는 <c>tools/factory/fleet.json</c> 이다(실험의 조건).
/// </summary>
public class BotTraitsTests
{
    private const ulong _fleetSeed = 51;

    private static IEnumerable<BotTraits> Bots(int count) =>
        Enumerable.Range(0, count).Select(i => BotTraits.Sample(_fleetSeed, i, TestConfigs.Fleet()));

    private static void ShouldBeIn(double value, IReadOnlyList<double> range, string what) =>
        value.ShouldBeInRange(range[0], range[1], $"{what} 가 범위 [{range[0]}, {range[1]}] 밖이다");

    [Fact]
    public void Fleet_json_의_범위는_두_칸이고_낮음이_높음보다_크지_않다()
    {
        // 사람이 손으로 적는 파일이다 — 한 칸짜리 범위는 뽑는 자리에서야 색인 예외로 터지고, 뒤집힌 범위는 조용히 거꾸로 뽑는다.
        FleetConfig fleet = TestConfigs.Fleet();
        var ranges = new Dictionary<string, IReadOnlyList<double>>
        {
            ["habit_dominant"] = fleet.HabitDominant,
            ["reaction"] = fleet.Reaction,
            ["jitter"] = fleet.Jitter,
            ["bias"] = fleet.Bias,
            ["rhythm"] = fleet.Rhythm,
            ["dash_inward"] = fleet.DashInward,
            ["rest_gap"] = fleet.RestGap,
            ["spacing_rest_gap"] = fleet.SpacingRestGap,
            ["greed"] = fleet.Greed,
            ["chain"] = fleet.Chain,
            ["jump_lead"] = fleet.JumpLead,
        };
        foreach ((string key, IReadOnlyList<double> range) in ranges)
        {
            range.Count.ShouldBe(2, $"{key} 는 [낮음, 높음] 두 칸이어야 한다");
            range[0].ShouldBeLessThanOrEqualTo(range[1], $"{key} 가 뒤집혔다");
        }

        fleet.HabitShare.ShouldBeInRange(0, 1);
        fleet.RhythmReferences.ShouldNotBeEmpty();
    }

    [Fact]
    public void 겨냥_표의_패턴은_2단계_명부에_있다()
    {
        // 겨냥 표는 공장 · 학습 · 검증이 같이 읽는다(#108 · 설계 §4.6). 명부 밖의 id 는 그 줄의 사례가 0건이라 관문이 "기저율보다 높다" 를
        // 잴 수 없다 — 사람이 손으로 적는 파일이라 오타가 여기서 멈춘다. 수단 넷의 습관에 한 줄씩이고, 혼합형과 간격 습관형은 도드라진 칸이 없다.
        FleetConfig fleet = TestConfigs.Fleet();
        IReadOnlyList<string> roster = StageRoster.For(TestConfigs.Stages(), 2);

        fleet.Targeting.Select(r => r.Habit).ShouldBe(new[] { BotHabit.Dash, BotHabit.Guard, BotHabit.Parry, BotHabit.Jump });
        foreach (TargetingRow row in fleet.Targeting)
        {
            roster.ShouldContain(row.Pattern, $"{row.Habit} 의 겨냥 {row.Pattern} 이 2단계 명부에 없다");
            row.MinRhythm.ShouldBeInRange(0, 1);
        }
    }

    [Fact]
    public void 같은_봇_번호는_같은_성향이다()
    {
        BotTraits.Sample(_fleetSeed, 7, TestConfigs.Fleet()).ShouldBe(BotTraits.Sample(_fleetSeed, 7, TestConfigs.Fleet()));
    }

    [Fact]
    public void 다른_봇은_다른_성향이다()
    {
        BotTraits.Sample(_fleetSeed, 7, TestConfigs.Fleet()).ShouldNotBe(BotTraits.Sample(_fleetSeed, 8, TestConfigs.Fleet()));
        BotTraits.Sample(_fleetSeed, 7, TestConfigs.Fleet()).ShouldNotBe(BotTraits.Sample(_fleetSeed + 1, 7, TestConfigs.Fleet()));
    }

    [Fact]
    public void 성향은_범위_안이다()
    {
        FleetConfig fleet = TestConfigs.Fleet();
        foreach (BotTraits t in Bots(200))
        {
            ShouldBeIn(t.ReactionSeconds, fleet.Reaction, "반응");
            ShouldBeIn(t.JitterSeconds, fleet.Jitter, "흔들림");
            ShouldBeIn(t.BiasSeconds, fleet.Bias, "편향");
            ShouldBeIn(t.Rhythm, fleet.Rhythm, "리듬");
            ShouldBeIn(t.DashInward, fleet.DashInward, "대시 방향");
            ShouldBeIn(t.RestGap, t.Habit == BotHabit.Spacing ? fleet.SpacingRestGap : fleet.RestGap, "거리");
            ShouldBeIn(t.Greed, fleet.Greed, "욕심");
            ShouldBeIn(t.Chain, fleet.Chain, "2연격");
            ShouldBeIn(t.JumpLead, fleet.JumpLead, "점프 앞당김");
        }
    }

    [Fact]
    public void 수단_비중의_합은_1이고_칸마다_0보다_크다()
    {
        // 판정마다의 수단은 대시 · 점프 · 패리를 비중의 합으로 나눠 고른다 — 가드가 도드라진 봇이라도 그 합이 0 으로 떨어지면 안 된다.
        foreach (BotTraits t in Bots(200))
        {
            (t.Dash + t.Jump + t.Parry + t.Guard).ShouldBe(1, 1e-12);
            t.Dash.ShouldBeGreaterThan(0);
            t.Jump.ShouldBeGreaterThan(0);
            t.Parry.ShouldBeGreaterThan(0);
            t.Guard.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public void 습관형의_주된_수단은_습관_범위다()
    {
        FleetConfig fleet = TestConfigs.Fleet();
        foreach (BotTraits t in Bots(400))
        {
            double? dominant = t.Habit switch
            {
                BotHabit.Dash => t.Dash,
                BotHabit.Jump => t.Jump,
                BotHabit.Parry => t.Parry,
                BotHabit.Guard => t.Guard,
                _ => null,
            };
            if (dominant is not { } d)
            {
                continue;
            }

            ShouldBeIn(d, fleet.HabitDominant, $"{t.Habit} 습관형의 주된 수단");
            double rest = (1 - d) / 3;
            new[] { t.Dash, t.Jump, t.Parry, t.Guard }.Count(w => Math.Abs(w - rest) < 1e-12).ShouldBe(3, "나머지 셋이 남은 몫을 똑같이 나누지 않았다");
        }
    }

    [Fact]
    public void 습관은_고르게_뽑힌다()
    {
        List<BotTraits> bots = Bots(1000).ToList();
        List<BotTraits> habits = bots.Where(t => t.Habit != BotHabit.Mixed).ToList();

        ((double)habits.Count / bots.Count).ShouldBeInRange(0.45, 0.55, "습관형의 몫이 habit_share(0.5)에서 멀다");
        foreach (BotHabit habit in new[] { BotHabit.Dash, BotHabit.Jump, BotHabit.Parry, BotHabit.Guard, BotHabit.Spacing })
        {
            ((double)habits.Count(t => t.Habit == habit) / habits.Count).ShouldBeInRange(0.15, 0.25, $"{habit} 습관이 고르게 안 뽑혔다");
        }
    }

    [Fact]
    public void 세션_시드는_봇마다_다르다()
    {
        Enumerable.Range(0, 100).Select(i => BotTraits.SessionSeed(_fleetSeed, i)).Distinct().Count().ShouldBe(100);
    }

    [Fact]
    public void 잡음의_평균은_0_편차는_sd_다()
    {
        // 균등 난수 넷의 합(Irwin–Hall)을 늘인 것이다 — 정규분포 변환(박스–뮬러)의 log · sqrt 를 안 쓴다(설계 2026-09-28 §8).
        double[] noise = Enumerable.Range(0, 10_000).Select(k => BotNoise.Sample(51, k, 0.05)).ToArray();
        double mean = noise.Average();
        double sd = Math.Sqrt(noise.Select(x => (x - mean) * (x - mean)).Average());

        Math.Abs(mean).ShouldBeLessThan(0.002);
        sd.ShouldBeInRange(0.05 * 0.95, 0.05 * 1.05);
    }

    [Fact]
    public void 잡음은_같은_키면_같고_편차가_0이면_0이다()
    {
        BotNoise.Sample(51, 3, 0.05).ShouldBe(BotNoise.Sample(51, 3, 0.05));
        BotNoise.Sample(51, 3, 0.05).ShouldNotBe(BotNoise.Sample(51, 4, 0.05));
        BotNoise.Sample(51, 3, 0).ShouldBe(0);
    }
}
