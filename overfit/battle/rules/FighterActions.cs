namespace Overfit.Battle.Rules;

/// <summary>
/// 파이터 망의 칸 (설계 2026-10-01 조각6 §1.1) — 6틱마다 하나를 고르고, 고른 칸은 다음 결정까지의 입력이다. 엣지(점프 · 대시 · 공격 · 패리 · 폭탄)는 첫 틱에만
/// 누른다 — 6틱 내내 누르면 엔진이 엣지를 한 번만 세는 것과 달라진다(사람의 누름은 한 번이다). 이동 · 가드는 6틱 내내 붙든다.
/// </summary>
public static class FighterActions
{
    public const int Idle = 0;
    public const int Left = 1;
    public const int Right = 2;
    public const int Jump = 3;
    public const int JumpLeft = 4;
    public const int JumpRight = 5;
    public const int DashLeft = 6;
    public const int DashRight = 7;
    public const int Attack = 8;
    public const int Parry = 9;
    public const int Guard = 10;
    public const int Bomb = 11;

    /// <summary>칸 수.</summary>
    public const int Count = 12;

    /// <summary>칸 <paramref name="action"/> 의 결정 뒤 <paramref name="tick"/> 번째(0 부터) 틱의 입력.</summary>
    public static InputFrame Input(int action, int tick)
    {
        bool first = tick == 0;
        sbyte move = action switch
        {
            Left or JumpLeft or DashLeft => -1,
            Right or JumpRight or DashRight => 1,
            _ => 0,
        };
        return new InputFrame(
            move,
            Jump: first && action is Jump or JumpLeft or JumpRight,
            Dash: first && action is DashLeft or DashRight,
            Parry: first && action == Parry,
            Attack: first && action == Attack,
            GuardHeld: action == Guard,
            Bomb: first && action == Bomb);
    }
}
