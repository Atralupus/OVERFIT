using System;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 탈진한 보스 (설계 §4.3 · §4.2) — 무엇을 안 하는가, 공중에서 무너지면 어떻게 내리는가. 두 원인(패리 · 게이지) 중 게이지로
/// 무너뜨린다: 받아칠 수 없는 점프 공격을 멈추는 길이 게이지뿐이라(설계 §4.2) 공중 탈진은 게이지로만 온다.
///
/// <para>
/// 판을 미는 기다림은 전부 틱 수로 묶고 뒤에서 그 조건을 단언한다 — 판은 결과가 난 뒤에도 틱을 받아, 규칙이 깨진 날 묶지 않은
/// 기다림은 실패하지 않고 게이트(<c>tools/build.sh check</c>)를 멈춰 세운다.
/// </para>
/// </summary>
public class BossExhaustTests
{
    private static readonly InputFrame _attack = new(0, false, false, false, Attack: true);
    private static readonly InputFrame _right = new(1, false, false, false, false);

    /// <summary>1타 한 대로 게이지가 끝까지 차는 기준 파이터 — 무너지는 순간을 한 틱으로 만든다.</summary>
    private static FighterConfig Breaker()
    {
        FighterConfig c = TestConfigs.Fighter();
        ComboStepDef s = c.Combo[0];
        c.Combo[0] = new ComboStepDef
        {
            Anim = s.Anim,
            Fps = s.Fps,
            Frames = s.Frames,
            StartFrame = s.StartFrame,
            BladeFrame = s.BladeFrame,
            Windup = s.Windup,
            Active = s.Active,
            Recover = s.Recover,
            Stiff = s.Stiff,
            Damage = s.Damage,
            Hitbox = s.Hitbox,
            Poise = 100,
        };
        return c;
    }

    /// <summary>
    /// 떠 있는 보스가 칼 끝(<see cref="BattleSim.FighterReach"/>)에 들어왔나 — 도약이 파이터 쪽으로 날아오는 동안 <b>한 번</b> 휘두를 자리다.
    /// 들어오기 전에 휘두르면 빗나가고, 1타는 경직까지 0.68초 커밋이라(#82) 36틱짜리 도약 안에서 다시 못 휘두른다 — 전에는 빗나가도
    /// 17틱 뒤에 또 휘둘러 도약 도중에 닿았다.
    /// </summary>
    private static bool InReach(BattleSim sim) =>
        sim.Boss.Y > 0 && sim.Boss.X - sim.Fighter.X <= sim.Boss.HalfWidth + sim.FighterReach;

