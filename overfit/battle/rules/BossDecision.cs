using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 결정 지점 (설계 2026-10-01 조각2 §1) — 판이 조종기에게 묻는 자리. 동작 중(캔슬 지점이 아닐 때) · 탈진 · 전환 · 멈칫 · 반응의 동작에서는 안 묻는다.
/// </summary>
public enum DecisionPoint
{
    /// <summary>보스가 자유로워진 틱 — 동작 끝 · 탈진에 든 틱 · 전환 끝 · 판이 설 때. 열린 칸은 기다리기 하나다(규칙 조종기가 계획을 고르는 자리).</summary>
    Freed,

    /// <summary>쉬는 동안 — 자유로워진 뒤 결정 간격(12틱)마다.</summary>
    Rest,

    /// <summary>다가가는 중 — 결정 간격마다.</summary>
    Approach,

    /// <summary>물러서는 중 — 결정 간격마다(설계 2026-10-01 조각3 §0).</summary>
    Retreat,

    /// <summary>다가가다 닿았거나 상한(3초)을 넘긴 틱 · 물러서다 아레나 끝에 닿은 틱. 다가가기 · 물러서기를 가린다(같은 틱에 또 닿아 끝없이 되묻는다).</summary>
    Arrived,

    /// <summary>러너가 지금 동작의 캔슬 지점에 드는 틱.</summary>
    Cancel,
}

/// <summary>
/// 결정의 칸 배치 (설계 2026-10-01 조각2 §1.1 · 조각3 §0) — <c>[기다리기, 다가가기, 물러서기, 넘어 뛰기, 뒤로 뛰기, 계속하기, 동작 0 … 동작 N−1]</c>.
/// 망의 출력 칸이 이 배치를 따른다(조각 4 가 못박는다). 칸을 숫자로 적지 말고 이 상수로 적는다 — 배치가 바뀌면 숫자가 다른 칸을 가리킨다.
/// </summary>
public sealed class BossActions
{
    public const int Wait = 0;

    public const int Approach = 1;

    public const int Retreat = 2;

    public const int LeapOver = 3;

    public const int LeapBack = 4;

    public const int Continue = 5;

    public const int FirstMove = 6;

    /// <summary>움직임 칸의 이름 — 로그와 대본(<see cref="ScriptActions"/>)이 쓴다.</summary>
    private static readonly string[] _names = ["wait", "approach", "retreat", "leap_over", "leap_back", "continue"];

    private readonly IReadOnlyList<string> _roster;

    /// <param name="roster">명부 — 동작 칸의 순서다.</param>
    public BossActions(IReadOnlyList<string> roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        _roster = roster;
    }

    /// <summary>칸 수 — 움직임 다섯 · 계속하기 · 명부 길이.</summary>
    public int Count => FirstMove + _roster.Count;

    /// <summary>명부의 <paramref name="rosterIndex"/> 칸 동작의 칸.</summary>
    public static int Move(int rosterIndex) => FirstMove + rosterIndex;

    /// <summary>동작 칸이면 명부의 칸, 아니면 null.</summary>
    public int? RosterIndex(int action) => action >= FirstMove && action < Count ? action - FirstMove : null;

    /// <summary>로그의 이름 — <c>wait</c> · <c>approach</c> · <c>retreat</c> · <c>leap_over</c> · <c>leap_back</c> · <c>continue</c> · 동작 id.</summary>
    public string Name(int action) =>
        action >= 0 && action < FirstMove ? _names[action] : RosterIndex(action) is int i ? _roster[i] : $"?{action}";

    /// <summary>이름의 칸 — 움직임 이름이나 동작 id. 모르면 null.</summary>
    public int? Index(string name)
    {
        int at = Array.IndexOf(_names, name);
        if (at >= 0)
        {
            return at;
        }

        for (int i = 0; i < _roster.Count; i++)
        {
            if (_roster[i] == name)
            {
                return Move(i);
            }
        }

        return null;
    }
}

