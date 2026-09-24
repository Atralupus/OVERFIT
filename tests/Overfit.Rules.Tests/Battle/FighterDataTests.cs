using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class FighterDataTests
{
    private static Dictionary<string, FighterConfig> Load() =>
        JsonData<FighterConfig>.ParseTable(File.ReadAllText(Path.Combine("data", "fighters.json")), "fighters.json");

    /// <summary>
    /// 시뮬레이션이 볼 수 있는 가장 짧은 시간. 이보다 가는 차이는 규칙 층에서 관측되지 않으므로
    /// 애니메이션과 액션을 맞출 때의 허용 오차도 이것이다 — 더 좁게 잡으면 데이터에 적을 수 없는
    /// 소수(1/12초 = 0.0833…)를 요구하게 되고, 더 넓게 잡으면 한 프레임이 통째로 어긋나도 초록이다.
    /// </summary>
    private const double _halfTick = BattleSim.Dt / 2;

    [Fact]
    public void 캐릭터가_하나다()
    {
        // 셋(단검 · 중검 · 대검)이던 것을 하나로 줄였다 (이슈 #38). 캐릭터 3택은 성장 루프와
        // 같이 빠졌는데(이슈 #22) 데이터만 남아 있었고, 그림은 처음부터 셋이 같은 하나였다.
        // 아래 가드들이 전부 이 표를 순회하므로, 표가 비면 그것들이 통째로 공허하게 참이 된다.
        Load().Count.ShouldBe(1);
    }

    [Fact]
    public void 무적_창은_대시보다_짧다()
    {
        // 끝자락에 맞을 수 있어야 대시 타이밍이 축이 된다
        foreach ((string id, FighterConfig c) in Load())
        {
            c.DashIFrames.ShouldBeLessThan(c.DashDuration, $"{id}: 무적이 대시만큼 길면 타이밍이 의미가 없다");
        }
    }

    [Fact]
    public void 패리_창_셋이_연타_패리_기억_순으로_선다()
    {
        // 세 창의 **순서가 곧 규칙**이다 (이슈 #27 · #53).
        // 연타 창이 패리 창보다 넓으면 난사가 벌이 아니라 상이 되고,
        // 기억 창이 패리 창보다 좁으면 "늦게 눌렀다" 가 다시 "아무것도 안 했다" 와 같은 점이 된다 —
        // 그 창이 계측의 공을 그 누름에 붙들어 두는 것이라 판정보다 오래 살아야 한다.
        foreach ((string id, FighterConfig c) in Load())
        {
            c.ParrySpamWindow.ShouldBeLessThan(c.ParryPreciseWindow, $"{id}: 연타 징벌이 창을 안 좁힌다");
            c.ParryPreciseWindow.ShouldBeLessThan(c.ParryMemoryWindow, $"{id}: 패리 창이 기억 창보다 넓다");
            c.ParryCost.ShouldBeGreaterThan(0, $"{id}: 방어 자세가 공짜다 — 난사에 값이 없다");
        }
    }

    /// <summary>
    /// 대시 한 번이 실제로 가는 거리(px). <b>속도 × 지속을 곱해 적지 않는다</b> —
    /// 행동이 끝나는 틱에는 <c>Move</c> 가 이미 Idle 을 보므로 실제 이동 틱은 하나 적고,
    /// 그 한 틱(36.7px)이 "보스를 지나는가" 의 경계에 그대로 걸린다.
    /// <see cref="PatternDataTests"/> 의 점프 정점과 같은 이유로 규칙을 직접 돌린다.
    /// </summary>
    private static double DashTravel(FighterConfig config)
    {
        var fighter = new Fighter(config, TestConfigs.Arena(), TestConfigs.Arena().Width / 2);
        double start = fighter.X;
        fighter.Tick(new InputFrame(0, false, Dash: true, false, false), BattleSim.Dt);
        while (fighter.Action == FighterAction.Dash)
        {
            fighter.Tick(default, BattleSim.Dt);
        }

        return Math.Abs(fighter.X - start);
    }

    [Fact]
    public void 대시는_보스_몸을_한_번에_지난다()
    {
        // 이슈 #27 의 대시 요구다. "몸 하나를 지난다" 는 **닿은 자리에서 반대편 닿은 자리까지**,
        // 즉 보스 반폭 + 파이터 반폭의 두 배다 (보스 반폭 85 · 파이터 반폭 30 → 230px).
        // 전에는 1100 × 0.18 ≈ 198(실측 183)이라 몸 하나도 못 지났다 — 몸 충돌이 있던 때는
        // 벽에 막혀서 그 사실이 안 보였고, 통과가 열린 지금은 그대로 몸 안에 서게 된다.
        double bossHalf = TestConfigs.Boss().HalfWidth;
        foreach ((string id, FighterConfig c) in Load())
        {
            double crossing = 2 * (bossHalf + c.HalfWidth);
            DashTravel(c).ShouldBeGreaterThan(crossing,
                $"{id}: 대시가 {DashTravel(c):0}px 라 보스 몸({crossing:0}px)을 못 지난다");
        }
    }

    [Fact]
    public void 무적_비율이_대시마다_크게_다르지_않다()
    {
        // 사거리를 늘릴 때 **무적 비율을 건드리지 않았다**는 계약이다(이슈 #27).
        // 거리는 속도로 늘리고 지속·무적은 그대로 둔다 — 지속을 늘려 거리를 벌면 무적 비율이
        // 같이 움직여 "더 멀리 가는 같은 기술" 이 아니라 **다른 기술**이 된다.
        // 캐릭터가 여럿이 되면 전부 이 범위 안이어야 한다 — 그래야 캐릭터를 바꿔도
        // 대시 타이밍 축이 같은 것을 재는 값이다.
        foreach ((string id, FighterConfig c) in Load())
        {
            double share = c.DashIFrames / c.DashDuration;
            share.ShouldBeInRange(0.70, 0.85, $"{id}: 무적 비율 {share:0.00} 이 대시를 다른 기술로 만든다");
        }
    }

    // ── 차지 공격 (이슈 #40) ─────────────────────────────────────────────────

    [Fact]
    public void 차지는_2초에_최대다()
    {
        // 유저가 정한 값이다 (이슈 #40: "차지는 2초동안 최대로"). 마지막 단계의 시간이 곧
        // 최대 차지 시간이라 데이터에 키가 따로 없다 — 그래서 그 규약을 여기서 못박는다.
        foreach ((string id, FighterConfig c) in Load())
        {
            c.ChargeTiers[^1].Seconds.ShouldBe(2.0, $"{id}: 최대 차지가 2초가 아니다");
        }
    }

    [Fact]
    public void 차지_단계는_시간과_배수가_같이_오른다()
    {
        // 표의 **순서가 곧 규칙**이다 — Fighter.TierFor 가 "닿은 마지막 칸" 을 답으로 쓰므로
        // 시간이 뒤죽박죽이면 더 모은 쪽이 더 낮은 단계를 받는다. 첫 칸이 0초 ×1 이어야
        // "그냥 누른 것" 이 0단계로 서고, 배수가 안 오르면 모을 이유가 없다.
        foreach ((string id, FighterConfig c) in Load())
        {
            c.ChargeTiers.Count.ShouldBeGreaterThan(1, $"{id}: 단계가 하나뿐이면 차지가 아니다");
            c.ChargeTiers[0].Seconds.ShouldBe(0, $"{id}: 첫 칸이 0초가 아니다 — 그냥 누른 것이 0단계다");
            c.ChargeTiers[0].DamageMultiplier.ShouldBe(1.0, $"{id}: 안 모은 한 대의 피해가 attack_damage 가 아니다");

            for (int i = 1; i < c.ChargeTiers.Count; i++)
            {
                c.ChargeTiers[i].Seconds.ShouldBeGreaterThan(
                    c.ChargeTiers[i - 1].Seconds, $"{id}: 단계 {i} 의 시간이 앞 단계보다 안 크다");
                c.ChargeTiers[i].DamageMultiplier.ShouldBeGreaterThan(
                    c.ChargeTiers[i - 1].DamageMultiplier, $"{id}: 단계 {i} 를 모을 이유가 없다");
            }
        }
    }

    /// <summary>
    /// 한 패턴의 <b>마지막 판정</b>부터 다음 패턴의 <b>첫 판정</b>까지 맞을 일이 없는 시간(초).
    /// 패턴 꼬리(마지막 판정 → 끝) + 패턴 간격 + 다음 패턴의 선딜이다.
    /// <b>패리는 안 센다</b> — 차지는 패리와 상관없는 기술이라, 이 창은 아무것도 안 하고
    /// 서 있기만 해도 주어지는 시간이어야 한다.
    /// </summary>
    private static double Window(PatternDef ended, PatternDef next, double gap)
    {
        double lastActive = 0;
        double firstActive = double.PositiveInfinity;
        foreach (PatternStep step in ended.Timeline)
        {
            if (step.Kind == "active" && step.T > lastActive)
            {
                lastActive = step.T;
            }
        }

        foreach (PatternStep step in next.Timeline)
        {
            if (step.Kind == "active" && step.T < firstActive)
            {
                firstActive = step.T;
            }
        }

        return (ended.Duration - lastActive) + gap + firstActive;
    }

    /// <summary>
    /// 차지 <paramref name="tier"/> 단계의 칼이 닿기까지 서 있어야 하는 시간(초).
    /// <b>붙들고 있는 시간이 곧 선딜이다</b> — 그래서 더해지는 것은 모은 시간과 <b>남은</b> 선딜뿐이고,
    /// 0.8초 이상을 모으면 선딜은 이미 다 지나 판정까지의 시간만 남는다.
    /// </summary>
    private static double StandingTime(FighterConfig c, int tier)
    {
        double held = c.ChargeTiers[tier].Seconds;
        return held + Math.Max(0, c.AttackWindup - held) + c.AttackActive;
    }

    [Fact]
    public void 중간_차지는_백장의_빈_시간에_언제나_들어간다()
    {
        // **이 기술이 죽어 있지 않다는 증명이다.** 중간 단계(0.8초)는 1.2166초면 칼이 닿는데
        // 백장의 가장 좁은 빈 시간이 1.90초라, 어떤 패턴 뒤에 어떤 패턴이 와도 성립한다.
        // 여기가 깨지면 차지는 "쓸 수 있는 자리가 없는 기술" 이 된다.
        BossConfig boss = TestConfigs.Boss();
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();

        foreach ((string id, FighterConfig c) in Load())
        {
            double need = StandingTime(c, 1);
            foreach ((string a, PatternDef ended) in patterns)
            {
                foreach ((string b, PatternDef next) in patterns)
                {
                    Window(ended, next, boss.PatternGap).ShouldBeGreaterThan(need,
                        $"{id}: {a} → {b} 사이에 1단계 차지({need:0.000}초)가 안 들어간다");
                }
            }
        }
    }

    [Fact]
    public void 최대_차지가_백장의_빈_시간에_들어간다()
    {
        // **이 이슈에서 가장 중요한 숫자다** (이슈 #40). 최대 차지는 2.0833초를 서 있어야 칼이 닿고
        // (모으기 2.0 + 남은 선딜 0 + 판정 0.0833 — 붙드는 것이 곧 선딜이다),
        // 백장의 빈 시간은 1.90~2.40초다. 그래서 **패리 없이, 그냥 선 채로 여섯 짝에서 들어간다.**
        //
        // 안 들어가는 아홉 짝은 전부 **앞 변종이 `III-역습`** 인 경우다 (이슈 #48). 계열이 하나가 되며
        // 선딜이 0.85 로 통일돼서, 도박을 지는 것은 이제 다음 패턴의 선딜이 아니라 **앞 패턴의 꼬리**다 —
        // 계열에서 그것만 0.35 이고 나머지 여덟은 0.60 이다. 그리고 그 하나가 하필
        // **욕심을 벌하는 변종**인 것이 이 설계의 문장이다: 그 뒤에 2초를 모으는 것이 바로 그 욕심이다.
        //
        // 두 단언이 같이 있어야 이 설계가 지켜진다. 위가 깨지면 최대 차지는 아무도 못 쓰는
        // 장식이 되고, 아래가 깨지면 다음 패턴이 무엇이든 늘 되는 공짜가 된다.
        BossConfig boss = TestConfigs.Boss();
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();

        foreach ((string id, FighterConfig c) in Load())
        {
            double need = StandingTime(c, c.ChargeTiers.Count - 1);
            int pairs = 0, fits = 0;

            foreach ((_, PatternDef ended) in patterns)
            {
                foreach ((_, PatternDef next) in patterns)
                {
                    pairs++;
                    if (Window(ended, next, boss.PatternGap) >= need)
                    {
                        fits++;
                    }
                }
            }

            fits.ShouldBeGreaterThanOrEqualTo(pairs / 2,
                $"{id}: 최대 차지({need:0.000}초)가 {fits}/{pairs} 짝에만 들어간다 — 쓸 수 없는 기술이다");
            fits.ShouldBeLessThan(pairs,
                $"{id}: 다음 패턴이 무엇이든 최대 차지가 들어간다 — 2초를 서 있는 데 도박이 없다");
        }
    }

    [Fact]
    public void 공격_액션이_공격_애니메이션_한_번과_같은_길이다()
    {
        // **이 저장소에서 실제로 밟은 버그다** (이슈 #38). 공격 액션의 총 길이(선딜 + 판정 + 후딜)가
        // 애니메이션 재생 시간보다 짧으면, 액션이 끝나는 순간 뷰가 자세를 idle 로 되돌려
        // 애니메이션이 중간에서 잘린다. 중검은 0.30초짜리 액션으로 0.50초짜리 6프레임을 돌렸고,
        // 칼이 나가는 5번째 프레임까지 간 적이 한 번도 없었다 — **칼 휘두르는 그림이 안 보였다.**
        //
        // 그래서 순서를 뒤집는다: 애니메이션이 먼저고 액션 길이가 거기 맞춘다.
        foreach ((string id, FighterConfig c) in Load())
        {
            double anim = c.AttackAnimFrames / c.AttackAnimFps;
            double cycle = c.AttackWindup + c.AttackActive + c.AttackRecover;
            cycle.ShouldBe(anim, _halfTick,
                $"{id}: 공격 액션 {cycle:0.0000}초가 애니메이션 {anim:0.0000}초와 다르다 — 그림이 잘리거나 남는다");
        }
    }

    [Fact]
    public void 공격_판정이_칼이_지나가는_프레임_위에_선다()
    {
        // 판정이 서는 구간과 **화면에서 칼이 지나가는 구간**이 같은 자리여야 한다.
        // 어긋나면 "닿았는데 칼은 아직 등 뒤" 또는 그 반대가 되고, 플레이어는 사거리를 못 배운다.
        // blade_frame 은 시트를 실제로 열어서 정한 값이다 — 프레임 번호로 짐작한 것이 아니다.
        foreach ((string id, FighterConfig c) in Load())
        {
            double frame = 1 / c.AttackAnimFps;

            c.AttackAnimBladeFrame.ShouldBeInRange(0, c.AttackAnimFrames - 1,
                $"{id}: blade_frame={c.AttackAnimBladeFrame} 이 {c.AttackAnimFrames}프레임 밖이다");

            c.AttackWindup.ShouldBe(c.AttackAnimBladeFrame * frame, _halfTick,
                $"{id}: 선딜이 끝나는 자리가 칼이 나가는 {c.AttackAnimBladeFrame}번 프레임의 시작과 다르다");

            c.AttackActive.ShouldBeGreaterThanOrEqualTo(frame - _halfTick,
                $"{id}: 판정이 한 프레임보다 짧다 — 칼이 지나가는 그림 위에 판정이 못 선다");

            (c.AttackWindup + c.AttackActive).ShouldBeLessThanOrEqualTo(
                (c.AttackAnimFrames / c.AttackAnimFps) + _halfTick,
                $"{id}: 판정이 애니메이션 밖으로 넘친다");
        }
    }

    [Fact]
    public void 가드는_흘리되_받아치는_것보다는_나쁘다()
    {
        // 가드의 값은 **스태미나**로 낸다 (이슈 #47). 흘리는 피해가 0 이면 받아칠 이유가 없어지고
        // (패리의 상은 피해 0 이다), 1 이면 막는 것에 뜻이 없다 — 그 사이여야 창을 노릴 값이 생긴다.
        //
        // ⚠ 전에는 이 관계를 <c>parry_internal_ratio</c> 와 견줬다. 그 중간 단계가 없어지면서
        // (이슈 #53) 비교 대상이 **패리 그 자체**로 바뀌었다: 받아치면 0, 막으면 이만큼이다.
        foreach ((string id, FighterConfig c) in Load())
        {
            c.GuardChipRatio.ShouldBeGreaterThan(0, $"{id}: 가드가 공짜다 — 받아칠 이유가 없다");
            c.GuardChipRatio.ShouldBeLessThan(1, $"{id}: 가드가 전액을 흘린다 — 막는 것에 뜻이 없다");
            c.GuardStaminaPerDamage.ShouldBeGreaterThan(0, $"{id}: 가드 비용이 0 이다");
            c.GuardBreakLock.ShouldBeGreaterThan(0, $"{id}: 가드가 깨져도 굳지 않는다 — 붕괴에 값이 없다");
        }
    }

    [Fact]
    public void 가장_센_판정_하나는_가득_찬_스태미나로_받아낸다()
    {
        // 값이 피해에 비례하므로(guard_stamina_per_damage) 한 방이 스태미나를 통째로 넘으면
        // 가드는 **언제나 깨지는** 기술이 되고, 그러면 guard_break 라는 성질도 뜻을 잃는다 —
        // 깰 것이 이미 없다.
        double heaviest = TestConfigs.Patterns().Values
            .SelectMany(d => d.Timeline.Where(s => s.Kind == "active"))
            .Max(s => (double)s.Damage);

        foreach ((string id, FighterConfig c) in Load())
        {
            (heaviest * c.GuardStaminaPerDamage).ShouldBeLessThanOrEqualTo(c.MaxStamina,
                $"{id}: 가장 센 판정({heaviest})이 스태미나를 통째로 넘는다 — 가드가 언제나 깨진다");
        }
    }

}
