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
/// 그래서 선딜과 판정이 서는 순간을 가른다 — 선딜에는 단계가 붙든 공격 자세에 서서 몸이 선딜 틴트 쪽으로
/// 무르익고, 판정이 서면 몸이 한 번 번쩍인다(화면 흔들림은 <c>BattleCues</c> 가 같이 건다).
/// </para>
///
/// <para>
/// <b>보스 공격에는 링이 없다</b> (#81). 전에는 선딜에 예고 링이 <b>조여 들고</b> 판정이 서면 충격파가 스파크와 함께
/// <b>퍼졌다</b>(<see cref="RingBurst"/> 하나 · <c>feel.tell_ring_*</c> · <c>boss_ring_*</c>). 유저가 걷었다 —
/// "적 공격에 동그라미 연출은 제거해주세요 전체적으로 캐릭터는 남겨놔도 됩니다." (2026-09-26). 파이터의 링
/// (<c>FighterView</c>)은 그 "캐릭터" 라 남는다. 판정이 서는 순간의 번쩍임과 흔들림은 동그라미가 아니라 남겼다.
/// </para>
///
/// <para>
/// <b>맞으면 희게 번쩍이기만 한다</b> (#71 · 설계 §6) — 셰이더(<c>hit_flash.gdshader</c>)가 색만 민다. 애니메이션도 장도 안
/// 바꾸므로 선딜 도중에 맞아도 공격 자세가 이어진다. 전에는 팩의 흰 실루엣(<c>hit_white</c>)을 틀어 자세가 끊겼다.
/// </para>
///
/// <para>
/// <b>무엇이 오는지는 그림이 말한다</b> (#72 · 설계 §6). 규칙의 단계가 가리키는 장(<c>anim</c> · <c>frame</c>)을 그대로
/// 붙든다 — 3연격은 칼을 든 f0, 점프 공격은 웅크린 <c>jump</c> f0 다. 옛 변종의 예고 표지(칼 · 끌기 · 도약 표지 아홉)와
/// 빨간 가드 불가 마무리(크림슨 · 링 · <c>危</c>)는 변종과 같이 걷었다 — 새 두 패턴에는 가드 불가 판정이 없어 빨강이 말할
/// 것이 없다. 선딜 틴트의 무르익음(<see cref="Ripeness"/>)이 "언제" 를 거들고, 5번 PR 이 그것도 걷는다(설계 §6).
/// </para>
/// </summary>
public partial class BossView : Node2D
{
    /// <summary>선딜이 무르익었을 때의 몸 색. 판정이 가까울수록 이쪽으로 간다.</summary>
    private static readonly Color _windupTint = new(2.00f, 0.72f, 0.30f);

    private static readonly Color _recoverTint = new(0.70f, 0.70f, 0.78f);

    /// <summary>
    /// <b>탈진</b>한 동안의 몸 색 — 푸른 톤 (#72 · 설계 §4.3 · §6). 그림은 take-hit(<c>hit</c>)를 한 번 돌고 마지막 장에 선
    /// 자세이고, 이 색이 "지금은 아무것도 안 온다 — 내 차례다" 를 말한다. 전에는 경직을 식은 회색 + 느린 idle 로 그렸다
    /// (팩에 지친 모션이 없다고 봤다) — 스펙이 take-hit 를 탈진의 그림으로 정했다.
    /// </summary>
    private static readonly Color _exhaustTint = new(0.56f, 0.70f, 1.35f);

    /// <summary>흰 플래시 셰이더의 세기 — <c>hit_flash.gdshader</c> 의 <c>uniform float flash</c> 다.</summary>
    private static readonly StringName _flashParam = "flash";

    private static readonly Color _activeFlash = new(2.60f, 2.30f, 1.60f);

    private AnimatedSprite2D _sprite = null!;
    private FeelBalance _feel = null!;

    private double _flashLeft;
    private double _flashTotal;
    private Color _flashColor;
    private bool _dead;

