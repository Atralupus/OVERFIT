using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// Q 표가 판을 자르는 자 (설계 2026-10-01 조각8 §1.1) — 관측의 단위 그대로(거리는 아레나 폭으로 · 높이는 300 으로 나눈 값). 표 JSON 이 같이 싣는다:
/// 자가 바뀐 표를 다른 자로 읽으면 다른 칸을 본다.
/// </summary>
/// <param name="DistanceEdges">파이터와의 거리 |dx|/W 의 칸 경계, 오름차순 — 칸 수는 길이 + 1.</param>
/// <param name="Air">높이(관측 단위)가 이보다 크면 떠 있다.</param>
public sealed record QTableShape(IReadOnlyList<double> DistanceEdges, double Air)
{
    public bool Equals(QTableShape? other) =>
        other is not null && Air.Equals(other.Air) && System.Linq.Enumerable.SequenceEqual(DistanceEdges, other.DistanceEdges);

    public override int GetHashCode() => HashCode.Combine(Air, DistanceEdges.Count);
}

/// <summary>배우기의 한 줄 — 칸 키 · 고른 칸 · 보상 · 다음 결정까지의 틱.</summary>
public readonly record struct QSample(string Key, int Action, double Reward, int Span);

/// <summary>
/// Q 표 — 망 없는 강화학습의 대조군 (설계 2026-10-01 조각8 §1). 판을 몇 칸(<see cref="Key"/>)으로 잘라 칸 × 행동마다 할인 보상의 평균을 든다.
///
/// <para>
/// <b>방문 수를 같이 든다</b> — 안 배운 행동의 Q 는 0 인데, 보상이 대개 음수(시간 벌 · 맞은 피해)라 0 이 가장 좋아 보인다. 그러면 표가 늘 안 해 본 것을 골라
/// 배운 것이 고르기에 안 닿는다. 배운 행동만 견주고, 하나도 안 배운 칸은 열린 칸에서 무작위다.
/// </para>
/// </summary>
public sealed class QTable
{
    private readonly int _actions;
    private readonly SortedDictionary<string, (double[] Q, int[] N)> _rows = new(StringComparer.Ordinal);

    /// <param name="shape">자르는 자.</param>
    /// <param name="actions">칸 수 — <see cref="BossActions.Count"/>.</param>
    public QTable(QTableShape shape, int actions)
    {
        ArgumentNullException.ThrowIfNull(shape);
        Shape = shape;
        _actions = actions;
    }

    public QTableShape Shape { get; }

    /// <summary>배운 칸 수.</summary>
    public int States => _rows.Count;

    /// <summary>
    /// 관측을 칸 키로 — <c>지점|거리|떠 있나|파이터 행동|지금 동작(0 = 없음)|형태</c>. 관측(망의 입력)에서 자르는 것은 망과 같은 것을 보고 출발했다는 것을 지키려서다.
    /// </summary>
    public static string Key(IReadOnlyList<double> obs, BossObservation layout, QTableShape shape)
    {
        ArgumentNullException.ThrowIfNull(obs);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(shape);
        int point = ArgMax(obs, 0, BossObservation.PointCount);
        int form = ArgMax(obs, BossObservation.FormOffset, 3) + 1;
        int move = ArgMax(obs, BossObservation.MoveOffset, layout.FighterOffset - BossObservation.MoveOffset - 2);
        int f = layout.FighterOffset;
        double dist = Math.Abs(obs[f]);
        int bin = 0;
        while (bin < shape.DistanceEdges.Count && dist >= shape.DistanceEdges[bin])
        {
            bin++;
        }

        int air = obs[f + 1] > shape.Air ? 1 : 0;
        int action = ArgMax(obs, f + 8, BossObservation.FighterActionCount);
        return string.Create(CultureInfo.InvariantCulture, $"{point}|{bin}|{air}|{action}|{move}|{form}");
    }

    /// <summary>
    /// 칸 하나 — 배운 칸이면 배운 열린 행동 중 Q 가 가장 큰 것(같으면 앞), 아니면 열린 칸에서 무작위. <paramref name="epsilon"/> 의 확률로 늘 무작위다(학습의 탐색).
    /// 난수는 <c>Det</c> 의 <c>BossControl</c> 에 결정 번호 · k2 갈래(1 = 탐색의 동전 · 2 = 무작위 칸)다.
    /// </summary>
    public int Pick(string key, IReadOnlyList<bool> mask, ulong seed, int number, double epsilon)
    {
        ArgumentNullException.ThrowIfNull(mask);
        int best = -1;
        bool explore = epsilon > 0 && Det.Roll01(seed, Det.Domain.BossControl, k1: number, k2: 1) < epsilon;
        if (!explore && _rows.TryGetValue(key, out (double[] Q, int[] N) row))
        {
            for (int a = 0; a < mask.Count; a++)
            {
                if (mask[a] && row.N[a] > 0 && (best < 0 || row.Q[a] > row.Q[best]))
                {
                    best = a;
                }
            }
        }

        if (best >= 0)
        {
            return best;
        }

        int open = 0;
        foreach (bool m in mask)
        {
            open += m ? 1 : 0;
        }

        int nth = Det.RollInt(seed, Det.Domain.BossControl, open, k1: number, k2: 2);
        for (int a = 0; a < mask.Count; a++)
        {
            if (mask[a] && nth-- == 0)
            {
                return a;
            }
        }

        throw new ArgumentException("열린 칸이 없다", nameof(mask));
    }

