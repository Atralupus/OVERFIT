namespace Overfit.Battle.Rules;

/// <summary>
/// 캐릭터 한 종의 수치. <c>data/fighters.json</c> 의 모양이고 키는 snake_case 로 변환된다.
/// <b>여기 없는 수치를 C# 에 상수로 두지 않는다.</b>
/// </summary>
public sealed class FighterConfig
{
    public required double MoveSpeed { get; init; }

    public required double JumpVelocity { get; init; }

    public required int MaxHealth { get; init; }

    public required double MaxStamina { get; init; }

    /// <summary>몸 절반 폭. 벽 제한이 이것을 쓴다.</summary>
    public required double HalfWidth { get; init; }

    /// <summary>키. 세로 판정(점프로 넘는가 · 대공에 걸리는가)이 이것을 쓴다.</summary>
    public required double Height { get; init; }

    public required double DashSpeed { get; init; }

    public required double DashDuration { get; init; }

    /// <summary>무적 창. <see cref="DashDuration"/> 보다 <b>짧아야</b> 대시 타이밍이 의미를 갖는다 — 끝자락엔 맞는다.</summary>
    public required double DashIFrames { get; init; }

    public required double DashCost { get; init; }

    /// <summary>막아주는 창. <see cref="ParryDuration"/> 보다 짧다 — 실패가 비싸야 패리 의존도가 축이 된다.</summary>
    public required double ParryWindow { get; init; }

    public required double ParryDuration { get; init; }

    public required double ParryCost { get; init; }

    public required double AttackWindup { get; init; }

    public required double AttackActive { get; init; }

    public required double AttackRecover { get; init; }

    public required double AttackReach { get; init; }

    public required int AttackDamage { get; init; }

    public required double AttackCost { get; init; }

    /// <summary>초당 회복량. 행동 중에는 회복하지 않는다.</summary>
    public required double StaminaRegen { get; init; }

    /// <summary>
    /// Duelyst SpriteFrames 의 id. <b>규칙은 이 값을 안 쓴다</b> — 뷰가 읽어 그릴 뿐이다.
    /// 여기 두는 이유는 "이 캐릭터가 무엇인가"의 진실이 data/fighters.json 한 곳이어야 하기 때문이다.
    /// </summary>
    public required string Sprite { get; init; }
}
