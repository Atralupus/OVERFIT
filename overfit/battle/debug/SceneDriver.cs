using System;
using System.Threading.Tasks;
using Godot;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Battle.Debug;

/// <summary>
/// 창을 띄운 판을 <b>손으로</b> 모는 도구 — 새 판을 세우고, 액션을 누르고, 프레임과 상태를 기다린다. 스크린샷 대본
/// (<see cref="ShotRunner"/>)과 GIF 러너(<see cref="GifRunner"/> · 설계 §6.2)가 쓴다.
///
/// <para>
/// #78 에서 <see cref="ShotRunner"/> 로부터 떼어 냈다. 그 파일은 주석 빼고 376줄이었고, #78 이 2단계 장면 넷 · 다시 찍는 선딜 · 판정 보기 두 장을
/// 더했다(CLAUDE.md §7). 대본(어느 장면을 언제 찍나)과 손(누르기 · 기다리기 · 판 세우기)은 따로 바뀐다 — 새 장면은 대본만 늘린다.
/// 누르기가 왜 엔진의 입력 큐를 타는지는 <see cref="Tap"/> 에 있다.
/// </para>
///
/// <para>
/// <b>무게는 부르는 쪽이 정한다.</b> 판이 안 섰거나 기다린 순간이 안 왔을 때 여기는 null · 거짓을 돌려줄 뿐 그것이 경고인지 규칙 위반인지
/// 말하지 않는다 — 같은 사건이 스크린샷에는 <c>[W]</c>(사진 한 장이 못 찍혔다)이고 GIF 에는 <c>[E]</c>(GIF 가 안 나왔다 — 도구의 실패)다.
/// 전에는 여기서 <c>[W] battle_scene_missing</c> 을 남기고 GIF 러너가 같은 사건에 <c>[E] battle_missing</c> 을 또 남겨 한 사건이 두 무게였다(#96).
/// 기다림의 상한(<see cref="Until"/>)만은 여기서 <c>[W]</c> 를 남긴다 — 그것은 어느 대본에서든 "그 순간이 안 와서 그냥 간다" 다.
/// </para>
/// </summary>
public sealed class SceneDriver
{
    /// <summary><see cref="Moves"/> 의 쉬기(초) — 옛 간격 그대로다. 대본이 쓰는 수라 여기 한 곳에 둔다.</summary>
    private const double _rest = 0.8;

    private readonly Node _host;

    /// <summary>로그 태그 — 부르는 쪽의 이름(<c>shots</c> · <c>gif</c>)이다. 기다림이 넘쳤을 때 그 이름으로 남긴다.</summary>
    private readonly string _tag;

