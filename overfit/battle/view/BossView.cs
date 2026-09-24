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
///
/// <para>
/// 색은 <b>둘</b>이고 뜻은 하나씩이다 (이슈 #53). 호박은 앞의 연타 — 막아도 된다.
/// 빨강은 마무리 — <b>가드로 못 막는다 = 받아쳐라</b>. 빨강 위의 <c>危</c> 는 같은 말을 색이 아닌
/// 모양으로 한 번 더 한다(색각 이상에서는 빨강과 호박이 같은 색이 된다 — <c>BossTell</c>).
/// </para>
/// </summary>
public partial class BossView : Node2D
{
    /// <summary>선딜이 무르익었을 때의 몸 색. 판정이 가까울수록 이쪽으로 간다.</summary>
    private static readonly Color _windupTint = new(2.00f, 0.72f, 0.30f);

    /// <summary>
    /// <b>가드 불가</b>(마무리) 선딜의 몸 색 — 크림슨 (이슈 #53).
    ///
    /// <para>
    /// <b>이 색은 주인이 바뀌었다.</b> 원래는 <c>parryable: false</c> 의 "받아치지 마라 · 대시해라"
    /// 였고(이슈 #27), 그때는 붉은색을 다른 뜻에 못 썼다 — 두 뜻이 정반대라 같은 색이면
    /// 플레이어가 거꾸로 반응한다. 그 주인(점프 강타)이 이슈 #48 에서 사라져 빨강이 비었고,
    /// 이제 <b>빨강 = 가드로 못 막는다 = 받아쳐라</b> 다. 마무리가 아홉 변종 전부 가드 불가라
    /// 이 색은 모든 패턴의 마지막 한 대에 뜨고, 받아치면 보스가 굳어 거기 최대 차지가 들어간다.
    /// </para>
    ///
    /// <para>
    /// <b>한 가지 뜻이어야 한다.</b> 한때 빨강을 "마무리" 에, 危 를 "가드 불가" 에 따로 매단 적이
    /// 있는데 그러면 빨강이 단계에 따라 "막힌다" 와 "안 막힌다" 를 다 말했다 — 위에 적은 바로 그
    /// 색-뜻 충돌이다(유저가 짚었다).
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>패리 불가 패턴이 돌아오면 이 색을 나눠 쓸 수 없다.</b> 그때는 그쪽에 다른 신호를
    /// 줘야 한다 — 값은 <c>parryable</c> 태그로 규칙에 그대로 살아 있으니 그림만 정하면 된다.
    /// 여기 다시 크림슨을 얹는 것만은 안 된다: 한 색이 "피해라" 와 "받아쳐라" 를 동시에 말한다.
    /// </para>
    /// </summary>
    private static readonly Color _guardBreakTint = new(2.40f, 0.10f, 0.22f);

    private static readonly Color _recoverTint = new(0.70f, 0.70f, 0.78f);

    /// <summary>
    /// <b>굳어 있는</b> 동안의 몸 색 (이슈 #53). 후딜(<see cref="_recoverTint"/>)보다 더 식는다 —
    /// 팩에 지친 모션이 없어서, 느려진 idle 과 이 색 둘이 "숨이 찼다" 를 나눠 진다.
    /// </summary>
    private static readonly Color _staggerTint = new(0.52f, 0.50f, 0.60f);

    private static readonly Color _tellRingColor = new(1.00f, 0.74f, 0.30f, 0.85f);

    /// <summary>가드 불가 예고의 링 색. 몸 색(<see cref="_guardBreakTint"/>)과 같은 빨강이다 —
    /// 링과 몸이 갈리면 둘 중 하나는 안 읽힌다.</summary>
    private static readonly Color _guardBreakRingColor = new(1.00f, 0.06f, 0.20f, 0.95f);
    private static readonly Color _shockRingColor = new(1.00f, 0.80f, 0.35f, 1.00f);
    private static readonly Color _activeFlash = new(2.60f, 2.30f, 1.60f);

    private AnimatedSprite2D _sprite = null!;
    private RingBurst _ring = null!;
    private BossTellLayer _tell = null!;
    private FeelBalance _feel = null!;

    private double _flashLeft;
    private double _flashTotal;
    private Color _flashColor;
    private double _hitPoseLeft;
    private bool _dead;

