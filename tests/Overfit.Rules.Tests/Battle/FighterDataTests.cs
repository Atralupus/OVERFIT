using System;
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
            c.ParryPreciseWindow.ShouldBeLessThan(c.ParryDuration, $"{id}: 패리 실패가 안 비싸면 의존도가 축이 안 된다");
        }
    }

    [Fact]
    public void 패리_창_셋이_연타_정확_부정확_순으로_선다()
    {
        // 세 창의 **순서가 곧 규칙**이다 (이슈 #27).
        // 연타 창이 정확 창보다 넓으면 난사가 벌이 아니라 상이 되고,
        // 부정확 창이 정확 창보다 좁으면 "늦게 눌렀다" 가 다시 "아무것도 안 했다" 와 같은 점이 된다.
        // 부정확 창은 패리 **행동**보다도 길어야 한다 — 행동이 끝난 뒤가 바로 그 늦은 자리다.
        foreach ((string id, FighterConfig c) in Load())
        {
            c.ParrySpamWindow.ShouldBeLessThan(c.ParryPreciseWindow, $"{id}: 연타 징벌이 창을 안 좁힌다");
            c.ParryPreciseWindow.ShouldBeLessThan(c.ParryImpreciseWindow, $"{id}: 정확 창이 부정확 창보다 넓다");
            c.ParryDuration.ShouldBeLessThan(c.ParryImpreciseWindow, $"{id}: 부정확 창이 패리 행동 안에서 끝난다");
            c.ParryInternalRatio.ShouldBeInRange(0, 1, $"{id}: 내상 비율이 0~1 이 아니다");
            c.ParryLock.ShouldBeGreaterThan(0, $"{id}: 부정확 패리에 고정이 없다");
        }
    }

    /// <summary>
    /// 대시 한 번이 실제로 가는 거리(px). <b>속도 × 지속을 곱해 적지 않는다</b> —
    /// 행동이 끝나는 틱에는 <c>Move</c> 가 이미 Idle 을 보므로 실제 이동 틱은 하나 적고,
    /// 그 한 틱(중검 기준 36.7px)이 "보스를 지나는가" 의 경계에 그대로 걸린다.
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
        // 즉 보스 반폭 + 파이터 반폭의 두 배다 (보스 반폭 85 · 중검 30 → 230px).
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
        // 셋이 비슷해야 캐릭터를 바꿔도 대시 타이밍 축이 같은 것을 재는 값이 된다.
        foreach ((string id, FighterConfig c) in Load())
        {
            double share = c.DashIFrames / c.DashDuration;
            share.ShouldBeInRange(0.70, 0.85, $"{id}: 무적 비율 {share:0.00} 이 다른 캐릭터와 다른 기술이 됐다");
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
