using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 태그는 <b>나중에 망의 입력이 된다.</b> 태그가 타임라인과 어긋나면 망은 거짓을 배우고,
/// 그 거짓은 "개인화가 잘 안 되네" 로만 보인다 — 원인을 찾을 길이 없다.
/// 그래서 사람이 손으로 단 태그를 기하와 대조하고, 타임라인이 틱 · 그림 · 움직임의 규약을 지키는지 본다 (설계 §8.1).
/// </summary>
public class PatternDataTests
{
    /// <summary>선 몸통의 키. 대공이 "지상은 안전" 이 되려면 판정 바닥이 이것보다 위여야 한다.</summary>
    private const double _standingHeight = 120;

    private static Dictionary<string, PatternDef> Load() =>
        JsonData<PatternDef>.ParseTable(File.ReadAllText(Path.Combine("data", "patterns.json")), "patterns.json");

    /// <summary>패턴의 판정들 — 판을 세울 때처럼 <see cref="BossHits"/> 가 짓는다.</summary>
    private static IEnumerable<HitBox> Hits(PatternDef def) =>
        BossHits.Of(def, TestConfigs.HitShapes()).Where(h => h is not null).Select(h => h!.Value);

    /// <summary>
    /// 보스 발 중심에서 <paramref name="dx"/> 떨어져(보는 쪽이 +) 선 이 캐릭터가 <b>서서는 맞고</b>, 제자리로 뛰면 창 내내 몸이 모양
    /// 밖에 있을 수 있나 (#85 · <see cref="JumpClearance"/>). 서서도 안 맞는 자리는 점프가 답인 자리가 아니라 뺀다.
    /// </summary>
    private static bool JumpAnswers(HitBox hit, FighterConfig fighter, double dx)
    {
        var at = new Placement(960, 0, 1);
        var standing = new HitRect(960 + dx - fighter.HalfWidth, 960 + dx + fighter.HalfWidth, 0, fighter.Height);
        return ShapeHit.Test(hit.Shape, at, standing) == ShapeContact.Overlap
            && new JumpClearance(fighter).Clears(hit.Shape, at, 960 + dx, BattleSim.TicksFor(hit.ActiveSeconds));
    }

    /// <summary>단계가 드는 틱 — 러너와 같은 반올림이다(<see cref="BattleSim.TicksFor"/> · 패턴의 첫 틱이 1).</summary>
    private static int TickOf(PatternStep step) => BattleSim.TicksFor(step.T);

    [Fact]
    public void 실제_patterns_json_이_읽힌다()
    {
        Load().ShouldNotBeEmpty();
    }

    [Fact]
    public void 타임라인은_시간순이고_active_를_적어도_하나_갖는다()
    {
        foreach ((string id, PatternDef def) in Load())
        {
            def.Timeline.Select(s => s.T).ToList()
                .ShouldBeInOrder(SortDirection.Ascending, $"{id}: 타임라인이 시간순이 아니다");
            def.Timeline.ShouldContain(s => s.Kind == "active", $"{id}: active 단계가 없다");
        }
    }

