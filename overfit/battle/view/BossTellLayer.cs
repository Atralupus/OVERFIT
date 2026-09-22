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
    }
}
