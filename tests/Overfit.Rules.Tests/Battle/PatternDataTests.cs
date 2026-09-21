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
    /// <summary>점프로 넘을 수 있다고 볼 높이. 캐릭터 키(120)보다 낮아야 한다.</summary>
    private const double _lowEnough = 100;

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

    [Fact]
    public void Jumpable_태그가_판정_높이와_맞는다()
    {
        // 점프로 넘으려면 판정의 위끝이 낮아야 한다.
        // active 가 하나도 없으면 All() 이 공허하게 true 를 주므로, 그 전에 최소 하나는 있어야 한다 —
        // 그래야 이 가드가 "태그가 거짓말을 해도 조용히 통과"하는 구멍 없이 실제로 기하를 본다.
        foreach ((string id, PatternDef def) in Load())
        {
            List<PatternStep> actives = def.Timeline.Where(s => s.Kind == "active").ToList();
            bool allLow = actives.Count > 0 && actives.All(s => s.Height![1] <= _lowEnough);
            def.Tags.Jumpable.ShouldBe(allLow, $"{id}: jumpable={def.Tags.Jumpable} 인데 판정 높이가 맞지 않는다");
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
    public void 패리_불가면_패리_창이_0_이다()
    {
        foreach ((string id, PatternDef def) in Load())
        {
            if (!def.Tags.Parryable)
            {
                def.Tags.ParryWindow.ShouldBe(0, $"{id}: 패리 불가인데 parry_window 가 있다");
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
