using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// 플레이어 스프라이트. <b>규칙을 하나도 모른다</b> — 위치와 행동을 받아 그리기만 한다.
/// 대시·패리 애니메이션이 없으므로(에셋에 없다) 색과 투명도로 대신한다.
/// </summary>
public partial class FighterView : Node2D
{
    private AnimatedSprite2D _sprite = null!;

    public override void _Ready() => _sprite = GetNode<AnimatedSprite2D>("Sprite");

    /// <summary>스프라이트를 갈아끼운다. id 는 data/fighters.json 의 sprite 값이다.</summary>
    public void Load(string spriteId)
    {
        var frames = GD.Load<SpriteFrames>($"res://addons/duelyst_animated_sprites/spriteframes/units/{spriteId}.tres");
        if (frames is null)
        {
            // PNG 는 저장소에 없다(tools/fetch_duelyst.py 가 받아 온다). 받기 전에는 .tres 파싱은
            // 되고 그 안의 텍스처 ext_resource 만 못 풀려, **엔진이 ERROR 블록을 여러 건 찍는다** —
            // 여기서 null 을 받아 조용히 넘어가는 것이 아니다. 그 소음은 우리 코드의 버그가 아니라
            // 환경이라 tools/build.sh 의 judge_headless 가 그 경로만 면제하고 건수를 경고로 남긴다.
            // 우리 쪽은 스프라이트 없이 계속 간다.
            Core.Log.Warn("view", $"sprite_missing id={spriteId}");
            return;
        }

        _sprite.SpriteFrames = frames;
        PlaySafe("idle");
        AlignToGround("idle");
    }

    /// <summary>한 프레임의 상태를 반영한다. y 는 위가 +인 규칙 좌표라 화면에서는 뒤집는다.</summary>
    public void Show(double x, double y, int facing, bool invulnerable, bool parrying)
    {
        Position = new Vector2((float)x, (float)-y);
        _sprite.FlipH = facing < 0;
        // 대시 무적은 반투명, 패리 창은 밝게. 전용 애니메이션이 없어서 쓰는 대체 표현이다.
        _sprite.Modulate = invulnerable ? new Color(1, 1, 1, 0.45f)
            : parrying ? new Color(1.6f, 1.6f, 1.0f)
            : Colors.White;
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

    /// <summary>
    /// 타일 바닥을 노드 원점에 맞춘다. AnimatedSprite2D 는 centered 라 그냥 두면 타일 <b>중심</b>이
    /// 바닥선에 놓여 몸의 절반이 지면 아래로 내려간다. 높이는 프레임에서 읽는다 — 유닛마다
    /// 타일 크기가 다를 수 있어 숫자를 박으면 갈아끼울 때 깨진다.
    /// </summary>
    private void AlignToGround(string anim)
    {
        Texture2D? frame = _sprite.SpriteFrames?.GetFrameTexture(anim, 0);
        if (frame is null)
        {
            return;
        }

        _sprite.Offset = new Vector2(0, -frame.GetHeight() / 2.0f);
    }
}
