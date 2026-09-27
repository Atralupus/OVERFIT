using System.Collections.Generic;
using Godot;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 파이터 스프라이트의 <b>시트를 어느 장에 세우고 언제 흘리나</b> — 칼질의 선딜 · 칼 · 경직, 대시와 패리의 마지막 자세, 가드의 멈춘 자세.
/// <see cref="FighterView"/> 가 무엇을 그릴지(자세 · 색 · 링 · 섬광)를 정하고, 이 클래스는 그 자세를 시트의 장으로 옮긴다.
///
/// <para>
/// <see cref="FighterView"/> 에서 떼어 냈다(#96 · CLAUDE.md §7) — 그 파일이 주석 빼고 398줄이었고, 늘어난 것이 전부 "이 장에 멈추고
/// 저 장에서 푼다" 였다(#54 선딜 · #82 경직). 노드가 아니다: 스프라이트는 뷰의 것이고 여기서는 빌려 쓴다.
/// </para>
/// </summary>
public sealed class FighterAnimator
{
    private readonly AnimatedSprite2D _sprite;

    /// <summary>
    /// 칼질마다 그릴 시트 — 1타 · 2타 (<c>fighters.json</c> 의 <c>combo</c>, <c>Battle</c> 이 옮겨 준다).
    /// 여기 숫자를 박으면 시트를 갈아끼울 때 조용히 엉뚱한 장에서 시작한다.
    /// </summary>
    private SwingSheet[] _swings = System.Array.Empty<SwingSheet>();

    /// <summary>패리가 도는 시트 — <c>attack2</c> 의 f0~f3 (설계 §5.3). <c>Battle</c> 이 데이터에서 옮겨 준다.</summary>
    private SwingSheet _parry;

    /// <summary>
    /// 패리가 도는 장 수(<c>parry_anim_frames</c> — f0~f3 이면 4). 패리의 마지막 장이 어디인지를 이것으로 안다(<see cref="HoldParry"/>) —
    /// 시트(<c>attack2</c>)는 그 뒤에 칼이 나가는 f4 · f5 가 더 있다.
    /// </summary>
    private int _parryFrames;

    /// <summary>가드가 멈춰 서는 장 — 칼을 사선으로 세운 <c>attack2</c> f1 (#96 · 설계 §6). <c>Battle</c> 이 데이터에서 옮겨 준다.</summary>
    private StillFrame _guard;

    /// <summary>지금 그리는 칼질이 몇 번째인가 (<see cref="SwingBegan"/> 이 정한다).</summary>
    private int _swing;

    /// <summary>
    /// 이번 칼질에서 칼이 이미 나갔나. <b>규칙이 알려 준다</b>(<see cref="AttackActive"/>) —
    /// 시트의 시계로 짐작하지 않는다. 그 뒤로는 칼이 나간 장부터 시트가 그냥 흐른다(칼 → 잔상).
    /// </summary>
    private bool _bladeOut;

    /// <summary>새 칼질이 시작됐나 (<see cref="SwingBegan"/>). 선딜 그림을 시작하는 장부터 다시 세운다.</summary>
    private bool _windupFresh;

    /// <summary>
    /// 칼질 뒤 경직이라 <c>idle</c> 첫 장에 멈춰 세워 두었나 (<see cref="StandAfterSwing"/>). 푸는 쪽(<see cref="Unstand"/>)이
    /// <b>이 파일이 멈춘 것만</b> 다시 흘리려고 든다.
    /// </summary>
    private bool _standing;

    public FighterAnimator(AnimatedSprite2D sprite) => _sprite = sprite;

    /// <summary>지금 그리는 칼질이 몇 번째인가 (0 = 1타). 2타의 섬광이 희게 타는지를 뷰가 이것으로 가른다.</summary>
    public int Swing => _swing;

