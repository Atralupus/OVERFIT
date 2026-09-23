using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// <b>계열의 밑그림</b> — 땅에 끌리는 칼 (이슈 #48 · 원래는 <see cref="BladeDragTell"/> 안에 있었다).
///
/// <para>
/// 내려찍기 변종 아홉은 <b>같은 기술</b>이라 표지도 같은 칼에서 출발한다. 변종을 가르는 것은
/// 그 위에 얹히는 표시 하나뿐이고, 그래서 칼을 그리는 코드는 한 곳에 있어야 한다 —
/// 여섯 파일에 베껴 두면 "계열이 하나" 라는 사실이 화면에서 조용히 갈라진다.
/// </para>
///
/// <para>
/// <b>Node 가 아니다.</b> 그리는 노드는 <see cref="BossTellLayer"/> 하나뿐이고 이것은 그 캔버스에
/// 선을 얹는 함수다 — <see cref="IBossTellShape"/> 와 같은 규약이다.
/// </para>
/// </summary>
public static class DragBlade
{
    /// <summary>
    /// 칼을 그리고 <b>칼끝의 자리</b>를 돌려준다. 변종의 표시가 거기서 이어지므로 —
    /// 각자 다시 계산하면 칼과 표시가 어긋나는 자리가 여섯 군데 생긴다.
    /// </summary>
    /// <param name="into">그릴 캔버스. <c>_Draw</c> 안에서만 부른다.</param>
    /// <param name="tell">이 패턴의 표지 위치와 크기. 이미 보스가 보는 쪽으로 뒤집혀 있다.</param>
    /// <param name="width">선 두께(px).</param>
    /// <param name="color">색. 패리 가능 여부와 무르익음이 이미 섞여 있다.</param>
    public static Vector2 Draw(CanvasItem into, BossTell tell, float width, Color color)
    {
        System.ArgumentNullException.ThrowIfNull(into);

        var mid = new Vector2((float)tell.X, (float)-tell.Y);
        float half = (float)tell.Length / 2.0f;
        var along = new Vector2(Mathf.Sign(tell.X) == 0 ? 1 : Mathf.Sign((float)tell.X), 0);

        // 날: 바닥과 나란하다. 칼끝만 살짝 내려 "끌리는" 쪽을 만든다.
        Vector2 hilt = mid - (along * half);
        Vector2 tip = mid + (along * half) + new Vector2(0, width * 0.6f);
        into.DrawLine(hilt, tip, color, width, antialiased: true);

        // 손잡이 쪽 짧은 가로대. 날의 방향이 한눈에 잡힌다.
        Vector2 guard = new Vector2(-along.Y, along.X) * (width * 2.2f);
        into.DrawLine(hilt - guard, hilt + guard, color, width * 0.8f, antialiased: true);

        // 긁힌 자국 셋. 바닥선 바로 위에 짧게 — 날이 땅에 닿아 있다는 증거다.
        var faded = new Color(color.R, color.G, color.B, color.A * 0.65f);
        for (int i = 1; i <= 3; i++)
        {
            float t = i / 4.0f;
            Vector2 at = hilt.Lerp(tip, t);
            into.DrawLine(
                new Vector2(at.X, 0),
                new Vector2(at.X - (along.X * width * 3.0f), 0),
                faded,
                width * 0.7f,
                antialiased: true);
        }

        return tip;
    }

    /// <summary>보스가 보는 쪽(+1 · -1). 표지의 x 가 이미 뒤집혀 들어오므로 그 부호가 곧 방향이다.</summary>
    public static float Facing(BossTell tell) => Mathf.Sign((float)tell.X) == 0 ? 1 : Mathf.Sign((float)tell.X);
}
