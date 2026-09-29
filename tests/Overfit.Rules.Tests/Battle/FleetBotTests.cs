using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 함대 봇 (#104 · 설계 2026-09-28 §3.1 · §3.3). 사람처럼 늦고 흔들린다 — 선딜이 시작하고 제 반응 지연이 지나야 누른다. 리듬형은 판정을
/// 눈으로 안 보고 기준 패턴의 박자로 누른다.
/// </summary>
public class FleetBotTests
{
    /// <summary>패리만 하고 흔들림 · 편향이 없는 봇 — 누르는 틱을 틱까지 잰다.</summary>
    private static BotTraits Parrier(double reaction, double rhythm = 0) => FleetPlay.Mid with
    {
        Dash = 0,
        Jump = 0,
        Parry = 1,
        Guard = 0,
        ReactionSeconds = reaction,
        JitterSeconds = 0,
        BiasSeconds = 0,
        Rhythm = rhythm,
        Greed = 0,
    };

    /// <summary>
    /// 판에 첫 패턴이 선 틱(봇이 그것을 처음 보는 <c>Next</c> 의 <c>Ticks</c>)과 첫 패리 누름의 틱. <paramref name="lockAt"/> 이 있으면 패턴이 선 뒤
    /// 그만큼 지난 틱에 파이터를 <paramref name="lockTicks"/> 동안 붙든다. 누름은 <b>파이터가 실제로 패리에 든 틱</b>으로 센다.
    /// </summary>
    private static (int PatternStart, int? FirstParry, int WindowIn) Watch(
        BattleSim sim, FleetBot bot, int ticks, int? lockAt = null, int lockTicks = 0)
    {
        int? start = null, firstParry = null;
        int windowIn = -1;
        for (int i = 0; i < ticks; i++)
        {
            if (start is null && sim.Boss.CurrentPattern is not null)
            {
                start = sim.Ticks;
                windowIn = (int)System.Math.Round(sim.NextActiveIn!.Value / BattleSim.Dt);
            }

            if (start is { } s && lockAt is { } at && sim.Ticks == s + at)
            {
                sim.Fighter.Grab(0, lockTicks);
            }

            int now = sim.Ticks;
            sim.Tick(bot.Next(sim));
            if (firstParry is null && start is not null && sim.Fighter.Action == FighterAction.Parry)
            {
                firstParry = now;
            }
        }

        return (start ?? -1, firstParry, windowIn);
    }

    [Fact]
    public void 알아채기_전에는_안_누른다()
    {
        // 판정이 30틱 뒤인데 반응이 0.6초(36틱)면 판정이 선 뒤에야 누른다 — 빠른 공격에 늦는 사람. 휘두름이 몸에 안 닿게(사거리 1) 둬서 창이
        // 끝까지 산다 — 닿으면 그 틱에 판정이 끝나 누를 판정이 없어진다.
        (int start, int? late, _) = Watch(TestConfigs.SweepSim(1, 0.5), new FleetBot(Parrier(0.6), 1, FleetPlay.Beats, TestConfigs.Fighter()), 90);
        (late - start).ShouldBe(36, "반응 지연 전에 눌렀거나 늦게 눌렀다");

        // 반응이 넉넉하면 판정이 서는 틱에 누름이 먹게 그 앞 틱에 누른다 — Next 가 낸 입력은 다음 Tick 에 먹는다(아래 테스트).
        (int start2, int? onTime, int window) = Watch(TestConfigs.SweepSim(1, 0.5), new FleetBot(Parrier(0.2), 1, FleetPlay.Beats, TestConfigs.Fighter()), 90);
        (onTime - start2).ShouldBe(window - 1, "반응이 넉넉한데 판정이 서는 틱에 누름이 안 먹었다");
    }

    [Fact]
    public void 편향이_0인_누름은_판정이_서는_틱에_먹는다()
    {
        // 누르는 시각 = 판정 시각 + 편향 + 잡음(설계 §3.1) — 편향 0 · 잡음 0 이면 판정이 서는 틱에 행동이 서 있어야 한다. Next(N) 의 입력은 Tick 이
        // N + 1 로 올리며 먹으므로, 판정이 서는 틱에 누르면 한 틱 늦다: 바닥 전체를 치는 휘두름은 서는 틱에 닿아 대시가 무적을 못 댄다. 그 한 틱이
        // 모든 봇을 늦은 쪽으로 밀었고 엇박(1타가 3연격보다 9틱 늦다 · 패리 창 8틱)의 경계에 걸려 리듬형의 절반이 안 속았다(재 봄 · #108).
        BotTraits dasher = FleetPlay.Mid with
        {
            Dash = 1,
            Jump = 0,
            Parry = 0,
            Guard = 0,
            ReactionSeconds = 0.1,
            JitterSeconds = 0,
            BiasSeconds = 0,
            DashInward = 1,
            Greed = 0,
        };
        BattleSim sim = TestConfigs.SweepSim(5000, 0.125);
        var bot = new FleetBot(dasher, 1, FleetPlay.Beats, TestConfigs.Fighter());
        for (int i = 0; i < 120 && sim.Events.Count == 0; i++)
        {
            sim.Tick(bot.Next(sim));
        }

        sim.Events.ShouldNotBeEmpty("휘두름이 관측을 안 냈다");
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Dodged, "편향 0 의 대시가 판정이 서는 틱에 무적을 못 댔다");
    }

    [Fact]
    public void 리듬형은_기준의_박자에_누른다()
    {
        // 엇박 3연격의 첫 판정은 1.00초(60틱)이고 3연격의 박자는 0.85초(51틱)다. 리듬형은 51틱에 누르고 — 패리 창 0.133초 밖이라 커밋 안에서
        // 맞는다 — 눈으로 누르는 봇은 60틱에 누른다.
        string[] script = ["엇박 3연격"];
        (int start, int? rhythm, _) = Watch(FleetPlay.Sim(2, 3, script), FleetPlay.Bot(Parrier(0.15, rhythm: 1), 3), 400);
        (rhythm - start).ShouldNotBeNull().ShouldBe(50, "3연격의 1타(51틱)에 먹게 그 앞 틱에 누른다");

        (int start2, int? sight, int window) = Watch(FleetPlay.Sim(2, 3, script), FleetPlay.Bot(Parrier(0.15), 3), 400);
        (sight - start2).ShouldNotBeNull().ShouldBe(window - 1);
        window.ShouldBeInRange(59, 61, "엇박의 첫 판정이 1.00초가 아니다 — 시험이 가정한 타임라인이 바뀌었다");
    }

    /// <summary>패턴이 선 틱부터 센, 파이터가 패리에 든 틱들 — 누름마다 한 번.</summary>
    private static List<int> ParryStarts(BattleSim sim, FleetBot bot, int ticks)
    {
        var starts = new List<int>();
        int? start = null;
        bool parrying = false;
        for (int i = 0; i < ticks; i++)
        {
            start ??= sim.Boss.CurrentPattern is not null ? sim.Ticks : null;
            int now = sim.Ticks;
            sim.Tick(bot.Next(sim));
            bool nowParrying = sim.Fighter.Action == FighterAction.Parry;
            if (start is { } s && nowParrying && !parrying)
            {
                starts.Add(now - s);
            }

            parrying = nowParrying;
        }

        return starts;
    }

    [Fact]
    public void 리듬형은_다음_타의_선딜을_기다리지_않는다()
    {
        // 3연격의 2타는 1.55초(93틱)에 서고 그 선딜은 1.30초(78틱)에 보인다. 반응이 0.35초(21틱)인 사람이 눈으로 누르면 99틱 — 늦는다. 박자로
        // 누르는 사람은 패턴을 알아챈 뒤로는 선딜을 안 기다린다: 93틱이다. 리듬이 "판정을 눈으로 안 보고 박자로 누르는 몫" 인 이상(설계 §3.3) 박자의
        // 누름을 타마다의 반응에 묶으면 느린 리듬형은 엇박의 늦은 타를 우연히 받아친다 — 엇박이 노리는 사람이 원본에 안 선다(재 봄 · #108).
        // 사람이 멀리 있어 칼이 안 닿는 판이라 1타의 패리가 헛쳐 패턴이 끊기지 않는다.
        string[] script = ["3연격"];
        List<int> rhythm = ParryStarts(FleetPlay.Sim(2, 3, script), FleetPlay.Bot(Parrier(0.35, rhythm: 1), 3), 200);
        List<int> sight = ParryStarts(FleetPlay.Sim(2, 3, script), FleetPlay.Bot(Parrier(0.35), 3), 200);

        rhythm.Count.ShouldBeGreaterThanOrEqualTo(2, "리듬형이 두 타를 다 안 눌렀다");
        rhythm[0].ShouldBe(50);
        rhythm[1].ShouldBe(92, "리듬형의 2타가 박자(93틱에 먹게 92틱)가 아니다");
        sight.Count.ShouldBeGreaterThanOrEqualTo(2, "눈으로 누르는 봇이 두 타를 다 안 눌렀다");
        sight[1].ShouldBeInRange(98, 100, "눈으로 누르는 봇의 2타는 선딜(78) + 반응(21)이다");
    }

    [Fact]
    public void 굳어서_안_먹은_누름은_누른_것이_아니다()
    {
        // 누를 틱(판정이 서는 30틱)에 붙들려 있으면 누름이 안 먹는다 — "눌렀다" 로 적으면 그 판정의 회피를 통째로 건너뛴다. 풀린 틱(패턴 + 40)에
        // 창이 살아 있으니(0.5초) 그때 누른다.
        (int start, int? parry, _) = Watch(
            TestConfigs.SweepSim(1, 0.5), new FleetBot(Parrier(0.15), 1, FleetPlay.Beats, TestConfigs.Fighter()), 90, lockAt: 20, lockTicks: 20);

        (parry - start).ShouldNotBeNull().ShouldBeInRange(40, 42);
    }

    [Fact]
    public void 돌진_한_판을_예외_없이_끝낸다()
    {
        // 돌진 중에는 도착까지 시계가 서서 NextActiveIn 이 추정이다 — 봇은 누를 틱만 매 틱 다시 셈한다.
        (_, BattleSim sim) = FleetPlay.Play(BotTraits.Sample(51, 3, TestConfigs.Fleet()), 11, script: ["1타 돌진"]);

        sim.Events.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 바깥_대시는_돌아선_뒤_나간다()
    {
        // 대시는 바라보는 쪽으로만 간다(Fighter.Move). 바깥을 고른 봇은 한 틱 돌아서고 다음 틱에 누른다 — 그래야 방향 축이 음수로 선다.
        BotTraits outward = FleetPlay.Mid with { Dash = 1, Jump = 0, Parry = 0, Guard = 0, DashInward = 0, Greed = 0 };
        PlayerAxes axes = PlayerAxes.From(FleetPlay.Play(outward, 5).Sim.Events);

        axes.DashSamples.ShouldBeGreaterThan(0);
        axes.DashDirectionBias.ShouldBeLessThan(0);
    }

    [Fact]
    public void 같은_성향과_시드는_같은_판이다()
    {
        BotTraits traits = BotTraits.Sample(51, 5, TestConfigs.Fleet());
        BattleSim a = FleetPlay.Play(traits, 7).Sim;
        BattleSim b = FleetPlay.Play(traits, 7).Sim;

        a.Ticks.ShouldBe(b.Ticks);
        a.Fighter.Health.ShouldBe(b.Fighter.Health);
        a.Boss.Health.ShouldBe(b.Boss.Health);
        a.Events.SequenceEqual(b.Events).ShouldBeTrue("같은 성향 · 같은 시드인데 관측이 갈렸다");
    }

    [Fact]
    public void 모자란_누름을_안_한다()
    {
        // BotPolicy 와 같은 약속 — 값보다 많을 때만 누른다. 봇이 스스로 탈진하는 성향은 두지 않는다(설계 §13).
        foreach (int bot in Enumerable.Range(0, 8))
        {
            BattleSim sim = FleetPlay.Sim(1, (ulong)(100 + bot));
            FleetBot fleetBot = FleetPlay.Bot(BotTraits.Sample(51, bot, TestConfigs.Fleet()), (ulong)(100 + bot));
            BattleOutcome? outcome = null;
            while (outcome is null)
            {
                InputFrame input = fleetBot.Next(sim);
                if (input.Dash)
                {
                    sim.Fighter.Affords(FighterAction.Dash).ShouldBeTrue($"봇 {bot} · {sim.Ticks}틱: 모자란 대시");
                }

                if (input.Parry)
                {
                    sim.Fighter.Affords(FighterAction.Parry).ShouldBeTrue($"봇 {bot} · {sim.Ticks}틱: 모자란 패리");
                }

                if (input.Attack)
                {
                    sim.Fighter.Affords(FighterAction.Attack).ShouldBeTrue($"봇 {bot} · {sim.Ticks}틱: 모자란 칼질");
                }

                outcome = sim.Tick(input);
            }
        }
    }
}
