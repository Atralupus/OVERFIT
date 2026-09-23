using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// <b>칼을 땅에 끌고 있다</b> — 내려찍기가 온다는 표지 (이슈 #28 · 나인 솔즈 백장).
///
/// <para>
/// 계열의 <b>기본형</b>이다 (이슈 #48). 1단계의 <c>내려찍기 I</c> 하나가 이것을 쓰고, 변종들은
/// 같은 칼(<see cref="DragBlade"/>) 위에 자기 표시를 하나씩 얹는다 — 아홉이 같은 기술이라
/// 표지도 같은 그림에서 갈라져야 "같은 계열의 변종" 으로 읽힌다.
/// </para>
///
/// <para>
/// 날을 바닥과 나란히 긋고 그 아래로 긁힌 자국을 남긴다. 자국은 바닥에 <b>닿아 있다</b> 는 것을
/// 한눈에 만드는 장치다 — 날만 그리면 높이 차이로만 갈려 순간적으로는 다른 그림과 같아진다.
/// </para>
/// </summary>
public sealed class BladeDragTell : IBossTellShape
{
    public void Draw(CanvasItem into, BossTell tell, float width, Color color) =>
        DragBlade.Draw(into, tell, width, color);
}
