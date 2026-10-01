using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Battle.View;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 일곱 동작이 <b>겨냥한 사람을 실제로 잡는가</b> (설계 2026-09-29 조각1 §2 — 옛 이름 Stage2BattleTests · #78). 실제 캐릭터(fighters.json) ·
/// 실제 보스 · 실제 동작에 대본(<see cref="ScriptPlanPicker"/>)으로 동작을 고정한다. 틱은 <b>패턴의 틱</b>(스펙 §2.5 의 표와 같은 자 · 첫 틱 1)이다:
/// 패턴이 선 판의 틱이 B 면 판의 B + p 틱이 패턴의 p 틱이다.
///
/// <para>
/// 판은 파이터 480 · 보스 1440 에서 서고, 보스는 쉬는 동안 제자리라(설계 2026-09-29 조각1 §5.1) 첫 패턴도 1440 에서 선다(0.8초 = 48틱 뒤).
/// 보스는 안 죽는다(체력 999_999) — 판이 패턴 도중에 끝나지 않게. 대본은 끝나면 처음부터 다시 돈다 — 같은 동작을 둘 잇는 판은 첫째가 끝난
/// 자리에서 둘째를 본다. 보스가 다가와야 서는 판은 계획의 달리기(§5.2)로 세운다.
/// </para>
///
/// <para>
/// 옛 1타 돌진 · 1타 잡기 · 점프 3연속에만 있던 테스트(1타 뒤 경직에 3타 · 1타를 받아치면(패리 · #168 에서 걷었다) 돌진도 잡기도 없다 · 점프 ×3 에 가드 붕괴 등)는
/// 동작과 같이 걷었다 — 그 조합(3연격 1타 뒤에 무엇이 오나)은 캔슬이 받는다(조각1 §3).
/// </para>
/// </summary>
public class MoveBattleTests
{
    private static readonly InputFrame _right = new(1, false, false, false);
    private static readonly InputFrame _dash = new(0, false, true, false);
    private static readonly InputFrame _jump = new(0, true, false, false);
    private static readonly InputFrame _attack = new(0, false, false, Attack: true);
    private static readonly InputFrame _guard = new(0, false, false, false, GuardHeld: true);

    /// <summary>파이터의 한 틱 걸음(px) — 실제 캐릭터와 기준 파이터가 같다(move_speed 420 · 틱당 7).</summary>
    private static readonly double _stride = Real().MoveSpeed * BattleSim.Dt;

    private static FighterConfig Real() => TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter];

    /// <summary>
    /// 보스전의 명부 위에 대본을 얹은 판 — 대본의 칸마다 0.8초 쉬고 끊지 않는다(옛 간격 그대로라 아래 틱들이 그 위에서 잰 값이다). 파이터는
    /// <paramref name="fighter"/> — 없으면 실제 캐릭터다.
    /// </summary>
    private static BattleSim Sim(FighterConfig? fighter, params string[] script) =>
        Sim(fighter, script.Select(id => new ScriptPlan(0.8, id)).ToArray());

    /// <summary>보스전의 명부 위에 계획을 그대로 얹은 판 — 쉬기 · 달리기를 칸마다 적는다.</summary>
    private static BattleSim Sim(FighterConfig? fighter, params ScriptPlan[] plans)
    {
        IReadOnlyList<string> roster = StageRoster.For(TestConfigs.Stages(), 1);
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        return new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = fighter ?? Real(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = roster,
            Patterns = patterns,
            Seed = 51,
            Picker = new ScriptPlanPicker(roster, patterns, plans),
            MaxTicks = TestConfigs.MaxTicks(),
        });
    }

    /// <summary>
    /// <paramref name="pattern"/> 이 설 때까지 <paramref name="before"/> 를 넣으며 민다 — 그 패턴이 선 판의 틱(B)을 돌려준다. 그 뒤 패턴의
    /// p 틱은 판의 B + p 틱이다(러너의 첫 틱은 선 다음 틱이다 · 설계 §3.6 ⑤). <paramref name="before"/> 는 판의 틱을 받는다.
    /// </summary>
    private static int UntilBegins(BattleSim sim, string pattern, Func<int, InputFrame>? before = null)
    {
        for (int i = 0; i < 60 * 30 && sim.Boss.CurrentPattern != pattern; i++)
        {
            sim.Tick(before?.Invoke(sim.Ticks + 1) ?? default);
        }

        sim.Boss.CurrentPattern.ShouldBe(pattern, $"{pattern} 이 안 섰다");
        return sim.Ticks;
    }

    /// <summary>지금 패턴이 끝나고(쉬는 간격) 다음 <paramref name="pattern"/> 이 설 때까지 민다 — 대본이 같은 동작을 다시 세운 틱도 잡는다.</summary>
    private static int UntilNextBegins(BattleSim sim, string pattern, Func<int, InputFrame>? before = null)
    {
        for (int i = 0; i < 60 * 30 && sim.Boss.CurrentPattern is not null; i++)
        {
            sim.Tick(before?.Invoke(sim.Ticks + 1) ?? default);
        }

        return UntilBegins(sim, pattern, before);
    }

    /// <summary>패턴의 <paramref name="p"/> 틱까지(그 틱 포함) 민다 — 틱마다 <paramref name="input"/> 이 그 패턴 틱의 입력을 낸다.</summary>
    private static void UntilTick(BattleSim sim, int begun, int p, Func<int, InputFrame>? input = null)
    {
        for (int i = 0; i < 60 * 30 && sim.Ticks < begun + p; i++)
        {
            sim.Tick(input?.Invoke(sim.Ticks + 1 - begun) ?? default);
        }

        sim.Ticks.ShouldBe(begun + p);
    }

    /// <summary>
    /// 보스 앞 <paramref name="gap"/>(보스 중심에서 파이터 중심까지) 자리로 한 걸음 — 한 걸음 안에 들면 선다. 자리는 한 걸음(7px) 안으로 맞는다.
    /// 물러서면 파이터는 보스를 등진다.
    /// </summary>
    private static InputFrame Toward(BattleSim sim, double gap)
    {
        double off = sim.Boss.X + (sim.Boss.Facing * gap) - sim.Fighter.X;
        return Math.Abs(off) < _stride ? default : new InputFrame((sbyte)Math.Sign(off), false, false, false);
    }

    /// <summary>
    /// 1타 사거리 안(보스와 358 떨어진 1082)으로 걸어 들어가는 입력 — 판이 선 뒤 86틱 동안 오른쪽을 누른다(602px). 첫 동작(48틱에 선다)의 1타
    /// 창(51틱)보다 한참 앞에 선다.
    /// </summary>
    private static InputFrame WalkIn(int tick) => tick <= 86 ? _right : default;

    /// <summary>첫 동작의 판정이 닿지 않는 자리(보스 앞 440 — 3연격 · 올려베기가 다 닿는 427 의 밖).</summary>
    private const double _outOfReach = 440;

    [Theory]
    [InlineData(150, 16)]
    [InlineData(150, 35)]
    [InlineData(250, 16)]
    [InlineData(250, 35)]
    [InlineData(400, 16)]
    [InlineData(400, 35)]
    public void 올려베기는_3연격_1타의_박자에_뛴_사람을_어느_거리에서든_잡는다(int gap, int jumpAt)
    {
        // 설계 2026-09-29 조각1 §2.1 — 3연격 1타(51틱)를 보스 앞 어디서든 점프로 넘는 누름은 16 ~ 35틱이다(발이 궤적 윗끝 236.5 위에 누른 틱 + 16 ~
        // + 42 · 아래 대조군). 올려베기는 같은 51틱에 [0, 396, 0, 360] 을 친다 — 점프의 정점에서 발이 300 이라 창(51 ~ 58) 내내 사각형 안이다. 그
        // 누름의 양 끝으로 보스 앞 150 · 250 · 400 어디서 뛰어도 공중에서 맞는다. 첫 올려베기는 사거리 밖(보스 앞 440)에서 헛치게 두고, 창이 닫힌 뒤
        // 걸어가 둘째 앞에서 그 자리에 선다.
        BattleSim sim = Sim(null, "올려베기");
        int first = UntilBegins(sim, "올려베기", _ => Toward(sim, _outOfReach));
        UntilTick(sim, first, 58, _ => Toward(sim, _outOfReach));
        int second = UntilNextBegins(sim, "올려베기", _ => Toward(sim, gap));
        UntilTick(sim, second, jumpAt - 1, _ => Toward(sim, gap));
        double stood = Math.Abs(sim.Boss.X - sim.Fighter.X);
        UntilTick(sim, second, 58, p => p == jumpAt ? _jump : default);

        stood.ShouldBe(gap, _stride, "뛰기 전에 그 자리에 못 섰다");
        sim.Events.Select(e => (e.Verb, e.Verdict)).ShouldBe(new[]
        {
            (DodgeVerb.Spacing, HitVerdict.MissedTooFar),
            (DodgeVerb.Jump, HitVerdict.Hit),
        }, "올려베기가 뛴 사람을 못 잡았다");
        sim.Events[1].Airborne.ShouldBeTrue("땅에서 맞았다 — 이 테스트가 점프를 안 본다");
        sim.Events[1].JumpAvailable.ShouldBeFalse("올려베기를 점프로 넘을 수 있었다고 실렸다");
    }

    [Theory]
    [InlineData(150, 16)]
    [InlineData(150, 35)]
    [InlineData(250, 16)]
    [InlineData(250, 35)]
    [InlineData(400, 16)]
    [InlineData(400, 35)]
    public void 같은_점프가_3연격_1타는_넘는다(int gap, int jumpAt)
    {
        // 위 테스트의 대조군 — 올려베기 자리에 3연격이 오면 같은 자리 · 같은 누름이 1타를 넘는다. 둘을 가르는 것은 선딜의 그림뿐이다(attack2 · attack).
        // 15틱 앞이나 36틱 뒤의 누름은 보스 앞 250 안에서 1타에 맞는다 — 궤적이 가장 높은 자리다(보스 앞 300 밖에서는 12 · 13 틱의 누름도 넘는다).
        BattleSim sim = Sim(null, "올려베기", "3연격");
        int first = UntilBegins(sim, "올려베기", _ => Toward(sim, _outOfReach));
        UntilTick(sim, first, 58, _ => Toward(sim, _outOfReach));
        int second = UntilNextBegins(sim, "3연격", _ => Toward(sim, gap));
        UntilTick(sim, second, jumpAt - 1, _ => Toward(sim, gap));
        UntilTick(sim, second, 59, p => p == jumpAt ? _jump : default);

        sim.Events.Select(e => (e.Verb, e.Verdict)).ShouldBe(new[]
        {
            (DodgeVerb.Spacing, HitVerdict.MissedTooFar),
            (DodgeVerb.Jump, HitVerdict.MissedByHeight),
        }, "같은 점프가 3연격 1타를 못 넘었다 — 대조군이 무너졌다");
    }

    [Theory]
    [InlineData("none", DodgeVerb.None, HitVerdict.Hit)]
    [InlineData("dash", DodgeVerb.Dash, HitVerdict.Dodged)]
    [InlineData("guard", DodgeVerb.Guard, HitVerdict.Guarded)]
    public void 올려베기는_선_사람을_치고_대시_가드로는_받는다(string answer, DodgeVerb verb, HitVerdict verdict)
    {
        // 설계 2026-09-29 조각1 §2.1 — 점프만 잡는 칼이 아니다: 서 있으면 맞고, 선 사람의 답(대시 무적 · 가드)은 다 받는다. 대시는 창의 첫 틱에
        // 누르고 가드는 창 앞부터 붙든다. 보스 앞 250 에 선다.
        BattleSim sim = Sim(null, "올려베기");
        int first = UntilBegins(sim, "올려베기", _ => Toward(sim, _outOfReach));
        UntilTick(sim, first, 58, _ => Toward(sim, _outOfReach));
        int second = UntilNextBegins(sim, "올려베기", _ => Toward(sim, 250));
        UntilTick(sim, second, 40, _ => Toward(sim, 250));
        UntilTick(sim, second, 58, p => answer switch
        {
            "dash" when p == 51 => _dash,
            "guard" => _guard,
            _ => default,
        });

        sim.Events.Count.ShouldBe(2, "둘째 올려베기의 관측이 안 섰다");
        (sim.Events[1].Verb, sim.Events[1].Verdict).ShouldBe((verb, verdict));
    }

    [Theory]
    [InlineData("none", DodgeVerb.None, HitVerdict.Hit)]
    [InlineData("dash", DodgeVerb.Dash, HitVerdict.Hit)]
    [InlineData("guard", DodgeVerb.Guard, HitVerdict.Hit)]
    [InlineData("jump", DodgeVerb.Jump, HitVerdict.MissedByHeight)]
    public void 점프_공격의_착지는_대시_무적도_가드도_맨몸이고_뛴_사람만_넘는다(string answer, DodgeVerb verb, HitVerdict verdict)
    {
        // 설계 2026-09-29 조각1 §2.3 — 유저: "점프공격은 대시로도 안피해지고 점프로만 회피 가능해야합니다." 착지(60틱)의 답 둘이 거짓이라 창의 첫 틱에
        // 누른 대시의 무적도 · 창 앞부터 붙든 가드도 맨몸에 24 를 맞는다. 띠가 아레나 전체라 거리로도 못 피한다. 30틱에 뛴
        // 사람만 넘는다(발이 누른 틱 + 3 ~ + 55 동안 60 위 — 창 60 ~ 67 을 덮는다). 파이터는 480 에 서 있고 보스는 그 앞 115(595)에 내린다.
        BattleSim sim = Sim(null, "점프 공격");
        int begun = UntilBegins(sim, "점프 공격");
        UntilTick(sim, begun, 68, p => answer switch
        {
            "dash" when p == 60 => _dash,
            "guard" => _guard,
            "jump" when p == 30 => _jump,
            _ => default,
        });

        DodgeEvent landing = sim.Events.ShouldHaveSingleItem("착지의 관측이 하나가 아니다");
        (landing.Verb, landing.Verdict).ShouldBe((verb, verdict));
        (landing.DashAvailable, landing.GuardAvailable, landing.JumpAvailable).ShouldBe((false, false, true),
            "관측이 착지의 답과 다른 말을 한다");
        sim.Fighter.Health.ShouldBe(Real().MaxHealth - (verdict == HitVerdict.Hit ? 24 : 0));
    }

    [Theory]
    [InlineData(false, DodgeVerb.Dash, HitVerdict.Dodged)]
    [InlineData(true, DodgeVerb.None, HitVerdict.Hit)]
    public void 빠른_3연격의_1타는_2연격에_묶인_사람의_대시를_버리고_1타만_친_사람은_흘린다(bool chain, DodgeVerb verb, HitVerdict verdict)
    {
        // 설계 2026-09-29 조각1 §3.4 · 이슈 #167 — 동작이 습관을 겨냥하도록 맞춘 박자는 없다. 여기는 칼질에 묶인 사람과 풀린 사람이 같은 대시에 다르게
        // 끝나는 역학만 본다. 3연격의 끝(213) 뒤 짧은 쉬기 24틱 + 달리기 12틱 뒤에 빠른 3연격이 선다. 185틱에 J 를 누르고 2타를 이은 사람(2연격 ·
        // 106틱)은 빠른 3연격이 선 뒤에도 묶여 있어, 1타(24틱)에 누른 대시가 버려지고 맞는다 — 칼질 중이라 욕심으로 남는다. 1타만 친 사람(40틱)은
        // 이미 풀려 같은 대시로 흘린다. 3연격은 사거리 밖(보스 앞 440)에서 헛치게 두고 — 셋 다 피하려면 427 넘게 떨어져야 한다 — 보스가 쉬기 뒤
        // 파이터 앞 280 까지 달려와(160px · 12틱) 빠른 3연격을 연다.
        BattleSim sim = Sim(null, new ScriptPlan(0.8, "3연격"), new ScriptPlan(0.4, "빠른 3연격", Run: true));
        int triple = UntilBegins(sim, "3연격", _ => Toward(sim, _outOfReach));
        UntilTick(sim, triple, 166, _ => Toward(sim, _outOfReach));
        UntilTick(sim, triple, 198, p => p == 185 || (chain && p == 188) ? _attack : default);
        int fast = UntilBegins(sim, "빠른 3연격");
        UntilTick(sim, fast, 31, p => p == 24 ? _dash : default);

        sim.Events.Take(3).ShouldAllBe(e => e.Verdict == HitVerdict.MissedTooFar, "3연격이 사거리 밖에서 헛치지 않았다 — 이 테스트가 칼질을 안 본다");
        DodgeEvent first = sim.Events.Skip(3).ShouldHaveSingleItem("빠른 3연격 1타의 관측이 하나가 아니다");
        (first.Verb, first.Verdict).ShouldBe((verb, verdict));
        first.GreedWindow.ShouldBe(chain, "2연격에 묶인 사람만 욕심으로 남는다");
    }

    [Fact]
    public void 돌진은_멀리_선_사람에게_달려와_도착_뒤_3타를_꽂는다()
    {
        // 설계 2026-09-29 조각1 §2.4 · 설계 §4.6 — 멀리 서서 지켜보는 사람(480)에게 보스(1440 · 쉬는 동안 제자리)가 곧장 달린다: 앞쪽 거리 960 에서
        // 멈출 자리(760)까지 680 은 ⌈680 / 60⌉ = 12틱이라 12틱에 닿는다(A). 달리는 동안 패턴 시계가 1 에 서 있다가 도착 다음 틱부터 다시 가 3타가
        // 시계 24 에 선다 — A + 23 = 35틱이다. 서 있던 사람은 맞는다(3타의 땅 사거리 +80 ~ +404 안).
        BattleSim sim = Sim(null, "돌진");
        int begun = UntilBegins(sim, "돌진");
        sim.Boss.X.ShouldBe(1440, 1e-9);

        UntilTick(sim, begun, 11);
        sim.Boss.X.ShouldBeGreaterThan(760, "11틱에 벌써 닿았다");
        UntilTick(sim, begun, 12);
        sim.Boss.X.ShouldBe(760, 1e-9, "돌진이 12틱에 파이터 앞 280 에 안 멈췄다");
        UntilTick(sim, begun, 34);
        sim.Events.ShouldBeEmpty("3타가 35틱보다 먼저 섰다");
        UntilTick(sim, begun, 35);
        sim.Events.Select(e => (e.Verb, e.Verdict)).ShouldBe(new[] { (DodgeVerb.None, HitVerdict.Hit) }, "3타가 35틱에 안 섰다");
        sim.Fighter.Health.ShouldBe(Real().MaxHealth - 14);
    }

    [Fact]
    public void 돌진은_280_안의_사람에게_24틱에_친다()
    {
        // 설계 2026-09-29 조각1 §2.4 「단독으로 나올 때의 예고」 — 파이터가 280 안이면 돌진은 0틱이고(움직이지 않고 그 틱에 끝난다 · 뒤로도 안 간다)
        // 선딜만 남는다: 3타가 24틱(0.40초 — 빠른 3연격의 첫 타와 같다)에 선다. 옛 0.25초면 15틱이었다. 첫 돌진이 가만히 선 사람(480) 앞 280(760)에
        // 달려와 친 뒤, 보스는 쉬는 동안 그 자리라(설계 2026-09-29 조각1 §5.1) 둘째 돌진은 멈출 거리 그 끝(280 앞)에서 선다.
        BattleSim sim = Sim(null, "돌진");
        UntilBegins(sim, "돌진");
        int second = UntilNextBegins(sim, "돌진");
        double x = sim.Boss.X;
        x.ShouldBe(760, 1e-6, "둘째 돌진이 첫 돌진이 멈춘 자리(760)에서 안 섰다 — 쉬는 동안 움직였다");
        (x - sim.Fighter.X).ShouldBe(280, 1e-6, "둘째 돌진이 280 안에서 안 섰다 — 이 테스트가 0틱 돌진을 안 본다");
        int seen = sim.Events.Count;

        UntilTick(sim, second, 23);
        sim.Boss.X.ShouldBe(x, "280 안의 사람에게 달렸다");
        sim.Events.Count.ShouldBe(seen, "3타가 24틱보다 먼저 섰다");
        UntilTick(sim, second, 24);
        sim.Events.Count.ShouldBe(seen + 1, "3타가 24틱에 안 섰다");
        (sim.Events[^1].Verb, sim.Events[^1].Verdict).ShouldBe((DodgeVerb.None, HitVerdict.Hit));
    }

    [Theory]
    [InlineData("jump", 13, DodgeVerb.Jump, HitVerdict.MissedByHeight)]
    [InlineData("jump", 57, DodgeVerb.Jump, HitVerdict.MissedByHeight)]
    [InlineData("jump", 58, DodgeVerb.Jump, HitVerdict.Grabbed)]
    [InlineData("dash", 56, DodgeVerb.Dash, HitVerdict.Grabbed)]
    public void 잡기는_보고_뛴_사람을_못_잡고_대시한_사람을_붙든다(string answer, int at, DodgeVerb verb, HitVerdict verdict)
    {
        // 설계 2026-09-29 조각1 §2.4 · 이슈 #167 — 흰 구가 나는 선딜(1.0초)이 예고의 전부다. 동작이 선 틱(1)에 보고 반응 0.2초(12틱) 뒤 13틱에 뛴
        // 사람은 넘는다(발이 누른 틱 + 3 ~ + 55 동안 60 위 — 창 60 ~ 67 을 덮는다). 창(60틱)에 닿는 마지막 누름은 57틱이다 — 반응할 틈 57틱(옛
        // 0.60초면 33틱). 한 틱 늦으면 잡힌다. 대시 무적 8틱(56 ~ 63)이 창의 첫 틱을 덮어도 잡힌다 — 잡기는 무적을 안 받는다.
        BattleSim sim = Sim(null, "잡기");
        int begun = UntilBegins(sim, "잡기");
        UntilTick(sim, begun, 68, p => p == at ? (answer == "jump" ? _jump : _dash) : default);

        DodgeEvent grab = sim.Events.ShouldHaveSingleItem("잡기의 관측이 하나가 아니다");
        (grab.Verb, grab.Verdict).ShouldBe((verb, verdict));
        grab.DashAvailable.ShouldBeFalse("잡기가 대시를 받는다고 실렸다");
        sim.Fighter.Held.ShouldBe(verdict == HitVerdict.Grabbed);
        sim.Fighter.Health.ShouldBe(Real().MaxHealth - (verdict == HitVerdict.Grabbed ? 25 : 0));
    }

    [Fact]
    public void 잡기에_죽으면_판이_그_틱에_끝나고_그_잡힘은_관측으로_남는다()
    {
        // Review Focus 4 (#78) — 체력이 25 이하인 채 잡힌다. 가만히 선 사람(480)에게 잡기를 되풀이하면 25씩 닿아 마지막 잡기에 죽는다 — 몇 번째인지는
        // 파이터 체력(fighters.json)에 달려 세지 않는다. 판은 그 틱에 진다(reason=dead) — 죽인 잡기가 닿은 틱이 판이 끝난 틱이고(붙들림 60틱을
        // 기다리지 않는다) 파이터는 붙들린 채다. 판을 끝낸 그 한 대는 닿은 것이라 관측이 있다(설계 §3.6 ③ — 끊기는 것은 남은 창뿐이다): 마지막 관측이
        // Grabbed 이고, 끊긴 창(cut_swing)은 없다.
        BattleSim sim = Sim(null, "잡기");
        using var log = new LogCapture();
        BattleOutcome? outcome = null;
        int grabs = 0, lastGrab = 0;
        for (int i = 0; i < 60 * 90 && outcome is null; i++)
        {
            outcome = sim.Tick(default);
            int now = sim.Events.Count(e => e.Verdict == HitVerdict.Grabbed);
            if (now > grabs)
            {
                (grabs, lastGrab) = (now, sim.Ticks);
            }
        }

        outcome.ShouldBe(BattleOutcome.Lose);
        grabs.ShouldBeGreaterThan(1, "잡기가 한 번에 죽였다 — 체력 25 이하로 시작한 판은 이 테스트가 보려는 것이 아니다");
        sim.Ticks.ShouldBe(lastGrab, "판이 죽인 잡기의 틱에 안 끝났다 — 붙들림이 끝나기를 기다렸다");
        sim.Fighter.Held.ShouldBeTrue("죽인 잡기에 붙들리지 않았다");
        sim.Fighter.Health.ShouldBe(0);
        sim.Events[^1].Verdict.ShouldBe(HitVerdict.Grabbed, "죽인 잡기가 관측으로 안 남았다");
        log.Lines.ShouldContain($"[result][I] lose reason=dead ticks={sim.Ticks} boss_hp={sim.Boss.Health}");
        log.Lines.ShouldNotContain(l => l.StartsWith("[boss][D] cut_swing ", StringComparison.Ordinal), "죽인 잡기를 끊긴 창으로 버렸다");
    }

    [Fact]
    public void 흰_구가_나는_자리는_잡기_창_바로_앞_단계이고_창이_산_동안_기다린다()
    {
        // 설계 §4.7 · §6 「잡기」 — 흰 구가 날고 · 붙들고 · 흩어지는 시각은 규칙의 단계와 잡힘이 정한다(규칙은 흰 구를 모른다). 뷰가 읽는 두 자리를
        // 못박는다. ① 지금 단계 바로 다음이 판정이면 그 판정과 지난 몫(BossHitAhead) — 잡기는 idle 한 장에서 곧장 창이라 동작의 첫 틱(1)부터 난다:
        // 몫은 idle 이 든 틱(1)에서 창(60)까지의 몫이라 첫 틱에 0 · 창 한 틱 앞(59)에 58/59 다. 쉬는 동안은 비었다. ② 잡기 창이 산 동안(GrabLive) —
        // 뛰어넘은 사람에게는 창 8틱(60 ~ 67)이 끝날 때까지 참이고, 흰 구는 그동안 바닥에서 기다렸다 흩어진다. 20틱에 뛰면 발이 23 ~ 75틱에 60 위다.
        BattleSim sim = Sim(null, "잡기");
        for (int i = 0; i < 40; i++)
        {
            sim.Tick(default);
            sim.BossHitAhead.ShouldBeNull($"쉬는 동안({sim.Ticks}틱) 흰 구가 날 자리가 있다");
        }

        int begun = UntilBegins(sim, "잡기");
        for (int p = 1; p <= 59; p++)
        {
            UntilTick(sim, begun, p, q => q == 20 ? _jump : default);
            (HitBox hit, double progress) = sim.BossHitAhead.ShouldNotBeNull($"{p}틱: 흰 구가 날 자리가 비었다");
            hit.GrabHoldSeconds.ShouldBeGreaterThan(0);
            progress.ShouldBe((p - 1) / 59.0, 1e-9, $"{p}틱: 지난 몫이 idle 에서 창까지의 몫이 아니다");
            sim.GrabLive.ShouldBeFalse();
        }

        for (int p = 60; p <= 67; p++)
        {
            UntilTick(sim, begun, p);
            sim.BossHitAhead.ShouldBeNull($"{p}틱: 창이 열렸는데 아직 날고 있다");
            sim.GrabLive.ShouldBe(p < 67, $"{p}틱: 뛰어넘은 사람 앞의 잡기 창");
        }

        sim.Fighter.Held.ShouldBeFalse();
    }

    [Fact]
    public void 흰_구가_나는_동안_보스가_무너지면_날_자리도_잡기_창도_없다()
    {
        // Review Focus 5 (#78) — 설계 §4.3: 게이지는 보스가 무엇을 하고 있었든 무너뜨린다. 흰 구가 나는 선딜 동안 보스를 무너뜨리면 패턴이 끊겨 날
        // 자리(BossHitAhead)가 그 틱에 사라지고 잡기 창은 안 선다 — 뷰의 흰 구는 그 자리에서 흩어진다(GrabOrb). 뷰가 앞 틱의 값을 붙들면 무너진 보스
        // 앞에 흰 구가 날아와 파이터를 감싼다. 칼 한 번에 게이지가 차는 기준 파이터가 보스 앞 150 까지 걸어가다 첫 잡기에 붙들리고, 풀린 뒤 마저
        // 다가가 둘째 잡기가 서면 칼을 넣는다.
        BattleSim sim = Sim(TestConfigs.Breaker(), "잡기");
        InputFrame Approach() => sim.Boss.X - sim.Fighter.X > 150 ? _right : default;
        UntilBegins(sim, "잡기", _ => Approach());
        int begun = UntilNextBegins(sim, "잡기", _ => Approach());
        UntilTick(sim, begun, 1);
        sim.BossHitAhead.ShouldNotBeNull("잡기의 첫 틱인데 날 자리가 없다 — 이 테스트가 나는 동안을 안 본다");

        for (int i = 0; i < 59 && !sim.Boss.Exhausted; i++)
        {
            sim.Tick(sim.Fighter.Action == FighterAction.Idle ? _attack : default);
        }

        sim.Boss.Exhausted.ShouldBeTrue("흰 구가 나는 동안 못 무너뜨렸다");
        (sim.Ticks - begun).ShouldBeLessThan(60, "잡기 창이 열린 뒤에 무너졌다");
        sim.BossHitAhead.ShouldBeNull("무너졌는데 흰 구가 날 자리가 남았다");
        UntilTick(sim, begun, 84);
        sim.GrabLive.ShouldBeFalse();
        sim.Fighter.Held.ShouldBeFalse("무너진 보스의 잡기가 섰다");
    }

    [Fact]
    public void 잡히면_그_틱에_잡기_창이_닫힌다()
    {
        // 한 번 휘두르면 한 번만 맞는다(설계 §3.5) — 잡은 창은 그 틱에 끝난다. 흰 구는 그때부터 창이 아니라 붙들림(Fighter.Held)을 따라간다.
        BattleSim sim = Sim(null, "잡기");
        int begun = UntilBegins(sim, "잡기");
        UntilTick(sim, begun, 60);

        sim.Fighter.Held.ShouldBeTrue();
        sim.GrabLive.ShouldBeFalse("잡은 창이 살아 있다");
    }

    [Fact]
    public void 잡기의_띠는_착지와_같은_모양이지만_대_본_판정이_붙드는_판정이라고_말한다()
    {
        // #83 · 설계 §4.7 — 착지의 흰 충격파는 "바닥에 닿은 사각형을 이으면 바닥 [0, 폭] 을 다 덮는 판정" 에 선다(FloorWave.Find · 패턴 이름으로 안
        // 가른다). 잡기의 띠 [0, 1920, 0, 60] 은 착지 띠와 모양이 같아 그 규칙만으로는 잡기에도 선다 — 그런데 잡기의 그림은 흰 구다(유저: "흰색 구가
        // 캐릭터를 잡도록"). 충격파는 보스가 바닥을 내리치는 그림이라 idle 로 선 보스 발밑에서 퍼지면 착지로 읽힌다. 그래서 뷰(BattleCues)가 이 틱에 대
        // 본 판정이 붙드는 판정이면(BossTestedGrab) 충격파를 안 건다 — 판정의 깃발로 가른다(CLAUDE.md §2). 뛰어넘은 사람 앞에서 창 8틱 내내 본다.
        BattleSim grab = Sim(null, "잡기");
        int begun = UntilBegins(grab, "잡기");
        for (int p = 60; p <= 67; p++)
        {
            UntilTick(grab, begun, p, q => q == 20 ? _jump : default);
            FloorWave.Find(grab.BossTestedRects, grab.Boss.X, TestConfigs.Arena().Width).ShouldNotBeNull($"{p}틱: 잡기의 띠가 바닥 전체가 아니다");
            grab.BossTestedGrab.ShouldBeTrue($"{p}틱: 잡기 창인데 붙드는 판정이라고 안 한다");
        }

        BattleSim leap = Sim(null, "점프 공격");
        begun = UntilBegins(leap, "점프 공격");
        UntilTick(leap, begun, 60);
        FloorWave.Find(leap.BossTestedRects, leap.Boss.X, TestConfigs.Arena().Width).ShouldNotBeNull("착지가 바닥 전체가 아니다");
        leap.BossTestedGrab.ShouldBeFalse("착지를 붙드는 판정이라고 한다 — 충격파가 안 선다");
    }

    [Fact]
    public void 점프_공격의_도약_중에_무너지면_그_자리에_내리고_착지는_없다()
    {
        // #59 의 4/6 넘김 — 공중 탈진은 _fall 이 내리고 EndPattern 이 착지를 걷는다(설계 §4.2). 가만히 선 파이터(480)에게 첫 도약이 내려(595)
        // 착지를 맞히고, 둘째 점프 공격은 같은 자리를 겨냥해 제자리에서 솟는다. 떠 있는 동안 칼을 넣어 게이지로 무너뜨리면: 공중에서 무너지고
        // (cause=poise) · 포물선의 높이만 따라 그 자리에 내리고 · 둘째 착지는 없다.
        BattleSim sim = Sim(TestConfigs.Breaker(), "점프 공격");
        int first = UntilBegins(sim, "점프 공격");
        using var log = new LogCapture();
        UntilTick(sim, first, 68);
        sim.Events.ShouldHaveSingleItem("첫 착지의 관측이 하나가 아니다").Verdict.ShouldBe(HitVerdict.Hit, "첫 착지가 안 맞았다");

        int second = UntilNextBegins(sim, "점프 공격");
        for (int i = 0; i < 200 && !sim.Boss.Exhausted; i++)
        {
            bool airborne = sim.Ticks - second > 24 && sim.Boss.Y > 0 && sim.Fighter.Action == FighterAction.Idle;
            sim.Tick(airborne ? _attack : default);
        }

        sim.Boss.Exhausted.ShouldBeTrue("둘째 도약 중에 무너지지 않았다");
        sim.Boss.Y.ShouldBeGreaterThan(0, "땅에서 무너졌다 — 공중 탈진을 못 본다");
        double x = sim.Boss.X;
        for (int i = 0; i < 120 && sim.Boss.Exhausted; i++)
        {
            sim.Tick(default);
            sim.Boss.X.ShouldBe(x, "무너진 보스가 가로로 움직였다");
        }

        sim.Boss.Exhausted.ShouldBeFalse("120틱 안에 탈진이 안 풀렸다 — 아래의 단언이 탈진 도중을 본다");
        sim.Boss.Y.ShouldBe(0, "탈진 안에 안 내렸다");
        log.Lines.Count(l => l.StartsWith("[boss][D] motion_begin id=leap ", StringComparison.Ordinal)).ShouldBe(2, "무너진 뒤에 또 뛰었다");
        log.Lines.ShouldContain(l => l.StartsWith("[boss][D] exhaust cause=poise id=점프 공격 ", StringComparison.Ordinal));
        log.Lines.ShouldContain(l => l.StartsWith("[boss][D] exhaust_landed ", StringComparison.Ordinal));
        sim.Events.Count.ShouldBe(1, "끊긴 도약의 착지가 관측을 남겼다");
    }

    [Fact]
    public void 엇박_3연격은_3연격의_박자에_누른_대시를_무적이_닫힌_뒤에_맞힌다()
    {
        // 설계 §4.9 · §6.2 — 박자로 누르는 사람. 3연격의 1타(51틱)에 맞춰 그 틱에 누른 안쪽 대시는 3연격이면 무적(0.14초 · 51 ~ 58틱)으로
        // 흘린다. 엇박의 1타는 60틱(누름 + 9)이라 무적이 닫힌 뒤 대시(11틱) 안에 떨어진다 — 맨몸이다. 관측의 수단은 대시 · 결과는 맞음이다.
        // GIF offbeat 의 사람이다(GifRunner). 전에는 박자로 누른 패리가 창 밖 커밋 안에서 맞았다 — 패리는 #168 에서 걷었다.
        foreach ((string pattern, HitVerdict expected) in new[] { ("3연격", HitVerdict.Dodged), ("엇박 3연격", HitVerdict.Hit) })
        {
            BattleSim sim = Sim(null, pattern);
            int begun = UntilBegins(sim, pattern, WalkIn);
            UntilTick(sim, begun, 60, p => p == 51 ? _dash : WalkIn(begun + p));

            sim.Events.Count.ShouldBe(1, $"{pattern}: 1타의 관측이 안 섰다");
            (sim.Events[0].Verb, sim.Events[0].Verdict).ShouldBe((DodgeVerb.Dash, expected), $"{pattern}: 박자로 누른 대시");
        }
    }

    // ── GIF 대본의 사람 (#147) ── GifRunner 의 대본과 같은 입력을 규칙 위에서 틱까지 못박는다. 대본의 숫자를 바꾸면 여기서 먼저 잰다.

    [Fact]
    public void GIF_rush_3연격을_1타_뒤에_끊은_돌진이_멀리_선_사람을_친다()
    {
        // 파이터는 480 에 선 채다 — 3연격은 960 밖에서 헛치고(1타), 첫 캔슬 지점(78)에서 끊어 돌진으로 잇는다. 돌진은 12틱에 닿고 35틱에 친다
        // (위의 돌진 테스트) — 3연격의 틱으로 78 + 35 = 113 이다.
        BattleSim sim = Sim(null, new ScriptPlan(0.8, "3연격", CancelPoint: 0, Next: "돌진"));
        int begun = UntilBegins(sim, "3연격");
        UntilTick(sim, begun, 78);
        sim.Boss.CurrentPattern.ShouldBe("돌진", "78틱에 돌진으로 안 이었다");
        UntilTick(sim, begun, 113);

        sim.Events.Select(e => (e.Verb, e.Verdict)).ShouldBe(new[]
        {
            (DodgeVerb.Spacing, HitVerdict.MissedTooFar),
            (DodgeVerb.None, HitVerdict.Hit),
        }, "1타가 헛치고 돌진이 113틱에 맞혀야 한다");
    }

    [Fact]
    public void GIF_grab_가드로_버틴_사람을_3연격_2타_뒤에_끊은_잡기가_붙든다()
    {
        // 1타 사거리 안(보스 앞 358)으로 걸어 들어가 ↓ 를 붙든다 — 1타 · 2타는 가드로 받고, 둘째 캔슬 지점(144)에서 잡기로 이어 60틱(204)에
        // 가드째 붙든다. 잡기는 가드를 안 받는다.
        BattleSim sim = Sim(null, new ScriptPlan(0.8, "3연격", CancelPoint: 1, Next: "잡기"));
        int begun = UntilBegins(sim, "3연격", WalkIn);
        UntilTick(sim, begun, 204, p => begun + p <= 86 ? _right : _guard);

        sim.Events.Select(e => (e.Verb, e.Verdict)).ShouldBe(new[]
        {
            (DodgeVerb.Guard, HitVerdict.Guarded),
            (DodgeVerb.Guard, HitVerdict.Guarded),
            (DodgeVerb.Guard, HitVerdict.Grabbed),
        }, "가드로 받은 두 타 뒤 잡기가 붙들어야 한다");
        sim.Fighter.Held.ShouldBeTrue();
    }

    [Fact]
    public void GIF_uppercut_걸어_들어와_3연격_1타의_박자에_뛴_사람을_올려베기가_공중에서_친다()
    {
        // 보스 쪽으로 걸어가다 35틱에 뛴다(보스 앞 386) — 3연격이면 1타(51)를 넘는 누름이다(16 ~ 35 · 위의 대조군). 올려베기는 같은 51틱에
        // 공중을 친다([0, 396] 안).
        BattleSim sim = Sim(null, "올려베기");
        int begun = UntilBegins(sim, "올려베기", _ => _right);
        UntilTick(sim, begun, 58, p => p < 35 ? _right : p == 35 ? _jump : default);

        DodgeEvent hit = sim.Events.ShouldHaveSingleItem("올려베기의 관측이 하나가 아니다");
        (hit.Verb, hit.Verdict).ShouldBe((DodgeVerb.Jump, HitVerdict.Hit));
        hit.Airborne.ShouldBeTrue("땅에서 맞았다");
    }

    [Fact]
    public void GIF_fast_2연격에_묶인_사람을_달려온_빠른_3연격이_친다()
    {
        // 위의 빠른 3연격 테스트를 정해진 입력으로 — 판이 선 뒤 74틱 걸어 보스 앞 442(3연격이 안 닿는 427 밖)에 선다. 3연격이 헛친 뒤 185 · 188 에
        // J 두 번(2연격), 보스는 0.4초 쉬고 달려와 빠른 3연격을 연다. 1타(24)에 누른 대시는 2연격에 묶여 버려지고 맞는다.
        BattleSim sim = Sim(null, new ScriptPlan(0.8, "3연격"), new ScriptPlan(0.4, "빠른 3연격", Run: true));
        int triple = UntilBegins(sim, "3연격", t => t <= 74 ? _right : default);
        UntilTick(sim, triple, 198, p => p is 185 or 188 ? _attack : default);
        int fast = UntilBegins(sim, "빠른 3연격");
        UntilTick(sim, fast, 31, p => p == 24 ? _dash : default);

        sim.Events.Take(3).ShouldAllBe(e => e.Verdict == HitVerdict.MissedTooFar, "3연격이 헛치지 않았다");
        DodgeEvent first = sim.Events.Skip(3).ShouldHaveSingleItem("빠른 3연격 1타의 관측이 하나가 아니다");
        first.Verdict.ShouldBe(HitVerdict.Hit);
        first.GreedWindow.ShouldBeTrue("2연격에 묶인 사람이 욕심으로 안 남았다");
    }
}
