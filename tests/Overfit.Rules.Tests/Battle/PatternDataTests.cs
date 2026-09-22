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
    public void 점프_정점이_넘을_판정과_못_넘을_판정_사이에_있다()
    {
        // jumpable 가드는 참거짓만 본다 — 정점이 판정 상단보다 1px 높아도 초록이다.
        // 요구는 **여유**다: 낮은 공격(이단 올려베기 150)을 확실히 넘되 높은 판정
        // (내려찍기 3연 340 · 점프 강타의 착지 충격 330)은 못 넘어야 "점프로 피할 수 있는가" 가 축이 된다.
        //
        // ⚠ 이 둘은 **같이 움직인다.** 이슈 #27 에서는 패턴 기하가 먼저 서 있어서 정점이 176 에
        // 갇혔고, 이슈 #28 이 패턴을 통째로 갈아엎으며 순서를 뒤집었다 — 점프를 먼저 정하고
        // 세 패턴의 height 를 거기 맞췄다. 한쪽만 고치면 여기서 빨개진다.
        const double margin = 1.8;
        Dictionary<string, PatternDef> patterns = Load();
        Dictionary<string, double> apexes = TestConfigs.Fighters()
            .ToDictionary(f => f.Key, f => JumpApex(f.Value));

        // 위 가드와 같은 이유로 못박는다 — 캐릭터 표가 비면 아래 foreach 가 공허하게 참이다.
        // 캐릭터를 셋에서 하나로 줄이면서(이슈 #38) 이 집합이 실제로 작아졌다.
        apexes.ShouldNotBeEmpty("캐릭터가 하나도 없다 — 이 가드가 아무것도 안 본다");

        double clearable = patterns.Values.Where(d => d.Tags.Jumpable)
            .SelectMany(d => d.Timeline.Where(s => s.Kind == "active"))
            .Max(s => s.Height![1]);
        double ceiling = patterns.Values.Where(d => !d.Tags.Jumpable)
            .SelectMany(d => d.Timeline.Where(s => s.Kind == "active"))
            .Min(s => s.Height![1]);

        foreach ((string who, double apex) in apexes)
        {
            apex.ShouldBeGreaterThan(clearable * margin,
                $"{who}: 정점 {apex:0.00}px 이 넘어야 할 판정({clearable}px)을 겨우 넘는다");
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
    public void 패턴마다_자기_예고가_있고_서로_다르다()
    {
        // 선딜 링은 "뭔가 온다" 까지만 말한다 — **무엇이** 오는지는 안 말한다(이슈 #28).
        // 백장의 두 패턴은 "칼이 땅에 있나 떠 있나" 로만 갈리므로, 예고가 같으면 그 둘은
        // 화면에서 같은 공격이다. 그래서 id(모양) · anim(보스 모션) 둘 다 패턴마다 달라야 한다.
        var shapes = new Dictionary<string, string>(StringComparer.Ordinal);
        var anims = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string id, PatternDef def) in Load())
        {
            def.Tell.Id.ShouldNotBeNullOrWhiteSpace($"{id}: tell.id 가 비었다");
            def.Tell.Anim.ShouldNotBeNullOrWhiteSpace($"{id}: tell.anim 이 비었다");
            def.Tell.Length.ShouldBeGreaterThan(0, $"{id}: tell.length 가 0 이면 아무것도 안 그려진다");
            def.Tell.Y.ShouldBeGreaterThanOrEqualTo(0, $"{id}: tell.y 가 바닥 아래다");

            shapes.ShouldNotContainKey(def.Tell.Id,
                $"{id}: 예고 모양 {def.Tell.Id} 를 {shapes.GetValueOrDefault(def.Tell.Id)} 와 같이 쓴다 — 화면에서 두 패턴이 같아진다");
            shapes[def.Tell.Id] = id;

            anims.ShouldNotContainKey(def.Tell.Anim,
                $"{id}: 보스 모션 {def.Tell.Anim} 를 {anims.GetValueOrDefault(def.Tell.Anim)} 와 같이 쓴다 — 선딜 모션으로 종류가 안 갈린다");
            anims[def.Tell.Anim] = id;
        }

        // 위 두 루프는 패턴이 없으면 공허하게 참이다. 이 가드가 실제로 무언가를 봤는지 못박는다.
        shapes.ShouldNotBeEmpty("패턴이 하나도 없다 — 이 가드가 아무것도 안 본다");
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
}
