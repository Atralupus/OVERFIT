using System;
using System.Collections.Generic;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 보스의 움직임 (설계 §8.1) — 등록표와 도약(§4.2). 움직임은 파이터 없이 돈다: 틱마다 받은 자리에서 설 자리를 낸다.
/// </summary>
public class BossMotionTests
{
    /// <summary>실제 아레나(1920) · 보스 반폭 85 · 파이터 반폭 30 의 범위다.</summary>
    private static readonly MotionBounds _bounds = new(85, 1835, 115);

    private static IBossMotion Leap() =>
        BossMotions.Create(new MotionDef { Id = "leap", Height = 280, Air = 0.60 }, _bounds)
        ?? throw new Xunit.Sdk.XunitException("leap 이 등록표에 없다");

    [Fact]
    public void 도약은_뛰는_틱의_파이터_앞_몸_둘_폭에_36틱_뒤_내린다()
    {
        // 설계 §4.2 — 착지 자리는 뛰는 틱의 파이터 X(480)에서 보스 쪽으로 115 앞(595)이고, 그 뒤로는 파이터를 안 따라간다.
        // 높이는 4H·s(1−s) 라 s = 0.5(18틱)가 정점 280 이다. 뜬 시간 0.60초 = 36틱 뒤에 정확히 내린다.
        IBossMotion leap = Leap();

        MotionStep start = leap.Tick(new MotionContext(1440, 0, -1, 480, 0));
        start.X.ShouldBe(1440);
        start.Y.ShouldBe(0);
        start.Finished.ShouldBeFalse();
        start.HoldClock.ShouldBeFalse("도약은 패턴 시계를 안 세운다");

        MotionStep top = leap.Tick(new MotionContext(start.X, start.Y, start.Facing, 900, 18));
        top.Y.ShouldBe(280, 1e-9);
        top.X.ShouldBe((1440 + 595) / 2.0, 1e-9, "뛴 뒤에 움직인 파이터를 따라갔다");

        MotionStep land = leap.Tick(new MotionContext(top.X, top.Y, top.Facing, 900, 36));
        land.Finished.ShouldBeTrue();
        land.X.ShouldBe(595);
        land.Y.ShouldBe(0);
    }

    [Fact]
    public void 도약은_뛰는_틱에_착지_자리_쪽으로_돌아선다()
    {
        // 파이터가 보스 등 뒤(오른쪽 1400)로 넘어갔고 보스는 왼쪽을 본 채다. 착지 자리는 파이터의 왼쪽 115 = 1285 로
        // 보스보다 오른쪽이다 — 안 돌면 등 뒤로 나는 그림이 된다(설계 §4.2).
        IBossMotion leap = Leap();

        leap.Tick(new MotionContext(1000, 0, -1, 1400, 0)).Facing.ShouldBe(1);
        leap.Tick(new MotionContext(1000, 0, 1, 1400, 36)).X.ShouldBe(1285);
    }

    [Fact]
    public void 같은_X_면_보던_쪽을_쓰고_아레나_안으로_자른다()
    {
        // 보스와 파이터가 같은 X(100)이고 보스는 오른쪽을 본다 — 보스 쪽은 보는 쪽의 반대(왼쪽)라 착지 자리는 100 − 115 = −15,
        // 보스가 설 수 있는 왼끝 85 로 자른다.
        IBossMotion leap = Leap();

        leap.Tick(new MotionContext(100, 0, 1, 100, 0));
        leap.Tick(new MotionContext(100, 0, 1, 100, 36)).X.ShouldBe(85);
    }

    [Fact]
    public void 모르는_움직임은_세우지_않는다()
    {
        BossMotions.Ids.ShouldContain("leap");
        BossMotions.Create(new MotionDef { Id = "없는움직임" }, _bounds).ShouldBeNull();
    }

