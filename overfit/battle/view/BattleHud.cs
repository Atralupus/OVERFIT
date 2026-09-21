using Godot;

namespace Overfit.Battle.View;

/// <summary>체력바와 스태미나바. 프로토타입의 UI 는 이 둘뿐이다.</summary>
public partial class BattleHud : Control
{
    private ProgressBar _health = null!;
    private ProgressBar _stamina = null!;
    private ProgressBar _bossHealth = null!;

    public override void _Ready()
    {
        _health = GetNode<ProgressBar>("%Health");
        _stamina = GetNode<ProgressBar>("%Stamina");
        _bossHealth = GetNode<ProgressBar>("%BossHealth");
    }

    public void Show(int health, int maxHealth, double stamina, double maxStamina, int bossHealth, int bossMaxHealth)
    {
        _health.MaxValue = maxHealth;
        _health.Value = health;
        _stamina.MaxValue = maxStamina;
        _stamina.Value = stamina;
        _bossHealth.MaxValue = bossMaxHealth;
        _bossHealth.Value = bossHealth;
    }
}
