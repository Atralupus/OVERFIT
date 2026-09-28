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
/// <param name="PickerId">고르기 id — 로그의 <c>picker=</c>. 단계의 id 그대로다(동전이 무작위 갈래를 골라도 <c>network</c>).</param>
/// <param name="Picker">그 시도의 시드와 기록으로 세운 고르기 — <c>BattleSetup.Picker</c>.</param>
/// <param name="Stage">실제로 싸우는 단계 — 범위 밖을 물으면 가장 가까운 단계로 잘라 쓴 값이다(#112 · 설계 2026-09-28 §6.5). 기록이 이것을 싣는다.</param>
/// <param name="Arm">
/// 동전의 갈래 — <c>network</c> · <c>uniform</c> (#112 · 설계 §6.3). 고르기 id 가 <c>network</c> 가 아닌 단계는 갈래가 없다(null).
/// </param>
/// <param name="Decision">망 갈래면 망 고르기의 결정(좁힌 명부 · 숨통 · 로짓 · 들어 올림 · 까닭) — 로그와 기록이 싣는다.</param>
public sealed record StageSetup(
    IReadOnlyList<string> PatternIds, string PickerId, IPatternPicker Picker, int Stage, string? Arm = null, PickDecision? Decision = null);

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
    /// <summary>동전의 무작위 갈래 — 등록표의 <c>uniform</c> 고르기로 선다.</summary>
    public const string UniformArm = "uniform";

    /// <summary>
    /// <paramref name="stage"/> 단계의 명부와 고르기를 세운다 (#72 · 설계 §4.4). <b>게임(<c>Battle</c>)과 데모(<c>BattleDemo</c>)가
    /// 이 한 자리에서 세운다</b> — 따로 세우면 로그의 <c>seed=</c> 를 데모에 넘겨도 다른 고르기로 돌 수 있다. 고르기는 시도를
    /// 시작할 때 그때까지의 기록으로 한 번 세운다 — 판 도중에는 안 바뀐다. 단계를 못 찾거나(<see cref="Resolve(IReadOnlyDictionary{string, StageDef}, int)"/> 가 <c>[E]</c> 를
    /// 남겼다) 고르기가 등록표에 없거나 대본이 명부 밖이면(<c>script</c> 고르기에 대본이 없는 것도) <c>[E]</c> 를 남기고 null 이다 — <b>던지지
    /// 않는다</b>: <c>Battle</c> 은 null 을 받아 판을 깨진 채로 멈추지만, 예외는 <c>_Ready</c> 를 빠져나가 반쯤 선 노드를 남긴다.
    /// </summary>
    /// <param name="stages"><c>stages.json</c>.</param>
    /// <param name="stage">단계.</param>
    /// <param name="seed">시도 시드.</param>
    /// <param name="history">그때까지 끝난 시도들 — 데모는 빈 목록을 넘긴다(기록 없이 시드만으로 선다).</param>
    /// <param name="script">
    /// 대본 — 있으면 이 전투만 단계의 <c>picker</c> 대신 <c>script</c> 로 선다 (#78 · 설계 §4.4 「대본이 전투에 닿는 길」). <c>Game</c> 의 다음
    /// 전투 한 칸이 GIF 러너 · 스크린샷에게서 받아 넘긴다. 명부는 그대로 그 단계의 것이다 — 대본은 명부 안의 순서만 정한다.
    /// </param>
    /// <param name="network">
    /// 망과 고르기의 수치 (#112) — 단계의 고르기가 <c>network</c> 면 필요하다. 없으면 <c>[E] network_missing</c> 을 남기고 판을 안 세운다. 게임과 데모가
    /// 부팅 때 읽은 것을 넘긴다.
    /// </param>
    /// <param name="arm">
    /// 동전 대신 정한 갈래 — <c>network</c> · <c>uniform</c> (#114 · 설계 2026-09-28 §7.1). 평가가 같은 시도를 두 갈래로 한 번씩 돌 때만 넘긴다 — 게임과
    /// 데모는 안 넘긴다(동전). 고르기가 <c>network</c> 가 아닌 단계에서는 안 쓴다. 모르는 갈래는 <c>[E] arm_unknown</c> 과 null 이다.
    /// </param>
    public static StageSetup? Setup(
        IReadOnlyDictionary<string, StageDef> stages, int stage, ulong seed, IReadOnlyList<AttemptRecord> history,
        IReadOnlyList<string>? script = null, NetworkContext? network = null, string? arm = null)
    {
        if (Resolve(stages, stage, out int used) is not { } def)
        {
            return null;
        }

        string id = script is null ? def.Picker : "script";

        // 반반의 동전 (설계 2026-09-28 §6.3). 단계의 고르기가 망이면 시도마다 망 · 무작위 갈래를 정한다 — 같은 사람에게 망 보스와 무작위 보스를
        // 붙여 보는 것 말고 망이 일하는지 증명할 길이 없다. 동전은 PickerArm 스트림이라 뽑기(PatternPick)를 안 민다: 무작위 갈래는 uniform 과
        // 같은 판이다. 정수 퍼센트와 견준다 — 사칙연산과 비교뿐이다(§8). 갈래를 정해 받으면(평가 · §7.1) 동전을 안 던진다 — 평가가 여기서
        // 판을 세워야 게임과 다른 길로 망 갈래를 세우지 않는다.
        string? chosen = null;
        string build = id;
        if (id == NetworkPicker.Id)
        {
            if (network is null)
            {
                Log.Error("stage", $"network_missing stage={used}");
                return null;
            }

            if (arm is not (null or NetworkPicker.Id or UniformArm))
            {
                Log.Error("stage", $"arm_unknown arm={arm} stage={used}");
                return null;
            }

            chosen = arm ?? (Det.RollInt(seed, Det.Domain.PickerArm, 100) < network.Knobs.NetworkSharePercent ? NetworkPicker.Id : UniformArm);
            build = chosen;
        }

        IPatternPicker? picker;
        try
        {
            picker = PatternPickers.Create(build, new PickerInputs(def.Patterns, history, seed, used, script, network));
        }
        catch (ArgumentException e)
        {
            // 대본이 명부 밖이거나(ScriptPicker) script 고르기에 대본이 없거나, 망의 머리가 명부와 다르면(NetworkPicker) 세울 때 던진다. 여기서
            // 받지 않으면 예외가 Battle._Ready 를 빠져나가고, Godot 은 찍기만 하고 노드를 그대로 둔다 — _broken 은 거짓 · _sim 은 null 인 채로 매
            // 프레임 NRE 가 나 이 한 줄이 그 밑에 묻혔다(#78 T6-I1). 다른 실패와 같이 [E] 를 남기고 null — Battle 이 판을 깨진 채로 멈춘다.
            Log.Error("stage", build == "script"
                ? $"script_rejected stage={used} reason={e.Message}"
                : $"picker_rejected id={build} stage={used} reason={e.Message}");
            return null;
        }

        if (picker is null)
        {
            Log.Error("stage", $"picker_missing id={build} stage={used}");
            return null;
        }

        return new StageSetup(def.Patterns, id, picker, used, chosen, (picker as NetworkPicker)?.Decision);
    }

    /// <summary>
    /// <paramref name="stage"/> 단계의 패턴 id 들 — <see cref="Resolve(IReadOnlyDictionary{string, StageDef}, int)"/> 가 찾은 단계의 명부. 못 찾으면 빈 목록이다.
    /// </summary>
    public static IReadOnlyList<string> For(IReadOnlyDictionary<string, StageDef> stages, int stage) =>
        Resolve(stages, stage)?.Patterns ?? Array.Empty<string>();

    /// <summary>
    /// <paramref name="stage"/> 단계의 정의. 정의된 범위를 벗어난 단계는 <b>가장 가까운 단계로 잘라</b>
    /// 쓰고 경고를 남긴다 — 여기서 <c>[E]</c> 를 내면 헤드리스 판정이 실패로 보는데,
    /// "없는 단계를 달라고 했다" 는 데이터 손상이 아니라 호출자의 범위 문제다. 전투는 명부와 고르기를 이 한 정의에서
    /// 읽는다(#72) — 따로 찾으면 잘린 단계의 명부와 물은 단계의 고르기가 갈린다. 못 찾으면(구멍 · 빈 명부) null 이다.
    /// </summary>
    public static StageDef? Resolve(IReadOnlyDictionary<string, StageDef> stages, int stage) => Resolve(stages, stage, out _);

    /// <summary><see cref="Resolve(IReadOnlyDictionary{string, StageDef}, int)"/> 에 잘라 쓴 단계(<paramref name="used"/>)를 더한다 — 기록이 싣는 값이다.</summary>
    public static StageDef? Resolve(IReadOnlyDictionary<string, StageDef> stages, int stage, out int used)
    {
        ArgumentNullException.ThrowIfNull(stages);
        used = stage;

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
        used = picked;
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
