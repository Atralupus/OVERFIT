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

    [Fact]
    public void 보스전은_망_조종기로_서고_대본_판은_규칙이다()
    {
        // 설계 2026-10-01 조각7 §2 — stages.json 의 controller. 대본(GIF · 스크린샷)으로 선 판은 계획 대본의 규칙 조종기다.
        StageSetup setup = StageRoster.Setup(TestConfigs.Stages(), 1, 7, [], TestConfigs.Patterns(), BattleSim.RestTicks(TestConfigs.Boss()),
            TestConfigs.Balance().Picker).ShouldNotBeNull();
        (setup.ControllerId, setup.PickerId).ShouldBe(("net", "net"));
        StageSetup scripted = StageRoster.Setup(TestConfigs.Stages(), 1, 7, [], TestConfigs.Patterns(), BattleSim.RestTicks(TestConfigs.Boss()),
            TestConfigs.Balance().Picker, [new ScriptPlan(0.8, "3연격")]).ShouldNotBeNull();
        (scripted.ControllerId, scripted.PickerId).ShouldBe(("rule", "script"));
    }

    [Fact]
    public void 보스의_망_목록으로_형태_조종기를_세운다()
    {
        BossConfig boss = TestConfigs.Boss();
        boss.Forms.Nets.ShouldNotBeNull().Count.ShouldBe(boss.Forms.Thresholds.Count + 1, "형태마다 망 하나가 아니다");
        IBossController c = BossNets.Create(
            boss.Forms.Nets!.Select(f => File.ReadAllText(Path.Combine("data", f))).ToArray(), boss.Forms.Nets!, Roster, seed: 3);
        c.ShouldBeOfType<FormNetController>();
    }

    [Fact]
    public void 망_보스는_폭탄을_알아챔_표시를_안_띄운다()
    {
        // 설계 2026-10-01 조각7 §6 — 반응 장치를 안 켜는 조종기면 "!" 가 끊는 보스를 약속하는 거짓말이 된다.
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter],
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = Roster,
            Patterns = TestConfigs.Patterns(),
            Seed = 51,
            Controller = new RandomController(1),
            MaxTicks = TestConfigs.MaxTicks(),
        });
        sim.Tick(new InputFrame(0, false, false, false, false, Bomb: true));
        TestConfigs.UntilTick(sim, 40);
        sim.Fighter.Throwing.ShouldBeTrue();
        sim.BossAlert.ShouldBeFalse();
    }
}
