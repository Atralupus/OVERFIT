using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// 보스 스프라이트. <b>캐릭터의 4배로 그린다</b> — 스펙이 정한 수치이고,
/// 히트박스(<c>BossConfig.HalfWidth</c>)도 그 크기에 맞춰 잡혀 있다.
/// </summary>
public partial class BossView : Node2D
{
    /// <summary>캐릭터 대비 배율. 그림만이 아니라 규칙(HalfWidth · 아레나 폭)이 이 값을 전제한다.</summary>
    public const float SizeMultiplier = 4.0f;

    private AnimatedSprite2D _sprite = null!;

    public override void _Ready()
    {
        _sprite = GetNode<AnimatedSprite2D>("Sprite");
        _sprite.Scale = new Vector2(SizeMultiplier, SizeMultiplier);
    }

    public void Load(string spriteId)
    {
        var frames = GD.Load<SpriteFrames>($"res://addons/duelyst_animated_sprites/spriteframes/units/{spriteId}.tres");
        if (frames is null)
        {
            Core.Log.Warn("view", $"sprite_missing id={spriteId}");
            return;
        }

        _sprite.SpriteFrames = frames;
        PlaySafe("idle");
    }

    public void Show(double x, string? pattern)
    {
        Position = new Vector2((float)x, 0);
        // 패턴 중에는 붉게. 전용 선딜 애니메이션이 없어서 쓰는 대체 표현이다.
        _sprite.Modulate = pattern is null ? Colors.White : new Color(1.4f, 0.8f, 0.8f);
    }

    /// <summary>
    /// 있는 애니메이션만 재생한다. 없는 이름으로 <c>Play</c> 하면 엔진이 <c>ERROR:</c> 를 찍고,
    /// 그건 헤드리스 판정(judge_headless)을 실패시킨다 — 696종이 전부 같은 세트를 갖지 않는다
    /// (실측 idle 695 · run 668 …).
    /// </summary>
    private void PlaySafe(string name)
    {
        if (_sprite.SpriteFrames is null || !_sprite.SpriteFrames.HasAnimation(name))
        {
            Core.Log.Warn("view", $"anim_missing name={name}");
            return;
        }

        _sprite.Play(name);
    }
}
