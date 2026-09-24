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
/// 그래서 사람이 손으로 단 태그를 기하와 대조한다.
/// </summary>
public class PatternDataTests
{
    /// <summary>선 몸통의 키. 대공이 "지상은 안전" 이 되려면 판정 바닥이 이것보다 위여야 한다.</summary>
    private const double _standingHeight = 120;

    private static Dictionary<string, PatternDef> Load() =>
        JsonData<PatternDef>.ParseTable(File.ReadAllText(Path.Combine("data", "patterns.json")), "patterns.json");

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
    public void Active_단계는_거리와_높이를_갖는다()
    {
        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline.Where(s => s.Kind == "active"))
            {
                step.Distance.ShouldNotBeNull($"{id}: active 에 distance 가 없다");
                step.Height.ShouldNotBeNull($"{id}: active 에 height 가 없다");
                step.Distance!.Count.ShouldBe(2, $"{id}: distance 는 [최소, 최대] 둘이다");
                step.Height!.Count.ShouldBe(2, $"{id}: height 는 [아래, 위] 둘이다");
                step.Damage.ShouldBeGreaterThan(0, $"{id}: active 인데 피해가 0 이다");
            }
        }
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

    /// <summary>
    /// 이 캐릭터가 실제로 올라가는 높이(px). <b><see cref="Fighter"/> 를 정말 뛰게 해서</b> 잰다.
    ///
    /// <para>
    /// 공식(v²/2g)을 여기 베껴 적지 않는 이유는 그것이 <b>연속</b> 적분이기 때문이다.
    /// <c>Fighter.Fall</c> 은 매 틱 <c>v -= g·dt; y += v·dt</c> 로 이산 적분하고, 옛 단검 수치로
    /// 재 보면 연속은 184.08 · 이산은 176.33 이 나온다 — 7.75px 차이다. 연속값으로 가드를 세우면
    /// 그 차이만큼 가드가 거짓말을 한다. 실제 규칙을 돌리면 어긋날 자리가 없다.
    /// </para>
    /// </summary>
    private static double JumpApex(FighterConfig config)
    {
        var fighter = new Fighter(config, TestConfigs.Arena(), 960);
        fighter.Tick(new InputFrame(0, Jump: true, false, false, false), BattleSim.Dt);

        double apex = fighter.Y;
        while (!fighter.Grounded)
        {
            fighter.Tick(default, BattleSim.Dt);
            apex = Math.Max(apex, fighter.Y);
        }

        return apex;
    }

    [Fact]
    public void Jumpable_태그가_각_캐릭터의_실제_점프_정점과_맞는다()
    {
        // 전에는 하드코딩한 100px 과 견줬다 — fighters.json 과 아무 관계가 없었다.
        // 정점이 판정 상단에 몇 px 차로 붙어 있으면 jump_velocity 를 조금 올리는 것만으로
        // 그 패턴이 실제로 넘을 수 있게 되면서도 태그는 false 인 채고 **아무 테스트도 안 빨개졌다.**
        // 그래서 캐릭터마다 실제 정점을 재서 대조한다.
        //
        // HitResolver 가 높이로 빗나가게 하는 조건은 발밑(Y)이 판정 상단보다 위인 것이다 —
        // 그래서 기준은 "정점 > 모든 active 의 상단" 이다.
        Dictionary<string, double> apexes = TestConfigs.Fighters()
            .ToDictionary(f => f.Key, f => JumpApex(f.Value));

        // 캐릭터가 없으면 아래 foreach 가 공허하게 참이다 — 이 가드가 한 번 그렇게 죽은 적이 있다
        // (안쪽 안전지대 가드가 continue 로만 빠져나가던 것과 같은 종류의 구멍이다).
        apexes.ShouldNotBeEmpty("캐릭터가 하나도 없다 — 이 가드가 아무것도 안 본다");

        foreach ((string id, PatternDef def) in Load())
        {
            List<PatternStep> actives = def.Timeline.Where(s => s.Kind == "active").ToList();
            // active 가 하나도 없으면 Max 가 던지고 All 은 공허하게 참이다. 먼저 못박는다 —
            // 그래야 이 가드가 "태그가 거짓말을 해도 조용히 통과"하는 구멍 없이 기하를 본다.
            actives.ShouldNotBeEmpty($"{id}: active 단계가 없다");
            double top = actives.Max(s => s.Height![1]);

            foreach ((string who, double apex) in apexes)
            {
                (apex > top).ShouldBe(def.Tags.Jumpable,
                    $"{id}: jumpable={def.Tags.Jumpable} 인데 {who}의 점프 정점 {apex:0.00}px 과"
                    + $" 판정 상단 {top}px 이 그 말과 다르다");
            }
        }
    }

    [Fact]
    public void 점프로_넘을_수_있는_판정이_지금은_하나도_없다()
    {
        // **빈 집합 위의 가드는 아무것도 안 보면서 초록이다.** 전에는 여기서 jumpable 패턴들의
        // 판정 상단을 Max 로 뽑아 "정점이 그 1.8배를 넘는가" 를 봤는데, 이슈 #48 이 계열을
        // 내려찍기 하나로 줄이면서 그 집합이 **비었다** — 빈 집합에 Max 는 던지고 All 은 공허하게
        // 참이라, 어느 쪽으로 적어도 "본 적 없는데 통과" 가 된다.
        //
        // 그래서 비었다는 사실 자체를 **직접** 단언한다. 지금의 진실은 "점프로 넘을 판정이 없다" 이고,
        // 그 값이 축 셋(jump_reliance · jump_timing_bias · airborne_at_impact)을 표본 0 으로 만든다
        // (PlayerAxes 의 주석에 적어 뒀다). **여기가 빨개지면 계열이 돌아온 것이다** —
        // 그때 아래 천장 가드 옆에 "넘어야 할 판정을 여유 있게 넘는가"(margin 1.8)를 되살려라.
        Dictionary<string, PatternDef> patterns = Load();
        patterns.ShouldNotBeEmpty("패턴이 하나도 없다 — 이 가드가 아무것도 안 본다");

        patterns.Values.Count(d => d.Tags.Jumpable).ShouldBe(0,
            "점프로 넘을 수 있는 패턴이 생겼다 — 점프 축이 살아났으니 여유 가드(정점 > 상단 × 1.8)를 되살려라");
    }

    [Fact]
    public void 점프_정점이_못_넘을_판정을_안_넘는다()
    {
        // 천장은 계열이 하나가 된 뒤에도 살아 있다. 넘지 말아야 할 판정(내려찍기의 상단 340)을
        // 정점이 넘으면 이 계열 전체가 **점프 한 번으로 공짜**가 되고, 그 순간 jumpable 태그도
        // 거짓말이 된다 — 위 가드가 "없다" 고 말한 그 집합이 실제로는 전부이기 때문이다.
        //
        // ⚠ 이 둘은 **같이 움직인다.** 점프를 먼저 정하고 패턴의 height 를 거기 맞추는 것이
        // 이슈 #28 이 바로잡은 순서다(#27 은 반대로 갇혔다). 한쪽만 고치면 여기서 빨개진다.
        List<double> tops = Load().Values
            .SelectMany(d => d.Timeline.Where(s => s.Kind == "active"))
            .Select(s => s.Height![1])
            .ToList();
        Dictionary<string, double> apexes = TestConfigs.Fighters()
            .ToDictionary(f => f.Key, f => JumpApex(f.Value));

        // 위 가드와 같은 이유로 못박는다 — 두 집합 중 하나라도 비면 아래 foreach 가 공허하게 참이다.
        tops.ShouldNotBeEmpty("판정이 하나도 없다 — 이 가드가 아무것도 안 본다");
        apexes.ShouldNotBeEmpty("캐릭터가 하나도 없다 — 이 가드가 아무것도 안 본다");

        double ceiling = tops.Min();
        foreach ((string who, double apex) in apexes)
        {
            apex.ShouldBeLessThanOrEqualTo(ceiling,
                $"{who}: 정점 {apex:0.00}px 이 못 넘어야 할 판정({ceiling}px)까지 넘는다");
        }
    }

    [Fact]
    public void Anti_air_태그가_판정_바닥과_맞는다()
    {
        // 대공은 지상이 안전하다 — 판정의 아래끝이 땅에서 떠 있어야 한다.
        foreach ((string id, PatternDef def) in Load())
        {
            bool offGround = def.Timeline.Where(s => s.Kind == "active").Any(s => s.Height![0] > _standingHeight);
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
        // 돌고 걸어서, 예고가 말한 자리가 아닌 곳을 친다. 간격이 차면 Begin 이 칼이 아직 살아 있는데 다음 패턴을
        // 세운다. 끝은 러너가 멈추는 시각(PatternDef.Duration — 마지막 단계인 end 의 t)이다.
        //
        // 초로 재도 틱에서 새지 않는다 — t + active_seconds 가 끝과 **같은** 창 110가지(t 11 × 창 10)를 실제로
        // 돌려 보니 끝난 뒤의 틱에 살아 있는 판정이 하나도 없었고, 0.01초만 넘겨도 그중 8가지가 한 틱씩 샜다.
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
    public void Dash_direction_이_안을_허용하면_안쪽에_안전지대가_있다()
    {
        // "in" 은 "보스 쪽으로 파고들면 판정을 빠져나간다" 는 뜻이고, "either" 는 그 안쪽 길이
        // 밖으로 빠지는 길과 **함께** 있다는 뜻이다. 둘 다 참이려면 판정의 **안쪽 끝**이
        // 보스 중심에서 떨어져 있어야 한다 (distance[0] > 0).
        // 문자열 화이트리스트만 보던 때 돌진이 distance[0]=0 인 채로 "in" 을 달고 있었다 —
        // 자기 타임라인에 대해 거짓인 태그였고, 망은 그걸 사실로 배웠을 것이다.
        int checkedPatterns = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            if (def.Tags.DashDirection is not ("in" or "either"))
            {
                continue;
            }

            checkedPatterns++;
            def.Timeline.Where(s => s.Kind == "active")
                .ShouldContain(s => s.Distance![0] > 0,
                    $"{id}: dash_direction={def.Tags.DashDirection} 인데 안쪽에 안전한 틈이 없다 (모든 active 의 distance[0]=0)");
        }

        // 여섯 패턴 전부 "out" 이던 때 이 가드는 매번 continue 로 빠져나가 **한 번도 실행되지 않았다.**
        // 통과하는 가드와 도는 가드를 구별하지 않으면, 나중에 안쪽 안전지대가 사라져도 여전히 초록이다.
        checkedPatterns.ShouldBeGreaterThan(0,
            "안으로 파고들 수 있는 패턴이 하나도 없다 — dash_direction_bias 축이 구조적으로 못 가른다");
    }

    [Fact]
    public void 한_판에서_만나는_변종은_예고가_서로_다르다()
    {
        // 선딜 링은 "뭔가 온다" 까지만 말한다 — **무엇이** 오는지는 안 말한다(이슈 #28).
        // 계열이 하나가 되면서(이슈 #48) 이 문제가 더 날카로워졌다: 아홉이 같은 기술이라
        // 화면이 안 가르면 플레이어에게는 한 공격이다.
        //
        // **화면이 갈라야 하는 것은 한 판에서 만나는 변종들이다.** 한 판은 한 단계의 명부에서만
        // 뽑는다(StageRoster) — 2단계 판은 2단계 변종만 굴리므로 `II-끌기` 와 `III-끌기` 를
        // 나란히 보는 일은 없다. 그래서 유일성을 **단계마다** 요구한다.
        //
        // ⚠ **겨눈 자리를 고친 것이지 약하게 만든 것이 아니다** (이슈 #53). 전에는 (모양 + 危) 쌍을
        // **아홉 전부에 걸쳐** 유일하게 요구했다. 그 요구의 목적은 단계를 넘는 같은 변종
        // (II-끌기 ↔ III-끌기)을 危 하나로 가르는 것이었는데, 그 둘은 플레이어가 한 화면에서
        // 견줄 일이 없다 — 틀린 것을 재고 있었다. 정작 중요한 한 판 안의 유일성은 그 요구에
        // **딸려서** 지켜졌을 뿐이다(한 단계 안에서는 危 가 늘 같았다). 마무리가 아홉 전부 가드
        // 불가가 되자 危 는 아무것도 못 가르게 됐고, 옛 요구를 그대로 두면 단계를 넘는 같은 변종에게
        // **다른 그림**을 강요한다 — 같은 기술이라는 것을 그림이 거짓말하게 된다. 그래서 딸려 있던
        // 요구를 본래 목적으로 세우고 틀린 요구는 뺐다. 한 단계 안에서 두 변종이 같은 모양을 쓰면
        // **지금도 빨개진다.**
        //
        // 단계를 넘는 같은 변종은 **같은 그림이어도 된다** — 오히려 그래야 한다: 같은 기술의
        // 윗단계라는 것을 그림이 말한다. 둘이 정말 다른 패턴인지는 그림이 아니라 모양이 본다
        // (셋째_단계_변종은_둘째_단계_짝과_가드_불가_말고도_다르다).
        Dictionary<string, PatternDef> patterns = Load();
        foreach ((string id, PatternDef def) in patterns)
        {
            def.Tell.Id.ShouldNotBeNullOrWhiteSpace($"{id}: tell.id 가 비었다");
            def.Tell.Anim.ShouldNotBeNullOrWhiteSpace($"{id}: tell.anim 이 비었다");
            def.Tell.Length.ShouldBeGreaterThan(0, $"{id}: tell.length 가 0 이면 아무것도 안 그려진다");
            def.Tell.Y.ShouldBeGreaterThanOrEqualTo(0, $"{id}: tell.y 가 바닥 아래다");
        }

        Dictionary<string, StageDef> stages = JsonData<StageDef>.ParseTable(
            File.ReadAllText(Path.Combine("data", "stages.json")), "stages.json");
        int compared = 0;
        foreach ((string stage, StageDef roster) in stages)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string id in roster.Patterns)
            {
                string shape = patterns[id].Tell.Id;
                seen.ShouldNotContainKey(shape,
                    $"{stage}단계: {id} 가 예고 {shape} 를 {seen.GetValueOrDefault(shape)} 와 같이 쓴다"
                    + " — 한 판에서 두 변종이 완전히 같은 그림이다");
                seen[shape] = id;
                compared++;
            }
        }

        // 위 루프는 명부가 비면 공허하게 참이다. 이 가드가 실제로 무언가를 봤는지 못박는다 —
        // 아홉 변종이 전부 어느 한 단계에 있으므로 아홉을 봤어야 한다.
        compared.ShouldBe(patterns.Count, "명부에 없는 변종이 있거나 명부가 비었다 — 이 가드가 다 안 본다");
    }

    [Fact]
    public void 예고_모션은_스윙_수를_말한다()
    {
        // **보스 팩의 모션은 셋뿐이다**(attack · attack2 · attack3). 변종 아홉에 1:1 로 못 붙으므로
        // 모션이 무엇을 말할지를 정해야 하고, 답은 "보스가 몇 번 휘두르는가"(판정 + 헛스윙)다 —
        // 그것이 모션으로 실제로 보이는 유일한 차이이기 때문이다. 변종을 가르는 일은 표지가 한다.
        //
        // 함수여야 하고(같은 스윙 수는 같은 모션) 단사여야 한다(다른 스윙 수는 다른 모션).
        // 한쪽만 보면 "전부 같은 모션" 도 "패턴마다 아무 모션" 도 통과한다.
        var bySwings = new Dictionary<int, string>();
        var byAnim = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string id, PatternDef def) in Load())
        {
            int swings = def.Timeline.Count(s => s.Kind is "active" or "feint");
            swings.ShouldBeGreaterThan(0, $"{id}: 휘두르지 않는 패턴이다");

            if (bySwings.TryGetValue(swings, out string? anim))
            {
                def.Tell.Anim.ShouldBe(anim, $"{id}: 스윙 {swings}번인데 모션이 다른 변종과 갈린다");
            }
            else
            {
                bySwings[swings] = def.Tell.Anim;
            }

            if (byAnim.TryGetValue(def.Tell.Anim, out int already))
            {
                swings.ShouldBe(already, $"{id}: 모션 {def.Tell.Anim} 이 스윙 {already}번과 {swings}번을 같이 쓴다");
            }
            else
            {
                byAnim[def.Tell.Anim] = swings;
            }
        }

        bySwings.Count.ShouldBeGreaterThan(1,
            "스윙 수가 한 가지뿐이라 모션이 아무것도 안 가른다 — 박자가 다른 변종이 계열에 있어야 한다");
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
    public void 가드_불가_태그가_타임라인과_같은_말을_한다()
    {
        // 태그는 망의 입력이고 타임라인은 실제로 일어나는 일이다 — multi_hit · jumpable 과 같은 규약이다.
        // guard_break 는 **판정 단위**다(마무리 한 대에만 붙는다). 태그는 그 요약일 뿐이라
        // 둘이 갈리면 망은 "이 패턴은 가드로 막힌다" 를 거짓으로 배운다.
        int withBreak = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            bool inTimeline = def.Timeline.Any(s => s.Kind == "active" && s.GuardBreak);
            def.Tags.HasGuardBreak.ShouldBe(inTimeline,
                $"{id}: has_guard_break={def.Tags.HasGuardBreak} 인데 타임라인은 {inTimeline} 라고 말한다");
            if (inTimeline)
            {
                withBreak++;
            }

            def.Timeline.Where(s => s.Kind != "active").ShouldNotContain(s => s.GuardBreak,
                $"{id}: active 가 아닌 단계에 guard_break 가 붙었다 — 판정이 없으면 깰 가드도 없다");
        }

        withBreak.ShouldBeGreaterThan(0,
            "가드 불가 판정이 하나도 없다 — 이 가드가 아무것도 안 보고, 가드가 언제나 답인 게임이 된다");
    }

    /// <summary>
    /// 패턴의 <b>가드 불가 깃발을 뺀</b> 모양 — 태그 전부와 타임라인 전부를 한 줄로.
    /// 마무리가 아홉 전부 가드 불가가 된 뒤로(이슈 #53) 그 깃발은 변종을 가르는 데 아무 몫이 없어서,
    /// 두 변종이 "정말 다른가" 는 이것으로 물어야 한다.
    /// </summary>
    private static string ShapeWithoutGuardBreak(PatternDef def)
    {
        PatternTags t = def.Tags;
        string tags = FormattableString.Invariant(
            $"{t.DashWindow}|{t.DashDirection}|{t.Jumpable}|{t.AntiAir}|{t.Parryable}|{t.ParryWindow}|{t.PunishGreed}|{t.Reach}|{t.Feint}|{t.MultiHit}|{t.Tracking}");
        IEnumerable<string> steps = def.Timeline.Select(s => FormattableString.Invariant(
            $"{s.T}:{s.Kind}:{string.Join(',', s.Distance ?? Array.Empty<double>())}:{string.Join(',', s.Height ?? Array.Empty<double>())}:{s.Damage}"));
        return tags + "#" + string.Join(';', steps);
    }

    [Fact]
    public void 셋째_단계_변종은_둘째_단계_짝과_가드_불가_말고도_다르다()
    {
        // **이 자리가 실제로 무너져 있었다** (이슈 #53). 마무리가 아홉 전부 가드 불가가 되자
        // `III-쐐기` 는 `II-쐐기` 와 갈리던 유일한 것을 잃었다 — 두 id 가 한 패턴을 가리키고,
        // "3단계로 올라간다" 는 말이 그 변종에서만 거짓이 됐다. 깃발이 단계를 가르던 동안은 이
        // 붕괴가 안 보였다. 그래서 짝마다 **깃발을 뺀 모양**이 다른지를 본다.
        //
        // 끌기와 쇄도는 원래부터 깃발 말고도 달랐다 — 끌기는 dash_window(0.20 → 0),
        // 쇄도는 마무리 사거리(900 → 1805). 이 테스트가 그것도 같이 확인한다.
        Dictionary<string, PatternDef> all = Load();
        int pairs = 0;
        foreach ((string id, PatternDef three) in all)
        {
            if (!id.Contains(" III-", StringComparison.Ordinal))
            {
                continue;
            }

            string twoId = id.Replace(" III-", " II-", StringComparison.Ordinal);
            if (!all.TryGetValue(twoId, out PatternDef? two))
            {
                continue;
            }

            ShapeWithoutGuardBreak(three).ShouldNotBe(ShapeWithoutGuardBreak(two),
                $"{id}: {twoId} 와 가드 불가 말고는 같다 — 3단계가 이 변종에서 아무것도 안 올린다");
            pairs++;
        }

        pairs.ShouldBe(3, "2단계에 짝이 있는 3단계 변종은 셋(끌기 · 쇄도 · 쐐기)이다 — 짝을 못 찾으면 이 가드가 비어 돈다");
    }

    [Fact]
    public void III_쐐기는_II_쐐기와_피해_숫자만_다르다()
    {
        // 3단계 쐐기의 봉인은 **피해라는 지렛대 하나**로만 선다 (이슈 #53). 피해는 맞았거나 가드로
        // 받았을 때만 판에 들어가므로, 이 지렛대만 당기면 받아치거나 피하는 사람의 판은 그대로다 —
        // WedgeSealTests 가 그 "그대로" 를 실제 판으로 보인다. 그 증명이 서려면 두 변종이 피해 말고는
        // **시각 · 사거리 · 높이 · 태그 · 예고**가 전부 같아야 한다. 하나라도 다르면 피하는 사람의
        // 판도 갈라지고, 그건 봉인이 아니라 그냥 다른 패턴이다.
        Dictionary<string, PatternDef> all = Load();
        PatternDef two = all["내려찍기 II-쐐기"];
        PatternDef three = all["내려찍기 III-쐐기"];

        three.Timeline.Count.ShouldBe(two.Timeline.Count);
        three.Tell.Id.ShouldBe(two.Tell.Id);
        three.Tell.Anim.ShouldBe(two.Tell.Anim);

        string Blind(PatternDef def) => System.Text.RegularExpressions.Regex.Replace(
            ShapeWithoutGuardBreak(def), @":(\d+)(?=;|$)", ":_");
        Blind(three).ShouldBe(Blind(two), "피해 말고 무언가가 다르다 — 피하는 사람의 판까지 갈라진다");

        three.Timeline.Select(s => s.Damage).ShouldNotBe(two.Timeline.Select(s => s.Damage),
            "피해까지 같다 — 3단계 쐐기가 2단계와 같은 패턴이다");
    }

    [Fact]
    public void 모든_마무리는_가드_불가다()
    {
        // **빨강은 한 가지 뜻이다 — 가드로 못 막는다, 받아쳐라** (이슈 #53 · 유저 결정).
        // 마무리 하나하나가 이 깃발을 달아야 한다. 한 변종이라도 빠지면 그 변종의 빨간 마무리는
        // "빨간데 막힌다" 가 되고, 빨강이 두 뜻(막힌다 · 안 막힌다)을 갖게 된다 — 빨강이
        // "패리 불가" 이던 시절에 설계로 없앤 바로 그 색-뜻 충돌이다.
        //
        // 아래 테스트(가드 불가는 언제나 마무리)와 **짝**이다. 둘이 같이 서야 가드 불가 ⟺ 마무리이고,
        // 그래야 뷰가 빨강을 guard_break 로 칠하든 규칙이 상을 마무리에 걸든 같은 한 대를 가리킨다.
        int finishers = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            PatternStep? last = def.Timeline.LastOrDefault(s => s.Kind == "active");
            if (last is null)
            {
                continue;
            }

            last.GuardBreak.ShouldBeTrue($"{id}: 마무리가 가드 불가가 아니다 — 빨간데 막히는 대가 생긴다");
            finishers++;
        }

        finishers.ShouldBeGreaterThan(0, "마무리가 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 가드_불가는_언제나_마무리_한_대다()
    {
        // **빨강과 危 가 겹쳐 뜨는 근거다** (이슈 #53). 화면은 둘을 나눠 말한다:
        // 빨강은 "마무리 — 받아치면 값이 크다", 危 는 "게다가 막을 수조차 없다".
        // 가드 불가가 마무리가 아닌 자리에 붙으면 危 만 뜨고 링은 호박인 장면이 생기고,
        // 그러면 두 표지가 서로 다른 대를 가리켜 둘 다 안 읽힌다.
        //
        // 규칙 층에도 같은 말이 걸려 있다 — 경직은 **마무리**에 걸리므로, 가드 불가가 마무리가
        // 아니면 "가드로 못 막는데 받아쳐도 상이 없는" 답 없는 판정이 된다.
        int checkedHits = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            List<PatternStep> actives = def.Timeline.Where(s => s.Kind == "active").ToList();
            for (int i = 0; i < actives.Count; i++)
            {
                if (!actives[i].GuardBreak)
                {
                    continue;
                }

                i.ShouldBe(actives.Count - 1,
                    $"{id}: {i + 1}번째 판정이 가드 불가인데 마무리가 아니다 — 받아쳐도 상이 없다");
                checkedHits++;
            }
        }

        checkedHits.ShouldBeGreaterThan(0, "가드 불가 판정이 하나도 없다 — 이 가드가 아무것도 안 본다");
    }

    [Fact]
    public void 가드_불가는_패리로_받아칠_수_있다()
    {
        // "가드 불가" 는 **답이 없다**가 아니라 "받아쳐라" 다 — 빨강의 뜻 전체가 그것이다(이슈 #53).
        // 패리도 안 되는 가드 불가는 거리로만 피할 수 있는데, 사거리를 덮는 변종 하나가 그 패턴을
        // 통째로 무답으로 만든다. 마무리가 아홉 전부 가드 불가가 된 뒤로 이 가드는 아홉 전부를 본다.
        foreach ((string id, PatternDef def) in Load())
        {
            if (!def.Tags.HasGuardBreak)
            {
                continue;
            }

            def.Tags.Parryable.ShouldBeTrue($"{id}: 가드 불가인데 패리도 안 된다 — 받아칠 길이 없다");
        }
    }

    [Fact]
    public void 타임라인의_kind_는_정해진_다섯뿐이다()
    {
        // **오타는 조용하다.** PatternRunner 는 모르는 kind 를 그냥 건너뛰므로 "actvie" 라고 적으면
        // 그 판정은 아무 일도 안 하고, 게임은 멀쩡히 돌면서 한 대를 덜 때린다.
        // feint 를 종류로 더한 이슈 #48 에서 이 목록이 처음 필요해졌다 — 종류가 둘일 때는
        // 타임라인 모양만 봐도 알았지만, 이제는 "판정이 없는 단계" 가 정상이라 눈으로 안 갈린다.
        string[] kinds = { "windup", "active", "feint", "recover", "end" };
        foreach ((string id, PatternDef def) in Load())
        {
            foreach (PatternStep step in def.Timeline)
            {
                kinds.ShouldContain(step.Kind, $"{id}: 모르는 kind={step.Kind} — 조용히 무시된다");
            }
        }
    }

    [Fact]
    public void 헛스윙이_있으면_feint_태그가_참이다()
    {
        // 태그는 망의 입력이고 타임라인은 실제로 일어나는 일이다 — multi_hit · has_guard_break 와
        // 같은 규약이다. **한쪽 방향만 본다**: feint 태그는 "판정 없는 헛스윙" 보다 넓은 뜻이라
        // (II-끌기 는 헛스윙 없이 3타를 밀어 속인다) 타임라인에서 되짚을 수 없다.
        // 되짚을 수 있는 쪽만 못박는다 — 헛스윙 단계가 있는데 태그가 거짓이면 그건 그냥 거짓이다.
        int withFeint = 0;
        foreach ((string id, PatternDef def) in Load())
        {
            if (!def.Timeline.Any(s => s.Kind == "feint"))
            {
                continue;
            }

            withFeint++;
            def.Tags.Feint.ShouldBeTrue($"{id}: 타임라인에 헛스윙이 있는데 feint 태그가 거짓이다");
        }

        withFeint.ShouldBeGreaterThan(0,
            "헛스윙이 든 패턴이 하나도 없다 — feint 종류가 데이터에서 한 번도 안 도는 채로 초록이 된다");
    }
}
