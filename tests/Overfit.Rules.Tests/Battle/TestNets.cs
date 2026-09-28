using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 손으로 지은 작은 망 (#112 · 설계 2026-09-28 §9) — 학습할 때마다 테스트가 흔들리지 않게 진짜 <c>network.json</c> 대신 쓴다. 입력 이름은
/// <see cref="PlayerFeatures.Names"/> · 평균 0 · 편차 1 · 머리는 2단계 명부다. 층을 안 주면 가중치가 전부 0 인 한 층이라 로짓이 곧 출력 편향이다
/// — 들어 올림(로짓 − 기저율)을 테스트가 손으로 정한다.
/// </summary>
internal static class TestNets
{
    public static readonly string[] Heads = ["3연격", "점프 3연속", "1타 돌진", "1타 잡기", "엇박 3연격"];

    /// <summary>진짜 망 — 출력 폴더의 <c>data/network.json</c>.</summary>
    public static PlayerNet Real() =>
        PlayerNet.Load(File.ReadAllText(Path.Combine("data", "network.json")), "network.json")
        ?? throw new InvalidDataException("data/network.json 의 모양이 틀렸다");

    /// <summary>로짓이 <paramref name="logits"/> 로 늘 같은 망(가중치 0 · 한 층). 기저율은 <paramref name="baseline"/>(없으면 0).</summary>
    public static PlayerNet Constant(double[] logits, double[]? baseline = null) =>
        PlayerNet.Load(Json(logits: logits, baseline: baseline), "시험 망") ?? throw new InvalidDataException("시험 망의 모양이 틀렸다");

    /// <summary>
    /// 망 JSON. 층은 (w, b) 들 — 안 주면 19 → 5 의 영행렬과 <paramref name="logits"/> 편향 한 층이다. 나머지 칸은 모양을 일부러 틀리게 하는 테스트만 준다.
    /// </summary>
    public static string Json(
        double[]? logits = null,
        double[]? baseline = null,
        IReadOnlyList<string>? features = null,
        double[]? mean = null,
        double[]? std = null,
        IReadOnlyList<string>? heads = null,
        IReadOnlyList<(double[][] W, double[] B)>? layers = null,
        string? omit = null)
    {
        features ??= PlayerFeatures.Names;
        heads ??= Heads;
        int n = features.Count;
        layers ??= [(Enumerable.Range(0, heads.Count).Select(_ => new double[n]).ToArray(), logits ?? new double[heads.Count])];
        var parts = new List<string>
        {
            "\"version\": 1",
            $"\"features\": [{string.Join(", ", features.Select(f => $"\"{f}\""))}]",
            $"\"mean\": {Numbers(mean ?? new double[n])}",
            $"\"std\": {Numbers(std ?? Enumerable.Repeat(1.0, n).ToArray())}",
            $"\"heads\": [{string.Join(", ", heads.Select(h => $"\"{h}\""))}]",
            $"\"baseline\": {Numbers(baseline ?? new double[heads.Count])}",
            "\"layers\": [" + string.Join(", ", layers.Select(l => $"{{\"w\": [{string.Join(", ", l.W.Select(Numbers))}], \"b\": {Numbers(l.B)}}}")) + "]",
            "\"trained_on\": {\"commit\": \"시험\", \"data_digest\": \"0\", \"fleet_seed\": 0, \"bots\": 0}",
        };
        if (omit is not null)
        {
            parts.RemoveAll(p => p.StartsWith($"\"{omit}\"", System.StringComparison.Ordinal));
        }

        return "{" + string.Join(", ", parts) + "}";
    }

    private static string Numbers(double[] values) =>
        "[" + string.Join(", ", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";

    /// <summary>관측 <paramref name="events"/> 건을 가진 기록 하나 — 근거의 수(samples)만 보는 테스트가 쓴다.</summary>
    public static AttemptRecord[] History(int events)
    {
        var list = new List<DodgeEvent>();
        for (int i = 0; i < events; i++)
        {
            list.Add(new DodgeEvent("3연격", DodgeVerb.Dash, HitVerdict.Dodged, 0, 1, false, 100, false, true, true, true, true));
        }

        return [new AttemptRecord(1, 1, 7, BattleOutcome.Win, list)];
    }

    /// <summary>진짜 고르기 수치(<c>balance.json</c> 의 <c>picker</c>)에서 몇 칸만 바꾼다.</summary>
    public static PickerBalance Knobs(int share = 50, int minSamples = 20, double liftMin = 0.405, int maxTargeted = 2) => new()
    {
        NetworkSharePercent = share,
        MinSamples = minSamples,
        LiftMin = liftMin,
        MaxTargeted = maxTargeted,
    };

    /// <summary>한 줄 설명을 위한 JSON 조각 — 테스트 메시지에 쓴다.</summary>
    public static string Describe(IEnumerable<double> values) =>
        new StringBuilder().AppendJoin(",", values.Select(v => v.ToString("F3", CultureInfo.InvariantCulture))).ToString();
}
