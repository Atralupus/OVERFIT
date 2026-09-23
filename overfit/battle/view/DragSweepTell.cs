using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// <b>방을 쓴다</b> — <c>쇄도</c> 변종의 표지 (이슈 #48).
///
/// <para>
/// 이 변종은 마무리의 사거리만 넓힌다. 화면에서 그것이 보이려면 <b>끝이 안 보이는 선</b>이어야
/// 한다 — 끝을 그리면 "저기까지" 가 되고, 그러면 도망갈 곳이 있다는 뜻이 된다. 그래서 띠 대신
/// 갈매기표를 점점 옅게 깔아 <b>화면 밖으로 이어지는</b> 것처럼 만든다.
/// </para>
///
/// <para>
/// <c>DragLureTell</c> 과 나란히 읽히도록 만든 짝이다: 저쪽은 <b>끊긴 띠</b>(밖이 위험) ·
/// 이쪽은 <b>안 끊기는 선</b>(밖이 없다). 둘 다 바닥에 그리는 이유는 내려찍기가 땅을 치기 때문이다.
/// </para>
/// </summary>
public sealed class DragSweepTell : IBossTellShape
{
    public void Draw(CanvasItem into, BossTell tell, float width, Color color)
    {
        System.ArgumentNullException.ThrowIfNull(into);

        DragBlade.Draw(into, tell, width, color);

        float facing = DragBlade.Facing(tell);
        float length = (float)tell.Length;
        const int marks = 4;

        for (int i = 1; i <= marks; i++)
        {
            // 멀어질수록 옅어진다 — 끝이 아니라 **사라짐**이라야 "끝이 없다" 로 읽힌다.
            float fade = 1.0f - (i / (float)(marks + 1));
            var dim = new Color(color.R, color.G, color.B, color.A * fade);
            float x = facing * length * (1.0f + (i * 0.75f));
            float wing = width * 3.2f;

            into.DrawLine(new Vector2(x, 0), new Vector2(x - (facing * wing), -wing), dim, width, antialiased: true);
            into.DrawLine(new Vector2(x, 0), new Vector2(x - (facing * wing), wing * 0.35f), dim, width, antialiased: true);
        }
    }
}
