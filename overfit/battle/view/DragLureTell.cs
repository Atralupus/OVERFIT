using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// <b>저 바깥이 덮인다</b> — <c>끌기</c> 변종의 표지 (이슈 #48).
///
/// <para>
/// 이 변종은 마무리를 <b>밖으로 한 대시의 착지점</b>에 세운다. 그러니 표지가 말해야 하는 것은
/// "칼이 온다" 가 아니라 <b>어디로 도망치면 안 되는가</b> 다 — 바닥에 띠 하나를 긋고 밖을 향한
/// 화살표를 붙인다. 붙어 있으면(띠 안쪽) 아무 일도 안 일어난다는 것까지 같은 그림이 말한다.
/// </para>
///
/// <para>
/// ⚠ <b>정확한 px 를 안 그린다.</b> 띠의 진짜 범위(290~620)는 <c>patterns.json</c> 이 알고,
/// 여기 베끼면 기하의 두 번째 진실이 생겨 한쪽만 고치는 날이 온다. 표지는 "칼 길이의 두 배쯤
/// 바깥" 이라는 <b>관계</b>만 그린다 — 데이터가 움직이면 같이 움직이는 쪽이다.
/// </para>
/// </summary>
public sealed class DragLureTell : IBossTellShape
{
    public void Draw(CanvasItem into, BossTell tell, float width, Color color)
    {
        System.ArgumentNullException.ThrowIfNull(into);

        DragBlade.Draw(into, tell, width, color);

        float facing = DragBlade.Facing(tell);
        float length = (float)tell.Length;
        float near = facing * length * 1.2f;
        float far = facing * length * 2.5f;
        float post = width * 3.5f;

        // 바닥의 띠. 양끝 기둥이 "여기서부터 저기까지" 를 끊어 준다 — 선만 그으면 어디서 끝나는지 안 보인다.
        into.DrawLine(new Vector2(near, 0), new Vector2(far, 0), color, width, antialiased: true);
        into.DrawLine(new Vector2(near, 0), new Vector2(near, -post), color, width, antialiased: true);
        into.DrawLine(new Vector2(far, 0), new Vector2(far, -post), color, width, antialiased: true);

        // 밖을 향한 화살표. **방향이 곧 경고다** — 대시가 데려다 놓는 쪽이 저기다.
        var head = new Vector2(far, -post * 0.6f);
        float barb = width * 3.0f;
        into.DrawLine(head, head - new Vector2(facing * barb, barb * 0.8f), color, width * 0.8f, antialiased: true);
        into.DrawLine(head, head - new Vector2(facing * barb, -barb * 0.8f), color, width * 0.8f, antialiased: true);
    }
}
