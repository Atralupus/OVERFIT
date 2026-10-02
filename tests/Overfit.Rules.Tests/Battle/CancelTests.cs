using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 캔슬과 계획의 흐름 (설계 2026-09-29 조각1 §3.2 · §3.3 · §4.1). 실제 캐릭터 · 실제 보스 · 실제 동작에 대본(<see cref="ScriptPlanPicker"/>)으로
/// 계획을 고정한다. 틱은 <b>러너의 틱</b>이다 — 동작이 선 판의 틱이 B 면 판의 B + p 틱에 러너가 p 틱에 든다(첫 틱 1). 캔슬 지점이 78 이면 러너가
/// 78 에 들 판의 틱(B + 78)에 끊는다.
///
/// <para>
/// 판은 파이터 480 · 보스 1440 에서 서고, 보스는 첫 계획의 쉬기(0.8초 = 48틱) 동안 제자리라(설계 2026-09-29 조각1 §5.1) 1440 에서 첫 동작을
/// 세운다. 보스는 안 죽는다.
/// </para>
/// </summary>
public class CancelTests
{
    private static readonly InputFrame _dash = new(0, false, true, false);
    private static readonly InputFrame _attack = new(0, false, false, Attack: true);
    private static readonly InputFrame _right = new(1, false, false, false);