    /// <summary>맞은 흰 플래시의 셰이더 — 스프라이트의 재질이다. 세기만 매 프레임 넣는다.</summary>
    private ShaderMaterial _hitFlash = null!;

    /// <summary>남은 흰 플래시(초). <c>feel.boss_hit_flash_seconds</c> 에서 0 으로 내려가며 셰이더의 세기가 1 → 0 이다.</summary>
    private double _hitFlashLeft;

    /// <summary>히트스톱으로 그림이 멈춰 있나.</summary>
    private bool _frozen;

    /// <summary>맞은 뒤 첫 <see cref="Show"/> 가 그린 장을 로그로 남길 차례인가 — <see cref="Hit"/> 가 세우고 그 Show 가 지운다.</summary>
    private bool _flashLogPending;

    public override void _Ready()
    {
        _sprite = GetNode<AnimatedSprite2D>("Sprite");
        _feel = Balance.Data.Feel;
        _sprite.Scale = new Vector2((float)_feel.BossSpriteScale, (float)_feel.BossSpriteScale);
        _hitFlash = new ShaderMaterial { Shader = GD.Load<Shader>("res://battle/view/hit_flash.gdshader") };
        _sprite.Material = _hitFlash;
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
        // **이름이 같아도 재생하고 바닥을 맞춘다** (이슈 #62). 프레임을 끼우는 순간 엔진이 재생을 멈추고
        // 첫 애니메이션(이 팩은 idle)으로 이름만 바꿔 두어, 그냥 Animate("idle") 은 아무것도 안 한다.
        // 파이터는 그래서 전투 시작마다 땅에 반쯤 묻혀 있었고, 보스는 시작하자마자 걸어 들어와(run)
        // 그 첫 전환이 바닥을 맞춰 줘서 가려져 있었을 뿐 같은 길이다.
        Animate("idle", force: true);
        Log.Debug("view", $"boss_sprite id={spriteId} anim={_sprite.Animation} offset_y={_sprite.Offset.Y:0.#}");
    }

    /// <summary>
    /// 한 프레임. <paramref name="frame"/> 의 모든 값은 <b>규칙이 정한 것</b>이고 여기서 다시 재지 않는다.
    /// </summary>
    public void Show(BossFrame frame)
    {
        double dt = GetProcessDeltaTime();
        // 규칙은 위가 + 이고 화면은 아래가 + 다 — 도약(설계 §4.2)하는 보스의 발이 규칙의 Y 에 선다.
        Position = new Vector2((float)frame.X, (float)-frame.Y);

        // 스프라이트 원본은 오른쪽을 본다 — 팩의 규약이고 FighterView 도 같다.
        // **뒤집어도 자리가 안 어긋난다**: Offset 의 x 는 0 이다(AlignToGround 는 y 만 건드린다).
        _sprite.FlipH = frame.Facing < 0;

        BossPhase phase = frame.Phase;

        _flashLeft = System.Math.Max(0, _flashLeft - dt);
        _hitFlashLeft = System.Math.Max(0, _hitFlashLeft - dt);
        HoldLastFrameWhenDead();

        // 선딜 틴트의 무르익음만 쓴다 — 같은 값으로 조여 들던 예고 링은 걷었다(#81). 탈진하면 패턴이 끊겨 Phase 가 Idle 이라
        // 0 이다 (#72 · 설계 §4.3).
        float ripeness = Ripeness(phase, frame.NextActiveIn);

        string anim = AnimationFor(frame.Anim, frame.Exhausted);
        if (anim == frame.Anim && frame.Frame is int held)
        {
            HoldAt(anim, held);
        }
        else
        {
            Animate(anim);
        }

        _sprite.SpeedScale = _frozen ? 0.0f : 1.0f;
        _sprite.Modulate = Tint(phase, ripeness, frame.Exhausted);

        // 흰 플래시는 틴트 위에 셰이더가 민다 — COLOR 에 modulate 가 이미 곱해져 있어 선딜 · 탈진 틴트 위에서도 희다.
        double flash = _feel.BossHitFlashSeconds <= 0 ? 0 : _hitFlashLeft / _feel.BossHitFlashSeconds;
        _hitFlash.SetShaderParameter(_flashParam, (float)flash);

        // 장을 고른 **뒤**에 찍어야 플래시 아래 실제로 그린 장이다(Hit 의 요약).
        if (_flashLogPending)
        {
            _flashLogPending = false;
            Log.Debug("view", $"boss_flash anim={_sprite.Animation} frame={_sprite.Frame} flash={flash:0.00}");
        }
    }

