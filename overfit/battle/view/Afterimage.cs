using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// 대시가 지나온 자리에 남는 잔상 한 장. 스스로 옅어지고 스스로 없어진다.
///
/// <para>
/// 전용 대시 애니메이션이 팩에 없다(Duelyst 에 roll/dodge 가 없다). 2D 액션에서 대시의 피드백은
/// 원래 애니메이션이 아니라 <b>잔상과 히트스톱</b>이 결정하므로, 이건 임시방편이 아니라 제 모양이다.
/// </para>
///
/// <para>
/// ⚠ <b>무적 창에만 뿌린다</b>(뿌리는 쪽은 <see cref="FighterView"/>). 대시가 끝날 때가 아니라
/// 무적이 끝날 때 잔상이 멈춰야, 대시(0.18초)보다 짧은 무적(0.14초)의 끝자락이 눈에 보인다 —
/// 그 끝자락에 맞는 것이 설계인데 플레이어에게는 지금까지 보이지 않았다.
/// </para>
/// </summary>
public partial class Afterimage : Sprite2D
{
    private double _life;
    private double _age;
    private Color _tint;

    /// <summary>
    /// <paramref name="from"/> 이 <b>지금 그리고 있는 프레임</b>을 그 자리에 박아 둔다.
    /// 부모를 따로 받는 이유는 파이터 밑에 달면 잔상이 파이터를 따라다니기 때문이다 —
    /// 잔상은 지나온 자리에 남아야 잔상이다.
    /// </summary>
    public static void Spawn(Node parent, AnimatedSprite2D from, Vector2 at, double life, Color tint)
    {
        if (parent is null || from is null || from.SpriteFrames is null)
        {
            return;
        }

        Texture2D? texture = from.SpriteFrames.GetFrameTexture(from.Animation, from.Frame);
        if (texture is null)
        {
            return;
        }

        var ghost = new Afterimage
        {
            Texture = texture,
            Position = at,
            Offset = from.Offset,
            FlipH = from.FlipH,
            Scale = from.Scale,
            Centered = from.Centered,
            Modulate = tint,
            _life = life,
            _tint = tint,

            // 잔상은 몸보다 **뒤**에 있어야 한다. 앞에 오면 본체가 잔상에 가려 "지금 어디 있나" 가 흐려진다.
            ZIndex = -1,
        };

        parent.AddChild(ghost);
    }

    public override void _Process(double delta)
    {
        _age += delta;
        if (_age >= _life)
        {
            QueueFree();
            return;
        }

        // 선형이 아니라 제곱으로 옅어진다 — 선형이면 끝까지 흐릿하게 남아 꼬리가 몇 장인지 안 세어진다.
        float left = (float)(1.0 - (_age / _life));
        Modulate = new Color(_tint.R, _tint.G, _tint.B, _tint.A * left * left);
    }
}
