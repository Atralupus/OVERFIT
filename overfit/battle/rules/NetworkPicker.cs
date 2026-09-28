using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 망 고르기가 한 시도에 선 까닭 (#112 · 설계 2026-09-28 §6.2). 로그(<c>[pick][D]</c>)와 시도 기록(<see cref="AttemptLog"/>)이 싣는다.
/// </summary>
/// <param name="Mode"><see cref="ModeFull"/>(다섯 전부 — uniform 과 같은 뽑기) · <see cref="ModeNarrowed"/>(좁힌 명부).</param>
/// <param name="Reason"><see cref="ReasonThin"/>(근거가 얇다) · <see cref="ReasonNoHabit"/>(도드라진 칸이 없다) · <see cref="ReasonHabit"/>(겨냥이 섰다).</param>
/// <param name="Samples">입력의 관측 수 — <c>min_samples</c> 와 견준 값.</param>
/// <param name="Narrowed">뽑는 칸들(명부의 인덱스 · 명부 순서). 다섯 전부면 0 ~ 4 다.</param>
/// <param name="Breathing">숨통 칸 — 좁혔을 때만.</param>
/// <param name="Logits">칸마다 로짓 — 망을 돌렸을 때만(<see cref="ReasonThin"/> 이면 null).</param>
/// <param name="Lifts">칸마다 들어 올림(로짓 − 기저율의 로짓) — 망을 돌렸을 때만.</param>
public sealed record PickDecision(
    string Mode, string Reason, int Samples, IReadOnlyList<int> Narrowed, int? Breathing, IReadOnlyList<double>? Logits, IReadOnlyList<double>? Lifts)
{
    public const string ModeFull = "full";
    public const string ModeNarrowed = "narrowed";
    public const string ReasonThin = "thin";
    public const string ReasonNoHabit = "no_habit";
    public const string ReasonHabit = "habit";
}

/// <summary>
/// 망의 고르기 — 좁힌 명부 (#112 · 설계 2026-09-28 §6.2). 등록표 id <see cref="Id"/>. <b>시도를 시작할 때 한 번</b> 세운다 — 판 도중에는 안 바뀐다.
///
/// <list type="number">
/// <item>입력 = <see cref="PlayerFeatures"/>(이번 런의 기록). 관측이 <c>min_samples</c> 보다 적으면 다섯 전부(<c>thin</c>).</item>
/// <item>칸마다 들어 올림 = 로짓 − 기저율의 로짓 — 평균의 사람보다 이 사람에게 얼마나 더 잘 먹히나(로그 오즈 비).</item>
/// <item>겨냥 = 들어 올림이 <c>lift_min</c> 이상인 칸, 큰 순서(같으면 앞 칸)로 많아야 <c>max_targeted</c> 개. 없으면 다섯 전부(<c>no_habit</c>).</item>
/// <item>숨통 = 겨냥이 아닌 칸 중 로짓이 가장 낮은 칸(같으면 앞 칸) — 이 사람이 가장 덜 맞을 패턴이다.</item>
/// <item>좁힌 명부 = 겨냥 ∪ 숨통, 명부 순서. 뽑기 = 좁힌 명부의 <c>Det.RollInt(시드, PatternPick, 좁힌 수, k1: draw)</c> 번째 칸.</item>
/// </list>
///
/// <para>
/// 다섯 전부일 때 좁힌 명부가 0 ~ 4 라 뽑기가 <see cref="UniformPicker"/> 와 <b>비트까지 같다</b> — 망 갈래의 thin · no_habit 과 무작위 갈래를 가르는 것은
/// 갈래 이름뿐이다. 결정 경로는 사칙연산과 비교뿐이다(설계 §8) — 기저율의 로짓은 파이썬이 한 번 셈해 데이터로 실었다.
/// </para>
///
/// <para>
/// <b>왜 들어 올림인가 — 약점 저격을 피하려고.</b> 로짓 자체가 높은 칸을 고르면 고르게 약한 초보자일수록 가혹해진다. 들어 올림은 "평균의 사람보다 이 사람에게
/// 더 먹히는" 칸이라 고르게 약한 사람은 어느 칸도 도드라지지 않아 다섯 전부로 돌기를 바랐는데, <b>아직 그렇지 않다</b> — 같은 봇에게 망 보스와 무작위
/// 보스를 붙여 재 보니 고르게 약한 봇의 망 갈래 시도 중 62% 가 좁혀졌다(설계 2026-09-28 §7.1 · 선은 20% 이하). 문턱은 여기서 조용히 고치지 않는다 —
/// 규칙의 변형을 가늠한 표(§7.1)를 보고 무엇을 바꿀지 정한다.
/// </para>
/// </summary>
public sealed class NetworkPicker : IPatternPicker
{
    /// <summary>등록표 id — <c>stages.json</c> 의 <c>picker</c>.</summary>
    public const string Id = "network";

    private readonly ulong _seed;
    private readonly int[] _narrowed;

    /// <summary>
    /// 세운다. 망이 없거나(<see cref="PickerInputs.Network"/>) 망의 머리가 명부와 다르면 <see cref="ArgumentException"/> — <c>StageRoster.Setup</c> 이
    /// 받아 <c>[E] picker_rejected</c> 로 바꾸고 판을 안 세운다. 머리의 순서가 곧 칸이라, 학습 뒤에 명부를 바꾸면 망의 칸이 다른 패턴을 가리킨다.
    /// </summary>
    public NetworkPicker(PickerInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        NetworkContext network = inputs.Network ?? throw new ArgumentException("망 고르기에 망(network.json)이 없다", nameof(inputs));
        if (!inputs.Roster.SequenceEqual(network.Net.Heads, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"망의 머리({string.Join(',', network.Net.Heads)})가 명부({string.Join(',', inputs.Roster)})와 다르다 — 망을 다시 학습하라", nameof(inputs));
        }

        _seed = inputs.Seed;
        Decision = Decide(inputs.History, network);
        _narrowed = [.. Decision.Narrowed];
        Log.Debug("pick", () => Describe(Decision, inputs.Roster));
    }

    /// <summary>이 시도의 결정 — 로그와 기록이 싣는다.</summary>
    public PickDecision Decision { get; }

    public int Pick(int draw) => _narrowed[Det.RollInt(_seed, Det.Domain.PatternPick, _narrowed.Length, k1: draw)];

    private static PickDecision Decide(IReadOnlyList<AttemptRecord> history, NetworkContext network)
    {
        PickerBalance knobs = network.Knobs;
        int heads = network.Net.Heads.Count;
        int[] all = [.. Enumerable.Range(0, heads)];
        double[] features = PlayerFeatures.From(history);
        int samples = (int)features[PlayerFeatures.SamplesIndex];
        if (samples < knobs.MinSamples)
        {
            return new PickDecision(PickDecision.ModeFull, PickDecision.ReasonThin, samples, all, null, null, null);
        }

        double[] logits = network.Net.Logits(features);
        double[] lifts = new double[heads];
        for (int h = 0; h < heads; h++)
        {
            lifts[h] = logits[h] - network.Net.Baseline[h];
        }

        // 큰 순서 · 같으면 앞 칸 — OrderByDescending 은 안정 정렬이라 같은 값의 인덱스 순서가 그대로 남는다. 숨통이 설 칸을 늘 하나 남긴다.
        int[] targeted = [.. all.Where(h => lifts[h] >= knobs.LiftMin).OrderByDescending(h => lifts[h]).Take(Math.Min(knobs.MaxTargeted, heads - 1))];
        if (targeted.Length == 0)
        {
            return new PickDecision(PickDecision.ModeFull, PickDecision.ReasonNoHabit, samples, all, null, logits, lifts);
        }

        int breathing = -1;
        for (int h = 0; h < heads; h++)
        {
            if (!targeted.Contains(h) && (breathing < 0 || logits[h] < logits[breathing]))
            {
                breathing = h;
            }
        }

        int[] narrowed = [.. targeted.Append(breathing).Order()];
        return new PickDecision(PickDecision.ModeNarrowed, PickDecision.ReasonHabit, samples, narrowed, breathing, logits, lifts);
    }

    /// <summary>로그 한 줄(설계 §6.6) — 수는 F3 이라 같은 double 은 같은 글자다.</summary>
    private static string Describe(PickDecision d, IReadOnlyList<string> roster)
    {
        string line = $"mode={d.Mode} reason={d.Reason} samples={d.Samples}";
        if (d.Mode == PickDecision.ModeNarrowed)
        {
            line += $" narrowed={string.Join(',', d.Narrowed.Select(i => roster[i]))} breathing={roster[d.Breathing!.Value]}";
        }

        if (d.Logits is not null && d.Lifts is not null)
        {
            line += $" logits={F3(d.Logits)} lifts={F3(d.Lifts)}";
        }

        return line;
    }

    private static string F3(IReadOnlyList<double> values) => string.Join(',', values.Select(v => v.ToString("F3", CultureInfo.InvariantCulture)));
}