    /// <summary>
    /// 판정이 선 틱. 몸이 한 번 <b>번쩍인다</b>. 여기서 퍼지던 충격파와 스파크는 걷었다 — 보스 공격에 동그라미를 안 그린다
    /// (#81 · 클래스 머리).
    /// </summary>
    public void ActiveNow()
    {
        Flash(_activeFlash, _feel.FlashSeconds);
    }

    /// <summary>
    /// 플레이어의 칼이 닿았다. 때린 것이 닿았는지가 보여야 공격에 값이 붙는다.
    ///
    /// <para>
    /// <b>희게 번쩍이기만 한다</b> (#71 · 설계 §6) — 유저: "맞으면 … 흰색으로 빛나게 플래쉬만 해주시고 공격을 멈추거나 다른
    /// 애니메이션을 재생하진 않습니다." 셰이더(<c>hit_flash.gdshader</c>)가 <c>feel.boss_hit_flash_seconds</c>(0.12초) 동안 1 에서
    /// 0 으로 희게 민다. 애니메이션도 장도 안 건드린다 — 선딜 도중에 맞으면 칼을 든 그 장이 그대로 번쩍인다.
    /// </para>
    ///
    /// <para>
    /// 전에는 작가가 그린 흰 실루엣(<c>hit_white</c> · 4장 10fps)을 애니메이션으로 틀었다(이슈 #28 — "셰이더도 modulate 도 아니다").
    /// 그러면 맞는 순간 공격 자세가 피격 자세로 바뀌어, 규칙에서는 공격이 도는데 그림은 끊겼다. 유저가 그 결정을 뒤집었다.
    /// 0.12초는 히트스톱 7프레임(0.117초)과 거의 같다 — 보스가 무너지는 틱의 한 대는 희게 멈춘 한 장면이 된다.
    /// </para>
    ///
    /// <para>
    /// 번쩍인 장을 로그로 남긴다 — "자세가 안 끊겼다" 를 스크린샷 한 장이 아니라 줄로도 본다(<c>[view][D] boss_flash</c>). 찍는 것은
    /// 여기가 아니라 <b>맞은 뒤 첫 <see cref="Show"/></b> 다. 이것은 <c>_PhysicsProcess</c> 의 <c>BattleCues.Observe</c> 가 불러 이 자리의
    /// 장은 맞기 <b>전</b>에 그린 것이다 — 여기서 찍던 때는 옛 hit_white 였어도 같은 <c>anim=attack frame=0</c> 이 나와 줄이 끊김을 못
    /// 봤다(#71 최종 리뷰 F-I4). Show 가 장을 고른 뒤에 찍으면 흰 플래시 아래 그린 장이라, 맞은 자세로 바꾸는 손질이 다시 들면 anim 이 바뀐다.
    /// </para>
    /// </summary>
    public void Hit()
    {
        _hitFlashLeft = _feel.BossHitFlashSeconds;
        _flashLogPending = true;
    }

    public void Die()
    {
        _dead = true;
        Animate("death");
    }

    /// <summary>
    /// 히트스톱. 그림만 세운다 — 시뮬레이션의 시계는 <c>Battle</c> 이 따로 멈춘다.
    /// <see cref="Show"/> 를 못 기다린다 — 같은 프레임에 걸려야 한다.
    /// </summary>
    public void Freeze(bool frozen)
    {
        _frozen = frozen;
        _sprite.SpeedScale = frozen ? 0.0f : 1.0f;
    }

