using System;
using System.IO;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Factory;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>학습의 일꾼 (설계 2026-10-01 조각4 §5 · §6) — 판 하나를 망 조종기로 돌려 결정마다 보상을 붙인다.</summary>
public class RolloutRunTests
{
    private static readonly Lazy<FactoryTables> _tables = new(() => FactoryTables.Load("data", "fleet.json"));

    private static FactoryTables Tables => _tables.Value;

    private static readonly RewardDef _reward = new() { WDealt = 1, WTaken = 1, WTime = 0.002, WWin = 1 };

    [Fact]
    public void 보상의_합은_판의_체력_변화와_시간과_끝에서_나온다()
    {
        Episode e = RolloutRun.Play(Tables, null, _reward, seed: 3, episode: 0);
        e.Steps.ShouldNotBeEmpty();
        double expected = (_reward.WDealt * e.FighterLost / Tables.Fighter.MaxHealth)
            - (_reward.WTaken * e.BossLost / Tables.Boss.MaxHealth)
            - (_reward.WTime * (e.Ticks - e.Steps[0].Tick + 1) / 60.0)
            + (e.BossWon ? _reward.WWin : -_reward.WWin);
        e.Steps.Sum(s => s.Reward).ShouldBe(expected, 1e-9);
        e.Steps.Count(s => s.Done).ShouldBe(1);
        e.Steps[^1].Done.ShouldBeTrue();
    }

    [Fact]
    public void 보스가_이긴_것은_파이터가_쓰러진_판이다()
    {
        Episode e = RolloutRun.Play(Tables, null, _reward, seed: 3, episode: 0);
        e.BossWon.ShouldBe(e.Outcome == BattleOutcome.Lose && e.FighterLost >= Tables.Fighter.MaxHealth);
    }

    [Fact]
    public void 같은_시드_같은_판이면_같은_경험이다()
    {
        Episode a = RolloutRun.Play(Tables, null, _reward, seed: 5, episode: 2);
        Episode b = RolloutRun.Play(Tables, null, _reward, seed: 5, episode: 2);
        a.Steps.Select(s => (s.Tick, s.Action, s.Reward)).ShouldBe(b.Steps.Select(s => (s.Tick, s.Action, s.Reward)));
        RolloutRun.Play(Tables, null, _reward, seed: 5, episode: 3).Steps.Select(s => s.Action)
            .ShouldNotBe(a.Steps.Select(s => s.Action), "판이 달라도 같은 경험이다");
    }

    [Fact]
    public void 판은_E_없이_끝까지_가고_결정은_다_열린_칸이다()
    {
        using var log = new LogCapture();
        Episode e = RolloutRun.Play(Tables, null, _reward, seed: 1, episode: 7);
        log.Lines.ShouldNotContain(l => l.Contains("][E]", StringComparison.Ordinal));
        e.Steps.ShouldAllBe(s => s.Mask[s.Action]);
        e.Steps.ShouldAllBe(s => s.Observation.Count == new BossObservation(Tables.Roster.Count).Size);
    }

    [Fact]
    public void 스레드_수와_무관하게_같은_바이트를_쓴다()
    {
        string one = Path.Combine(Path.GetTempPath(), $"rollout-{Guid.NewGuid():N}");
        string four = Path.Combine(Path.GetTempPath(), $"rollout-{Guid.NewGuid():N}");
        try
        {
            RolloutWriter.Run(Tables, null, _reward, seed: 9, episodes: 6, threads: 1, outDir: one);
            RolloutWriter.Run(Tables, null, _reward, seed: 9, episodes: 6, threads: 4, outDir: four);
            File.ReadAllBytes(Path.Combine(one, "steps.bin")).ShouldBe(File.ReadAllBytes(Path.Combine(four, "steps.bin")));
            File.ReadAllText(Path.Combine(one, "episodes.csv")).ShouldBe(File.ReadAllText(Path.Combine(four, "episodes.csv")));
        }
        finally
        {
            Directory.Delete(one, true);
            Directory.Delete(four, true);
        }
    }

    [Theory]
    [InlineData(BattleOutcome.Lose, false, true)]
    [InlineData(BattleOutcome.Lose, true, false)]
    [InlineData(BattleOutcome.Win, false, false)]
    [InlineData(BattleOutcome.Win, true, false)]
    public void 보스의_승리는_판의_결과가_짐이고_파이터가_쓰러진_것이다(BattleOutcome outcome, bool fighterAlive, bool bossWon)
    {
        // 최종 리뷰가 밟았다 — 같은 틱에 둘 다 쓰러지면 게임은 파이터의 승리다(BattleSim.Outcome 이 보스의 죽음을 먼저 본다). "파이터가 쓰러졌다" 만 보면
        // 그 판을 보스의 승리로 쳐 가장 큰 보상의 부호가 뒤집힌다. 시간 초과(짐 · 파이터가 살아 있다)는 보스의 짐이다.
        RolloutRun.BossWon(outcome, fighterAlive).ShouldBe(bossWon);
    }
}
