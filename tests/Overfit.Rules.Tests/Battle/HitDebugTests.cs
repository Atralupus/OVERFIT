using System.Collections.Generic;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 판정 기록 (이슈 #59 · 설계 §6.1). 디버그 표시는 규칙이 <b>이 틱에 실제로 대 본</b> 사각형을 받아 그리기만 한다 —
/// 여기서 보는 것은 그 조회가 규칙이 댄 자리와 같은가다.
/// </summary>
public class HitDebugTests
{
    private static Placement BossAt(BattleSim sim) => new(sim.Boss.X, sim.Boss.Y, sim.Boss.Facing);

    [Fact]
    public void 쉬는_동안에는_보스_판정이_안_보인다()
    {
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0.5);
        sim.Tick(default);   // 패턴 간격(0.2초) 안 — 아직 아무 패턴도 안 섰다

        sim.BossTestedRects.ShouldBeEmpty();
        sim.BossNextRects.ShouldBeEmpty();
    }

    [Fact]
    public void 선딜_동안에는_다음_판정이_칠_자리가_보인다()
    {
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0.5);
        TestConfigs.UntilWindup(sim);

        sim.BossTestedRects.ShouldBeEmpty("아직 판정이 안 섰는데 댄 자리가 있다");
        sim.BossNextRects.ShouldBe(HitShape.Band(0, 5000, 0, 5000).Place(BossAt(sim)));
    }

    [Fact]
    public void 그림에서_뽑은_판정도_러너가_낼_그_모양이_다음_판정으로_보인다()
    {
        // 설계 §8.1 — 판정 단계가 hitbox id 를 가리키면 그 모양(hitboxes.json)으로 친다. 디버그 표시의 "다음 판정" 은 러너가 낼
        // 바로 그 판정을 그린다 — 판을 세울 때 지은 것이다(BossHits). 보스는 왼쪽을 보고 있으니 궤적이 왼쪽으로 뒤집혀 놓인다.
        HitShape swing = TestConfigs.HitShapes()["medieval_king/attack/2"];
        var pattern = new PatternDef
        {
            Tell = TestConfigs.Tell(),
            Tags = TestConfigs.Sweep(100, 0).Tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0.0, Kind = "windup" },
                new() { T = 0.85, Kind = "active", Hitbox = "medieval_king/attack/2", Damage = 8, ActiveSeconds = 0.125 },
                new() { T = 1.3, Kind = "end" },
            },
        };
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 0.2),
            PatternIds = new[] { "1타" },
            Patterns = new Dictionary<string, PatternDef> { ["1타"] = pattern },
            Seed = 1,
            MaxTicks = 60 * 30,
        });
        TestConfigs.UntilWindup(sim);

        sim.Boss.Facing.ShouldBe(-1);
        sim.BossNextRects.ShouldBe(swing.Place(BossAt(sim)));
    }

    [Fact]
    public void 판정이_선_틱에는_규칙이_댄_자리가_보이고_끝나면_사라진다()
    {
        // 파이터는 사거리 5000 안이라 판정이 서는 틱에 맞고 휘두름이 끝난다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0.5);
        TestConfigs.UntilFired(sim);

        sim.BossTestedRects.ShouldBe(HitShape.Band(0, 5000, 0, 5000).Place(BossAt(sim)));
        sim.Events.Count.ShouldBe(1, "선 틱에 맞았어야 한다");

        sim.Tick(default);
        sim.BossTestedRects.ShouldBeEmpty("맞고 끝난 휘두름이 다음 틱에도 그려진다");
    }

    [Fact]
    public void 창이_사는_동안에는_매_틱_보인다()
    {
        // 사거리 50 — 안 닿으므로 30틱 창 내내 대 본다. 선 틱이 첫 틱이다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 50, activeSeconds: 0.5);
        TestConfigs.UntilFired(sim);
        sim.BossTestedRects.Count.ShouldBe(2, "창이 막 섰는데 댄 자리가 안 보인다");

        for (int i = 0; i < 29; i++)
        {
            sim.Tick(default);
            sim.BossTestedRects.Count.ShouldBe(2, $"창 {i + 2}틱째인데 댄 자리가 안 보인다");
        }

        sim.Tick(default);
        sim.BossTestedRects.ShouldBeEmpty("창이 닫혔는데 그려진다");
    }

    [Fact]
    public void 파이터_칼은_판정_창_동안_보인다()
    {
        // 칼은 판정 창 동안 산다 (이슈 #59 · 2번 PR) — 옛 칼은 창의 첫 틱에만 대 봐서 한 프레임만 번쩍였다.
        // 보스는 960px 밖이라 안 닿는다: 안 닿은 칼은 창 내내 대 보고, 표시는 그 틱마다 보인다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0.5);
        sim.Tick(new InputFrame(0, false, false, false, true));

        int shown = 0, active = 0;
        for (int i = 0; i < 30; i++)
        {
            if (sim.Fighter.AttackActive)
            {
                active++;
            }

            if (sim.FighterTestedRects.Count > 0)
            {
                shown++;
                var at = new Placement(sim.Fighter.X, sim.Fighter.Y, sim.Fighter.Facing);
                sim.FighterTestedRects.ShouldBe(TestConfigs.TestSword().Place(at));
            }

            sim.Tick(default);
        }

        active.ShouldBeGreaterThan(1, "판정 창이 한 틱뿐이다 — 이 테스트가 창을 못 본다");
        shown.ShouldBe(active, "칼이 판정 창의 일부 틱에만 보인다 — 규칙이 창 내내 대 보지 않는다");
    }
}
