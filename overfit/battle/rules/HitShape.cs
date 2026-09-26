using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 축 정렬 사각형. 좌표계는 쓰는 자리가 정한다 — <see cref="HitShape"/> 안에서는 <b>공격자 기준</b>
/// (x 는 몸 중심에서 보는 쪽이 +, y 는 발바닥에서 위로), 월드에서는 아레나 좌표(위가 +)다.
/// </summary>
public readonly record struct HitRect(double X0, double X1, double Y0, double Y1)
{
    /// <summary>
    /// 겹치나. <b>가장자리만 닿아도 겹친 것이다</b> — 옛 판정(거리 띠 · 높이 띠)이 경계값을 맞은 것으로
    /// 쳤고, 모양으로 옮기면서 그 경계를 뒤집지 않는다. 뒤집으면 같은 자리가 반 틱마다 다른 답을 낸다.
    /// </summary>
    public bool Overlaps(HitRect other) =>
        X0 <= other.X1 && other.X0 <= X1 && Y0 <= other.Y1 && other.Y0 <= Y1;
}

/// <summary>판정 모양을 어디에 놓나 — 공격자의 발 중심 · 발바닥 높이 · 보는 쪽.</summary>
/// <param name="X">발 중심 x (월드).</param>
/// <param name="Y">발바닥 높이 (월드, 바닥 0).</param>
/// <param name="Facing">-1 왼쪽 · +1 오른쪽. 0 은 오른쪽으로 친다.</param>
public readonly record struct Placement(double X, double Y, int Facing);

/// <summary>판정 모양이 몸에 어떻게 닿았나. <c>HitResolver</c> 가 이것을 판정 결과로 옮긴다.</summary>
public enum ShapeContact
{
    /// <summary>사각형 하나 이상이 몸과 겹친다.</summary>
    Overlap,

    /// <summary>
    /// 몸이 모양의 <b>끝 너머</b>다 — 멀어서. 모양 전체의 가로 범위 밖이거나, 몸 높이에서 모양이 뻗은 앞끝 너머 · 보스 등 뒤로
    /// 뒤끝 너머다(<see cref="ShapeHit.Test"/>).
    /// </summary>
    TooFar,

    /// <summary>가로로는 안인데 <b>세로 범위 밖</b>이다 — 넘었거나 아래에 섰다.</summary>
    ByHeight,

    /// <summary>모양 <b>안쪽의 빈 칸</b>이다 — 초승달 안쪽 같은 곳. 옛 "안쪽 주머니" 도 여기다.</summary>
    ByGap,
}

/// <summary>
/// 판정 모양 — 공격자 기준 사각형 여러 장 (이슈 #59 · 설계 §3).
///
/// <para>
/// 왜 여러 장인가: 그림의 칼 궤적은 초승달이라 바운딩 박스 하나로 두면 <c>attack3</c> 은 상자의 71% 가
/// 빈 공간이다 — 초승달 한가운데 서 있어도 맞는다. 보이는 것이 곧 맞는 것이어야 하므로 흰 픽셀을 격자로
/// 떠서(<c>tools/extract_hitboxes.py</c>) 줄마다 합친 사각형으로 든다.
/// </para>
/// </summary>
public sealed class HitShape
{
    private readonly HitRect[] _local;

    public HitShape(IReadOnlyList<HitRect> local)
    {
        ArgumentNullException.ThrowIfNull(local);
        if (local.Count == 0)
        {
            throw new ArgumentException("빈 판정 모양이다 — 아무것도 안 치는 판정은 판정이 아니다", nameof(local));
        }

        _local = new HitRect[local.Count];
        double x0 = double.PositiveInfinity, x1 = double.NegativeInfinity;
        double y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
        for (int i = 0; i < local.Count; i++)
        {
            HitRect r = local[i];
            if (r.X0 > r.X1 || r.Y0 > r.Y1)
            {
                throw new ArgumentException($"뒤집힌 사각형 #{i} — {r}", nameof(local));
            }

            _local[i] = r;
            x0 = Math.Min(x0, r.X0);
            x1 = Math.Max(x1, r.X1);
            y0 = Math.Min(y0, r.Y0);
            y1 = Math.Max(y1, r.Y1);
        }

        Bounds = new HitRect(x0, x1, y0, y1);
    }

    /// <summary>공격자 기준 사각형들.</summary>
    public IReadOnlyList<HitRect> Local => _local;

    /// <summary>
    /// 모든 사각형을 덮는 외곽 상자 (공격자 기준). 빗나간 이유(<see cref="ShapeContact"/>)를 가르는 잣대다.
    /// </summary>
    public HitRect Bounds { get; }

    /// <summary>
    /// 옛 거리 띠 · 높이 띠를 모양으로 옮긴다 — <b>좌우 대칭 두 장</b>. 옛 판정은 보스 중심에서의
    /// 거리를 <c>Math.Abs</c> 로 재서 좌우를 안 가렸고, 그 성질을 그대로 옮긴다. 안쪽 끝
    /// (<paramref name="minDistance"/>)이 0 보다 크면 두 장 사이가 빈다 — 옛 "안쪽 주머니" 다.
    /// </summary>
    public static HitShape Band(double minDistance, double maxDistance, double lowHeight, double highHeight) =>
        new(new[]
        {
            new HitRect(minDistance, maxDistance, lowHeight, highHeight),
            new HitRect(-maxDistance, -minDistance, lowHeight, highHeight),
        });

