using System;
using System.Collections.Generic;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 파이터의 칼 (이슈 #59 · 설계 §5.1 · 1번 PR 이 넘긴 것). 칼은 <b>그림의 흰 궤적</b>에서 뽑은 모양으로 보스
/// 몸통에 대 보고, <b>판정 창 동안 산다</b>. 옛 칼은 높이를 안 보는 가로 거리 하나(<c>attack_reach</c>)였고
/// 창의 첫 틱에만 한 번 대 봤다 — 그래서 판정 보기에서 한 프레임만 번쩍였다.
/// </summary>
public class SwordTests
{
    /// <summary>실제 보스의 몸통 — 발 중심 <paramref name="x"/>, 바닥에서 키만큼.</summary>
    private static HitRect BossBody(double x)
    {
        BossConfig boss = TestConfigs.Boss();
        return new HitRect(x - boss.HalfWidth, x + boss.HalfWidth, 0, boss.Height);
    }

    [Theory]
    [InlineData("martial_hero/attack/4")]
    [InlineData("martial_hero/attack2/4")]
    public void 칼끝이_보스_몸통에_닿으면_맞고_반_px_밖이면_안_맞는다(string id)
    {
        // 경계는 그림이 정한다 — 칼의 앞끝은 모양 외곽 상자의 앞끝(X1)이고, 보스 몸통의 앞끝이 거기 닿으면 맞는다
        // (가장자리만 닿아도 겹친 것이다 — HitRect.Overlaps). 반 px 밖이면 멀어서 안 맞는다.
        HitShape sword = TestConfigs.HitShapes()[id];
        var at = new Placement(0, 0, 1);
        double touching = sword.Bounds.X1 + TestConfigs.Boss().HalfWidth;

        ShapeHit.Test(sword, at, BossBody(touching)).ShouldBe(ShapeContact.Overlap, $"{id}: 칼끝에 닿았는데 안 맞았다");
        ShapeHit.Test(sword, at, BossBody(touching + 0.5)).ShouldBe(ShapeContact.TooFar, $"{id}: 칼끝 밖인데 맞았다");
    }

    [Fact]
    public void 왼쪽을_보면_칼도_왼쪽을_벤다()
    {
        // 칼의 모양은 앞으로 길고(230) 뒤로 짧다(40). 보는 쪽이 판정에 안 실리면 등 뒤의 보스를 벤다.
        HitShape sword = TestConfigs.HitShapes()["martial_hero/attack/4"];
        var facingLeft = new Placement(0, 0, -1);
        double touching = sword.Bounds.X1 + TestConfigs.Boss().HalfWidth;

        ShapeHit.Test(sword, facingLeft, BossBody(-touching))
            .ShouldBe(ShapeContact.Overlap, "보는 쪽(왼쪽)의 보스를 못 벴다");
        ShapeHit.Test(sword, facingLeft, BossBody(touching))
            .ShouldBe(ShapeContact.TooFar, "등 뒤의 보스를 벴다 — 보는 쪽이 판정에 안 실렸다");
    }

    /// <summary>보스가 1px/틱으로 다가오기만 하는 판 — 패턴은 안 선다(간격 999초).</summary>
    private static BattleSim Approaching() => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighter(),
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 60, patternGap: 999),
        PatternIds = new[] { TestConfigs.SweepId },
        Patterns = new Dictionary<string, PatternDef> { [TestConfigs.SweepId] = TestConfigs.Sweep(100, 0) },
        Seed = 1,
        MaxTicks = 60 * 60,
    });

    /// <summary>보스와의 거리(중심 사이)가 <paramref name="gap"/> 안이 될 때까지 보스 쪽으로 걷는다.</summary>
    private static void WalkUpTo(BattleSim sim, double gap)
    {
        for (int i = 0; i < 600 && Math.Abs(sim.Fighter.X - sim.Boss.X) > gap; i++)
        {
            sim.Tick(new InputFrame((sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1), false, false, false, false));
        }
    }

    /// <summary>
    /// 칼질 하나를 끝까지 민다. 판정 창의 <b>몇 번째 틱</b>에 보스가 처음 맞았나를 돌려준다(안 맞았으면 −1).
    /// 한 틱을 민 뒤에 그 틱의 상태를 읽는다 — <c>AttackActive</c> 와 칼이 댄 결과가 같은 틱의 것이다.
    /// </summary>
    private static int SwingOnce(BattleSim sim)
    {
        int before = sim.Boss.Health, activeTicks = 0, landedAt = -1;
        sim.Tick(new InputFrame(0, false, false, false, Attack: true));
        for (int i = 0; i < 120 && sim.Fighter.Action == FighterAction.Attack; i++)
        {
            sim.Tick(default);
            if (!sim.Fighter.AttackActive)
            {
                continue;
            }

            activeTicks++;
            if (landedAt < 0 && sim.Boss.Health < before)
            {
                landedAt = activeTicks;
            }
        }

        return landedAt;
    }

    [Fact]
    public void 칼은_판정_창_동안_산다()
    {
        // 옛 칼은 판정 창의 첫 틱에만 대 봤다 — 그 틱에 1px 모자라면 창이 남아 있어도 헛쳤다.
        // 보스는 1px/틱으로 다가오고, 칼은 누른 틱부터 다섯째 틱에 선다(선딜 0.08 = 5틱 · 누른 틱도 센다).
        // 그 다섯 틱 동안 보스가 5px 오므로, 누르기 전 거리가 "닿는 거리 + 6" 이면 창의 첫 틱에는 1px 이 모자라고
        // 둘째 틱에 닿는다. 걸음(7px)과 보스(1px)가 정수 px 로 움직여 그 자리가 정확히 한 틱 선다.
        BattleSim sim = Approaching();
        double reach = TestConfigs.TestSword().Bounds.X1 + sim.Boss.HalfWidth;
        WalkUpTo(sim, reach + 40);
        for (int i = 0; i < 600 && Math.Abs(sim.Fighter.X - sim.Boss.X) - reach > 6; i++)
        {
            sim.Tick(default);
        }

        (Math.Abs(sim.Fighter.X - sim.Boss.X) - reach).ShouldBe(6, 1e-9, "누를 자리를 못 맞췄다 — 이 테스트의 기하가 움직였다");

        SwingOnce(sim).ShouldBe(2,
            "창의 첫 틱에 맞았거나(누른 자리가 틀렸다) 창 안에서 끝내 안 맞았다(칼이 창 동안 안 산다)");
    }

    [Fact]
    public void 한_번_휘두르면_보스는_한_번만_맞는다()
    {
        // 보스가 창 내내 칼 안에 있어도 한 번이다 — 보스의 휘두름과 같은 규칙(한 번 휘두르면 한 번만 맞는다).
        BattleSim sim = Approaching();
        WalkUpTo(sim, 120);
        int before = sim.Boss.Health;

        SwingOnce(sim).ShouldBe(1, "붙어 서 있는데 창의 첫 틱에 안 맞았다");
        (before - sim.Boss.Health).ShouldBe(TestConfigs.Fighter().Combo[0].Damage, "한 번 휘두른 칼이 여러 번 맞았다");
    }
}
