using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>게임 안의 망 (설계 2026-10-01 조각7) — exp 없는 뽑기 · 형태마다의 망 · 파이썬과 비트 일치.</summary>
public class GameNetTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1e-300)]
    [InlineData(-0.5)]
    [InlineData(-1.0)]
    [InlineData(-3.7)]
    [InlineData(-20.0)]
    [InlineData(-100.25)]
    [InlineData(-700.0)]
    [InlineData(2.5)]
    public void DetMath_Exp_는_Math_Exp_와_상대_오차_1e_14_안이다(double x)
    {
        double want = Math.Exp(x);
        Math.Abs(DetMath.Exp(x) - want).ShouldBeLessThanOrEqualTo(want * 1e-14);
    }

    [Fact]
    public void DetMath_Exp_는_0_에서_1_이고_아주_작으면_0_이다()
    {
        DetMath.Exp(0).ShouldBe(1.0);
        DetMath.Exp(-800).ShouldBe(0.0);
        DetMath.Exp(double.NegativeInfinity).ShouldBe(0.0);
    }

    private static IReadOnlyList<string> Roster => StageRoster.For(TestConfigs.Stages(), 1);

    private static PolicyNet Load(int form) => PolicyNet.Parse(
        File.ReadAllText(Path.Combine("data", "boss_net", $"form{form}.json")), $"form{form}",
        new BossObservation(Roster.Count).Size, new BossActions(Roster).Count, Roster);

    [Fact]
    public void 세_망의_로짓과_가치가_파이썬과_비트까지_같다()
    {
        // ml/rl/golden.py 가 순수 파이썬(덧셈 순서를 PolicyNet 과 같게)으로 셈한 값 — 망을 바꾸고 골든을 안 지으면 sha256 이 먼저 잡는다.
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine("Battle", "NetGolden.json")));
        foreach (JsonElement entry in doc.RootElement.GetProperty("nets").EnumerateArray())
        {
            string file = Path.Combine("data", entry.GetProperty("file").GetString()!);
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()
                .ShouldBe(entry.GetProperty("sha256").GetString(), $"{file} 가 골든을 지은 망이 아니다 — tools/build.sh golden");
            int form = int.Parse(Path.GetFileNameWithoutExtension(file)[4..], System.Globalization.CultureInfo.InvariantCulture);
            PolicyNet net = Load(form);
            foreach (JsonElement c in entry.GetProperty("cases").EnumerateArray())
            {
                double[] obs = [.. c.GetProperty("obs").EnumerateArray().Select(e => e.GetDouble())];
                (double[] logits, double value) = net.Forward(obs);
                logits.Select(Hex).ShouldBe(c.GetProperty("logits").EnumerateArray().Select(e => e.GetString()!), $"form{form} 의 로짓");
                Hex(value).ShouldBe(c.GetProperty("value").GetString(), $"form{form} 의 가치");
            }
        }
    }

    private static string Hex(double x) => BitConverter.DoubleToInt64Bits(x).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void 형태_조종기는_형태마다_다른_망으로_고르고_가려진_칸을_안_고른다()
    {
        PolicyNet[] nets = [Load(1), Load(2), Load(3)];
        var c = new FormNetController(nets, new BossActions(Roster), seed: 5);
        (c.WantsObservation, c.ReactsToBombs).ShouldBe((true, false));
        int size = new BossObservation(Roster.Count).Size;
        var rng = new Random(1);
        double[] obs = [.. Enumerable.Range(0, size).Select(_ => rng.NextDouble())];
        bool[] mask = [.. Enumerable.Range(0, new BossActions(Roster).Count).Select(i => i != BossActions.Continue)];
        var picks = new Dictionary<int, HashSet<int>>();
        foreach (int form in new[] { 1, 2, 3 })
        {
            picks[form] = [];
            for (int n = 0; n < 200; n++)
            {
                var sight = new BossSight(0, form, 1440, -1, 1200, new FighterSnapshot(480, 0, 1, FighterAction.Idle, false, 220, 10), []);
                int a = c.Decide(new BossDecision(DecisionPoint.Rest, n, 12, mask, sight, null, null, obs));
                mask[a].ShouldBeTrue();
                picks[form].Add(a);
            }
        }

        // 거의 안 배운 1페이즈는 여러 칸을 쓰고, 망이 다르니 형태마다 쓰는 칸이 같지 않다.
        picks[1].Count.ShouldBeGreaterThan(1);
        picks.Values.Select(s => string.Join(',', s.Order())).Distinct().Count().ShouldBeGreaterThan(1);
    }

    [Fact]
    public void 형태_조종기의_판은_E_없이_끝까지_가고_같은_시드면_같은_판이다()
    {
        PolicyNet[] nets = [Load(1), Load(2), Load(3)];
        (int, int) Run()
        {
            var sim = new BattleSim(new BattleSetup
            {
                Arena = TestConfigs.Arena(),
                Fighter = TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter],
                HitShapes = TestConfigs.HitShapes(),
                Boss = TestConfigs.Boss(),
                PatternIds = Roster,
                Patterns = TestConfigs.Patterns(),
                Seed = 51,
                Controller = new FormNetController(nets, new BossActions(Roster), seed: 51),
                MaxTicks = TestConfigs.MaxTicks(),
            });
            var bot = new BotPolicy(51);
            BattleOutcome? o = null;
            while (o is null)
            {
                o = sim.Tick(bot.Next(sim));
            }

            return (sim.Ticks, sim.Boss.Health);
        }

        using var log = new LogCapture();
        (int, int) a = Run();
        log.Lines.ShouldNotContain(l => l.Contains("][E]", StringComparison.Ordinal));
        Run().ShouldBe(a);
    }
}
