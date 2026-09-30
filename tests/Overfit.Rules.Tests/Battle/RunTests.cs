using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 쉬기는 제자리 · 달리기 (설계 2026-09-29 조각1 §5). 실제 캐릭터 · 실제 보스 · 실제 동작에 대본(<see cref="ScriptPlanPicker"/>)으로 계획을 고정한다.
/// 판은 파이터 480 · 보스 1440 에서 선다(거리 960). 보스는 쉬는 동안 돌아서기만 하고, 계획이 달리기를 골랐으면 쉬기가 끝난 틱부터 파이터 앞
/// <c>run_stop</c>(280)까지 틱당 14px 로 달려 닿는 틱에 첫 동작을 세운다.
/// </summary>
public class RunTests
{
    private static readonly InputFrame _right = new(1, false, false, false, false);
    private static readonly InputFrame _attack = new(0, false, false, false, true);

    private static readonly string[] _roster = ["3연격", "돌진"];

    private static FighterConfig Real() => TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter];

    /// <summary>대본 <paramref name="script"/> 로 선 판 — 보스는 안 죽는다.</summary>
    private static BattleSim Sim(ScriptPlan[] script, BossConfig? boss = null, FighterConfig? fighter = null)
    {
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        return new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = fighter ?? Real(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = boss ?? TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = _roster,
            Patterns = patterns,
            Seed = 51,
            Picker = new ScriptPlanPicker(_roster, patterns, script),
            MaxTicks = TestConfigs.MaxTicks(),
        });
    }

    /// <summary>첫 동작이 설 때까지 민다 — 선 판의 틱을 돌려준다. <paramref name="input"/> 은 판의 틱을 받는다.</summary>
    private static int UntilBegins(BattleSim sim, Func<int, InputFrame>? input = null)
    {
        for (int i = 0; i < 60 * 30 && sim.Boss.CurrentPattern is null; i++)
        {
            sim.Tick(input?.Invoke(sim.Ticks + 1) ?? default);
        }

        sim.Boss.CurrentPattern.ShouldNotBeNull("첫 동작이 안 섰다");
        return sim.Ticks;
    }

    [Fact]
    public void 쉬는_동안_보스는_제자리에서_돌아서기만_한다()
    {
        // 설계 2026-09-29 조각1 §5.1 — 옛 보스는 쉬는 동안 파이터 쪽으로 160px/s 로 미끄러졌다(걷기 그림이 없어 idle 그대로 · 0.8초에 128px).
        // 이제 제자리다. 파이터가 보스를 지나가면 돌아서기만 한다 — 몸 충돌이 없어 지나갈 수 있다(#27).
        BattleSim sim = Sim([new ScriptPlan(5.0, "3연격")]);
        double x = sim.Boss.X;

        for (int i = 0; i < 200; i++)
        {
            sim.Tick(_right);
            sim.Boss.X.ShouldBe(x, $"{sim.Ticks}틱: 쉬는 보스가 움직였다");
            (sim.Boss.CurrentPattern, sim.BossRunning).ShouldBe((null, false), $"{sim.Ticks}틱: 5초 쉬기 안에 무언가 섰다");
        }

        sim.Fighter.X.ShouldBeGreaterThan(x, "파이터가 보스를 못 지나갔다");
        sim.Boss.Facing.ShouldBe(1, "지나간 파이터 쪽으로 안 돌아섰다");
    }

    [Fact]
    public void 달리기를_고른_계획은_쉬기_뒤_파이터_앞_280_까지_달리고_닿는_틱에_첫_동작이_선다()
    {
        // 설계 §5.2 · §5.3 — 쉬기(0.4초 · 24틱)가 끝난 틱부터 달린다. 판이 선 거리 960 에서 280 까지 680px 를 틱당 14 로 — 48걸음(672)에 8 이 남아 49째
        // 걸음에 닿는다(판의 24 ~ 72틱). 닿는 틱에 첫 동작을 세운다 — 선 자리가 파이터 앞 280 이다.
        using var capture = new LogCapture(LogLevel.Debug);
        BattleSim sim = Sim([new ScriptPlan(0.4, "3연격", Run: true)]);
        var running = new List<int>();

        while (sim.Boss.CurrentPattern is null && sim.Ticks < 600)
        {
            sim.Tick(default);
            if (sim.BossRunning)
            {
                running.Add(sim.Ticks);
            }
        }

        int begun = sim.Ticks;
        begun.ShouldBe(72);
        running.ShouldBe(Enumerable.Range(24, 48).ToList(), "달린 틱이 쉬기가 끝난 24 부터 닿기 전 71 까지가 아니다");
        (sim.Boss.X, sim.Boss.Facing, sim.Boss.CurrentPattern, sim.BossRunning).ShouldBe((480.0 + 280, -1, "3연격", false));
        capture.Lines.ShouldContain(l => l.StartsWith("[boss][D] run_begin d=960 tick=24", StringComparison.Ordinal));
        capture.Lines.ShouldContain(l => l.StartsWith("[boss][D] run_end ticks=49 moved=680 x=760 tick=72", StringComparison.Ordinal));
    }

    [Fact]
    public void 이미_280_안이면_안_달린다()
    {
        // 설계 §5.2 — 쉬기(2초 · 120틱) 동안 파이터가 걸어 들어와 보스 앞 260 에 섰다. 달리기를 고른 계획이어도 안 달리고 쉬기가 끝난 틱에 곧장
        // 첫 동작이다 — 보스는 1440 그대로다.
        using var capture = new LogCapture(LogLevel.Debug);
        BattleSim sim = Sim([new ScriptPlan(2.0, "3연격", Run: true)]);

        int begun = UntilBegins(sim, tick => tick <= 100 ? _right : default);

        begun.ShouldBe(120);
        (sim.Fighter.X, sim.Boss.X).ShouldBe((480.0 + 700, 1440.0));
        capture.Lines.ShouldContain("[boss][D] run_end ticks=1 moved=0 x=1440 tick=120", "안 달린 달리기가 로그에서 안 갈린다");
    }

    [Fact]
    public void 파이터가_등_뒤로_가면_돌아서서_그쪽으로_달린다()
    {
        // 설계 §5.2 — 쉬는 동안(5초) 파이터가 보스를 지나 오른쪽 벽 가까이(1740)로 갔다. 보스는 돌아서서(+1) 오른쪽으로 달려 파이터 앞 280 에 선다 —
        // 달리기는 늘 파이터 쪽으로 돌아선 뒤 앞으로만 간다.
        BattleSim sim = Sim([new ScriptPlan(5.0, "3연격", Run: true)]);

        UntilBegins(sim, tick => tick <= 180 ? _right : default);

        sim.Fighter.X.ShouldBe(480.0 + 1260);
        (sim.Boss.Facing, sim.Boss.X).ShouldBe((1, 1740.0 - 280));
    }

    [Fact]
    public void 벽에_붙은_파이터에게는_경계에서_멈추고_첫_동작이_선다()
    {
        // Review Focus 2 — 멈출 자리가 보스가 설 수 있는 범위 밖이다(run_stop 20 · 파이터가 오른쪽 벽 1890 에 붙었다 → 1870 > 1920 − 85). 달리기는
        // 경계(1835)까지 가서 끝나고 첫 동작을 세운다 — 자르지 않으면 "아직 멀다" 가 매 틱 나와 상한까지 달린다. 등 뒤로 간 파이터에게 돌아서서
        // 달리는 판이기도 하다.
        using var capture = new LogCapture(LogLevel.Warn);
        BattleSim sim = Sim([new ScriptPlan(5.0, "3연격", Run: true)], TestConfigs.Boss(maxHealth: 999_999, runStop: 20));

        int begun = UntilBegins(sim, _ => _right);

        sim.Fighter.X.ShouldBe(1920.0 - Real().HalfWidth);
        (sim.Boss.Facing, sim.Boss.X).ShouldBe((1, 1920.0 - sim.Boss.HalfWidth));
        begun.ShouldBeLessThan(300 + 60, "경계에서 안 끝나고 상한까지 달렸다");
        capture.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void 달리는_몸에는_판정이_없고_파이터의_칼은_닿는다()
    {
        // 설계 §5.2 — 달리는 몸에 닿아도 파이터는 안 맞는다(돌진의 달리는 동안과 같다). run_stop 0 이라 보스가 파이터 몸을 지나 한가운데까지 달려온다.
        // 파이터의 칼은 달리는 보스에도 닿는다 — 달려오는 보스를 1타로 친다.
        BattleSim sim = Sim([new ScriptPlan(0.4, "3연격", Run: true)], TestConfigs.Boss(maxHealth: 999_999, runStop: 0));
        int health = sim.Fighter.Health;
        bool struck = false;

        while (sim.Boss.CurrentPattern is null)
        {
            bool near = sim.BossRunning && Math.Abs(sim.Boss.X - sim.Fighter.X) < 360;
            sim.Tick(near && !struck ? _attack : default);
            struck |= sim.Boss.Health < 999_999;
            if (sim.BossRunning)
            {
                sim.Fighter.Health.ShouldBe(health, $"{sim.Ticks}틱: 달리는 보스에 맞았다");
                sim.Events.ShouldBeEmpty();
            }
        }

        struck.ShouldBeTrue("파이터의 칼이 달리는 보스에 안 닿았다");
        sim.Boss.X.ShouldBe(sim.Fighter.X, 1e-9, "run_stop 0 인데 파이터 한가운데까지 안 왔다");
    }

    [Fact]
    public void 삼초를_넘기면_W_run_timeout_을_남기고_그_자리에서_첫_동작이다()
    {
        // 설계 §5.3 — run_max_seconds(3초 · 180틱)는 안전장치다. 빠르기 60px/s(틱당 1)면 680px 에 680틱이 든다 — 180째 걸음에 멈추고 [W] 를 한 줄
        // 남긴 뒤 그 자리(1440 − 180)에서 첫 동작을 세운다. 쉬기(0.4초)가 끝난 24틱부터 180걸음이라 203틱이다.
        using var capture = new LogCapture(LogLevel.Warn);
        BattleSim sim = Sim([new ScriptPlan(0.4, "3연격", Run: true)], TestConfigs.Boss(maxHealth: 999_999, runSpeed: 60));

        int begun = UntilBegins(sim);

        (begun, sim.Boss.X).ShouldBe((24 + 179, 1440.0 - 180));
        capture.Lines.ShouldHaveSingleItem().ShouldStartWith("[boss][W] run_timeout ticks=180 d=780 x=1260 tick=203");
    }

    [Fact]
    public void 달리는_동안_탈진하면_계획이_끝난다()
    {
        // 설계 §5.2 — 달리는 동안 경직 게이지가 차 무너지면 달리기도 끝난다(계획이 끝나는 것과 같다 · §3.2). 1타 한 대로 무너뜨리는 파이터가 달려오는
        // 보스를 친다 — 칼이 설 때 보스가 앞 120 쯤에 있게 선딜만큼 앞서 한 번 누른다(기준 파이터의 칼은 앞 90 · 보스 반폭 85 라 175 안이면 닿는다).
        // 첫 동작은 안 서고 다음 계획을 고른다.
        FighterConfig breaker = TestConfigs.Breaker();
        BossConfig boss = TestConfigs.Boss(maxHealth: 999_999, runStop: 0);
        BattleSim sim = Sim([new ScriptPlan(0.4, "3연격", Run: true)], boss, breaker);
        double lead = BattleSim.TicksFor(breaker.Combo[0].Windup) * boss.RunSpeed * BattleSim.Dt;
        int plans = sim.Plans.Count;
        bool pressed = false;

        for (int i = 0; i < 60 * 5 && !sim.Boss.Exhausted; i++)
        {
            bool press = !pressed && sim.BossRunning && Math.Abs(sim.Boss.X - sim.Fighter.X) - lead <= 120;
            pressed |= press;
            sim.Tick(press ? _attack : default);
        }

        sim.Boss.Exhausted.ShouldBeTrue("달리는 보스가 안 무너졌다");
        (sim.BossRunning, sim.Boss.CurrentPattern).ShouldBe((false, null));
        sim.Drawn.ShouldBeEmpty("무너진 달리기 뒤에 첫 동작이 섰다");
        sim.Plans.Count.ShouldBe(plans + 1, "탈진에 들며 다음 계획을 안 골랐다");
    }
}
