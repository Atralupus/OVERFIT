using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Overfit.Core;

/// <summary>
/// 씬 라우터. <c>project.godot</c> 의 <c>[autoload]</c> 로 등록된다.
/// 등록 순서는 Balance → Game. Game 이 마지막이라 <see cref="_Ready"/> 에서 앞의 것들을 쓸 수 있다.
///
/// <para>
/// 씬 전환은 <b>전부 여기를 지난다.</b> 씬끼리 서로를 직접 <c>ChangeSceneToFile</c> 하지 않는다 —
/// 그러면 "지금 어디인가"를 아는 곳이 없어지고, 전환 로그도 흩어진다.
/// </para>
/// </summary>
public partial class Game : Node
{
    public enum Scene
    {
        Title,
        Play,
    }

    private static readonly Dictionary<Scene, string> _scenePaths = new()
    {
        [Scene.Title] = "res://title/Title.tscn",
        [Scene.Play] = "res://play/Play.tscn",
    };

    /// <summary>순회 한 걸음마다 주는 시간. 씬이 <c>_Ready</c> 를 끝내고 첫 프레임을 그릴 만큼이면 된다.</summary>
    private const double _tourStepSeconds = 0.3;

    public static Game Instance { get; private set; } = null!;

    public Scene Current { get; private set; } = Scene.Title;

    public override void _Ready()
    {
        // 로그 출력을 Godot 에 꽂는다. 모듈 초기화(LogSink.AutoInstall)가 이미 꽂았으므로 여기선 멱등이다 —
        // 부팅 경로에서 "로그는 여기서 살아난다"가 보이라고 남긴 명시적 호출이다.
        LogSink.Install();
        Instance = this;

        Log.Info("boot", "OVERFIT 부팅");
        Log.Info("boot", $"version={Setting("application/config/version")} debug={OS.IsDebugBuild()}");
        Log.Info("boot", $"viewport={Setting("display/window/size/viewport_width")}x{Setting("display/window/size/viewport_height")}"
            + $" renderer={Setting("rendering/renderer/rendering_method")}");
        Log.Info("scene", $"start={Current} log_level={Log.Level}");
        Log.Debug("boot", $"user_args=[{string.Join(" ", OS.GetCmdlineUserArgs())}] user_dir={OS.GetUserDataDir()}");

        // 검증용. tools/build.sh smoke 가 `-- --tour` 로 띄운다.
        // 규칙 자체 테스트는 여기 없다 — Godot 을 안 띄우는 `tools/build.sh test` 가 전부 돌린다.
        string[] args = OS.GetCmdlineUserArgs();
        if (OS.IsDebugBuild() && CmdArgs.Has(args, "--tour"))
        {
            _ = TourAsync();
        }
        else if (OS.IsDebugBuild() && CmdArgs.Has(args, "--battle-demo"))
        {
            // Autoload 의 자식이라 씬이 바뀌어도 살아남는다. 뷰를 안 만들고 규칙만 돌린다.
            AddChild(new Battle.Debug.BattleDemo());
        }
    }

    public void GoTo(Scene scene)
    {
        Log.Debug("scene", $"transition from={Current} to={scene} path={_scenePaths[scene]}");
        Current = scene;
        Log.Info("scene", $"goto={scene}");
        Error err = GetTree().ChangeSceneToFile(_scenePaths[scene]);
        if (err != Error.Ok)
        {
            Log.Error("scene", $"change_failed to={scene} err={err}");
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!OS.IsDebugBuild())
        {
            return;
        }

        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F9 })
        {
            Log.Debug("scene", "input key=F9 action=cycle");
            GoTo(Next(Current));
            GetViewport().SetInputAsHandled();
        }
    }

    private static Scene Next(Scene scene) => scene switch
    {
        Scene.Title => Scene.Play,
        _ => Scene.Title,
    };

    private static Variant Setting(string name) => ProjectSettings.GetSetting(name);

    /// <summary>
    /// 씬을 차례로 돌고 종료한다. 라우팅이 살아 있는지 · 씬이 뜨는지 · 스크립트가 붙는지를 창 없이 본다.
    /// 마지막의 <c>[tour][M] tour=done</c> 이 헤드리스 판정의 완료 표지다 (tools/build.sh 의 judge_headless).
    /// </summary>
    private async Task TourAsync()
    {
        await ToSignal(GetTree().CreateTimer(_tourStepSeconds), SceneTreeTimer.SignalName.Timeout);

        foreach (Scene scene in new[] { Scene.Play, Scene.Title })
        {
            Log.Trace("scene", $"tour step={scene} frame={Engine.GetProcessFrames()}");
            GoTo(scene);
            await ToSignal(GetTree().CreateTimer(_tourStepSeconds), SceneTreeTimer.SignalName.Timeout);
        }

        Log.Marker("tour", "tour=done");
        GetTree().Quit();
    }
}
