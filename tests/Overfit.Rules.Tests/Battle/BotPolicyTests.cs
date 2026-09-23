using System.Collections.Generic;
using System.IO;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class BotPolicyTests
{
    private static BattleSetup Setup(ulong seed = 51) => new()
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighter(),
        Boss = TestConfigs.Boss(),
        PatternIds = new[] { "내려찍기 3연", "이단 올려베기", "점프 강타" },
        Patterns = JsonData<PatternDef>.ParseTable(
            File.ReadAllText(Path.Combine("data", "patterns.json")), "patterns.json"),
        Seed = seed,
        MaxTicks = TestConfigs.MaxTicks(),
    };

    private static (BattleOutcome Outcome, BattleSim Sim) Play(ulong seed)
    {
        var sim = new BattleSim(Setup(seed));
        var bot = new BotPolicy(seed);
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(bot.Next(sim));
        }

        return (outcome.Value, sim);
    }

    [Fact]
    public void 봇이_한_판을_끝낸다()
    {
        // 데이터 공장의 전제다 — 한 판이 반드시 끝나야 수백만 판을 돌릴 수 있다.
        (BattleOutcome outcome, BattleSim sim) = Play(51);

        outcome.ShouldBeOneOf(BattleOutcome.Win, BattleOutcome.Lose);
        sim.Ticks.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 봇이_가만히_선_것보다는_잘한다()
    {
        // 봇이 아무 판단도 안 하면 학습 데이터가 "가만히 있으면 죽는다" 하나뿐이다.
        //
        // ⚠ "더 잘한다" 를 살아남은 틱 수로 재면 안 된다. 그건 양쪽이 다 지는 동안에만 맞는
        // 대리 지표이고, 봇이 실제로 이길 수 있게 되면 뒤집힌다 — **이기는 것은 천천히 죽는 것보다
        // 빠르다.** 그래서 결과를 먼저 보고, 결과가 같을 때만 시간을 본다.
        (BattleOutcome botOutcome, BattleSim botRun) = Play(51);

        var idle = new BattleSim(Setup());
        BattleOutcome? idleOutcome = null;
        while (idleOutcome is null)
        {
            idleOutcome = idle.Tick(default);
        }

        if (botOutcome == BattleOutcome.Win)
        {
            idleOutcome.Value.ShouldBe(BattleOutcome.Lose);
        }
        else
        {
            botRun.Ticks.ShouldBeGreaterThan(idle.Ticks);
        }
    }

    [Fact]
    public void 봇이_회피_수단_셋을_다_쓴다()
    {
        // 한 수단만 쓰는 봇은 나머지 축을 영원히 0 으로 만든다 — 그 데이터로는 개인화를 못 배운다.
        (_, BattleSim sim) = Play(51);
        var used = new HashSet<DodgeVerb>();
        foreach (DodgeEvent e in sim.Events)
        {
            used.Add(e.Verb);
        }

        used.ShouldContain(DodgeVerb.Dash);
        used.ShouldContain(DodgeVerb.Parry);
        used.ShouldContain(DodgeVerb.Jump);
    }

    [Fact]
    public void 봇도_차지를_낸다()
    {
        // **입력 계약은 사람과 봇이 같이 쓰는 통로다.** 사람만 차지할 수 있으면 나중에 망이
        // "차지가 없는 전투" 를 배우고, 그 데이터는 사람에게 아무 의미가 없다 (이슈 #40).
        // 그래서 봇이 실제로 모아서 휘두르는지를 본다 — 계약에 칸이 있는지가 아니라.
        var sim = new BattleSim(Setup(51));
        var bot = new BotPolicy(51);
        var swings = new HashSet<int>();
        bool charged = false;

        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(bot.Next(sim));
            charged |= sim.Fighter.Charging;

            // 칼이 나가는 틱의 단계가 곧 "얼마를 모아서 휘둘렀나" 다.
            if (sim.Fighter.AttackActive)
            {
                swings.Add(sim.Fighter.ChargeTier);
            }
        }

        charged.ShouldBeTrue("봇이 한 번도 안 모았다");
        swings.ShouldContain(0, "봇이 그냥 누르는 공격을 아예 안 낸다");
        swings.Count.ShouldBeGreaterThan(1, $"봇이 낸 차지 단계가 {string.Join(",", swings)} 뿐이다 — 한 종류면 차지가 데이터에 없는 것과 같다");
    }

    [Fact]
    public void 최대_차지가_패리_없이_닿는다()
    {
        // **차지는 패리와 상관없는 기술이다** (이슈 #40). 최대 차지가 백장의 빈 시간에 들어간다는
        // 것은 FighterDataTests 가 산수로 보지만, 산수는 "그런 자리가 있다" 까지만 말한다 —
        // 실제로 한 판을 돌려서 최대로 모은 칼이 보스에 닿는지는 여기서 본다.
        //
        // 이 봇은 정확 패리를 노리고 치지 않는다(회피 셋을 좌표로 고를 뿐이다). 그래서 여기서
        // 최대 차지가 나온다는 것은 **패리 없이도 닿는다**는 뜻이다.
        //
        // 시드를 여럿 도는 이유는 한 시드에 매다는 것이 증인을 하나만 세우는 일이기 때문이다 —
        // 봇은 단계를 좌표로 고르고 패턴 순서도 시드가 정하므로, 한 판에 최대 차지가 없는 것은
        // 흔한 일이고 그건 설계가 깨진 것이 아니다. 여덟 판을 다 뒤져도 한 번도 없다면 그때가
        // 진짜 빨개져야 하는 자리다.
        var landed = new HashSet<int>();
        foreach (ulong seed in new ulong[] { 7, 51, 99, 777, 2024, 31337, 12345, 8 })
        {
            var sim = new BattleSim(Setup(seed));
            var bot = new BotPolicy(seed);
            BattleOutcome? outcome = null;
            int lastBossHealth = sim.Boss.Health;

            while (outcome is null)
            {
                outcome = sim.Tick(bot.Next(sim));

                // 닿은 칼만 센다 — 휘두른 것이 아니라 보스 체력이 준 그 틱이다.
                if (sim.Boss.Health < lastBossHealth)
                {
                    landed.Add(sim.Fighter.ChargeTier);
                    lastBossHealth = sim.Boss.Health;
                }
            }
        }

        landed.ShouldContain(2,
            $"여덟 판 동안 최대 차지가 한 번도 안 닿았다 — 닿은 단계는 {string.Join(",", landed)} 뿐이다. "
            + "패리 없이는 최대 차지를 못 쓰는 상태라면 이 기술의 마지막 단계는 장식이다.");
    }

    [Fact]
    public void 같은_시드는_같은_판을_만든다()
    {
        (BattleOutcome a, BattleSim simA) = Play(51);
        (BattleOutcome b, BattleSim simB) = Play(51);

        a.ShouldBe(b);
        simA.Ticks.ShouldBe(simB.Ticks);
        simA.Events.Count.ShouldBe(simB.Events.Count);
    }

    [Fact]
    public void 다른_시드는_다른_판을_만든다()
    {
        (_, BattleSim a) = Play(51);
        (_, BattleSim b) = Play(777);

        a.Ticks.ShouldNotBe(b.Ticks);
    }
}
