using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// <b>그 틈은 미끼다</b> — <c>역습</c> 변종의 표지 (이슈 #48).
///
/// <para>
/// 내려찍기 계열은 2타와 3타 사이를 1.10초 벌려 둔다(이슈 #54). 거기 칼이 들어가고,
/// 그렇게 하라고 벌려 둔 것이기도 하다 — 이 변종만 그 자리에(2타 직후의 칼질 위에) 판정을 하나 끼운다.
/// 그러니 표지는 <b>틈 자체</b>를 그린다: 칼과 보스 사이 바닥에 고리를 하나 놓고 아래로 미늘을 건다.
/// 낚싯바늘로 읽히는 것이 목적이고, 그래야 "들어가도 되는 자리" 가 "들어가면 물리는 자리" 로 뒤집힌다.
/// </para>
/// </summary>
public sealed class DragBaitTell : IBossTellShape
{
    public void Draw(CanvasItem into, BossTell tell, float width, Color color)
    {
        System.ArgumentNullException.ThrowIfNull(into);

        DragBlade.Draw(into, tell, width, color);

        float facing = DragBlade.Facing(tell);
        float radius = width * 3.2f;
        var at = new Vector2(facing * (float)tell.Length * 0.55f, -radius * 1.6f);

        // 고리. 바닥 위에 살짝 떠 있어 "여기가 빈자리" 로 읽힌다.
        into.DrawArc(at, radius, 0, Mathf.Tau, 28, color, width * 0.9f, antialiased: true);

        // 미늘. 고리에서 바닥으로 내려와 반대쪽으로 꺾인다 — 갈고리의 문법이다.
        var down = new Vector2(at.X, -width * 0.6f);
        into.DrawLine(new Vector2(at.X, at.Y + radius), down, color, width * 0.9f, antialiased: true);
        into.DrawLine(down, down + new Vector2(-facing * radius * 0.9f, -radius * 0.7f), color, width * 0.9f, antialiased: true);
    }
}
