using Godot;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 보스의 알아챔 표시 (설계 2026-09-30 조각2 §2.1 · §5) — 규칙이 "보스가 던지기를 안다" 고 말하는 동안(<c>BattleSim.BossAlert</c>) 보스 머리 위에
/// "!" 를 띄운다. 끊을 자리가 아직 안 와 못 끊고 있어도 "보고 있다" 가 보여야, 뒤이은 끊김이 무작위가 아니라 반응으로 읽힌다. 끊은 뒤의 멈칫
/// 동안에도 던지기가 돌면 그대로 떠 있고, 던지기가 끝나면(놓음 · 끊김) 사라진다. 팩에 그림이 없어 코드로 그린다(폭탄 · 흰 구와 같다) — 글꼴에
/// 기대지 않는다.
///
/// <para>
/// 크기와 자리는 <c>balance.json</c> 의 <c>feel</c> 이고, 색은 이름 붙은 상수다 — 수치는 "얼마나", 색은 "무엇" 이라는 규약(<see cref="FeelBalance"/>
/// 머리) 그대로다.
/// </para>
/// </summary>
public partial class AlertMark : Node2D
{
    /// <summary>표시의 몸 — 따뜻한 노랑. 폭탄 심지의 불꽃과 같은 계열이라 "폭탄 때문이다" 가 색으로 이어진다.</summary>
    private static readonly Color _mark = new(1.00f, 0.84f, 0.26f, 1f);

    /// <summary>테두리 — 밤하늘 배경에서도 보스의 밝은 칼 위에서도 모양이 선다.</summary>
    private static readonly Color _outline = new(0.08f, 0.08f, 0.10f, 1f);

    private FeelBalance _feel = null!;

    /// <summary>지금 떠 있나 — 바뀔 때만 다시 그린다.</summary>
    private bool _shown;

    public override void _Ready()
    {
        _feel = Balance.Data.Feel;

        // 두 몸 위에 그린다 — 보스의 칼이 머리 위로 올라가도 가려지지 않는다. 폭탄과 같은 층이다(흰 구 10 아래).
        ZIndex = 9;
    }

    /// <summary>한 프레임. 보스 머리 위(규칙 좌표 · 위가 +)에 선다 — 보스가 뛰면(도약) 같이 뜬다.</summary>
    /// <param name="alert">규칙이 "안다" 고 말하나.</param>
    /// <param name="bossX">보스 발 중심.</param>
    /// <param name="bossY">보스 발바닥 높이.</param>
    /// <param name="bossHeight">보스 몸의 키 — 판정의 키다(<c>bosses.json</c> 의 <c>height</c>).</param>
    public void Show(bool alert, double bossX, double bossY, double bossHeight)
    {
        Position = new Vector2((float)bossX, (float)-(bossY + bossHeight + _feel.AlertOffsetY));
        if (alert == _shown)
        {
            return;
        }

        _shown = alert;
        QueueRedraw();
        Log.Debug("view", $"boss_alert shown={(alert ? 1 : 0)}");
    }

    public override void _Draw()
    {
        if (!_shown)
        {
            return;
        }

        // 원점이 표시의 발끝이다 — 위로 그린다(화면은 아래가 +). 막대는 위가 넓고 아래가 좁은 사다리꼴, 그 밑에 점 하나.
        float h = (float)_feel.AlertSize;
        float top = h * 0.16f;
        float bottom = h * 0.10f;
        float pad = h * 0.06f;
        Vector2[] bar =
        [
            new(-top, -h),
            new(top, -h),
            new(bottom, -h * 0.36f),
            new(-bottom, -h * 0.36f),
        ];
        Vector2[] rim =
        [
            new(-top - pad, -h - pad),
            new(top + pad, -h - pad),
            new(bottom + pad, -h * 0.36f + pad),
            new(-bottom - pad, -h * 0.36f + pad),
        ];
        var dot = new Vector2(0, -h * 0.12f);
        float r = h * 0.12f;

        DrawColoredPolygon(rim, _outline);
        DrawCircle(dot, r + pad, _outline);
        DrawColoredPolygon(bar, _mark);
        DrawCircle(dot, r, _mark);
    }
}
