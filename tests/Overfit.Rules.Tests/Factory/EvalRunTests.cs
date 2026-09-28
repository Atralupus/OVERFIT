using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Overfit.Battle.Rules;
using Overfit.Factory;
using Overfit.Rules.Tests.Battle;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>
/// 같은 봇에게 망 보스와 무작위 보스 (#114 · 설계 2026-09-28 §7.1). 짝지은 비교가 서려면 두 갈래가 같은 1단계 · 같은 시도 번호 · 같은 시드에서 갈라져야
/// 하고, 무작위 갈래는 공장의 2단계 그대로여야 한다 — 그래야 차이가 고르기에서만 나온다. 실제 데이터 · 실제 함대 설정 · 진짜 망으로 돈다.
/// </summary>
public class EvalRunTests
{
    private const ulong _fleetSeed = 51;

    private static readonly Lazy<FactoryTables> _tables = new(() => FactoryTables.Load("data", "fleet.json"));

    private static readonly Lazy<NetworkContext> _network = new(TestConfigs.Network);

    private static FactoryTables Tables => _tables.Value;

    private static EvalResult Eval(int bot, int stage1Tries = 5, int stage2Tries = 5) =>
        EvalRun.Run(_fleetSeed, bot, Tables, _network.Value, stage1Tries, stage2Tries);

    /// <summary>
    /// 봇 0 ~ 63 과 2단계에 가는 봇들(<see cref="FleetBots"/>) 중 <paramref name="wanted"/> 를 만족하는 첫 봇 — 없으면 실패다. 보스 체력 400(#126)에서는
    /// 앞 64 대 안에 1단계를 넘는 봇이 하나뿐이라, 2단계를 보는 조건은 뒤의 봇들이 채운다.
    /// </summary>
    private static (int Bot, EvalResult Result) First(Func<EvalResult, bool> wanted, int stage1Tries = 5)
    {
        foreach (int bot in Enumerable.Range(0, 64).Concat(FleetBots.ReachStage2).Distinct())
        {
            EvalResult result = Eval(bot, stage1Tries);
            if (wanted(result))
            {
                return (bot, result);
            }
        }

        throw new InvalidOperationException("봇 0 ~ 63 과 FleetBots.ReachStage2 안에 찾는 봇이 없다");
    }

    private static string Csv(EvalResult result)
    {
        var text = new StringBuilder();
        EvalCsv.AppendAttempts(text, result);
        EvalCsv.AppendSamples(text, result);
        EvalCsv.AppendBot(text, result);
        return text.ToString();
    }

    [Fact]
    public void 무작위_갈래는_공장의_2단계와_같은_판이다()
    {
        // 같은 시드 · 같은 UniformPicker · 같은 봇 — 공장의 원본이 곧 무작위 갈래다. 둘째 시도까지 본다: 앞 시도의 기록이 붙어도 무작위는 안 흔들린다.
        (int bot, EvalResult eval) = First(r => r.Uniform.Attempts.Count >= 2);
        BotResult factory = BotRun.Run(_fleetSeed, bot, Tables, 5, 5);

        eval.Stage1Attempts.ShouldBe(factory.Stage1Attempts);
        (eval.Uniform.Attempts.Count, eval.Uniform.Won).ShouldBe((factory.Stage2Attempts, factory.WonStage2));
        eval.Uniform.Attempts.SelectMany(a => a.Instances.Select(i => (a.Number, i.Slot, i.Hit)))
            .ShouldBe(factory.Samples.Select(s => (s.Attempt, s.Slot, s.Hit)));
        eval.Uniform.Attempts.ShouldAllBe(a => a.Decision == null);
    }

    [Fact]
    public void 두_갈래는_같은_1단계에서_같은_번호와_시드로_갈라진다()
    {
        (int _, EvalResult r) = First(r => r.Network.Attempts.Count >= 2 && r.Uniform.Attempts.Count >= 2);

        r.Network.Attempts[0].Number.ShouldBe(r.Stage1Attempts + 1);
        foreach ((EvalAttempt n, EvalAttempt u) in r.Network.Attempts.Zip(r.Uniform.Attempts))
        {
            (n.Number, n.Seed).ShouldBe((u.Number, u.Seed));
        }

        // 첫 시도는 입력이 같다(1단계 기록뿐) — 로짓도 같다. 둘째부터는 갈래마다 앞 2단계 시도가 달라 입력이 갈린다.
        r.Network.Attempts[0].Logits.ShouldBe(r.Uniform.Attempts[0].Logits);
        r.Network.Attempts[0].Samples.ShouldBe(r.Uniform.Attempts[0].Samples);
    }

