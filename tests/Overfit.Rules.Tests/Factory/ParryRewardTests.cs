using System;
using System.Linq;
using Overfit.Factory;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>
/// 패리 보상 (이슈 #167) — 유저: "파이터가 근데 패리를 성공한적이 없네요". 받아친 틱이 든 파이터의 결정에 w_parry 를 더한다. 받아치면 보스가 1.5초 탈진하지만
/// 그 뒤 칼이 닿아야 보상이 생겨, 0.133초 창을 맞춘 것이 배울 신호로 너무 멀었다.
/// </summary>
public class ParryRewardTests
{
    private static readonly Lazy<FactoryTables> _tables = new(() => FactoryTables.Load("data", "fleet.json"));

    private static readonly RewardDef _plain = new() { WDealt = 1, WTaken = 1, WTime = 0, WWin = 1 };

    private static readonly RewardDef _parry = new() { WDealt = 1, WTaken = 1, WTime = 0, WWin = 1, WParry = 0.3 };

    [Fact]
    public void 받아친_만큼_파이터의_보상이_는다()
    {
        FactoryTables t = _tables.Value;
        var boss = new BossSpec(RolloutController.Rule, null);
        var fighter = new FighterSpec(FighterKind.Net, null);
        var m = new Matchup(RolloutSide.Fighter, boss, fighter);

        // 무작위 파이터가 한 번이라도 받아친 판을 찾는다(무작위라 대개 몇 판 안에 있다).
        for (int e = 0; e < 40; e++)
        {
            Episode a = RolloutRun.Play(t, m, _plain, _plain, seed: 5, episode: e);
            if (a.Parries == 0)
            {
                continue;
            }

            Episode b = RolloutRun.Play(t, m, _plain, _parry, seed: 5, episode: e);
            b.Steps.Select(s => s.Action).ShouldBe(a.Steps.Select(s => s.Action));
            (b.Steps.Sum(s => s.Reward) - a.Steps.Sum(s => s.Reward)).ShouldBe(0.3 * a.Parries, 1e-9);
            b.Steps.Zip(a.Steps).Count(p => Math.Abs(p.First.Reward - p.Second.Reward) > 1e-12).ShouldBeGreaterThan(0);
            return;
        }

        throw new ShouldAssertException("무작위 파이터가 40 판 동안 한 번도 못 받아쳤다");
    }
}