    /// <param name="host">트리에 붙은 노드 — 신호와 타이머를 여기서 받는다.</param>
    /// <param name="tag">로그 태그.</param>
    public SceneDriver(Node host, string tag)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _tag = tag;
    }

    /// <summary>
    /// 새 판을 세우고 씬이 설 때까지 기다린다. 판을 못 찾으면 null 이다 — 로그는 부르는 쪽이 제 무게로 남긴다(클래스 주석).
    ///
    /// <para>
    /// 한 자리다 (#78 · #59 의 3/6 넘김). 전에는 대본의 네 곳이 같은 네 줄(단계 · 씬 전환 · 네 프레임 · 씬 읽기)을 따로 들고 있었고, 그중
    /// 둘은 단계를 안 적어 앞 판의 단계를 이어 썼다. 보스전이 하나가 되어(설계 2026-09-29 조각1 §1) 단계는 걷혔다.
    /// </para>
    ///
    /// <para>
    /// <paramref name="script"/> 를 주면 그 판 하나를 대본(계획의 목록 · 설계 2026-09-29 조각1 §4.2)으로 세운다 (#78 · 설계 §4.4 「대본이 전투에
    /// 닿는 길」) — <c>Game</c> 의 다음 전투 한 칸에 넣고 전투로 가면 <c>Battle</c> 이 가져간다. 무엇이 올지 알아야 "그 동작이 왔을 때" 를 찍는다.
    /// 동작만 적을 때는 <see cref="Moves"/> 로 짓는다. <c>params</c> 인 까닭: 부르는 자리가 배열을 새로 적으면 분석기(CA1861 · 상수 배열 인수)가
    /// 그 줄마다 경고한다.
    /// </para>
    /// </summary>
    public async Task<Overfit.Battle.Battle?> NewBattle(params ScriptPlan[] script)
    {
        if (script.Length > 0)
        {
            Game.Instance.SetNextScript(script);
        }

        Game.Instance.GoTo(Game.Scene.Battle);
        await Frames(4);
        return _host.GetTree().CurrentScene as Overfit.Battle.Battle;
    }

    /// <summary>
    /// 동작만 적은 대본 — 칸마다 0.8초 쉬고 끊지 않는다(설계 2026-09-29 조각1 §4.2). 옛 간격(0.8초) 그대로라 대본의 장면들이 잰 틱이 안 움직인다.
    /// 쉬기나 캔슬을 적어야 하는 대본은 <see cref="ScriptPlan"/> 을 그대로 쓴다.
    /// </summary>
    public static ScriptPlan[] Moves(params string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var plans = new ScriptPlan[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            plans[i] = new ScriptPlan(_rest, ids[i]);
        }

        return plans;
    }

    /// <summary>
    /// 액션 하나를 <b>이번 프레임에만</b> 누른다. 엔진의 입력 큐로 넣으므로
    /// <c>Battle</c> 은 사람이 누른 것과 구별할 수 없다 — 합성 경로를 따로 만들지 않는 이유다.
    /// </summary>
    public static void Tap(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
    }

    /// <summary>이동처럼 누르고 있어야 하는 액션. <c>IsActionPressed</c>(레벨)가 읽는다.</summary>
    public static void Hold(string action, bool pressed)
    {
        if (pressed)
        {
            Input.ActionPress(action);
        }
        else
        {
            Input.ActionRelease(action);
        }
    }

    public async Task Wait(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        await _host.ToSignal(_host.GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    public async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>
    /// 조건이 참이 될 때까지 프레임 단위로 기다린다. <b>상한이 있다</b> —
    /// 안 오는 상태를 영원히 기다리면 완료 표지가 안 찍혀 <c>shots</c> 가 "끝까지 못 갔다" 로 죽는데,
    /// 진짜 이유(그 상태가 안 왔다)는 로그에 안 남는다. 왔으면 참이다 — 그 순간이 없던 것을 제 이름으로 남길 대본이 쓴다.
    ///
    /// <para>
    /// <b>조건은 한 프레임에 한 번만 부른다.</b> 조건이 상태를 쥐는 자리가 있다 — 정점(<c>ShotRunner.Apex</c>)은 앞 프레임의 높이와 견주고,
    /// 조건 안에서 트리를 멈춘다(<see cref="Pause"/>). 전에는 빠져나온 뒤 한 번 더 물어(경고를 낼지) 같은 프레임에 조건이 두 번 돌았다 — 정점은
    /// 앞 높이가 방금 높이로 바뀐 채 두 번째 답을 냈고, 우연히 같은 답이라 드러나지 않았을 뿐이다(#96 · #93 리뷰 T10-M1).
    /// </para>
    /// </summary>
    public async Task<bool> Until(Func<bool> ready, double timeout)
    {
        ArgumentNullException.ThrowIfNull(ready);
        double waited = 0;
        bool done;
        while (!(done = ready()) && waited < timeout)
        {
            await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            waited += 1.0 / Engine.PhysicsTicksPerSecond;
        }

        if (!done)
        {
            Log.Warn(_tag, $"timeout waited={waited:0.0}s — 그 순간이 안 와서 그냥 간다");
        }

        return done;
    }

    /// <summary>트리를 멈춘다. 기다림의 조건 안에서 부르려고 참을 돌려준다 — 조건이 참인 그 틱에 판을 세워 찍는 자리가 쓴다.</summary>
    public bool Pause()
    {
        _host.GetTree().Paused = true;
        return true;
    }

    /// <summary>멈춘 트리를 푼다.</summary>
    public void Resume() => _host.GetTree().Paused = false;
}
