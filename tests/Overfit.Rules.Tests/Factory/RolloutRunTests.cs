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

    [Theory]
    [InlineData(RolloutController.Rule)]
    [InlineData(RolloutController.Random)]
    public void 규칙_무작위_조종기의_판은_결정을_안_적고_E_없이_끝까지_간다(RolloutController kind)
    {
        // 설계 2026-10-01 조각5 §3 — 비교의 대조군. 판의 체력 · 틱 · 승패만 쓴다(episodes.csv).
        using var log = new LogCapture();
        Episode e = RolloutRun.Play(Tables, null, _reward, seed: 11, episode: 0, kind);
        e.Steps.ShouldBeEmpty();
        e.Ticks.ShouldBeGreaterThan(0);
        log.Lines.ShouldNotContain(l => l.Contains("][E]", StringComparison.Ordinal));
    }

    [Fact]
    public void 결정의_길이는_다음_결정까지의_틱이고_합이_판의_남은_길이다()
    {
        // 최종 리뷰가 밟았다 — 할인을 결정마다 하면 결정이 길수록(잡기처럼) 끝의 보상이 덜 깎여, 결정 수를 줄이는 쪽으로 기운다. 학습기는 결정의 길이로
        // 할인한다(γ^(틱/12)) — 그 길이를 일꾼이 싣는다.
        Episode e = RolloutRun.Play(Tables, null, _reward, seed: 3, episode: 0);
        e.Steps.Sum(s => s.Span).ShouldBe(e.Ticks - e.Steps[0].Tick + 1);
        // 같은 틱에 결정이 둘일 수 있다(물러서다 벽에 닿으면 그 틱에 다시 묻는다) — 앞의 것은 길이 0 이고 할인도 없다.
        e.Steps.ShouldAllBe(s => s.Span >= 0);
    }

    private static readonly RewardDef _fighterReward = new() { WDealt = 1, WTaken = 1, WTime = 0, WWin = 1 };

    [Fact]
    public void 배우는_쪽이_파이터면_파이터의_결정만_6틱마다_적고_보상은_파이터_쪽이다()
    {
        // 설계 2026-10-01 조각6 §1.3 · §2 — 상대는 안 배운 보스 망(none). 보스 망의 결정은 안 적는다.
        var matchup = new Matchup(RolloutSide.Fighter, new BossSpec(RolloutController.Net, null), new FighterSpec(FighterKind.Net, null));
        Episode e = RolloutRun.Play(Tables, matchup, _reward, _fighterReward, seed: 4, episode: 1);
        e.Steps.Select(s => s.Tick).ShouldBe(Enumerable.Range(0, e.Steps.Count).Select(k => k * 6));
        e.Steps.ShouldAllBe(s => s.Observation.Count == new FighterObservation(Tables.Roster.Count).Size && s.Mask.Count == FighterActions.Count);
        bool fighterWon = e.Outcome == BattleOutcome.Win;
        double expected = (_fighterReward.WDealt * e.BossLost / Tables.Boss.MaxHealth) - (_fighterReward.WTaken * e.FighterLost / Tables.Fighter.MaxHealth)
            + (fighterWon ? 1 : -1);
        e.Steps.Sum(s => s.Reward).ShouldBe(expected, 1e-9);
        e.Steps.Sum(s => s.Span).ShouldBe(e.Ticks);
    }

    [Fact]
    public void 상대는_시드와_판으로_고르고_고른_칸을_싣는다()
    {
        int a = RolloutRun.Opponent(seed: 5, episode: 3, count: 4);
        a.ShouldBe(RolloutRun.Opponent(5, 3, 4));
        Enumerable.Range(0, 64).Select(ep => RolloutRun.Opponent(5, ep, 4)).Distinct().Count().ShouldBe(4, "상대 넷을 다 안 고른다");
    }

    [Fact]
    public void 파이터가_배우는_여러_상대의_판도_스레드_수와_무관하게_같은_바이트이고_상대_칸을_싣는다()
    {
        // 최종 리뷰가 밟았다 — 스레드 무관 테스트가 옛 한 짝 오버로드만 돌았다. 상대 셋(규칙 · 무작위 · 안 배운 망) 중 판마다 하나.
        Matchup[] matchups =
        [
            new(RolloutSide.Fighter, new BossSpec(RolloutController.Rule, null), new FighterSpec(FighterKind.Net, null)),
            new(RolloutSide.Fighter, new BossSpec(RolloutController.Random, null), new FighterSpec(FighterKind.Net, null)),
            new(RolloutSide.Fighter, new BossSpec(RolloutController.Net, null), new FighterSpec(FighterKind.Net, null)),
        ];
        string one = Path.Combine(Path.GetTempPath(), $"rollout-{Guid.NewGuid():N}");
        string four = Path.Combine(Path.GetTempPath(), $"rollout-{Guid.NewGuid():N}");
        try
        {
            RolloutWriter.Run(Tables, matchups, _reward, _fighterReward, seed: 6, episodes: 6, threads: 1, outDir: one);
            RolloutWriter.Run(Tables, matchups, _reward, _fighterReward, seed: 6, episodes: 6, threads: 4, outDir: four);
            File.ReadAllBytes(Path.Combine(one, "steps.bin")).ShouldBe(File.ReadAllBytes(Path.Combine(four, "steps.bin")));
            string[] rows = File.ReadAllLines(Path.Combine(one, "episodes.csv"));
            rows[0].ShouldStartWith("episode,opponent,");
            rows.Skip(1).Select(r => int.Parse(r.Split(',')[1], System.Globalization.CultureInfo.InvariantCulture))
                .ShouldBe(Enumerable.Range(0, 6).Select(e => RolloutRun.Opponent(6, e, 3)));
        }
        finally
        {
            Directory.Delete(one, true);
            Directory.Delete(four, true);
        }
    }

    [Fact]
    public void 보스가_파이터_망과_싸워도_보스의_결정만_적는다()
    {
        var matchup = new Matchup(RolloutSide.Boss, new BossSpec(RolloutController.Net, null), new FighterSpec(FighterKind.Net, null));
        Episode e = RolloutRun.Play(Tables, matchup, _reward, _fighterReward, seed: 8, episode: 0);
        e.Steps.ShouldNotBeEmpty();
        e.Steps.ShouldAllBe(s => s.Observation.Count == new BossObservation(Tables.Roster.Count).Size && s.Mask.Count == new BossActions(Tables.Roster).Count);
        e.Habit.ShouldBe("net");
    }

    [Fact]
    public void 학습_설정에_파이터_보상이_없으면_읽을_때_멈춘다()
    {
        // 최종 리뷰가 밟았다 — 없으면 보스의 보상(시간 벌 0.002)으로 조용히 대신했다. 스펙 §1.3 은 파이터에게 시간 벌이 없다.
        const string json = """{ "reward": { "w_dealt": 1, "w_taken": 1, "w_time": 0.002, "w_win": 1 } }""";
        Should.Throw<Overfit.Core.DataException>(() => Overfit.Core.JsonData<TrainConfig>.ParseOne(json, "시험"));
    }
}
