using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Overfit.Battle.Rules;
using Overfit.Factory;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Factory;

/// <summary>
/// 묶음 병렬과 결정론 (#108 · 설계 2026-09-28 §4.5) — 봇은 서로 독립이라 여러 스레드로 돌리되 봇 번호 순서로 쓴다. 같은 입력이면 스레드 수와
/// 무관하게 파일이 글자까지 같다.
/// </summary>
public class FactoryBatchTests
{
    private static readonly Lazy<FactoryTables> _tables = new(() => FactoryTables.Load("data", "fleet.json"));

    /// <summary>봇 <paramref name="from"/> ~ <paramref name="to"/>(끝 빼고)을 돌려 두 CSV 를 글자로 짓는다 — 묶음마다 받은 봇 번호도 적는다.</summary>
    private static (string Samples, string Bots, List<int[]> Chunks) Csv(int from, int to, int threads, int chunk)
    {
        var samples = new StringBuilder(SampleCsv.SamplesHeader).Append('\n');
        var bots = new StringBuilder(SampleCsv.BotsHeader).Append('\n');
        var chunks = new List<int[]>();
        FactoryBatch.Run(from, to, threads, bot => BotRun.Run(51, bot, _tables.Value, 3), results =>
        {
            chunks.Add(results.Select(r => r.Bot).ToArray());
            foreach (BotResult result in results)
            {
                SampleCsv.AppendBot(bots, result);
                foreach (FactorySample sample in result.Samples)
                {
                    SampleCsv.AppendSample(samples, sample);
                }
            }
        }, chunk);

        return (samples.ToString(), bots.ToString(), chunks);
    }

    [Fact]
    public void 스레드_수와_무관하게_같은_글자다()
    {
        // 보스전이 하나라 판마다 사례가 선다 — 봇 24 ~ 31 이 사례를 낸다.
        (string samples1, string bots1, List<int[]> chunks1) = Csv(24, 32, threads: 1, chunk: 3);
        (string samples4, string bots4, List<int[]> chunks4) = Csv(24, 32, threads: 4, chunk: 3);

        samples4.ShouldBe(samples1);
        bots4.ShouldBe(bots1);

        // 묶음은 봇 번호 순서로, 묶음 안도 번호 순서로 온다 — 끝 묶음은 남은 만큼이다.
        chunks1.Select(c => string.Join(' ', c)).ShouldBe(new[] { "24 25 26", "27 28 29", "30 31" });
        chunks4.Select(c => string.Join(' ', c)).ShouldBe(chunks1.Select(c => string.Join(' ', c)));
        samples1.Split('\n').Length.ShouldBeGreaterThan(2, "봇 여덟 대가 사례를 하나도 안 냈다");
    }

    [Fact]
    public void 사례의_줄은_봇_시도_칸_라벨이다()
    {
        var builder = new StringBuilder();

        SampleCsv.AppendSample(builder, new FactorySample(7, 3, 4, true));

        builder.ToString().ShouldBe("7,3,4,1\n");
        SampleCsv.SamplesHeader.Split(',').ShouldBe(new[] { "bot", "attempt", "slot", "label" });
        SampleCsv.BotsHeader.Split(',').Length.ShouldBe(SampleCsv.BotsHeader.Split(',').Distinct().Count(), "bots.csv 의 머리에 같은 이름이 둘이다");
    }

    [Fact]
    public void 성향의_수는_왕복_서식이다()
    {
        // 같은 double 은 같은 글자다 — 되읽으면 비트까지 같아야 분석(파이썬)이 공장과 같은 수를 본다. 칸은 봇 · 습관 뒤의 열둘이다(TraitsHeader — 패리의 칸은 #168 에서 걷었다).
        BotResult result = BotRun.Run(51, 0, _tables.Value, 1);
        var builder = new StringBuilder();

        SampleCsv.AppendBot(builder, result);

        string[] cells = builder.ToString().TrimEnd('\n').Split(',');
        double[] back = cells[2..14].Select(c => double.Parse(c, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        BotTraits t = result.Traits;
        double[] traits = [t.Dash, t.Jump, t.Guard, t.ReactionSeconds, t.JitterSeconds, t.BiasSeconds, t.Rhythm, t.DashInward, t.RestGap, t.Greed, t.Chain, t.JumpLead];
        back.Select(BitConverter.DoubleToInt64Bits).ShouldBe(traits.Select(BitConverter.DoubleToInt64Bits));
    }

    [Fact]
    public void 봇의_줄은_머리와_칸_수가_같다()
    {
        var builder = new StringBuilder();
        BotResult result = BotRun.Run(51, 0, _tables.Value, 1);

        SampleCsv.AppendBot(builder, result);

        string[] cells = builder.ToString().TrimEnd('\n').Split(',');
        cells.Length.ShouldBe(SampleCsv.BotsHeader.Split(',').Length);
        cells[0].ShouldBe("0");
        cells[1].ShouldBe(result.Traits.Habit.ToString().ToLowerInvariant());
    }
}
