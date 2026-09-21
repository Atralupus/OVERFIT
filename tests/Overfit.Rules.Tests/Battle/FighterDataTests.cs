using System.Collections.Generic;
using System.IO;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class FighterDataTests
{
    private static Dictionary<string, FighterConfig> Load() =>
        JsonData<FighterConfig>.ParseTable(File.ReadAllText(Path.Combine("data", "fighters.json")), "fighters.json");

    [Fact]
    public void 캐릭터가_셋이다()
    {
        Load().Count.ShouldBe(3);
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
    public void 패리_창은_패리_지속보다_짧다()
    {
        foreach ((string id, FighterConfig c) in Load())
        {
            c.ParryWindow.ShouldBeLessThan(c.ParryDuration, $"{id}: 패리 실패가 안 비싸면 의존도가 축이 안 된다");
        }
    }

    [Fact]
    public void 사거리와_속도가_반대로_간다()
    {
        // 빠른 것은 짧고 느린 것은 길다. 셋이 같은 값이면 캐릭터 선택이 무의미하다.
        Dictionary<string, FighterConfig> all = Load();
        var byReach = new List<FighterConfig>(all.Values);
        byReach.Sort((a, b) => a.AttackReach.CompareTo(b.AttackReach));

        // 사거리가 짧을수록 공격 사이클이 빨라야 한다
        double CycleOf(FighterConfig c) => c.AttackWindup + c.AttackActive + c.AttackRecover;
        CycleOf(byReach[0]).ShouldBeLessThan(CycleOf(byReach[1]));
        CycleOf(byReach[1]).ShouldBeLessThan(CycleOf(byReach[2]));
    }
}