    private static FighterConfig Real() => TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter];

    /// <summary>명부 <paramref name="roster"/>(없으면 3연격 · 돌진) 위에 대본 <paramref name="script"/> 를 얹은 판.</summary>
    private static BattleSim Sim(ScriptPlan[] script, string[]? roster = null, FighterConfig? fighter = null)
    {
        string[] ids = roster ?? ["3연격", "돌진"];
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        return new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = fighter ?? Real(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = ids,
            Patterns = patterns,
            Seed = 51,
            Picker = new ScriptPlanPicker(ids, patterns, script),
            MaxTicks = TestConfigs.MaxTicks(),
        });
    }

    /// <summary>3연격을 1타 뒤(1.30초 · 78틱)에 끊고 돌진으로 잇는 계획 하나 — 대본이 되풀이한다.</summary>
    private static readonly ScriptPlan[] _tripleToRush = [new(0.8, "3연격", 0, "돌진")];

    /// <summary><paramref name="pattern"/> 이 설 때까지 민다 — 선 판의 틱(B)을 돌려준다. <paramref name="before"/> 는 판의 틱을 받는다.</summary>
    private static int UntilBegins(BattleSim sim, string pattern, Func<int, InputFrame>? before = null)
    {
        for (int i = 0; i < 60 * 30 && sim.Boss.CurrentPattern != pattern; i++)
        {
            sim.Tick(before?.Invoke(sim.Ticks + 1) ?? default);
        }

        sim.Boss.CurrentPattern.ShouldBe(pattern, $"{pattern} 이 안 섰다");
        return sim.Ticks;
    }

    /// <summary>러너의 <paramref name="p"/> 틱이 될 판의 틱까지 민다 — 틱마다 <paramref name="input"/> 이 그 러너 틱의 입력을 낸다.</summary>
    private static void UntilTick(BattleSim sim, int begun, int p, Func<int, InputFrame>? input = null)
    {
        for (int i = 0; i < 60 * 30 && sim.Ticks < begun + p; i++)
        {
            sim.Tick(input?.Invoke(sim.Ticks + 1 - begun) ?? default);
        }

        sim.Ticks.ShouldBe(begun + p);
    }

    /// <summary>
    /// 1타 사거리 안(보스와 358 떨어진 1082)으로 걸어 들어가는 입력 — 판이 선 뒤 86틱 동안 오른쪽을 누른다(602px). 첫 동작(48틱에 선다)의 1타
    /// 창(51틱)보다 한참 앞에 선다.
    /// </summary>
    private static InputFrame WalkIn(int tick) => tick <= 86 ? _right : default;

    /// <summary>
    /// 칼이 닿는 자리(보스와 266 떨어진 1174)까지 걸어 들어가는 입력 — 판이 선 뒤 99틱 동안 오른쪽을 누른다(693px). 1타의 궤적(앞 230)이 보스
    /// 몸(반폭 85)에 닿는다. 걸어 들어가는 동안 3연격(48틱에 선다)이 선다.
    /// </summary>
    private static InputFrame Closer(int tick) => tick <= 99 ? _right : default;

    [Theory]
    [InlineData(0, 78)]
    [InlineData(1, 144)]
    public void 캔슬_지점의_틱에_하던_동작이_끝나고_쉬지_않고_잇는_동작이_선다(int point, int at)
    {
        // 설계 §3.2 — 캔슬 지점의 틱에 러너는 그 단계(다음 타의 선딜)에 들지 않고, 하던 동작을 걷고 · 돌아서고 · 잇는 동작을 그 틱에 세운다. 쉬지
        // 않는다 — 보스가 동작 없이 선 틱이 한 틱도 없다. 잇는 동작은 다음 틱에 첫 단계에 든다(돌진의 run). 3연격의 다음 타(attack2 · attack3)는
        // 한 장도 안 그려진다.
        BattleSim sim = Sim([new ScriptPlan(0.8, "3연격", point, "돌진")]);
        int begun = UntilBegins(sim, "3연격");
        using var log = new LogCapture();
        for (int p = 1; p < at; p++)
        {
            UntilTick(sim, begun, p);
            sim.Boss.CurrentPattern.ShouldBe("3연격", $"{p}틱: 캔슬 지점 전에 동작이 바뀌었다");
        }

        string before = sim.BossStep.ShouldNotBeNull().Anim ?? "";
        UntilTick(sim, begun, at);
        sim.Boss.CurrentPattern.ShouldBe("돌진", $"캔슬 지점({at}틱)에 잇는 동작이 안 섰다");
        sim.BossStep.ShouldBeNull("잇는 동작이 선 틱에 벌써 단계에 들었다 — 첫 단계는 다음 틱이다");
        sim.Cancels.ShouldBe(new[] { ("3연격", "돌진") });
        sim.Drawn.ShouldBe(new[] { "3연격", "돌진" });

        UntilTick(sim, begun, at + 1);
        sim.BossStep.ShouldNotBeNull().Anim.ShouldBe("run", "다음 틱에 돌진의 첫 단계에 안 들었다");
        before.ShouldBe(point == 0 ? "attack" : "attack2", "끊기 앞 틱의 그림이 앞 타의 후딜이 아니다");
        log.Lines.ShouldContain($"[boss][D] cancel id=3연격 at={at} next=돌진 facing=-1 tick={begun + at}");
    }

    [Fact]
    public void 캔슬하면_파이터_쪽으로_돌아선다()
    {
        // 설계 §3.2 ② — 방향 잠금은 동작이 도는 동안의 것이라(Boss.Face 의 가드) 동작 사이인 캔슬의 틱에는 풀린다. 1타 창이 열리는 틱(51)에 보스 쪽으로
        // 대시해 무적으로 흘리며 보스를 뚫고 등 뒤에 선 사람 — 1타 뒤 후딜 동안(52 ~ 77틱)은 보스가 그대로 왼쪽을 보고, 끊는 틱(78)에 오른쪽으로
        // 돌아서 잇는 동작을 그쪽으로 세운다.
        BattleSim sim = Sim(_tripleToRush);
        int begun = UntilBegins(sim, "3연격", WalkIn);
        UntilTick(sim, begun, 77, p => p == 51 ? _dash : WalkIn(begun + p));

        sim.Fighter.X.ShouldBeGreaterThan(sim.Boss.X, "대시가 보스를 못 뚫었다 — 이 테스트가 등 뒤를 안 본다");
        sim.Boss.Facing.ShouldBe(-1, "동작 중에 돌아섰다 — 방향 잠금이 풀렸다");

        UntilTick(sim, begun, 78);
        sim.Boss.CurrentPattern.ShouldBe("돌진");
        sim.Boss.Facing.ShouldBe(1, "끊는 틱에 등 뒤의 파이터 쪽으로 안 돌아섰다");
    }

    [Fact]
    public void 잇는_동작이_끝나면_다음_계획의_쉬기부터다()
    {
        // 설계 §3.3 — 흐름은 쉬기 → 첫 동작 → 끊고 잇기 → 다음 계획이다. 잇는 동작이 끝나는 틱(E)에 다음 계획을 고르고, 그 쉬기(0.8초 = 48틱)가
        // 지나야 다음 첫 동작이 선다 — 그동안 보스는 동작 없이 쉰다.
        BattleSim sim = Sim(_tripleToRush);
        UntilBegins(sim, "돌진");
        for (int i = 0; i < 60 * 10 && sim.Boss.CurrentPattern is not null; i++)
        {
            sim.Tick(default);
        }

        int ended = sim.Ticks;
        sim.Boss.CurrentPattern.ShouldBeNull("돌진이 안 끝났다");
        int next = UntilBegins(sim, "3연격");

        (next - ended).ShouldBe(48, "잇는 동작 뒤 다음 계획의 쉬기가 0.8초(48틱)가 아니다");
        sim.Plans.Count.ShouldBe(2, "계획이 둘이 아니다 — 첫 계획과 잇는 동작 뒤의 다음 계획이다");
    }

    [Theory]
    [InlineData(0.4, 24)]
    [InlineData(0.8, 48)]
    [InlineData(1.2, 72)]
    public void 판이_서면_첫_계획의_쉬기부터다(double rest, int tick)
    {
        // 설계 §3.4 — 판이 서면 첫 계획의 쉬기부터다(옛 간격 0.8초 고정 대신 계획이 고른 길이). 첫 동작은 쉬기의 마지막 틱에 선다.
        BattleSim sim = Sim([new ScriptPlan(rest, "3연격")]);

        UntilBegins(sim, "3연격").ShouldBe(tick);
    }

    [Fact]
    public void 캔슬_전에_탈진하면_잇는_동작은_안_선다()
    {
        // Review Focus 1 · 설계 §3.2 — 탈진하면 계획이 끝난다: 남은 캔슬 · 잇는 동작은 버린다. 1타의 경직도를 게이지 끝까지 키운 캐릭터가
        // 칼이 닿는 자리까지 걸어 들어가(판의 99틱 · 3연격의 51틱) 캔슬 지점(78틱) 앞인 55틱에 1타 한 번으로 보스를 무너뜨린다(90틱). 보스의
        // 1타(51틱)는 맞는다 — 맞아도 칼질은 안 끊긴다. 전에는 1타 창 앞에 누른 K 가 받아쳐
        // 무너뜨렸다 — 패리는 #168 에서 걷었다. 탈진에 드는 틱에 다음 계획을 고르고, 탈진이 풀린 뒤 그 쉬기(48틱)가 지나야 다음 첫 동작(3연격)이
        // 선다 — 돌진은 한 번도 안 서고 캔슬도 없다. 탈진이 풀리는 틱(무너진 틱 + 90)이 쉬기의 첫 틱이라 무너진 틱에서 90 + 48 − 1 = 137틱 뒤다.
        FighterConfig breaker = Real();
        breaker.Combo[0] = TestConfigs.Step(breaker.Combo[0], poise: 1000);
        BattleSim sim = Sim(_tripleToRush, fighter: breaker);
        int begun = UntilBegins(sim, "3연격", Closer);
        using var log = new LogCapture();
        int broke = 0;
        for (int i = 0; i < 70 && broke == 0; i++)
        {
            int p = sim.Ticks + 1 - begun;
            sim.Tick(p == 55 ? _attack : Closer(sim.Ticks + 1));
            broke = sim.Boss.Exhausted ? sim.Ticks : 0;
        }

        broke.ShouldBeGreaterThan(0, "1타가 게이지를 못 채웠다 — 이 테스트가 탈진을 안 본다");
        (broke - begun).ShouldBeLessThan(78, "캔슬 지점 뒤에 무너졌다 — 이 테스트가 캔슬 전의 탈진을 안 본다");
        int exhausted = log.Lines.Count(l => l.StartsWith("[boss][D] exhaust cause=poise ", StringComparison.Ordinal));
        exhausted.ShouldBe(1);

        int next = UntilBegins(sim, "3연격");

        sim.Drawn.ShouldBe(new[] { "3연격", "3연격" }, "탈진 뒤에 잇는 동작이 섰다");
        sim.Cancels.ShouldBeEmpty("탈진한 계획의 캔슬이 쓰였다");
        sim.Plans.Count.ShouldBe(2, "탈진에 드는 틱에 다음 계획을 안 골랐다");
        log.Lines.ShouldNotContain(l => l.StartsWith("[boss][D] cancel ", StringComparison.Ordinal));
        (next - broke).ShouldBe(90 + 48 - 1, "탈진(90틱)이 풀린 틱부터 다음 계획의 쉬기(48틱)를 세지 않았다");
    }

    [Fact]
    public void 잇는_동작은_지점이_있어도_끝까지_간다()
    {
        // 설계 §3.2 — 계획 하나에 캔슬 한 번. 3연격을 1타 뒤에 끊고 빠른 3연격으로 이으면, 빠른 3연격은 제 지점(36 · 69틱)이 있어도 끊기지 않고 세
        // 타를 다 친다(끝 138틱).
        BattleSim sim = Sim([new ScriptPlan(0.8, "3연격", 0, "빠른 3연격")], ["3연격", "빠른 3연격"]);
        int fast = UntilBegins(sim, "빠른 3연격");
        UntilTick(sim, fast, 137);

        sim.Boss.CurrentPattern.ShouldBe("빠른 3연격", "잇는 동작이 끝나기 전에 끊겼다");
        UntilTick(sim, fast, 138);
        sim.Boss.CurrentPattern.ShouldBeNull("잇는 동작이 138틱에 안 끝났다");
        sim.Cancels.ShouldBe(new[] { ("3연격", "빠른 3연격") });
    }

    [Fact]
    public void 사례는_첫_동작과_잇는_동작_둘이다()
    {
        // 설계 §3.2 — 잇는 동작은 첫 동작과 달라 사례를 가르는 InstanceTracker 가 id 가 바뀐 틱에 새 사례로 센다(쉬는 틱이 없어도 — 패턴이 간격 없이
        // 바뀌는 자리를 트래커가 처음 만난다). 멀리 선 사람(480)은 3연격의 1타를 사거리 밖에서 흘리고(안 맞음) 달려온 돌진의 3타를 맞는다(맞음).
        BattleSim sim = Sim(_tripleToRush);
        var tracker = new InstanceTracker();
        for (int i = 0; i < 60 * 10 && !(sim.Drawn.Count == 2 && sim.Boss.CurrentPattern is null); i++)
        {
            sim.Tick(default);
            tracker.Observe(sim.Boss.CurrentPattern, sim.Events.Count);
        }

        tracker.Finish(sim.Events).ShouldBe(new[] { new PatternInstance("3연격", false), new PatternInstance("돌진", true) });
    }

    [Theory]
    [InlineData(48, 99, null, null, "reason=move_out_of_range move=99 roster=2")]
    [InlineData(0, 0, null, null, "reason=rest rest=0")]
    [InlineData(48, 0, 0, 0, "reason=next_same id=3연격")]
    [InlineData(48, 0, 5, 1, "reason=point_missing id=3연격 point=5 points=2")]
    [InlineData(48, 1, 0, 0, "reason=point_missing id=돌진 point=0 points=0")]
    [InlineData(48, 0, 0, null, "reason=half_cancel cancel=0 next=-")]
    public void 틀린_계획은_계획마다_E_한_줄이고_판은_끝까지_간다(int rest, int move, int? point, int? next, string reason)
    {
        // Review Focus 5 · 설계 §4.1 — 고르기가 틀린 계획을 내면(조각 4 의 망은 데이터를 읽어 올 수 있는 자리다) [E] plan_invalid 를 남기고 그 계획을
        // 버린다. 가장 짧은 쉬기(0.4초 = 24틱) 뒤 다음 번호로 다시 고른다 — 매 틱 쏟지 않는다. 판은 끝까지 간다(시간 초과로 진다). 200틱이면
        // 0 · 24 · … · 192틱의 아홉 번이다.
        using var log = new LogCapture();
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(),
            PatternIds = ["3연격", "돌진"],
            Patterns = TestConfigs.Patterns(),
            Seed = 51,
            Picker = new Fixed(new BossPlan(rest, move, point, next)),
            MaxTicks = 200,
        });

        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(default);
            sim.Boss.CurrentPattern.ShouldBeNull("틀린 계획의 동작이 섰다");
        }

        outcome.ShouldBe(BattleOutcome.Lose);
        List<string> errors = log.Lines.Where(l => l.StartsWith("[boss][E] plan_invalid", StringComparison.Ordinal)).ToList();
        errors.Count.ShouldBe(9, "가장 짧은 쉬기마다 한 번이 아니다(판을 세울 때 · 24 · 48 · … · 192틱)");
        errors[0].ShouldBe($"[boss][E] plan_invalid n=0 {reason} tick=0");
        errors[1].ShouldBe($"[boss][E] plan_invalid n=1 {reason} tick=24");
        sim.Plans.ShouldBeEmpty();
    }

    /// <summary>늘 같은 계획을 내는 고르기 — 틀린 계획을 내게 해서 규칙 위반의 자리를 본다.</summary>
    private sealed class Fixed(BossPlan plan) : IPlanPicker
    {
        public BossPlan Next(PlanRequest request) => plan;
    }
}
