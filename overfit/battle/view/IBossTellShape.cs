using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// 보스 선딜에 "무엇이 오는가" 를 그리는 모양 하나.
///
/// <para>
/// <b>왜 인터페이스 + 등록표인가.</b> 예고는 패턴 종류만큼 늘어나는 자리다. 여기에 <c>switch</c> 를
/// 두면 패턴을 하나 더할 때마다 이 파일을 열게 되고, 그 <c>switch</c> 는 곧 "패턴이 무엇인가" 의
/// 두 번째 진실이 된다 — <c>patterns.json</c> 과 어긋나도 아무것도 빨개지지 않는 종류의 두 번째다.
/// 구현은 자기 모양만 알고, 어느 패턴이 그것을 쓰는지는 <b>데이터가</b> 정한다
/// (<c>patterns.json</c> 의 <c>tell.id</c>). 기존 모양을 쓰는 네 번째 패턴은 C# 을 한 줄도 안 연다.
/// </para>
///
/// <para>
/// 구현은 <b>Node 가 아니다.</b> 그리는 노드는 <see cref="BossTellLayer"/> 하나뿐이고 모양은
/// 그 캔버스에 선을 얹을 뿐이다 — 모양마다 노드를 만들면 패턴 하나를 더하는 것이
/// 씬 트리를 바꾸는 일이 된다.
/// </para>
/// </summary>
public interface IBossTellShape
{
    /// <summary>
    /// <paramref name="into"/> 의 로컬 좌표에 그린다. 원점이 <b>보스 발밑</b>이고
    /// <b>화면 좌표라 y 는 아래가 +</b> 다 — 규칙의 높이 h 는 여기서 -h 다.
    /// </summary>
    /// <param name="into">그릴 캔버스. <c>_Draw</c> 안에서만 부른다.</param>
    /// <param name="tell">이 패턴의 표지 위치와 크기. 이미 보스가 보는 쪽으로 뒤집혀 있다.</param>
    /// <param name="width">선 두께(px). 이 크기의 화면에서 가늘면 아예 안 읽힌다.</param>
    /// <param name="color">색. <b>패리 가능 여부와 무르익음이 이미 섞여 있다</b> —
    /// 모양이 그걸 다시 판단하면 크림슨의 진실이 두 곳에 생긴다.</param>
    void Draw(CanvasItem into, BossTell tell, float width, Color color);
}
