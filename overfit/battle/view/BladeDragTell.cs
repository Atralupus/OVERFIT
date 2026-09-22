using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// <b>칼을 땅에 끌고 있다</b> — 내려찍기가 온다는 표지 (이슈 #28 · 나인 솔즈 백장).
///
/// <para>
/// 백장의 두 근접 패턴은 <b>칼이 땅에 있나 떠 있나</b> 로만 갈린다. 그래서 이 모양이 말해야 하는 것은
/// 하나뿐이다: <b>날이 바닥선 위에 누워 있다.</b> 날을 바닥과 나란히 긋고 그 아래로 긁힌 자국을
/// 남긴다 — 자국은 바닥에 <b>닿아 있다</b> 는 것을 한눈에 만드는 장치라, 날만 그리면
/// <see cref="BladeRaisedTell"/> 와 높이 차이로만 갈려 순간적으로는 같은 그림이 된다.
/// </para>
/// </summary>
public sealed class BladeDragTell : IBossTellShape
{
    public void Draw(CanvasItem into, BossTell tell, float width, Color color)
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
    }
}
