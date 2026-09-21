using PreReLU.Battle.Rules;

namespace PreReLU.Rules.Tests.Battle;

/// <summary>테스트가 같이 쓰는 기준 수치. 실제 게임 값은 data/fighters.json 이 갖는다.</summary>
public static class TestConfigs
{
    public static FighterConfig Fighter() => new()
    {
        MoveSpeed = 420,
        JumpVelocity = 900,
        MaxHealth = 100,
        MaxStamina = 100,
        HalfWidth = 30,
        Height = 120,
        DashSpeed = 1100,
        DashDuration = 0.18,
        DashIFrames = 0.14,
        DashCost = 25,
        ParryWindow = 0.12,
        ParryDuration = 0.30,
        ParryCost = 15,
        AttackWindup = 0.08,
        AttackActive = 0.06,
        AttackRecover = 0.14,
        AttackReach = 90,
        AttackDamage = 8,
        AttackCost = 12,
        StaminaRegen = 40,
        Sprite = "test_unit",
    };
}
