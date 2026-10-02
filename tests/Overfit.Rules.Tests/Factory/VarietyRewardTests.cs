using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Factory;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>
/// 공격을 고루 쓰는 보상 (이슈 #167) — 유저: "어떤 페이즈든 공격 자체는 전체 다 써야". sp-1 의 보스는 3연격 · 엇박 3연격을 거의 안 썼다(빠른 3연격에
/// 밀린다). 한 판에서 어느 공격을 처음 쓰는 결정에 w_variety / 명부 길이를 더한다 — 일곱을 다 쓰면 판마다 w_variety 다.
/// </summary>
public class VarietyRewardTests
{
    private static readonly Lazy<FactoryTables> _tables = new(() => FactoryTables.Load("data", "fleet.json"));

    private static readonly RewardDef _plain = new() { WDealt = 1, WTaken = 1, WTime = 0.002, WWin = 1 };

    private static readonly RewardDef _variety = new() { WDealt = 1, WTaken = 1, WTime = 0.002, WWin = 1, WVariety = 0.7 };

    [Fact]
    public void 처음_쓰는_공격의_결정에만_몫이_붙는다()
    {
        FactoryTables t = _tables.Value;
        int moves = t.Roster.Count;
        int first = new BossActions(t.Roster).Count - moves;
        Episode a = RolloutRun.Play(t, null, _plain, seed: 3, episode: 1);
        Episode b = RolloutRun.Play(t, null, _variety, seed: 3, episode: 1);
        b.Steps.Select(s => s.Action).ShouldBe(a.Steps.Select(s => s.Action), "보상이 판을 바꿨다");

        var seen = new HashSet<int>();
        for (int i = 0; i < a.Steps.Count; i++)
        {
            int act = a.Steps[i].Action;
            bool fresh = act >= first && seen.Add(act);
            (b.Steps[i].Reward - a.Steps[i].Reward).ShouldBe(fresh ? 0.7 / moves : 0, 1e-12, $"결정 {i} act={act}");
        }

        seen.Count.ShouldBeGreaterThan(1);
    }
}