    /// <summary>지금 칼질의 시트 이름. 칼질 목록이 빈 판이면 null 이다(<see cref="Sheet"/>).</summary>
    public string? SwingAnim => Sheet.Anim;

    /// <summary>패리가 도는 시트 이름.</summary>
    public string ParryAnim => _parry.Anim;

    /// <summary>
    /// 가드가 서는 시트 이름. 팩에 그 시트가 없으면 <c>idle</c> 이다 — #96 전의 가드 그림(선 자세 위의 색과 멈춘 링)으로 물러선다.
    /// 없는 이름을 <see cref="Animate"/> 에 넘기면 가드 내내 매 프레임 <c>[W]</c> 가 찍히고 앞 그림이 남는다(<see cref="FighterView.Load"/> 가
    /// 한 번만 알린다).
    /// </summary>
    public string GuardAnim => HasAnimation(_guard.Anim) ? _guard.Anim : "idle";

    /// <summary>지금 칼질의 시트. 목록이 비었으면(스프라이트가 없는 판) 빈 이름이라 아래 갈래가 전부 시트 없음으로 빠진다.</summary>
    private SwingSheet Sheet => _swings.Length == 0 ? default : _swings[System.Math.Clamp(_swing, 0, _swings.Length - 1)];

    /// <summary>
    /// 칼질마다의 시트와 패리의 시트, 가드의 장을 받는다 — <see cref="FighterView.Load"/> 가 옮겨 준다.
    /// <paramref name="parry"/> 는 칼이 나가는 장이 없다(<c>BladeFrame</c> 은 안 쓴다).
    /// <paramref name="parryFrames"/> 는 그 시트에서 패리가 도는 장 수다(<c>parry_anim_frames</c>).
    /// <paramref name="guard"/> 는 가드가 멈춰 서는 장이다(<c>guard_anim</c> · <c>guard_frame</c>).
    /// </summary>
    public void SetSheets(IReadOnlyList<SwingSheet> swings, SwingSheet parry, int parryFrames, StillFrame guard)
    {
        _parry = parry;
        _guard = guard;
        _parryFrames = parryFrames;
        _swings = new SwingSheet[swings.Count];
        for (int i = 0; i < _swings.Length; i++)
        {
            _swings[i] = swings[i];
        }
    }

    /// <summary>
    /// 한 프레임의 자세를 시트의 장으로 옮긴다. <paramref name="anim"/> 은 그 자세의 이름이고(<see cref="FighterView"/> 가 고른다 —
    /// 맞은 자세 · 죽은 자세가 이긴다), <paramref name="free"/> 는 죽지도 맞은 자세도 아닌가다.
    /// </summary>
    public void Show(FighterFrame frame, string anim, bool free)
    {
        // 칼질(1타 · 2타)은 이름이 아니라 **장**으로 말한다 — 같은 시트에서 어느 장에 서 있느냐가
        // 선딜인지 칼이 나간 뒤인지를 가르는데, 이름만 보는 Animate 는 그 둘을 구별하지 못한다.
        // 맞았거나 죽었으면 그 그림이 이긴다: 칼질은 규칙에서 안 끊겼지만 그림은 맞은 자세가 이긴다.
        bool swinging = frame.Pose == FighterPose.Attack && free;
        if (swinging && frame.Stiff)
        {
            StandAfterSwing();
            return;
        }

        Unstand();
        if (swinging && !_bladeOut)
        {
            HoldWindup();
        }
        else if (swinging)
        {
            FollowBlade();
        }
        else
        {
            Animate(anim);
            if (free)
            {
                HoldDash(frame, anim);
                HoldParry(frame);
                HoldGuard(frame);
            }
        }
    }

    /// <summary>새 칼질이 시작됐다 — <see cref="FighterView.SwingBegan"/> 의 주석을 보라.</summary>
    /// <param name="step">몇 번째 칼질인가 (0 = 1타).</param>
    public void SwingBegan(int step)
    {
        _swing = step;
        _bladeOut = false;
        _windupFresh = true;
    }