    [Fact]
    public void 망_갈래의_첫_결정은_1단계_기록으로_세운_망의_결정이다()
    {
        // 게임의 고르기가 서는 그 자리 · 그 재료 — 평가만의 길로 세우면 게임과 다른 고르기를 잰다.
        (int bot, EvalResult r) = First(r => r.Network.Attempts.Count > 0 && r.Network.Attempts[0].Decision?.Mode == PickDecision.ModeNarrowed);
        var records = new List<AttemptRecord>();
        BotRun.Run(_fleetSeed, bot, Tables, 5, 5, records.Add);
        EvalAttempt first = r.Network.Attempts[0];
        var picker = new NetworkPicker(new PickerInputs(
            StageRoster.For(Tables.Stages, 2), [.. records.Where(x => x.Stage == 1)], first.Seed, 2, Network: _network.Value));

        PickDecision d = first.Decision.ShouldNotBeNull();
        (d.Mode, d.Reason, d.Samples, d.Breathing).ShouldBe((picker.Decision.Mode, picker.Decision.Reason, picker.Decision.Samples, picker.Decision.Breathing));
        d.Narrowed.ShouldBe(picker.Decision.Narrowed);
        first.Logits.ShouldBe(picker.Decision.Logits.ShouldNotBeNull(), "평가가 셈한 로짓이 망 고르기의 로짓과 같아야 보정이 고르기가 본 수를 잰다");
    }

    [Fact]
    public void 좁힌_시도의_사례는_좁힌_칸_안에만_있고_숨통을_늘_싣는다()
    {
        int narrowed = 0;
        foreach (int bot in FleetBots.ReachStage2)
        {
            foreach (EvalAttempt a in Eval(bot).Network.Attempts.Where(a => a.Decision?.Mode == PickDecision.ModeNarrowed))
            {
                narrowed++;
                PickDecision d = a.Decision!;
                d.Narrowed.ShouldContain(d.Breathing.ShouldNotBeNull());
                a.Instances.ShouldAllBe(i => d.Narrowed.Contains(i.Slot));
            }
        }

        narrowed.ShouldBeGreaterThan(0, "2단계에 가는 봇 열여섯 대의 망 갈래에 좁힌 시도가 하나도 없다");
    }

    [Fact]
    public void 일단계를_못_이긴_봇은_두_갈래가_비었다()
    {
        (int _, EvalResult r) = First(r => !r.ReachedStage2, stage1Tries: 1);

        r.Network.Attempts.ShouldBeEmpty();
        r.Uniform.Attempts.ShouldBeEmpty();
        (r.Network.Won, r.Uniform.Won).ShouldBe((false, false));
    }

    [Fact]
    public void 같은_봇은_같은_글자다()
    {
        Csv(Eval(5)).ShouldBe(Csv(Eval(5)));
    }

    [Fact]
    public void 스레드_수와_무관하게_같은_글자다()
    {
        static string Batch(int threads)
        {
            var text = new StringBuilder();
            FactoryBatch.Run(0, 6, threads, bot => EvalRun.Run(_fleetSeed, bot, Tables, _network.Value, 3, 3), results =>
            {
                foreach (EvalResult result in results)
                {
                    text.Append(Csv(result));
                }
            }, chunk: 4);
            return text.ToString();
        }

        Batch(threads: 4).ShouldBe(Batch(threads: 1));
    }

    [Fact]
    public void 줄은_머리와_칸_수가_같다()
    {
        (int _, EvalResult r) = First(r => r.Network.Attempts.Count > 0 && r.Uniform.Attempts.Count > 0);
        int heads = StageRoster.For(Tables.Stages, 2).Count;
        var attempts = new StringBuilder();
        var samples = new StringBuilder();
        var bots = new StringBuilder();

        EvalCsv.AppendAttempts(attempts, r);
        EvalCsv.AppendSamples(samples, r);
        EvalCsv.AppendBot(bots, r);

        foreach ((StringBuilder text, string header) in new[] { (attempts, EvalCsv.AttemptsHeader(heads)), (samples, EvalCsv.SamplesHeader), (bots, EvalCsv.BotsHeader) })
        {
            string[] lines = text.ToString().TrimEnd('\n').Split('\n');
            int cells = header.Split(',').Length;
            lines.ShouldNotBeEmpty();
            lines.Select(line => line.Split(',').Length).ShouldAllBe(n => n == cells, header);
        }

        attempts.ToString().Split('\n')[0].Split(',')[1].ShouldBe(NetworkPicker.Id, "한 봇의 줄은 망 갈래가 먼저다");
    }
}
