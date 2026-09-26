using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 보스 판정을 <b>판을 세울 때 한 번</b> 짓는다 (#72 · 설계 §8.1) — 타임라인의 active 단계마다 모양(그림에서 뽑은 id 또는 바닥 띠)을
/// 찾는다. 러너는 지어 둔 판정을 내기만 하고, 디버그 표시의 "다음 판정" 도 같은 것을 그린다. 점프로 넘을 수 있나는 여기서 안 잰다 —
/// 판정이 아니라 그 판정이 선 자리의 것이라 창이 열릴 때 잰다(<see cref="JumpClearance"/> · #85).
///
/// <para>
/// <b>빠진 모양은 전부 모아 한 번에 거절한다</b> — 칼의 모양(<c>BattleSim.Swords</c>)과 같은 규약이다. 판정이 처음 서는 틱에
/// 찾다 틀리면 판이 한참 돈 뒤라 무엇이 빠졌는지가 스택에 안 남는다.
/// </para>
/// </summary>
public static class BossHits
{
    /// <summary>
    /// 명부의 패턴마다 타임라인 칸별 판정 — active 가 아닌 칸은 null 이다. 명부에 있는데 패턴 표에 없는 id 는 건너뛴다
    /// (그 패턴이 뽑히는 틱에 <c>BattleSim.Begin</c> 이 <c>[E]</c> 를 남긴다).
    /// </summary>
    /// <exception cref="ArgumentException">모양이 없는 판정이 있다 — 빠진 것을 전부 싣는다.</exception>
    public static Dictionary<string, HitBox?[]> Resolve(
        IReadOnlyList<string> roster,
        IReadOnlyDictionary<string, PatternDef> patterns,
        IReadOnlyDictionary<string, HitShape> shapes)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(patterns);

        var hits = new Dictionary<string, HitBox?[]>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (string id in roster)
        {
            if (hits.ContainsKey(id) || !patterns.TryGetValue(id, out PatternDef? def))
            {
                continue;
            }

            hits[id] = Of(id, def, shapes, problems);
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException($"판정 모양을 못 지었다 — {string.Join(", ", problems)}", nameof(shapes));
        }

        return hits;
    }

    /// <summary>패턴 하나의 판정들. 테스트와 러너를 혼자 세우는 자리가 쓴다 — 못 지으면 던진다.</summary>
    public static HitBox?[] Of(PatternDef def, IReadOnlyDictionary<string, HitShape> shapes)
    {
        var problems = new List<string>();
        HitBox?[] hits = Of("-", def, shapes, problems);
        return problems.Count == 0
            ? hits
            : throw new ArgumentException($"판정 모양을 못 지었다 — {string.Join(", ", problems)}", nameof(shapes));
    }

    private static HitBox?[] Of(
        string id, PatternDef def, IReadOnlyDictionary<string, HitShape> shapes, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        var hits = new HitBox?[def.Timeline.Count];
        for (int i = 0; i < hits.Length; i++)
        {
            PatternStep step = def.Timeline[i];
            if (step.Kind != "active")
            {
                continue;
            }

            HitShape? shape = ShapeOf(step, shapes);
            if (shape is null)
            {
                problems.Add($"{id}[{i}] {step.Hitbox ?? "모양 없음"}");
                continue;
            }

            hits[i] = new HitBox(shape, step.Damage, step.ActiveSeconds);
        }

        return hits;
    }

    /// <summary>한 단계의 모양 — 그림에서 뽑은 id 가 있으면 그것, 아니면 띠. 둘 다 없거나 id 가 표에 없으면 null.</summary>
    private static HitShape? ShapeOf(PatternStep step, IReadOnlyDictionary<string, HitShape> shapes)
    {
        if (step.Hitbox is { } hitbox)
        {
            return shapes.TryGetValue(hitbox, out HitShape? shape) ? shape : null;
        }

        return step.Band is { Count: 4 } b ? HitShape.Band(b[0], b[1], b[2], b[3]) : null;
    }
}
