using System;
using System.Threading.Tasks;
using Godot;
using Overfit.Core;

namespace Overfit.Battle.Debug;

/// <summary>
/// 창을 띄운 채 씬을 돌며 스크린샷을 찍는다. <c>tools/build.sh shots</c> 가 띄운다.
///
/// <para>
/// 전에는 벽시계로만 기다렸다 — 정해진 초에 셔터를 눌렀다. 그러면 <b>무엇이 찍힐지 아무도 모른다</b>:
/// 패턴 주기(간격 0.8초 + 패턴 0.9~1.35초)와 어긋나 실행할 때마다 다른 순간이 나오고,
/// 플레이어는 아무것도 안 하므로 대시·패리·공격은 한 번도 안 찍힌다.
/// </para>
///
/// <para>
/// 그래서 둘을 한다. ① <b>입력을 넣는다</b> — 사람이 누르는 것과 같은 액션을 합성해
/// <c>Battle</c> 의 <c>Read()</c> 가 그대로 읽게 한다. ② <b>상태를 보고 셔터를 누른다</b> —
/// 보스가 선딜에 들어갔을 때, 체력이 줄었을 때, 결과가 떴을 때.
/// 그래야 스크린샷이 "이 피드백이 보이는가" 를 실제로 증명한다.
/// </para>
/// </summary>
public partial class ShotRunner : Node
{
    /// <summary>한 판이 끝나기를 기다리는 상한(초). 넘으면 결과 화면 없이 넘어간다.</summary>
    private const double _battleTimeout = 90.0;

    /// <summary>상태를 기다릴 때의 기본 상한(초).</summary>
    private const double _pollTimeout = 8.0;

    private Overfit.Battle.Battle? _battle;

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        Log.Info("shots", "start");

        await Wait(0.6);
        await Screenshot.CaptureAsync(this, "title");

        // 크레딧도 찍는다. 이 화면은 data/credits.json 을 읽어 **자기가 짓는** 화면이라
        // 항목이 늘면 줄이 늘고 링크가 길면 잘린다 — 그 종류의 실패는 로그에 안 남는다.
        // 이 저장소는 화면의 실패를 여러 번 스크린샷에서 처음 봤다.
        Game.Instance.GoTo(Game.Scene.Credits);
        await Frames(6);
        await Screenshot.CaptureAsync(this, "credits");

        Game.Instance.GoTo(Game.Scene.Battle);
        await Frames(4);
        _battle = GetTree().CurrentScene as Overfit.Battle.Battle;
        if (_battle is null)
        {
            // [E] 가 아니라 [W] 다 — 스크린샷이 못 찍힌 것은 게임의 규칙 위반이 아니다.
            // 대신 shots 는 PNG 개수를 세어 0장이면 실패시킨다.
            Log.Warn("shots", "battle_scene_missing");
        }

        await Shoot("battle-1-approach", 1.0);

        // 보스 쪽으로 붙는다. 붙어 있어야 판정에 걸리고, 그래야 피격·패리가 찍힌다.
        Hold("move_right", true);
        await Wait(1.1);
        Hold("move_right", false);

        // ── 대시: 무적 창 한가운데를 잡는다 ────────────────────────────────
        // 무적은 0.14초(≈8프레임)이고 대시는 0.18초다. 4프레임째면 잔상이 서너 장 깔린 채
        // 아직 무적이다 — 정확히 그 차이를 보여주려고 이 순간을 고른다.
        Tap("dash");
        await Frames(4);
        await Screenshot.CaptureAsync(this, "battle-2-dash");

        // 같은 대시의 9프레임째 — 무적은 끝났고(8.4프레임) 대시는 아직 돈다(10.8프레임까지).
        // **이 두 장이 나란히 있어야** 그 0.04초가 증명된다: 잔상이 멈추고 몸이 어두워진다.
        // 설계상 여기서 맞는 것이 맞는데, 플레이어에게는 지금까지 보이지 않던 구간이다.
        await Frames(5);
        await Screenshot.CaptureAsync(this, "battle-3-dash-tail");

        await Wait(0.5);

        // ── 패리: 창이 열린 직후 ──────────────────────────────────────────
        Tap("parry");
        await Frames(3);
        await Screenshot.CaptureAsync(this, "battle-4-parry");

        await Wait(0.5);

        // ── 공격: 판정이 서는 순간(선딜 0.09초 ≈ 6프레임) ─────────────────
        Tap("attack");
        await Frames(6);
        await Screenshot.CaptureAsync(this, "battle-5-attack");

        // ── 보스 선딜: 예고 링이 조여 드는 중 ─────────────────────────────
        await Until(() => _battle?.BossWindingUp == true, _pollTimeout);
        await Frames(12);
        await Screenshot.CaptureAsync(this, "battle-6-windup");

        // ── 패리 불가 선딜: 크림슨 ────────────────────────────────────────
        // 평소 예고(호박색)와 **같은 화면에서 견줄 수 있어야** 이 연출이 일한다.
        // 두 장이 나란히 없으면 "붉은가" 만 알 수 있고 "다른가" 는 모른다.
        await Until(() => _battle is { BossWindingUp: true, BossUnparryable: true }, _pollTimeout);
        await Frames(12);
        await Screenshot.CaptureAsync(this, "battle-6b-unparryable");

        // ── 피격: 체력이 줄어든 바로 다음 프레임 ──────────────────────────
        int before = _battle?.FighterHealth ?? 0;
        await Until(() => (_battle?.FighterHealth ?? 0) < before, _pollTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, "battle-7-hit");

        // ── 결과 화면: 아무것도 안 하고 맞아 죽는다 ───────────────────────
        await Until(() => _battle?.ResultVisible == true, _battleTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, "battle-8-result");

        Log.Marker("shots", "shots=done");
        GetTree().Quit();
    }

    /// <summary>
    /// 액션 하나를 <b>이번 프레임에만</b> 누른다. 엔진의 입력 큐로 넣으므로
    /// <c>Battle</c> 은 사람이 누른 것과 구별할 수 없다 — 합성 경로를 따로 만들지 않는 이유다.
    /// </summary>
    private static void Tap(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
    }

    /// <summary>이동처럼 누르고 있어야 하는 액션. <c>IsActionPressed</c>(레벨)가 읽는다.</summary>
    private static void Hold(string action, bool pressed)
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

    private async Task Shoot(string name, double after)
    {
        await Wait(after);
        await Screenshot.CaptureAsync(this, name);
    }

    private async Task Wait(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>
    /// 조건이 참이 될 때까지 프레임 단위로 기다린다. <b>상한이 있다</b> —
    /// 안 오는 상태를 영원히 기다리면 완료 표지가 안 찍혀 <c>shots</c> 가 "끝까지 못 갔다" 로 죽는데,
    /// 진짜 이유(그 상태가 안 왔다)는 로그에 안 남는다.
    /// </summary>
    private async Task Until(Func<bool> ready, double timeout)
    {
        double waited = 0;
        while (!ready() && waited < timeout)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            waited += 1.0 / Engine.PhysicsTicksPerSecond;
        }

        if (!ready())
        {
            Log.Warn("shots", $"timeout waited={waited:0.0}s — 그 순간이 안 와서 그냥 찍는다");
        }
    }
}
