using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// <b>칼을 땅에서 떼어 뒤로 뻗었다</b> — 올려베기가 온다는 표지 (이슈 #28 · 나인 솔즈 백장).
///
/// <para>
/// <see cref="BladeDragTell"/> 와 <b>짝으로 읽히도록</b> 만든 모양이다. 같은 날을 그리되 셋을 뒤집는다:
/// 바닥에서 떠 있고(<c>tell.y</c>), 등 뒤에 있고(<c>tell.x</c> 가 음수), 위를 향한다. 날 끝에서
/// 바닥까지 가는 점선이 <b>떠 있다는 사실 자체</b>를 그린다 — 높이만으로는 "조금 높은 칼" 로 보이지
/// "땅에서 뗐다" 로 안 보인다. 그 둘을 못 가르면 플레이어는 두 패턴을 못 가른다.
/// </para>
/// </summary>
public sealed class BladeRaisedTell : IBossTellShape
{
    public void Draw(CanvasItem into, BossTell tell, float width, Color color)
    {
        System.ArgumentNullException.ThrowIfNull(into);

        var mid = new Vector2((float)tell.X, (float)-tell.Y);
        float half = (float)tell.Length / 2.0f;

        // 칼끝이 위를 본다. 등 뒤(x<0)면 왼쪽 위로, 앞이면 오른쪽 위로 기운다.
        float lean = tell.X < 0 ? -1.0f : 1.0f;
        var along = new Vector2(lean * 0.45f, -0.89f).Normalized();

        Vector2 hilt = mid - (along * half);
        Vector2 tip = mid + (along * half);
        into.DrawLine(hilt, tip, color, width, antialiased: true);

        Vector2 guard = new Vector2(-along.Y, along.X) * (width * 2.2f);
        into.DrawLine(hilt - guard, hilt + guard, color, width * 0.8f, antialiased: true);

        // 칼끝의 갈매기표. "여기서 위로 올라온다" 를 방향으로 말한다.
        Vector2 wing = new Vector2(-along.Y, along.X) * (width * 3.0f);
        Vector2 back = tip - (along * width * 4.0f);
        into.DrawLine(tip, back - wing, color, width * 0.8f, antialiased: true);
        into.DrawLine(tip, back + wing, color, width * 0.8f, antialiased: true);

        // 바닥까지의 점선. **이 선이 "땅에서 뗐다" 를 그린다.**
        var faded = new Color(color.R, color.G, color.B, color.A * 0.5f);
        float top = hilt.Y;
        for (float y = top; y < 0; y += width * 3.0f)
        {
            into.DrawLine(
                new Vector2(hilt.X, y),
                new Vector2(hilt.X, Mathf.Min(0, y + (width * 1.5f))),
                faded,
                width * 0.6f,
                antialiased: true);
        }
    }
}
