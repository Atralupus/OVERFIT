using Godot;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 플레이어 스프라이트. <b>규칙을 하나도 모른다</b> — <see cref="FighterFrame"/> 하나와
/// 사건 메서드 몇 개를 받아 그리기만 한다.
///
/// <para>
/// 평생 <c>idle</c> 만 재생하던 것이 이 화면의 가장 큰 문제였다. 팩에 있는 것은
/// <c>idle · run · attack · hit · death</c> 다섯이고 대시·패리 전용 그림은 없다 —
/// 그 둘은 <b>이펙트로 만든다</b>(잔상 · 링 · 섬광). 2D 액션에서 대시와 패리의 피드백은
/// 원래 애니메이션이 아니라 이펙트와 히트스톱이 결정하므로 대체품이 아니라 제 모양이다.
/// </para>
/// </summary>
public partial class FighterView : Node2D
{
    /// <summary>
    /// 무적 창 안의 몸 색. <b>희게 탄다.</b> 잔상(푸른색)과 <b>다른 색</b>이어야 하는 이유는
    /// 실측이다 — 둘을 같은 청록으로 두니 몸과 꼬리가 한 덩어리 얼룩으로 뭉쳐서,
    /// 120px 짜리 캐릭터에서는 잔상이 몇 장인지도 지금 어디 있는지도 안 읽혔다.
    /// </summary>
    private static readonly Color _invulnerableTint = new(1.80f, 2.20f, 2.40f);

    /// <summary>
    /// 무적이 끝난 뒤의 대시 꼬리. <b>일부러 빛이 죽는다.</b> 대시(0.18초)는 아직 도는데
    /// 무적(0.14초)은 끝난 0.04초다 — 설계상 여기서 맞는 것이 맞고, 그 사실이 보여야 한다.
    /// 밝은 채로 두면 플레이어는 대시 내내 무적이라고 배운다.
    ///
    /// <para>
    /// 더 어둡게(0.45) 잡아 봤더니 어두운 배경에서 캐릭터가 아예 사라졌다 —
    /// "무적이 끝났다" 를 "내가 어디 있나" 와 바꾼 셈이라 되돌렸다. 신호는 어두움이 아니라
    /// <b>잔상이 멈추는 것</b>이 지고, 이 색은 거기에 한 겹 보탤 뿐이다.
    /// </para>
    /// </summary>
    private static readonly Color _dashTailTint = new(0.78f, 0.76f, 0.88f);

    private static readonly Color _ghostTint = new(0.22f, 0.80f, 1.30f, 0.75f);
    private static readonly Color _parryTint = new(1.45f, 1.45f, 0.85f);
    private static readonly Color _parryRingColor = new(0.60f, 0.95f, 1.00f, 0.90f);
    private static readonly Color _parryBurstColor = new(1.00f, 0.97f, 0.65f, 1.00f);
    private static readonly Color _parryFlash = new(2.60f, 2.60f, 2.10f);
    private static readonly Color _attackFlash = new(2.10f, 2.10f, 1.70f);
    private static readonly Color _slashColor = new(1.00f, 0.92f, 0.72f, 0.95f);
    private static readonly Color _hitFlash = new(2.40f, 0.45f, 0.45f);

    private AnimatedSprite2D _sprite = null!;
    private RingBurst _ring = null!;

    /// <summary>
    /// 공격 섬광. 패리 링과 <b>자리가 달라</b> 노드를 따로 둔다 — 패리는 몸을 감싸고
    /// 공격은 칼이 닿는 앞쪽에 선다. 한 노드를 옮겨 쓰면 이전 섬광이 날아가는 중에
    /// 자리가 바뀌어 화면을 가로질러 미끄러진다.
    /// </summary>
    private RingBurst _slash = null!;

    private FeelBalance _feel = null!;
    private int _facing = 1;

    private double _flashLeft;
    private double _flashTotal;
    private Color _flashColor;
    private double _hitPoseLeft;
    private double _ghostLeft;
    private bool _dead;

