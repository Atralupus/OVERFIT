using System;
using System.Collections.Generic;
using System.Globalization;
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
        Battle,
        Credits,
    }

    private static readonly Dictionary<Scene, string> _scenePaths = new()
    {
        [Scene.Title] = "res://title/Title.tscn",
        [Scene.Play] = "res://play/Play.tscn",
        [Scene.Battle] = "res://battle/Battle.tscn",
        [Scene.Credits] = "res://credits/Credits.tscn",
    };

    /// <summary>순회 한 걸음마다 주는 시간. 씬이 <c>_Ready</c> 를 끝내고 첫 프레임을 그릴 만큼이면 된다.</summary>
    private const double _tourStepSeconds = 0.3;

    /// <summary>
    /// 단계 점프 디버그 액션의 접두어 (이슈 #54). <c>project.godot</c> 의 <c>debug_stage_1..2</c> 이고
    /// 끝의 숫자가 곧 단계다. 타이틀의 조작 안내는 <c>debug_</c> 로 시작하는 액션을 안 싣는다(Title).
    /// </summary>
    private const string _stageJumpPrefix = "debug_stage_";

    /// <summary>
    /// 순회가 전투에서 눌러 보는 단계 점프 — 마지막 단계(2)라 1 에서 옮겨 간 것이 로그에서 갈린다. 3단계가 없어져(#72)
    /// 3 을 누르면 전투가 2단계로 잘려 서고 <c>[W]</c> 를 남기므로 smoke 가 "3단계 전투가 섰다" 를 못 본다.
    /// </summary>
    private const string _tourStageJump = "debug_stage_2";

    public static Game Instance { get; private set; } = null!;

    public Scene Current { get; private set; } = Scene.Title;

    /// <summary>
    /// 지금 도는 단계. 이기면 오르고, 타이틀로 나가면 1로 돌아간다.
    ///
    /// <para>
    /// <b>이것이 남은 유일한 진행 상태다.</b> 캐릭터 3택과 스탯 강화는 만들지 않는다(이슈 #22) —
    /// 단계 진행은 성장 루프가 아니라 보스 설계의 축이라 남긴다: 보스는 두 단계이고(#72 · 설계 §4),
    /// 2단계가 1단계에서 쓴 답을 겨냥하게 된다(5번 PR — 지금은 두 단계가 같은 명부다). 그 "나를 보고 바뀐다" 가 게임 자체다.
    /// </para>
    ///
    /// <para>
    /// 씬이 아니라 여기(Autoload)에 둔다. 전투 씬은 다시 시작할 때마다 새로 만들어지므로
    /// 씬 안에 두면 이긴 단계가 그 자리에서 사라진다.
    /// </para>
    /// </summary>
    public int Stage { get; private set; } = 1;

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
        else if (OS.IsDebugBuild() && CmdArgs.Has(args, "--shots"))
        {
            // 창이 있어야 뷰포트에 그려진 것이 있다 — 헤드리스로 돌리면 빈 이미지가 나온다.
            AddChild(new Battle.Debug.ShotRunner());
        }
        else if (OS.IsDebugBuild() && CmdArgs.Has(args, "--battle-demo"))
        {
            // Autoload 의 자식이라 씬이 바뀌어도 살아남는다. 뷰를 안 만들고 규칙만 돌린다.
            AddChild(new Battle.Debug.BattleDemo());
        }
    }

    /// <summary>
    /// 단계를 옮긴다. <b>범위를 여기서 안 자른다</b> — 몇 단계가 있는지는
    /// <c>data/stages.json</c> 이 알고, 그 파일을 읽는 것은 전투 씬이다.
    /// 여기서 상한을 박으면 데이터와 코드에 같은 숫자가 둘이 된다.
    /// </summary>
    public void SetStage(int stage)
    {
        Stage = System.Math.Max(1, stage);
        Log.Info("run", $"stage={Stage}");
    }

    /// <summary>판을 처음으로. 타이틀로 나갈 때 부른다 — 안 부르면 다음 판이 5단계에서 시작한다.</summary>
    public void ResetRun() => SetStage(1);

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
            return;
        }

        // 전투 중 1 · 2 → 그 단계를 바로 시작한다 (이슈 #54). 유저가 2단계를 보려고 한 판을
        // 이기고 올라가지 않게 하는 디버그 키다. **위의 IsDebugBuild 가드가 릴리즈 빌드를 이미 걸렀다** —
        // 그 한 줄이 이 키를 릴리즈에서 죽이는 전부라, 이 갈래를 그 가드 위로 올리지 않는다.
        if (Current == Scene.Battle && StageJump(e) is (string action, int stage))
        {
            Log.Debug("scene", $"input action={action} stage_jump from={Stage} to={stage}");
            SetStage(stage);
            GoTo(Scene.Battle);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// 눌린 것이 단계 점프 키(<c>debug_stage_N</c>)면 그 액션과 단계 번호. 아니면 null.
    ///
    /// <para>
    /// 단계 번호를 여기 표로 적지 않고 <b>액션 이름에서 읽는다</b> — 키와 단계의 짝이 <c>project.godot</c>
    /// 한 곳에만 있어야 한다. 몇 단계까지 있는지는 여기서 안 자른다: <c>data/stages.json</c> 이 알고,
    /// 없는 단계는 전투 씬이 가장 가까운 단계로 잘라 [W] 를 남긴다(<c>StageRoster.For</c>).
    /// </para>
    /// </summary>
    private static (string Action, int Stage)? StageJump(InputEvent e)
    {
        foreach (StringName name in InputMap.GetActions())
        {
            string action = name.ToString();
            if (action.StartsWith(_stageJumpPrefix, StringComparison.Ordinal)
                && int.TryParse(action.AsSpan(_stageJumpPrefix.Length), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int stage)
                && e.IsActionPressed(action))
            {
                return (action, stage);
            }
        }

        return null;
    }

    private static Scene Next(Scene scene) => scene switch
    {
        Scene.Title => Scene.Play,
        Scene.Play => Scene.Battle,
        Scene.Battle => Scene.Credits,
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

        // 크레딧도 순회에 넣는다. 데이터(data/credits.json)를 읽어 스스로를 짓는 화면이라
        // 파일이 깨지면 [credits][E] 가 뜨고, 그 한 줄이 이 순회를 실패로 만든다 — 공짜 불변식이다.
        foreach (Scene scene in new[] { Scene.Play, Scene.Battle, Scene.Credits, Scene.Title })
        {
            Log.Trace("scene", $"tour step={scene} frame={Engine.GetProcessFrames()}");
            GoTo(scene);
            await ToSignal(GetTree().CreateTimer(_tourStepSeconds), SceneTreeTimer.SignalName.Timeout);

            // 전투에서는 단계 점프 키를 한 번 눌러 본다 (이슈 #54). 키가 InputMap 에 있는지 · 입력이
            // _UnhandledInput 까지 오는지 · 단계가 정말 바뀌어 전투가 다시 서는지를 창 없이 본다 —
            // tools/build.sh smoke 가 그 로그 두 줄을 찾는다. 엔진의 입력 큐로 넣으므로 사람이 누른 것과 같은 길이다.
            if (scene == Scene.Battle)
            {
                Input.ParseInputEvent(new InputEventAction { Action = _tourStageJump, Pressed = true });
                Input.ParseInputEvent(new InputEventAction { Action = _tourStageJump, Pressed = false });
                await ToSignal(GetTree().CreateTimer(_tourStepSeconds), SceneTreeTimer.SignalName.Timeout);
            }
        }

        Log.Marker("tour", "tour=done");
        GetTree().Quit();
    }
}