    /// <summary>히트스톱으로 그림이 멈춰 있나. 경직의 느린 재생과 <b>한 자리에서</b> 정해야 한다 —
    /// 둘이 각자 <c>SpeedScale</c> 을 쓰면 나중에 쓰는 쪽이 이겨서 히트스톱 중에도 그림이 돈다.</summary>
    private bool _frozen;

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

        // 예고 표지는 링과 **다른 노드**다. 링은 "언제" 를 말하고 표지는 "무엇" 을 말하므로
        // 수명이 다르다 — 한 노드에서 둘 다 그리면 링을 끄는 프레임에 표지도 같이 사라진다.
        //
        // 危 표지는 보스 **머리 위**에 선다 (이슈 #47). 몸에 겹치면 획이 실루엣에 먹혀
        // "무슨 글자인가" 가 안 읽힌다. 높이는 링이 도는 자리(boss_ring_offset_y)의 두 배 남짓이라
        // 보스 키(297px)를 넘고, 크기는 링 굵기에서 끌어온다 — 숫자를 여기 박으면 feel 을 고쳐도 안 따라온다.
        _tell = new BossTellLayer
        {
            LineWidth = (float)_feel.RingWidth * 1.3f,
            MarkHeight = (float)_feel.BossRingOffsetY * 2.4f,
            MarkSize = (float)_feel.RingWidth * 14.0f,
        };
        AddChild(_tell);
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
        Position = new Vector2((float)frame.X, 0);

        // 스프라이트 원본은 오른쪽을 본다 — 팩의 규약이고 FighterView 도 같다.
        // **뒤집어도 자리가 안 어긋난다**: Offset 의 x 는 0 이고(AlignToGround 는 y 만 건드린다)
        // 링도 x=0 에 선다. 좌우가 비대칭인 것은 예고 표지 하나뿐이고, 그건 Battle 이
        // 이 Facing 으로 이미 뒤집어 넘긴다.
        _sprite.FlipH = frame.Facing < 0;

        BossPhase phase = frame.Phase;

        _flashLeft = System.Math.Max(0, _flashLeft - dt);
        _hitPoseLeft = System.Math.Max(0, _hitPoseLeft - dt);
        HoldLastFrameWhenDead();

        // **굳은 동안에는 예고를 안 그린다** (이슈 #53). 경직 중에는 타임라인이 안 밀리므로
        // NextActiveIn 이 그대로 멈춰 있고, 그러면 링도 표지도 얼어붙은 채 "곧 온다" 를
        // 2.3초 내내 거짓말한다 — 지금 오는 것은 아무것도 없다.
        float ripeness = frame.Staggered ? 0 : Ripeness(phase, frame.NextActiveIn);

        // **빨강은 가드 불가 하나를 말한다** (이슈 #53). 몸 색 · 링 · 표지가 전부 이 한 칸을 읽는다 —
        // 셋 중 하나라도 다른 칸을 읽으면 같은 순간에 화면이 두 말을 한다.
        bool guardBreak = frame.Tell is { GuardBreak: true };
        Color tellColor = guardBreak ? _guardBreakRingColor : _tellRingColor;
        if (ripeness > 0)
        {
            // 판정이 가까울수록 링이 **조여 든다.** 퍼지는 충격파와 방향이 반대라 둘을 헷갈릴 수 없다.
            _ring.Charge(Mathf.Lerp((float)_feel.TellRingFrom, (float)_feel.TellRingTo, ripeness), tellColor);
        }

        // 표지는 선딜 **내내** 보인다. 무르익음에만 묶으면 tell_lead_seconds 밖의 선딜이
        // 통째로 무표지가 되는데, 백장의 올려베기가 정확히 그 구간이 가장 긴 패턴이다 —
        // "크게 예고한다" 가 설계인 패턴이 예고를 제일 늦게 받는 것은 뒤집힌 것이다.
        if (phase == BossPhase.Windup && !frame.Staggered && frame.Tell is { } mark)
        {
            _tell.Show(mark, new Color(tellColor.R, tellColor.G, tellColor.B, 0.45f + (0.55f * ripeness)));
        }

        Animate(AnimationFor(phase, frame.Anim, frame.Staggered));

        // 재생 속도를 **여기 한 자리에서** 정한다. 히트스톱이 이기고(그건 시간을 세운 것이다),
        // 아니면 굳은 동안 idle 이 느려진다 — 팩에 지친 모션이 없어서 고른 방법이다.
        _sprite.SpeedScale = _frozen ? 0.0f
            : frame.Staggered ? (float)_feel.StaggerAnimSpeed
            : 1.0f;

