using System;
using System.Threading.Tasks;
using Godot;
using Overfit.Core;

namespace Overfit.Battle.Debug;

/// <summary>
/// 창을 띄운 판을 <b>손으로</b> 모는 도구 — 새 판을 세우고, 액션을 누르고, 프레임과 상태를 기다린다. 스크린샷 대본
/// (<see cref="ShotRunner"/>)이 쓴다.
///
/// <para>
/// <see cref="ShotRunner"/> 에서 떼어 냈다 (#78). 그 파일은 주석 빼고 376줄이었고, 이 PR 이 2단계 장면 넷 · 다시 찍는 선딜 · 판정 보기
/// 두 장을 얹는다(CLAUDE.md §7). 대본(어느 장면을 언제 찍나)과 손(누르기 · 기다리기 · 판 세우기)은 따로 바뀐다 — 새 장면은 대본만 늘리고,
/// 손은 GIF 러너(#78 · 설계 §6.2)도 같이 쓴다.
/// </para>
///
/// <para>
/// 누르기는 <b>엔진의 입력 큐</b>로 넣는다(<see cref="Input.ParseInputEvent"/> · <see cref="Input.ActionPress"/>) — <c>Battle</c> 은
/// 사람이 누른 것과 구별할 수 없다. 합성 경로를 따로 만들지 않는 이유다.
/// </para>
/// </summary>
public sealed class SceneDriver
{
    private readonly Node _host;

    /// <summary>로그 태그 — 부르는 쪽의 이름(<c>shots</c>)이다. 기다림이 넘치거나 판이 안 섰을 때 그 이름으로 남긴다.</summary>
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
    /// 그 단계의 새 판을 세우고 씬이 설 때까지 기다린다. 판을 못 찾으면 <c>[W]</c> 를 남기고 null 이다 — 스크린샷이 못 찍힌 것은
    /// 게임의 규칙 위반이 아니다(<c>shots</c> 는 PNG 개수를 세어 0장이면 실패시킨다).
    ///
    /// <para>
    /// 한 자리다 (#78 · #59 의 3/6 넘김). 전에는 대본의 네 곳이 같은 네 줄(단계 · 씬 전환 · 네 프레임 · 씬 읽기)을 따로 들고 있었고, 그중
    /// 둘은 단계를 안 적어 앞 판의 단계를 이어 썼다.
    /// </para>
    /// </summary>
    public async Task<Overfit.Battle.Battle?> NewBattle(int stage)
    {
        Game.Instance.SetStage(stage);
        Game.Instance.GoTo(Game.Scene.Battle);
        await Frames(4);
        var battle = _host.GetTree().CurrentScene as Overfit.Battle.Battle;
        if (battle is null)
        {
            Log.Warn(_tag, "battle_scene_missing");
        }

        return battle;
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
    /// 진짜 이유(그 상태가 안 왔다)는 로그에 안 남는다.
    /// </summary>
    public async Task Until(Func<bool> ready, double timeout)
    {
        ArgumentNullException.ThrowIfNull(ready);
        double waited = 0;
        while (!ready() && waited < timeout)
        {
            await _host.ToSignal(_host.GetTree(), SceneTree.SignalName.PhysicsFrame);
            waited += 1.0 / Engine.PhysicsTicksPerSecond;
        }

        if (!ready())
        {
            Log.Warn(_tag, $"timeout waited={waited:0.0}s — 그 순간이 안 와서 그냥 찍는다");
        }
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