    /// <summary>패리가 시작된 <b>그 틱</b> — 시트를 처음부터 다시 돌린다. 왜 사건으로 트는지는 <see cref="FighterView.ParryBegan"/> 의 주석을 보라.</summary>
    public void ParryBegan()
    {
        if (!HasSheet(_parry))
        {
            return;
        }

        _sprite.Play(_parry.Anim, SpeedFor(_parry));
        _sprite.SetFrameAndProgress(_parry.StartFrame, 0.0f);
        AlignToGround(_parry.Anim);
    }

    /// <summary>
    /// 공격 판정이 선 틱 — <b>그림을 칼이 나가는 장으로 맞춰 세운다</b>(이슈 #54). 왜 맞춰 세우는지는
    /// <see cref="FighterView.AttackActive"/> 의 주석을 보라 — 여기서 안 넘기면 1타든 2타든 칼이 아예 안 나간다.
    /// </summary>
    public void AttackActive()
    {
        _bladeOut = true;
        ShowBlade(Sheet.BladeFrame);
    }

    /// <summary>
    /// 이름이 바뀔 때만 재생하고, 그때마다 바닥을 다시 맞춘다.
    /// 매 프레임 <c>Play</c> 하면 안 바뀐 것처럼 보이지만 애니메이션이 1프레임에 붙들린다.
    /// <paramref name="force"/> 는 이름이 같아도 그렇게 한다 — 엔진이 이름만 바꿔 두고 재생도 맞춤도 안 한
    /// 자리(<see cref="FighterView.Load"/>)가 쓴다.
    /// </summary>
    public void Animate(string name, bool force = false)
    {
        if (_sprite.SpriteFrames is null || (!force && _sprite.Animation == name))
        {
            return;
        }

        if (!_sprite.SpriteFrames.HasAnimation(name))
        {
            // 없는 이름으로 Play 하면 엔진이 ERROR: 를 찍고, 그건 헤드리스 판정(judge_headless)을
            // 실패시킨다. 지금 팩에는 다 있지만(install_assets.py 의 REQUIRED_ANIMS 가 여섯을 확인한다 — 2타 · 패리 ·
            // 가드의 attack2 까지) 팩을 갈아끼우는 것이 이 파일의 전제라 확인은 남긴다.
            Log.Warn("view", $"anim_missing name={name}");
            return;
        }

        _sprite.Play(name);
        AlignToGround(name);
    }

    /// <summary>
    /// 이 칼질을 시트의 속도의 몇 배로 돌리나 — 2타는 같은 attack2 시트를 반속(6fps)으로 돈다(설계 §5.1).
    /// 시트의 속도는 <c>.tres</c> 가 알고, 칼질의 속도는 데이터(<c>combo[].fps</c>)가 안다.
    /// </summary>
    private float SpeedFor(SwingSheet sheet)
    {
        double native = _sprite.SpriteFrames?.GetAnimationSpeed(sheet.Anim) ?? 0;
        return native <= 0 ? 1.0f : (float)(sheet.Fps / native);
    }

    /// <summary>시트가 있는가 — 없는 이름으로 Play 하면 엔진이 ERROR 를 찍는다(<see cref="Animate"/> 의 주석).</summary>
    private bool HasSheet(SwingSheet sheet) => HasAnimation(sheet.Anim);

    /// <summary>팩에 그 이름의 애니메이션이 있는가. 스프라이트가 없는 판이면 없다.</summary>
    private bool HasAnimation(string? name) =>
        _sprite.SpriteFrames is { } frames && !string.IsNullOrEmpty(name) && frames.HasAnimation(name);

