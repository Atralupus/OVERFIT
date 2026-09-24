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
    public void 파이터_칼은_규칙이_댄_틱에만_보인다()
    {
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0.5);
        sim.Tick(new InputFrame(0, false, false, false, true));

        int shown = 0;
        for (int i = 0; i < 30; i++)
        {
            if (sim.FighterTestedRects.Count > 0)
            {
                shown++;
                var at = new Placement(sim.Fighter.X, sim.Fighter.Y, sim.Fighter.Facing);
                sim.FighterTestedRects.ShouldBe(HitShape.Reach(TestConfigs.Fighter().AttackReach).Place(at));
            }

            sim.Tick(default);
        }

        shown.ShouldBe(1, "옛 칼 판정은 한 번 휘두를 때 한 틱만 댄다 — 그 틱에만 보여야 한다");
    }
}
