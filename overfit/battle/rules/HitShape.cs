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

    /// <summary>몸이 모양 전체의 <b>가로 범위 밖</b>이다 — 멀어서.</summary>
    TooFar,

    /// <summary>가로로는 안인데 <b>세로 범위 밖</b>이다 — 넘었거나 아래에 섰다.</summary>
    ByHeight,

    /// <summary>외곽 상자 안인데 <b>빈 칸</b>이다 — 초승달 안쪽 같은 곳. 옛 "안쪽 주머니" 도 여기다.</summary>
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
    /// <summary>
    /// <see cref="Reach"/> 의 높이 — "높이를 안 본다" 를 사각형으로 말하는 값이다. 옛 공격 판정(Strike)은
    /// 가로 거리만 봤고, 그림의 모양으로 갈아끼우기(설계 §10 의 2번) 전까지 그 성질을 그대로 옮긴다.
    /// </summary>
    private const double _unbounded = 1_000_000;

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
    /// 옛 공격 사거리를 모양으로 옮긴다 — 좌우로 <paramref name="reach"/>, 높이는 안 본다.
    /// 옛 판정(<c>|dx| − 보스 반폭 ≤ 사거리</c>)은 몸통 대 사각형으로 옮겨도 가로가 정확히 같다:
    /// 보스 몸통(중심 ± 반폭)이 이 사각형과 겹치는 조건이 바로 그 부등식이다.
    /// </summary>
    public static HitShape Reach(double reach) =>
        new(new[] { new HitRect(-reach, reach, -_unbounded, _unbounded) });

    /// <summary>
    /// 공격자 기준 사각형 하나를 월드로 놓는다. <b>놓는 계산은 여기 하나다</b> — 판정(<see cref="ShapeHit"/>)과
    /// 디버그 표시(<see cref="Place"/>)가 같은 함수를 부르므로 둘이 다른 자리를 가리킬 수 없다.
    /// </summary>
    public static HitRect ToWorld(HitRect local, Placement at) =>
        at.Facing >= 0
            ? new HitRect(at.X + local.X0, at.X + local.X1, at.Y + local.Y0, at.Y + local.Y1)
            : new HitRect(at.X - local.X1, at.X - local.X0, at.Y + local.Y0, at.Y + local.Y1);

    /// <summary>
    /// 월드에 놓은 사각형 전부. <b>디버그 표시용이다</b> — 판정은 이것을 안 부른다
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

        return ShapeContact.ByGap;
    }
}