    private static BattleSim Sim(FighterConfig fighter, BossConfig boss, string pattern) => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = fighter,
        HitShapes = TestConfigs.HitShapes(),
        Boss = boss,
        PatternIds = new[] { pattern },
        Patterns = TestConfigs.Patterns(),
        Seed = 1,
        MaxTicks = 60 * 60,
    });

    [Fact]
    public void 탈진한_보스는_다가가지도_돌아서지도_않는다()
    {
        // 설계 §4.3 — 탈진한 보스는 아무것도 안 한다. 쉬는 보스는 파이터를 향해 돌아서고 다가가는데(BattleSim.AdvanceBoss 의 쉬는
        // 갈래), 탈진 동안 그 갈래가 돌면 무너진 보스가 파이터를 쫓아 돈다 — 반격 창이 "보스가 멈춘 자리" 가 아니게 된다. 전에는 골든만
        // 이것을 잡았다(#59 의 3/6 넘김). 파이터가 보스를 가로질러 반대편으로 가도 보스는 안 돌고 안 움직여야 한다.
        BattleSim sim = Sim(Breaker(), TestConfigs.Boss(maxHealth: 999_999, patternGap: 1000), "3연격");
        double standoff = sim.Boss.HalfWidth + sim.Fighter.HalfWidth;
        for (int i = 0; i < 600 && Math.Abs(sim.Boss.X - sim.Fighter.X) > standoff; i++)
        {
            sim.Tick(_right);
        }

        Math.Abs(sim.Boss.X - sim.Fighter.X).ShouldBeLessThanOrEqualTo(standoff, "파이터가 칼이 닿는 자리까지 못 걸어갔다");
        sim.Tick(_attack);
        for (int i = 0; i < 30 && !sim.Boss.Exhausted; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.Exhausted.ShouldBeTrue("한 대로 무너지는 칼이 안 무너뜨렸다 — 이 테스트가 탈진을 못 본다");
        double x = sim.Boss.X;
        int facing = sim.Boss.Facing;

        bool crossed = false;
        for (int i = 0; i < 600; i++)
        {
            sim.Tick(_right);
            if (!sim.Boss.Exhausted)
            {
                break;
            }

            crossed |= sim.Fighter.X > sim.Boss.X;
            sim.Boss.X.ShouldBe(x, $"{sim.Ticks}틱: 탈진한 보스가 움직였다");
            sim.Boss.Facing.ShouldBe(facing, $"{sim.Ticks}틱: 탈진한 보스가 돌아섰다");
        }

        crossed.ShouldBeTrue("파이터가 보스를 못 지나갔다 — 돌아서기를 부를 자리가 없었다");

        // 풀리는 틱부터 쉬는 보스다 — 그 틱에 돌아서고 다가간다. 이것이 없으면 위의 단언은 "원래 안 돌고 안 움직이는 보스" 로도 초록이다.
        sim.Boss.Exhausted.ShouldBeFalse("600틱 안에 탈진이 안 풀렸다");
        sim.Boss.Facing.ShouldBe(-facing, "탈진이 풀렸는데 반대편의 파이터 쪽으로 안 돌았다");
        sim.Boss.X.ShouldNotBe(x, "탈진이 풀렸는데 안 다가갔다");
    }

    [Fact]
    public void 탈진의_남은_몫은_무너진_틱에_1_이고_풀리는_틱에_0_이다()
    {
        // 설계 §6 — HUD 의 경직 게이지는 탈진 동안 푸른 모양으로 바뀌어 **남은 탈진**을 그린다: 무너지는 순간 가득 찬 채 푸르게 바뀌어
        // 준다. 규칙의 게이지는 무너질 때 0 이라 그것을 그리면 "꽉 찼다" 가 한 프레임도 안 보인다. 몫은 규칙이 낸다 — 탈진의 틱 수를
        // 뷰가 따로 세면 탈진 길이를 고치는 날 바가 거짓말한다. 그래서 실제 보스(1.5초 = 90틱)가 아닌 길이(1.0초 = 60틱)로 잰다 — 90 을
        // 박은 몫도 실제 보스로는 초록이다.
        BattleSim sim = Sim(Breaker(), TestConfigs.Boss(maxHealth: 999_999, patternGap: 1000, exhaustSeconds: 1.0), "3연격");
        int total = BattleSim.TicksFor(1.0);
        total.ShouldNotBe(BattleSim.TicksFor(TestConfigs.Boss().ExhaustSeconds), "실제 보스와 같은 길이다 — 이 테스트가 분모를 어디서 읽는지 못 가른다");
        double standoff = sim.Boss.HalfWidth + sim.Fighter.HalfWidth;
        for (int i = 0; i < 600 && Math.Abs(sim.Boss.X - sim.Fighter.X) > standoff; i++)
        {
            sim.Tick(_right);
        }

        Math.Abs(sim.Boss.X - sim.Fighter.X).ShouldBeLessThanOrEqualTo(standoff, "파이터가 칼이 닿는 자리까지 못 걸어갔다");
        sim.Boss.ExhaustLeft.ShouldBe(0, "무너지지도 않았는데 남은 탈진이 있다");
        sim.Tick(_attack);
        for (int i = 0; i < 30 && !sim.Boss.Exhausted; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.ExhaustLeft.ShouldBe(1, "무너진 틱에 남은 탈진이 가득이 아니다");
        sim.Tick(default);
        sim.Boss.ExhaustLeft.ShouldBe((total - 1) / (double)total, 1e-9, "탈진이 한 틱에 한 몫씩 안 줄었다 — 분모가 이 탈진의 길이가 아니다");

        int ticks = 1;
        for (; ticks < 600 && sim.Boss.Exhausted; ticks++)
        {
            sim.Tick(default);
        }

        ticks.ShouldBe(total, "탈진이 받은 길이만큼 안 갔다");
        sim.Boss.ExhaustLeft.ShouldBe(0);
    }

    [Fact]
    public void 공중에서_무너지면_착지_판정_없이_포물선의_높이를_따라_그_자리에_내린다()
    {
        // 설계 §4.2 · §12 「공중 탈진」 — 게이지가 공중에서 차면 하던 패턴(착지 판정)은 끊기고, 보스는 포물선의 높이를 그대로 따라
        // 그 자리에 내린다(가로는 멈춘다). 전에는 탈진이 움직임을 버려 보스가 무너진 높이에 떠 있었다(#59 의 3/6 넘김).
        // 견줄 판(control)은 같은 판에서 칼만 안 휘두른다 — 도약의 높이는 시각에만 달려 두 판이 같은 틱에 같은 높이다.
        BossConfig boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 2.0);
        BattleSim sim = Sim(Breaker(), boss, "점프 공격");
        BattleSim control = Sim(Breaker(), boss, "점프 공격");

        // 보스에게서 몸 둘 폭보다 조금 먼 자리(≈ 205)까지 걸어가 선다 — 도약이 파이터 쪽으로 90px 남짓 날아오는 동안 칼(±90)이 몸(±85)에 닿는다.
        for (int i = 0; i < 600 && sim.Boss.X - sim.Fighter.X > 205; i++)
        {
            sim.Tick(_right);
            control.Tick(_right);
        }

        (sim.Boss.X - sim.Fighter.X).ShouldBeLessThanOrEqualTo(205, "파이터가 도약 앞자리까지 못 걸어갔다");
        int exhaustedAt = 0;
        for (int i = 0; i < 300 && exhaustedAt == 0; i++)
        {
            bool swing = InReach(sim) && sim.Fighter.Action == FighterAction.Idle;
            sim.Tick(swing ? _attack : default);
            control.Tick(default);
            if (sim.Boss.Exhausted)
            {
                exhaustedAt = sim.Ticks;
            }
        }

        exhaustedAt.ShouldBeGreaterThan(0, "도약 중에 칼이 안 닿았다 — 이 테스트가 공중 탈진을 못 본다");
        sim.Boss.Y.ShouldBeGreaterThan(0, "땅에서 무너졌다 — 공중 탈진을 못 본다");
        control.Boss.Y.ShouldBe(sim.Boss.Y, 1e-9, "두 판이 이미 갈렸다");
        double x = sim.Boss.X;

        for (int i = 0; i < 120 && control.Boss.Y > 0; i++)
        {
            sim.Tick(default);
            control.Tick(default);
            sim.Boss.Y.ShouldBe(control.Boss.Y, 1e-9, $"{sim.Ticks}틱: 무너진 보스가 포물선의 높이를 안 따른다");
            sim.Boss.X.ShouldBe(x, $"{sim.Ticks}틱: 무너진 보스가 가로로 움직였다");
        }

        sim.Boss.Y.ShouldBe(0, "무너진 보스가 땅에 안 내렸다");
        control.Boss.X.ShouldNotBe(x, "견줄 판의 보스가 그 자리에 섰다 — 가로가 멈춘 것을 못 본다");
        control.Events.ShouldNotBeEmpty("견줄 판에 착지 판정이 안 섰다");
        sim.Events.ShouldBeEmpty("무너진 보스의 착지 판정이 섰다");
        sim.Boss.Exhausted.ShouldBeTrue("내리기 전에 탈진이 풀렸다");
    }

    [Fact]
    public void 공중_탈진은_로그로_무너진_높이와_내린_자리를_남긴다()
    {
        // CLAUDE.md §5 — 판단과 전이는 [D] 로 남긴다. 공중 탈진은 끊긴 착지 판정이 관측도 안 남기므로(설계 §3.5 5) 로그가 유일한 흔적이다.
        BossConfig boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 2.0);
        BattleSim sim = Sim(Breaker(), boss, "점프 공격");
        for (int i = 0; i < 600 && sim.Boss.X - sim.Fighter.X > 205; i++)
        {
            sim.Tick(_right);
        }

        (sim.Boss.X - sim.Fighter.X).ShouldBeLessThanOrEqualTo(205, "파이터가 도약 앞자리까지 못 걸어갔다");
        using var log = new LogCapture();
        for (int i = 0; i < 300 && !(sim.Boss.Exhausted && sim.Boss.Y == 0); i++)
        {
            bool swing = InReach(sim) && !sim.Boss.Exhausted && sim.Fighter.Action == FighterAction.Idle;
            sim.Tick(swing ? _attack : default);
        }

        log.Lines.ShouldContain(l => l.StartsWith("[boss][D] exhaust cause=poise id=점프 공격 "));
        log.Lines.ShouldContain(l => l.StartsWith("[boss][D] exhaust_fall y="));
        log.Lines.ShouldContain($"[boss][D] exhaust_landed x={sim.Boss.X:0} tick={sim.Ticks}");
    }

    [Fact]
    public void 도약이_서는_틱에_무너지면_땅에_선_채로_탈진한다()
    {
        // 내림은 보스가 떠 있을 때만 든다(BattleSim.Exhaust 의 Boss.Y > 0). 도약이 서는 틱은 움직임이 이미 섰는데 높이가 0 이다
        // (LeapMotion 의 첫 틱) — 거기서 무너진 보스가 끊긴 도약을 들고 가면 탈진 동안 포물선을 끝까지 날아오른다. 누르는 틱을 한 틱씩
        // 밀며 칼이 도약이 서는 틱에 닿는 판을 찾는다 — 칼질의 선딜과 도약의 시각이 데이터라 틱을 박지 않는다.
        BossConfig boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 3.0);
        bool found = false;
        for (int lead = 0; lead <= 40 && !found; lead++)
        {
            BattleSim sim = Sim(Breaker(), boss, "점프 공격");
            double standoff = sim.Boss.HalfWidth + sim.Fighter.HalfWidth;
            for (int i = 0; i < 600 && sim.Boss.X - sim.Fighter.X > standoff; i++)
            {
                sim.Tick(_right);
            }

            for (int i = 0; i < 600 && sim.Boss.CurrentPattern is null; i++)
            {
                sim.Tick(default);
            }

            sim.Boss.CurrentPattern.ShouldBe("점프 공격", "걸어가는 동안이나 그 뒤에 점프 공격이 안 섰다");
            for (int i = 0; i < lead; i++)
            {
                sim.Tick(default);
            }

            using var log = new LogCapture();
            sim.Tick(_attack);
            for (int i = 0; i < 60 && !sim.Boss.Exhausted; i++)
            {
                sim.Tick(default);
            }

            int at = sim.Ticks;
            if (!sim.Boss.Exhausted || !log.Lines.Contains($"[boss][D] exhaust cause=poise id=점프 공격 tick={at}")
                || !log.Lines.Any(l => l.StartsWith("[boss][D] motion_begin id=leap ") && l.EndsWith($" tick={at}")))
            {
                continue;
            }

            found = true;
            sim.Boss.Y.ShouldBe(0, "도약이 서는 틱에 이미 떠 있다 — 이 테스트가 땅의 첫 틱을 못 본다");
            for (int i = 0; i < 600 && sim.Boss.Exhausted; i++)
            {
                sim.Tick(default);
                sim.Boss.Y.ShouldBe(0, $"{sim.Ticks}틱: 도약이 서는 틱에 무너진 보스가 끊긴 도약을 따라 떠올랐다");
            }

            log.Lines.ShouldNotContain(l => l.StartsWith("[boss][D] exhaust_fall "), "땅에서 무너졌는데 공중 탈진으로 적었다");
        }

        found.ShouldBeTrue("칼이 도약이 서는 틱에 닿는 판을 못 찾았다 — 이 테스트가 땅의 첫 틱을 안 본다");
    }
}
