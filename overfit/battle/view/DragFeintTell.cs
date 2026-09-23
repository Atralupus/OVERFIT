using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// <b>한 번은 허공이다</b> — <c>역린</c> 변종의 표지 (이슈 #48).
///
/// <para>
/// 이 변종에는 판정이 없는 <b>헛스윙</b>이 하나 섞인다. 표지는 그 사실을 말하되 <b>언제인지는
/// 안 말한다</b> — 진짜 칼 옆에 점선으로 두 번째 칼을 그린다. 실선과 점선이 나란히 있으면
/// "둘 중 하나는 없는 것" 으로 읽히고, 어느 쪽이 없는지는 눈으로 세는 수밖에 없다.
/// </para>
///
/// <para>
/// 예고가 여기까지만 말하는 것이 <b>정직한 미끼</b>의 조건이다. 헛스윙을 아예 안 알리면 벌만
/// 남고(반응할 근거가 없다), 어느 박자가 헛것인지까지 알리면 아무도 안 문다.
/// </para>
/// </summary>
public sealed class DragFeintTell : IBossTellShape
{
    public void Draw(CanvasItem into, BossTell tell, float width, Color color)
    {
        System.ArgumentNullException.ThrowIfNull(into);

        Vector2 tip = DragBlade.Draw(into, tell, width, color);
        float facing = DragBlade.Facing(tell);
        float length = (float)tell.Length;

        // 점선 칼: 같은 길이 · 같은 방향인데 조금 떠 있고 반쯤 지워져 있다.
        var ghostTip = new Vector2(tip.X, tip.Y - (width * 6.0f));
        var ghostHilt = new Vector2(ghostTip.X - (facing * length), ghostTip.Y);
        var faint = new Color(color.R, color.G, color.B, color.A * 0.5f);

        float dash = width * 2.4f;
        for (float t = 0; t < length; t += dash * 2.0f)
        {
            var from = new Vector2(ghostHilt.X + (facing * t), ghostHilt.Y);
            var to = new Vector2(ghostHilt.X + (facing * Mathf.Min(length, t + dash)), ghostHilt.Y);
            into.DrawLine(from, to, faint, width * 0.9f, antialiased: true);
        }

        // 점선 쪽 가로대도 점선처럼 짧게 — 이것이 "칼" 이라는 것까지는 읽혀야 한다.
        into.DrawLine(
            ghostHilt + new Vector2(0, -width * 1.6f),
            ghostHilt + new Vector2(0, width * 1.6f),
            faint,
            width * 0.8f,
            antialiased: true);
    }
}
