using Godot;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 보스 스프라이트. <b>캐릭터의 4배로 그린다</b> — 스펙이 정한 수치이고,
/// 히트박스(<c>BossConfig.HalfWidth</c>)도 그 크기에 맞춰 잡혀 있다.
///
/// <para>
/// 패턴 중 붉은 틴트 하나로는 <b>무엇이 오는지</b>가 안 보인다는 것이 플레이 피드백이었다.
/// 그래서 선딜과 판정이 서는 순간을 확실히 가른다 — 선딜에는 <c>attack</c> 모션과 함께
/// 예고 링이 <b>조여 들고</b>, 판정이 서면 충격파가 <b>퍼진다</b>. 방향이 반대라
/// 둘을 헷갈릴 수 없다.
/// </para>
/// </summary>
public partial class BossView : Node2D
{
    /// <summary>캐릭터 대비 배율. 그림만이 아니라 규칙(HalfWidth · 아레나 폭)이 이 값을 전제한다.</summary>
    public const float SizeMultiplier = 4.0f;

    /// <summary>선딜이 무르익었을 때의 몸 색. 판정이 가까울수록 이쪽으로 간다.</summary>
    private static readonly Color _windupTint = new(2.00f, 0.55f, 0.45f);

    private static readonly Color _recoverTint = new(0.70f, 0.70f, 0.78f);
    private static readonly Color _tellRingColor = new(1.00f, 0.42f, 0.34f, 0.85f);
    private static readonly Color _shockRingColor = new(1.00f, 0.80f, 0.35f, 1.00f);
    private static readonly Color _activeFlash = new(2.60f, 2.30f, 1.60f);
    private static readonly Color _hitFlash = new(2.40f, 2.40f, 2.40f);

    private AnimatedSprite2D _sprite = null!;
    private RingBurst _ring = null!;
    private FeelBalance _feel = null!;

    private double _flashLeft;
    private double _flashTotal;
    private Color _flashColor;
    private double _hitPoseLeft;
    private bool _dead;