    /// <summary>판정까지 얼마나 무르익었나(0 = 아직 멀었다 · 1 = 이번 프레임).</summary>
    private float Ripeness(BossPhase phase, double? nextActiveIn)
    {
        if (phase != BossPhase.Windup || nextActiveIn is not { } left || left > _feel.TellLeadSeconds)
        {
            return 0;
        }

        return Mathf.Clamp(1.0f - (float)(left / _feel.TellLeadSeconds), 0.0f, 1.0f);
    }

    private string AnimationFor(string? anim, bool exhausted)
    {
        if (_dead)
        {
            return "death";
        }

        // **탈진은 take-hit 다** (#72 · 설계 §4.3 · §6). 제 속도(10fps · 0.4초)로 한 번 돌고 마지막 장에 선다: 반복하지 않는
        // 애니메이션이라 엔진이 마지막 장에서 멈춘다. 패리로든 경직 게이지로든(#71) 같은 그림이다. 탈진한 보스를 때려도 흰 플래시만
        // 얹혀 이 장이 안 끊긴다(Hit).
        if (exhausted)
        {
            return "hit";
        }

        // **규칙의 단계가 가리키는 그림이다** (#72 · 설계 §6) — 선딜 · 판정 · 후딜 모두. 후딜의 장(칼을 내린 f3)과
        // 끝(idle)이 "끝났다" 를 말하고, 몸 색(후딜 틴트)이 거들어 "아직 온다" 와 갈린다. 패턴이 안 돌면 idle 이다:
        // 이름이 비었을 때 조용히 attack 을 쓰면 그림이 빠진 단계가 "잘 도는 것처럼" 보인다.
        return !string.IsNullOrEmpty(anim) ? anim : "idle";
    }

    private void Flash(Color color, double seconds)
    {
        _flashColor = color;
        _flashTotal = seconds;
        _flashLeft = seconds;
    }

    private Color Tint(BossPhase phase, float ripeness, bool exhausted)
    {
        // 맞은 흰 플래시는 틴트가 아니라 셰이더다(Hit) — 그 위에 얹히므로 여기서 틴트를 걷을 필요가 없다. 옛 흰 실루엣(hit_white)은
        // 작가의 픽셀을 덧칠하지 않으려고 틴트를 하양으로 걷었다: 크림슨 선딜 중의 피격이 "붉은 실루엣" 이 됐다(이슈 #28).
        if (exhausted)
        {
            return _exhaustTint;
        }

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

    /// <summary>
    /// 그 애니메이션의 <paramref name="frame"/> 번째 장에 <b>세운다</b> (#72 · 설계 §6) — 타임라인 단계가 가리키는 장이다.
    /// 장이 바뀐 때만 손댄다: 매 프레임 <c>Frame</c> 을 다시 넣으면 엔진이 그 장의 진행을 0 으로 되돌릴 뿐 그림은 같다.
    /// 없는 장이면 손대지 않는다 — 데이터 테스트(<c>PatternDataTests</c>)가 장 수를 팩과 대 보므로 여기 올 일이 없다.
    /// </summary>
    private void HoldAt(string name, int frame)
    {
        Animate(name);
        if (_sprite.SpriteFrames is not { } frames || !frames.HasAnimation(name)
            || frame >= frames.GetFrameCount(name) || (_sprite.Frame == frame && !_sprite.IsPlaying()))
        {
            return;
        }

        _sprite.Frame = frame;
        _sprite.Pause();
    }

    /// <summary>
    /// 이름이 바뀔 때만 재생하고, 그때마다 바닥을 다시 맞춘다. <paramref name="force"/> 는 이름이 같아도
    /// 그렇게 한다 — 엔진이 이름만 바꿔 두고 재생도 맞춤도 안 한 자리(<see cref="Load"/>)가 쓴다.
    /// </summary>
    private void Animate(string name, bool force = false)
    {
        if (_sprite.SpriteFrames is null || (!force && _sprite.Animation == name))
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
