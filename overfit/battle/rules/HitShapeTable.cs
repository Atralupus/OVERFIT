using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// <c>hitboxes.json</c> 의 한 칸. <b>도구(<c>tools/extract_hitboxes.py</c>)가 쓰고 사람은 안 고친다</b> —
/// 판정 모양은 그림에서 뽑는 것이지 손으로 적는 것이 아니다 (이슈 #59 · 설계 §3).
/// </summary>
public sealed class HitShapeDef
{
    /// <summary>그림의 배율. <c>balance.json</c> 의 <c>feel.*_sprite_scale</c> 과 같아야 한다 — 테스트가 본다.</summary>
    public required double Scale { get; init; }

    /// <summary><c>.tres</c> 의 region — [x, y, 폭, 높이]. 어느 픽셀에서 뽑았는지를 남긴다.</summary>
    public required IReadOnlyList<double> Region { get; init; }

    /// <summary>공격자 기준 사각형들 — 각각 [x0, x1, y0, y1] (월드 px).</summary>
    public required IReadOnlyList<IReadOnlyList<double>> Rects { get; init; }
}

/// <summary><c>hitboxes.json</c> → 판정 모양. 문제는 <b>전부</b> 모아 한 번에 던진다 — 부팅이 빠진 키를 전부 나열하는 것과 같은 규약이다.</summary>
public static class HitShapeTable
{
    public static Dictionary<string, HitShape> Parse(string json, string source)
    {
        Dictionary<string, HitShapeDef> defs = JsonData<HitShapeDef>.ParseTable(json, source);
        var shapes = new Dictionary<string, HitShape>(defs.Count);
        var problems = new List<string>();

        foreach ((string id, HitShapeDef def) in defs)
        {
            var rects = new List<HitRect>(def.Rects.Count);
            bool broken = false;
            foreach (IReadOnlyList<double> r in def.Rects)
            {
                if (r.Count != 4)
                {
                    problems.Add($"{id}: 사각형은 [x0, x1, y0, y1] 넷이어야 한다 — {r.Count}개");
                    broken = true;
                    continue;
                }

                rects.Add(new HitRect(r[0], r[1], r[2], r[3]));
            }

            if (broken)
            {
                continue;
            }

            if (rects.Count == 0)
            {
                problems.Add($"{id}: 사각형이 없다 — tools/extract_hitboxes.py 를 돌렸나");
                continue;
            }

            try
            {
                shapes[id] = new HitShape(rects);
            }
            catch (ArgumentException e)
            {
                problems.Add($"{id}: {e.Message}");
            }
        }

        if (problems.Count > 0)
        {
            throw new DataException($"{source}: {string.Join(" / ", problems)}");
        }

        return shapes;
    }
}
