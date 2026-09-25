using System;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>판정 모양 (이슈 #59 · 설계 §3). 몸통 사각형을 모양에 대 보고, 안 닿으면 왜인지를 말한다.</summary>
public class HitShapeTests
{
    /// <summary>보스 발밑 (1000, 0) 에서 오른쪽을 본다.</summary>
    private static readonly Placement _right = new(1000, 0, 1);

    /// <summary>같은 자리에서 왼쪽을 본다.</summary>
    private static readonly Placement _left = new(1000, 0, -1);

    /// <summary>폭 60 · 키 120 몸통 — 발 중심이 x, 발바닥이 y.</summary>
    private static HitRect Body(double x, double y = 0) => new(x - 30, x + 30, y, y + 120);

    /// <summary>앞으로 100~300, 바닥에서 0~200 한 장.</summary>
    private static HitShape Front() => new(new[] { new HitRect(100, 300, 0, 200) });

    [Fact]
    public void 겹치면_Overlap_이다()
    {
        ShapeHit.Test(Front(), _right, Body(1200)).ShouldBe(ShapeContact.Overlap);
    }

    [Fact]
    public void 보는_쪽이_바뀌면_좌우가_뒤집힌다()
    {
        // 오른쪽을 보면 1100~1300 을 치고, 왼쪽을 보면 700~900 을 친다.
        ShapeHit.Test(Front(), _left, Body(800)).ShouldBe(ShapeContact.Overlap);
        ShapeHit.Test(Front(), _left, Body(1200))
            .ShouldBe(ShapeContact.TooFar, "왼쪽을 보는데 오른쪽 몸이 맞았다 — 뒤집기가 빠졌다");
    }

    [Fact]
    public void 가로_범위_밖이면_TooFar_다()
    {
        ShapeHit.Test(Front(), _right, Body(1400)).ShouldBe(ShapeContact.TooFar);
    }

    [Fact]
    public void 위로_넘으면_ByHeight_다()
    {
        // 판정 윗끝 200 · 발바닥 201 — 한 픽셀 위.
        ShapeHit.Test(Front(), _right, Body(1200, y: 201)).ShouldBe(ShapeContact.ByHeight);
    }

    [Fact]
    public void 초승달_안쪽의_빈_곳은_ByGap_이다()
    {
        // 두 장 사이(−100~100)가 비어 있다. 몸(−30~30)이 거기 서면 외곽 상자 안인데 안 닿는다.
        var crescent = new HitShape(new[]
        {
            new HitRect(-300, -100, 0, 200),
            new HitRect(100, 300, 0, 200),
        });

        ShapeHit.Test(crescent, _right, Body(1000)).ShouldBe(ShapeContact.ByGap);
    }

    [Fact]
    public void 가장자리가_닿아도_겹친_것이다()
    {
        // 판정 앞끝 1300 · 몸 왼끝 1300. 옛 거리 띠가 경계값을 맞은 것으로 쳤던 것을 그대로 잇는다.
        ShapeHit.Test(Front(), _right, Body(1330)).ShouldBe(ShapeContact.Overlap);
    }

    [Fact]
    public void Band_는_좌우_대칭_두_장이다()
    {
        HitShape band = HitShape.Band(50, 250, 0, 340);

        band.Local.Count.ShouldBe(2);
        ShapeHit.Test(band, _right, Body(1150)).ShouldBe(ShapeContact.Overlap);
        ShapeHit.Test(band, _right, Body(850)).ShouldBe(ShapeContact.Overlap, "옛 띠는 좌우를 안 가렸다");
        ShapeHit.Test(band, _right, Body(1000))
            .ShouldBe(ShapeContact.ByGap, "안쪽 50 은 비어 있다 — 옛 안쪽 주머니가 이것이다");
    }

    [Fact]
    public void 몸보다_좁은_틈에는_못_숨는다()
    {
        // 안쪽 20 — 틈 폭 40 이 몸 폭 60 보다 좁다. 옛 판정(몸을 점으로 봤다)에서는 숨을 수 있었다.
        ShapeHit.Test(HitShape.Band(20, 250, 0, 340), _right, Body(1000)).ShouldBe(ShapeContact.Overlap);
    }

    [Fact]
    public void 외곽_상자는_모든_사각형을_덮는다()
    {
        var shape = new HitShape(new[] { new HitRect(-120, -20, 40, 90), new HitRect(60, 260, 0, 30) });

        shape.Bounds.ShouldBe(new HitRect(-120, 260, 0, 90));
    }

    [Fact]
    public void 빈_모양은_거절한다()
    {
        Should.Throw<ArgumentException>(() => new HitShape(Array.Empty<HitRect>()));
    }

    [Fact]
    public void 뒤집힌_사각형은_거절한다()
    {
        Should.Throw<ArgumentException>(() => new HitShape(new[] { new HitRect(300, 100, 0, 200) }));
    }

    [Fact]
    public void Place_와_판정이_같은_자리를_가리킨다()
    {
        // 표시는 Place 를, 판정은 ShapeHit 을 쓴다. 둘이 다른 자리를 말하면 디버그 화면이 거짓말을 한다.
        var shape = new HitShape(new[]
        {
            new HitRect(-120, -20, 40, 90),
            new HitRect(60, 260, 0, 30),
            new HitRect(80, 140, 150, 300),
        });

        foreach (Placement at in new[] { _right, _left })
        {
            for (double x = 600; x <= 1400; x += 10)
            {
                for (double y = 0; y <= 320; y += 40)
                {
                    HitRect body = Body(x, y);
                    bool drawn = shape.Place(at).Any(r => r.Overlaps(body));
                    bool ruled = ShapeHit.Test(shape, at, body) == ShapeContact.Overlap;
                    ruled.ShouldBe(drawn, $"facing={at.Facing} x={x} y={y}");
                }
            }
        }
    }
}
