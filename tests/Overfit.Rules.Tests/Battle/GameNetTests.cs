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

    /// <summary>
    /// DetMath.Exp 의 상수와 연산 순서는 <b>계약이다</b>(Det 와 같다 · CLAUDE.md §4) — 망 보스의 뽑기가 이 비트에 달렸다. 위의 1e-14 는 호너의 묶음을 바꿔도
    /// 초록이라 옛 시도의 되살리기가 같은 지문으로 다른 판이 된다(최종 리뷰). 값은 파이썬으로 알고리즘을 <b>따로 구현해</b> 얻었다(round = 짝수로 · ldexp).
    /// ln2/2 는 x·(1/ln2) 가 정확히 0.5 라 짝수 반올림(k = 0)을 본다.
    /// </summary>
    [Theory]
    [InlineData(-0.5, 0x3FE368B2FC6F960AUL)]
    [InlineData(-1.0, 0x3FD78B56362CEF38UL)]
    [InlineData(-3.7, 0x3F99511FC6871044UL)]
    [InlineData(-20.0, 0x3E21B48655F37267UL)]
    [InlineData(-100.25, 0x36E4ACD31E167EBBUL)]
    [InlineData(-700.0, 0x00D14F2B0FB9307FUL)]
    [InlineData(2.5, 0x40285D6FD931E0BBUL)]
    [InlineData(0.34657359027997264, 0x3FF6A09E667F3BCCUL)]
    [InlineData(-0.34657359027997264, 0x3FE6A09E667F3BCCUL)]
    [InlineData(1e-09, 0x3FF000000044B830UL)]
    [InlineData(-12.345678, 0x3ED23D2C7DA10725UL)]
    public void DetMath_Exp_는_따로_구현한_파이썬과_비트까지_같다(double x, ulong bits)
    {
        BitConverter.DoubleToUInt64Bits(DetMath.Exp(x)).ShouldBe(bits, $"x={x:R}");
    }

    [Fact]
    public void DetMath_Exp_의_훑기가_따로_구현한_파이썬과_같다()
    {
        // 위의 몇 점은 호너의 묶음(r·p/n → r·(p/n))이나 항 하나(14 → 13)를 바꿔도 같은 비트라 못 잡는다 — 2 만 점(3 에서 −71 까지)을 훑어 sha256 으로
        // 견준다. 파이썬은 같은 알고리즘을 따로 구현했고 그 두 바꿈에서 다른 값(855b… · 3ec9…)을 낸다.
        var bytes = new List<byte>(20000 * 8);
        for (int i = 0; i < 20000; i++)
        {
            double x = (-i * 0.0037) + 3.0;
            bytes.AddRange(BitConverter.GetBytes(DetMath.Exp(x)));
        }

        Convert.ToHexString(SHA256.HashData([.. bytes])).ToLowerInvariant().ShouldBe("b02b2283317e2083218dd4501e4a3de5469b46cc4c5d603cbf9adfb24ba0bdd5");
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

    /// <summary>판을 그대로 두고 조종기가 고른 칸만 적는다.</summary>
    private sealed class Recording(IBossController inner) : IBossController
    {
        public List<int> Picks { get; } = [];

        public bool ReactsToBombs => inner.ReactsToBombs;

        public bool WantsObservation => inner.WantsObservation;

        public int Decide(BossDecision decision)
        {
            int a = inner.Decide(decision);
            Picks.Add(a);
            return a;
        }
    }

    [Theory]
    [InlineData(null, 51, "6fb4d0dc54d78199", 2103, 600)]
    [InlineData(380, 65, "0099782e20c6a5ed", 2564, 100)]
    public void 시드_51_의_망_보스가_고른_칸들이_박아_둔_값이다(int? startHealth, int picks, string sha16, int ticks, int health)
    {
        // 시작 체력 380 은 3페이즈(400 아래)에서 선다 — 첫 줄은 1페이즈에서 서 문턱 600 에 닿고 끝난다. 값은 이슈 #167 의 데이터(체력 800 ·
        // 문턱 600 · 400 · 후딜 +0.3초)와 셀프 플레이 sp-2 의 망(r11 · r12 · r20)으로 적었다 — 망을 다시 배우면 다시 적는다.
        // 위의 "같은 시드면 같은 판" 은 한 기기의 두 번을 견줄 뿐이다 — 뽑기 · exp · 순전파의 어느 하나가 바뀌어도 둘 다 같이 바뀌어 초록이다(최종 리뷰).
        // 고른 칸의 줄을 박아 둔다: 바뀌면 옛 시도의 되살리기가 같은 지문으로 다른 판이다. 일부러 바꿨으면(망 · 관측 · 뽑기) 이 값을 새로 적고 까닭을 남긴다.
        var rec = new Recording(new FormNetController([Load(1), Load(2), Load(3)], new BossActions(Roster), seed: 51));
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter],
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(),
            PatternIds = Roster,
            Patterns = TestConfigs.Patterns(),
            Seed = 51,
            Controller = rec,
            MaxTicks = TestConfigs.MaxTicks(),
            BossStartHealth = startHealth,
        });
        var bot = new BotPolicy(51);
        BattleOutcome? o = null;
        while (o is null)
        {
            o = sim.Tick(bot.Next(sim));
        }

        string sha = Convert.ToHexString(SHA256.HashData(rec.Picks.Select(p => (byte)p).ToArray())).ToLowerInvariant()[..16];
        (rec.Picks.Count, sha, sim.Ticks, sim.Boss.Health).ShouldBe((picks, sha16, ticks, health));
    }

    [Fact]
    public void 데이터_지문은_보스들이_읽는_망을_빠짐없이_든다()
    {
        // DataDigest.Files 는 손으로 적은 목록이다(무엇이 판을 세우나의 선언) — bosses.json 의 forms.nets 와 갈리면 망을 바꾼 옛 시도가 [W] 대신
        // 결정론 위반([E])으로 읽히거나, 지운 망을 지문이 읽다 부팅이 죽는다(최종 리뷰). 둘이 같아야 한다.
        string[] nets = [.. TestConfigs.Bosses().Values.SelectMany(b => b.Forms.Nets ?? []).Distinct().Order(StringComparer.Ordinal)];
        nets.ShouldNotBeEmpty();
        Overfit.Core.DataDigest.Files.Where(f => f.StartsWith("boss_net/", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ShouldBe(nets);
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
