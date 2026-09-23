using Godot;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 플레이어 스프라이트. <b>규칙을 하나도 모른다</b> — <see cref="FighterFrame"/> 하나와
/// 사건 메서드 몇 개를 받아 그리기만 한다.
///
/// <para>
/// 평생 <c>idle</c> 만 재생하던 것이 이 화면의 가장 큰 문제였다. 뷰가 부르는 것은
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
    /// 130px 짜리 캐릭터에서는 잔상이 몇 장인지도 지금 어디 있는지도 안 읽혔다.
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

    /// <summary>
    /// <b>부정확</b> 패리의 색. 정확 패리(희고 노란 폭발 + 스파크 + 히트스톱)와 달리
    /// 탁하고 좁게 터진다 — 두 결과가 같아 보이면 화면은 "막았다" 만 말하고
    /// "절반 흘렸다" 는 안 말한다. 결과가 둘이면 피드백도 둘이어야 한다(이슈 #27).
    /// </summary>
    private static readonly Color _parryChipColor = new(0.85f, 0.52f, 0.40f, 0.85f);

    /// <summary>부정확 패리에 굳은 동안의 몸 색. 어둡고 채도가 죽는다 — 안 보이면
    /// 0.6초 동안 키가 안 먹는 것이 버그로 읽힌다.</summary>
    private static readonly Color _lockedTint = new(0.50f, 0.46f, 0.58f);

    /// <summary>
    /// 공격 섬광. <b>일부러 약하다.</b> 전에는 (2.10, 2.10, 1.70) 이라 0.09초 동안 몸이 흰색으로
    /// 날아갔는데, 그 0.09초가 정확히 <b>칼이 지나가는 한 프레임</b> 위에 떨어진다 —
    /// 유일하게 보여줘야 할 그림을 섬광이 덮고 있었다(이슈 #38).
    ///
    /// <para>
    /// 다른 섬광들(패리 2.60 · 피격 2.40)이 센 것은 그 자리에 <b>그림이 없기 때문</b>이다:
    /// 팩에 패리 애니메이션이 없어 섬광이 곧 피드백이다. 공격은 반대다 — 작가가 흰 궤적을
    /// 이미 그려 뒀으므로, 여기서 할 일은 말을 더 하는 것이 아니라 그 그림을 안 가리는 것이다.
    /// </para>
    /// </summary>
    private static readonly Color _attackFlash = new(1.25f, 1.20f, 1.00f);
    private static readonly Color _slashColor = new(1.00f, 0.92f, 0.72f, 0.95f);
    private static readonly Color _hitFlash = new(2.40f, 0.45f, 0.45f);

    /// <summary>
    /// 모으는 동안의 몸 색. <b>덥혀지듯 붉게 간다</b> — 최대(<see cref="_chargeMaxTint"/>)와
    /// 같은 흰색으로 두면 "모으는 중" 과 "다 모았다" 가 한 그림이 되어, 2초를 셀 방법이 사라진다.
    /// </summary>
    private static readonly Color _chargeTint = new(1.35f, 1.00f, 0.70f);

    /// <summary>
    /// <b>최대</b> 차지의 몸 색. 희게 탄다 — 무적 창(<see cref="_invulnerableTint"/>)과 비슷한 밝기인데,
    /// 그 둘은 같은 순간에 절대 안 나온다(대시 중에는 못 모은다). 색보다 중요한 것은 <b>한 번 터지는
    /// 섬광과 멈춘 링</b>이고(<see cref="ChargeTierUp"/>), 이 색은 그 뒤로 계속 남아 "아직 최대다" 를 말한다.
    /// </summary>
    private static readonly Color _chargeMaxTint = new(2.10f, 2.10f, 1.90f);

    private static readonly Color _chargeRingColor = new(1.00f, 0.72f, 0.30f, 0.85f);
    private static readonly Color _chargeMaxRingColor = new(1.00f, 0.97f, 0.72f, 1.00f);
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

    /// <summary>
    /// 차지 자세로 세울 프레임 번호. <b>데이터에서 온다</b> — <c>fighters.json</c> 의
    /// <c>attack_anim_blade_frame</c> 바로 앞 장이 "칼을 끝까지 뒤로 뺀" 마지막 선딜 프레임이다.
    /// 여기 숫자를 박으면 시트를 갈아끼울 때 조용히 엉뚱한 장에서 멈춘다.
    /// </summary>
    private int _chargeFrame;

    /// <summary>지난 프레임에 차지 자세였나. 차지가 끝나면 시트를 처음부터 다시 돌려야 한다.</summary>
    private bool _holdingCharge;

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
        // 배율은 데이터다. 팩의 그림은 원본 전신이 52px 뿐이라 1배로 두면 화면에서 안 읽히고,
        // 그 배율이 곧 fighters.json 의 height 와 맞물린다 (balance.json 의 _note_sprite_scale).
        _sprite.Scale = new Vector2(
            (float)_feel.FighterSpriteScale, (float)_feel.FighterSpriteScale);
        _ring = new RingBurst
        {
            Position = new Vector2(0, (float)-_feel.RingOffsetY),
            LineWidth = (float)_feel.RingWidth,
        };
        AddChild(_ring);

        _slash = new RingBurst { LineWidth = (float)_feel.RingWidth };
        AddChild(_slash);
    }

    /// <summary>
    /// 스프라이트를 갈아끼운다. id 는 data/fighters.json 의 sprite 값이다.
    /// <paramref name="chargeFrame"/> 은 차지 자세로 세울 프레임 번호 — 부르는 쪽(<c>Battle</c>)이
    /// 데이터에서 읽어 준다. 뷰가 fighters.json 을 직접 읽으면 규칙과 뷰가 같은 파일을 두 번 읽는다.
    /// </summary>
    public void Load(string spriteId, int chargeFrame)
    {
        _chargeFrame = System.Math.Max(0, chargeFrame);

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

        // 차지 자세는 이름이 아니라 **프레임**이라 Animate 로 못 말한다 — 스윙과 같은 attack 시트를
        // 쓰면서 한 장에 멈춰 서는 것이라, 이름만 보는 Animate 는 둘을 구별하지 못한다.
        // 맞았거나 죽었으면 그 그림이 이긴다: 모으던 것은 이미 규칙에서 끊겼다.
        bool holding = frame.Pose == FighterPose.Charge && !_dead && _hitPoseLeft <= 0;
        if (holding)
        {
            HoldCharge();
        }
        else
        {
            if (_holdingCharge)
            {
                Release();
            }

            Animate(AnimationFor(frame.Pose));
        }

        _holdingCharge = holding;
        _sprite.Modulate = Tint(frame);
    }

    /// <summary>
    /// 공격 판정이 선 틱 — 시트에서 <b>칼이 실제로 지나가는 프레임</b>이다
    /// (fighters.json 의 attack_anim_blade_frame).
    ///
    /// <para>
    /// 여기서 하는 일은 둘이다. 몸을 <b>살짝</b> 밝히고(<see cref="_attackFlash"/>), 칼이 닿는
    /// 앞쪽에 섬광을 하나 세운다. 몸 섬광만으로는 130px 짜리 캐릭터에서 안 읽혀서 앞쪽 섬광을
    /// 더했었는데, 그 뒤 몸 섬광을 세게 올려 놓는 바람에 <b>정작 칼 그림을 덮고 있었다</b>(이슈 #38).
    /// 앞쪽 섬광이 세기를 맡으므로 몸은 약해도 된다.
    /// </para>
    /// </summary>
    /// <param name="tier">모아서 휘두른 단계 (0 = 그냥 한 대). <b>반지름은 안 건드린다</b> —
    /// 이 링의 끝 반지름은 <c>attack_reach</c> 와 같은 눈금이라(balance.json) 키우면 사거리를
    /// 속이는 그림이 된다. 모은 값은 <b>스파크와 색</b>이 말한다: 닿는 곳은 같고 실린 것이 다르다.</param>
    public void AttackActive(int tier)
    {
        Flash(_attackFlash, _feel.FlashSeconds);
        _slash.Position = new Vector2(
            _facing * (float)_feel.AttackRingX,
            (float)-_feel.RingOffsetY);
        _slash.Burst(
            (float)_feel.AttackRingFrom,
            (float)_feel.AttackRingTo,
            _feel.FlashSeconds * 2.2,
            tier > 0 ? _chargeMaxRingColor : _slashColor,
            sparks: tier > 0 ? _feel.SparkCount : 0,
            sparkLength: (float)(_feel.SparkLength * 0.5));
    }

    /// <summary>
    /// 차지 단계가 올랐다 (이슈 #40). <b>순간이라 상태가 아니다</b> — <c>Battle</c> 이 틱 전후를
    /// 견줘 부른다.
    ///
    /// <para>
    /// 최대와 중간을 <b>일부러 크게 다르게</b> 준다. 중간은 조용한 고리 하나지만 최대는
    /// 섬광 + 스파크 + 흰 몸이다 — 최대에 닿은 것을 못 알아채면 플레이어는 2초를 셀 수가 없고,
    /// 그러면 이 기술은 "언제 놓을지 모르는 기술" 이 된다. 정확 패리와 같은 세기를 쓰는 것은
    /// 일부러다: 둘 다 "지금이다" 를 말하는 순간이고, 이 게임에서 가장 비싼 두 순간이다.
    /// </para>
    /// </summary>
    /// <param name="maxed">이번에 오른 단계가 최대인가.</param>
    public void ChargeTierUp(bool maxed)
    {
        if (maxed)
        {
            Flash(_parryFlash, _feel.FlashSeconds);
            _ring.Burst(
                (float)_feel.ChargeRingTo,
                (float)_feel.ParryRingTo,
                _feel.BurstSeconds,
                _chargeMaxRingColor,
                _feel.SparkCount,
                (float)_feel.SparkLength);
            return;
        }

        _ring.Burst(
            (float)_feel.ChargeRingTo,
            (float)((_feel.ChargeRingTo + _feel.ChargeRingFrom) / 2),
            _feel.BurstSeconds * 0.5,
            _chargeRingColor,
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

    /// <summary>
    /// 부정확 패리가 받아냈다. <b>일부러 약하다</b> — 섬광도 스파크도 히트스톱도 없이
    /// 탁한 고리 하나다. 정확 패리와 같은 연출을 주면 "정확히 눌렀나" 가 화면에서 사라진다.
    /// </summary>
    public void ParryImprecise()
    {
        _ring.Burst(
            (float)_feel.ParryRingFrom,
            (float)((_feel.ParryRingFrom + _feel.ParryRingTo) / 2),
            _feel.BurstSeconds * 0.6,
            _parryChipColor,
            sparks: 0,
            sparkLength: 0);
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

    /// <summary>
    /// 창이 열려 있는 동안의 링. 패리와 차지가 <b>같은 노드를 쓴다</b> — 둘은 같은 순간에
    /// 절대 안 나오고(모으는 중에는 패리를 못 누른다), 노드를 나누면 안 쓰는 링이 프레임마다
    /// 자기 자리를 지키느라 코드만 두 벌이 된다.
    ///
    /// <para>
    /// 방향이 반대다. 패리 링은 창이 닫히는 쪽으로 <b>퍼지고</b>, 차지 링은 몸으로 <b>조여 든다</b> —
    /// 모이는 것은 퍼지는 것이 아니다. 최대에 닿으면 진행도가 1 에서 멈추므로 링도 멈추고,
    /// 그 <b>멈춤</b>이 "더 모을 것이 없다" 는 말이 된다.
    /// </para>
    /// </summary>
    private void Ring(FighterFrame frame)
    {
        if (frame.Pose == FighterPose.Charge)
        {
            float t = Mathf.Clamp((float)frame.ChargeProgress, 0.0f, 1.0f);
            _ring.Charge(
                Mathf.Lerp((float)_feel.ChargeRingFrom, (float)_feel.ChargeRingTo, t),
                frame.ChargeMaxed ? _chargeMaxRingColor : _chargeRingColor);
            return;
        }

        if (!frame.Parrying)
        {
            return;
        }

        float u = Mathf.Clamp((float)frame.ParryProgress, 0.0f, 1.0f);
        _ring.Charge(Mathf.Lerp((float)_feel.ParryRingFrom, (float)_feel.ParryRingTo, u), _parryRingColor);
    }

    /// <summary>
    /// 모으는 자세. <b>시트를 처음부터 돌리다가 선딜 마지막 장에서 세운다</b> —
    /// 칼을 뒤로 빼는 동작(0~3번 프레임)을 실제로 감고 나서 그 자세로 멈추는 것이라,
    /// 규칙이 "붙드는 것이 곧 선딜" 이라고 말하는 것과 그림이 정확히 같은 말을 한다.
    ///
    /// <para>
    /// 세우는 것이 핵심이다. <c>Play</c> 만 하고 두면 0.5초짜리 시트가 그대로 돌아
    /// <b>모으는 중에 칼이 나간다.</b> 반대로 처음부터 멈춰 세우면(예전) 뒤로 빼는 동작이
    /// 한 프레임에 건너뛰어져 "모으기 시작했다" 가 그림에 없다.
    /// </para>
    /// </summary>
    private void HoldCharge()
    {
        if (_sprite.SpriteFrames is null || !_sprite.SpriteFrames.HasAnimation("attack"))
        {
            return;
        }

        if (_sprite.Animation != "attack")
        {
            _sprite.Play("attack");
            _sprite.SetFrameAndProgress(0, 0.0f);
            AlignToGround("attack");
        }

        int last = System.Math.Min(_chargeFrame, _sprite.SpriteFrames.GetFrameCount("attack") - 1);
        if (_sprite.Frame >= last)
        {
            _sprite.Frame = last;
            _sprite.Pause();
        }
    }

    /// <summary>
    /// 차지를 놓았다 — 멈춰 세운 그 장에서 <b>이어서</b> 돌린다. 처음부터 다시 돌리면 화면이
    /// 칼을 두 번 뒤로 뺀다: 2초 동안 뺀 칼을 놓는 순간 다시 빼는 그림이 되고, 그건 규칙이
    /// 말하는 것(선딜은 이미 지났다)과 반대다.
    ///
    /// <para>
    /// 둘의 시계가 맞는 것은 우연이 아니다. 선딜(0.3333초)이 곧 시트의 0~3번 네 장이라,
    /// t 초 붙들었으면 그림은 <c>t×fps</c> 번째 장에 있고 규칙의 남은 선딜은 <c>0.3333-t</c> 다 —
    /// 같은 값이다. 그래서 중간에 놓아도 그림과 판정이 같이 간다.
    /// </para>
    /// </summary>
    private void Release()
    {
        if (_sprite.SpriteFrames is null || !_sprite.SpriteFrames.HasAnimation(_sprite.Animation))
        {
            return;
        }

        _sprite.Play();
    }

    private void Flash(Color color, double seconds)
    {
        _flashColor = color;
        _flashTotal = seconds;
        _flashLeft = seconds;
    }

    private Color Tint(FighterFrame frame)
    {
        // 고정을 맨 앞에 본다. 굳은 동안에는 다른 무엇도 못 하므로 다른 색이 이길 수 없다.
        Color baseTint = frame.Locked ? _lockedTint
            : frame.Invulnerable ? _invulnerableTint
            : frame.Pose == FighterPose.Dash ? _dashTailTint
            // 모으는 중 · 다 모았다는 **서로 다른 색**이어야 한다. 하나로 두면 최대에 닿은 순간이
            // 섬광 한 번뿐이라, 그 0.34초를 놓치면 지금이 최대인지 알 방법이 없다.
            : frame.Pose == FighterPose.Charge ? (frame.ChargeMaxed ? _chargeMaxTint : _chargeTint)
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
            // 차지도 attack 이다 — 다만 한 장에 멈춰 선다(HoldCharge). 여기 있는 이유는
            // 맞거나 죽어서 차지가 끊긴 프레임에 이 갈래로 떨어지기 때문이다.
            FighterPose.Attack or FighterPose.Charge => "attack",
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
    /// 바닥선에 놓여 몸의 절반이 지면 아래로 내려간다. 높이는 프레임에서 읽는다 — 유닛마다,
    /// 그리고 <b>애니메이션마다</b> 타일 크기가 다를 수 있어 바꿀 때마다 다시 잰다.
    /// 숫자를 박으면 갈아끼울 때 깨진다.
    ///
    /// <para>
    /// 프레임 <b>아래끝이 곧 발바닥</b>이라는 것이 이 계산의 전제다. Martial Hero 는 200px 프레임
    /// 안에서 발이 y=122 라 그냥 두면 78px 떠 있었다 — <c>tools/install_assets.py</c> 가
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
