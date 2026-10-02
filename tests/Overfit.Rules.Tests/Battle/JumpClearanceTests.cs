using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// "점프로 넘을 수 있었나" 를 <b>그 판정이 선 자리에서</b> 잰다 (#85 · 설계 §7.3). 모양 전체의 윗끝 하나로 재던 때(#72)는 3연격이
/// "1타만 넘는다" 였는데, 실제 판정은 몸과 사각형 하나하나를 견줘 2타는 보스 앞에서 · 3타는 바짝 붙어서도 뛰어 넘는다.
/// </summary>
public class JumpClearanceTests
{
    /// <summary>보스 발 중심 — 아레나 한가운데. 보스는 오른쪽을 본다(모양의 + 가 오른쪽이다).</summary>
    private const double _bossX = 960;

    private static readonly Placement _boss = new(_bossX, 0, 1);

    /// <summary>창 한 장 — 0.125초 = 8틱(<see cref="BattleSim.TicksFor"/>).</summary>
    private static readonly int _window = BattleSim.TicksFor(0.125);

    private static JumpClearance Real() => new(TestConfigs.Fighters().Values.Single());

    private static HitShape Shape(string id) => TestConfigs.HitShapes()[id];

    [Fact]
    public void 실제_캐릭터는_60틱_떠_있고_바닥_띠_위로는_53틱_몸이_빈다()
    {
        // 설계 §4.2 의 산수 그대로다 — 이산 점프(jump_velocity 1220 · 중력 2400 · 1/60초)는 누른 틱부터 60틱 떠 있고, 발이 60 위에
        // 있는 것은 53틱(누른 틱 + 3 ~ + 55)이다. 띠는 바닥 전체라 어디서 뛰어도 같다. 이 53 이 스펙의 점프 누름 창(46틱)을 떠받친다.
        JumpClearance jump = Real();
        HitShape band = HitShape.Band(0, 1920, 0, 60);

        jump.AirTicks.ShouldBe(60);
        jump.ClearRun(band, _boss, _bossX + 115).ShouldBe(53);
        jump.ClearRun(band, _boss, _bossX - 700).ShouldBe(53, "바닥 전체 띠인데 자리마다 다르다");
    }

    [Fact]
    public void 실제_3연격_1타는_어느_자리에서든_점프로_넘는다()
    {
        // 1타의 궤적 윗끝은 236.5 이고 정점은 300 이다 — 발이 그 위에 27틱 있다(누른 틱 + 16 ~ + 42). 칼이 없는 자리는 떠 있는 60틱이
        // 다 빈다. 보스 등 뒤 −450 부터 칼끝 너머 +500 까지 한 px 씩 재도 창 8틱보다 짧은 자리가 없다.
        JumpClearance jump = Real();
        HitShape first = Shape("medieval_king/attack/2");

        jump.ClearRun(first, _boss, _bossX + 115).ShouldBe(27);
        for (int dx = -450; dx <= 500; dx++)
        {
            jump.Clears(first, _boss, _bossX + dx, _window).ShouldBeTrue($"1타를 dx={dx} 에서 못 넘는다고 한다");
        }
    }

    [Fact]
    public void 실제_3연격_2타는_보스_앞에서_넘고_등_뒤에서는_못_넘는다()
    {
        // 2타의 윗끝 346.5 는 보스 등 뒤(−382 ~ −212)와 칼끝에만 있다. 보스 앞 115 에서 몸에 닿는 칸은 104.5 까지라 발이 그 위에
        // 49틱 있다 — "1타만 넘는다" 였던 옛 잣대(외곽 상자의 윗끝)는 이것을 못 봤다(#85). 등 뒤의 높은 궤적 아래에서는 창을 못 넘긴다.
        JumpClearance jump = Real();
        HitShape second = Shape("medieval_king/attack2/2");

        jump.ClearRun(second, _boss, _bossX + 115).ShouldBe(49);
        jump.Clears(second, _boss, _bossX + 115, _window).ShouldBeTrue("보스 앞에 선 사람이 2타를 못 넘는다고 한다");
        jump.Clears(second, _boss, _bossX - 211, _window).ShouldBeTrue();
        jump.Clears(second, _boss, _bossX - 212, _window).ShouldBeFalse("등 뒤의 높은 궤적 아래에서 넘는다고 한다");
        jump.Clears(second, _boss, _bossX - 300, _window).ShouldBeFalse();
        jump.Clears(second, _boss, _bossX - 382, _window).ShouldBeFalse();
        jump.Clears(second, _boss, _bossX - 383, _window).ShouldBeTrue();
    }

