using System;
using System.Collections.Generic;
using System.Globalization;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>한 단계가 쓰는 패턴 명부와 고르기. <c>data/stages.json</c> 의 값 하나다.</summary>
public sealed class StageDef
{
    /// <summary>설계가 이 단계에 요구하는 패턴 수 — 지금 두 단계 모두 2 다(#72 · 설계 §4). 명부가 이보다 짧으면 <c>[W] short</c>.</summary>
    public required int Want { get; set; }

    /// <summary>실제로 쓰는 패턴 id 들. <b>이 순서가 계약이다</b> — 뽑기 좌표가 여기 인덱스다.</summary>
    public required IReadOnlyList<string> Patterns { get; set; }

    /// <summary>
    /// 이 단계의 패턴 고르기 — 등록표(<see cref="PatternPickers"/>)의 id (#72 · 설계 §4.4). <b>required</b> 다: 빠진 채로
    /// 읽히면 어느 고르기로 돌지를 코드의 기본값이 조용히 정한다. 모르는 id 는 데이터 테스트가 막는다.
    /// </summary>
    public required string Picker { get; set; }
}

/// <summary>
/// 한 판의 보스 쪽 재료 — 단계의 명부와 그 위에 세운 고르기 (#72 · 설계 §4.4). <see cref="StageRoster.Setup"/> 이 짓는다.
/// </summary>
/// <param name="PatternIds">명부 — <c>BattleSetup.PatternIds</c>.</param>
/// <param name="PickerId">고르기 id — 로그의 <c>picker=</c>.</param>
/// <param name="Picker">그 시도의 시드와 기록으로 세운 고르기 — <c>BattleSetup.Picker</c>.</param>
public sealed record StageSetup(IReadOnlyList<string> PatternIds, string PickerId, IPatternPicker Picker);

/// <summary>
/// 단계 번호 → 그 단계가 쓰는 패턴 id 목록과 고르기.
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
    /// <paramref name="stage"/> 단계의 명부와 고르기를 세운다 (#72 · 설계 §4.4). <b>게임(<c>Battle</c>)과 데모(<c>BattleDemo</c>)가
    /// 이 한 자리에서 세운다</b> — 따로 세우면 로그의 <c>seed=</c> 를 데모에 넘겨도 다른 고르기로 돌 수 있다. 고르기는 시도를
    /// 시작할 때 그때까지의 기록으로 한 번 세운다 — 판 도중에는 안 바뀐다. 단계를 못 찾거나(<see cref="Resolve"/> 가 <c>[E]</c> 를
    /// 남겼다) 고르기가 등록표에 없으면 null 이다.
    /// </summary>
    /// <param name="stages"><c>stages.json</c>.</param>
    /// <param name="stage">단계.</param>
    /// <param name="seed">시도 시드.</param>
    /// <param name="history">그때까지 끝난 시도들 — 데모는 빈 목록을 넘긴다(기록 없이 시드만으로 선다).</param>
    public static StageSetup? Setup(
        IReadOnlyDictionary<string, StageDef> stages, int stage, ulong seed, IReadOnlyList<AttemptRecord> history)
    {
        if (Resolve(stages, stage) is not { } def)
        {
            return null;
        }

        IPatternPicker? picker = PatternPickers.Create(def.Picker, new PickerInputs(def.Patterns, history, seed, stage));
        if (picker is null)
        {
            Log.Error("stage", $"picker_missing id={def.Picker} stage={stage}");
            return null;
        }

        return new StageSetup(def.Patterns, def.Picker, picker);
    }

    /// <summary>
    /// <paramref name="stage"/> 단계의 패턴 id 들 — <see cref="Resolve"/> 가 찾은 단계의 명부. 못 찾으면 빈 목록이다.
    /// </summary>
    public static IReadOnlyList<string> For(IReadOnlyDictionary<string, StageDef> stages, int stage) =>
        Resolve(stages, stage)?.Patterns ?? Array.Empty<string>();

    /// <summary>
    /// <paramref name="stage"/> 단계의 정의. 정의된 범위를 벗어난 단계는 <b>가장 가까운 단계로 잘라</b>
    /// 쓰고 경고를 남긴다 — 여기서 <c>[E]</c> 를 내면 헤드리스 판정이 실패로 보는데,
    /// "없는 단계를 달라고 했다" 는 데이터 손상이 아니라 호출자의 범위 문제다. 전투는 명부와 고르기를 이 한 정의에서
    /// 읽는다(#72) — 따로 찾으면 잘린 단계의 명부와 물은 단계의 고르기가 갈린다. 못 찾으면(구멍 · 빈 명부) null 이다.
    /// </summary>
    public static StageDef? Resolve(IReadOnlyDictionary<string, StageDef> stages, int stage)
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
            return null;
        }

        int picked = Math.Clamp(stage, lowest, highest);
        if (picked != stage)
        {
            Log.Warn("stage", $"out_of_range asked={stage} used={picked} defined={lowest}..{highest}");
        }

        // 색인이 아니라 조회다. 범위를 맞췄다고 그 키가 있는 것은 아니다 —
        // stages.json 의 키가 연속이라는 것은 **어디에도 적히지 않은 가정**이고, 구멍이 하나만
        // 생겨도 색인은 KeyNotFoundException 이었다. 예외는 우리 로그 형식으로 안 찍혀
        // [E] 게이트를 그냥 지나가고 엔진 ERROR 블록으로만 나온다.
        if (!stages.TryGetValue(picked.ToString(CultureInfo.InvariantCulture), out StageDef? def))
        {
            Log.Error("stage", $"stage_missing stage={picked} defined={lowest}..{highest}");
            return null;
        }

        // 빈 명부는 경고가 아니라 거절이다. 전에는 short 경고만 내고 빈 목록을 그대로 돌려줬는데,
        // 그것을 받은 BattleSim.Begin 이 Det.RollInt(n: 0) 으로 터졌다 — 데이터가 깨진 것을
        // **쓰는 자리**에서 알게 되면 원인이 로그에 안 남는다.
        if (def.Patterns.Count == 0)
        {
            Log.Error("stage", $"empty_roster stage={picked} want={def.Want}");
            return null;
        }

        // 모자람을 조용히 삼키지 않는다. 명부가 설계의 수보다 짧은 단계를 로그에 안 남기면 나중에
        // "단계가 올라도 왜 안 어려워지지" 를 데이터에서 찾을 수 없다.
        if (def.Patterns.Count < def.Want)
        {
            Log.Warn("stage", $"short stage={picked} want={def.Want} have={def.Patterns.Count}");
        }

        Log.Info("stage", () => $"roster stage={picked} patterns={string.Join(',', def.Patterns)} picker={def.Picker}");
        return def;
    }
}
