using System;
using System.Collections.Generic;
using System.Globalization;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary><c>network.json</c> 의 층 하나 — w 는 행이 출력 · 열이 입력이다.</summary>
public sealed class NetworkLayer
{
    public required IReadOnlyList<IReadOnlyList<double>> W { get; init; }

    public required IReadOnlyList<double> B { get; init; }
}

/// <summary>망이 배운 원본 — 공장의 매니페스트에서 옮겨 적었다(#110).</summary>
public sealed class NetworkTrainedOn
{
    /// <summary>원본을 지은 커밋.</summary>
    public required string Commit { get; init; }

    /// <summary>원본을 지을 때의 <see cref="Core.DataDigest"/> — 지금과 다르면 망이 배운 게임이 지금의 게임이 아니다.</summary>
    public required string DataDigest { get; init; }

    public required ulong FleetSeed { get; init; }

    public required int Bots { get; init; }
}

/// <summary><c>overfit/data/network.json</c> 의 모양 (설계 2026-09-28 §5.6). <c>metrics</c> 는 사람이 읽는 수라 안 읽는다.</summary>
public sealed class NetworkData
{
    public required int Version { get; init; }

    public required IReadOnlyList<string> Features { get; init; }

    public required IReadOnlyList<double> Mean { get; init; }

    public required IReadOnlyList<double> Std { get; init; }

    public required IReadOnlyList<string> Heads { get; init; }

    /// <summary>칸마다 학습 몫 기저율의 로짓 — 들어 올림 = 로짓 − 이것(설계 §6.2). 로그는 파이썬이 한 번 셈했다.</summary>
    public required IReadOnlyList<double> Baseline { get; init; }

    public required IReadOnlyList<NetworkLayer> Layers { get; init; }

    public required NetworkTrainedOn TrainedOn { get; init; }
}

/// <summary>
/// 게임 안의 망 (#112 · 설계 2026-09-28 §6.1) — 입력 19칸(<see cref="PlayerFeatures"/>)에서 2단계 명부의 칸마다 로짓 하나.
///
/// <para>
/// <b>연산 순서가 계약이다</b>(설계 §5.7): 표준화 <c>z = (x - mean) / std</c> → 층마다 출력 j 에 대해 <c>acc = b[j]</c> 에서 입력 순서대로
/// <c>acc += w[j][i] * z[i]</c> → 마지막 층을 빼고 ReLU = <c>acc &gt; 0 ? acc : 0.0</c>. 파이썬의 골든(<c>ml/golden.py</c>)이 같은 순서로 셈했고
/// 테스트가 <c>==</c> 로 본다 — 사칙연산은 올바르게 반올림된 결과가 하나뿐이고 .NET 은 부동소수를 스스로 합치지 않으므로(FMA 없음) 기기가 달라도
/// 같다. 반복문만 쓴다 — <c>Vector&lt;T&gt;</c> · 병렬 · <c>Math.Exp</c> · <c>Math.Log</c> 가 없다(설계 §8). ReLU 를 <c>Math.Max</c> 로 안 쓰는 까닭:
/// −0 과 NaN 에서 파이썬의 <c>max</c> 와 다른 값을 낸다.
/// </para>
/// </summary>
public sealed class PlayerNet
{
    private readonly double[] _mean;
    private readonly double[] _std;
    private readonly double[][][] _w;
    private readonly double[][] _b;

    private PlayerNet(NetworkData data)
    {
        _mean = [.. data.Mean];
        _std = [.. data.Std];
        _w = new double[data.Layers.Count][][];
        _b = new double[data.Layers.Count][];
        for (int k = 0; k < data.Layers.Count; k++)
        {
            NetworkLayer layer = data.Layers[k];
            _w[k] = new double[layer.W.Count][];
            for (int j = 0; j < layer.W.Count; j++)
            {
                _w[k][j] = [.. layer.W[j]];
            }

            _b[k] = [.. layer.B];
        }

        Heads = data.Heads;
        Baseline = data.Baseline;
        TrainedOn = data.TrainedOn;
    }

