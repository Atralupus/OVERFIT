using System.Collections.Generic;
using Godot;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 폭탄 (설계 2026-09-30 조각2 §5) — 선딜 동안 손 위의 폭탄, 나는 폭탄, 보스 몸에서 터지는 불꽃, 끊겨 잃은 폭탄의 연기. 팩에 폭탄 그림이 없어
/// 코드로 그린다(흰 구 · 착지 충격파와 같다). 규칙은 이 노드를 모른다 — 무엇이 언제인지는 <see cref="BombFrame"/> 과 사건 메서드가 말한다.
///
/// <para>
/// <b>나는 폭탄은 보스의 지금 자리로 간다.</b> 규칙이 나는 동안 보스를 따라가 떨어뜨리므로(§1.3) 그림도 프레임마다 보스를 다시 잰다 — 놓은
/// 자리에서 보스 몸 가운데로 가는 선 위에 포물선(<c>feel.bomb_arc_height</c>)을 얹는다. 보스가 달리면 폭탄이 휘어 따라간다: 그것이 규칙의 말이다.
/// </para>
///
/// <para>
/// 색은 이름 붙은 상수다 — 수치는 데이터, 색은 "얼마나" 가 아니라 "무엇" 이라는 규약(<c>FeelBalance</c> 머리) 그대로다.
/// </para>
/// </summary>
public partial class BombView : Node2D
{
    /// <summary>폭탄의 몸 — 거의 검은색. 어두운 배경에서도 테두리(<see cref="_rim"/>)가 모양을 잡는다.</summary>
    private static readonly Color _body = new(0.10f, 0.10f, 0.12f, 1f);

    private static readonly Color _rim = new(0.62f, 0.62f, 0.68f, 1f);

    /// <summary>심지의 불꽃 — 깜빡인다. 폭탄이 "곧 터진다" 는 것을 말하는 유일한 움직임이다.</summary>
    private static readonly Color _spark = new(1.00f, 0.74f, 0.28f, 1f);

    /// <summary>터지는 불꽃 원 — 주황. 가드 고리(보라)와 색으로 갈린다.</summary>
    private static readonly Color _boom = new(1.00f, 0.56f, 0.16f, 1f);

    /// <summary>끊겨 잃은 폭탄의 연기 — 탁한 회색. 터지지 않았다는 것이 색으로 보인다.</summary>
    private static readonly Color _fizzle = new(0.62f, 0.62f, 0.66f, 0.9f);

    private FeelBalance _feel = null!;
    private RingBurst _burst = null!;
    private RingBurst _puff = null!;

    /// <summary>이 프레임에 그릴 폭탄들의 자리(월드 · 화면 좌표) — 손 위의 것과 나는 것.</summary>
    private readonly List<Vector2> _bombs = new();

    /// <summary>지난 프레임의 손 · 과녁(보스 몸 가운데) — 사건(<see cref="Boom"/> · <see cref="Fizzle"/>)이 그 자리에서 터진다.</summary>
    private Vector2 _hand;

    private Vector2 _target;

    /// <summary>심지가 깜빡이는 시계(초) — 뷰 전용이다. 규칙의 시계를 안 읽는다.</summary>
    private double _clock;

    public override void _Ready()
    {
        _feel = Balance.Data.Feel;

        // 두 몸 위에 그린다 — 손 위의 폭탄이 파이터 몸에 가려지면 "던지고 있다" 가 안 보인다. 흰 구(10)보다는 아래다.
        ZIndex = 9;
        _burst = new RingBurst { LineWidth = (float)_feel.RingWidth * 1.5f };
        AddChild(_burst);
        _puff = new RingBurst { LineWidth = (float)_feel.RingWidth };
        AddChild(_puff);
    }

    /// <summary>한 프레임. <paramref name="frame"/> 의 값은 전부 규칙이 정한 것이다.</summary>
    public void Show(BombFrame frame)
    {
        _clock += GetProcessDeltaTime();
        _hand = Hand(frame.FighterX, frame.FighterY, frame.Facing);
        _target = new Vector2((float)frame.BossX, (float)-(frame.BossY + (frame.BossBodyHeight / 2)));

        _bombs.Clear();
        if (frame.InHand)
        {
            _bombs.Add(_hand);
        }

        foreach (BombArc arc in frame.Flying)
        {
            // 놓은 뒤의 손 — 던질 때 보스 쪽으로 돌아섰으므로(§1.1) 손은 보스 쪽에 있다.
            int toward = frame.BossX >= arc.FromX ? 1 : -1;
            Vector2 from = Hand(arc.FromX, arc.FromY, toward);
            float p = (float)arc.Progress;
            _bombs.Add(from.Lerp(_target, p) + new Vector2(0, -(float)(_feel.BombArcHeight * 4 * p * (1 - p))));
        }

        QueueRedraw();
    }

    /// <summary>폭탄이 보스에게 떨어졌다 — 보스 몸 가운데에서 주황 불꽃 원이 터진다. 보스의 번쩍임과 화면 흔들림은 <c>BattleCues</c> 가 건다.</summary>
    public void Boom()
    {
        _burst.Position = _target;
        _burst.Burst(
            (float)_feel.RingFrom,
            (float)(_feel.RingTo * 2),
            _feel.BombBurstSeconds,
            _boom,
            _feel.SparkCount,
            (float)_feel.SparkLength);
    }

    /// <summary>던지기가 끊겨 폭탄을 잃었다 — 손에서 작은 회색 연기가 흩어진다. 터지지 않았다.</summary>
    public void Fizzle()
    {
        _puff.Position = _hand;
        _puff.Burst(
            (float)_feel.BombRadius,
            (float)(_feel.BombRadius * 3),
            _feel.BurstSeconds * 0.6,
            _fizzle,
            sparks: 0,
            sparkLength: 0);
    }

    public override void _Draw()
    {
        float r = (float)_feel.BombRadius;
        bool lit = ((int)(_clock * 12)) % 2 == 0;
        foreach (Vector2 at in _bombs)
        {
            DrawCircle(at, r, _body);
            DrawArc(at, r, 0, Mathf.Tau, 24, _rim, 2);
            Vector2 fuse = at + new Vector2(r * 0.6f, -r * 1.25f);
            DrawLine(at + new Vector2(r * 0.35f, -r * 0.85f), fuse, _rim, 2);
            if (lit)
            {
                DrawCircle(fuse, r * 0.4f, _spark);
            }
        }
    }

    /// <summary>손의 자리(화면 좌표) — 발 중심에서 보는 쪽으로 <c>bomb_hand_x</c>, 위로 <c>bomb_hand_y</c>. 규칙 좌표의 y 는 위가 + 라 뒤집는다.</summary>
    private Vector2 Hand(double x, double y, int facing) =>
        new((float)(x + (facing * _feel.BombHandX)), (float)-(y + _feel.BombHandY));
}