    [Fact]
    public void 실제_3연격_3타는_바짝_붙으면_넘고_그_너머_사거리_안에서는_못_넘는다()
    {
        // 3타의 땅 궤적은 +80 ~ +404(파이터 중심) 를 치고 높이 566.5 까지 올라간다 — 그 사이에 선 사람은 정점(300)으로 못 넘는다.
        // 다만 바짝 붙은 자리(+80 ~ +167)는 몸에 닿는 칸이 낮아 넘는다(+115 에서 49틱). +168 부터 +448 까지는 창 8틱을 못 채운다.
        JumpClearance jump = Real();
        HitShape third = Shape("medieval_king/attack3/2");

        jump.ClearRun(third, _boss, _bossX + 115).ShouldBe(49);
        jump.Clears(third, _boss, _bossX + 167, _window).ShouldBeTrue("바짝 붙은 사람이 3타를 못 넘는다고 한다");
        jump.Clears(third, _boss, _bossX + 168, _window).ShouldBeFalse();
        jump.Clears(third, _boss, _bossX + 300, _window).ShouldBeFalse("3타의 높은 궤적 안에서 넘는다고 한다");
        jump.Clears(third, _boss, _bossX + 448, _window).ShouldBeFalse();
        jump.Clears(third, _boss, _bossX + 449, _window).ShouldBeTrue();
    }

    [Fact]
    public void 창이_몸이_비는_틱보다_길면_못_넘는다()
    {
        // 정점이 모양 위로 가는 것만으로는 모자라다 — 창 내내 몸이 비어야 넘는다. 같은 높이 60 띠를 창 8틱과 창 60틱(53틱보다 길다)으로
        // 잰다. 옛 BossHitsTests.점프_가능은_발이_창_내내_윗끝_위에_있을_수_있을_때만_참이다 가 보던 것을 자리마다의 잣대로 옮겼다.
        JumpClearance jump = Real();
        HitShape band = HitShape.Band(0, 1920, 0, 60);

        jump.Clears(band, _boss, _bossX + 115, BattleSim.TicksFor(0.125)).ShouldBeTrue();
        jump.Clears(band, _boss, _bossX + 115, BattleSim.TicksFor(1.0)).ShouldBeFalse("창 60틱은 몸이 빈 53틱보다 긴데 넘는다고 한다");

        // 경계 — 몸이 빈 틱이 창의 틱과 같으면 넘는다("창 내내"). 53틱은 첫 테스트가 잰 ClearRun 이다.
        jump.Clears(band, _boss, _bossX + 115, 53).ShouldBeTrue("몸이 창 53틱 내내 비는데 못 넘는다고 한다");
        jump.Clears(band, _boss, _bossX + 115, 54).ShouldBeFalse();

        // "창 내내" 는 **끊기지 않고** 다 — 몸이 빈 틱을 다 더한 수가 아니라 가장 길게 이어진 수다. 위의 띠는 빈 틱이 한 줄이라 둘을 못
        // 가른다(다 더하는 변이가 439건을 다 통과했다 · Task 1 리뷰). 발 높이 150 ~ 160 의 얇은 막은 몸(120)이 오를 때 한 틱 · 내릴 때 두
        // 틱 막 밑으로 비고, 막을 뚫고 지나는 틱들을 사이에 두고 위로 41틱 빈다. 다 더하면 44 라 창 42틱도 넘는다고 한다 — 막 밑의 틱과
        // 위의 틱 사이에 몸이 막에 닿는다.
        var film = new HitShape(new[] { new HitRect(-2000, 2000, 150, 160) });
        jump.ClearRun(film, _boss, _bossX + 115).ShouldBe(41);
        jump.Clears(film, _boss, _bossX + 115, 41).ShouldBeTrue();
        jump.Clears(film, _boss, _bossX + 115, 42).ShouldBeFalse("몸이 빈 틱이 막을 뚫는 틱으로 끊겼는데 창 42틱을 넘는다고 한다 — 창은 끊김 없이 덮여야 한다");
    }