/// <summary>
/// 파이터의 한 틱 모습 (설계 2026-10-01 조각2 §2) — 늦은 관측의 고리(<see cref="SightBuffer"/>)가 쌓는다. 망의 입력 칸 표는 조각 4 가 정한다.
/// </summary>
/// <param name="X">자리(발 중심).</param>
/// <param name="Y">발바닥 높이.</param>
/// <param name="Facing">보는 쪽.</param>
/// <param name="Action">지금 행동.</param>
/// <param name="Throwing">폭탄을 던지는 중인가.</param>
/// <param name="Health">체력.</param>
/// <param name="BombsLeft">남은 폭탄.</param>
/// <param name="StaminaRatio">스태미나 / 최대 (설계 2026-10-01 조각4 §2).</param>
/// <param name="ThrowProgress">던지기의 진행 0 ~ 1 — 던지는 중이 아니면 0. 아이템의 "준비 중" 속성이다.</param>
public readonly record struct FighterSnapshot(
    double X, double Y, int Facing, FighterAction Action, bool Throwing, int Health, int BombsLeft, double StaminaRatio = 1, double ThrowProgress = 0);

/// <summary>
/// 결정이 보는 판 (설계 2026-10-01 조각2 §2) — 보스 자신은 지금의 것이고 파이터는 늦춤(18틱) 앞의 것이다. 이기는 이유가 반응 속도가 아니라 읽기가 되게.
/// </summary>
/// <param name="Tick">판의 틱.</param>
/// <param name="Form">보스의 지금 형태 — 1 부터.</param>
/// <param name="BossX">보스의 자리.</param>
/// <param name="BossFacing">보스가 보는 쪽.</param>
/// <param name="BossHealth">보스 체력.</param>
/// <param name="Fighter">늦춤만큼 앞의 파이터.</param>
/// <param name="Events">이 판의 회피 관측 — 규칙 조종기의 고르기(<see cref="PlanRequest"/>)가 받는다.</param>
public sealed record BossSight(int Tick, int Form, double BossX, int BossFacing, int BossHealth, FighterSnapshot Fighter, IReadOnlyList<DodgeEvent> Events);

/// <summary>결정 하나 (설계 2026-10-01 조각2 §1).</summary>
/// <param name="Point">어느 지점인가.</param>
/// <param name="Number">이 판의 결정 번호 — 0 부터. 무작위 조종기의 좌표다.</param>
/// <param name="Elapsed">자유로워진 뒤 센 쉬는 틱 — 규칙 조종기가 계획의 쉬기와 견준다.</param>
/// <param name="Mask">열린 칸 — 길이가 <see cref="BossActions.Count"/> 다.</param>
/// <param name="Sight">이 결정이 보는 판.</param>
/// <param name="Current">캔슬 지점의 지금 동작 — 그 밖은 null.</param>
/// <param name="CancelPoint">캔슬 지점의 칸(<c>cancel_points</c>) — 그 밖은 null.</param>
/// <param name="Observation">관측(<see cref="BossObservation"/>) — 원하는 조종기(<see cref="IBossController.WantsObservation"/>)에게만 짓는다. 그 밖은 null.</param>
public sealed record BossDecision(
    DecisionPoint Point, int Number, int Elapsed, IReadOnlyList<bool> Mask, BossSight Sight, string? Current, int? CancelPoint,
    IReadOnlyList<double>? Observation = null);

/// <summary>
/// 보스의 조종기 (설계 2026-10-01 조각2 §3 · 우산 §3.4) — 결정 지점에서 칸 하나를 고른다. 판이 마스크를 다시 본다: 가려진 칸은 <c>[E] action_masked</c>.
/// </summary>
public interface IBossController
{
    /// <summary>폭탄 던지기를 보면 끊으려 하나(<c>bomb_reaction</c>) — 규칙 조종기만 참이다. 끊는 자리가 결정 지점과 달라 판의 장치로 둔다(§3).</summary>
    bool ReactsToBombs { get; }

    /// <summary>
    /// 관측(<see cref="BossDecision.Observation"/>)을 원하나 (설계 2026-10-01 조각4 §2) — 망 조종기만 참이다. 규칙 · 무작위 조종기의 판은 할당이 안 는다.
    /// </summary>
    bool WantsObservation => false;

    /// <summary>열린 칸 하나.</summary>
    int Decide(BossDecision decision);
}
