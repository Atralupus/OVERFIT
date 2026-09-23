using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// <b>한 번 더, 그리고 그게 무겁다</b> — <c>쐐기</c> 변종의 표지 (이슈 #48).
///
/// <para>
/// 이 변종이 다른 것은 <b>박자</b>다: 셋이 아니라 넷이고 마지막 하나가 세 배로 무겁다. 그래서
/// 표지도 기하가 아니라 <b>수</b>를 그린다 — 칼 위에 눈금 넷을 세우고 마지막 하나만 크게.
/// 가드로 버티는 사람이 스태미나를 나눠 쓸 수 있으려면 "몇 대인가" 가 선딜에 보여야 한다.
/// </para>
/// </summary>
public sealed class DragWedgeTell : IBossTellShape
{
    /// <summary>이 변종의 판정 수. <b>데이터의 multi_hit 이 아니다</b> — 뷰는 규칙을 안 읽는다.
    /// 표지는 "여러 번, 마지막이 크다" 를 말하는 그림이고, 정확한 개수를 두 곳에 두면 갈린다.</summary>
    private const int _beats = 4;

    public void Draw(CanvasItem into, BossTell tell, float width, Color color)
    {
        System.ArgumentNullException.ThrowIfNull(into);

        Vector2 tip = DragBlade.Draw(into, tell, width, color);
        float facing = DragBlade.Facing(tell);
        float step = width * 3.4f;

        for (int i = 0; i < _beats; i++)
        {
            bool last = i == _beats - 1;
            float height = last ? width * 7.0f : width * 3.2f;
            float x = tip.X - (facing * step * (_beats - 1 - i));
            var top = new Vector2(x, tip.Y - height);
            into.DrawLine(new Vector2(x, tip.Y), top, color, last ? width * 1.4f : width * 0.8f, antialiased: true);

            if (!last)
            {
                continue;
            }

            // 마지막 눈금에만 쐐기를 씌운다 — **여기가 무겁다**를 크기가 아니라 모양으로도 말한다.
            float barb = width * 2.6f;
            into.DrawLine(top, top + new Vector2(-barb, barb * 1.2f), color, width * 1.2f, antialiased: true);
            into.DrawLine(top, top + new Vector2(barb, barb * 1.2f), color, width * 1.2f, antialiased: true);
        }
    }
}
