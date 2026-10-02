using System;
using System.Globalization;
using System.Text;
using Overfit.Battle.Rules;

namespace Overfit.Factory;

/// <summary>
/// 공장의 두 CSV 의 서식 (#108 · 설계 2026-09-28 §4.4). 수는 불변 문화 · 왕복 서식(<c>"R"</c>)이다 — 같은 double 은 같은 글자라 스레드 수와
/// 무관하게 파일이 바이트까지 같고, 파이썬이 되읽으면 비트까지 같은 수를 본다. 줄 끝은 <c>\n</c> 하나다(기기마다 안 갈리게).
/// </summary>
public static class SampleCsv
{
    /// <summary><c>samples.csv</c> 의 머리 — 봇 · 시도 · 칸 · 라벨.</summary>
    public const string SamplesHeader = "bot,attempt,slot,label";

    /// <summary>성향의 칸 — 습관 · 수단 셋 · 나머지 아홉(<see cref="AppendTraits"/> 의 순서). 패리의 칸은 패리와 같이 걷었다(#168).</summary>
    public const string TraitsHeader = "habit,dash,jump,guard,reaction,jitter,bias,rhythm,dash_inward,rest_gap,greed,chain,jump_lead";

    /// <summary><c>bots.csv</c> 의 머리 — 봇 번호 · 성향 전부 · 흐름(시도 · 이겼나) · 틱 · 사례 수.</summary>
    public const string BotsHeader = "bot," + TraitsHeader + ",attempts,won,ticks,samples";

    /// <summary>사례 한 줄 — 라벨은 0 · 1.</summary>
    public static void AppendSample(StringBuilder builder, FactorySample sample)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sample);
        Int(builder, sample.Bot).Append(',');
        Int(builder, sample.Attempt).Append(',');
        Int(builder, sample.Slot).Append(',');
        builder.Append(sample.Hit ? '1' : '0').Append('\n');
    }

    /// <summary>봇 한 줄 — 습관은 <c>fleet.json</c> 과 같은 소문자 이름 · 참거짓은 0 · 1.</summary>
    public static void AppendBot(StringBuilder builder, BotResult result)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(result);
        Int(builder, result.Bot).Append(',');
        AppendTraits(builder, result.Traits);
        Int(builder.Append(','), result.Attempts).Append(',');
        builder.Append(result.Won ? '1' : '0').Append(',');
        builder.Append(result.Ticks.ToString(CultureInfo.InvariantCulture)).Append(',');
        Int(builder, result.Samples.Count).Append('\n');
    }

    /// <summary>성향 — <see cref="TraitsHeader"/> 의 칸. 앞에 쉼표를 안 두고 습관부터 쓴다.</summary>
    internal static void AppendTraits(StringBuilder builder, BotTraits t)
    {
        builder.Append(t.Habit.ToString().ToLowerInvariant());
        ReadOnlySpan<double> traits =
        [
            t.Dash,
            t.Jump,
            t.Guard,
            t.ReactionSeconds,
            t.JitterSeconds,
            t.BiasSeconds,
            t.Rhythm,
            t.DashInward,
            t.RestGap,
            t.Greed,
            t.Chain,
            t.JumpLead,
        ];
        foreach (double value in traits)
        {
            Real(builder.Append(','), value);
        }
    }

    internal static StringBuilder Int(StringBuilder builder, int value) => builder.Append(value.ToString(CultureInfo.InvariantCulture));

    internal static StringBuilder Real(StringBuilder builder, double value) => builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
}