    /// <summary>
    /// 선딜 — 1타든 2타든 같은 규칙이다. <b>시작하는 장부터 돌리다가 칼이 나가기 바로 앞 장에서
    /// 세운다.</b> 둘 다 칼을 끝까지 뒤로 뺀 장에서 칼이 나가기를 기다린다.
    ///
    /// <para>
    /// 세우는 것이 핵심이다. <c>Play</c> 만 하고 두면 시트가 그대로 돌아 <b>선딜 중에 칼이 나간다.</b>
    /// 칼이 나가는 순간은 이 메서드가 아니라 규칙이 정한다(<see cref="AttackActive"/>) — 여기서는 기다릴 뿐이다.
    /// </para>
    ///
    /// <para>
    /// 지금 데이터에서 1타는 시작하는 장과 멈추는 장이 같은 3번이라 누르자마자 그 자세로 선다(이슈 #54).
    /// 2타는 둘이 갈라진다(시작 0 · 선딜 네 장) — 뒤로 빼는 동작을 반속으로 실제로 감고 나서 멈춘다. 선딜이 곧
    /// 그 장수라(FighterDataTests) 멈춘 장을 제 길이만큼 보이고 판정 틱에 칼 장으로 넘어간다
    /// (흘려보냈을 때와 같은 그림이다).
    /// </para>
    /// </summary>
    private void HoldWindup()
    {
        SwingSheet sheet = Sheet;
        if (!HasSheet(sheet))
        {
            return;
        }

        if (_windupFresh || _sprite.Animation != sheet.Anim)
        {
            _windupFresh = false;
            _sprite.Play(sheet.Anim, SpeedFor(sheet));
            _sprite.SetFrameAndProgress(sheet.StartFrame, 0.0f);
            AlignToGround(sheet.Anim);
        }

        int last = _sprite.SpriteFrames!.GetFrameCount(sheet.Anim) - 1;
        int hold = System.Math.Min(System.Math.Max(sheet.StartFrame, sheet.BladeFrame - 1), last);
        if (_sprite.Frame >= hold)
        {
            _sprite.Frame = hold;
            _sprite.Pause();
        }
    }

    /// <summary>
    /// 칼이 나간 뒤 — 맞춰 세운 칼 장에서 시트가 그냥 흐른다(칼 → 잔상). 판정과 후딜이 각 한 장이라
    /// (fighters.json 의 _note_attack) 시트의 시계가 곧 규칙의 시계다.
    ///
    /// <para>
    /// 흐르던 칼질이 피격 자세에 끊겼다 돌아오면 칼은 <b>이미 지나갔다</b> — 칼 다음 장(잔상)에서 잇는다.
    /// 처음부터 다시 돌리면 끝난 칼질이 다시 칼을 빼는 그림이 된다.
    /// </para>
    /// </summary>
    private void FollowBlade()
    {
        if (_sprite.Animation != Sheet.Anim)
        {
            ShowBlade(Sheet.BladeFrame + 1);
        }
    }

