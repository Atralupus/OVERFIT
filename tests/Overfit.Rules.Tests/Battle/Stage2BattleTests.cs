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
/// 2단계의 네 패턴이 <b>겨냥한 사람을 실제로 잡는가</b> (#78 · 설계 §4.6 ~ §4.9 · §6.2). README 의 GIF 가 보이는 장면을 규칙 위에서 틱까지
/// 못박는다 — 실제 캐릭터(fighters.json) · 실제 보스 · 실제 패턴에 대본(<see cref="ScriptPicker"/>)으로 패턴을 고정한다. 틱은 **패턴의
/// 틱**(설계 §4 의 표와 같은 자 · 첫 틱 1)이다: 패턴이 선 틱 B 에서 판의 B + p 틱이 패턴의 p 틱이다.
///
/// <para>
/// 판은 파이터 480 · 보스 1440 에서 서고, 보스는 첫 패턴 앞의 간격(0.8초 = 48틱)에 128px 걸어 와 1312 에서 첫 패턴을 세운다. 보스는
/// 안 죽는다(체력 999_999) — 판이 패턴 도중에 끝나지 않게.
/// </para>
/// </summary>
public class Stage2BattleTests
{
    private static readonly InputFrame _right = new(1, false, false, false, false);
    private static readonly InputFrame _dash = new(0, false, true, false, false);
    private static readonly InputFrame _parry = new(0, false, false, true, false);
    private static readonly InputFrame _jump = new(0, true, false, false, false);
    private static readonly InputFrame _guard = new(0, false, false, false, false, GuardHeld: true);

