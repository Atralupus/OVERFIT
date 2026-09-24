using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 판정 창 (이슈 #59 · 설계 §3.5) — 판정은 한 틱이 아니라 <b>창</b> 동안 산다. 창 안에서 몸에 닿으면
/// 그 휘두름은 끝나고(한 번만 맞는다), 창이 닫힐 때까지 안 닿으면 관측을 하나 남긴다.
/// </summary>
public class LiveSwingTests
{
    [Fact]
    public void 창은_초를_틱으로_반올림한다()
    {
        BattleSim.TicksFor(0).ShouldBe(1, "옛 패턴(창 0)은 한 틱이다");
        BattleSim.TicksFor(0.125).ShouldBe(8, "8fps 한 장 = 7.5틱 — 반올림은 한 방향으로");
        BattleSim.TicksFor(1.0 / 6.0).ShouldBe(10);
    }

    [Fact]
    public void 대시_무적이_창_중간에_풀리면_그_뒤에_맞는다()
    {
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0.5);
        TestConfigs.UntilNear(sim);

        sim.Tick(new InputFrame(0, false, true, false, false));   // 판정이 서기 한두 틱 전에 대시가 선다
        for (int i = 0; i < 40 && sim.Events.Count == 0; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(1, "한 번 휘두르면 관측은 하나다");
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Hit, "무적 8틱이 30틱 창보다 먼저 풀렸는데 안 맞았다");
    }

    [Fact]
    public void 창이_한_틱이면_같은_대시가_피한다()
    {
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0);
        TestConfigs.UntilNear(sim);

        sim.Tick(new InputFrame(0, false, true, false, false));
        for (int i = 0; i < 5 && sim.Events.Count == 0; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(1);
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Dodged, "옛 패턴의 한 틱 판정은 그대로 피해져야 한다");
    }

    [Fact]
    public void 한_번_휘두르면_한_번만_맞는다()
    {
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0.5);
        int before = sim.Fighter.Health;
        TestConfigs.UntilFired(sim);

        for (int i = 0; i < 40; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(1);
        sim.Fighter.Health.ShouldBe(before - 7, "30틱 동안 서 있었는데 두 번 이상 맞았다");
    }

    [Fact]
    public void 창이_닫힐_때까지_안_닿으면_빗나감_하나만_남긴다()
    {
        // 사거리 50 — 파이터(480)는 보스(1440)와 960 떨어져 한 번도 안 닿는다. 창은 30틱이다:
        // 선 틱이 첫 틱이고, 그 뒤 28틱은 조용하고, 그다음 틱(30번째)에 창이 닫히며 관측이 하나 나온다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 50, activeSeconds: 0.5);
        TestConfigs.UntilFired(sim);
        sim.Events.Count.ShouldBe(0, "창이 막 섰는데 관측이 먼저 나왔다");

        for (int i = 0; i < 28; i++)
        {
            sim.Tick(default);
            sim.Events.Count.ShouldBe(0, $"창이 살아 있는데({i + 2}틱째) 관측이 먼저 나왔다");
        }

        sim.Tick(default);   // 30번째 틱 — 창이 닫힌다
        sim.Events.Count.ShouldBe(1);
        sim.Events[0].Verdict.ShouldBe(HitVerdict.MissedTooFar);
    }

    [Fact]
    public void 패턴이_끝나는_틱에_선_판정도_제_이름으로_남는다()
    {
        // end 가 active 와 같은 0.5초다. 러너가 그 틱에 끝나 _current 와 CurrentPattern 이 지워져도,
        // 판정은 낼 때 잡아 둔 태그와 이름으로 대져야 한다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0, endAt: 0.5);
        TestConfigs.UntilFired(sim);   // 판정이 선 틱 — 러너도 그 틱에 끝났다

        sim.Events.Count.ShouldBe(1);
        sim.Events[0].PatternId.ShouldBe(TestConfigs.SweepId);
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Hit);
    }
}
