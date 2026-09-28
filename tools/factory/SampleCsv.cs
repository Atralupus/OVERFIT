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
    /// <summary><c>samples.csv</c> 의 머리 — 봇 · 시도 · 칸 · 라벨 뒤에 입력 19칸이 <see cref="PlayerFeatures.Names"/> 의 순서로 온다.</summary>
    public static string SamplesHeader { get; } = "bot,attempt,slot,label," + string.Join(',', PlayerFeatures.Names);

    /// <summary><c>bots.csv</c> 의 머리 — 봇 번호 · 성향 전부 · 흐름(1단계 시도 · 2단계에 갔나 · 2단계 시도 · 2단계를 이겼나) · 틱 · 사례 수.</summary>
    public static string BotsHeader { get; } =
        "bot,habit,dash,jump,parry,guard,reaction,jitter,bias,rhythm,dash_inward,rest_gap,greed,chain,jump_lead,"
        + "stage1_attempts,reached_stage2,stage2_attempts,won_stage2,ticks,samples";

    /// <summary>사례 한 줄 — 라벨은 0 · 1.</summary>
    public static void AppendSample(StringBuilder builder, FactorySample sample)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(sample);
        Int(builder, sample.Bot).Append(',');
        Int(builder, sample.Attempt).Append(',');
        Int(builder, sample.Slot).Append(',');
        builder.Append(sample.Hit ? '1' : '0');
        foreach (double value in sample.Features)
        {
            Real(builder.Append(','), value);
        }

        builder.Append('\n');
    }

    /// <summary>봇 한 줄 — 습관은 <c>fleet.json</c> 과 같은 소문자 이름 · 참거짓은 0 · 1.</summary>
    public static void AppendBot(StringBuilder builder, BotResult result)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(result);
        BotTraits t = result.Traits;
        Int(builder, result.Bot).Append(',');
        builder.Append(t.Habit.ToString().ToLowerInvariant());
        ReadOnlySpan<double> traits =
        [
            t.Dash,
            t.Jump,
            t.Parry,
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

        Int(builder.Append(','), result.Stage1Attempts).Append(',');
        builder.Append(result.ReachedStage2 ? '1' : '0').Append(',');
        Int(builder, result.Stage2Attempts).Append(',');
        builder.Append(result.WonStage2 ? '1' : '0').Append(',');
        builder.Append(result.Ticks.ToString(CultureInfo.InvariantCulture)).Append(',');
        Int(builder, result.Samples.Count).Append('\n');
    }

    private static StringBuilder Int(StringBuilder builder, int value) => builder.Append(value.ToString(CultureInfo.InvariantCulture));

    private static StringBuilder Real(StringBuilder builder, double value) => builder.Append(value.ToString("R", CultureInfo.InvariantCulture));
}