    [Fact]
    public void 판정_단계는_hitbox_band_rects_중_꼭_하나를_갖는다()
    {
        // 설계 §8.1 · 설계 2026-09-29 조각1 §2.1 — 판정 단계는 그림에서 뽑은 모양의 id(hitbox) · 바닥 띠(band) · 손으로 적은 사각형(rects) 중
        // **꼭 하나**를 갖는다. 둘 이상이면 어느 것이 치는지 데이터만 보고 모르고, 없으면 판을 세울 때 거절된다(BossHits). id 는 hitboxes.json 에
        // 있어야 한다 — JsonData 는 모르는 키를 조용히 버리므로 "hitbx" 같은 오타는 빌드를 그냥 지나간다.
        Dictionary<string, HitShape> shapes = TestConfigs.HitShapes();
        int checkedSteps = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline)
            {
                if (step.Kind != "active")
                {
                    (step.Hitbox is null && step.Band is null && step.Rects is null)
                        .ShouldBeTrue($"{id}: t={step.T} 판정이 아닌 단계({step.Kind})에 모양이 있다");
                    continue;
                }

                checkedSteps++;
                new object?[] { step.Hitbox, step.Band, step.Rects }.Count(o => o is not null)
                    .ShouldBe(1, $"{id}: t={step.T} 판정 단계는 hitbox · band · rects 중 꼭 하나다");
                step.Damage.ShouldBeGreaterThan(0, $"{id}: t={step.T} 판정인데 피해가 0 이다");
                if (step.Hitbox is { } hitbox)
                {
                    shapes.ShouldContainKey(hitbox, $"{id}: t={step.T} 의 hitbox {hitbox} 가 hitboxes.json 에 없다");
                }
                else if (step.Band is { } b)
                {
                    b.Count.ShouldBe(4, $"{id}: t={step.T} band 는 [안쪽, 바깥쪽, 아래, 위] 넷이다");
                    b[0].ShouldBeLessThanOrEqualTo(b[1], $"{id}: t={step.T} band 의 안쪽이 바깥쪽보다 멀다");
                    b[2].ShouldBeLessThanOrEqualTo(b[3], $"{id}: t={step.T} band 의 아래가 위보다 높다");
                }
                else
                {
                    step.Rects!.ShouldNotBeEmpty($"{id}: t={step.T} rects 가 비었다");
                    foreach (IReadOnlyList<double> r in step.Rects!)
                    {
                        r.Count.ShouldBe(4, $"{id}: t={step.T} rects 의 사각형은 [x0, x1, y0, y1] 넷이다");
                        r[0].ShouldBeLessThanOrEqualTo(r[1], $"{id}: t={step.T} rects 의 x0 이 x1 보다 크다");
                        r[2].ShouldBeLessThanOrEqualTo(r[3], $"{id}: t={step.T} rects 의 y0 이 y1 보다 크다");
                    }
                }
            }
        }

        checkedSteps.ShouldBeGreaterThan(0, "판정이 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 그림을_뒤집는_단계는_그림이_있다()
    {
        // 설계 2026-09-29 조각1 §2.1 — mirror 는 뷰만 읽는 칸이다(anim · frame 과 같다): 그 단계의 그림을 보는 쪽의 반대로 그린다. 그림이 없는 단계
        // (end)에 적으면 뒤집을 것이 없다 — 오타이거나 단계를 잘못 짚은 것이다.
        int mirrored = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline.Where(s => s.Mirror))
            {
                mirrored++;
                step.Anim.ShouldNotBeNull($"{id}: t={step.T} 그림이 없는 단계에 mirror 가 있다");
            }
        }

        mirrored.ShouldBeGreaterThan(0, "뒤집는 단계가 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 캔슬_지점은_단계의_경계고_정수_틱이고_패턴_안이며_판정_창과_움직임_밖이다()
    {
        // 설계 2026-09-29 조각1 §3.1 — 캔슬하는 틱에 러너는 그 단계에 안 들고 하던 동작을 걷는다(§3.2). 그래서 지점마다 넷을 본다.
        // ① 그 틱에 드는 단계가 있다 — 장의 경계라 뷰가 반 장을 안 그린다. ② t × 60 이 정수다 — 반 틱이면 반올림이 지점을 옆 장으로 민다.
        // ③ 0 < t < 끝 — 첫 틱이나 끝에서 끊으면 끊을 것이 없다. ④ 앞 판정 창이 닫힌 뒤이고 움직임(도약 · 돌진) 밖이다 — 끊는 틱에 열린 창도
        // 도는 움직임도 없어야 걷을 것이 러너 하나다. 지점은 시간순이고 겹치지 않는다(계획은 칸 번호로 고른다).
        int points = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            IReadOnlyList<CancelPointDef> cancel = def.CancelPoints ?? [];
            for (int k = 0; k < cancel.Count; k++)
            {
                points++;
                double t = cancel[k].T;
                int tick = BattleSim.TicksFor(t);
                string where = $"{id}: 캔슬 지점 {k}(t={t})";

                (t * 60).ShouldBe(Math.Round(t * 60), 1e-9, $"{where} 가 정수 틱이 아니다");
                def.Timeline.ShouldContain(s => TickOf(s) == tick, $"{where} 에 드는 단계가 없다 — 장의 경계가 아니다");
                t.ShouldBeGreaterThan(0, $"{where} 가 패턴의 첫 틱이다");
                t.ShouldBeLessThan(def.Duration, $"{where} 가 패턴의 끝이거나 그 뒤다");
                if (k > 0)
                {
                    t.ShouldBeGreaterThan(cancel[k - 1].T, $"{where} 가 앞 지점보다 이르거나 같다");
                }

                foreach (PatternStep step in def.Timeline.Where(s => TickOf(s) < tick))
                {
                    if (step.Kind == "active")
                    {
                        (TickOf(step) + BattleSim.TicksFor(step.ActiveSeconds)).ShouldBeLessThanOrEqualTo(tick,
                            $"{where} 에 t={step.T} 의 판정 창이 아직 열려 있다");
                    }

                    if (step.Motion is { Id: "leap" } leap)
                    {
                        (TickOf(step) + BattleSim.TicksFor(leap.Air)).ShouldBeLessThanOrEqualTo(tick, $"{where} 에 t={step.T} 의 도약이 아직 떠 있다");
                    }
                }

                def.Timeline.Where(s => s.Motion is { Id: "rush" }).ShouldAllBe(s => TickOf(s) != tick, $"{where} 가 돌진이 서는 틱이다");
            }
        }

        points.ShouldBeGreaterThan(0, "캔슬 지점이 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void Multi_hit_태그가_active_개수와_같다()
    {
        foreach ((string id, PatternDef def) in Load())
        {
            int actives = def.Timeline.Count(s => s.Kind == "active");
            def.Tags.MultiHit.ShouldBe(actives, $"{id}: multi_hit={def.Tags.MultiHit} 인데 active 는 {actives}개다");
        }
    }

    [Fact]
    public void Jumpable_태그는_판정_하나라도_점프로_넘을_수_있는가와_같다()
    {
        // 설계 §7.3 — 점프 가능은 **판정마다 · 자리마다** 캐릭터의 실제 점프로 잰다(JumpClearance — 관측의 JumpAvailable 이 창이 열린
        // 자리의 그 값이다 · #85). 태그 jumpable 은 패턴의 요약(망의 입력)이라 "서서는 맞는 자리에서 뛰어 넘을 수 있는 판정이 하나라도
        // 있다" 와 같아야 한다 — 보스 등 뒤 600 부터 앞 600 까지 한 px 씩 잰다. 서서도 안 맞는 자리는 빼야 뜻이 선다: 사거리 밖에서는
        // 어느 판정이든 "뛰어도 산다".
        Dictionary<string, FighterConfig> fighters = TestConfigs.Fighters();

        // 캐릭터가 없으면 아래 foreach 가 공허하게 참이다 — 이 가드가 한 번 그렇게 죽은 적이 있다.
        fighters.ShouldNotBeEmpty("캐릭터가 하나도 없다 — 이 가드가 아무것도 안 본다");

        foreach ((string id, PatternDef def) in Load())
        {
            foreach ((string who, FighterConfig c) in fighters)
            {
                Hits(def).Any(h => Enumerable.Range(-600, 1201).Any(dx => JumpAnswers(h, c, dx))).ShouldBe(def.Tags.Jumpable,
                    $"{id}: jumpable={def.Tags.Jumpable} 인데 {who} 의 점프로 잰 판정들이 그 말과 다르다");
            }
        }
    }

    [Fact]
    public void 실제_3연격의_점프_가능은_자리마다_다르고_점프_공격의_착지는_어디서든_넘는다()
    {
        // 설계 §4.1 · §7.3 (#85) — 서서는 맞는 자리에서 잰다. 보스 앞 115(두고 서는 자리)에서는 셋 다 넘는다 — 2 · 3타도 몸에 닿는
        // 칸은 낮다. 앞 300 에서는 2타는 넘고 3타는 높은 궤적(566.5) 안이라 못 넘는다. 등 뒤 300 에서는 2타의 높은 궤적(346.5) 아래라
        // 못 넘는다(3타는 거기서 서서도 안 맞는다 — 등 뒤 궤적은 412.5 위에만 있다).
        // 모양 전체의 윗끝 하나로 재던 때(#72)는 어디서나 [참, 거짓, 거짓] — "1타만 넘는다" 였다. 이름을 바꿨다(옛 이름:
        // 실제_3연격은_1타만_점프로_넘고_점프_공격의_착지는_넘는다). 점프 공격의 착지 띠(높이 60)는 바닥 전체라 어디서든 53틱 몸이 빈다.
        Dictionary<string, PatternDef> patterns = Load();
        foreach ((string who, FighterConfig c) in TestConfigs.Fighters())
        {
            HitBox[] triple = Hits(patterns["3연격"]).ToArray();
            triple.Select(h => JumpAnswers(h, c, 115)).ShouldBe(new[] { true, true, true }, $"{who}: 3연격 · 보스 앞 115");
            triple.Skip(1).Select(h => JumpAnswers(h, c, 300)).ShouldBe(new[] { true, false }, $"{who}: 2 · 3타 · 앞 300");
            JumpAnswers(triple[1], c, -300).ShouldBeFalse($"{who}: 2타 · 등 뒤 300");
            JumpAnswers(Hits(patterns["점프 공격"]).Single(), c, -700).ShouldBeTrue($"{who}: 점프 공격 · 멀리");
        }
    }

    [Fact]
    public void Anti_air_태그가_판정_바닥과_맞는다()
    {
        // 대공은 지상이 안전하다 — 판정의 아래끝이 땅에서 떠 있어야 한다.
        foreach ((string id, PatternDef def) in Load())
        {
            bool offGround = Hits(def).Any(h => h.Shape.Bounds.Y0 > _standingHeight);
            def.Tags.AntiAir.ShouldBe(offGround, $"{id}: anti_air={def.Tags.AntiAir} 인데 판정 바닥이 맞지 않는다");
        }
    }

    [Fact]
    public void 패리_가능_여부와_패리_창이_같은_말을_한다()
    {
        // 이 둘은 이제 **같은 사실의 두 표현**이다. HitResolver 가 유효 창을 파이터와 패턴 중
        // 좁은 쪽으로 잡으므로, parryable=true 인데 창이 0 이면 그 패턴은 사실 패리 불가인데
        // DodgeEvent.ParryAvailable 은 "가능했다" 고 싣는다 — 의존도 축의 분모가 거짓이 된다.
        foreach ((string id, PatternDef def) in Load())
        {
            def.Tags.Parryable.ShouldBe(def.Tags.ParryWindow > 0,
                $"{id}: parryable={def.Tags.Parryable} 인데 parry_window={def.Tags.ParryWindow} 다");
        }
    }

    [Fact]
    public void 열려_있다고_한_창은_적어도_한_틱은_열려_있다()
    {
        // 창이 0 이라는 것은 "그 수단으로는 못 피한다" 는 뜻이고, 0 이 아니라는 것은
        // "피할 수 있다" 는 뜻이다. 한 틱(1/60초)보다 짧은 양수는 그 둘 중 어느 쪽도 아니다 —
        // 값으로는 "가능" 이라 태그가 그렇게 실리는데 실제로는 한 번도 안에 들어갈 수 없다.
        // 태그는 망의 입력이 되므로 그 간극이 그대로 거짓이 된다.
        foreach ((string id, PatternDef def) in Load())
        {
            def.Tags.DashWindow.ShouldBeGreaterThanOrEqualTo(0, $"{id}: dash_window 가 음수다");
            if (def.Tags.DashWindow > 0)
            {
                def.Tags.DashWindow.ShouldBeGreaterThanOrEqualTo(BattleSim.Dt,
                    $"{id}: dash_window={def.Tags.DashWindow} 가 한 틱보다 짧다 — 0 이 아닌데 실제로는 대시 불가다");
            }

            if (def.Tags.Parryable)
            {
                def.Tags.ParryWindow.ShouldBeGreaterThanOrEqualTo(BattleSim.Dt,
                    $"{id}: parry_window={def.Tags.ParryWindow} 가 한 틱보다 짧다 — 패리 가능이라 실렸는데 못 받는다");
            }
        }
    }

    [Fact]
    public void 판정_창은_음수가_아니다()
    {
        // active_seconds 가 음수면 BattleSim.TicksFor 가 조용히 한 틱으로 읽는다 — 데이터가 틀렸다는 말이 어디에도
        // 안 남고 판정은 멀쩡히 돈다. CLAUDE.md §5 가 음수 값을 규칙 위반으로 치는 바로 그 자리다 (이슈 #59 · 최종 리뷰).
        int windows = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline.Where(s => s.Kind == "active"))
            {
                step.ActiveSeconds.ShouldBeGreaterThanOrEqualTo(0,
                    $"{id}: t={step.T} 의 active_seconds={step.ActiveSeconds} 가 음수다 — 한 틱으로 조용히 읽힌다");
                windows++;
            }
        }

        windows.ShouldBeGreaterThan(0, "active 가 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 판정_창은_패턴이_끝나기_전에_닫힌다()
    {
        // 창이 패턴보다 오래 살면 판정이 **쉬는 보스**의 손에 남는다 (이슈 #59 · 최종 리뷰). 러너가 끝나면
        // BattleSim 은 쉬는 갈래로 가서 보스를 돌려세우고(Boss.Face) 걸린다(Approach) — 살아 있는 칼이 보스를 따라
        // 돌고 걸어서, 그림이 말한 자리가 아닌 곳을 친다. 끝은 러너가 멈추는 시각(end 의 t)이다.
        int windows = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline.Where(s => s.Kind == "active"))
            {
                (step.T + step.ActiveSeconds).ShouldBeLessThanOrEqualTo(def.Duration,
                    $"{id}: t={step.T} + active_seconds={step.ActiveSeconds} 가 패턴의 끝을 넘는다(end={def.Duration})"
                    + " — 보스가 쉬는 동안에도 칼이 살아 있다");
                windows++;
            }
        }

        windows.ShouldBeGreaterThan(0, "active 가 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 판정_단계의_시각은_정수_틱이다()
    {
        // 설계 §3.6 ⑤ — 판정이 반 틱에 서면 반올림이 창을 한 틱 밀어, 창이 다음 장으로 넘어간 뒤까지 산다. 8fps 한 장은
        // 7.5틱이라 한 장씩 붙들면 쉽게 그렇게 된다(엇박 3연격이 f0 를 한 장이 아니라 0.15초 붙드는 까닭이다 · §4.9).
        // 그림 장의 경계(0.725 = 43.5틱)는 반 틱이어도 된다 — 뷰가 규칙의 단계를 그리므로 그림과 판정은 같은 틱에 바뀐다.
        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline.Where(s => s.Kind == "active"))
            {
                double ticks = step.T / BattleSim.Dt;
                Math.Abs(ticks - Math.Round(ticks)).ShouldBeLessThan(1e-9, $"{id}: t={step.T} 는 {ticks} 틱이다 — 판정은 정수 틱에 선다");
            }
        }
    }

    [Fact]
    public void 다음_단계는_판정_창이_닫힌_뒤에_든다()
    {
        // 설계 §3.6 ⑤ — 창은 그 판정의 장 동안만 산다. 다음 단계(다음 장)가 창이 닫히기 전에 들면 그림은 다음 장인데
        // 판정은 앞 장의 궤적으로 산다 — 보이는 것이 곧 맞는 것이 아니게 된다.
        foreach ((string id, PatternDef def) in Load())
        {
            for (int i = 0; i + 1 < def.Timeline.Count; i++)
            {
                PatternStep step = def.Timeline[i];
                if (step.Kind != "active")
                {
                    continue;
                }

                int closed = TickOf(step) + BattleSim.TicksFor(step.ActiveSeconds);
                TickOf(def.Timeline[i + 1]).ShouldBeGreaterThanOrEqualTo(closed,
                    $"{id}: t={step.T} 의 창(~{closed - 1}틱)이 닫히기 전에 다음 단계(t={def.Timeline[i + 1].T})가 든다");
            }
        }
    }

    [Fact]
    public void 움직임은_등록표에_있고_판정_창과_안_겹친다()
    {
        // 설계 §3.5 6 · §8.1 — 보스는 판정 창 동안 움직이지 않는다. 그래서 모양을 놓는 자리를 틱마다 한 번 잡는 구조로
        // 충분하다. 움직임은 판정이 아닌 단계에만 달고, 끝나는 틱은 창이 열리는 틱까지다(도약은 착지하는 그 틱에 창이 열린다 —
        // 그 틱의 자리가 이미 착지 자리다). id 는 등록표에 있어야 한다 — 없으면 BattleSim 이 [E] 를 남기고 제자리에서 돈다.
        int motions = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline.Where(s => s.Motion is not null))
            {
                motions++;
                MotionDef motion = step.Motion!;
                step.Kind.ShouldNotBe("active", $"{id}: t={step.T} 판정 단계에 움직임이 있다");
                BossMotions.Ids.ShouldContain(motion.Id, $"{id}: 움직임 {motion.Id} 가 등록표에 없다");

                int from = TickOf(step);
                int to = from + BattleSim.TicksFor(motion.Air);
                foreach (PatternStep active in def.Timeline.Where(s => s.Kind == "active"))
                {
                    int opens = TickOf(active);
                    int closes = opens + BattleSim.TicksFor(active.ActiveSeconds);
                    (opens >= to || closes <= from).ShouldBeTrue(
                        $"{id}: 움직임({from} ~ {to}틱)이 t={active.T} 의 창({opens} ~ {closes - 1}틱)과 겹친다");
                }
            }
        }

        motions.ShouldBeGreaterThan(0, "움직임이 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 도약은_착지_판정이_서는_틱에_내린다()
    {
        // 설계 §4.2 · §8.1 — 도약 시각 + air = 착지 판정 시각. 포물선을 식으로 그리는 것이 착지를 정확한 틱에 두려고서다 —
        // 한 틱이라도 이르거나 늦으면 판정이 공중의 보스에게서 서거나(보스가 판정 창 동안 움직인다) 내린 뒤 빈 틱이 생긴다.
        int leaps = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            for (int i = 0; i < def.Timeline.Count; i++)
            {
                if (def.Timeline[i].Motion is not { Id: "leap" } leap)
                {
                    continue;
                }

                leaps++;

                // height · air 는 도약만 쓰는 수치라 required 가 아니다(돌진은 speed · stop 을 쓴다 · #78). 키 이름이 틀리면 JsonData 가
                // 조용히 버려 0 이 되고, 보스가 땅에서 미끄러지며 36틱 내내 파이터의 칼에 닿는다 — 여기서 막는다.
                leap.Height.ShouldBeGreaterThan(0, $"{id}: 도약(t={def.Timeline[i].T})의 height 가 없다");
                leap.Air.ShouldBeGreaterThan(0, $"{id}: 도약(t={def.Timeline[i].T})의 air 가 없다");
                PatternStep landing = def.Timeline.Skip(i + 1).First(s => s.Kind == "active");
                TickOf(landing).ShouldBe(TickOf(def.Timeline[i]) + BattleSim.TicksFor(leap.Air),
                    $"{id}: 도약(t={def.Timeline[i].T}) + air {leap.Air} 가 착지 판정(t={landing.T})과 다른 틱이다");
            }
        }

        leaps.ShouldBeGreaterThan(0, "도약이 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 도약은_탈진보다_짧아_공중에서_무너진_보스가_탈진_안에_내린다()
    {
        // 설계 §4.2 · #71 — 공중에서 게이지로 무너진 보스는 끊긴 도약의 높이만 따라 내리고, 그 내림은 탈진 갈래에서만 돈다
        // (BattleSim.Fall). 뜬 시간이 탈진보다 길면 풀린 보스가 공중에 선 채 쉬는 갈래로 가 떠 있는다. 지금은 0.60 < 1.5 다.
        int exhaust = BattleSim.TicksFor(TestConfigs.Boss().ExhaustSeconds);
        int leaps = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline.Where(s => s.Motion is { Id: "leap" }))
            {
                leaps++;
                BattleSim.TicksFor(step.Motion!.Air).ShouldBeLessThan(exhaust,
                    $"{id}: 도약(t={step.T})이 {step.Motion.Air}초 떠 있다 — 탈진({exhaust}틱) 안에 못 내린다");
            }
        }

        leaps.ShouldBeGreaterThan(0, "도약이 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 단계의_그림은_보스_팩에_있는_이름과_장이다()
    {
        // 설계 §6 — 뷰는 규칙의 단계가 가리키는 장(anim · frame)을 그대로 붙든다. 이름이 틀리면 뷰가 [W] 한 줄 남기고
        // 아무것도 안 바꾸고(PlaySafe), 장이 틀리면 엔진이 ERROR: 를 찍는다. JsonData 는 모르는 키를 조용히 버리므로
        // 오타가 빌드를 그냥 지나간다 — 그래서 보스 팩의 .tres 와 대 본다. end 가 아닌 단계는 모두 그림을 갖는다.
        Dictionary<string, int> pack = TestConfigs.PackFrames(TestConfigs.Boss().Sprite);
        pack.ShouldContainKey("attack", "팩을 못 읽었다 — 이 가드가 아무것도 안 본다");

        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline)
            {
                if (step.Kind == "end")
                {
                    step.Anim.ShouldBeNull($"{id}: end 에 그림이 있다");
                    continue;
                }

                step.Anim.ShouldNotBeNull($"{id}: t={step.T} 에 그림이 없다 — 뷰가 무엇을 그릴지 모른다");
                pack.ShouldContainKey(step.Anim!, $"{id}: t={step.T} 의 anim {step.Anim} 이 보스 팩에 없다");
                if (step.Frame is int frame)
                {
                    frame.ShouldBeInRange(0, pack[step.Anim!] - 1, $"{id}: t={step.T} 의 {step.Anim} 에 {frame} 번 장이 없다");
                }
            }
        }
    }

    [Fact]
    public void 태그_문자열은_정해진_값만_쓴다()
    {
        string[] directions = { "in", "out", "either" };
        string[] reaches = { "close", "mid", "far" };
        foreach ((string id, PatternDef def) in Load())
        {
            directions.ShouldContain(def.Tags.DashDirection, $"{id}: dash_direction 이 이상하다");
            reaches.ShouldContain(def.Tags.Reach, $"{id}: reach 가 이상하다");
        }
    }

    [Fact]
    public void 타임라인의_kind_는_정해진_넷뿐이다()
    {
        // **오타는 조용하다.** PatternRunner 는 판정이 아닌 kind 를 그냥 지나가므로 "actvie" 라고 적으면 그 판정은 아무 일도
        // 안 하고, 게임은 멀쩡히 돌면서 한 대를 덜 때린다. 헛스윙(feint)은 옛 변종과 같이 걷었다(#72 · 설계 §8.1).
        string[] kinds = { "windup", "active", "recover", "end" };
        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline)
            {
                kinds.ShouldContain(step.Kind, $"{id}: 모르는 kind={step.Kind} — 조용히 무시된다");
            }
        }
    }

    [Fact]
    public void 파이터_탈진은_3연격의_2타에서_3타까지를_덮는다()
    {
        // 설계 §5.5 — 파이터 탈진(가드 붕괴 · 스태미나 0) 1.1초의 근거는 3연격의 2타 → 3타 간격이다: 93틱 → 159틱 = 66틱. 2타에 깨진
        // 가드는 3타가 서는 틱까지 못 선다 — "남은 타격을 그대로 맞는 값" 이라는 뜻이 새 패턴 위에서 선다. 전에는 옛 변종들의 미끼
        // 간격(1.10 · 이슈 #54)이 근거였고, 키는 guard_break_lock 이었다(#71 에서 exhaust_seconds). 한쪽만 고치는 날 여기서 빨개진다.
        List<int> hits = Load()["3연격"].Timeline.Where(s => s.Kind == "active").Select(TickOf).ToList();
        hits.Count.ShouldBe(3);

        foreach ((string who, FighterConfig c) in TestConfigs.Fighters())
        {
            BattleSim.TicksFor(c.ExhaustSeconds).ShouldBeGreaterThanOrEqualTo(hits[2] - hits[1],
                $"{who}: 탈진 {c.ExhaustSeconds}초가 3연격의 2타 → 3타({hits[2] - hits[1]}틱)보다 짧다 — 깨진 사람이 3타 앞에서 다시 선다");
        }
    }
}
