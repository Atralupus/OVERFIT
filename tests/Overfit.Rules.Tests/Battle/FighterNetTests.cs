using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>파이터 망 — 칸 · 관측 · 망 조종기 (설계 2026-10-01 조각6 §1). 학습용 상대라 게임은 안 쓴다.</summary>
public class FighterNetTests
{
    [Fact]
    public void 칸은_열하나고_엣지는_첫_틱만_이동과_가드는_내내다()
    {
        FighterActions.Count.ShouldBe(11);
        InputFrame first = FighterActions.Input(FighterActions.JumpRight, 0);
        InputFrame later = FighterActions.Input(FighterActions.JumpRight, 3);
        (first.Jump, first.Move, later.Jump, later.Move).ShouldBe((true, (sbyte)1, false, (sbyte)1));
        (FighterActions.Input(FighterActions.DashLeft, 0).Dash, FighterActions.Input(FighterActions.DashLeft, 0).Move).ShouldBe((true, (sbyte)-1));
        FighterActions.Input(FighterActions.Guard, 5).GuardHeld.ShouldBeTrue();
        (FighterActions.Input(FighterActions.Attack, 0).Attack, FighterActions.Input(FighterActions.Attack, 1).Attack).ShouldBe((true, false));
        (FighterActions.Input(FighterActions.Bomb, 0).Bomb, FighterActions.Input(FighterActions.Bomb, 1).Bomb).ShouldBe((true, false));
        FighterActions.Input(FighterActions.Idle, 0).ShouldBe(default(InputFrame));
    }

    private static IReadOnlyList<string> Roster => StageRoster.For(TestConfigs.Stages(), 1);

    private static BattleSim Sim() => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter],
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(maxHealth: 999_999),
        PatternIds = Roster,
        Patterns = TestConfigs.Patterns(),
        Seed = 51,
        Picker = new ScriptPlanPicker(Roster, TestConfigs.Patterns(), [new ScriptPlan(0.4, "3연격")]),
        MaxTicks = TestConfigs.MaxTicks(),
    });

    [Fact]
    public void 망_조종기는_6틱마다_고르고_관측의_보스는_12틱_앞이다()
    {
        var driver = new FighterNetDriver(null, 7, Roster, arenaWidth: 1920);
        BattleSim sim = Sim();
        var bossX = new List<double> { sim.Boss.X };
        for (int t = 0; t < 120; t++)
        {
            sim.Tick(driver.Next(sim));
            bossX.Add(sim.Boss.X);
        }

        driver.Steps.Count.ShouldBe(20, "6틱마다 한 번이 아니다");
        driver.Steps.Select(s => s.Tick).ShouldBe(Enumerable.Range(0, 20).Select(k => k * 6));
        var obs = new FighterObservation(Roster.Count);
        driver.Steps[0].Observation.Count.ShouldBe(obs.Size);
        driver.Steps.ShouldAllBe(s => s.Mask[s.Action]);
    }

    [Fact]
    public void 남은_폭탄이_없으면_폭탄_칸을_가린다()
    {
        FighterNetDriver.Mask(bombsLeft: 0)[FighterActions.Bomb].ShouldBeFalse();
        FighterNetDriver.Mask(bombsLeft: 3).ShouldAllBe(m => m);
    }

    [Fact]
    public void 같은_시드면_같은_칸이다()
    {
        static IReadOnlyList<int> Run(ulong seed)
        {
            var d = new FighterNetDriver(null, seed, Roster, arenaWidth: 1920);
            BattleSim sim = Sim();
            for (int t = 0; t < 60; t++)
            {
                sim.Tick(d.Next(sim));
            }

            return d.Steps.Select(s => s.Action).ToArray();
        }

        Run(3).ShouldBe(Run(3));
        Run(3).ShouldNotBe(Run(4));
    }

    [Fact]
    public void 관측의_보스_자리는_12틱_앞의_자리다()
    {
        // 보스가 달려오는 판(대본: 0.4초 쉬고 달려 3연격) — 결정 t 의 관측 첫 칸 (보스 x − 파이터 x)/W 의 보스 x 는 t − 12 틱 뒤의 x 다.
        var driver = new FighterNetDriver(null, 1, Roster, arenaWidth: 1920);
        BattleSim sim = new(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter],
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = Roster,
            Patterns = TestConfigs.Patterns(),
            Seed = 51,
            Picker = new ScriptPlanPicker(Roster, TestConfigs.Patterns(), [new ScriptPlan(0.4, "3연격", Run: true)]),
            MaxTicks = TestConfigs.MaxTicks(),
        });
        var bossX = new Dictionary<int, double> { [0] = sim.Boss.X };
        var fighterX = new Dictionary<int, double> { [0] = sim.Fighter.X };
        for (int t = 1; t <= 60; t++)
        {
            sim.Tick(driver.Next(sim));
            bossX[sim.Ticks] = sim.Boss.X;
            fighterX[sim.Ticks] = sim.Fighter.X;
        }

        NetStep at48 = driver.Steps.Single(s => s.Tick == 48);
        at48.Observation[0].ShouldBe((bossX[36] - fighterX[48]) / 1920, 1e-12);
    }
}