    /// <summary>
    /// 칼질 뒤 경직 (#82) — 시트는 <b>제 속도로 끝까지</b> 흐르게 두고(칼 → 잔상), 다 돌면 <b><c>idle</c> 의 첫 장에 멈춰 선다</b>.
    /// 칼은 끝났고 서 있는 것이 경직이라, 그림도 그 말만 한다.
    ///
    /// <para>
    /// <b>마지막 장을 붙들지 않는다 — 실제로 밟았다.</b> 처음에는 시트의 마지막 장(f5)을 경직 내내 붙들었는데, 두 시트 다 f5 가 흩어지는
    /// 흰 궤적이다. 판정은 이미 꺼졌는데 큰 흰 호가 1타 뒤 0.40초 · 2타 뒤 0.50초 동안 얼어붙어 살아 있는 칼로 읽혔다(<c>battle-6-windup</c> ·
    /// <c>10e</c> · <c>12b</c> · #82 리뷰). 대시 · 패리처럼 그 행동의 마지막 자세를 붙들 수 없는 까닭이 그것이다 — 칼질의 마지막 자세가
    /// 곧 궤적이다. 팩에 칼을 거둔 장이 따로 없어 선 자세(<c>idle</c> f0)를 빌리고, 흘리지 않고 멈춰 둔다: 경직 동안은 가만히 서 있고,
    /// 풀리면 숨 쉬는 <c>idle</c> 이 다시 흐른다(<see cref="Unstand"/>).
    /// </para>
    ///
    /// <para>
    /// 시트가 <b>칼 장부터 뒤로 흐르는 동안만</b> 기다린다 — 끝나면 엔진이 마지막 장에서 멈춘다(<c>.tres</c> 의 loop false). 그 플래그에
    /// 기대지는 않는다: 반복하는 시트로 바뀌는 날에도 첫 장으로 감기는 순간 서므로 경직 동안 칼을 다시 빼는 그림이 되지 않는다. 피격 자세에
    /// 끊겼다 돌아오면 곧장 선다 — 칼은 이미 지나갔다(<see cref="FollowBlade"/> 와 같은 이유). 1타의 경직 중 J 로 이은 2타는
    /// <see cref="SwingBegan"/> 이 선딜부터 다시 세운다.
    /// </para>
    /// </summary>
    private void StandAfterSwing()
    {
        SwingSheet sheet = Sheet;
        bool trailing = HasSheet(sheet) && _sprite.Animation == sheet.Anim && _sprite.IsPlaying()
            && _sprite.Frame >= sheet.BladeFrame;
        if (trailing || _sprite.SpriteFrames is not { } frames || !frames.HasAnimation("idle"))
        {
            return;
        }

        if (_sprite.Animation == "idle" && !_sprite.IsPlaying() && _sprite.Frame == 0)
        {
            return;
        }

        _sprite.Play("idle");
        _sprite.SetFrameAndProgress(0, 0.0f);
        _sprite.Pause();
        AlignToGround("idle");
        _standing = true;
    }

    /// <summary>
    /// 칼질 뒤 경직에서 멈춰 둔 <c>idle</c> 을 다시 흘린다(<see cref="StandAfterSwing"/>). 경직이 끝나면 자세는 대개 Idle 이라 이름이 같은
    /// <c>idle</c> 이고(가드는 제 시트로 바뀐다 · #96), <see cref="Animate"/> 는 이름이 안 바뀌었다고 보고 다시 안 튼다 — 대시 뒤 걸음의 멈춘 <c>run</c> 과 같은 함정이다
    /// (<see cref="HoldDash"/>). 이 파일이 멈춘 것만 푼다(<see cref="_standing"/>) — 다른 까닭으로 멈춘 시트는 제 주인이 푼다.
    /// </summary>
    private void Unstand()
    {
        if (!_standing)
        {
            return;
        }

        _standing = false;
        if (_sprite.Animation == "idle" && !_sprite.IsPlaying())
        {
            _sprite.Play();
        }
    }

