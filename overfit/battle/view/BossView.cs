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
/// 그래서 선딜과 판정이 서는 순간을 확실히 가른다 — 선딜에는 단계가 붙든 공격 자세와 함께
/// 예고 링이 <b>조여 들고</b>, 판정이 서면 충격파가 <b>퍼진다</b>. 방향이 반대라
/// 둘을 헷갈릴 수 없다.
/// </para>
///
/// <para>
/// <b>무엇이 오는지는 그림이 말한다</b> (#72 · 설계 §6). 규칙의 단계가 가리키는 장(<c>anim</c> · <c>frame</c>)을 그대로
/// 붙든다 — 3연격은 칼을 든 f0, 점프 공격은 웅크린 <c>jump</c> f0 다. 옛 변종의 예고 표지(칼 · 끌기 · 도약 표지 아홉)와
/// 빨간 가드 불가 마무리(크림슨 · 링 · <c>危</c>)는 변종과 같이 걷었다 — 새 두 패턴에는 가드 불가 판정이 없어 빨강이 말할
/// 것이 없다. 남은 링과 선딜 틴트는 "언제" 를 말하고, 5번 PR(링의 조임 · 틴트의 무르익음) · 6번 PR(링 전부)이 걷는다.
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

    private static readonly Color _tellRingColor = new(1.00f, 0.74f, 0.30f, 0.85f);
    private static readonly Color _shockRingColor = new(1.00f, 0.80f, 0.35f, 1.00f);
    private static readonly Color _activeFlash = new(2.60f, 2.30f, 1.60f);

    private AnimatedSprite2D _sprite = null!;
    private RingBurst _ring = null!;
    private FeelBalance _feel = null!;

    private double _flashLeft;
    private double _flashTotal;
    private Color _flashColor;
    private double _hitPoseLeft;
    private bool _dead;

    /// <summary>히트스톱으로 그림이 멈춰 있나.</summary>
    private bool _frozen;

    /// <summary>
    /// 이번 탈진에 <c>hit</c> 를 이미 틀었나. 틀었으면 돌아올 때(탈진한 보스를 때려 <c>hit_white</c> 가 끼었다 끝날 때) 처음부터
    /// 다시 돌지 않고 마지막 장에 선다 — 탈진 자세로 돌아오는 것이다(설계 §4.3).
    /// </summary>
    private bool _exhaustShown;

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
        // **뒤집어도 자리가 안 어긋난다**: Offset 의 x 는 0 이고(AlignToGround 는 y 만 건드린다) 링도 x=0 에 선다.
        _sprite.FlipH = frame.Facing < 0;

        BossPhase phase = frame.Phase;

        _flashLeft = System.Math.Max(0, _flashLeft - dt);
        _hitPoseLeft = System.Math.Max(0, _hitPoseLeft - dt);
        HoldLastFrameWhenDead();

        // 탈진하면 패턴이 끊겨 Phase 가 Idle 이므로 링은 저절로 안 그려진다 (#72 · 설계 §4.3).
        float ripeness = Ripeness(phase, frame.NextActiveIn);
        if (ripeness > 0)
        {
            // 판정이 가까울수록 링이 **조여 든다.** 퍼지는 충격파와 방향이 반대라 둘을 헷갈릴 수 없다.
            _ring.Charge(Mathf.Lerp((float)_feel.TellRingFrom, (float)_feel.TellRingTo, ripeness), _tellRingColor);
        }

        string anim = AnimationFor(frame.Anim, frame.Exhausted);
        if (anim == "hit" && _exhaustShown && _sprite.Animation != "hit")
        {
            HoldLastFrame("hit");
        }
        else if (anim == frame.Anim && frame.Frame is int held)
        {
            HoldAt(anim, held);
        }
        else
        {
            Animate(anim);
        }

        _exhaustShown = frame.Exhausted && (_exhaustShown || anim == "hit");
        _sprite.SpeedScale = _frozen ? 0.0f : 1.0f;
        _sprite.Modulate = Tint(phase, ripeness, frame.Exhausted);
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

    /// <summary>
    /// 플레이어의 칼이 닿았다. 때린 것이 닿았는지가 보여야 공격에 값이 붙는다.
    ///
    /// <para>
    /// <b>흰색은 셰이더도 modulate 도 아니라 작가가 그린 그림이다</b> (이슈 #28). 두 팩 모두
    /// <c>Take Hit - white silhouette</c> 를 포함하고 <c>hit_white</c> 로 잘려 있다 — 픽셀아트를
    /// 코드로 하얗게 만들면 외곽선과 그림자까지 같이 날아가 실루엣이 뭉개진다. 그릴 것이 이미
    /// 있는데 흉내 내지 않는다.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>흰 프레임은 애니메이션의 <u>두 번째</u>다.</b> 팩의 take-hit-white 는 4프레임이고
    /// (피격 자세 → <b>전부 흰색</b> → 복귀 ×2) 10fps 라, 흰색은 0.10~0.20초 구간에 있다.
    /// <c>hit_flash_seconds</c>(0.20)가 그 구간에서 정확히 끝나므로 "자세 → 번쩍 → 컷" 이 된다 —
    /// 작가가 정한 타이밍이고, 앞당기려고 <c>Frame</c> 을 손으로 건드리면 그 자세가 사라진다.
    /// 스크린샷이 이 연출을 증명하려면 <b>피격 7프레임 뒤</b>를 찍어야 한다(ShotRunner).
    /// </para>
    /// </summary>
    public void Hit() => _hitPoseLeft = _feel.HitFlashSeconds;

    public void Die()
    {
        _dead = true;
        _hitPoseLeft = 0;
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

        if (_hitPoseLeft > 0)
        {
            return "hit_white";
        }

        // **탈진은 take-hit 다** (#72 · 설계 §4.3 · §6) — 흰 실루엣(hit_white)이 아니다. 제 속도(10fps · 0.4초)로 한 번 돌고
        // 마지막 장에 선다: 반복하지 않는 애니메이션이라 엔진이 마지막 장에서 멈춘다. 패리로든 경직 게이지로든(4번 PR)
        // 같은 그림이다.
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
        // 흰 피격 실루엣은 **작가가 그린 픽셀 그대로** 나가야 한다. 선딜 틴트를 그 위에 얹으면
        // 크림슨 선딜 중의 피격이 "붉은 실루엣" 이 되어, 정작 흰색이라는 것이 안 보인다 —
        // 화면에서 그 둘을 나란히 보고 알았다(docs/shots/battle-5b-boss-hit.png).
        // 셰이더로 흰색을 만드는 대신 그림을 쓰기로 한 이상, 그 그림을 덧칠하지도 않는다.
        if (_hitPoseLeft > 0)
        {
            return Colors.White;
        }

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
    /// 그 애니메이션의 <b>마지막 장에 세운다</b> — 탈진한 보스를 때려 <c>hit_white</c> 가 끼었다 끝나면 take-hit 를 처음부터
    /// 다시 돌지 않고 서 있던 자세로 돌아온다.
    /// </summary>
    private void HoldLastFrame(string name)
    {
        Animate(name);
        if (_sprite.SpriteFrames is { } frames && frames.HasAnimation(name))
        {
            _sprite.Frame = frames.GetFrameCount(name) - 1;
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
