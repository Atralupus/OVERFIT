using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Overfit.Battle.Rules;

namespace Overfit.Factory;

/// <summary>
/// 평가의 세 CSV 의 서식 (#114 · 설계 2026-09-28 §7.1) — 공장(<see cref="SampleCsv"/>)과 같은 약속: 불변 문화 · 왕복 서식 · 줄 끝 <c>\n</c> 하나라
/// 스레드 수와 무관하게 바이트까지 같다. 갈래는 <c>network</c> · <c>uniform</c> 이고, 한 봇의 줄은 망 갈래가 먼저다.
///
/// <list type="bullet">
/// <item><c>attempts.csv</c> — 2단계 시도 한 줄: 갈래 · 번호 · 시드 · 관측 수 · (망 갈래면) 결정 · 칸마다의 로짓 · 결과 · 틱.</item>
/// <item><c>samples.csv</c> — 사례 한 줄: 갈래 · 시도 · 칸 · 라벨. 예측은 그 시도의 로짓에서 칸으로 읽는다.</item>
/// <item><c>bots.csv</c> — 봇 한 줄: 성향 · 1단계 · 갈래마다의 시도 수와 이겼나.</item>
/// </list>
/// </summary>
public static class EvalCsv
{
    /// <summary><c>attempts.csv</c> 의 머리 — 로짓은 머리(2단계 명부)마다 한 칸이다. 좁힌 칸은 공백으로 잇는다(쉼표는 칸의 경계다).</summary>
    public static string AttemptsHeader(int heads) =>
        "bot,arm,attempt,seed,samples,mode,reason,narrowed,breathing,"
        + string.Join(',', Enumerable.Range(0, heads).Select(h => string.Create(CultureInfo.InvariantCulture, $"logit_{h}")))
        + ",outcome,ticks";

    /// <summary><c>samples.csv</c> 의 머리.</summary>
    public const string SamplesHeader = "bot,arm,attempt,slot,label";

    /// <summary><c>bots.csv</c> 의 머리 — 성향은 공장의 <c>bots.csv</c> 와 같은 칸이다.</summary>
    public static string BotsHeader { get; } =
        "bot," + SampleCsv.TraitsHeader + ",stage1_attempts,reached_stage2,network_attempts,network_won,uniform_attempts,uniform_won,ticks";

    /// <summary>봇의 2단계 시도들 — 망 갈래 먼저.</summary>
    public static void AppendAttempts(StringBuilder builder, EvalResult result)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(result);
        foreach (EvalArm arm in new[] { result.Network, result.Uniform })
        {
            foreach (EvalAttempt a in arm.Attempts)
            {
                SampleCsv.Int(builder, result.Bot).Append(',').Append(arm.Arm).Append(',');
                SampleCsv.Int(builder, a.Number).Append(',');
                builder.Append(a.Seed.ToString(CultureInfo.InvariantCulture)).Append(',');
                SampleCsv.Int(builder, a.Samples).Append(',');
                PickDecision? d = a.Decision;
                builder.Append(d?.Mode).Append(',').Append(d?.Reason).Append(',');
                if (d is not null)
                {
                    builder.AppendJoin(' ', d.Narrowed);
                }

                builder.Append(',');
                if (d?.Breathing is { } breathing)
                {
                    SampleCsv.Int(builder, breathing);
                }

                foreach (double logit in a.Logits)
                {
                    SampleCsv.Real(builder.Append(','), logit);
                }

                builder.Append(',').Append(Outcome(a.Outcome)).Append(',');
                SampleCsv.Int(builder, a.Ticks).Append('\n');
            }
        }
    }

    /// <summary>봇의 사례들 — 갈래 · 시도 · 선 순서.</summary>
    public static void AppendSamples(StringBuilder builder, EvalResult result)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(result);
        foreach (EvalArm arm in new[] { result.Network, result.Uniform })
        {
            foreach (EvalAttempt a in arm.Attempts)
            {
                foreach (EvalInstance instance in a.Instances)
                {
                    SampleCsv.Int(builder, result.Bot).Append(',').Append(arm.Arm).Append(',');
                    SampleCsv.Int(builder, a.Number).Append(',');
                    SampleCsv.Int(builder, instance.Slot).Append(',').Append(instance.Hit ? '1' : '0').Append('\n');
                }
            }
        }
    }

    /// <summary>봇 한 줄 — 참거짓은 0 · 1.</summary>
    public static void AppendBot(StringBuilder builder, EvalResult result)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(result);
        SampleCsv.Int(builder, result.Bot).Append(',');
        SampleCsv.AppendTraits(builder, result.Traits);
        SampleCsv.Int(builder.Append(','), result.Stage1Attempts).Append(',').Append(result.ReachedStage2 ? '1' : '0');
        foreach (EvalArm arm in new[] { result.Network, result.Uniform })
        {
            SampleCsv.Int(builder.Append(','), arm.Attempts.Count).Append(',').Append(arm.Won ? '1' : '0');
        }

        builder.Append(',').Append(result.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    /// <summary>결과의 소문자 이름 — 시도 기록(<see cref="AttemptLog"/>)과 같은 표기다.</summary>
    private static string Outcome(BattleOutcome outcome) => outcome.ToString().ToLowerInvariant();
}
