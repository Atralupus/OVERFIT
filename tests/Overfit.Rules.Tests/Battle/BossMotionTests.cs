using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 보스의 움직임 (설계 §8.1) — 등록표와 도약(§4.2) · 돌진(§4.6 · #78). 움직임은 파이터 없이 돈다: 틱마다 받은 자리에서 설 자리를 낸다.
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
        BossMotions.Ids.ShouldContain("rush");
        BossMotions.Create(new MotionDef { Id = "없는움직임" }, _bounds).ShouldBeNull();
    }

    /// <summary>
    /// 움직임이 시작된 뒤 <paramref name="held"/> 틱 동안 패턴 시계를 세우고, 그다음 틱에 끝나는 가짜 움직임 — 도착 시각이
    /// 파이터 자리에 달린 돌진(설계 §4.6)의 뼈대다. 자리는 안 옮긴다.
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
        return TestConfigs.PatternSim(TestConfigs.SweepId, def, maxTicks: 60 * 30, motions: motions);
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

    /// <summary>끝났다고 말하면서 시계도 세우겠다는 가짜 움직임 — 둘이 부딪칠 때 무엇이 이기나를 본다.</summary>
    private sealed class FinishedButHolding : IBossMotion
    {
        public MotionStep Tick(MotionContext context) =>
            new(context.BossX, context.BossY, 0, Finished: true, HoldClock: true, HoldTicks: 99);
    }

    [Fact]
    public void 끝난_움직임은_시계를_세운다고_해도_안_세운다()
    {
        // BattleSim.Move 의 `_holdClock = !step.Finished && step.HoldClock` (#59 의 3/6 넘김 — 돌진은 HoldClock 을 참으로 내는 첫 움직임이다).
        // 끝난 움직임은 걷히므로 다음 틱에 시계를 풀어 줄 자리가 없다 — 끝난 틱의 HoldClock 을 따르면 시계가 영영 선다. 곧장 끝난 움직임
        // (held 0 · 43틱)과 같은 틱에 판정이 서야 한다. 남은 틱(HoldTicks)도 다음 판정까지 남은 시간에 안 들어간다.
        BattleSim sim = MotionBeforeSweep("가짜", (_, _) => new FinishedButHolding());
        TestConfigs.UntilWindup(sim);
        TestConfigs.UntilTick(sim, 42);

        sim.NextActiveIn.ShouldNotBeNull();
        sim.NextActiveIn.Value.ShouldBeLessThan(2 * BattleSim.Dt, "끝난 움직임의 남은 틱(99)이 다음 판정까지 남은 시간에 들었다");
        TestConfigs.UntilFired(sim);

        sim.Ticks.ShouldBe(43, "끝난 움직임이 세운다고 한 시계를 따랐다");
    }

    [Fact]
    public void 보스는_아레나_밖이나_땅_밑으로_안_간다()
    {
        // Boss.Move 의 경계 (#59 의 3/6 넘김) — 움직임이 낸 자리를 보스가 설 수 있는 범위(반폭 ~ 아레나 폭 − 반폭)로 자르고, 발은 바닥 아래로
        // 안 간다. 움직임이 그 밖을 내도 보스는 경계에 선다 — 돌진은 목표를 먼저 잘라 경계에서 끝난다(아래 테스트).
        var boss = new Boss(TestConfigs.Boss(), TestConfigs.Arena(), 960);

        boss.Move(-500, -40, 0);
        (boss.X, boss.Y).ShouldBe((85.0, 0.0));
        boss.Move(5000, 120, 0);
        (boss.X, boss.Y).ShouldBe((1835.0, 120.0));
    }

    private static IBossMotion Rush(double stop = 280, double speed = 3600) =>
        BossMotions.Create(new MotionDef { Id = "rush", Speed = speed, Stop = stop }, _bounds)
        ?? throw new Xunit.Sdk.XunitException("rush 가 등록표에 없다");

    /// <summary>돌진을 끝날 때까지(상한 200틱) 민다 — 틱마다의 한 걸음. 파이터는 제자리다.</summary>
    private static List<MotionStep> RushAll(IBossMotion rush, double bossX, int facing, double fighterX)
    {
        var steps = new List<MotionStep>();
        double x = bossX;
        for (int t = 0; t < 200; t++)
        {
            MotionStep step = rush.Tick(new MotionContext(x, 0, facing, fighterX, t));
            steps.Add(step);
            x = step.X;
            if (step.Finished)
            {
                break;
            }
        }

        steps[^1].Finished.ShouldBeTrue("돌진이 200틱 안에 안 끝났다");
        return steps;
    }

    [Fact]
    public void 돌진은_틱당_60px_로_다가가_파이터_앞_280_에_닿을_때까지_시계를_세운다()
    {
        // 설계 §4.6 — 3600 px/s 는 틱당 정확히 60px 다. 보스 1312 · 파이터 480 이면 d = 832 — 멈출 자리까지 552 는 ⌈552 / 60⌉ = 10틱이다
        // (9 × 60 + 12 · 끝 걸음이 짧은 자리를 일부러 고른 값이다 — 옛 판에서 쉬는 동안 보스가 128px 걸어 온 자리였다). 닿는 틱까지 시계를 세우고(HoldClock), 닿는 틱에 끝난다. 남은 틱(HoldTicks)은
        // 그 걸음 뒤 지금 자리에서 잰 ⌈(d − 280) / 60⌉ 이다 — 다음 판정까지 남은 시간이 그것을 더한다.
        List<MotionStep> steps = RushAll(Rush(), 1312, -1, 480);

        steps.Count.ShouldBe(10);
        steps.Select(s => s.X).ShouldBe(new double[] { 1252, 1192, 1132, 1072, 1012, 952, 892, 832, 772, 760 });
        steps.Select(s => s.HoldClock).ShouldBe(Enumerable.Repeat(true, 9).Append(false));
        steps.Select(s => s.HoldTicks).ShouldBe(new[] { 9, 8, 7, 6, 5, 4, 3, 2, 1, 0 });
        steps[^1].Finished.ShouldBeTrue();
        steps.ShouldAllBe(s => s.Y == 0 && s.Facing == 0, "돌진이 뜨거나 돌아섰다 — 패턴 중 잠금");
        steps.ShouldAllBe(s => s.GoalX == 760, "도착할 자리가 파이터 앞 280 이 아니다");
    }

    [Theory]
    [InlineData(1100, 900)]
    [InlineData(1100, 1140)]
    public void 이미_280_안이거나_등_뒤면_돌진은_0틱이고_뒤로_안_간다(double bossX, double fighterX)
    {
        // 설계 §4.6 — 이미 d ≤ 280 이면(붙어 있던 사람 · d = 200) 돌진은 0틱이고 3타가 곧장 온다. d < 0(1타 동안 보스를 뚫고 지나가 등 뒤에
        // 선 사람 · d = −40)도 0틱이다 — 보스는 뒤로 안 가고 돌아서지도 않는다. 3타는 앞으로 헛친다.
        MotionStep step = Rush().Tick(new MotionContext(bossX, 0, -1, fighterX, 0));

        step.Finished.ShouldBeTrue();
        step.HoldClock.ShouldBeFalse();
        step.X.ShouldBe(bossX, "이미 닿은 돌진이 움직였다");
        step.Facing.ShouldBe(0, "돌진이 돌아섰다");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3600)]
    public void 빠르기가_0_이하면_돌진은_규칙_위반을_남기고_그_틱에_끝나고_시계를_안_세운다(double speed)
    {
        // 빠르기가 0 이하면 멀리 있는 파이터에게도 그 틱에 끝난다 — 안 막으면 남은 틱(⌈남은 거리 / 0⌉)이 쓰레기가 되고 시계가 영영 서
        // 패턴이 안 끝난다. 뒤로도 안 간다. 데이터의 빠르기는 데이터 테스트가 보지만 그것은 patterns.json 만 막는다 — 움직임 자신의 약속은
        // 여기서 못박는다. 조용히 끝나면 안 된다: 0 이하의 빠르기는 규칙 위반이라(CLAUDE.md §5 — 음수 값) 등록표에 없는 움직임(motion_missing)과
        // 같이 [E] 를 한 줄 남긴다(#96). 헤드리스 판정이 그 한 줄로 떨어진다.
        using var log = new LogCapture();
        MotionStep step = Rush(speed: speed).Tick(new MotionContext(1312, 0, -1, 480, 0));

        step.Finished.ShouldBeTrue("빠르기가 0 이하인 돌진이 안 끝났다");
        step.HoldClock.ShouldBeFalse("빠르기가 0 이하인 돌진이 시계를 세웠다");
        step.X.ShouldBe(1312, "빠르기가 0 이하인 돌진이 움직였다");
        log.Lines.Where(l => l.StartsWith("[boss][E] ", StringComparison.Ordinal))
            .ShouldBe(new[] { $"[boss][E] rush_speed_invalid speed={speed} stop=280 boss_x=1312 fighter_x=480" }, "빠르기가 0 이하인 돌진이 조용히 끝났다");
    }

    [Fact]
    public void 제대로_된_돌진은_규칙_위반을_안_남긴다()
    {
        // 위 [E] 의 대조군 — 이미 닿은 돌진(0틱)도 끝까지 달린 돌진도 [E] 가 아니다. 0틱은 붙어 있던 사람 · 등 뒤로 간 사람에게 늘 난다(설계 §4.6).
        using var log = new LogCapture();
        RushAll(Rush(), 1312, -1, 480);
        Rush().Tick(new MotionContext(1100, 0, -1, 900, 0)).Finished.ShouldBeTrue();

        log.Lines.ShouldNotContain(l => l.StartsWith("[boss][E] ", StringComparison.Ordinal));
    }

    [Fact]
    public void 멈출_자리가_경계_밖이면_돌진은_경계에서_끝난다()
    {
        // 파이터가 벽(30)에 붙으면 보스는 310 에 선다 — 경계(85) 안이다(설계 §4.6). 멈출 거리가 0 이면 멈출 자리(30)가 경계 밖이다: 목표를
        // 경계로 자르지 않으면 Boss.Move 가 85 로 되돌리는 동안 돌진은 매 틱 "아직 55 남았다" 를 내고 시계가 영영 선다(#59 의 3/6 넘김).
        RushAll(Rush(), 1835, -1, 30)[^1].X.ShouldBe(310);

        List<MotionStep> walled = RushAll(Rush(stop: 0), 1835, -1, 30);
        walled[^1].Finished.ShouldBeTrue("경계 밖을 겨냥한 돌진이 안 끝난다");
        walled[^1].X.ShouldBe(85);
    }

    /// <summary>
    /// 돌진 하나짜리 시험 패턴 — 0.5초에 돌진하고 **같은 시각**의 선딜을 거쳐 0.75초에 3타(실제 모양)를 친다. 도착한 틱 A 의 다음 틱부터
    /// 시계가 다시 가므로 3타는 늘 A + 15 에 선다(설계 §4.6 의 1.300 → 1.550 과 같은 15틱). 보스는 안 걷는다. <paramref name="fighter"/> ·
    /// <paramref name="stop"/> 은 달리는 보스에게 칼을 넣어 보는 테스트만 준다.
    /// </summary>
    private static BattleSim RushSim(FighterConfig? fighter = null, double stop = 280)
    {
        PatternDef def = TestConfigs.Sweep(maxDistance: 50, activeSeconds: 0.125);
        def.Timeline.Clear();
        def.Timeline.Add(new PatternStep { T = 0.0, Kind = "windup" });
        def.Timeline.Add(new PatternStep { T = 0.5, Kind = "windup", Motion = new MotionDef { Id = "rush", Speed = 3600, Stop = stop } });
        def.Timeline.Add(new PatternStep { T = 0.5, Kind = "windup" });
        def.Timeline.Add(new PatternStep
        {
            T = 0.75,
            Kind = "active",
            Hitbox = "medieval_king/attack3/2",
            Damage = 14,
            ActiveSeconds = 0.125,
        });
        def.Timeline.Add(new PatternStep { T = 1.25, Kind = "end" });
        return TestConfigs.PatternSim(TestConfigs.SweepId, def, fighter, maxTicks: 60 * 30);
    }

    [Fact]
    public void 돌진_중_다음_판정까지는_남은_돌진과_3타의_선딜이다()
    {
        // 설계 §4.6 — 다음 판정까지 남은 시간은 돌진 중에 "지금 자리에서 닿기까지 남은 틱 ⌈max(0, d − S) / 60⌉ / 60 + 선딜" 이다. 파이터가
        // 가만히 서 있으면 추정이 곧 사실이다: 판 42틱(간격 12 + 러너 30)에 돌진이 서고 d = 960 − 280 = 680 은 ⌈680 / 60⌉ = 12틱이다 — 11번 걸음
        // 뒤 53틱에 닿고, 3타는 그 15틱 뒤 68틱에 선다. 돌진이 서는 틱부터 틱마다 남은 시간이 정확히 한 틱씩 준다.
        BattleSim sim = RushSim();
        TestConfigs.UntilTick(sim, 42);

        for (int i = 0; i < 40 && sim.NextActiveIn is not null; i++)
        {
            sim.NextActiveIn.Value.ShouldBe((68 - sim.Ticks) * BattleSim.Dt, 1e-9, $"{sim.Ticks}틱: 남은 시간이 돌진을 안 셌다");
            sim.Tick(default);
        }

        sim.Ticks.ShouldBe(68, "3타가 도착 뒤 15틱에 안 섰다");
        sim.Boss.X.ShouldBe(sim.Fighter.X + 280, 1e-9, "보스가 파이터 앞 280 에 안 섰다");
    }

    [Fact]
    public void 돌진은_다가오는_파이터_앞_243_3_에서_280_사이에_멈춘다()
    {
        // 설계 §4.6 — 돌진 도중에는 파이터가 등 뒤로 못 간다. 보스가 움직인 뒤 d ≥ 280 이고 파이터는 한 틱에 많아야 36.7px(대시) 다가오므로
        // 다음 틱의 d 는 243.3 이상이다 — 그러면 d ≤ 280 이라 그 틱에 돌진이 끝난다. 돌진 도중 어느 틱에 보스 쪽으로 대시해도 멈춘 자리의
        // d 가 [243.3, 280] 이다. 대시는 바라보는 쪽으로만 가므로 파이터는 처음부터 보스 쪽(오른쪽)을 본다.
        for (int lead = 0; lead < 12; lead++)
        {
            BattleSim sim = RushSim();
            TestConfigs.UntilTick(sim, 42 + lead);

            sim.Tick(new InputFrame(0, false, Dash: true, false, false));
            for (int i = 0; i < 40 && sim.Boss.CurrentPattern is not null && sim.NextActiveIn is > 15 * BattleSim.Dt + 1e-9; i++)
            {
                sim.Tick(default);
            }

            sim.NextActiveIn.ShouldNotBeNull($"{lead}틱 뒤의 대시 — 돌진이 닿기 전에 패턴이 끝났다");
            sim.NextActiveIn.Value.ShouldBeLessThanOrEqualTo(15 * BattleSim.Dt + 1e-9, $"{lead}틱 뒤의 대시 — 40틱 안에 돌진이 안 닿았다");
            double d = sim.Boss.X - sim.Fighter.X;
            d.ShouldBeInRange(280 - (2200 * BattleSim.Dt) - 1e-9, 280 + 1e-9, $"{lead}틱 뒤의 대시 — 돌진이 d = {d} 에 멈췄다");
        }
    }

    [Fact]
    public void 움직이는_동안_다음_판정의_미리보기는_도착할_자리의_땅에_선다()
    {
        // #59 의 3/6 넘김 — 판정 보기의 "다음 판정" 은 선딜 중 다음 판정이 칠 자리다(설계 §6.1). 움직이는 패턴에서 그 자리는 지금 보스가 선
        // 곳이 아니라 **움직임이 끝날 곳**이다: 도약은 뛰는 틱에 정한 착지 자리의 땅, 돌진은 지금 파이터 앞 280 의 땅. 지금 자리에 놓으면
        // 도약의 착지 띠가 공중에 뜨고 돌진의 3타가 달리는 보스를 따라 미끄러진다. 움직임이 없으면 보스 자리 그대로다.
        BattleSim sim = RushSim();
        TestConfigs.UntilTick(sim, 43);

        HitShape third = TestConfigs.HitShapes()["medieval_king/attack3/2"];
        sim.Boss.X.ShouldBeGreaterThan(sim.Fighter.X + 280, "돌진이 벌써 닿았다 — 이 테스트가 움직이는 동안을 안 본다");
        sim.BossNextRects.ShouldBe(third.Place(new Placement(sim.Fighter.X + 280, 0, sim.Boss.Facing)), "돌진의 3타가 도착할 자리에 안 섰다");

        BattleSim leap = new(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, rest: 0.2),
            PatternIds = new[] { "점프 공격" },
            Patterns = TestConfigs.Patterns(),
            Seed = 1,
            MaxTicks = 60 * 30,
        });
        for (int i = 0; i < 600 && leap.Boss.Y < 100; i++)
        {
            leap.Tick(default);
        }

        leap.Boss.Y.ShouldBeGreaterThan(100, "보스가 안 떴다");
        leap.BossNextRects.ShouldNotBeEmpty();
        leap.BossNextRects.ShouldAllBe(r => r.Y0 == 0, "착지 띠의 미리보기가 공중에 떴다");
        leap.BossNextRects.Min(r => r.X0).ShouldBe(leap.Fighter.X + 115 - 1920, 1e-9, "착지 띠가 착지 자리에 안 섰다");
    }

    [Fact]
    public void 돌진_도중_게이지로_무너지면_그_자리에_서고_다음_패턴은_간격_뒤에_처음부터_선다()
    {
        // Review Focus 3 — 달려오는 보스에게 칼을 넣어 게이지로 무너뜨린다. 탈진은 하던 패턴을 끊는다(설계 §4.3): 땅을 가는 돌진은 무너진 그
        // 자리에서 멈추고(공중이 아니라 내릴 것이 없다 · #71 계획 결정 6), 3타는 안 온다. 세웠던 패턴 시계는 같이 걷힌다 — 남으면 풀린 뒤 고른
        // 다음 패턴의 러너가 한 틱도 못 가 판정이 영영 안 선다. 멈출 거리를 100 으로 줄인 판이다: 기준 파이터의 칼(±90)이 닿으려면 보스가
        // 파이터 앞 175 안으로 와야 한다. 칼이 선 틱에 보스가 아직 멈출 자리(파이터 앞 100)에 안 닿았어야 이 테스트가 달리는 보스를 본다.
        BattleSim sim = RushSim(TestConfigs.Breaker(), stop: 100);
        bool pressed = false;
        for (int i = 0; i < 600 && !sim.Boss.Exhausted; i++)
        {
            bool press = !pressed && sim.Boss.CurrentPattern is not null && sim.Boss.X - sim.Fighter.X <= 85 + 90 + (5 * 60);
            pressed |= press;
            sim.Tick(new InputFrame(0, false, false, false, Attack: press));
        }

        sim.Boss.Exhausted.ShouldBeTrue("달리는 보스를 못 무너뜨렸다");
        double x = sim.Boss.X;
        (x - sim.Fighter.X).ShouldBeGreaterThan(100, "보스가 멈출 자리에 닿은 뒤에 무너졌다 — 이 테스트가 달리는 보스를 안 본다");
        sim.NextActiveIn.ShouldBeNull("무너졌는데 3타가 남았다");
        for (int i = 0; i < 60; i++)
        {
            sim.Tick(default);
            sim.Boss.X.ShouldBe(x, "무너진 보스가 계속 달렸다");
        }

        sim.Events.ShouldBeEmpty("무너졌는데 3타의 관측이 섰다");
        for (int i = 0; i < 600 && sim.Boss.Exhausted; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.Exhausted.ShouldBeFalse("600틱 안에 탈진이 안 풀렸다");

        // 간격(0.2초 = 12틱)을 처음부터 세어 새 패턴이 서고(탈진이 풀리는 틱이 간격의 첫 틱이다 · BossPoiseTests 와 같은 규약), 그다음 틱부터
        // 시계가 간다 — 판정(0.75초 = 45틱)까지 44틱이 남는다.
        int free = sim.Ticks;
        for (int i = 0; i < 600 && sim.Boss.CurrentPattern is null; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.CurrentPattern.ShouldNotBeNull("풀린 뒤 600틱 안에 새 패턴이 안 섰다");
        (sim.Ticks - free + 1).ShouldBe(BattleSim.TicksFor(0.2), "풀린 뒤 간격을 처음부터 안 셌다");

        // 새 패턴이 선 그 틱에는 움직임이 한 번도 안 돌았다(패턴이 서는 틱은 러너도 움직임도 안 민다) — 남은 시간은 러너의 것뿐이다. 끊긴 돌진이
        // 세운 시계의 남은 틱(HoldTicks)이 걷히지 않고 남으면 여기서 더해진다(#96 — 시계는 EndPattern 이 걷고 남은 틱도 같이 걷는다).
        sim.NextActiveIn.ShouldNotBeNull();
        sim.NextActiveIn.Value.ShouldBe(45 * BattleSim.Dt, 1e-9, "새 패턴이 선 틱의 남은 시간에 끊긴 돌진이 세웠던 남은 틱이 들었다");
        sim.Tick(default);
        sim.NextActiveIn.ShouldNotBeNull();
        sim.NextActiveIn.Value.ShouldBe(44 * BattleSim.Dt, 1e-9, "새 패턴의 첫 틱에 시계가 안 갔다 — 끊긴 돌진이 세웠던 시계가 남았다");

        // 판정 보기의 "다음 판정" 도 끊긴 돌진의 설 자리를 잊는다 — 새 패턴은 아직 안 움직였으니 보스 자리다. 남으면 새 패턴의 선딜 내내 3타가
        // 옛 멈출 자리(무너질 때의 파이터 앞 100)에 선다.
        HitShape third = TestConfigs.HitShapes()["medieval_king/attack3/2"];
        sim.BossNextRects.ShouldBe(third.Place(new Placement(sim.Boss.X, 0, sim.Boss.Facing)), "끊긴 돌진의 설 자리가 다음 패턴의 미리보기에 남았다");
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
