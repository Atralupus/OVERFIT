using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 예고 모양 id → 구현. <c>patterns.json</c> 의 <c>tell.id</c> 가 이 표를 조회한다.
///
/// <para>
/// <b>이 표가 유일한 분기다.</b> 패턴이 늘어나는 자리에 <c>switch</c> 를 두지 않기로 한 대가로
/// 한 곳만 남겼다 — 새 <b>모양</b>은 여기 한 줄이고, 있는 모양을 쓰는 새 <b>패턴</b>은
/// 데이터만 고친다. 둘을 구별하는 것이 요점이다: 패턴은 자주 늘고 모양은 드물게 는다.
/// </para>
/// </summary>
public static class BossTellShapes
{
    private static readonly Dictionary<string, IBossTellShape> _byId = new(System.StringComparer.Ordinal)
    {
        ["blade_drag"] = new BladeDragTell(),
        ["blade_raised"] = new BladeRaisedTell(),
        ["leap_mark"] = new LeapMarkTell(),
    };

    /// <summary>
    /// <paramref name="id"/> 의 모양. 모르는 id 면 <b>null 과 <c>[W]</c></b> 다.
    /// <c>[E]</c> 가 아닌 이유: 예고가 안 그려지는 것은 그림이 모자란 것이지 규칙 위반이 아니고,
    /// <c>[E]</c> 한 줄은 헤드리스 판정을 통째로 실패시킨다. 대신 <b>매 프레임 찍지 않는다</b> —
    /// 한 번 본 id 는 다시 경고하지 않는다. 안 그러면 로그가 이 한 줄로 뒤덮여
    /// 판정이 읽어야 할 것이 묻힌다.
    /// </summary>
    public static IBossTellShape? Find(string id)
    {
        if (_byId.TryGetValue(id, out IBossTellShape? shape))
        {
            return shape;
        }

        if (_warned.Add(id))
        {
            Log.Warn("view", $"tell_shape_missing id={id}");
        }

        return null;
    }

    private static readonly HashSet<string> _warned = new(System.StringComparer.Ordinal);
}
