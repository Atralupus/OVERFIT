using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// <b>뛰어올라 내리꽂는다</b> — 점프 강타가 온다는 표지 (이슈 #28 · 나인 솔즈 백장).
///
/// <para>
/// 이 패턴은 패리가 안 되는 크림슨이고, 색은 부르는 쪽이 이미 정해 넘긴다. 모양이 더할 것은
/// <b>어디가 안전한가</b> 다: 바닥에 그리는 고리가 착지 충격의 <b>안쪽 빈 곳</b>(<c>tell.length</c>)이라,
/// 그 안에 서 있으면 산다. "패리 불가 = 대시로만" 위에 "혹은 파고들어라" 가 얹히는 자리이고,
/// 몸 충돌을 걷은 뒤(이슈 #27) <c>distance_bias</c> 축을 살려 두는 것이 이 주머니 하나다 —
/// 화면이 그 주머니를 안 그리면 플레이어에게는 없는 선택지다.
/// </para>
/// </summary>
public sealed class LeapMarkTell : IBossTellShape
{
    public void Draw(CanvasItem into, BossTell tell, float width, Color color)
    {
        System.ArgumentNullException.ThrowIfNull(into);

        // 바닥의 안전 주머니. 납작한 타원으로 그린다 — 정원으로 그리면 가로 게임에서
        // "공중에 뜬 고리" 로 읽혀 바닥이라는 것이 안 보인다.
        float radius = (float)tell.Length;
        const int steps = 48;
        var ring = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Tau * i / steps;
            ring[i] = new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * 0.22f);
        }

        into.DrawPolyline(ring, color, width, antialiased: true);

        // 내리꽂는 화살표. 표지 높이에서 바닥으로 — 방향이 곧 "위에서 온다" 다.
        var from = new Vector2((float)tell.X, (float)-tell.Y);
        var to = new Vector2((float)tell.X, -width * 2.0f);
        into.DrawLine(from, to, color, width, antialiased: true);

        float head = width * 4.0f;
        into.DrawLine(to, to + new Vector2(-head, -head), color, width, antialiased: true);
        into.DrawLine(to, to + new Vector2(head, -head), color, width, antialiased: true);
    }
}