    /// <summary>
    /// 판 하나로 배운다 — 몬테카를로: 결정 i 의 할인 보상 <c>G_i = r_i + γ^(span_i / decideTicks) · G_{i+1}</c>(PPO 와 같은 시간 할인 · <c>ml/rl/ppo.py</c>)로
    /// <c>Q += α (G − Q)</c>.
    /// </summary>
    public void Learn(IReadOnlyList<QSample> episode, double gamma, int decideTicks, double alpha)
    {
        ArgumentNullException.ThrowIfNull(episode);
        double g = 0;
        for (int i = episode.Count - 1; i >= 0; i--)
        {
            QSample s = episode[i];
            g = s.Reward + (i + 1 < episode.Count ? Math.Pow(gamma, (double)s.Span / decideTicks) * g : 0);
            (double[] q, int[] n) = Row(s.Key);
            q[s.Action] += alpha * (g - q[s.Action]);
            n[s.Action]++;
        }
    }

    /// <summary>테스트 · 읽기가 칸 하나를 적는다.</summary>
    public void Set(string key, int action, double q, int visits)
    {
        (double[] qs, int[] ns) = Row(key);
        qs[action] = q;
        ns[action] = visits;
    }

    public double Q(string key, int action) => _rows.TryGetValue(key, out (double[] Q, int[] N) r) ? r.Q[action] : 0;

    public int Visits(string key, int action) => _rows.TryGetValue(key, out (double[] Q, int[] N) r) ? r.N[action] : 0;

    /// <summary>JSON — 키 순이라 같은 표는 같은 바이트다.</summary>
    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"distance_edges\": [")
            .AppendJoin(", ", System.Linq.Enumerable.Select(Shape.DistanceEdges, d => d.ToString("R", CultureInfo.InvariantCulture)))
            .Append("], \"air\": ").Append(Shape.Air.ToString("R", CultureInfo.InvariantCulture))
            .Append(", \"actions\": ").Append(_actions.ToString(CultureInfo.InvariantCulture))
            .Append(", \"states\": {");
        bool first = true;
        foreach ((string key, (double[] q, int[] n)) in _rows)
        {
            sb.Append(first ? "\n" : ",\n").Append("  ").Append(JsonSerializer.Serialize(key)).Append(": {\"q\": [")
                .AppendJoin(", ", System.Linq.Enumerable.Select(q, v => v.ToString("R", CultureInfo.InvariantCulture)))
                .Append("], \"n\": [").AppendJoin(", ", n).Append("]}");
            first = false;
        }

        return sb.Append("\n}}\n").ToString();
    }

    /// <summary>JSON 을 읽는다 — 칸 수가 판과 다르면 <see cref="DataException"/>.</summary>
    public static QTable Parse(string json, string source, int actions)
    {
        QTableDef def = JsonData<QTableDef>.ParseOne(json, source);
        if (def.Actions != actions)
        {
            throw new DataException($"{source}: 표의 actions={def.Actions} 가 판의 {actions} 와 다르다 — 다시 학습하라");
        }

        var t = new QTable(new QTableShape(def.DistanceEdges, def.Air), actions);
        foreach ((string key, QRowDef row) in def.States)
        {
            if (row.Q.Count != actions || row.N.Count != actions)
            {
                throw new DataException($"{source}: 칸 {key} 의 길이가 {actions} 가 아니다");
            }

            for (int a = 0; a < actions; a++)
            {
                t.Set(key, a, row.Q[a], row.N[a]);
            }
        }

        return t;
    }

    private (double[] Q, int[] N) Row(string key)
    {
        if (!_rows.TryGetValue(key, out (double[] Q, int[] N) row))
        {
            row = (new double[_actions], new int[_actions]);
            _rows[key] = row;
        }

        return row;
    }

    private static int ArgMax(IReadOnlyList<double> v, int from, int count)
    {
        int best = 0;
        for (int i = 1; i < count; i++)
        {
            if (v[from + i] > v[from + best])
            {
                best = i;
            }
        }

        return best;
    }
}

/// <summary>Q 표 JSON 의 모양.</summary>
public sealed class QTableDef
{
    public required IReadOnlyList<double> DistanceEdges { get; init; }

    public required double Air { get; init; }

    public required int Actions { get; init; }

    public required IReadOnlyDictionary<string, QRowDef> States { get; init; }
}

/// <summary>Q 표의 칸 하나 — 행동마다의 Q 와 방문 수.</summary>
public sealed class QRowDef
{
    public required IReadOnlyList<double> Q { get; init; }

    public required IReadOnlyList<int> N { get; init; }
}
