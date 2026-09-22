using Godot;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 보스 스프라이트. 배율은 <c>balance.json</c> 의 <c>feel.boss_sprite_scale</c> 이고,
/// 히트박스(<c>BossConfig.HalfWidth</c>)가 <b>그 배율로 그려진 몸</b>에 맞춰 잡혀 있다.
///
/// <para>
/// 배율이 여기 <c>const</c> 로 있던 때는 그림만 바꿔도 규칙이 조용히 어긋났다 —
/// 4배 스프라이트에 맞춰 잡은 반폭 120 이 팩을 갈아도 그대로 남았다. 둘을 같은 층(데이터)에
/// 두면 한쪽만 고치는 일이 눈에 띈다.
/// </para>
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
    /// <summary>선딜이 무르익었을 때의 몸 색. 판정이 가까울수록 이쪽으로 간다.</summary>
    private static readonly Color _windupTint = new(2.00f, 0.72f, 0.30f);

    /// <summary>
    /// <b>패리 불가</b> 패턴의 선딜 색 — 크림슨. 나인 솔즈의 관례고, 이 게임에는 이미
    /// <c>parryable: false</c> 태그가 있었는데 화면이 그 말을 안 했다(이슈 #27).
    ///
    /// <para>
    /// 평소 예고를 <b>호박색으로 옮겼다.</b> 전에는 선딜이 (1.00, 0.42, 0.34) 로 이미 붉은 쪽이라
    /// 크림슨을 얹어도 "조금 더 붉은 붉은색" 이었다 — 못 받아치는 공격을 받아치려다 맞는 것은
    /// 정보가 없어서지 반사 신경이 모자라서가 아니다. 두 색은 색상환에서 갈라야 한다.
    /// </para>
    /// </summary>
    private static readonly Color _unparryableTint = new(2.40f, 0.10f, 0.22f);

    private static readonly Color _recoverTint = new(0.70f, 0.70f, 0.78f);
    private static readonly Color _tellRingColor = new(1.00f, 0.74f, 0.30f, 0.85f);
    private static readonly Color _unparryableRingColor = new(1.00f, 0.06f, 0.20f, 0.95f);
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
        _feel = Balance.Data.Feel;
        _sprite.Scale = new Vector2((float)_feel.BossSpriteScale, (float)_feel.BossSpriteScale);
        _ring = new RingBurst
        {
            Position = new Vector2(0, (float)-_feel.BossRingOffsetY),
            LineWidth = (float)_feel.RingWidth * 1.6f,
        };
        AddChild(_ring);
    }

    public void Load(string spriteId)
    {
        var frames = GD.Load<SpriteFrames>($"res://assets/spriteframes/{spriteId}.tres");
        if (frames is null)
        {
            // PNG 는 저장소에 없다(tools/install_assets.py 가 받아둔 zip 을 푼다). 풀기 전에는 .tres 파싱은
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

    /// <summary>한 프레임.</summary>
    /// <param name="x">보스의 규칙 좌표 x.</param>
    /// <param name="phase">패턴의 어디쯤인가 — 선딜 · 후딜 · 쉬는 중.</param>
    /// <param name="nextActiveIn">다음 판정까지 남은 시간(초). 더 올 판정이 없으면 null.</param>
    /// <param name="parryable">지금 도는 패턴을 받아칠 수 있나. 못 받아치면 예고가 크림슨이다.
    /// <b>규칙이 아니라 태그를 그대로 그린다</b> — 뷰가 판정을 다시 계산하면 두 곳이 갈린다.</param>
    public void Show(double x, BossPhase phase, double? nextActiveIn, bool parryable)
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
                parryable ? _tellRingColor : _unparryableRingColor);
        }

        Animate(AnimationFor(phase));
        _sprite.Modulate = Tint(phase, ripeness, parryable);
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
        // 팩에 attack 이 셋 있다(#28 의 세 패턴에 하나씩) — 패턴별 배정은 그 이슈에서 한다.
        return phase == BossPhase.Windup ? "attack" : "idle";
    }

    private void Flash(Color color, double seconds)
    {
        _flashColor = color;
        _flashTotal = seconds;
        _flashLeft = seconds;
    }

    private Color Tint(BossPhase phase, float ripeness, bool parryable)
    {
        Color baseTint = phase switch
        {
            BossPhase.Windup => Colors.White.Lerp(parryable ? _windupTint : _unparryableTint, ripeness),
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
            // 실패시킨다. 지금 팩에는 다섯이 다 있지만(install_assets.py 의 REQUIRED_ANIMS 가 확인한다)
            // 팩을 갈아끼우는 것이 이 파일의 전제라 확인은 남긴다.
            Log.Warn("view", $"anim_missing name={name}");
            return;
        }

        _sprite.Play(name);
        AlignToGround(name);
    }

    /// <summary>
    /// 타일 바닥을 노드 원점에 맞춘다. AnimatedSprite2D 는 centered 라 그냥 두면 타일 <b>중심</b>이
    /// 바닥선에 놓여 몸의 절반이 지면 아래로 내려간다 — 보스는 5.5배라 283px 가 묻힌다.
    /// 높이는 프레임에서 읽는다. <c>Offset</c> 은 로컬 좌표라 스프라이트의 <c>Scale</c> 이 곱해지므로
    /// 큰 보스도 작은 파이터와 같은 구현으로 맞는다.
    ///
    /// <para>
    /// 프레임 <b>아래끝이 곧 발바닥</b>이라는 것이 이 계산의 전제다. 지금 팩은 그렇지 않아서
    /// (Martial Hero 는 200px 프레임 안에서 발이 y=122 다) <c>tools/install_assets.py</c> 가
    /// SpriteFrames 의 region 을 팩 전체의 불투명 범위로 잘라 그 전제를 만들어 둔다.
    /// </para>
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