    [Fact]
    public void 왼쪽을_보는_보스의_모양은_뒤집은_자리에서_잰다()
    {
        // 모양은 공격자 기준이라 보스가 왼쪽을 보면 앞뒤가 뒤집힌다 — 왼쪽 115 가 앞이고 오른쪽 300 이 등 뒤다. 판정(ShapeHit)이 쓰는
        // HitShape.ToWorld 를 그대로 쓴다.
        JumpClearance jump = Real();
        HitShape second = Shape("medieval_king/attack2/2");
        var facingLeft = new Placement(_bossX, 0, -1);

        jump.Clears(second, facingLeft, _bossX - 115, _window).ShouldBeTrue();
        jump.Clears(second, facingLeft, _bossX + 300, _window).ShouldBeFalse();
    }

    [Fact]
    public void 관측의_점프_가능은_판정이_선_틱의_파이터_자리에서_잰다()
    {
        // 같은 판정이 자리에 따라 다른 답을 싣는다 — 가까이는 높고(400) 멀리는 낮은(60) 모양이다. 가만히 선 파이터(보스에서 960)는
        // 낮은 쪽에 맞고 그 자리에서는 뛰어 넘을 수 있었다. 판정 전에 보스 쪽으로 걸어 간 파이터(보스에서 260)는 높은 쪽에 맞고 그
        // 자리에서는 못 넘었다. 판을 세울 때 한 번 재던 때(모양의 윗끝 400)는 둘 다 거짓이었다.
        DodgeEvent Stand(int walkTicks)
        {
            var shapes = TestConfigs.HitShapes();
            shapes["test/step"] = new HitShape(new[] { new HitRect(0, 500, 0, 400), new HitRect(500, 2000, 0, 60) });
            var pattern = new PatternDef
            {
                Tags = TestConfigs.Sweep(100, 0).Tags,
                Timeline = new List<PatternStep>
                {
                    new() { T = 0, Kind = "windup" },
                    new() { T = 2.0, Kind = "active", Hitbox = "test/step", Damage = 5, ActiveSeconds = 0.125 },
                    new() { T = 2.5, Kind = "end" },
                },
            };
            BattleSim sim = TestConfigs.PatternSim("계단", pattern, shapes: shapes);

            for (int i = 0; i < 300 && sim.Events.Count == 0; i++)
            {
                sim.Tick(new InputFrame((sbyte)(i < walkTicks ? 1 : 0), false, false, false));
            }

            return sim.Events.Single();
        }

        DodgeEvent far = Stand(walkTicks: 0);
        far.Verdict.ShouldBe(HitVerdict.Hit);
        far.Distance.ShouldBe(960, 0.5);
        far.JumpAvailable.ShouldBeTrue("낮은 쪽에 선 사람이 뛰어 넘을 수 없었다고 실렸다");

        DodgeEvent near = Stand(walkTicks: 100);
        near.Verdict.ShouldBe(HitVerdict.Hit);
        near.Distance.ShouldBe(260, 0.5);
        near.JumpAvailable.ShouldBeFalse("높은 쪽에 선 사람이 뛰어 넘을 수 있었다고 실렸다");
    }