    /// <summary>
    /// 대시 뒤 경직 (#82) — 대시의 <b>마지막 자세</b>를 붙든다(규칙: 경직도 대시다). 대시는 <c>run</c> 을 빌려 도는데, 경직 동안 흘려
    /// 두면 제자리에서 달리는 그림이 되고, <c>idle</c> 로 두면 "이제 움직일 수 있다" 고 말하는데 키는 안 먹는다 — 탈진에 색을 입히는
    /// 것과 같은 이유다(안 보이면 버그로 읽힌다). 멈춘 <c>run</c> 장과 꺼진 꼬리 색(<c>FighterView</c> 의 대시 꼬리 색)이 "아직 대시다" 를
    /// 말한다.
    ///
    /// <para>
    /// <b>푸는 것도 여기서 한다 — 걸음(<see cref="FighterPose.Run"/>)까지.</b> 걸음도 같은 <c>run</c> 이라, 경직이 끝나는 틱에
    /// 곧장 이어진 걸음이든 새 대시든 <see cref="Animate"/> 는 이름이 안 바뀌었다고 보고 다시 안 튼다. 대시 쪽만 풀던 때는
    /// 방향키를 쥔 채 대시한 사람(가장 흔한 입력이다 — 가운데 Idle 한 틱이 없다)이 멈춘 <c>run</c> 장 하나로 미끄러져 걸었다 —
    /// 키를 떼거나 다른 그림으로 바뀔 때까지. <c>battle-9c</c> 의 대본이 바로 그 길인데 찍기 직전에 키를 떼 Idle 로 찍히므로 사진에는
    /// 안 나왔다 — 리뷰가 찾았고, 매 프레임 장 번호 로그로 확인했다(대시 뒤 걸음에서 멈춘 <c>run</c> 18프레임 → 0).
    /// 시트가 <c>run</c> 일 때만 푼다 — 팩에 <c>run</c> 이 없어 <see cref="Animate"/> 가 앞 시트를 남겼으면 그 시트(붙든
    /// 패리의 마지막 장 · 가드의 멈춘 장)를 흘려서는 안 된다. 칼질의 경직은 시트가 아니라 멈춘 `idle` 을 붙들고, 그건 걸음으로 풀리기 전에 이미 흐른다.
    /// </para>
    /// </summary>
    /// <param name="frame">이 프레임의 상태.</param>
    /// <param name="anim">그 자세의 이름(<c>run</c>) — 시트가 그 이름일 때만 세우고 푼다.</param>
    private void HoldDash(FighterFrame frame, string anim)
    {
        if (frame.Pose is not (FighterPose.Dash or FighterPose.Run) || _sprite.Animation != anim)
        {
            return;
        }

        if (frame.Pose == FighterPose.Dash && frame.Stiff)
        {
            if (_sprite.IsPlaying())
            {
                _sprite.Pause();
            }
        }
        else if (!_sprite.IsPlaying())
        {
            _sprite.Play();
        }
    }

    /// <summary>
    /// 패리 — 칼을 사선으로 세운 <b>마지막 장</b>(f3)에 닿으면 거기 선다 (#82). 패리는 <c>attack2</c> 의 앞 네 장만 쓰는데 시트는 그 뒤로
    /// 칼이 나가는 f4 · f5 가 더 있어, 커밋(0.333초 = 네 장) 뒤의 <b>패리 뒤 경직</b>(0.25초) 동안 흘려 두면 휘두르지 않은 칼이 화면에서
    /// 나간다 — 받아친 줄 알았던 사람에게 거짓 반격으로 읽힌다. idle 로 두면 키가 안 먹는데 풀린 것처럼 보인다(대시 경직과 같은 이유).
    /// 그래서 세운 자세를 붙들어 "아직 패리에 묶였다" 를 말한다. 경직 중에 맞았다 돌아오면(<see cref="Animate"/> 가 시트를 처음부터 튼다)
    /// 곧장 마지막 장으로 선다 — 패리를 다시 세우는 그림이 아니다. 새 패리는 <see cref="ParryBegan"/> 이 처음부터 돌린다.
    /// </summary>
    private void HoldParry(FighterFrame frame)
    {
        if (frame.Pose != FighterPose.Parry || _parryFrames <= 0 || !HasSheet(_parry) || _sprite.Animation != _parry.Anim)
        {
            return;
        }

        int last = System.Math.Min(
            _parry.StartFrame + _parryFrames - 1, _sprite.SpriteFrames!.GetFrameCount(_parry.Anim) - 1);
        if ((frame.Stiff || _sprite.Frame >= last) && (_sprite.Frame != last || _sprite.IsPlaying()))
        {
            _sprite.Frame = last;
            _sprite.Pause();
        }
    }