    public override void _Ready()
    {
        _sprite = GetNode<AnimatedSprite2D>("Sprite");
        _sprite.Scale = new Vector2(SizeMultiplier, SizeMultiplier);
        _feel = Balance.Data.Feel;
        _ring = new RingBurst
        {
            Position = new Vector2(0, (float)-_feel.BossRingOffsetY),
            LineWidth = (float)_feel.RingWidth * 1.6f,
        };
        AddChild(_ring);
    }

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
            Log.Warn("view", $"sprite_missing id={spriteId}");
            return;
        }

        _sprite.SpriteFrames = frames;
        Animate("idle");
    }

    /// <summary>한 프레임. <paramref name="nextActiveIn"/> 은 다음 판정까지 남은 시간(초)이고 없으면 null.</summary>
    public void Show(double x, BossPhase phase, double? nextActiveIn)
    {
        double dt = GetProcessDeltaTime();
        Position = new Vector2((float)x, 0);

        _flashLeft = System.Math.Max(0, _flashLeft - dt);
        _hitPoseLeft = System.Math.Max(0, _hitPoseLeft - dt);
        HoldLastFrameWhenDead();

        float ripeness = Ripeness(phase, nextActiveIn);
        if (ripeness > 0)
        {
            // 판정이 가까울수록 링이 **조여 든다.** 퍼지는 충격파와 방향이 반대라 둘을 헷갈릴 수 없다.
            _ring.Charge(
                Mathf.Lerp((float)_feel.TellRingFrom, (float)_feel.TellRingTo, ripeness),
                _tellRingColor);
        }

        Animate(AnimationFor(phase));
        _sprite.Modulate = Tint(phase, ripeness);
    }

    /// <summary>판정이 선 틱. 충격파가 <b>퍼진다</b> — 선딜과 방향이 반대다.</summary>
    public void ActiveNow()
    {
        Flash(_activeFlash, _feel.FlashSeconds);
        _ring.Burst(
            (float)_feel.TellRingTo,
            (float)_feel.BossRingTo,
            _feel.BurstSeconds,
            _shockRingColor,
            _feel.SparkCount,
            (float)_feel.SparkLength);
    }

    /// <summary>플레이어의 칼이 닿았다. 때린 것이 닿았는지가 보여야 공격에 값이 붙는다.</summary>
    public void Hit()
    {
        Flash(_hitFlash, _feel.FlashSeconds);
        _hitPoseLeft = _feel.FlashSeconds;
    }

    public void Die()
    {
        _dead = true;
        _hitPoseLeft = 0;
        Animate("death");
    }

    /// <summary>히트스톱. 그림만 세운다 — 시뮬레이션의 시계는 <c>Battle</c> 이 따로 멈춘다.</summary>
    public void Freeze(bool frozen) => _sprite.SpeedScale = frozen ? 0.0f : 1.0f;

    /// <summary>판정까지 얼마나 무르익었나(0 = 아직 멀었다 · 1 = 이번 프레임).</summary>
    private float Ripeness(BossPhase phase, double? nextActiveIn)
    {
        if (phase != BossPhase.Windup || nextActiveIn is not { } left || left > _feel.TellLeadSeconds)
        {
            return 0;
        }

        return Mathf.Clamp(1.0f - (float)(left / _feel.TellLeadSeconds), 0.0f, 1.0f);
    }

    private string AnimationFor(BossPhase phase)
    {
        if (_dead)
        {
            return "death";
        }

        if (_hitPoseLeft > 0)
        {
            return "hit";
        }

        // 선딜에만 attack 이다. 후딜까지 attack 으로 두면 "아직 온다" 와 "끝났다" 가 같은 그림이 된다.
        return phase == BossPhase.Windup ? "attack" : "idle";
    }

    private void Flash(Color color, double seconds)
    {
        _flashColor = color;
        _flashTotal = seconds;
        _flashLeft = seconds;
    }

    private Color Tint(BossPhase phase, float ripeness)
    {
        Color baseTint = phase switch
        {
            BossPhase.Windup => Colors.White.Lerp(_windupTint, ripeness),
            BossPhase.Recover => _recoverTint,
            _ => Colors.White,
        };

        if (_flashLeft <= 0 || _flashTotal <= 0)
        {
            return baseTint;
        }

        return baseTint.Lerp(_flashColor, (float)(_flashLeft / _flashTotal));
    }

    private void HoldLastFrameWhenDead()
    {
        if (_dead && _sprite.SpriteFrames is { } frames && frames.HasAnimation(_sprite.Animation)
            && _sprite.Frame >= frames.GetFrameCount(_sprite.Animation) - 1)
        {
            _sprite.Pause();
        }
    }

    /// <summary>이름이 바뀔 때만 재생하고, 그때마다 바닥을 다시 맞춘다.</summary>
    private void Animate(string name)
    {
        if (_sprite.SpriteFrames is null || _sprite.Animation == name)
        {
            return;
        }

        if (!_sprite.SpriteFrames.HasAnimation(name))
        {
            // 없는 이름으로 Play 하면 엔진이 ERROR: 를 찍고, 그건 헤드리스 판정(judge_headless)을
            // 실패시킨다 — 696종이 전부 같은 세트를 갖지 않는다 (실측 idle 695 · run 668 …).
            Log.Warn("view", $"anim_missing name={name}");
            return;
        }

        _sprite.Play(name);
        AlignToGround(name);
    }

    /// <summary>
    /// 타일 바닥을 노드 원점에 맞춘다. AnimatedSprite2D 는 centered 라 그냥 두면 타일 <b>중심</b>이
    /// 바닥선에 놓여 몸의 절반이 지면 아래로 내려간다 — 보스는 4배라 230px 가 묻힌다.
    /// 높이는 프레임에서 읽는다. <c>Offset</c> 은 로컬 좌표라 스프라이트의 <c>Scale</c> 이 곱해지므로
    /// 4배 보스도 1배 파이터와 같은 구현으로 맞는다. 유닛마다, 그리고 애니메이션마다 타일 크기가
    /// 다를 수 있어 바꿀 때마다 다시 잰다.
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
