using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 다음 패턴을 고른다 (#72 · 설계 §4.4). <c>BattleSim.Begin</c> 이 부르는 자리 하나다. 몇 번째로 뽑는지(<c>draw</c>)는
/// <c>BattleSim</c> 이 세고, 고르기는 그 번호로 좌표를 조회하므로 상태가 없어도 된다.
/// </summary>
public interface IPatternPicker
{
    /// <summary>명부의 칸 번호 — <c>[0, 명부 수)</c>. 밖이면 <c>BattleSim</c> 이 <c>[E]</c> 를 남기고 쉬었다 다시 고른다.</summary>
    int Pick(int draw);
}

/// <summary>
/// 고르기를 세우는 재료 (설계 §4.4) — 시도를 시작할 때 그때까지의 기록으로 <b>한 번</b> 세운다. 판 도중에는 안 바뀐다.
/// </summary>
/// <param name="Roster">그 단계의 명부 — <c>stages.json</c> 의 <c>patterns</c>. 칸 번호가 이 인덱스다.</param>
/// <param name="History">그때까지 끝난 시도들. 지금의 두 고르기는 안 읽는다 — 옛 망(태그 <c>v0.9.2</c>)이 읽던 자리이고, 조각 3 의 기억이 다시 채운다.</param>
/// <param name="Seed">시도 시드.</param>
/// <param name="Stage">단계.</param>
/// <param name="Script">대본 — 패턴 id 의 순서. <c>script</c> 고르기(#78)만 읽는다. 대본을 넘긴 전투(<c>Game</c> 의 대본 칸)가 아니면 null 이다.</param>
public sealed record PickerInputs(
    IReadOnlyList<string> Roster, IReadOnlyList<AttemptRecord> History, ulong Seed, int Stage, IReadOnlyList<string>? Script = null);

/// <summary>
/// 무작위 — 시드 위의 <c>Det.RollInt(시드, PatternPick, 명부 수, k1: draw)</c>. 옛 <c>BattleSim.Begin</c> 의 한 줄과 <b>비트까지
/// 같다</b>: 인터페이스를 끼우는 것만으로는 순서가 안 움직인다. 예측 망이 들어오면(조각 4) 이것이 <b>대조군</b>이다 — 같은 사람에게 무작위
/// 보스와 망 보스를 붙여 비교하는 것 말고 망이 일하는지 증명할 길이 없다.
/// </summary>
public sealed class UniformPicker : IPatternPicker
{
    private readonly ulong _seed;
    private readonly int _count;

    public UniformPicker(ulong seed, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        _seed = seed;
        _count = count;
    }

    public int Pick(int draw) => Det.RollInt(_seed, Det.Domain.PatternPick, _count, k1: draw);
}

/// <summary>
/// 대본 — 정한 순서(<see cref="PickerInputs.Script"/>)를 돌고, 끝나면 처음부터 다시 돈다 (#78 · 설계 §4.4). GIF 도구와 스크린샷이 패턴을
/// 고정하는 데 쓴다 — 무엇이 올지 알아야 "그 패턴이 왔을 때 그 사람이 어떻게 되는가" 를 찍는다. <b>데이터의 단계에는 안 쓴다</b>
/// (<c>StageRosterTests</c> 가 막는다): 전투에 닿는 길은 <c>Game</c> 의 다음 전투 한 칸뿐이다.
///
/// <para>
/// 명부에 없는 id 는 <b>세울 때</b> 거절한다 — 빠진 것을 전부 싣는다. 대본은 사람이 손으로 쓰는 것이라 틀리면 그 판을 세우는 자리에서 바로
/// 멈춰야 한다(판 도중에 명부 밖을 내면 <c>BattleSim</c> 은 <c>[E]</c> 를 남기며 간격마다 다시 고를 뿐이다). 이 예외는 <c>StageRoster.Setup</c> 이
/// 받아 <c>[E] script_rejected</c> 로 바꾸고 판을 세우지 않는다 — <c>Battle</c> 은 깨진 채로 멈춘다. uniform 과 같이 상태가 없다 — 몇 번째로
/// 뽑는지(<c>draw</c>)를 받아 조회만 한다.
/// </para>
/// </summary>
public sealed class ScriptPicker : IPatternPicker
{
    /// <summary>대본의 칸마다 명부의 칸 번호 — 세울 때 한 번 찾는다.</summary>
    private readonly int[] _order;

    public ScriptPicker(IReadOnlyList<string> roster, IReadOnlyList<string> script)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(script);
        if (script.Count == 0)
        {
            throw new ArgumentException("대본이 비었다 — 고를 패턴이 없다", nameof(script));
        }

        _order = new int[script.Count];
        var missing = new List<string>();
        for (int i = 0; i < script.Count; i++)
        {
            _order[i] = IndexOf(roster, script[i]);
            if (_order[i] < 0)
            {
                missing.Add(script[i]);
            }
        }

        if (missing.Count > 0)
        {
            throw new ArgumentException($"대본의 패턴이 명부에 없다 — {string.Join(", ", missing)}", nameof(script));
        }
    }

    public int Pick(int draw) => _order[draw % _order.Length];

    private static int IndexOf(IReadOnlyList<string> roster, string id)
    {
        for (int k = 0; k < roster.Count; k++)
        {
            if (string.Equals(roster[k], id, StringComparison.Ordinal))
            {
                return k;
            }
        }

        return -1;
    }
}

/// <summary>
/// 고르기 등록표 — <c>stages.json</c> 의 <c>picker</c> id → 구현 (CLAUDE.md §2 · 설계 §4.4). 고르기를 하나 더할 때 이 표에
/// 한 줄을 더한다 — <c>BattleSim</c> 은 안 연다. <c>uniform</c>(3번 PR) · <c>script</c>(대본 · #78)가 있다. 옛 망 고르기(<c>network</c> · #112)는
/// 조각 1 에서 걷었다(설계 2026-09-29 조각1 §6) — 명부가 바뀌어 옛 망의 머리가 설 자리가 없다.
/// </summary>
public static class PatternPickers
{
    private static readonly Dictionary<string, Func<PickerInputs, IPatternPicker>> _table = new(StringComparer.Ordinal)
    {
        ["uniform"] = inputs => new UniformPicker(inputs.Seed, inputs.Roster.Count),
        ["script"] = inputs => new ScriptPicker(
            inputs.Roster, inputs.Script ?? throw new ArgumentException("script 고르기에 대본이 없다", nameof(inputs))),
    };

    /// <summary>등록된 id 들 — 데이터 테스트가 <c>stages.json</c> 의 <c>picker</c> 를 여기와 대 본다.</summary>
    public static IReadOnlyCollection<string> Ids => _table.Keys;

    /// <summary>
    /// 고르기 하나를 세운다. 모르는 id 면 null — 부르는 쪽이 <c>[E]</c> 를 남긴다. 재료가 그 고르기에 안 맞으면(대본이 비었거나 명부 밖 ·
    /// <c>script</c> 인데 대본이 없음) <see cref="ArgumentException"/> 을 던진다 — <c>StageRoster.Setup</c> 이 받아 <c>[E] script_rejected</c> 를
    /// 남기고 null 로 바꾼다.
    /// </summary>
    public static IPatternPicker? Create(string id, PickerInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return _table.TryGetValue(id, out Func<PickerInputs, IPatternPicker>? make) ? make(inputs) : null;
    }
}