        _sprite.Modulate = Tint(phase, ripeness, guardBreak, frame.Staggered);
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
    /// <b>헛스윙이 지나갔다</b> (이슈 #48). 판정이 <b>없는</b> 박자라 <see cref="ActiveNow"/> 와
    /// 같은 그림이면 안 된다 — 같으면 화면이 거짓말을 하고, 플레이어는 그 변종을 배울 길이 없다.
    ///
    /// <para>
    /// 그래서 셋을 뺀다: 몸의 섬광 · 스파크 · 링의 진하기. 남는 것은 <b>비어서 퍼지는 고리</b>
    /// 하나이고, 그것이 "칼은 지나갔는데 아무것도 안 나왔다" 의 그림이다.
    /// 안 그리는 쪽은 답이 아니다 — 안 보이는 헛스윙은 미끼가 아니라 그냥 빈 시간이다.
    /// </para>
    /// </summary>
    public void FeintNow() =>
        _ring.Burst(
            (float)_feel.TellRingTo,
            (float)_feel.BossRingTo * 0.6f,
            _feel.BurstSeconds,
            new Color(_shockRingColor.R, _shockRingColor.G, _shockRingColor.B, 0.35f),
            sparks: 0,
            sparkLength: 0);

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
    /// 실제로 <c>SpeedScale</c> 을 정하는 것은 <see cref="Show"/> 한 자리다(경직의 느린 재생과
    /// 다투지 않게), 다만 히트스톱은 <see cref="Show"/> 를 못 기다린다 — 같은 프레임에 걸려야 한다.
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

    private string AnimationFor(BossPhase phase, string? anim, bool staggered)
    {
        if (_dead)
        {
            return "death";
        }

        if (_hitPoseLeft > 0)
        {
            return "hit_white";
        }

        // **굳은 동안은 idle 이다** (이슈 #53). Medieval King Pack 2 에는 지친 모션이 없다 —
        // 시트가 열뿐이고(idle · run · jump · fall · attack1~3 · take-hit · take-hit-white · death)
        // 그중 어느 것도 "숨이 차 서 있다" 가 아니다. 없는 이름으로 Play 하면 아래 Animate 가
        // [W] 한 줄 남기고 **아무것도 안 바꾸므로**, 칼을 든 공격 자세가 2.3초 동안 그대로 선다.
        // 그래서 지어내지 않고 있는 것을 느리게 돌린다(Show 가 SpeedScale 을 깎는다).
        if (staggered)
        {
            return "idle";
        }

        // 선딜에만 공격 모션이다. 후딜까지 두면 "아직 온다" 와 "끝났다" 가 같은 그림이 된다.
        // **어느 모션인가는 패턴이 정한다**(patterns.json 의 tell.anim) — 팩의 셋을 백장의 셋에
        // 하나씩 붙였다. 이름이 비면 옛 이름으로 돌아가지 않고 idle 이다: 조용히 attack 을 쓰면
        // 배정이 빠진 패턴이 "잘 도는 것처럼" 보인다.
        return phase == BossPhase.Windup && !string.IsNullOrEmpty(anim) ? anim : "idle";
    }

    private void Flash(Color color, double seconds)
    {
        _flashColor = color;
        _flashTotal = seconds;
        _flashLeft = seconds;
    }

    private Color Tint(BossPhase phase, float ripeness, bool guardBreak, bool staggered)
    {
        // 흰 피격 실루엣은 **작가가 그린 픽셀 그대로** 나가야 한다. 선딜 틴트를 그 위에 얹으면
        // 크림슨 선딜 중의 피격이 "붉은 실루엣" 이 되어, 정작 흰색이라는 것이 안 보인다 —
        // 화면에서 그 둘을 나란히 보고 알았다(docs/shots/battle-5b-boss-hit.png).
        // 셰이더로 흰색을 만드는 대신 그림을 쓰기로 한 이상, 그 그림을 덧칠하지도 않는다.
        if (_hitPoseLeft > 0)
        {
            return Colors.White;
        }

        // 굳은 것이 예고를 이긴다. 경직 중에도 CurrentPattern 은 살아 있어 Phase 는 Windup 인데,
        // 그 색(호박·빨강)은 "곧 온다" 는 뜻이라 굳은 보스에게는 거짓말이다.
        if (staggered)
        {
            return _staggerTint;
        }

        Color baseTint = phase switch
        {
            BossPhase.Windup => Colors.White.Lerp(guardBreak ? _guardBreakTint : _windupTint, ripeness),
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
