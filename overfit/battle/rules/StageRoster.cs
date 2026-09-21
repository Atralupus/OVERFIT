using System;
using System.Collections.Generic;
using System.Globalization;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>한 단계가 쓰는 패턴 명부. <c>data/stages.json</c> 의 값 하나다.</summary>
public sealed class StageDef
{
    /// <summary>설계 문서가 이 단계에 요구하는 패턴 수 (2 · 3 · 5 · 7 · 10).</summary>
    public required int Want { get; set; }

    /// <summary>실제로 쓰는 패턴 id 들. <b>이 순서가 계약이다</b> — 뽑기 좌표가 여기 인덱스다.</summary>
    public required IReadOnlyList<string> Patterns { get; set; }
}

/// <summary>
/// 단계 번호 → 그 단계가 쓰는 패턴 id 목록.
///
/// <para>
/// 전에는 <c>patterns.json</c> 의 키 순서에서 앞 N 개를 잘라 썼다. 그러면 패턴 하나를
/// 파일 맨 위에 끼워 넣는 것만으로 1단계가 <b>다른 전투</b>가 되고, 앞서 만든 리플레이와
/// 학습 데이터가 전부 조용히 달라진다 — 골든은 id 를 직접 적어 두어서 여전히 초록이다.
/// 그래서 명부를 데이터에 적어 두고 여기서 읽기만 한다.
/// </para>
/// </summary>
public static class StageRoster
{
    /// <summary>
    /// <paramref name="stage"/> 단계의 패턴 id 들. 정의된 범위를 벗어난 단계는 <b>가장 가까운 단계로 잘라</b>
    /// 쓰고 경고를 남긴다 — 여기서 <c>[E]</c> 를 내면 헤드리스 판정이 실패로 보는데,
    /// "없는 단계를 달라고 했다" 는 데이터 손상이 아니라 호출자의 범위 문제다.
    /// </summary>
    public static IReadOnlyList<string> For(IReadOnlyDictionary<string, StageDef> stages, int stage)
    {
        ArgumentNullException.ThrowIfNull(stages);

        int lowest = int.MaxValue, highest = int.MinValue;
        foreach (string key in stages.Keys)
        {
            if (!int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                continue;
            }

            lowest = Math.Min(lowest, n);
            highest = Math.Max(highest, n);
        }

        if (lowest > highest)
        {
            Log.Error("stage", "no_stages_defined");
            return Array.Empty<string>();
        }

        int picked = Math.Clamp(stage, lowest, highest);
        if (picked != stage)
        {
            Log.Warn("stage", $"out_of_range asked={stage} used={picked} defined={lowest}..{highest}");
        }

        StageDef def = stages[picked.ToString(CultureInfo.InvariantCulture)];

        // 모자람을 조용히 삼키지 않는다. 프로토타입은 패턴이 여섯뿐이라 4·5단계가 설계보다 짧은데,
        // 그걸 로그에 안 남기면 나중에 "단계가 올라도 왜 안 어려워지지" 를 데이터에서 찾을 수 없다.
        if (def.Patterns.Count < def.Want)
        {
            Log.Warn("stage", $"short stage={picked} want={def.Want} have={def.Patterns.Count}");
        }

        Log.Info("stage", () => $"roster stage={picked} patterns={string.Join(',', def.Patterns)}");
        return def.Patterns;
    }
}
