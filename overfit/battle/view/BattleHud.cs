using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// 체력바와 스태미나바. 프로토타입의 UI 는 이 둘뿐이다.
///
/// <para>
/// <b>색은 여기 없다.</b> 전부 <c>ui/theme/main.tres</c> 가 정하고 씬은 이름(<c>theme_type_variation</c>)만
/// 고른다. 그 테마가 <c>ProgressBar</c> 를 아예 안 다루던 동안 세 바가 전부 엔진 기본 회색으로 나왔고,
/// 그래서 화면이 "누가 고른 적 없는 색" 뿐인 디버깅 화면처럼 보였다.
/// </para>
///
/// <para>
/// 남은 체력을 <b>숫자로도</b> 적는다. 바의 길이는 비율만 말해서 "몇 대 더 버티나" 를 못 알려준다 —
/// 한 대에 깎이는 양이 눈금으로 안 보이기 때문이다.
/// </para>
/// </summary>
public partial class BattleHud : Control
{
    private ProgressBar _health = null!;
    private ProgressBar _stamina = null!;
    private ProgressBar _bossHealth = null!;
    private Label _healthNumber = null!;
    private Label _bossNumber = null!;

    public override void _Ready()
    {
        _health = GetNode<ProgressBar>("%Health");
        _stamina = GetNode<ProgressBar>("%Stamina");
        _bossHealth = GetNode<ProgressBar>("%BossHealth");
        _healthNumber = GetNode<Label>("%HealthNumber");
        _bossNumber = GetNode<Label>("%BossNumber");
    }

    public void Show(int health, int maxHealth, double stamina, double maxStamina, int bossHealth, int bossMaxHealth)
    {
        _health.MaxValue = maxHealth;
        _health.Value = health;
        _stamina.MaxValue = maxStamina;
        _stamina.Value = stamina;
        _bossHealth.MaxValue = bossMaxHealth;
        _bossHealth.Value = bossHealth;

        // 스태미나에는 숫자를 안 적는다. 소수점이 매 틱 흔들려 읽히지 않고,
        // 스태미나는 "얼마 남았나" 가 아니라 "한 번 더 되나" 라 길이만으로 충분하다.
        _healthNumber.Text = $"{health} / {maxHealth}";
        _bossNumber.Text = $"{bossHealth} / {bossMaxHealth}";
    }
}
