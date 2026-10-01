using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>층 하나 — <c>w</c> 는 [출력][입력], <c>b</c> 는 [출력].</summary>
public sealed class LayerDef
{
    public required IReadOnlyList<IReadOnlyList<double>> W { get; init; }

    public required IReadOnlyList<double> B { get; init; }
}

/// <summary>가중치 JSON 의 모양 (설계 2026-10-01 조각4 §3).</summary>
public sealed class PolicyNetDef
{
    public required int Obs { get; init; }

    public required int Actions { get; init; }

    public required IReadOnlyList<string> Roster { get; init; }

    public required IReadOnlyList<LayerDef> Policy { get; init; }

    public required IReadOnlyList<LayerDef> Value { get; init; }
}

/// <summary>
/// 정책망 (설계 2026-10-01 조각4 §3 · 우산 §6.1) — 관측 → 칸마다 점수(로짓)와 판의 가치 하나. 은닉은 ReLU, 마지막 층은 선형. <b>사칙연산과 max 뿐이다</b> —
/// 같은 가중치 · 같은 관측이면 같은 비트다(덧셈 순서를 고정한다: 출력마다 b 에서 시작해 입력 순서로 더한다). 학습의 일꾼과 게임(조각 7)이 같은 코드다.
///
/// <para>
/// 관측의 칸 수 · 칸 배치의 수 · 명부가 판과 다르면 <b>읽을 때 거절한다</b> — 칸 표나 명부가 바뀐 판에 옛 망을 대면 다른 칸을 읽고도 아무 말 없이 돈다.
/// </para>
/// </summary>
public sealed class PolicyNet
{
    private readonly IReadOnlyList<LayerDef> _policy;
    private readonly IReadOnlyList<LayerDef> _value;

    private PolicyNet(IReadOnlyList<LayerDef> policy, IReadOnlyList<LayerDef> value)
    {
        _policy = policy;
        _value = value;
    }

    /// <summary>JSON 을 읽는다 — 판의 관측 칸 수 · 칸 수 · 명부와 대 본다. 틀리면 <see cref="DataException"/>.</summary>
    public static PolicyNet Parse(string json, string source, int obs, int actions, IReadOnlyList<string> roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        PolicyNetDef def = JsonData<PolicyNetDef>.ParseOne(json, source);
        if (def.Obs != obs || def.Actions != actions)
        {
            throw new DataException($"{source}: 망의 obs={def.Obs} actions={def.Actions} 가 판의 obs={obs} actions={actions} 와 다르다 — 다시 학습하라");
        }

        if (def.Roster.Count != roster.Count || !System.Linq.Enumerable.SequenceEqual(def.Roster, roster))
        {
            throw new DataException($"{source}: 망의 명부 [{string.Join(',', def.Roster)}] 가 판의 [{string.Join(',', roster)}] 와 다르다 — 다시 학습하라");
        }

        Check(def.Policy, obs, actions, source, "policy");
        Check(def.Value, obs, 1, source, "value");
        return new PolicyNet(def.Policy, def.Value);
    }

    /// <summary>순전파 — 칸마다 로짓 · 가치.</summary>
    public (double[] Logits, double Value) Forward(IReadOnlyList<double> x)
    {
        ArgumentNullException.ThrowIfNull(x);
        return (Run(_policy, x), Run(_value, x)[0]);
    }

    private static double[] Run(IReadOnlyList<LayerDef> layers, IReadOnlyList<double> input)
    {
        IReadOnlyList<double> h = input;
        for (int l = 0; l < layers.Count; l++)
        {
            LayerDef layer = layers[l];
            var o = new double[layer.B.Count];
            for (int j = 0; j < o.Length; j++)
            {
                double sum = layer.B[j];
                IReadOnlyList<double> row = layer.W[j];
                for (int i = 0; i < row.Count; i++)
                {
                    sum += row[i] * h[i];
                }

                o[j] = l < layers.Count - 1 ? Math.Max(0, sum) : sum;
            }

            h = o;
        }

        return (double[])h;
    }

    private static void Check(IReadOnlyList<LayerDef> layers, int input, int output, string source, string name)
    {
        if (layers.Count == 0)
        {
            throw new DataException($"{source}: {name} 에 층이 없다");
        }

        int width = input;
        for (int l = 0; l < layers.Count; l++)
        {
            LayerDef layer = layers[l];
            if (layer.W.Count != layer.B.Count || System.Linq.Enumerable.Any(layer.W, row => row.Count != width))
            {
                throw new DataException($"{source}: {name}[{l}] 의 모양이 틀렸다 — 입력 {width} 을 받아 b 의 길이({layer.B.Count})만큼 내야 한다");
            }

            width = layer.B.Count;
        }

        if (width != output)
        {
            throw new DataException($"{source}: {name} 의 끝이 {width} 칸이다 — {output} 칸이어야 한다");
        }
    }
}