    /// <summary>
    /// 움직임이 시작된 뒤 <paramref name="held"/> 틱 동안 패턴 시계를 세우고, 그다음 틱에 끝나는 가짜 움직임 — 도착 시각이
    /// 파이터 자리에 달린 돌진(5번 PR · 설계 §4.6)의 뼈대다. 자리는 안 옮긴다.
    /// </summary>
    private sealed class Holding(int held) : IBossMotion
    {
        public MotionStep Tick(MotionContext context) =>
            new(context.BossX, context.BossY, 0, Finished: context.Tick >= held, HoldClock: context.Tick < held);
    }

    /// <summary>
    /// 시험 패턴(<see cref="TestConfigs.Sweep"/> — 판정 0.5초)의 판정 바로 앞에 <b>같은 시각</b>의 움직임 단계를 끼운 판.
    /// 간격 0.2초(12틱) 뒤 러너의 30번째 틱(판의 42틱)에 움직임 단계에 든다. 사거리 50 이라 판정은 파이터에 안 닿는다.
    /// </summary>
    private static BattleSim MotionBeforeSweep(string motionId, Func<MotionDef, MotionBounds, IBossMotion?>? motions)
    {
        PatternDef def = TestConfigs.Sweep(maxDistance: 50, activeSeconds: 0);
        def.Timeline.Insert(1, new PatternStep { T = 0.5, Kind = "windup", Motion = new MotionDef { Id = motionId } });
        return new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 0.2),
            PatternIds = new[] { TestConfigs.SweepId },
            Patterns = new Dictionary<string, PatternDef> { [TestConfigs.SweepId] = def },
            Seed = 1,
            MaxTicks = 60 * 30,
            Motions = motions,
        });
    }

    [Theory]
    [InlineData(0, 43)]
    [InlineData(5, 48)]
    public void 움직임이_세운_시계는_BattleSim_을_거쳐_러너에_닿는다(int held, int fires)
    {
        // 설계 §8.1 — "3번은 쓰는 움직임이 없으니 가짜 움직임 하나로 테스트한다". 러너가 HoldClock 을 따르는 것은
        // PatternRunnerTests 가 손으로 넘겨 보고, 여기서는 **움직임의 HoldClock 이 BattleSim 을 거쳐 다음 틱의 러너에 닿는지**를 본다.
        // 42틱에 움직임 단계에 들고(같은 틱에 움직임의 0번째 틱이 돈다) 0 ~ held−1 번째 틱이 시계를 세운다 — 러너는 43 ~ 42+held 틱을
        // 쉬고, 움직임이 끝난 틱(42+held = A)의 다음 틱 A + 1 에 판정이 선다(설계 §4.6). held 0 은 곧장 끝난 움직임이다.
        BattleSim sim = MotionBeforeSweep("가짜", (_, _) => new Holding(held));

        TestConfigs.UntilFired(sim);

        sim.Ticks.ShouldBe(fires, "움직임이 세운 시계가 러너에 안 닿았다 — 뒤 단계가 움직임을 안 기다린다");
    }

    [Fact]
    public void 등록표에_없는_움직임은_규칙_위반을_남기고_제자리에서_패턴을_마친다()
    {
        // 등록표에 없는 id 는 데이터 테스트가 먼저 막는다. 그래도 여기까지 오면 BattleSim 이 [E] 를 남기고, 보스는 움직이지 않은 채
        // 패턴을 끝까지 돈다 — 시계를 세울 움직임이 없으므로 판정은 움직임 단계의 다음 틱(43)에 선다.
        using var log = new LogCapture();
        BattleSim sim = MotionBeforeSweep("없는움직임", motions: null);
        double x = sim.Boss.X;

        TestConfigs.UntilFired(sim);

        sim.Ticks.ShouldBe(43);
        sim.Boss.X.ShouldBe(x);
        log.Lines.ShouldContain(l => l.StartsWith("[boss][E] motion_missing id=없는움직임 ", StringComparison.Ordinal));
        for (int i = 0; i < 120 && sim.Boss.CurrentPattern is not null; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.CurrentPattern.ShouldBeNull("움직임이 빠진 패턴이 안 끝난다");
    }
}