    /// <summary>
    /// 가드 — 칼을 사선으로 세운 장(<c>guard_frame</c> · <c>attack2</c> f1)에 <b>멈춰 선다</b> (#96 · 설계 §6). 팩에 막는 모션이 없어 빌린
    /// 자세이고, 버티는 동안 아무것도 안 변하는 것이 가드라 흘리지 않는다 — 가드 링이 크기를 안 바꾸는 것과 같은 말이다.
    ///
    /// <para>
    /// <b>패리와 실루엣이 같다 — 움직임으로는 거의 안 갈린다.</b> 패리는 같은 시트의 f0~f3 을 흘리고 경직 동안 f3 에 서는데
    /// (<see cref="HoldParry"/>), 이 네 장은 칼과 몸이 같은 자세다: 장마다 다른 것은 날리는 붉은 스카프와 몸 전체가 옆으로 밀리는
    /// 2~4px(2.5배로 화면 5~10px)뿐이다(#96 에서 시트를 쟀다 — 칼의 흰 픽셀은 네 장 모두 같은 덩어리이고 원본 200px 칸의
    /// x 82~102 안에서 옆으로만 옮긴다). 그래서 가드와 패리를 가르는 것은 <c>FighterView</c> 의 가드 색과 가드 링이다(패리는 칠하지
    /// 않는다). 다른 장을 골라도 안 갈린다 — 네 장의 칼이 같다.
    /// </para>
    ///
    /// <para>
    /// 가드는 처음부터 f1 에 멈춰 있다. 그래서 드는 순간 곧장 그 장이다: <see cref="Animate"/> 가 시트를 f0 부터 틀어도 같은 프레임에 여기서 세운다.
    /// 패리 경직에서 ↓ 를 쥔 채 풀리면 이름이 같아(<c>attack2</c>) <see cref="Animate"/> 가 안 트는데, 그때도 f3 에서 f1 로 곧장 옮겨 선다.
    /// 놓으면 자세가 Idle 이라 <see cref="Animate"/> 가 <c>idle</c> 을 튼다. 가드 중에 누른 패리는 <see cref="ParryBegan"/> 이 f0 부터 돌린다.
    /// </para>
    ///
    /// <para>
    /// 시트가 가드의 것일 때만 선다 — 맞은 자세 · 탈진(<c>hit</c>)이 이기고, 팩에 그 시트가 없으면 <see cref="GuardAnim"/> 이 <c>idle</c> 로
    /// 물러서 여기는 아무것도 안 한다. 장이 모자라면 있는 마지막 장이다 — 데이터가 팩에 있는 장을 가리키는지는 <c>FighterDataTests</c> 가 본다.
    /// </para>
    /// </summary>
    private void HoldGuard(FighterFrame frame)
    {
        if (frame.Pose != FighterPose.Guard || !HasAnimation(_guard.Anim) || _sprite.Animation != _guard.Anim)
        {
            return;
        }

        int still = System.Math.Clamp(_guard.Frame, 0, _sprite.SpriteFrames!.GetFrameCount(_guard.Anim) - 1);
        if (_sprite.Frame != still || _sprite.IsPlaying())
        {
            _sprite.Frame = still;
            _sprite.Pause();
        }
    }

    /// <summary>
    /// 시트를 <paramref name="frame"/> 장에 세우고 거기서부터 흘린다. 시트가 없거나 장이 모자라면
    /// 있는 마지막 장이다 — 없는 이름으로 <c>Play</c> 하면 엔진이 ERROR 를 찍는다(<see cref="Animate"/> 의 주석).
    /// </summary>
    private void ShowBlade(int frame)
    {
        SwingSheet sheet = Sheet;
        if (!HasSheet(sheet))
        {
            return;
        }

        bool fresh = _sprite.Animation != sheet.Anim;
        _sprite.Play(sheet.Anim, SpeedFor(sheet));
        _sprite.SetFrameAndProgress(
            System.Math.Min(frame, _sprite.SpriteFrames!.GetFrameCount(sheet.Anim) - 1), 0.0f);
        if (fresh)
        {
            AlignToGround(sheet.Anim);
        }
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
