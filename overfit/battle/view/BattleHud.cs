using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// 체력바 · 스태미나바 · 보스의 경직 게이지. 프로토타입의 UI 는 이것뿐이다.
///
/// <para>
/// <b>색은 여기 없다.</b> 전부 <c>ui/theme/main.tres</c> 가 정하고 씬은 이름(<c>theme_type_variation</c>)만
/// 고른다. 그 테마가 <c>ProgressBar</c> 를 아예 안 다루던 동안 세 바가 전부 엔진 기본 회색으로 나왔고,
/// 그래서 화면이 "누가 고른 적 없는 색" 뿐인 디버깅 화면처럼 보였다. 탈진의 파랑도 테마의 이름(<c>ExhaustBar</c>)이다 —
/// 여기서는 바꿔 끼울 이름만 안다.
/// </para>
///
/// <para>
/// 남은 체력을 <b>숫자로도</b> 적는다. 바의 길이는 비율만 말해서 "몇 대 더 버티나" 를 못 알려준다 —
/// 한 대에 깎이는 양이 눈금으로 안 보이기 때문이다.
/// </para>
/// </summary>
public partial class BattleHud : Control
{
    private static readonly StringName _staminaBar = "StaminaBar";
    private static readonly StringName _poiseBar = "PoiseBar";
    private static readonly StringName _exhaustBar = "ExhaustBar";

    private ProgressBar _health = null!;
    private ProgressBar _stamina = null!;
    private ProgressBar _bossHealth = null!;
    private ProgressBar _poise = null!;
    private Label _healthNumber = null!;
    private Label _bossNumber = null!;

    public override void _Ready()
    {
        _health = GetNode<ProgressBar>("%Health");
        _stamina = GetNode<ProgressBar>("%Stamina");
        _bossHealth = GetNode<ProgressBar>("%BossHealth");
        _poise = GetNode<ProgressBar>("%Poise");
        _healthNumber = GetNode<Label>("%HealthNumber");
        _bossNumber = GetNode<Label>("%BossNumber");
        _poise.MaxValue = 1;
    }

    public void Show(HudFrame frame)
    {
        _health.MaxValue = frame.MaxHealth;
        _health.Value = frame.Health;
        _stamina.MaxValue = frame.MaxStamina;
        _stamina.Value = frame.Stamina;
        _bossHealth.MaxValue = frame.BossMaxHealth;
        _bossHealth.Value = frame.BossHealth;

        // 스태미나에는 숫자를 안 적는다. 소수점이 매 틱 흔들려 읽히지 않고,
        // 스태미나는 "얼마 남았나" 가 아니라 "한 번 더 되나" 라 길이만으로 충분하다.
        _healthNumber.Text = $"{frame.Health} / {frame.MaxHealth}";
        _bossNumber.Text = $"{frame.BossHealth} / {frame.BossMaxHealth}";

        // 파이터가 탈진한 동안 스태미나 바가 파랗다 (#71 · 설계 §6) — 보스 게이지의 탈진과 같은 파랑이다. 길이는 그대로 스태미나를
        // 그린다: 탈진 동안에도 스태미나는 차고(1.1초에 44), 풀릴 때 "한 번 더 되나" 가 그 길이다.
        Paint(_stamina, frame.FighterExhausted ? _exhaustBar : _staminaBar);

        // 경직 게이지 (#71 · 설계 §6) — 보스 체력바 바로 밑, 같은 폭 · 얇게 · 숫자 없음. 탈진 동안에는 푸른 모양으로 바뀌어
        // **남은 탈진**을 그린다: 무너지는 순간 가득 찬 채 푸르게 바뀌어 준다. 규칙의 게이지는 무너질 때 0 이라, 그것을 그리면
        // "꽉 찼다" 가 한 프레임도 안 보인다.
        bool exhausted = frame.BossExhaustLeft > 0;
        Paint(_poise, exhausted ? _exhaustBar : _poiseBar);
        _poise.Value = exhausted ? frame.BossExhaustLeft : frame.Poise;
    }

    /// <summary>바의 색 이름을 바꿔 끼운다 — 바뀔 때만. 같은 이름을 매 프레임 넣어도 엔진이 테마를 다시 훑지 않게.</summary>
    private static void Paint(ProgressBar bar, StringName variation)
    {
        if (bar.ThemeTypeVariation != variation)
        {
            bar.ThemeTypeVariation = variation;
        }
    }
}
