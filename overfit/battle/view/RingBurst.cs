using System;
using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// 원과 스파크를 그리는 이펙트 노드 하나. 파이터의 링 — 가드 링 · 받아친 고리 · 가드가 받아낸 고리 · 붕괴의 큰 고리 ·
/// 공격 섬광 — 이 <b>같은 그림</b>이라 한 클래스로 둔다. 크기와 색만 부르는 쪽(<c>FighterView</c>)이 정한다.
///
/// <para>
/// 보스의 선딜 예고 링과 판정 충격파도 이 클래스였는데 걷었다 (#81 — 유저: "적 공격에 동그라미 연출은 제거해주세요
/// 전체적으로 캐릭터는 남겨놔도 됩니다." 2026-09-26). 그래서 지금 부르는 쪽은 파이터뿐이다.
/// </para>
///
/// <para>
/// 두 가지 모드가 있다.
/// <list type="bullet">
/// <item><see cref="Charge"/> — <b>프레임마다 다시 준다.</b> 자세가 서 있는 동안의 링이다(지금은 가드 링 하나).
/// 안 주는 프레임엔 <b>저절로 사라진다</b> —
/// 끄는 메서드를 일부러 안 만들었다. 끄는 것을 잊어 링이 남는 사고를 구조로 없앤다.</item>
/// <item><see cref="Burst"/> — <b>한 번 주면 스스로 끝난다.</b> 받아침 · 막음 · 붕괴 · 칼질 같은 순간이다.</item>
/// </list>
/// 창(charge)은 상태고 순간(burst)은 사건이라 시계를 따로 둔다. 하나로 합치면 창이 닫히는
/// 프레임에 성공 섬광이 같이 지워진다 — 정확히 그 순간에 보여야 하는 것인데.
/// </para>
/// </summary>
public partial class RingBurst : Node2D
{
    private static readonly float[] _noSparks = Array.Empty<float>();

    private bool _chargeOn;
    private bool _chargeGiven;
    private float _chargeRadius;
    private Color _chargeColor;

    private double _burstAge;
    private double _burstLife;
    private float _burstFrom;
    private float _burstTo;
    private Color _burstColor;
    private float _sparkLength;
    private float[] _sparkAngles = _noSparks;

    /// <summary>선 두께(px). 이 크기의 화면에서 가늘면 아예 안 읽힌다 — 값은 balance.json 의 feel 이 정한다.</summary>
    public float LineWidth { get; set; } = 6.0f;

    /// <summary>창이 열려 있는 동안의 링. <b>프레임마다 다시 부른다</b> — 안 부르면 그 프레임에 사라진다.</summary>
    public void Charge(float radius, Color color)
    {
        _chargeGiven = true;
        _chargeRadius = radius;
        _chargeColor = color;
    }

    /// <summary>순간의 섬광. 스스로 <paramref name="seconds"/> 동안 퍼지고 옅어진다.</summary>
    public void Burst(float from, float to, double seconds, Color color, int sparks, float sparkLength)
    {
        _burstAge = 0;
        _burstLife = seconds;
        _burstFrom = from;
        _burstTo = to;
        _burstColor = color;
        _sparkLength = sparkLength;

        // 고르게 퍼뜨리되 조금 흔든다 — 완전히 고르면 톱니바퀴처럼 보이고 "터졌다" 가 안 된다.
        // ⚠ 여기 난수는 **뷰 전용**이다. Det 를 안 쓴다 — 규칙이 아니라 그림이라 같은 시드가
        //   같은 스파크 각도를 낼 이유가 없고, 리플레이는 입력과 시드만 저장하므로 영향도 없다.
        _sparkAngles = new float[Math.Max(0, sparks)];
        for (int i = 0; i < _sparkAngles.Length; i++)
        {
            float even = Mathf.Tau * i / _sparkAngles.Length;
            _sparkAngles[i] = even + ((GD.Randf() - 0.5f) * Mathf.Tau / (_sparkAngles.Length * 2));
        }
    }

    public override void _Process(double delta)
    {
        // 이번 프레임에 Charge 를 받았나. 받은 것만 그리므로, 창이 닫히면 부르는 쪽이
        // 아무것도 안 하는 것만으로 링이 사라진다 — 끄는 것을 잊어 링이 남는 사고가 없다.
        _chargeOn = _chargeGiven;
        _chargeGiven = false;

        if (_burstLife > 0)
        {
            _burstAge += delta;
            if (_burstAge >= _burstLife)
            {
                _burstLife = 0;
                _sparkAngles = _noSparks;
            }
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_chargeOn && _chargeRadius > 1.0f)
        {
            DrawArc(Vector2.Zero, _chargeRadius, 0, Mathf.Tau, 64, _chargeColor, LineWidth, antialiased: true);
        }

        if (_burstLife <= 0)
        {
            return;
        }

        float t = (float)(_burstAge / _burstLife);
        // 처음이 빠르고 끝이 느리다. 선형이면 "퍼진다" 가 아니라 "커진다" 로 보인다.
        float eased = 1.0f - ((1.0f - t) * (1.0f - t));
        float radius = Mathf.Lerp(_burstFrom, _burstTo, eased);
        var fade = new Color(_burstColor.R, _burstColor.G, _burstColor.B, _burstColor.A * (1.0f - t));

        if (radius > 1.0f)
        {
            DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 64, fade, LineWidth * (1.0f - (t * 0.6f)), antialiased: true);
        }

        foreach (float angle in _sparkAngles)
        {
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float inner = radius * 0.55f;
            DrawLine(dir * inner, dir * (inner + (_sparkLength * (1.0f - t))), fade, LineWidth * 0.7f, antialiased: true);
        }
    }
}
