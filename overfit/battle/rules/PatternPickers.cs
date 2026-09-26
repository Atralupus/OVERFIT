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
/// 고르기를 세우는 재료 (설계 §4.4) — 시도를 시작할 때 그때까지의 기록으로 <b>한 번</b> 세운다. 판 도중에는 안 바뀐다
/// (유저: "실시간은 아니고 보스전에 실패할때마다 업데이트").
/// </summary>
/// <param name="Roster">그 단계의 명부 — <c>stages.json</c> 의 <c>patterns</c>. 칸 번호가 이 인덱스다.</param>
/// <param name="History">그때까지 끝난 시도들. <c>uniform</c> 은 안 읽는다 — 망이 읽는다.</param>
/// <param name="Seed">시도 시드.</param>
/// <param name="Stage">단계.</param>
/// <param name="Script">대본 — 패턴 id 의 순서. <c>script</c> 고르기(5번 PR)만 읽는다. 3번 PR 에서는 늘 null 이다.</param>
public sealed record PickerInputs(
    IReadOnlyList<string> Roster, IReadOnlyList<AttemptRecord> History, ulong Seed, int Stage, IReadOnlyList<string>? Script = null);

/// <summary>
/// 무작위 — 시드 위의 <c>Det.RollInt(시드, PatternPick, 명부 수, k1: draw)</c>. 옛 <c>BattleSim.Begin</c> 의 한 줄과 <b>비트까지
/// 같다</b>: 인터페이스를 끼우는 것만으로는 순서가 안 움직인다. 망이 들어오면 이것이 <b>대조군</b>이다 — 같은 사람에게 무작위
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
/// 고르기 등록표 — <c>stages.json</c> 의 <c>picker</c> id → 구현 (CLAUDE.md §2 · 설계 §4.4). 고르기를 하나 더할 때 이 표에
/// 한 줄을 더한다 — <c>BattleSim</c> 은 안 연다. 3번 PR 은 <c>uniform</c> 하나다: <c>script</c>(대본)는 5번 PR, 망은 나중이다.
/// </summary>
public static class PatternPickers
{
    private static readonly Dictionary<string, Func<PickerInputs, IPatternPicker>> _table = new(StringComparer.Ordinal)
    {
        ["uniform"] = inputs => new UniformPicker(inputs.Seed, inputs.Roster.Count),
    };

    /// <summary>등록된 id 들 — 데이터 테스트가 <c>stages.json</c> 의 <c>picker</c> 를 여기와 대 본다.</summary>
    public static IReadOnlyCollection<string> Ids => _table.Keys;

    /// <summary>고르기 하나를 세운다. 모르는 id 면 null — 부르는 쪽이 <c>[E]</c> 를 남긴다.</summary>
    public static IPatternPicker? Create(string id, PickerInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return _table.TryGetValue(id, out Func<PickerInputs, IPatternPicker>? make) ? make(inputs) : null;
    }
}