    /// <summary>머리 — 2단계 명부의 패턴 id, 그 순서(뽑기 좌표의 칸).</summary>
    public IReadOnlyList<string> Heads { get; }

    /// <summary>칸마다 기저율의 로짓.</summary>
    public IReadOnlyList<double> Baseline { get; }

    public NetworkTrainedOn TrainedOn { get; }

    /// <summary>
    /// 망을 세운다. 필수 키가 빠지면 <see cref="DataException"/>(빠진 키 전부 — 부팅이 멈춘다). 모양이 틀리면(입력 이름이 <see cref="PlayerFeatures.Names"/>
    /// 와 다름 · 층이 안 이어짐 · 편차 ≤ 0 · 기저율의 수가 머리와 다름) <c>[E]</c> 를 남기고 null — 규칙 위반이다.
    /// </summary>
    public static PlayerNet? Load(string json, string source)
    {
        NetworkData data = JsonData<NetworkData>.ParseOne(json, source);
        List<string> problems = Shape(data);
        if (problems.Count > 0)
        {
            Log.Error("net", $"shape source={source} {string.Join(" · ", problems)}");
            return null;
        }

        return new PlayerNet(data);
    }

    /// <summary>입력 19칸 → 로짓(머리 수). 순서는 위 그대로다.</summary>
    public double[] Logits(IReadOnlyList<double> input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Count != _mean.Length)
        {
            throw new ArgumentException($"입력이 {input.Count}칸이다 — 망은 {_mean.Length}칸을 받는다", nameof(input));
        }

        double[] z = new double[_mean.Length];
        for (int i = 0; i < z.Length; i++)
        {
            z[i] = (input[i] - _mean[i]) / _std[i];
        }

        for (int k = 0; k < _w.Length; k++)
        {
            bool hidden = k < _w.Length - 1;
            double[][] w = _w[k];
            double[] b = _b[k];
            double[] next = new double[b.Length];
            for (int j = 0; j < b.Length; j++)
            {
                double acc = b[j];
                double[] row = w[j];
                for (int i = 0; i < z.Length; i++)
                {
                    acc += row[i] * z[i];
                }

                next[j] = hidden ? (acc > 0 ? acc : 0.0) : acc;
            }

            z = next;
        }

        return z;
    }

    private static List<string> Shape(NetworkData data)
    {
        var problems = new List<string>();
        IReadOnlyList<string> names = PlayerFeatures.Names;
        bool sameNames = data.Features.Count == names.Count;
        for (int i = 0; sameNames && i < names.Count; i++)
        {
            sameNames = string.Equals(data.Features[i], names[i], StringComparison.Ordinal);
        }

        if (!sameNames)
        {
            problems.Add($"features={string.Join(',', data.Features)} expected={string.Join(',', names)}");
        }

        int width = data.Features.Count;
        if (data.Mean.Count != width || data.Std.Count != width)
        {
            problems.Add($"mean={data.Mean.Count} std={data.Std.Count} features={width}");
        }

        for (int i = 0; i < data.Std.Count; i++)
        {
            if (!(data.Std[i] > 0))
            {
                problems.Add($"std[{i}]={data.Std[i].ToString("R", CultureInfo.InvariantCulture)}");
            }
        }

        if (data.Layers.Count == 0)
        {
            problems.Add("layers=0");
        }

        for (int k = 0; k < data.Layers.Count; k++)
        {
            NetworkLayer layer = data.Layers[k];
            if (layer.W.Count != layer.B.Count)
            {
                problems.Add($"layer={k} rows={layer.W.Count} b={layer.B.Count}");
            }

            for (int j = 0; j < layer.W.Count; j++)
            {
                if (layer.W[j].Count != width)
                {
                    problems.Add($"layer={k} row={j} width={layer.W[j].Count} expected={width}");
                    break;
                }
            }

            width = layer.B.Count;
        }

        if (width != data.Heads.Count || data.Baseline.Count != data.Heads.Count)
        {
            problems.Add($"outputs={width} heads={data.Heads.Count} baseline={data.Baseline.Count}");
        }

        return problems;
    }
}
