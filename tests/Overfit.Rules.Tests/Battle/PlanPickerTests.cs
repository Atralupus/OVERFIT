using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 계획 고르기 (설계 2026-09-29 조각1 §3.5 · §4.1 · §4.2 — 옛 이름 PatternPickerTests · #72). 보스는 다음을 고를 때 계획(쉬기 · 첫 동작 · 캔슬
/// 지점 · 잇는 동작)을 통째로 고른다. 등록표(<see cref="PatternPickers"/>)가 <c>stages.json</c> 의 <c>picker</c> id 로 구현을 세운다 —
/// <c>uniform</c>(결정마다 제 스트림의 무작위)과 <c>script</c>(대본 · #78)가 있다. 예측 망은 조각 4 에서 구현 하나를 더한다.
/// </summary>
public class PlanPickerTests
{
    /// <summary>지점이 있는 둘(3연격 · 빠른 3연격)과 없는 둘(점프 공격 · 잡기).</summary>
    private static readonly string[] _roster = ["3연격", "점프 공격", "빠른 3연격", "잡기"];

    private static readonly int[] _rest = [24, 48, 72];

    private static readonly Dictionary<string, PatternDef> _patterns = TestConfigs.Patterns();

    private static PickerInputs Inputs(
        ulong seed, int cancelPercent = 50, IReadOnlyList<int>? rest = null, IReadOnlyList<ScriptPlan>? script = null, int runPercent = 50) =>
        new(_roster, _patterns, rest ?? _rest, new PickerBalance { CancelPercent = cancelPercent, RunPercent = runPercent }, Array.Empty<AttemptRecord>(),
            seed, script);

    /// <summary>계획 번호 <paramref name="number"/> 의 부름 — 두 고르기는 번호만 읽는다.</summary>
    private static PlanRequest Request(int number) => new(number, 0, Array.Empty<DodgeEvent>(), 0, 1, 0);

    private static bool HasPoints(int move) => _patterns[_roster[move]].CancelPoints is { Count: > 0 };

    [Fact]
    public void 무작위_계획의_첫_동작은_옛_uniform_의_좌표다()
    {
        // 설계 2026-09-29 조각1 §3.5 — 첫 동작은 옛 uniform 의 좌표(pattern_pick · k1 = 번호) 그대로다. 끊나 · 어디서 · 무엇으로 · 쉬기는 제 스트림이라
        // 첫 동작을 안 민다 — 계획을 끼운 것만으로는 옛 뽑기의 첫 동작 순서가 안 움직인다.
        foreach (ulong seed in new ulong[] { 0, 51, ulong.MaxValue, Det.Hash64(51, Det.Domain.Attempt, k1: 1) })
        {
            var picker = new UniformPlanPicker(Inputs(seed));
            for (int k = 0; k < 500; k++)
            {
                picker.Next(Request(k)).Move.ShouldBe(Det.RollInt(seed, Det.Domain.PatternPick, _roster.Length, k1: k));
            }
        }
    }

    [Fact]
    public void 잇는_동작은_첫_동작과_다르고_지점은_그_동작의_것이다()
    {
        // 설계 §3.2 — 같은 것을 이으면 1타를 한 번 더 할 뿐이고, 사례를 가르는 InstanceTracker 가 id 가 바뀌어야 새 사례로 센다. 지점은 첫 동작의
        // 목록의 칸이다. 잇는 동작은 첫 동작을 뺀 명부 전부에서 나온다 — 지점이 없는 동작도 이어진다(한 번에 끝나는 동작이 캔슬의 끝이다).
        var picker = new UniformPlanPicker(Inputs(51));
        var nexts = new HashSet<int>();
        int cancels = 0;
        for (int k = 0; k < 1000; k++)
        {
            BossPlan plan = picker.Next(Request(k));
            (plan.CancelPoint is null).ShouldBe(plan.Next is null, $"{k}번: 지점과 잇는 동작 중 하나만 있다");
            if (plan is not { CancelPoint: int point, Next: int next })
            {
                continue;
            }

            cancels++;
            next.ShouldNotBe(plan.Move, $"{k}번: 잇는 동작이 첫 동작과 같다");
            next.ShouldBeInRange(0, _roster.Length - 1);
            point.ShouldBeInRange(0, _patterns[_roster[plan.Move]].CancelPoints!.Count - 1, $"{k}번: 지점이 {_roster[plan.Move]} 의 것이 아니다");
            nexts.Add(next);
        }

        cancels.ShouldBeGreaterThan(0, "1000번에 한 번도 안 끊었다 — 이 가드가 아무것도 안 본다");
        nexts.Order().ShouldBe(Enumerable.Range(0, _roster.Length), "잇는 동작이 명부를 다 덮지 않는다");
    }

    [Fact]
    public void 지점이_없는_동작은_안_끊는다()
    {
        // 점프 공격 · 잡기는 지점이 없다(우산 §3.1) — 늘 끊는 수치(100)여도 그 동작의 계획에는 캔슬이 없다.
        var picker = new UniformPlanPicker(Inputs(51, cancelPercent: 100));
        int seen = 0;
        for (int k = 0; k < 500; k++)
        {
            BossPlan plan = picker.Next(Request(k));
            if (!HasPoints(plan.Move))
            {
                seen++;
                (plan.CancelPoint, plan.Next).ShouldBe((null, null), $"{k}번: 지점이 없는 {_roster[plan.Move]} 을 끊었다");
            }
        }

        seen.ShouldBeGreaterThan(0, "지점이 없는 동작이 한 번도 안 나왔다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void Cancel_percent_가_0_이면_안_끊고_100_이면_지점이_있는_동작은_늘_끊는다()
    {
        // 설계 §3.5 — 끊나는 RollInt(…, 100) < cancel_percent 다. 0 은 "안 끊는다", 100 은 "지점이 있으면 늘 끊는다" 로 뜻이 있다. 50 이면 지점이
        // 있는 동작의 계획 중 대략 절반이 끊긴다.
        var never = new UniformPlanPicker(Inputs(51, cancelPercent: 0));
        var always = new UniformPlanPicker(Inputs(51, cancelPercent: 100));
        var half = new UniformPlanPicker(Inputs(51, cancelPercent: 50));
        int pointed = 0, halfCut = 0;
        for (int k = 0; k < 1000; k++)
        {
            never.Next(Request(k)).CancelPoint.ShouldBeNull($"{k}번: 0 인데 끊었다");
            BossPlan plan = always.Next(Request(k));
            (plan.CancelPoint is not null).ShouldBe(HasPoints(plan.Move), $"{k}번: 100 인데 지점이 있는 {_roster[plan.Move]} 을 안 끊었다");
            if (HasPoints(plan.Move))
            {
                pointed++;
                halfCut += half.Next(Request(k)).CancelPoint is null ? 0 : 1;
            }
        }

        ((double)halfCut / pointed).ShouldBeInRange(0.4, 0.6, $"50 인데 지점이 있는 계획 {pointed} 중 {halfCut} 을 끊었다");
    }

    [Fact]
    public void 쉬는_틱은_목록에서_고른다()
    {
        var picker = new UniformPlanPicker(Inputs(51));

        Enumerable.Range(0, 300).Select(k => picker.Next(Request(k)).RestTicks).Distinct().Order().ShouldBe(_rest);
    }

    [Fact]
    public void 같은_번호는_같은_계획이다()
    {
        // 좌표가 번호에 매여 있다(k1) — 부른 순서와 무관하게 같은 번호는 같은 계획이다. 거꾸로 불러도 같다.
        var forward = new UniformPlanPicker(Inputs(51));
        var backward = new UniformPlanPicker(Inputs(51));

        BossPlan[] a = Enumerable.Range(0, 200).Select(k => forward.Next(Request(k))).ToArray();
        BossPlan[] b = Enumerable.Range(0, 200).Reverse().Select(k => backward.Next(Request(k))).Reverse().ToArray();

        b.ShouldBe(a);
    }

    [Fact]
    public void 한_결정의_칸_수가_바뀌어도_다른_결정의_좌표는_안_밀린다()
    {
        // 설계 §3.5 — 결정마다 도메인이 따로라 쉬는 길이를 하나 더해도(칸 수가 바뀌어도) 첫 동작 · 끊나 · 지점 · 잇는 동작은 그대로다.
        var three = new UniformPlanPicker(Inputs(51));
        var four = new UniformPlanPicker(Inputs(51, rest: [24, 48, 72, 96]));

        for (int k = 0; k < 300; k++)
        {
            BossPlan a = three.Next(Request(k));
            BossPlan b = four.Next(Request(k));
            (b.Move, b.CancelPoint, b.Next).ShouldBe((a.Move, a.CancelPoint, a.Next), $"{k}번: 쉬기의 칸 수가 다른 결정을 밀었다");
        }
    }

    [Fact]
    public void 대본은_계획을_차례로_돌고_끝나면_처음부터다()
    {
        // 설계 §4.2 — 대본은 계획의 목록이고 칸마다 다섯을 다 적는다(쉬기 초 · 첫 동작 · 지점 · 잇는 동작 · 달리기). 끝나면 처음부터 다시 돈다 —
        // uniform 과 같이 상태가 없다(번호로 조회만 한다).
        IPlanPicker picker = PatternPickers.Create(
            "script", Inputs(51, script: [new ScriptPlan(0.4, "잡기", Run: true), new ScriptPlan(1.2, "3연격", 1, "점프 공격")])).ShouldNotBeNull();

        Enumerable.Range(0, 4).Select(k => picker.Next(Request(k))).ShouldBe(new[]
        {
            new BossPlan(24, 3, null, null, Run: true),
            new BossPlan(72, 0, 1, 1),
            new BossPlan(24, 3, null, null, Run: true),
            new BossPlan(72, 0, 1, 1),
        });
    }

    [Fact]
    public void 달리기는_run_percent_로_고른다()
    {
        // 설계 2026-09-29 조각1 §5.2 — 달리나는 제 스트림(plan_run · 15)의 RollInt(…, 100) 을 run_percent 와 견준다. 0 이면 안 달리고 100 이면 늘
        // 달린다. 제 스트림이라 run_percent 를 바꿔도 다른 결정(첫 동작 · 쉬기 · 끊기)이 안 움직인다.
        foreach (ulong seed in new ulong[] { 0, 51, ulong.MaxValue })
        {
            var never = new UniformPlanPicker(Inputs(seed, runPercent: 0));
            var half = new UniformPlanPicker(Inputs(seed, runPercent: 50));
            var always = new UniformPlanPicker(Inputs(seed, runPercent: 100));
            int runs = 0;
            for (int k = 0; k < 500; k++)
            {
                BossPlan n = never.Next(Request(k));
                BossPlan h = half.Next(Request(k));
                (n.Run, always.Next(Request(k)).Run).ShouldBe((false, true), $"{k}번");
                h.Run.ShouldBe(Det.RollInt(seed, Det.Domain.PlanRun, 100, k1: k) < 50, $"{k}번");
                (h with { Run = false }).ShouldBe(n, $"{k}번: 달리기를 고른 것이 다른 결정을 밀었다");
                runs += h.Run ? 1 : 0;
            }

            runs.ShouldBeInRange(200, 300, "반쯤 달려야 한다");
        }
    }

    [Fact]
    public void 대본의_명부_밖_동작이나_없는_지점은_세울_때_거절한다()
    {
        // 설계 §4.2 — 대본은 사람이 손으로 쓰는 것이라 틀리면 판을 세우는 자리에서 바로 멈춰야 한다. 이 예외를 StageRoster.Setup 이 [E] 로 바꿔 판을
        // 세우지 않는다(StageRosterTests). 틀린 칸은 전부 싣는다.
        ArgumentException thrown = Should.Throw<ArgumentException>(() => new ScriptPlanPicker(_roster, _patterns,
        [
            new ScriptPlan(0.8, "돌진"),
            new ScriptPlan(0.8, "잡기", 0, "3연격"),
            new ScriptPlan(0.8, "3연격", 0, "3연격"),
            new ScriptPlan(0.8, "3연격", 0),
            new ScriptPlan(0, "3연격"),
            new ScriptPlan(0.8, "3연격", 2, "잡기"),
            new ScriptPlan(0.8, "3연격", 1, "잡기"),
        ]));

        thrown.Message.ShouldContain("0번 칸 — 돌진 가 명부나 정의에 없다");
        thrown.Message.ShouldContain("1번 칸 — 잡기 에 캔슬 지점 0 이 없다");
        thrown.Message.ShouldContain("2번 칸 — 잇는 동작이 첫 동작(3연격)과 같다");
        thrown.Message.ShouldContain("3번 칸 — 지점과 잇는 동작 중 하나만 적었다");
        thrown.Message.ShouldContain("4번 칸 — 쉬기 0초");
        thrown.Message.ShouldContain("5번 칸 — 3연격 에 캔슬 지점 2 이 없다");
        thrown.Message.ShouldNotContain("6번 칸", Case.Sensitive, "맞는 칸까지 거절했다");
        Should.Throw<ArgumentException>(() => new ScriptPlanPicker(_roster, _patterns, Array.Empty<ScriptPlan>()));
        Should.Throw<ArgumentException>(() => PatternPickers.Create("script", Inputs(51)), "대본 없이 script 를 세웠다");
    }

    [Fact]
    public void 등록표는_uniform_과_script_를_세우고_모르는_고르기는_안_세운다()
    {
        PatternPickers.Ids.ShouldBe(new[] { "uniform", "script" }, ignoreOrder: true);
        PatternPickers.Create("uniform", Inputs(51)).ShouldBeOfType<UniformPlanPicker>();
        PatternPickers.Create("없는고르기", Inputs(51)).ShouldBeNull();
    }

    [Fact]
    public void 대본으로_선_판은_대본의_계획대로_동작을_세운다()
    {
        // 대본이 판의 한 자리(PlanFlow)를 그대로 탄다 — 판을 세울 때 넘기면 그 판의 동작이 대본 순서대로 선다(시드와 무관하다). 판이 선 동작의
        // 순서를 스스로 싣는다(#112 · 설계 2026-09-28 §6.5) — 동작이 서는 틱마다 한 칸이다. 가만히 선 파이터는 착지(24)와 곁에 내린 보스의 3연격을
        // 맞으므로 여섯 동작을 버틸 실제 캐릭터(체력 220)로 선다 — 기준 파이터(100)는 다섯째 앞에서 쓰러진다.
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter],
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = _roster,
            Patterns = _patterns,
            Seed = 51,
            Picker = new ScriptPlanPicker(_roster, _patterns, [new ScriptPlan(0.8, "점프 공격"), new ScriptPlan(0.8, "점프 공격"), new ScriptPlan(0.8, "3연격")]),
            MaxTicks = 60 * 30,
        });

        List<string> begins = Begins(sim, 6);

        begins.ShouldBe(new[] { "점프 공격", "점프 공격", "3연격", "점프 공격", "점프 공격", "3연격" });
        sim.Drawn.Take(6).ShouldBe(begins);
    }

    [Fact]
    public void 고르기를_안_주면_시드_위의_uniform_이고_끊지도_달리지도_않는다()
    {
        // BattleSetup 의 고르기 칸은 선택이다 — 비우면 (Seed, PatternIds) 위의 uniform 인데 끊지도 달리지도 않는다(cancel_percent · run_percent 0).
        // 그래서 고르기를 모르는 테스트의 판들이 캔슬 · 달리기 없이 선다. 쉬기는 보스의 rest_seconds 에서 고른다. 입력 없이 끝까지 돌려 선 동작의
        // 순서를 견준다.
        BattleSim Sim(IPlanPicker? picker) => new(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(),
            PatternIds = _roster,
            Patterns = _patterns,
            Seed = 51,
            Picker = picker,
            MaxTicks = TestConfigs.MaxTicks(),
        });

        BattleSim none = Sim(null);
        BattleSim uniform = Sim(new UniformPlanPicker(Inputs(51, cancelPercent: 0, rest: BattleSim.RestTicks(TestConfigs.Boss()), runPercent: 0)));
        List<string> a = Begins(none, int.MaxValue);
        List<string> b = Begins(uniform, int.MaxValue);

        a.Count.ShouldBeGreaterThan(3);
        a.ShouldBe(b);
        none.Cancels.ShouldBeEmpty();
        none.Plans.ShouldBe(uniform.Plans);
    }

    /// <summary>판을 밀며 동작이 설 때마다 그 id 를 모은다 — <paramref name="count"/> 개가 모이거나 판이 끝날 때까지.</summary>
    private static List<string> Begins(BattleSim sim, int count)
    {
        var begins = new List<string>();
        string? last = null;
        while (begins.Count < count && sim.Tick(default) is null)
        {
            if (sim.Boss.CurrentPattern is { } id && id != last)
            {
                begins.Add(id);
            }

            last = sim.Boss.CurrentPattern;
        }

        return begins;
    }
}