    private static FighterConfig Real() => TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter];

    /// <summary>2단계 명부 위에 대본을 얹은 판. 파이터는 <paramref name="fighter"/> — 없으면 실제 캐릭터다.</summary>
    private static BattleSim Sim(FighterConfig? fighter, params string[] script)
    {
        IReadOnlyList<string> roster = StageRoster.For(TestConfigs.Stages(), 2);
        return new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = fighter ?? Real(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = roster,
            Patterns = TestConfigs.Patterns(),
            Seed = 51,
            Picker = new ScriptPicker(roster, script),
            MaxTicks = TestConfigs.MaxTicks(),
        });
    }

    /// <summary>
    /// <paramref name="pattern"/> 이 설 때까지 <paramref name="before"/> 를 넣으며 민다 — 그 패턴이 선 판의 틱(B)을 돌려준다. 그 뒤 패턴의
    /// p 틱은 판의 B + p 틱이다(러너의 첫 틱은 선 다음 틱이다 · 설계 §3.6 ⑤).
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

    /// <summary>패턴의 <paramref name="p"/> 틱까지(그 틱 포함) 민다 — 틱마다 <paramref name="input"/> 이 그 패턴 틱의 입력을 낸다.</summary>
    private static void UntilTick(BattleSim sim, int begun, int p, Func<int, InputFrame>? input = null)
    {
        for (int i = 0; i < 60 * 30 && sim.Ticks < begun + p; i++)
        {
            sim.Tick(input?.Invoke(sim.Ticks + 1 - begun) ?? default);
        }

        sim.Ticks.ShouldBe(begun + p);
    }

    [Fact]
    public void 일타_돌진은_후딜을_기다리던_사람에게_달려와_도착_15틱_뒤에_3타를_꽂는다()
    {
        // 설계 §4.6 · §6.2 — 멀리 서서 지켜보는 사람(480)은 1타(51틱)가 사거리 밖으로 헛치는 것을 본다. 1.30초(78틱)에 보스가 달린다: 앞쪽 거리
        // 1312 − 480 = 832 에서 멈출 자리(760)까지 552 는 ⌈552 / 60⌉ = 10틱이라 87틱에 닿고(A), 3타는 A + 15 = 102틱에 선다. 서 있던 사람은 맞는다.
        BattleSim sim = Sim(null, "1타 돌진");
        int begun = UntilBegins(sim, "1타 돌진");
        sim.Boss.X.ShouldBe(1312, 1e-9);

        UntilTick(sim, begun, 102);

        sim.Boss.X.ShouldBe(760, 1e-9, "돌진이 파이터 앞 280 에 안 멈췄다");
        sim.Events.Select(e => (e.Verb, e.Verdict)).ShouldBe(new[]
        {
            (DodgeVerb.Spacing, HitVerdict.MissedTooFar),
            (DodgeVerb.None, HitVerdict.Hit),
        }, "1타는 멀리서 헛치고 3타는 달려와 맞혀야 한다 — 3타가 102틱에 안 섰다");
        sim.Fighter.Health.ShouldBe(Real().MaxHealth - 14);
    }

    /// <summary>실제 캐릭터에서 1타 뒤 경직만 <paramref name="stiff"/> 초로 바꾼 것 — 경직이 무엇을 묶는지 견주는 대조군이다.</summary>
    private static FighterConfig RealWithFirstStiff(double stiff)
    {
        FighterConfig c = Real();
        c.Combo[0] = TestConfigs.Step(c.Combo[0], stiff: stiff);
        return c;
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0.0, false)]
    public void 일타_돌진은_달려오는_보스를_찌른_사람을_1타_뒤_경직에서_3타로_맞힌다(double? stiff, bool hit)
    {
        // 설계 §4.6 · §6.2 · #82 — 후딜에 찔끔 치는 사람. 1타(51틱)가 멀리서 헛치자 후딜을 노려 걸어 들어가고(60 ~ 77틱 · 480 → 606), 보스가
        // 달리기 시작하는 78틱에 J 를 누른다. 칼은 달려오는 보스에 닿고(84틱 · 보스 체력 −10) 보스는 85틱에 파이터 앞 280(886)에 선다 — 3타는
        // A + 15 = 100틱이다. 1타는 94틱에 끝나지만 **1타 뒤 경직(0.40초 · 24틱)** 이 118틱까지 묶어, 95틱에 누른 대시가 버려지고 3타를 맞는다 —
        // 칼질 뒤 경직도 칼질이라 욕심(GreedWindow)으로 남는다. 경직을 0 으로 둔 같은 사람은 95틱의 대시로 보스를 뚫고 나가(무적 95 ~ 102)
        // 3타를 흘린다. 경직이 "한 번만 치고 마는 사람" 을 세운 것(#82)이 돌진의 미끼가 무는 자리다. 맞는 쪽은 fighters.json 그대로의 캐릭터다
        // (stiff 가 null) — README 의 돌진 GIF 의 사람이다. 경직만 바꾼 대조군이 0 이다.
        FighterConfig fighter = stiff is { } s ? RealWithFirstStiff(s) : Real();
        BattleSim sim = Sim(fighter, "1타 돌진");
        int begun = UntilBegins(sim, "1타 돌진");
        int bossHp = sim.Boss.Health;
        UntilTick(sim, begun, 100, p => p switch
        {
            >= 60 and <= 77 => _right,
            78 => new InputFrame(0, false, false, false, Attack: true),
            95 => _dash,
            _ => default,
        });

        (bossHp - sim.Boss.Health).ShouldBe(fighter.Combo[0].Damage, "찌른 칼이 달려오는 보스에 안 닿았다");
        sim.Boss.X.ShouldBe(606 + 280, 1e-9, "돌진이 찌른 사람 앞 280 에 안 멈췄다");
        sim.Fighter.Stiff.ShouldBe(hit, "3타가 선 틱의 1타 뒤 경직이 데이터의 값과 다르다 — 이 테스트가 경직을 안 본다");

        // 닿은 판정은 닿은 틱에, 빗나간 판정은 창이 닫히는 틱에 관측이 선다(설계 §3.6 ①) — 창(8틱)이 닫힐 때까지 민다.
        for (int i = 0; i < 8 && sim.Events.Count < 2; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(2, "3타의 관측이 안 섰다");
        DodgeEvent third = sim.Events[1];
        if (hit)
        {
            (third.Verb, third.Verdict, third.GreedWindow).ShouldBe((DodgeVerb.None, HitVerdict.Hit, true), "경직에 선 사람이 3타를 안 맞았다");
        }
        else
        {
            (third.Verb, third.Verdict).ShouldBe((DodgeVerb.Dash, HitVerdict.MissedByGap), "경직이 없는 사람이 대시로 3타를 못 흘렸다 — 대조군이 무너졌다");
        }
    }

    /// <summary>1타 사거리 안(보스와 356 떨어진 956)으로 걸어 들어가는 입력 — 판이 선 뒤 68틱 동안 오른쪽을 누른다(476px).</summary>
    private static InputFrame WalkIn(int tick) => tick <= 68 ? _right : default;

    [Fact]
    public void 일타_동안_보스를_뚫고_등_뒤로_간_사람에게_돌진은_0틱이고_3타는_앞으로_헛친다()
    {
        // 설계 §4.6 — d < 0 은 돌진이 시작하는 78틱에만 있다: 1타 동안 보스를 뚫고 지나가 등 뒤에 선 사람이다. 돌진은 0틱이고 보스는 뒤로 안 가고
        // 돌아서지도 않는다 — 3타는 93틱에 앞으로 헛친다(등 뒤 궤적은 높이 412.5 위에만 있다). 1타 창이 열리는 틱(51)에 보스 쪽으로 대시하면
        // 무적으로 흘리며 403px 를 가 보스(1312) 등 뒤 1359 에 선다 — 보스 중심에서 47 이라 몸이 품 안에 걸쳐 빗나간 이유는 틈이다(설계 §3.6 ②).
        // 데모(시드 51)의 봇이 2단계에서 이렇게 됐다.
        BattleSim sim = Sim(null, "1타 돌진");
        int begun = UntilBegins(sim, "1타 돌진", WalkIn);
        UntilTick(sim, begun, 100, p => p == 51 ? _dash : WalkIn(begun + p));

        sim.Fighter.X.ShouldBeGreaterThan(sim.Boss.X, "대시가 보스를 못 뚫었다 — 이 테스트가 등 뒤를 안 본다");
        sim.Boss.X.ShouldBe(1312, 1e-9, "등 뒤의 사람에게 보스가 달렸다");
        sim.Boss.Facing.ShouldBe(-1, "등 뒤의 사람에게 보스가 돌아섰다");
        sim.Events.Select(e => (e.Verb, e.Verdict)).ShouldBe(new[]
        {
            (DodgeVerb.Dash, HitVerdict.Dodged),
            (DodgeVerb.Spacing, HitVerdict.MissedByGap),
        }, "1타는 대시로 흘리고 3타는 앞으로 헛쳐야 한다");
        sim.Fighter.Health.ShouldBe(Real().MaxHealth);
    }

    [Fact]
    public void 일타를_받아치면_돌진도_잡기도_없다()
    {
        // 설계 §4.6 · §4.7 · §4.3 — 받아치면(1타) 연격이 끊기고 보스가 탈진한다: 돌진은 안 서고 잡기도 없다 — 흰 구가 날 자리(BossHitAhead)도 잡기
        // 창(GrabLive)도 없다. 1타 창(51틱)의 2틱 앞에 누른 K 가 받아친다. 탈진(90틱)과 간격(48틱) 뒤 대본이 같은 패턴을 다시 세우기 전(170틱)까지 본다.
        foreach (string pattern in new[] { "1타 돌진", "1타 잡기" })
        {
            BattleSim sim = Sim(null, pattern);
            int begun = UntilBegins(sim, pattern, WalkIn);
            using var log = new LogCapture();
            for (int i = 0; i < 400 && sim.Ticks < begun + 170; i++)
            {
                int p = sim.Ticks + 1 - begun;
                sim.Tick(p == 49 ? _parry : WalkIn(sim.Ticks + 1));
                (sim.BossHitAhead is { Hit.GrabHoldSeconds: > 0 } || sim.GrabLive).ShouldBeFalse($"{pattern} · {p}틱: 받아쳤는데 잡기가 남았다");
            }

            sim.Ticks.ShouldBe(begun + 170, $"{pattern}: 대본이 다시 세우기 전(170틱)까지 못 밀었다 — 뒤의 단언이 짧은 판을 본다");
            sim.Events.Select(e => e.Verdict).ShouldBe(new[] { HitVerdict.Parried }, $"{pattern}: 받아친 뒤에 3타나 잡기가 섰다");
            log.Lines.ShouldNotContain(l => l.StartsWith("[boss][D] motion_begin id=rush ", StringComparison.Ordinal), $"{pattern}: 받아쳤는데 달렸다");
            sim.Fighter.Held.ShouldBeFalse();
        }
    }

    [Fact]
    public void 점프_3연속_사이에_보스를_넘어가면_다음_도약이_돌아서_따라간다()
    {
        // 설계 §4.8 — 매 도약이 그 틱의 파이터를 겨냥하고 그쪽으로 돌아선다(§4.2 의 규칙 그대로) — 파이터가 옮겨 가도 따라간다. 첫 착지(60틱 ·
        // 480 앞 595)를 맞은 뒤 보스 등 뒤로 걸어가(802 · 106틱) 선다. 둘째 도약(114틱)은 오른쪽으로 돌아서 그 파이터의 보스 쪽 115 앞(687)에 내린다.
        BattleSim sim = Sim(null, "점프 3연속");
        int begun = UntilBegins(sim, "점프 3연속");
        UntilTick(sim, begun, 150, p => p > 60 && sim.Fighter.X < 800 ? _right : default);

        sim.Fighter.X.ShouldBe(802, 1e-9, "파이터가 보스(595)를 넘어 802 에 안 섰다");
        sim.Boss.Facing.ShouldBe(1, "둘째 도약이 등 뒤로 간 파이터 쪽으로 안 돌아섰다");
        sim.Boss.X.ShouldBe(sim.Fighter.X - 115, 1e-9, "둘째 착지가 옮겨 간 파이터 앞이 아니다");
        sim.Events.Select(e => e.Verdict).ShouldBe(new[] { HitVerdict.Hit, HitVerdict.Hit }, "옮겨 간 파이터에게 둘째 착지가 안 닿았다");
    }

    [Fact]
    public void 일타_잡기는_1타를_대시로_흘리고_또_대시한_사람을_무적째로_붙든다()
    {
        // 설계 §4.7 · §6.2 · §9 — 대시로만 피하는 사람. 1타 창(51 ~ 58틱)이 열리는 틱에 대시해 무적 8틱으로 흘리고(보스를 뚫고 등 뒤로 빠진다),
        // 대시(11틱)와 그 뒤 경직(6틱 · #82)이 68틱에 풀린 뒤, 잡기 창(102틱)이 열리기 4틱 앞(98틱)에 또 대시한다 — 무적 8틱(98 ~ 105)이 창의 첫 틱을 덮는다. 잡기는 무적을 안 받는다: 잡혀 60틱
        // 붙들린다. 관측은 수단 Dash · 결과 Grabbed 이고 누른 틱이 창 4틱 앞이라 오차 −0.067 이다.
        BattleSim sim = Sim(null, "1타 잡기");
        int begun = UntilBegins(sim, "1타 잡기", WalkIn);
        UntilTick(sim, begun, 102, p => p is 51 or 98 ? _dash : WalkIn(begun + p));

        sim.Events.Count.ShouldBe(2, "1타와 잡기의 관측이 안 섰다");
        (sim.Events[0].Verb, sim.Events[0].Verdict).ShouldBe((DodgeVerb.Dash, HitVerdict.Dodged), "1타를 대시로 못 흘렸다");
        DodgeEvent grab = sim.Events[1];
        (grab.Verb, grab.Verdict).ShouldBe((DodgeVerb.Dash, HitVerdict.Grabbed));
        grab.TimingError.ShouldBe(-4 * BattleSim.Dt, 1e-9, "잡기 앞의 대시가 창 4틱 앞이 아니다");
        grab.DashAvailable.ShouldBeFalse("잡기가 대시를 받는다고 실렸다");
        sim.Fighter.Held.ShouldBeTrue();
        sim.Fighter.Health.ShouldBe(Real().MaxHealth - 25);

        UntilTick(sim, begun, 162);
        sim.Fighter.Held.ShouldBeFalse("붙드는 60틱이 지났는데 안 풀렸다");
        sim.Boss.CurrentPattern.ShouldBeNull("잡기가 2.70초(162틱)에 안 끝났다");
    }

    [Fact]
    public void 일타_잡기는_보고_뛴_사람을_못_잡는다()
    {
        // 설계 §4.7 — 78틱의 idle 이 3연격 · 돌진과 갈리는 첫 그림이다. 반응 0.2초(12틱) 뒤 90틱에 뛴 사람은 창(102틱)의 3 ~ 48틱 앞이라 넘는다
        // (보고 뛰는 몫 21틱). 가만히 선 사람은 가드도 없이 잡힌다.
        BattleSim sim = Sim(null, "1타 잡기");
        int begun = UntilBegins(sim, "1타 잡기");
        UntilTick(sim, begun, 110, p => p == 90 ? _jump : default);

        sim.Events[^1].Verb.ShouldBe(DodgeVerb.Jump);
        sim.Events[^1].Verdict.ShouldBe(HitVerdict.MissedByHeight);
        sim.Fighter.Held.ShouldBeFalse("뛰어넘었는데 붙들렸다");
    }

    [Fact]
    public void 엇박_3연격은_3연격의_박자에_누른_패리를_창_밖_커밋_안에서_맞힌다()
    {
        // 설계 §4.9 · §6.2 — 패리를 많이 하는 사람. 3연격의 1타(51틱)에 맞춰 2틱 앞(49틱)에 누른 K 는 3연격이면 받아친다. 엇박의 1타는 60틱
        // (누름 + 11)이라 창(+ 6)을 지나 커밋(+ 18) 안에 떨어진다 — 맨몸이다. 관측의 수단은 패리 · 결과는 맞음이다.
        foreach ((string pattern, HitVerdict expected) in new[] { ("3연격", HitVerdict.Parried), ("엇박 3연격", HitVerdict.Hit) })
        {
            BattleSim sim = Sim(null, pattern);
            int begun = UntilBegins(sim, pattern, WalkIn);
            UntilTick(sim, begun, 60, p => p == 49 ? _parry : WalkIn(begun + p));

            sim.Events.Count.ShouldBe(1, $"{pattern}: 1타의 관측이 안 섰다");
            (sim.Events[0].Verb, sim.Events[0].Verdict).ShouldBe((DodgeVerb.Parry, expected), $"{pattern}: 박자로 누른 패리");
        }
    }

    [Fact]
    public void 엇박_3연격에_박자로_누른_사람은_맞은_뒤_3연격의_간격으로_잰_2타에서도_또_맞는다()
    {
        // 설계 §4.9 · §5.3 · #82 — 셋 다 늦추므로 맞은 뒤 다음 타를 3연격의 간격(1 → 2타 42틱)으로 재어도 또 늦다. 49틱의 K 는 커밋(20틱)과 패리 뒤
        // 경직(15틱)에 묶여 83틱까지 선다 — 헛친 한 번이 0.583초다. 풀린 뒤 1타에 맞은 틱(60)에서 42틱 뒤의 2타를 겨냥해 2틱 앞(100틱)에 누른 K 는
        // 엇박의 2타(111틱 · 누름 + 11)를 창(+ 6) 밖 커밋 안에서 맞는다. 두 관측 모두 수단 패리 · 결과 맞음이다.
        BattleSim sim = Sim(null, "엇박 3연격");
        int begun = UntilBegins(sim, "엇박 3연격", WalkIn);
        UntilTick(sim, begun, 111, p => p is 49 or 100 ? _parry : WalkIn(begun + p));

        sim.Events.Select(e => (e.Verb, e.Verdict)).ShouldBe(new[]
        {
            (DodgeVerb.Parry, HitVerdict.Hit),
            (DodgeVerb.Parry, HitVerdict.Hit),
        }, "박자로 누른 사람이 1타 · 2타 중 하나를 받아쳤다");
    }

    [Fact]
    public void 엇박_3연격도_칼이_안_오르는_것을_보고_누르면_받아친다()
    {
        // 설계 §4.9 — 3연격이면 칼이 오르는 44틱에 엇박은 f0 에 그대로 서 있다. 판정(60틱) 16틱 앞에 보이는 다름이라 반응 0.2초(12틱) 뒤
        // 56틱에 누르면 창(54 ~ 60)에 든다. 받아치면 어느 타든 연격이 끊기고 보스가 탈진한다(설계 §4.3).
        BattleSim sim = Sim(null, "엇박 3연격");
        int begun = UntilBegins(sim, "엇박 3연격", WalkIn);
        UntilTick(sim, begun, 60, p => p == 56 ? _parry : WalkIn(begun + p));

        sim.Events.Single().Verdict.ShouldBe(HitVerdict.Parried);
        sim.Boss.Exhausted.ShouldBeTrue("받아쳤는데 보스가 안 무너졌다");
    }

    [Fact]
    public void 가드로_버티면_3연격을_막고_점프_3연속의_셋째_착지에서_붕괴한다()
    {
        // 설계 §4.8 · §6.2 — ↓ 를 놓지 않는 사람. 3연격의 세 타를 막고(8 · 8 · 14 의 1.8배 = 54) 곧장 온 점프 ×3 의 착지를 막는다. 가드 중에는
        // 안 차므로 46 → 24.4 → 2.8 이고 셋째 21.6 을 못 내 붕괴한다(54 + 21.6 × 2 = 97.2) — 전액 12 를 맞고 탈진한다.
        BattleSim sim = Sim(null, "3연격", "점프 3연속");
        UntilBegins(sim, "3연격", WalkIn);
        UntilBegins(sim, "점프 3연속", t => t <= 68 ? _right : _guard);
        for (int i = 0; i < 400 && sim.Events.Count < 6; i++)
        {
            sim.Tick(_guard);
        }

        sim.Events.Select(e => (e.PatternId, e.Verdict)).ShouldBe(new[]
        {
            ("3연격", HitVerdict.Guarded),
            ("3연격", HitVerdict.Guarded),
            ("3연격", HitVerdict.Guarded),
            ("점프 3연속", HitVerdict.Guarded),
            ("점프 3연속", HitVerdict.Guarded),
            ("점프 3연속", HitVerdict.GuardBroken),
        });
        sim.Fighter.Exhausted.ShouldBeTrue("붕괴가 탈진이 아니다");
    }

    [Fact]
    public void 잡기에_죽으면_판이_그_틱에_끝나고_그_잡힘은_관측으로_남는다()
    {
        // Review Focus 4 — 체력이 25 이하인 채 잡힌다. 가만히 선 사람(480)에게 1타 잡기를 되풀이하면 1타는 멀리서 헛치고 잡기가 되풀이해
        // 닿아(25씩 · 걸어 온 보스의 1타도 섞인다) 마지막 잡기에 죽는다 — 몇 번째인지는 파이터 체력(fighters.json)에 달려 세지 않는다.
        // 판은 그 틱에 진다(reason=dead) — 죽인 잡기가 닿은 틱이 판이 끝난 틱이고(붙들림 60틱을 기다리지 않는다) 파이터는 붙들린 채다.
        // 판을 끝낸 그 한 대는 닿은 것이라 관측이 있다(설계 §3.6 ③ — 끊기는 것은 남은 창뿐이다): 마지막 관측이 Grabbed 이고, 끊긴 창(cut_swing)은 없다.
        BattleSim sim = Sim(null, "1타 잡기");
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
        // 못박는다. ① 지금 단계 바로 다음이 판정이면 그 판정과 지난 몫(BossHitAhead) — 1.30초(78틱)의 idle 에서 0 이고 창(102틱) 한 틱 앞에
        // 23/24 다. 1타의 f1(44 ~ 50틱)도 판정 앞이지만 그 판정은 안 붙든다. ② 잡기 창이 산 동안(GrabLive) — 뛰어넘은 사람에게는 창 8틱
        // (102 ~ 109)이 끝날 때까지 참이고, 흰 구는 그동안 바닥에서 기다렸다 흩어진다. 1타의 창(51 ~ 58틱)은 산 판정이어도 붙드는 판정이 아니라
        // 거짓이다 — 멀리 선 사람이라 1타가 창 끝까지 산다.
        BattleSim sim = Sim(null, "1타 잡기");
        int begun = UntilBegins(sim, "1타 잡기");

        UntilTick(sim, begun, 45);
        sim.BossHitAhead.ShouldNotBeNull("1타의 f1 이 판정 바로 앞인데 비었다").Hit.GrabHoldSeconds.ShouldBe(0);
        for (int p = 51; p <= 57; p++)
        {
            UntilTick(sim, begun, p);
            sim.SwingLive.ShouldBeTrue($"{p}틱: 1타의 창이 안 살았다 — 이 테스트가 산 판정을 안 본다");
            sim.GrabLive.ShouldBeFalse($"{p}틱: 1타의 창을 잡기 창이라고 한다");
        }

        UntilTick(sim, begun, 77);
        sim.BossHitAhead.ShouldBeNull("1타의 후딜(f3) 다음은 판정이 아니다");

        for (int p = 78; p <= 101; p++)
        {
            UntilTick(sim, begun, p, q => q == 90 ? _jump : default);
            (HitBox hit, double progress) = sim.BossHitAhead.ShouldNotBeNull($"{p}틱: 흰 구가 날 자리가 비었다");
            hit.GrabHoldSeconds.ShouldBeGreaterThan(0);
            progress.ShouldBe((p - 78) / 24.0, 1e-9, $"{p}틱: 지난 몫이 idle 에서 창까지의 몫이 아니다");
            sim.GrabLive.ShouldBeFalse();
        }

        for (int p = 102; p <= 109; p++)
        {
            UntilTick(sim, begun, p);
            sim.BossHitAhead.ShouldBeNull($"{p}틱: 창이 열렸는데 아직 날고 있다");
            sim.GrabLive.ShouldBe(p < 109, $"{p}틱: 뛰어넘은 사람 앞의 잡기 창");
        }

        sim.Fighter.Held.ShouldBeFalse();
    }

    [Fact]
    public void 흰_구가_나는_동안_보스가_무너지면_날_자리도_잡기_창도_없다()
    {
        // Review Focus 5 — 설계 §4.3: 게이지는 보스가 무엇을 하고 있었든 무너뜨린다. 흰 구가 나는 1.30초의 idle 동안 보스를 무너뜨리면 패턴이
        // 끊겨 날 자리(BossHitAhead)가 그 틱에 사라지고 잡기 창은 안 선다 — 뷰의 흰 구는 그 자리에서 흩어진다(GrabOrb). 뷰가 앞 틱의 값을 붙들면
        // 무너진 보스 앞에 흰 구가 날아와 파이터를 감싼다. 칼 한 번에 게이지가 차는 기준 파이터가 보스(1312) 앞 150 까지 걸어가 1타를 맞고 서
        // 있다가, idle 에 들면 칼을 넣는다.
        BattleSim sim = Sim(TestConfigs.Breaker(), "1타 잡기");
        int begun = UntilBegins(sim, "1타 잡기", _ => sim.Boss.X - sim.Fighter.X > 150 ? _right : default);
        UntilTick(sim, begun, 78, _ => sim.Boss.X - sim.Fighter.X > 150 ? _right : default);
        sim.BossHitAhead.ShouldNotBeNull("idle 에 들었는데 날 자리가 없다 — 이 테스트가 나는 동안을 안 본다");

        for (int i = 0; i < 24 && !sim.Boss.Exhausted; i++)
        {
            sim.Tick(sim.Fighter.Action == FighterAction.Idle ? new InputFrame(0, false, false, false, Attack: true) : default);
        }

        sim.Boss.Exhausted.ShouldBeTrue("흰 구가 나는 동안 못 무너뜨렸다");
        (sim.Ticks - begun).ShouldBeLessThan(102, "잡기 창이 열린 뒤에 무너졌다");
        sim.BossHitAhead.ShouldBeNull("무너졌는데 흰 구가 날 자리가 남았다");
        UntilTick(sim, begun, 120);
        sim.GrabLive.ShouldBeFalse();
        sim.Fighter.Held.ShouldBeFalse("무너진 보스의 잡기가 섰다");
    }

    [Fact]
    public void 잡히면_그_틱에_잡기_창이_닫힌다()
    {
        // 한 번 휘두르면 한 번만 맞는다(설계 §3.5) — 잡은 창은 그 틱에 끝난다. 흰 구는 그때부터 창이 아니라 붙들림(Fighter.Held)을 따라간다.
        BattleSim sim = Sim(null, "1타 잡기");
        int begun = UntilBegins(sim, "1타 잡기");
        UntilTick(sim, begun, 102);

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
        BattleSim grab = Sim(null, "1타 잡기");
        int begun = UntilBegins(grab, "1타 잡기");
        for (int p = 102; p <= 109; p++)
        {
            UntilTick(grab, begun, p, q => q == 90 ? _jump : default);
            FloorWave.Find(grab.BossTestedRects, grab.Boss.X, TestConfigs.Arena().Width).ShouldNotBeNull($"{p}틱: 잡기의 띠가 바닥 전체가 아니다");
            grab.BossTestedGrab.ShouldBeTrue($"{p}틱: 잡기 창인데 붙드는 판정이라고 안 한다");
        }

        BattleSim leaps = Sim(null, "점프 3연속");
        begun = UntilBegins(leaps, "점프 3연속");
        UntilTick(leaps, begun, 60);
        FloorWave.Find(leaps.BossTestedRects, leaps.Boss.X, TestConfigs.Arena().Width).ShouldNotBeNull("착지가 바닥 전체가 아니다");
        leaps.BossTestedGrab.ShouldBeFalse("착지를 붙드는 판정이라고 한다 — 충격파가 안 선다");
    }

    [Fact]
    public void 점프_3연속의_둘째_도약에서_무너지면_그_자리에_내리고_셋째_도약은_없다()
    {
        // #59 의 4/6 넘김 — 점프 ×3 의 공중 탈진은 _fall 이 내리고 EndPattern 이 남은 도약을 걷는다. 확인만 하면 된다(설계 §4.8 · §4.2).
        // 가만히 선 파이터(480)에게 첫 도약이 내려(595) 착지를 맞히고, 둘째 도약은 같은 자리를 겨냥해 제자리에서 솟는다. 떠 있는 동안 칼을 넣어
        // 게이지로 무너뜨리면: 공중에서 무너지고(cause=poise) · 포물선의 높이만 따라 그 자리에 내리고 · 둘째 착지도 셋째 도약도 없다.
        BattleSim sim = Sim(TestConfigs.Breaker(), "점프 3연속");
        int begun = UntilBegins(sim, "점프 3연속");
        using var log = new LogCapture();
        UntilTick(sim, begun, 60);
        sim.Events.Single().Verdict.ShouldBe(HitVerdict.Hit, "첫 착지가 안 맞았다");

        for (int i = 0; i < 200 && !sim.Boss.Exhausted; i++)
        {
            bool airborne = sim.Ticks - begun > 114 && sim.Boss.Y > 0 && sim.Fighter.Action == FighterAction.Idle;
            sim.Tick(airborne ? new InputFrame(0, false, false, false, Attack: true) : default);
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
        log.Lines.Count(l => l.StartsWith("[boss][D] motion_begin id=leap ", StringComparison.Ordinal)).ShouldBe(2, "무너진 뒤에 셋째 도약이 섰다");
        log.Lines.ShouldContain(l => l.StartsWith("[boss][D] exhaust cause=poise id=점프 3연속 ", StringComparison.Ordinal));
        log.Lines.ShouldContain(l => l.StartsWith("[boss][D] exhaust_landed ", StringComparison.Ordinal));
        sim.Events.Count.ShouldBe(1, "끊긴 도약의 착지가 관측을 남겼다");
    }
}