    public override void _Ready()
    {
        _sprite = GetNode<AnimatedSprite2D>("Sprite");
        _feel = Balance.Data.Feel;
        _ring = new RingBurst
        {
            Position = new Vector2(0, (float)-_feel.RingOffsetY),
            LineWidth = (float)_feel.RingWidth,
        };
        AddChild(_ring);

        _slash = new RingBurst { LineWidth = (float)_feel.RingWidth };
        AddChild(_slash);
    }

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
            Log.Warn("view", $"sprite_missing id={spriteId}");
            return;
        }

        _sprite.SpriteFrames = frames;
        Animate("idle");
    }

    /// <summary>한 프레임의 상태를 반영한다. y 는 위가 +인 규칙 좌표라 화면에서는 뒤집는다.</summary>
    public void Show(FighterFrame frame)
    {
        double dt = GetProcessDeltaTime();
        Position = new Vector2((float)frame.X, (float)-frame.Y);
        _sprite.FlipH = frame.Facing < 0;
        _facing = frame.Facing;

        Advance(dt);
        Trail(frame, dt);
        Ring(frame);
        Animate(AnimationFor(frame.Pose));
        _sprite.Modulate = Tint(frame);
    }

    /// <summary>
    /// 공격 판정이 선 틱. 몸 섬광 <b>하나로는 안 읽혔다</b> — 캐릭터가 120px 뿐이라
    /// 살짝 밝아지는 것은 이 크기에서 보이지 않는다. 칼이 닿는 앞쪽에 섬광을 하나 더 세운다.
    /// </summary>
    public void AttackActive()
    {
        Flash(_attackFlash, _feel.FlashSeconds);
        _slash.Position = new Vector2(
            _facing * (float)_feel.AttackRingX,
            (float)-_feel.RingOffsetY);
        _slash.Burst(
            (float)_feel.AttackRingFrom,
            (float)_feel.AttackRingTo,
            _feel.FlashSeconds * 2.2,
            _slashColor,
            sparks: 0,
            sparkLength: 0);
    }

    /// <summary>맞았다. 붉은 플래시 + <c>hit</c> 자세. 화면 흔들림은 <c>Battle</c> 이 건다.</summary>
    public void Hit()
    {
        Flash(_hitFlash, _feel.HitFlashSeconds);
        _hitPoseLeft = _feel.HitFlashSeconds;
    }

    /// <summary>
    /// 패리가 받아냈다. 강한 섬광 + 스파크. <b>실패하면 아무 일도 없다</b> —
    /// 없음이 곧 피드백이라 실패용 이펙트를 일부러 안 만든다.
    /// </summary>
    public void ParrySuccess()
    {
        Flash(_parryFlash, _feel.BurstSeconds);
        _ring.Burst(
            (float)_feel.ParryRingFrom,
            (float)_feel.ParryRingTo,
            _feel.BurstSeconds,
            _parryBurstColor,
            _feel.SparkCount,
            (float)_feel.SparkLength);
    }

    /// <summary>죽었다. 마지막 프레임에서 멈춘다 — 결과 화면은 <c>Battle</c> 이 그 뒤에 띄운다.</summary>
    public void Die()
    {
        _dead = true;
        _hitPoseLeft = 0;
        Animate(AnimationFor(FighterPose.Death));
    }

    /// <summary>히트스톱. 그림만 세운다 — 시뮬레이션의 시계는 <c>Battle</c> 이 따로 멈춘다.</summary>
    public void Freeze(bool frozen) => _sprite.SpeedScale = frozen ? 0.0f : 1.0f;

    private void Advance(double dt)
    {
        _flashLeft = System.Math.Max(0, _flashLeft - dt);
        _hitPoseLeft = System.Math.Max(0, _hitPoseLeft - dt);

        // 죽으면 마지막 프레임에 선다. SpriteFrames 의 loop 플래그를 끄면 GD.Load 가 캐시하는
        // **공유 자원**을 고치는 셈이라 다른 유닛까지 따라 바뀐다 — 여기서 세운다.
        if (_dead && _sprite.SpriteFrames is { } frames && frames.HasAnimation(_sprite.Animation)
            && _sprite.Frame >= frames.GetFrameCount(_sprite.Animation) - 1)
        {
            _sprite.Pause();
        }
    }

    /// <summary>
    /// 잔상. <b>무적 창에만 뿌린다</b> — 대시가 아니라 무적이다.
    /// 무적(0.14초)이 대시(0.18초)보다 짧은 것은 설계고, 그 0.04초가 지금까지 안 보였다.
    /// </summary>
    private void Trail(FighterFrame frame, double dt)
    {
        if (!frame.Invulnerable)
        {
            // 다음 무적의 첫 프레임에 곧바로 한 장 남기게 0 으로 되돌린다.
            _ghostLeft = 0;
            return;
        }

        _ghostLeft -= dt;
        if (_ghostLeft > 0)
        {
            return;
        }

        _ghostLeft = _feel.DashGhostInterval;
        // 부모(World)에 단다. 여기(파이터) 밑에 달면 잔상이 파이터를 따라다녀 잔상이 아니게 된다.
        Afterimage.Spawn(GetParent(), _sprite, Position, _feel.DashGhostFade, _ghostTint);
    }

    /// <summary>패리 창이 열려 있는 동안의 링. 반지름이 곧 "얼마나 남았나" 다.</summary>
    private void Ring(FighterFrame frame)
    {
        if (!frame.Parrying)
        {
            return;
        }

        float t = Mathf.Clamp((float)frame.ParryProgress, 0.0f, 1.0f);
        _ring.Charge(Mathf.Lerp((float)_feel.ParryRingFrom, (float)_feel.ParryRingTo, t), _parryRingColor);
    }

    private void Flash(Color color, double seconds)
    {
        _flashColor = color;
        _flashTotal = seconds;
        _flashLeft = seconds;
    }

    private Color Tint(FighterFrame frame)
    {
        Color baseTint = frame.Invulnerable ? _invulnerableTint
            : frame.Pose == FighterPose.Dash ? _dashTailTint
            : frame.Parrying ? _parryTint
            : Colors.White;

        if (_flashLeft <= 0 || _flashTotal <= 0)
        {
            return baseTint;
        }

        return baseTint.Lerp(_flashColor, (float)(_flashLeft / _flashTotal));
    }

    /// <summary>
    /// 자세 → 애니메이션 이름. 대시는 <c>run</c> 을 빌려 쓰고 나머지는 이펙트가 말한다 —
    /// 팩에 대시·패리 그림이 없다. 패리는 <c>idle</c> 이다: 제자리에서 받는 행동이라
    /// 달리는 그림을 붙이면 무엇을 하는지가 오히려 흐려진다.
    /// </summary>
    private string AnimationFor(FighterPose pose)
    {
        if (_dead)
        {
            return "death";
        }

        if (_hitPoseLeft > 0)
        {
            return "hit";
        }

        return pose switch
        {
            FighterPose.Run or FighterPose.Dash => "run",
            FighterPose.Attack => "attack",
            FighterPose.Hit => "hit",
            FighterPose.Death => "death",
            _ => "idle",
        };
    }

    /// <summary>
    /// 이름이 바뀔 때만 재생하고, 그때마다 바닥을 다시 맞춘다.
    /// 매 프레임 <c>Play</c> 하면 안 바뀐 것처럼 보이지만 애니메이션이 1프레임에 붙들린다.
    /// </summary>
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
    /// 바닥선에 놓여 몸의 절반이 지면 아래로 내려간다. 높이는 프레임에서 읽는다 — 유닛마다,
    /// 그리고 <b>애니메이션마다</b> 타일 크기가 다를 수 있어 바꿀 때마다 다시 잰다.
    /// 숫자를 박으면 갈아끼울 때 깨진다.
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
