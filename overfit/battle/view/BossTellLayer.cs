using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// 보스 선딜의 예고 표지를 그리는 노드 하나. 모양은 <see cref="BossTellShapes"/> 가 주고,
/// 여기는 <b>언제 보이는가</b>만 안다.
///
/// <para>
/// <see cref="RingBurst"/> 와 같은 규약이다 — <b>프레임마다 다시 준다.</b> 안 주는 프레임엔
/// 저절로 사라지므로 끄는 것을 잊어 표지가 남는 사고가 구조로 없다. 선딜이 끝났는데 칼이
/// 계속 떠 있으면 그 표지는 거짓말이 되고, 백장의 설계가 통째로 무너진다.
/// </para>
/// </summary>
public partial class BossTellLayer : Node2D
{
    private bool _on;
    private bool _given;
    private BossTell _tell;
    private Color _color;

    /// <summary>선 두께(px). <c>balance.json</c> 의 feel 이 정한다.</summary>
    public float LineWidth { get; set; } = 6.0f;

    /// <summary>
    /// 가드 불가 표지(<c>危</c>)를 띄울 높이(px, 발밑에서 위로) — 이슈 #47.
    /// <b>보스 키보다 위여야</b> 몸과 겹쳐 획이 먹히지 않는다. <c>BossView</c> 가 데이터에서 준다.
    /// </summary>
    public float MarkHeight { get; set; } = 340.0f;

    /// <summary>가드 불가 표지의 글자 크기(px).</summary>
    public float MarkSize { get; set; } = 96.0f;

    /// <summary>이번 프레임의 표지. <b>매 프레임 부른다</b> — 안 부르면 그 프레임에 사라진다.</summary>
    public void Show(BossTell tell, Color color)
    {
        _given = true;
        _tell = tell;
        _color = color;
    }

    public override void _Process(double delta)
    {
        _on = _given;
        _given = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_on)
        {
            return;
        }

        BossTellShapes.Find(_tell.ShapeId)?.Draw(this, _tell, LineWidth, _color);

        // 가드 불가는 **모양이 아니라 표지**다 (이슈 #47) — 패턴마다 다른 것이 아니라
        // 깃발 하나에 붙는 하나뿐인 그림이라 등록표를 안 탄다. 색은 부르는 쪽이 준 링 색이고,
        // 그 색은 여기서 언제나 빨강이다 (이슈 #53): 가드 불가는 **늘 마무리 한 대**라
        // 링이 이미 빨강이기 때문이다(PatternDataTests 가 그 규약을 못박는다).
        // 글자와 링이 갈리면 둘 중 하나는 안 읽힌다.
        if (_tell.GuardBreak)
        {
            GuardBreakMark.Draw(this, new Vector2(0, -MarkHeight), MarkSize, _color);
        }
    }
}
