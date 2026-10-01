using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>보스의 움직임 — 관측의 칸 (설계 2026-10-01 조각4 §2).</summary>
public enum TravelState
{
    None,
    Approach,
    Retreat,
    Leap,
}

/// <summary>
/// 관측을 짓는 데 드는 것 (설계 2026-10-01 조각4 §2) — 판이 결정의 틱에 채운다. 파이터는 늦은 관측의 고리에서 온 모습이다(18틱 앞 · 최근 K 는 6틱씩 더 앞).
/// </summary>
public sealed record ObservationInput(
    DecisionPoint Point,
    double ArenaWidth,
    double BossX,
    double BossY,
    int BossFacing,
    int BossHealth,
    int BossMaxHealth,
    int Form,
    int FormCount,
    double PoiseRatio,
    bool Exhausted,
    bool Shifting,
    TravelState Travel,
    int MoveIndex,
    double MoveProgress,
    int TicksToCancel,
    FighterSnapshot Fighter,
    IReadOnlyList<FighterSnapshot> Recent,
    int FighterMaxHealth,
    IReadOnlyList<BombFlight> Bombs);

/// <summary>
/// 관측 — 결정의 판을 숫자 배열로 (설계 2026-10-01 조각4 §2). <b>칸 표가 계약이다</b>: 망의 입력이라 칸의 자리 · 순서 · 정규화를 바꾸면 다시 학습한다
/// (<c>BossObservationTests</c> 가 자리를 못박는다). 자리는 아레나 폭으로, 높이는 300 으로, 체력은 최대로 나눠 대략 −1 ~ 1 에 둔다.
///
/// <para>
/// <b>아이템은 공통 속성만이다</b>(우산 §4.1) — 칸에 아이템의 이름이 없다. 나는 폭탄은 "있나 · 난 몫 · 놓은 자리" 이고, 던지는 중인 폭탄은 파이터 칸의
/// "던지기 진행" 이다. 아이템이 늘면 속성 칸을 넓히고 다시 학습한다.
/// </para>
/// </summary>
public sealed class BossObservation
{
    /// <summary>파이터의 최근 모습 수 K — 18틱 앞에서 6틱씩 더 앞.</summary>
    public const int Recent = 8;

    /// <summary>최근 모습 사이(틱).</summary>
    public const int RecentGap = 6;

    /// <summary>나는 폭탄 칸 수 M — 넘치면 앞의 넷만.</summary>
    public const int Items = 4;

    /// <summary>높이의 나눗수(px) — 도약의 정점 280 이 1 근처다.</summary>
    private const double _height = 300;

    private static readonly int _actions = Enum.GetValues<FighterAction>().Length;
    private static readonly int _points = Enum.GetValues<DecisionPoint>().Length;
    private static readonly int _travels = Enum.GetValues<TravelState>().Length;

    private readonly int _roster;

    /// <param name="rosterCount">명부 길이 N — 동작 원핫의 칸 수.</param>
    public BossObservation(int rosterCount) => _roster = rosterCount;

    /// <summary>결정 지점 원핫의 칸 수 — 관측의 맨 앞이다.</summary>
    public static int PointCount => _points;

    /// <summary>형태 원핫(1 · 2 · 3)의 첫 칸.</summary>
    public static int FormOffset => _points + 4;

    /// <summary>파이터 행동 원핫의 칸 수.</summary>
    public static int FighterActionCount => _actions;

    /// <summary>지금 동작 원핫(없음 + 명부)의 첫 칸.</summary>
    public static int MoveOffset => _points + 10 + _travels;

    /// <summary>파이터 칸의 첫 칸 — 자리(dx/W) · 높이 · 보는 쪽 · 마주 보나 · 체력 · 기력 · 폭탄 · 던지기 진행 · 행동 원핫 순이다.</summary>
    public int FighterOffset => MoveOffset + _roster + 3;

    /// <summary>칸 수 D.</summary>
    public int Size => _points + 10 + _travels + (_roster + 3) + (8 + _actions) + (Recent * _actions) + (Items * 3);

    /// <summary>관측을 짓는다.</summary>
    public double[] Encode(ObservationInput o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var v = new double[Size];
        int i = 0;
        double w = o.ArenaWidth;

        v[i + (int)o.Point] = 1;
        i += _points;

        v[i++] = o.BossX / w;
        v[i++] = o.BossY / _height;
        v[i++] = o.BossFacing;
        v[i++] = Ratio(o.BossHealth, o.BossMaxHealth);
        for (int f = 1; f <= 3; f++)
        {
            v[i++] = o.Form == f ? 1 : 0;
        }

        v[i++] = o.PoiseRatio;
        v[i++] = o.Exhausted ? 1 : 0;
        v[i++] = o.Shifting ? 1 : 0;

        v[i + (int)o.Travel] = 1;
        i += _travels;

        v[i + (o.MoveIndex < 0 ? 0 : o.MoveIndex + 1)] = 1;
        i += _roster + 1;
        v[i++] = o.MoveIndex < 0 ? 0 : o.MoveProgress;
        v[i++] = o.TicksToCancel < 0 ? 0 : o.TicksToCancel / 60.0;

        FighterSnapshot fs = o.Fighter;
        int towardBoss = Math.Sign(o.BossX - fs.X);
        v[i++] = (fs.X - o.BossX) / w;
        v[i++] = fs.Y / _height;
        v[i++] = fs.Facing;
        v[i++] = towardBoss != 0 && fs.Facing == towardBoss ? 1 : 0;
        v[i++] = Ratio(fs.Health, o.FighterMaxHealth);
        v[i++] = fs.StaminaRatio;
        v[i++] = fs.BombsLeft / 10.0;
        v[i++] = fs.ThrowProgress;
        v[i + (int)fs.Action] = 1;
        i += _actions;

        for (int k = 0; k < Recent; k++)
        {
            if (k < o.Recent.Count)
            {
                v[i + (int)o.Recent[k].Action] = 1;
            }

            i += _actions;
        }

        for (int k = 0; k < Items && k < o.Bombs.Count; k++)
        {
            BombFlight b = o.Bombs[k];
            v[i + (k * 3)] = 1;
            v[i + (k * 3) + 1] = b.Progress;
            v[i + (k * 3) + 2] = (b.FromX - o.BossX) / w;
        }

        return v;
    }

    private static double Ratio(int value, int max) => max <= 0 ? 0 : (double)value / max;
}
