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
