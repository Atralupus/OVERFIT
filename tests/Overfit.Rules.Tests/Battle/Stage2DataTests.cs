using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 2단계의 데이터 (#78 · 설계 §4 · §4.6 ~ §4.9 · §8.1) — 명부 다섯, 새 패턴 넷의 타임라인이 설계의 틱 표와 같은 말을 하는지, 판정의 답 ·
/// 붙드는 시간 · 돌진의 수치가 규약을 지키는지. <c>PatternDataTests</c> 가 모든 패턴에 거는 규약(정수 틱 · 창 · 움직임 · 그림)은 거기 두고,
/// 여기는 2단계의 패턴들이 서로 · 1단계와 맺는 관계를 본다 — 미끼는 "같은 그림" 이어야 미끼다.
/// </summary>
public class Stage2DataTests
{
    private static Dictionary<string, PatternDef> Patterns() => TestConfigs.Patterns();

    /// <summary>단계가 드는 틱 — 러너와 같은 반올림이다(<see cref="BattleSim.TicksFor"/> · 패턴의 첫 틱이 1).</summary>
    private static int TickOf(PatternStep step) => BattleSim.TicksFor(step.T);

    /// <summary>
    /// 뷰와 규칙이 읽는 단계의 전부 — 둘이 같으면 그 틱에 두 패턴은 그림도 판정도 움직임도 같다. 움직임은 id 만이 아니라 수치까지 본다
    /// (도약의 높이 · 체공 · 돌진의 속도 · 설 거리) — id 만 보던 때는 점프 3연속의 둘째 도약 높이를 280 → 200 으로 바꿔도 2단계 테스트가
    /// 다 초록이었다(최종 리뷰 F-I5). "점프공격은 2단계에선 그냥 3번" 의 "그냥" 이 그 수치다.
    /// </summary>
    private static string Sig(PatternStep s) =>
        $"{TickOf(s)}|{s.Kind}|{s.Anim}|{s.Frame}|{s.Hitbox}|{string.Join(',', s.Band ?? Array.Empty<double>())}|{s.Damage}|{s.ActiveSeconds}"
        + $"|{s.Dash}|{s.Guard}|{s.Parry}|{s.GrabHoldSeconds}|{s.Motion?.Id}"
        + $"|{s.Motion?.Height}|{s.Motion?.Air}|{s.Motion?.Speed}|{s.Motion?.Stop}";

    [Fact]
    public void 이단계_명부는_바탕_3연격에_1단계의_습관을_겨냥한_넷을_더한_다섯이다()
    {
        // 설계 §4 · §12 「2단계 명부」 — "2단계의 추가 패턴은 지금 말한 것만" 에 1단계의 바탕(3연격)을 두고, "점프공격은 2단계에선 그냥 3번" 이
        // 단발을 대신한다. 순서는 뽑기 좌표다(stages.json 의 _note_순서) — 설계의 표 그대로다. 고르기는 아직 무작위(uniform)다.
        StageDef two = TestConfigs.Stages()["2"];

        two.Patterns.ShouldBe(new[] { "3연격", "점프 3연속", "1타 돌진", "1타 잡기", "엇박 3연격" });
        two.Want.ShouldBe(5);
        two.Picker.ShouldBe("uniform");
    }

    [Fact]
    public void 일타로_여는_세_패턴은_1_30초까지_한_틱도_다르지_않다()
    {
        // 설계 §4 · §4.6 · §4.7 — 1.30초(78틱) 앞의 단계는 3연격 · 1타 돌진 · 1타 잡기가 그림도 판정도 같다. 멀리서 지켜보는 사람이 "3연격이구나"
        // 하고 후딜을 기다리게 두는 것이 돌진의 미끼이고, 1타를 대시로 흘린 사람이 다음 대시를 준비하게 두는 것이 잡기의 미끼다. 1.30초에
        // 셋이 갈린다 — 3연격은 attack2 를 들고, 돌진은 달리고, 잡기는 idle 로 선다.
        Dictionary<string, PatternDef> p = Patterns();
        string[] Before(string id) => p[id].Timeline.Where(s => TickOf(s) < 78).Select(Sig).ToArray();
        string[] At(string id) => p[id].Timeline.Where(s => TickOf(s) == 78).Select(s => s.Anim ?? "").ToArray();

        Before("3연격").Length.ShouldBe(4, "3연격의 1타 네 장이 아니다 — 이 가드가 무엇과 견주는지 모른다");
        Before("1타 돌진").ShouldBe(Before("3연격"));
        Before("1타 잡기").ShouldBe(Before("3연격"));

        At("3연격").ShouldBe(new[] { "attack2" });
        At("1타 돌진").ShouldBe(new[] { "run", "attack3" });
        At("1타 잡기").ShouldBe(new[] { "idle" });
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
    public void 점프_3연속은_점프_공격을_1_50초마다_세_번_그대로_잇는다()
    {
        // 설계 §4.8 — "점프공격은 2단계에선 그냥 3번". 점프 공격의 1.50초를 0 · 1.50 · 3.00초에 그대로 세 번 적었고 4.50초에 끝난다.
        // 착지 창은 60 · 150 · 240틱이다 — 창 사이 90틱에 앞 창을 넘은 누름이 내리고도 다음 창을 넘는 누름이 30틱 남는다.
        Dictionary<string, PatternDef> p = Patterns();
        List<PatternStep> one = p["점프 공격"].Timeline;
        List<PatternStep> three = p["점프 3연속"].Timeline;
        int body = one.Count - 1;

        three.Count.ShouldBe((3 * body) + 1);
        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < body; i++)
            {
                PatternStep a = one[i];
                PatternStep b = three[(round * body) + i];
                (b.T - (1.5 * round)).ShouldBe(a.T, 1e-9, $"{round + 1}번째 {i}번 장의 시각이 점프 공격과 다르다");
                Sig(b)[Sig(b).IndexOf('|', StringComparison.Ordinal)..].ShouldBe(Sig(a)[Sig(a).IndexOf('|', StringComparison.Ordinal)..],
                    $"{round + 1}번째 {i}번 장이 점프 공격과 다른 그림 · 판정 · 움직임이다");
            }
        }

        three[^1].T.ShouldBe(4.5, 1e-9);
        three.Where(s => s.Kind == "active").Select(TickOf).ShouldBe(new[] { 60, 150, 240 });
    }

    [Fact]
    public void 판정의_답은_판정_단계에만_거짓으로_적는다()
    {
        // 설계 §7.3 · §8.1 — dash · guard · parry 는 태그를 **좁히기만** 한다. true 는 태그대로라 적을 까닭이 없고, 적으면 "넓힌다" 로 읽혀
        // 태그가 막은 수단을 연다고 오해한다 — 규칙은 태그와 답을 둘 다 봐서 넓히지 못한다(HitResolver.Effective). 판정이 아닌 단계의 답과
        // 붙드는 시간은 아무도 안 읽는다 — JsonData 가 조용히 받으므로 여기서 막는다.
        int answers = 0;
        foreach ((string id, PatternDef def) in Patterns())
        {
            foreach (PatternStep s in def.Timeline)
            {
                bool?[] keys = { s.Dash, s.Guard, s.Parry };
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
        // 설계 §4.7 「패턴이 2.70초에 끝나는 이유」 — 흰 구가 붙들고 있는 동안 보스가 다음 패턴을 시작하면 그림이 거짓말이다. 창이 열린 뒤
        // 붙드는 시간이 지나야 끝난다(1.70 + 1.0 = 2.70). 창의 끝 틱에 잡히면 몇 틱 더 붙들리지만 그 사이는 간격(48틱)이라 다음 칼이 없다.
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
    public void 실제_잡기는_점프만_받고_1_0초_붙들고_1타는_다_받는다()
    {
        // 설계 §4.7 — 잡기의 답은 대시 · 가드 · 패리 셋 다 거짓이고 점프만 참이다(높이 60 띠를 실제 캐릭터가 창 8틱 내내 넘는다). 1타는 3연격의
        // 1타 그대로라 다 받는다 — 한 패턴 안에서 판정마다 답이 다른 첫 자리다(설계 §7.3).
        HitBox[] hits = BossHits.Of(Patterns()["1타 잡기"], TestConfigs.HitShapes())
            .Where(h => h is not null).Select(h => h!.Value).ToArray();

        hits.Length.ShouldBe(2);
        (hits[0].Dashable, hits[0].Guardable, hits[0].Parryable, hits[0].GrabHoldSeconds).ShouldBe((true, true, true, 0.0), "1타");
        (hits[1].Dashable, hits[1].Guardable, hits[1].Parryable).ShouldBe((false, false, false), "잡기");
        hits[1].GrabHoldSeconds.ShouldBe(1.0);
        hits[1].Damage.ShouldBe(25);

        // 점프는 자리마다 창이 열릴 때 잰다(#85 · JumpClearance) — 바닥 전체 띠라 어디서든 같다. 실제 캐릭터가 두고 서는 자리(115)에서 넘는다.
        foreach ((string who, FighterConfig c) in TestConfigs.Fighters())
        {
            new JumpClearance(c).Clears(hits[1].Shape, new Placement(960, 0, 1), 960 + 115, BattleSim.TicksFor(hits[1].ActiveSeconds))
                .ShouldBeTrue($"{who}: 잡기를 뛰어 못 넘는다");
        }
    }

    [Fact]
    public void 돌진은_빠르기와_멈출_거리를_갖고_뒤_단계는_같은_시각에서_도착을_기다린다()
    {
        // 설계 §4.6 · §8.1 — rush 는 speed · stop 을 읽는다. 둘 다 required 가 아니라(도약은 height · air 를 쓴다) 키 이름이 틀리면 JsonData 가
        // 조용히 0 으로 둔다: 빠르기 0 이면 돌진이 그 자리에서 끝나 3타가 멀리서 헛친다. 돌진은 패턴 시계를 세우므로 뒤 단계(3타의 선딜)의
        // T 는 **선 시계**로 적는다 — 돌진과 같은 시각이다. 3타는 그 15틱 뒤다(도착 뒤 A + 15).
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
                PatternStep strike = def.Timeline.Skip(i + 1).First(s => s.Kind == "active");
                (TickOf(strike) - TickOf(def.Timeline[i])).ShouldBe(15, $"{id}: 3타가 도착 뒤 15틱이 아니다");
            }
        }

        rushes.ShouldBeGreaterThan(0, "돌진이 하나도 없다 — 이 가드가 아무것도 안 본다");
    }
}
