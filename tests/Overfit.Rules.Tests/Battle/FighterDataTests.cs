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

    // ── 2연격 (설계 §5.1) ────────────────────────────────────────────────────

    [Fact]
    public void 칼질은_1타와_2타_둘이다()
    {
        // 유저가 정한 모양이다 (설계 §5.1): 1타는 attack(팩의 attack1) · 2타는 attack2.
        foreach ((string id, FighterConfig c) in Load())
        {
            c.Combo.Count.ShouldBe(2, $"{id}: 2연격이 아니다");
            c.Combo[0].Anim.ShouldBe("attack", $"{id}: 1타의 그림");
            c.Combo[1].Anim.ShouldBe("attack2", $"{id}: 2타의 그림");
        }
    }

    [Fact]
    public void 연격_2타는_1타의_세_배고_반속이다()
    {
        // **유저가 정한 값이다** (설계 §5.1): "2타 3배 공격력, 속도는 반". 값을 바꾸면 이 테스트도 같이 고친다.
        foreach ((string id, FighterConfig c) in Load())
        {
            c.Combo[1].Damage.ShouldBe(3 * c.Combo[0].Damage, $"{id}: 2타가 1타의 세 배가 아니다");
            c.Combo[1].Fps.ShouldBe(c.Combo[0].Fps / 2, $"{id}: 2타가 반속이 아니다");
        }
    }

    [Fact]
    public void 칼질마다_액션이_그림_한_번과_같은_길이다()
    {
        // ← 공격_액션이_공격_애니메이션_한_번과_같은_길이다 의 주석 그대로
        //
        // **이 저장소에서 실제로 밟은 버그다** (이슈 #38). 공격 액션의 총 길이(선딜 + 판정 + 후딜)가
        // 애니메이션 재생 시간보다 짧으면, 액션이 끝나는 순간 뷰가 자세를 idle 로 되돌려
        // 애니메이션이 중간에서 잘린다. 중검은 0.30초짜리 액션으로 0.50초짜리 6프레임을 돌렸고,
        // 칼이 나가는 5번째 프레임까지 간 적이 한 번도 없었다 — **칼 휘두르는 그림이 안 보였다.**
        //
        // 그래서 순서를 뒤집는다: 애니메이션이 먼저고 액션 길이가 거기 맞춘다.
        //
        // "한 번" 은 **시트 전체가 아니라 탭이 도는 구간**이다 (이슈 #54). 선딜을 0.3333 → 0.0833 으로
        // 줄이면서 탭은 0번이 아니라 start_frame 에서 시작한다 — 칼을 뒤로 빼는 네 장을
        // 0.0833초에 다 돌릴 수는 없어서다. 그래서 재는 길이도 거기서 끝까지다. 요구는 그대로다:
        // 액션이 끝나는 순간과 그림이 끝나는 순간이 같아야 한다.
        foreach ((string id, FighterConfig c) in Load())
        {
            for (int i = 0; i < c.Combo.Count; i++)
            {
                ComboStepDef s = c.Combo[i];
                double anim = (s.Frames - s.StartFrame) / s.Fps;
                double cycle = s.Windup + s.Active + s.Recover;
                cycle.ShouldBe(anim, _halfTick,
                    $"{id} {i + 1}타: 액션 {cycle:0.0000}초가 {s.Anim} 의 {s.StartFrame}번부터 {anim:0.0000}초와 다르다"
                    + " — 그림이 잘리거나 남는다");
            }
        }
    }

    [Fact]
    public void 칼질마다_판정이_칼이_지나가는_장_위에_선다()
    {
        // ← 공격_판정이_칼이_지나가는_프레임_위에_선다 의 주석 그대로
        //
        // 판정이 서는 구간과 **화면에서 칼이 지나가는 구간**이 같은 자리여야 한다.
        // 어긋나면 "닿았는데 칼은 아직 등 뒤" 또는 그 반대가 되고, 플레이어는 사거리를 못 배운다.
        // blade_frame 은 시트를 실제로 열어서 정한 값이다 — 프레임 번호로 짐작한 것이 아니다.
        //
        // 선딜은 **탭이 시작하는 장에서 칼이 나가는 장까지**다 (이슈 #54). 시작하는 장은 칼이 나가는
        // 장보다 **앞이어야** 한다 — 같거나 뒤면 선딜 동안 화면에 칼이 이미 나가 있어, 판정이 서기도
        // 전에 그림이 "벴다" 고 말한다(이슈 #38 의 반대쪽 거짓말이다).
        foreach ((string id, FighterConfig c) in Load())
        {
            for (int i = 0; i < c.Combo.Count; i++)
            {
                ComboStepDef s = c.Combo[i];
                double frame = 1 / s.Fps;

                s.BladeFrame.ShouldBeInRange(0, s.Frames - 1,
                    $"{id} {i + 1}타: blade_frame={s.BladeFrame} 이 {s.Frames}장 밖이다");

                s.StartFrame.ShouldBeInRange(0, s.BladeFrame - 1,
                    $"{id} {i + 1}타: start_frame={s.StartFrame} 이 칼이 나가는 {s.BladeFrame}번 앞이 아니다"
                    + " — 선딜 동안 칼이 이미 나가 있다");

                s.Windup.ShouldBe((s.BladeFrame - s.StartFrame) * frame, _halfTick,
                    $"{id} {i + 1}타: 선딜이 끝나는 자리가 칼이 나가는 {s.BladeFrame}번 장의 시작과 다르다"
                    + $" ({s.StartFrame}번부터 셌다)");

                s.Active.ShouldBeGreaterThanOrEqualTo(frame - _halfTick,
                    $"{id} {i + 1}타: 판정이 한 장보다 짧다 — 칼이 지나가는 그림 위에 판정이 못 선다");

                (s.Windup + s.Active).ShouldBeLessThanOrEqualTo(((s.Frames - s.StartFrame) * frame) + _halfTick,
                    $"{id} {i + 1}타: 판정이 그림 밖으로 넘친다");
            }
        }
    }

    [Fact]
    public void 칼의_모양은_그_칼질의_칼_장에서_뽑은_것이다()
    {
        // 칼질의 그림과 칼의 모양이 **같은 장**이어야 보이는 것이 곧 맞는 것이다(설계 §3). 판정 id 는
        // `팩/애니메이션/장` 이라(설계 §3.2) 그 셋이 칼질의 sprite · anim · blade_frame 과 같아야 한다 —
        // 다른 장의 모양을 달면 2타를 휘두르는데 판정은 1타의 궤적인 식으로 조용히 갈린다.
        foreach ((string id, FighterConfig c) in Load())
        {
            foreach (ComboStepDef s in c.Combo)
            {
                s.Hitbox.ShouldBe($"{c.Sprite}/{s.Anim}/{s.BladeFrame}",
                    $"{id}: 칼의 모양이 칼이 지나가는 장에서 온 것이 아니다");
            }
        }
    }

    [Fact]
    public void 칼의_모양이_hitboxes_json_에_있다()
    {
        // 없는 id 는 판을 세울 때 BattleSim 이 거절한다(ArgumentException) — 게임이 첫 전투에서 멈추기 전에 여기서 잡는다.
        Dictionary<string, HitShape> shapes = HitShapeTable.Parse(
            File.ReadAllText(Path.Combine("data", "hitboxes.json")), "hitboxes.json");
        foreach ((string id, FighterConfig c) in Load())
        {
            foreach (ComboStepDef s in c.Combo)
            {
                shapes.ShouldContainKey(s.Hitbox,
                    $"{id}: {s.Hitbox} 가 hitboxes.json 에 없다 — tools/extract_hitboxes.py 의 목록에 넣고 다시 뽑는다");
            }
        }
    }

    [Fact]
    public void 탭은_거의_즉발이다()
    {
        // **유저가 직접 해 보고 낸 요청이다** (이슈 #54): "공격하면 거의 바로 공격이 되게".
        // 선딜 0.3333(20틱)은 그림에서 거꾸로 정한 값이었는데(칼을 뒤로 빼는 네 장) 손에는 굼떴다.
        // 이슈가 제안한 것은 0.08초 — 60Hz 로 **5틱**이다. 그 틱 수를 못박는다.
        //
        // 산수로 재지 않고 **규칙을 돌려서** 센다. 경계는 틱 누산의 부동소수에 걸리므로
        // (TestConfigs.UntilNear 의 주석) 숫자만 보고 "0.0833 이니 5틱" 이라 적으면 실제로는 6틱일 수 있다.
        // 누른 틱이 1틱째다 — Begin 이 Advance 보다 먼저라 누른 틱도 선딜에 들어간다.
        foreach ((string id, FighterConfig c) in Load())
        {
            var fighter = new Fighter(c, TestConfigs.Arena(), TestConfigs.Arena().Width / 2);
            fighter.Tick(new InputFrame(0, false, false, false, Attack: true), BattleSim.Dt);
            int ticks = 1;
            while (!fighter.AttackActive && ticks < 60)
            {
                fighter.Tick(default, BattleSim.Dt);
                ticks++;
            }

            fighter.AttackActive.ShouldBeTrue($"{id}: 탭한 칼이 1초 안에 한 번도 안 섰다");
            ticks.ShouldBeLessThanOrEqualTo(5, $"{id}: 탭한 칼이 {ticks}틱 만에 선다 — 즉발이 아니다");
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
