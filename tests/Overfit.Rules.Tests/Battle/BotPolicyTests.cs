using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(),
        PatternIds = StageRoster.For(TestConfigs.Stages(), 3),
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

    /// <summary>
    /// 여러 판에서 실제로 나온 회피 수단들.
    ///
    /// <para>
    /// ⚠ <b>증인을 여럿 세운다.</b> 한 판은 관측이 15건 안팎뿐이고 봇은 수단을 좌표로 고르므로,
    /// 어느 한 수단이 한 판에 안 나오는 것은 흔한 일이지 설계가 깨진 것이 아니다
    /// (<c>봇의_2타가_보스에_닿는다</c> 가 같은 이유로 같은 시드 목록을 돈다).
    /// 수단이 넷이 되면서(가드 · 이슈 #47) 한 판의 관측이 더 얇게 나뉘어, 시드 51 한 판은
    /// 실제로 점프 없이 끝난다 — <b>단언이 아니라 표본을 넓힌다.</b>
    /// </para>
    ///
    /// <para>
    /// 이슈 #54 에서 한 번 더 넓혔다(여덟 → 열여섯 판). 점프로 넘을 판정이 없어(이슈 #48) <c>Jump</c> 는
    /// "떴다가 떠 있는 채로 맞았다" 로만 실리는 가장 얇은 칸인데, 박자(1단계 간격 0.70 · 1.10)와 붕괴 고정
    /// (0.9 → 1.1)이 바뀌자 옛 여덟 판에서 그 칸이 <b>한 건(시드 12345) → 0 건</b>이 됐다. 봇은 여전히 판마다
    /// 열 번 넘게 뛰고, 늘린 여덟 판에는 네 건이 있다 — 봇이 점프를 안 쓰게 된 것이 아니라 증인이 얇았다.
    /// </para>
    /// </summary>
    private static HashSet<DodgeVerb> VerbsUsed()
    {
        var used = new HashSet<DodgeVerb>();
        foreach (ulong seed in _seeds)
        {
            (_, BattleSim sim) = Play(seed);
            foreach (DodgeEvent e in sim.Events)
            {
                used.Add(e.Verb);
            }
        }

        return used;
    }

    /// <summary>봇을 여러 판 돌려볼 시드들. 한 판의 주사위에 매달지 않기 위한 목록이다.</summary>
    private static readonly ulong[] _seeds = { 7, 51, 99, 777, 2024, 31337, 12345, 8, 1, 2, 3, 4, 5, 6, 9, 10 };

    [Fact]
    public void 봇이_회피_수단_셋을_다_쓴다()
    {
        // 한 수단만 쓰는 봇은 나머지 축을 영원히 0 으로 만든다 — 그 데이터로는 개인화를 못 배운다.
        HashSet<DodgeVerb> used = VerbsUsed();

        used.ShouldContain(DodgeVerb.Dash);
        used.ShouldContain(DodgeVerb.Parry);
        used.ShouldContain(DodgeVerb.Jump);
    }

    [Fact]
    public void 봇도_2연격을_낸다()
    {
        // **입력 계약은 사람과 봇이 같이 쓰는 통로다** (설계 §5.4). 봇이 1타만 치면 망은 "2타가 없는 전투" 를 배우고,
        // 그 데이터는 사람에게 아무 의미가 없다. 칼이 나가는 틱의 칼질 번호를 모은다.
        var steps = new HashSet<int>();
        foreach (ulong seed in _seeds.Take(4))
        {
            var sim = new BattleSim(Setup(seed));
            var bot = new BotPolicy(seed);
            BattleOutcome? outcome = null;
            while (outcome is null)
            {
                outcome = sim.Tick(bot.Next(sim));
                if (sim.Fighter.AttackActive)
                {
                    steps.Add(sim.Fighter.ComboStep);
                }
            }
        }

        steps.ShouldContain(0, "봇이 1타를 안 낸다");
        steps.ShouldContain(1, "봇이 2타를 한 번도 안 이었다 — 2연격이 데이터에 없다");
    }

    [Fact]
    public void 봇의_2타가_보스에_닿는다()
    {
        // 2타는 1초를 서 있는 무거운 칼이다. 이어 놓고 한 번도 안 닿으면 그 칸은 데이터에 벌만 남는다 —
        // 닿은 칼만 센다(휘두른 것이 아니라 보스 체력이 준 그 틱이다).
        bool landed = false;
        foreach (ulong seed in _seeds)
        {
            var sim = new BattleSim(Setup(seed));
            var bot = new BotPolicy(seed);
            BattleOutcome? outcome = null;
            int lastBossHealth = sim.Boss.Health;
            while (outcome is null && !landed)
            {
                outcome = sim.Tick(bot.Next(sim));
                landed = sim.Boss.Health < lastBossHealth && sim.Fighter.ComboStep == 1;
                lastBossHealth = sim.Boss.Health;
            }

            if (landed)
            {
                break;
            }
        }

        landed.ShouldBeTrue("열여섯 판 동안 2타가 한 번도 안 닿았다 — 2타는 장식이다");
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

    [Fact]
    public void 봇도_가드를_낸다()
    {
        // **사람과 봇이 같은 통로를 타야 학습 데이터가 뜻을 가진다** (이슈 #47 · 설계 §5.4). 봇이 못 내는 기술은 봇 함대가
        // 만드는 데이터에 영영 안 들어가고, 망은 그 기술이 없는 게임을 배운다. 가드는 이제 ↓ 를 **누르고 있는 동안**이라
        // (설계 §5.2) 봇이 레벨(GuardHeld)을 실제로 내는지를 본다.
        VerbsUsed().ShouldContain(DodgeVerb.Guard, "봇이 한 번도 안 막았다");
    }

    [Fact]
    public void 봇의_가드가_막아내기도_하고_깨지기도_한다()
    {
        // 개수 둘(GuardSamples · GuardBrokenSamples)이 갈리는지는 PlayerAxesTests 가 보지만,
        // **실제 전투에서 두 결과가 다 나오는지**는 여기서만 보인다. 한쪽만 나오면 그 칸은
        // 데이터에 늘 0 이고, 그러면 개수를 둘로 나눈 것이 아무 일도 안 한 셈이다.
        var verdicts = new HashSet<HitVerdict>();
        foreach (ulong seed in _seeds)
        {
            (_, BattleSim sim) = Play(seed);
            foreach (DodgeEvent e in sim.Events)
            {
                verdicts.Add(e.Verdict);
            }
        }

        verdicts.ShouldContain(HitVerdict.Guarded, "봇의 가드가 한 번도 안 버텼다");
        verdicts.ShouldContain(HitVerdict.GuardBroken, "봇의 가드가 한 번도 안 깨졌다");
    }

    [Fact]
    public void 산_창이_있는_동안_봇은_가드를_놓지_않고_칼질을_새로_누르지_않는다()
    {
        // 설계 §3.6 ④ — 봇은 창이 살아 있는 동안을 "판정이 지금" 으로 본다. NextActiveIn 만 보던 때는 판정이 서는 틱에 그 값이
        // null 이 되어 봇이 창의 첫 틱에 가드를 풀고 칼을 눌렀다 — 창이 8틱이면 남은 틱에 맞는다. 여기 판정은 멀리(사거리 50)
        // 서서 파이터에게 안 닿은 채 창 30틱을 다 산다. 1타 도중에 2타를 잇는 누름은 새 칼질이 아니다.
        int live = 0, guardedLive = 0;
        foreach (ulong seed in _seeds.Take(4))
        {
            BattleSim sim = TestConfigs.SweepSim(maxDistance: 50, activeSeconds: 0.5);
            var bot = new BotPolicy(seed);
            bool guarding = false;
            for (int t = 0; t < 60 * 20; t++)
            {
                bool swingLive = sim.SwingLive;
                bool attacking = sim.Fighter.Action == FighterAction.Attack;
                InputFrame input = bot.Next(sim);
                if (swingLive)
                {
                    live++;
                    if (!attacking)
                    {
                        input.Attack.ShouldBeFalse("산 창 안에서 칼질을 새로 눌렀다");
                    }

                    if (guarding)
                    {
                        input.GuardHeld.ShouldBeTrue("가드로 받기로 한 창 안에서 가드를 놓았다");
                        guardedLive++;
                    }
                }
                else
                {
                    guarding = input.GuardHeld;
                }

                sim.Tick(input);
            }
        }

        live.ShouldBeGreaterThan(0, "산 창이 한 번도 없었다 — 이 테스트가 아무것도 안 본다");
        guardedLive.ShouldBeGreaterThan(0, "가드로 받기로 한 창이 한 번도 없었다 — 이 테스트가 가드를 안 본다");
    }
}
