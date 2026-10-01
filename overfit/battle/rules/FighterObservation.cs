using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>파이터가 본 보스의 한 틱 모습 (설계 2026-10-01 조각6 §1.2) — 고리에 쌓아 12틱 앞의 것을 읽는다.</summary>
public readonly record struct BossGlance(
    double X, double Y, int Facing, int Health, int MaxHealth, int Form, int MoveIndex, int StepKind, double? NextActiveIn, bool GrabNext,
    TravelState Travel, bool Exhausted, bool Shifting, bool Alert);

/// <summary>파이터 자신의 지금 (설계 2026-10-01 조각6 §1.2).</summary>
public readonly record struct FighterSelf(
    double X, double Y, double VelocityY, int Facing, FighterAction Action, bool Stiff, bool Exhausted, bool Held, double StaminaRatio,
    int Health, int MaxHealth, int BombsLeft, double ThrowProgress, int ComboStep, bool Grounded, bool AirDashSpent);

/// <summary>
/// 파이터 망의 관측 (설계 2026-10-01 조각6 §1.2) — 보스는 12틱 앞(사람의 반응), 파이터 자신은 지금. 보스 관측(<see cref="BossObservation"/>)과 같은 정규화다.
/// 칸 표가 계약이다 — 바꾸면 파이터 망을 다시 학습한다(파이터 망은 학습용 상대라 게임에는 안 들어간다).
/// </summary>
public sealed class FighterObservation
{
    private const double _height = 300;
    private static readonly int _actions = Enum.GetValues<FighterAction>().Length;
    private static readonly int _travels = Enum.GetValues<TravelState>().Length;
    private readonly int _roster;

    public FighterObservation(int rosterCount) => _roster = rosterCount;

    /// <summary>보스 단계의 원핫 — 없음 · 선딜 · 판정 · 후딜.</summary>
    public const int StepKinds = 4;

    /// <summary>칸 수.</summary>
    public int Size => (4 + 3 + (_roster + 1) + StepKinds + 2 + _travels + 3) + (4 + _actions + 11) + BossObservation.Items;

    /// <summary>단계 이름 → 원핫 칸(없음 0).</summary>
    public static int StepKind(string? kind) => kind switch
    {
        "windup" => 1,
        "active" => 2,
        "recover" => 3,
        _ => 0,
    };

    public double[] Encode(BossGlance b, FighterSelf f, IReadOnlyList<BombFlight> bombs, double arenaWidth)
    {
        ArgumentNullException.ThrowIfNull(bombs);
        var v = new double[Size];
        int i = 0;
        v[i++] = (b.X - f.X) / arenaWidth;
        v[i++] = b.Y / _height;
        v[i++] = b.Facing;
        v[i++] = b.MaxHealth <= 0 ? 0 : (double)b.Health / b.MaxHealth;
        for (int k = 1; k <= 3; k++)
        {
            v[i++] = b.Form == k ? 1 : 0;
        }

        v[i + (b.MoveIndex < 0 ? 0 : b.MoveIndex + 1)] = 1;
        i += _roster + 1;
        v[i + b.StepKind] = 1;
        i += StepKinds;
        v[i++] = b.NextActiveIn is double next ? Math.Min(2, Math.Max(0, next)) : 0;
        v[i++] = b.GrabNext ? 1 : 0;
        v[i + (int)b.Travel] = 1;
        i += _travels;
        v[i++] = b.Exhausted ? 1 : 0;
        v[i++] = b.Shifting ? 1 : 0;
        v[i++] = b.Alert ? 1 : 0;

        int toward = Math.Sign(b.X - f.X);
        v[i++] = f.Y / _height;
        v[i++] = f.VelocityY / 1000.0;
        v[i++] = f.Facing;
        v[i++] = toward != 0 && f.Facing == toward ? 1 : 0;
        v[i + (int)f.Action] = 1;
        i += _actions;
        v[i++] = f.Stiff ? 1 : 0;
        v[i++] = f.Exhausted ? 1 : 0;
        v[i++] = f.Held ? 1 : 0;
        v[i++] = f.StaminaRatio;
        v[i++] = f.MaxHealth <= 0 ? 0 : (double)f.Health / f.MaxHealth;
        v[i++] = f.BombsLeft / 10.0;
        v[i++] = f.ThrowProgress;
        v[i++] = f.ComboStep;
        v[i++] = f.Grounded ? 1 : 0;
        v[i++] = f.AirDashSpent ? 1 : 0;
        v[i++] = f.X / arenaWidth;

        for (int k = 0; k < BossObservation.Items && k < bombs.Count; k++)
        {
            v[i + k] = bombs[k].Progress;
        }

        return v;
    }
}