    [Fact]
    public void 관측의_점프_가능은_창_안에서_걸어_든_자리가_아니라_창이_열린_자리의_것이다()
    {
        // 창이 열린 틱의 자리다(BossSwings 의 첫 판정) — 결과가 갈린 틱의 자리가 아니다. 분모는 "그 판정이 설 때 무엇을 고를 수 있었나" 라서다.
        // 보스 앞 500 까지 높이 400 인 모양(기준 파이터의 정점 176 으로는 어디서도 못 넘는다)의 창을 0.25초(15틱)로 둔다. 파이터는 사거리
        // 바로 밖(보스에서 561 — 몸의 앞끝이 모양 끝 500 에서 31 모자라다)에 서 있다가 창이 열리면 걸어 들어가 창 안에서 맞는다. 창이 열린
        // 자리는 사거리 밖이라 뛰어도 산다(참) — 맞은 자리에서 재면 거짓이다. 선 채로 맞는 위 테스트는 두 틱의 자리가 같아 이것을 못 가른다.
        var shapes = TestConfigs.HitShapes();
        var high = new HitShape(new[] { new HitRect(0, 500, 0, 400) });
        shapes["test/high"] = high;
        var pattern = new PatternDef
        {
            Tags = TestConfigs.Sweep(100, 0).Tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0, Kind = "windup" },
                new() { T = 2.0, Kind = "active", Hitbox = "test/high", Damage = 5, ActiveSeconds = 0.25 },
                new() { T = 2.5, Kind = "end" },
            },
        };
        BattleSim sim = TestConfigs.PatternSim("높은 칼", pattern, shapes: shapes);

        // 480 → 879 (57틱 × 7px). 보스는 1440 에서 왼쪽을 본다 — 모양은 940 ~ 1440 이고 몸의 앞끝은 909 다.
        var right = new InputFrame(1, false, false, false);
        for (int i = 0; i < 57; i++)
        {
            sim.Tick(right);
        }

        int opened = 0;
        for (int i = 0; i < 300 && sim.Events.Count == 0; i++)
        {
            sim.Tick(opened > 0 ? right : default);
            if (opened == 0 && sim.SwingLive)
            {
                opened = sim.Ticks;
            }
        }

        DodgeEvent e = sim.Events.Single();
        e.Verdict.ShouldBe(HitVerdict.Hit);
        opened.ShouldBeGreaterThan(0, "창이 산 틱이 없었다 — 열린 틱에 맞았다");
        sim.Ticks.ShouldBeGreaterThan(opened, "창이 열린 틱에 맞았다 — 이 테스트가 두 자리를 못 가른다");
        new JumpClearance(TestConfigs.Fighter())
            .Clears(high, new Placement(sim.Boss.X, 0, sim.Boss.Facing), sim.Fighter.X, BattleSim.TicksFor(0.25))
            .ShouldBeFalse("맞은 자리에서도 뛰어 넘을 수 있다 — 이 테스트가 두 자리를 못 가른다");
        e.JumpAvailable.ShouldBeTrue("창이 열린 자리(사거리 밖)가 아니라 걸어 든 자리에서 쟀다");
    }

    [Fact]
    public void 관측의_점프_가능은_도약의_착지를_보스가_내린_자리에서_잰다()
    {
        // 보스 쪽 자리도 **창이 열린 틱의 첫 판정이 선 자리**다 (#85 Task 1 리뷰 — 위 테스트의 파이터 쪽 자리와 짝이다). 도약은 착지 판정이 서는
        // 바로 그 틱에 내리는데(설계 §4.2), 러너가 판정을 내는 것은 그 틱의 움직임 앞이다 — 그때 재면 보스는 아직 앞 틱의 공중(발 ≈ 30)에 있어
        // 띠가 30 ~ 90 이고, 판정은 내린 자리(띠 0 ~ 60)에 선다. 창을 실제 캐릭터가 바닥 띠 위로 몸이 비는 53틱으로 두면 두 자리가 갈린다: 내린
        // 자리는 53틱이라 넘고 공중 자리는 51틱이라 못 넘는다. 창 8틱 · 바닥 전체 띠인 지금 데이터는 둘 다 넘어 골든이 이것을 못 봤다.
        FighterConfig real = TestConfigs.Fighters().Values.Single();
        var leap = new PatternDef
        {
            Tags = TestConfigs.Sweep(100, 0).Tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0, Kind = "windup" },
                new() { T = 0.4, Kind = "windup", Motion = new MotionDef { Id = "leap", Height = 280, Air = 0.6 } },
                new() { T = 1.0, Kind = "active", Band = new double[] { 0, 1920, 0, 60 }, Damage = 5, ActiveSeconds = 53 / 60.0 },
                new() { T = 2.0, Kind = "end" },
            },
        };
        BattleSim landing = TestConfigs.PatternSim("도약", leap, real);

        // 가만히 선 파이터는 착지 띠에 첫 틱에 맞는다 — 관측이 나온 틱이 판정이 선 틱이고, 그 틱 앞의 보스 자리가 움직이기 전의 자리다.
        var air = new Placement(0, 0, 0);
        for (int i = 0; i < 300 && landing.Events.Count == 0; i++)
        {
            air = new Placement(landing.Boss.X, landing.Boss.Y, landing.Boss.Facing);
            landing.Tick(default);
        }

        DodgeEvent landed = landing.Events.Single();
        landed.Verdict.ShouldBe(HitVerdict.Hit);
        landing.Boss.Y.ShouldBe(0);
        air.Y.ShouldBeGreaterThan(0, "판정이 선 틱 앞에 보스가 이미 땅에 있었다 — 이 테스트가 두 자리를 못 가른다");
        new JumpClearance(real).Clears(HitShape.Band(0, 1920, 0, 60), air, landing.Fighter.X, BattleSim.TicksFor(53 / 60.0))
            .ShouldBeFalse("공중 자리에서도 넘는다 — 이 테스트가 두 자리를 못 가른다");
        landed.JumpAvailable.ShouldBeTrue("착지 판정을 내린 자리가 아니라 움직이기 전의 공중 자리에서 쟀다");
    }

    [Fact]
    public void 점프가_띄운_몸이_거리로_빗나가면_점프의_공이고_서서도_안_닿는_자리면_간격이다()
    {
        // 분자도 자리마다다 (#85). 2타는 보스 앞에서 뛰어 넘는다(위) — 그런데 떠 있는 몸은 모양의 외곽 상자 안이라 빗나간 이유가 높이가
        // 아니라 거리(몸 높이의 궤적 끝 너머) · 틈이다(설계 §3.6 ②). 공을 간격으로 돌리면 분모("뛰어 넘을 수 있었다")에 들고 분자("뛰었다")에는
        // 안 들어, 뛰어 넘은 사람이 "뛸 수 있었는데 안 뛰었다" 로 실린다. 그래서 대시의 반사실(#46)과 같은 물음을 점프에도 한다 — 같은 가로
        // 자리의 땅에 서 있었으면 닿았나. 닿았으면 몸을 띄운 점프가 피한 것이다. 사거리 밖에서 뛴 것은 서서도 안 닿으므로 그대로 간격이다.
        DodgeEvent Jumped(int walkTicks)
        {
            var pattern = new PatternDef
            {
                Tags = TestConfigs.Sweep(100, 0).Tags,
                Timeline = new List<PatternStep>
                {
                    new() { T = 0, Kind = "windup" },
                    new() { T = 3.0, Kind = "active", Hitbox = "medieval_king/attack2/2", Damage = 8, ActiveSeconds = 0.125 },
                    new() { T = 3.5, Kind = "end" },
                },
            };
            BattleSim sim = TestConfigs.PatternSim("2타", pattern, TestConfigs.Fighters().Values.Single());

            // 판정이 서기 10틱 앞에 뛴다 — 보스 앞 115 에서 2타 위로 몸이 비는 49틱 안에 창 8틱이 든다.
            for (int i = 0; i < 400 && sim.Events.Count == 0; i++)
            {
                bool jump = sim.Fighter.Grounded && sim.NextActiveIn is { } left && left <= 10 * BattleSim.Dt;
                sim.Tick(new InputFrame((sbyte)(i < walkTicks ? 1 : 0), jump, false, false));
            }

            return sim.Events.Single();
        }

        // 480 → 1320 (120틱 × 7px) — 보스(1440)에서 120. 서 있었으면 2타(땅에서 +426 까지)에 맞는다.
        DodgeEvent close = Jumped(walkTicks: 120);
        close.Distance.ShouldBe(120, 0.5);
        close.Airborne.ShouldBeTrue("뛴 몸이 판정 때 땅에 있다 — 이 테스트가 점프를 안 본다");
        close.Verdict.ShouldBeOneOf(HitVerdict.MissedTooFar, HitVerdict.MissedByGap);
        close.JumpAvailable.ShouldBeTrue();
        close.Verb.ShouldBe(DodgeVerb.Jump, "뛰어 넘은 2타가 간격의 공이 됐다 — 뛸 수 있었는데 안 뛴 사람으로 실린다");

        // 480 → 900 (60틱) — 보스에서 540. 사거리(+426) 밖이라 서서도 안 닿는다: 뛰었어도 거리가 피하게 했다.
        DodgeEvent far = Jumped(walkTicks: 60);
        far.Distance.ShouldBe(540, 0.5);
        far.Airborne.ShouldBeTrue();
        far.Verdict.ShouldBe(HitVerdict.MissedTooFar);
        far.Verb.ShouldBe(DodgeVerb.Spacing, "사거리 밖에서 뛴 것이 점프의 공이 됐다");
    }

    [Fact]
    public void 떠서_밖으로_대시해_거리로_빗나가면_대시의_반사실이_먼저라_대시의_공이다()
    {
        // 대시의 반사실이 먼저다 (#46 · #85). 뛰어 오른 채 밖으로 대시해 거리로 빗나갔으면, 같은 자리의 땅에서는 닿았어도 대시를 시작한
        // 자리에서 닿았으면 그 거리를 만든 것은 대시다. 두 반사실이 **다 참인 자리**라야 순서가 갈린다 — 위 테스트의 둘은 대시가 없어 순서를
        // 뒤집어도 초록이었다(점프를 먼저 묻는 변이가 이 테스트 전에는 살았다). 보스 앞 400 까지 높이 400 · 그 너머 1100 까지 높이 60 인
        // 모양이다. 보스에서 302 에 서서 뛰고, 떠 있는 채 밖으로 대시해 676 에서 창을 맞는다 — 몸은 낮은 칸 위에 떠 있고, 땅에 서 있었으면
        // 낮은 칸에 닿았고, 대시를 시작한 자리(309 · 발 높이 167)는 높은 칸 안이다.
        var shapes = TestConfigs.HitShapes();
        var ledge = new HitShape(new[] { new HitRect(0, 400, 0, 400), new HitRect(400, 1100, 0, 60) });
        shapes["test/ledge"] = ledge;
        var ledgePattern = new PatternDef
        {
            Tags = TestConfigs.Sweep(100, 0).Tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0, Kind = "windup" },
                new() { T = 3.0, Kind = "active", Hitbox = "test/ledge", Damage = 8, ActiveSeconds = 0.125 },
                new() { T = 3.5, Kind = "end" },
            },
        };
        BattleSim sim = TestConfigs.PatternSim("턱", ledgePattern, TestConfigs.Fighters().Values.Single(), shapes);

        // 480 → 1138 (94틱 × 7px). 판정 20틱 앞에 뛰고, 9틱 뒤 밖을 보고, 10틱 뒤 공중 대시(착지 전 한 번은 된다)로 빠진다.
        int jumpedAt = -1;
        HitRect? dashFrom = null;
        for (int i = 0; i < 400 && sim.Events.Count == 0; i++)
        {
            bool jump = jumpedAt < 0 && sim.Fighter.Grounded && sim.NextActiveIn is { } left && left <= 20 * BattleSim.Dt;
            jumpedAt = jump ? i : jumpedAt;
            bool dash = jumpedAt >= 0 && i == jumpedAt + 10;
            if (dash)
            {
                // 대시를 누르기 전의 몸 — DodgeCredit 이 대시의 반사실로 대는 자리(틱 시작의 wasX · wasY)다.
                dashFrom = new HitRect(sim.Fighter.X - sim.Fighter.HalfWidth, sim.Fighter.X + sim.Fighter.HalfWidth,
                    sim.Fighter.Y, sim.Fighter.Y + sim.Fighter.BodyHeight);
            }

            sbyte move = (sbyte)(i < 94 ? 1 : jumpedAt >= 0 && i == jumpedAt + 9 ? -1 : 0);
            sim.Tick(new InputFrame(move, jump, dash, false));
        }

        DodgeEvent dashed = sim.Events.Single();
        var at = new Placement(sim.Boss.X, 0, sim.Boss.Facing);
        var standing = new HitRect(sim.Fighter.X - sim.Fighter.HalfWidth, sim.Fighter.X + sim.Fighter.HalfWidth, 0, sim.Fighter.BodyHeight);
        dashed.Distance.ShouldBe(Math.Abs(sim.Fighter.X - sim.Boss.X), 0.5, "창이 선 뒤에 움직였다 — 땅의 몸을 그 자리에 못 세운다");
        ShapeHit.Test(ledge, at, standing).ShouldBe(ShapeContact.Overlap, "땅에 서 있었어도 안 닿는 자리다 — 이 테스트가 순서를 못 가른다");
        dashFrom.ShouldNotBeNull("대시를 안 눌렀다");
        ShapeHit.Test(ledge, at, dashFrom.Value).ShouldBe(ShapeContact.Overlap, "대시를 시작한 자리가 안 닿는다 — 이 테스트가 순서를 못 가른다");
        dashed.Airborne.ShouldBeTrue();
        dashed.Verdict.ShouldBe(HitVerdict.MissedTooFar);
        dashed.Verb.ShouldBe(DodgeVerb.Dash, "떠서 뛴 대시가 만든 거리가 점프의 공이 됐다 — 대시의 반사실이 먼저다");
    }
}
