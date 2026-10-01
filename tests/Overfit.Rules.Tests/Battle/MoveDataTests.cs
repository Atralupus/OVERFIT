using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 일곱 동작의 데이터 (설계 2026-09-29 조각1 §2 — 옛 이름 Stage2DataTests · #78). 명부 일곱 · 동작마다의 틱이 스펙의 표(§2.5)와 같은 말을 하는지,
/// 판정의 답 · 붙드는 시간 · 돌진의 수치 · 손으로 적은 사각형 · 그림 뒤집기가 규약을 지키는지. <c>PatternDataTests</c> 가 모든 패턴에 거는
/// 규약(정수 틱 · 창 · 움직임 · 그림 · 태그)은 거기 두고, 여기는 동작들이 서로 맺는 관계와 동작마다의 수치를 본다 — 박자 셋(3연격 · 엇박 ·
/// 빠른)은 "같은 세 타" 여야 박자만 다르다.
/// </summary>
public class MoveDataTests
{
    private static Dictionary<string, PatternDef> Patterns() => TestConfigs.Patterns();

    /// <summary>단계가 드는 틱 — 러너와 같은 반올림이다(<see cref="BattleSim.TicksFor"/> · 패턴의 첫 틱이 1).</summary>
    private static int TickOf(PatternStep step) => BattleSim.TicksFor(step.T);

    /// <summary>판정 단계들이 드는 틱.</summary>
    private static int[] Windows(PatternDef def) => def.Timeline.Where(s => s.Kind == "active").Select(TickOf).ToArray();

    /// <summary>판을 세울 때 짓는 판정들 — active 칸만.</summary>
    private static HitBox[] Hits(PatternDef def) =>
        BossHits.Of(def, TestConfigs.HitShapes()).Where(h => h is not null).Select(h => h!.Value).ToArray();

    [Fact]
    public void 명부는_일곱이다()
    {
        // 설계 2026-09-29 조각1 §2 — 옛 1타 돌진 · 1타 잡기 · 점프 3연속을 걷고, 남은 셋(3연격 · 점프 공격 · 엇박 3연격)의 순서를 지킨 채 새 넷을
        // 맨 뒤에 붙였다 — 순서는 뽑기 좌표다(stages.json 의 _note_순서). 고르기는 uniform 이다(조각 1 의 고르기는 무작위 · 망은 조각 4).
        StageDef only = TestConfigs.Stages()["1"];
        Dictionary<string, PatternDef> patterns = Patterns();

        only.Patterns.ShouldBe(new[] { "3연격", "점프 공격", "엇박 3연격", "빠른 3연격", "돌진", "잡기", "올려베기" });
        only.Want.ShouldBe(7);
        only.Picker.ShouldBe("uniform");
        only.Patterns.Where(id => !patterns.ContainsKey(id)).ShouldBeEmpty("명부의 id 가 patterns.json 에 없다");
    }

    [Fact]
    public void 일곱_동작의_판정_창과_끝은_스펙의_틱이다()
    {
        // 설계 2026-09-29 조각1 §2.5 의 표에서 끝만 이슈 #167 로 0.3초(18틱) 늦췄다 — 러너의 첫 틱이 1 이고 판정 창은 8틱(0.125초)이다. 후딜은 마지막
        // 창이 닫힌 뒤 끝까지다. 잡기는 창을 36 → 60틱으로 미루고 끝을 창의 끝 + 1.5초(90틱)로 뒀다 — 붙드는 60틱보다 길어 붙든 뒤에도 보스가 선다.
        // 돌진의 판정은 **선 시계**로 센 틱이다 — 달리는 동안 시계가 서므로 판 위의 틱은 도착에 달렸다(MoveBattleTests).
        var table = new (string Id, int[] Windows, int End, int Recover)[]
        {
            ("3연격", [51, 93, 159], 213, 46),
            ("엇박 3연격", [60, 111, 186], 240, 46),
            ("빠른 3연격", [24, 51, 84], 138, 46),
            ("돌진", [24], 78, 46),
            ("잡기", [60], 158, 90),
            ("점프 공격", [60], 108, 40),
            ("올려베기", [51], 105, 46),
        };
        Dictionary<string, PatternDef> patterns = Patterns();

        table.Select(r => r.Id).Order(StringComparer.Ordinal)
            .ShouldBe(TestConfigs.Stages()["1"].Patterns.Order(StringComparer.Ordinal), "표가 명부의 일곱을 다 안 본다");
        foreach ((string id, int[] windows, int end, int recover) in table)
        {
            PatternDef def = patterns[id];
            Windows(def).ShouldBe(windows, $"{id}: 판정 창이 드는 틱");
            def.Timeline.Where(s => s.Kind == "active").Select(s => BattleSim.TicksFor(s.ActiveSeconds))
                .ShouldAllBe(n => n == 8, $"{id}: 판정 창이 8틱이 아니다");
            BattleSim.TicksFor(def.Duration).ShouldBe(end, $"{id}: 끝의 틱");
            (end - (windows[^1] + 8)).ShouldBe(recover, $"{id}: 후딜");
        }
    }

    [Fact]
    public void 캔슬_지점은_3연격_계열의_다음_타_선딜이다()
    {
        // 설계 2026-09-29 조각1 §3.1 의 표 — 지점은 다음 타의 선딜이 서는 시각이다(앞 타의 후딜 장을 보여 준 뒤 · 다음 타를 들기 직전). 3연격의
        // 1.30초(78틱)가 옛 1타 돌진 · 1타 잡기가 3연격과 갈리던 그 시각이다. 나머지 넷은 지점이 없다(우산 §3.1) — 한 번에 끝나는 동작이다.
        var table = new Dictionary<string, int[]>(StringComparer.Ordinal)
        {
            ["3연격"] = [78, 144],
            ["엇박 3연격"] = [87, 162],
            ["빠른 3연격"] = [36, 69],
            ["돌진"] = [],
            ["잡기"] = [],
            ["점프 공격"] = [],
            ["올려베기"] = [],
        };
        Dictionary<string, PatternDef> patterns = Patterns();

        table.Keys.Order(StringComparer.Ordinal)
            .ShouldBe(TestConfigs.Stages()["1"].Patterns.Order(StringComparer.Ordinal), "표가 명부의 일곱을 다 안 본다");
        foreach ((string id, int[] points) in table)
        {
            (patterns[id].CancelPoints ?? []).Select(c => BattleSim.TicksFor(c.T)).ShouldBe(points, $"{id}: 캔슬 지점");
            foreach (CancelPointDef point in patterns[id].CancelPoints ?? [])
            {
                PatternStep at = patterns[id].Timeline.Single(s => TickOf(s) == BattleSim.TicksFor(point.T));
                (at.Kind, at.Frame).ShouldBe(("windup", 0), $"{id}: {point.T}초가 다음 타의 첫 선딜 장이 아니다");
            }
        }
    }

    [Fact]
    public void 엇박_3연격은_3연격과_같은_세_타에_칼이_오르기_전의_f0_만_0_15초씩_더_붙든다()
    {
        // 설계 §4.9 — 같은 판정 · 같은 피해이고, 다른 것은 박자 하나다: 타마다 f0 을 0.15초(9틱) 더 붙든다. 그래서 k 번째 타의 모든 장이
        // 3연격보다 9k 틱 늦고(판정 51 · 93 · 159 → 60 · 111 · 186), 칼이 오르는 f1 에서 판정까지는 3연격과 같은 7틱이다. 0.725초(44틱)까지는
        // 3연격과 같다 — 44틱에 3연격은 칼을 올리고 엇박은 f0 에 더 선다(판정 16틱 앞 · 보고 누르는 사람의 단서).
        Dictionary<string, PatternDef> p = Patterns();
        List<PatternStep> beat = p["3연격"].Timeline;
        List<PatternStep> off = p["엇박 3연격"].Timeline;

        off.Count.ShouldBe(beat.Count, "엇박이 3연격과 다른 수의 장을 갖는다");
        int hits = 0;
        for (int i = 0; i < beat.Count; i++)
        {
            PatternStep b = beat[i];
            PatternStep o = off[i];
            (o.Kind, o.Anim, o.Frame, o.Hitbox, o.Damage, o.ActiveSeconds)
                .ShouldBe((b.Kind, b.Anim, b.Frame, b.Hitbox, b.Damage, b.ActiveSeconds), $"{i}번 장이 3연격과 다른 그림 · 판정이다");

            // k = 이 장 앞(자기 자신이 f0 이면 그 앞)에 붙든 f0 의 수. f0 자체는 앞 타들의 늦춤만큼 늦게 **들고** 제 몫만큼 길게 선다.
            int k = beat.Take(i + 1).Count(s => s.Frame == 0 && s.Kind == "windup") - (b.Frame == 0 && b.Kind == "windup" ? 1 : 0);
            (o.T - b.T).ShouldBe(0.15 * k, 1e-9, $"{i}번 장({b.Anim} f{b.Frame})이 3연격보다 {0.15 * k}초 늦지 않다");
            if (b.Kind == "active")
            {
                hits++;
                (TickOf(o) - TickOf(off[i - 1])).ShouldBe(7, $"{i}번 판정 — 칼이 오르는 f1 에서 판정까지가 3연격과 다르다");
            }
        }

        hits.ShouldBe(3);
        off.Where(s => s.Kind == "active").Select(TickOf).ShouldBe(new[] { 60, 111, 186 });
        off.First(s => s.Frame == 1).T.ShouldBe(0.875, "첫 타의 f1 이 3연격의 0.725 에서 0.15 늦지 않다");
    }

    [Fact]
    public void 빠른_3연격은_3연격과_같은_세_타를_빠른_박자로_친다()
    {
        // 설계 2026-09-29 조각1 §2.2 — 모양 · 피해 · 그림은 3연격과 같고(attack1 ~ 3 · 8 · 8 · 14)
        // 박자만 다르다: 판정 24 · 51 · 84틱. 칼이 오르는 f1 에서 판정까지는 3연격과 같은 7틱이다 — 빨라도 그 한 장이 예고다. 판정 사이 27 · 33틱은
        // 대시(17틱)로 이어 피할 수 있고 판정 사이에 한 번 치는 것(40틱)은 못 들어간다.
        Dictionary<string, PatternDef> p = Patterns();
        List<PatternStep> beat = p["3연격"].Timeline;
        List<PatternStep> fast = p["빠른 3연격"].Timeline;

        fast.Count.ShouldBe(beat.Count, "빠른 3연격이 3연격과 다른 수의 장을 갖는다");
        for (int i = 0; i < beat.Count; i++)
        {
            PatternStep b = beat[i];
            PatternStep f = fast[i];
            (f.Kind, f.Anim, f.Frame, f.Hitbox, f.Damage, f.ActiveSeconds)
                .ShouldBe((b.Kind, b.Anim, b.Frame, b.Hitbox, b.Damage, b.ActiveSeconds), $"{i}번 장이 3연격과 다른 그림 · 판정이다");
            if (f.Kind == "active")
            {
                (TickOf(f) - TickOf(fast[i - 1])).ShouldBe(7, $"{i}번 판정 — 칼이 오르는 f1 에서 판정까지가 3연격과 다르다");
            }
        }

        int[] windows = Windows(p["빠른 3연격"]);
        (windows[1] - windows[0], windows[2] - windows[1]).ShouldBe((27, 33), "판정 사이가 스펙과 다르다");
        p["빠른 3연격"].Tags.PunishGreed.ShouldBeTrue("patterns.json 의 태그(punish_greed)가 거짓이다 — 망의 입력이 바뀐다");
    }

    [Fact]
    public void 점프_공격은_한_번_뛰고_착지는_대시_가드가_안_되며_피해는_24다()
    {
        // 설계 2026-09-29 조각1 §2.3 — 옛 점프 3연속과 단발을 하나로 합쳤다(유저: "점프공격이 3회, 2회 막반복되니까 지루합니다"). 도약 한 번 · 착지 한 번.
        // 착지의 답 둘이 거짓이라 대시 무적 · 가드가 모두 맨몸이고, 띠가 아레나 전체라 거리로도 못 피한다 — 발이 60 위여야만 넘는다. 태그의
        // dash_window 는 0 이다 — 관측(DashAvailable)이 같은 말을 한다. 옛 "창이 열리는 바로 그 틱의 대시만 산다" 는 없어졌다.
        PatternDef jump = Patterns()["점프 공격"];
        HitBox[] hits = Hits(jump);

        jump.Timeline.Count(s => s.Motion is { Id: "leap" }).ShouldBe(1, "도약이 한 번이 아니다");
        hits.Length.ShouldBe(1, "착지가 한 번이 아니다");
        (hits[0].Dashable, hits[0].Guardable).ShouldBe((false, false), "착지가 대시 · 가드 중 무엇을 받는다");
        hits[0].Damage.ShouldBe(24);
        hits[0].GrabHoldSeconds.ShouldBe(0, "착지가 붙든다");
        (jump.Tags.DashWindow, jump.Tags.Jumpable).ShouldBe((0.0, true));
    }

    [Fact]
    public void 판정의_답은_판정_단계에만_거짓으로_적는다()
    {
        // 설계 §7.3 · §8.1 — dash · guard 는 태그를 **좁히기만** 한다. true 는 태그대로라 적을 까닭이 없고, 적으면 "넓힌다" 로 읽혀
        // 태그가 막은 수단을 연다고 오해한다 — 규칙은 태그와 답을 둘 다 봐서 넓히지 못한다(HitResolver.Effective). 판정이 아닌 단계의 답과
        // 붙드는 시간은 아무도 안 읽는다 — JsonData 가 조용히 받으므로 여기서 막는다.
        int answers = 0;
        foreach ((string id, PatternDef def) in Patterns())
        {
            foreach (PatternStep s in def.Timeline)
            {
                bool?[] keys = { s.Dash, s.Guard };
                if (s.Kind != "active")
                {
                    keys.ShouldAllBe(k => k == null, $"{id}: t={s.T} 판정이 아닌 단계에 답이 있다");
                    s.GrabHoldSeconds.ShouldBe(0, $"{id}: t={s.T} 판정이 아닌 단계에 붙드는 시간이 있다");
                    continue;
                }

                keys.ShouldAllBe(k => k != true, $"{id}: t={s.T} 답을 true 로 적었다 — 좁히기만 한다(없으면 받는다)");
                answers += keys.Count(k => k == false);
                s.GrabHoldSeconds.ShouldBeGreaterThanOrEqualTo(0, $"{id}: t={s.T} 붙드는 시간이 음수다");
            }
        }

        answers.ShouldBeGreaterThan(0, "답을 적은 판정이 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 붙드는_판정의_패턴은_창이_열린_뒤_붙드는_시간이_지나야_끝난다()
    {
        // 설계 §4.7 「패턴이 끝나는 시각」 — 흰 구가 붙들고 있는 동안 보스가 다음 패턴을 시작하면 그림이 거짓말이다. 창이 열린 뒤 붙드는 시간이
        // 지나야 끝난다. 잡기는 이슈 #167 로 창의 끝 + 1.5초(2.625초)에 끝나 붙드는 시간(1.00 + 1.0 = 2.00초)보다 넉넉히 길다.
        int grabs = 0;
        foreach ((string id, PatternDef def) in Patterns())
        {
            foreach (PatternStep s in def.Timeline.Where(s => s.GrabHoldSeconds > 0))
            {
                grabs++;
                (TickOf(s) + BattleSim.TicksFor(s.GrabHoldSeconds)).ShouldBeLessThanOrEqualTo(BattleSim.TicksFor(def.Duration),
                    $"{id}: t={s.T} 의 잡기가 붙드는 동안 패턴이 끝난다(end={def.Duration})");
            }
        }

        grabs.ShouldBeGreaterThan(0, "붙드는 판정이 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 잡기는_1_0초에_점프만_받고_1_0초_붙든다()
    {
        // 설계 2026-09-29 조각1 §2.4 — 옛 1타 잡기의 1.30초 뒤를 당겨 단독 동작으로 떼어 냈다. 앞에서 알리던 1타가 없어 흰 구가 나는 선딜이 예고의
        // 전부라 0.40 → 0.60초(유저 확인) → 1.0초(이슈 #167 — "잡기 선딜과 후딜 많이 늘려야합니다")로 늘렸다. 답은 점프 하나다 — 대시 · 가드 둘 다 거짓이다(대시 무적 중에도 ·
        // 가드 중에도 잡힌다). 보스는 idle 로 선다 — 흰 구가 예고다(뷰 · GrabOrb).
        PatternDef grab = Patterns()["잡기"];
        HitBox[] hits = Hits(grab);

        hits.Length.ShouldBe(1);
        Windows(grab).ShouldBe(new[] { 60 });
        (hits[0].Dashable, hits[0].Guardable).ShouldBe((false, false), "잡기가 대시 · 가드 중 무엇을 받는다");
        hits[0].GrabHoldSeconds.ShouldBe(1.0);
        hits[0].Damage.ShouldBe(25);
        grab.Timeline.Where(s => s.Kind != "end").Select(s => s.Anim).ShouldAllBe(a => a == "idle", "잡기의 보스가 idle 이 아닌 그림을 든다");

        // 점프는 자리마다 창이 열릴 때 잰다(#85 · JumpClearance) — 바닥 전체 띠라 어디서든 같다. 실제 캐릭터가 두고 서는 자리(115)에서 넘는다.
        foreach ((string who, FighterConfig c) in TestConfigs.Fighters())
        {
            new JumpClearance(c).Clears(hits[0].Shape, new Placement(960, 0, 1), 960 + 115, BattleSim.TicksFor(hits[0].ActiveSeconds))
                .ShouldBeTrue($"{who}: 잡기를 뛰어 못 넘는다");
        }
    }

    [Fact]
    public void 돌진은_빠르기와_멈출_거리를_갖고_뒤_단계는_같은_시각에서_도착을_기다린다()
    {
        // 설계 §4.6 · §8.1 — rush 는 speed · stop 을 읽는다. 둘 다 required 가 아니라(도약은 height · air 를 쓴다) 키 이름이 틀리면 JsonData 가
        // 조용히 0 으로 둔다: 빠르기 0 이면 돌진이 그 자리에서 끝나 3타가 멀리서 헛친다. 돌진은 패턴 시계를 세우므로 뒤 단계(3타의 선딜)의
        // T 는 **선 시계**로 적는다 — 돌진과 같은 시각이다.
        int rushes = 0;
        foreach ((string id, PatternDef def) in Patterns())
        {
            for (int i = 0; i < def.Timeline.Count; i++)
            {
                if (def.Timeline[i].Motion is not { Id: "rush" } rush)
                {
                    continue;
                }

                rushes++;
                rush.Speed.ShouldBeGreaterThan(0, $"{id}: 돌진의 speed 가 없다");
                rush.Stop.ShouldBeGreaterThan(0, $"{id}: 돌진의 stop 이 없다");
                def.Timeline[i + 1].T.ShouldBe(def.Timeline[i].T, $"{id}: 돌진 뒤 단계가 선 시계로 적히지 않았다");
            }
        }

        rushes.ShouldBeGreaterThan(0, "돌진이 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 돌진은_도착_뒤_0_40초에_3타를_친다()
    {
        // 설계 2026-09-29 조각1 §2.4 — 옛 1타 돌진의 1.30초 뒤를 0초로 당겼다: run + 돌진(3600 px/s · 파이터 앞 280 에서 멈춤) · attack3 · 피해 14.
        // 도착 뒤 선딜을 0.25 → 0.40초로 늘렸다 — 옛 돌진은 1타가 앞에서 알렸는데 단독으로 나오면 파이터가 280 안일 때 달리자마자 닿아 선딜만
        // 남는다(반응할 틈 15 → 24틱 · 일부러 빠르게 만든 첫 타인 빠른 3연격과 같다). 선 시계로 돌진이 1 · 3타가 24 다 — 0초의 단계는 러너의 첫 틱이다
        // (BattleSim.TicksFor). 달리는 동안 시계가 1 에 서 있다가 도착 다음 틱부터 다시 가므로 3타는 도착한 틱의 23틱 뒤다 — 280 안이면 도착이 첫 틱이라
        // 24틱(0.40초)이다(MoveBattleTests).
        PatternDef rush = Patterns()["돌진"];
        PatternStep run = rush.Timeline[0];
        MotionDef motion = run.Motion.ShouldNotBeNull("돌진의 첫 단계가 움직임이 아니다");
        PatternStep strike = rush.Timeline.Single(s => s.Kind == "active");

        (motion.Id, motion.Speed, motion.Stop, run.Anim).ShouldBe(("rush", 3600.0, 280.0, "run"));
        (TickOf(run), TickOf(strike)).ShouldBe((1, 24), "선 시계로 돌진이 1 · 3타가 24(0.40초)가 아니다");
        (strike.Anim, strike.Hitbox, strike.Damage).ShouldBe(("attack3", "medieval_king/attack3/2", 14), "3타가 3연격의 3타가 아니다");
    }

    [Fact]
    public void 올려베기는_attack2_를_뒤집어_그리고_손으로_적은_사각형으로_친다()
    {
        // 설계 2026-09-29 조각1 §2.1 — attack2 의 높은 궤적은 보스 등 뒤라(x −352 ~ −132) 그림을 좌우로 뒤집는다(mirror · 뷰만 읽는다 — 네 장 다).
        // 판정은 그림에서 뽑지 않고 손으로 적는다(유저 확인 — 그림과 달라도 크게): [0, 396, 0, 360] 한 장. 3연격 1타와 같은 51틱에 친다 — 3연격 1타를
        // 점프로 넘으려 먼저 뛴 사람을 잡는다. 선 사람은 대시 · 가드로 받는다.
        Dictionary<string, PatternDef> patterns = Patterns();
        PatternDef up = patterns["올려베기"];

        up.Timeline.Where(s => s.Kind != "end").Select(s => (s.Anim, s.Frame, s.Mirror)).ShouldBe(new (string?, int?, bool)[]
        {
            ("attack2", 0, true), ("attack2", 1, true), ("attack2", 2, true), ("attack2", 3, true),
        });
        HitBox hit = Hits(up).Single();
        hit.Shape.Local.ShouldBe(new[] { new HitRect(0, 396, 0, 360) });
        (hit.Damage, hit.Dashable, hit.Guardable).ShouldBe((14, true, true));
        Windows(up).ShouldBe(Windows(patterns["3연격"]).Take(1).ToArray(), "3연격 1타와 다른 틱에 친다");

        // 점프로는 못 넘는다 — 실제 캐릭터의 점프 정점(발 300)이 사각형 윗끝(360) 아래다. 사각형이 닿는 보스 앞 어느 자리에서든.
        foreach ((string who, FighterConfig c) in TestConfigs.Fighters())
        {
            foreach (int dx in new[] { 0, 150, 250, 396 })
            {
                new JumpClearance(c).Clears(hit.Shape, new Placement(960, 0, 1), 960 + dx, BattleSim.TicksFor(hit.ActiveSeconds))
                    .ShouldBeFalse($"{who}: 앞 {dx} 에서 올려베기를 뛰어 넘는다");
            }
        }
    }
}