    /// <summary>
    /// 공격자 기준 사각형 하나를 월드로 놓는다. <b>놓는 계산은 여기 하나다</b> — 판정(<see cref="ShapeHit"/>)과
    /// 뷰가 읽는 사각형(<see cref="Place"/>)이 같은 함수를 부르므로 둘이 다른 자리를 가리킬 수 없다.
    /// </summary>
    public static HitRect ToWorld(HitRect local, Placement at) =>
        at.Facing >= 0
            ? new HitRect(at.X + local.X0, at.X + local.X1, at.Y + local.Y0, at.Y + local.Y1)
            : new HitRect(at.X - local.X1, at.X - local.X0, at.Y + local.Y0, at.Y + local.Y1);

    /// <summary>
    /// 월드에 놓은 사각형 전부. <b>뷰가 읽는다</b> — 판정 보기(<c>HitboxDebug</c>)가 대 본 판정과 다음 판정의 사각형으로, 착지
    /// 충격파(<c>FloorWave</c> · #83)가 <c>BattleSim.BossTestedRects</c> 로 받는다. 판정은 이것을 안 부른다
    /// (<see cref="ShapeHit.Test"/> 는 매 틱 목록을 만들지 않고 한 장씩 놓아 본다).
    /// </summary>
    public IReadOnlyList<HitRect> Place(Placement at)
    {
        var placed = new HitRect[_local.Length];
        for (int i = 0; i < _local.Length; i++)
        {
            placed[i] = ToWorld(_local[i], at);
        }

        return placed;
    }
}

/// <summary>판정 모양을 몸에 대 본다. <b>상태가 없다.</b></summary>
public static class ShapeHit
{
    /// <summary>
    /// 몸이 모양에 닿았나, 안 닿았으면 왜인가.
    ///
    /// <para>
    /// 빗나간 이유는 <b>외곽 상자</b>로 가른다 (설계 §7.1): 가로 범위 밖이면 멀어서, 가로로는 안인데
    /// 세로 범위 밖이면 넘어서, 둘 다 안인데 안 닿았으면 틈에 서서다. 가로를 먼저 보는 것은 옛 판정의
    /// 순서(거리 → 높이)를 잇기 위해서다.
    /// </para>
    ///
    /// <para>
    /// 외곽 상자 안에서도 <b>몸 높이에서 모양이 뻗은 끝 너머</b>는 멀어서다 (#72). 그림에서 뽑은 모양은 높이마다 끝이 다르다 —
    /// <c>attack2/2</c>(설계 §4.1 의 3연격 2타)는 땅에 선 몸 높이에서 앞으로 +396 까지만 치고 그 위에서는 +440 까지 친다. 외곽
    /// 상자만 보면 그 사거리 바로 밖(+437)에 선 사람이 "틈" 이 되고, 거리 축(<c>PlayerAxes.DistanceBias</c>)이 틈을 음수로 실어 간격을 둔 사람을 보스에 붙은 사람으로
    /// 읽는다. 뒤도 같다: 몸 높이에 등 뒤를 치는 사각형이 없는데 몸이 보스 중심 뒤에 있으면 보스를 돌아 나간 것이지 품에 든 것이
    /// 아니다(설계 §3.6 ② — "등 뒤를 틈으로 치면 '초승달을 끌어안았다' 와 '보스를 돌아 나갔다' 가 한 점이 된다"). 남는 틈은
    /// 몸 높이의 궤적 사이와, 보스 중심과 앞 궤적 사이(품 안)다 — 몸 높이에 사각형이 하나도 없으면 모양 안의 구멍이라 틈이다.
    /// </para>
    /// </summary>
    public static ShapeContact Test(HitShape shape, Placement at, HitRect body)
    {
        ArgumentNullException.ThrowIfNull(shape);

        IReadOnlyList<HitRect> local = shape.Local;
        for (int i = 0; i < local.Count; i++)
        {
            if (HitShape.ToWorld(local[i], at).Overlaps(body))
            {
                return ShapeContact.Overlap;
            }
        }

        HitRect bounds = HitShape.ToWorld(shape.Bounds, at);
        if (body.X1 < bounds.X0 || body.X0 > bounds.X1)
        {
            return ShapeContact.TooFar;
        }

        if (body.Y1 < bounds.Y0 || body.Y0 > bounds.Y1)
        {
            return ShapeContact.ByHeight;
        }

        return BeyondAtHeight(local, at, body) ? ShapeContact.TooFar : ShapeContact.ByGap;
    }

    /// <summary>
    /// 몸이 <b>몸 높이에서</b> 모양이 뻗은 끝 너머인가 — 앞끝 너머이거나, 보스 중심 뒤로 뒤끝 너머다(<see cref="Test"/> 의 둘째 문단).
    /// 공격자 기준(보는 쪽이 +)으로 옮겨 잰다. 몸 높이에 사각형이 없으면 가를 끝이 없어 거짓이다.
    /// </summary>
    private static bool BeyondAtHeight(IReadOnlyList<HitRect> local, Placement at, HitRect body)
    {
        double x0 = at.Facing >= 0 ? body.X0 - at.X : at.X - body.X1;
        double x1 = at.Facing >= 0 ? body.X1 - at.X : at.X - body.X0;
        double y0 = body.Y0 - at.Y;
        double y1 = body.Y1 - at.Y;

        double near = double.PositiveInfinity, far = double.NegativeInfinity;
        for (int i = 0; i < local.Count; i++)
        {
            HitRect r = local[i];
            if (r.Y0 <= y1 && y0 <= r.Y1)
            {
                near = Math.Min(near, r.X0);
                far = Math.Max(far, r.X1);
            }
        }

        return far >= near && (x0 > far || x1 < Math.Min(near, 0));
    }
}
