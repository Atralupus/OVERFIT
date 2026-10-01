using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Overfit.Battle.Rules;

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
    /// 순회가 첫 전투에만 넣는 대본 (#96 · 설계 §4.4 「대본이 전투에 닿는 길」) — 계획 한 칸이면 된다(쉬기 0.8 · 끊지 않는다 · 설계 2026-09-29
    /// 조각1 §4.2). smoke 가 그 전투의 <c>picker=script</c> 와, 다시 선 둘째 전투의 <c>picker=uniform</c> 을 같이 본다. 둘째 줄이
    /// <see cref="TakeScript"/> 가 가져가며 비운다는 증명이다 — 칸이 남으면 다음 전투도 <c>script</c> 로 선다. 규칙 테스트로는 못 본다: 칸은
    /// Autoload(Godot 쪽)에 있다.
    /// </summary>
    private static readonly ScriptPlan[] _tourScript = { new(0.8, "3연격") };

    public static Game Instance { get; private set; } = null!;

    public Scene Current { get; private set; } = Scene.Title;

    /// <summary>
    /// 세션의 시도 번호와 기록 (#72 · 설계 §4.4). 여기(Autoload)에 둔다 — 전투 씬은 설 때마다 새로 만들어진다.
    /// 로직은 <see cref="RunHistory"/>(규칙 층 · 테스트 안)에 있다.
    /// </summary>
    public RunHistory History { get; private set; } = null!;

    /// <summary>
    /// <b>다음 전투 하나에만</b> 쓰는 대본 — 계획의 목록 (#78 · 설계 2026-09-29 조각1 §4.2 · 설계 §4.4 「대본이 전투에 닿는 길」). GIF 러너와 스크린샷 대본이(smoke
    /// 순회도 첫 전투에 · <see cref="_tourScript"/>) 채우고 전투로 가면, <c>Battle</c> 이 가져가며(<see cref="TakeScript"/>) 그 전투의 고르기를
    /// 단계의 <c>picker</c> 대신 <c>script</c> 로 세운다. 기록처럼 여기(Autoload)에 두는 이유는 같다 — 전투 씬은 설 때마다 새로 만들어진다.
    /// <c>stages.json</c> 은 안 건드린다: 데이터에 <c>picker: script</c> 를 적는 길을 안 만든다.
    /// </summary>
    private IReadOnlyList<ScriptPlan>? _nextScript;

    /// <summary>다음 전투의 보스 시작 체력 (설계 2026-10-01 조각1 §2.5) — 대본과 같이, 또는 홀로(<see cref="SetNextStart"/> · 조각8 §3) 채우고 전투가 가져가며 비운다. 없으면 최대 체력.</summary>
    private int? _nextBossStartHealth;

    /// <summary>다음 대본 전투의 칸 대본 (설계 2026-10-01 조각3 §4) — 있으면 그 전투는 대본 조종기(<c>ScriptActions</c>)로 선다. 대본과 같이 비운다.</summary>
    private IReadOnlyList<string>? _nextActions;

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

        string[] args = OS.GetCmdlineUserArgs();
        History = new RunHistory(SessionSeed(args));
        Log.Info("run", $"session_seed={History.SessionSeed}");

        // 검증용. tools/build.sh smoke 가 `-- --tour` 로 띄운다.
        // 규칙 자체 테스트는 여기 없다 — Godot 을 안 띄우는 `tools/build.sh test` 가 전부 돌린다.
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
        else if (OS.IsDebugBuild() && CmdArgs.Text(args, "--gif=") is { } gif)
        {
            // README 의 패턴별 GIF (#78 · 설계 §6.2) — tools/build.sh gifs 가 창과 Movie Maker 로 띄운다. 스크린샷처럼 창이 있어야 그린 것이 있다.
            AddChild(new Battle.Debug.GifRunner { ScriptId = gif });
        }
        else if (OS.IsDebugBuild() && CmdArgs.Text(args, "--duel=") is { } fighterNet)
        {
            // 학습한 파이터와 보스의 한 판 영상 (이슈 #167) — tools/build.sh duel 이 창과 Movie Maker 로 띄운다.
            AddChild(new Battle.Debug.DuelRunner { FighterNet = fighterNet, Seed = CmdArgs.UInt64(args, "--duel-seed=") ?? 1 });
        }
    }

    /// <summary>다음 전투 하나를 대본으로 세운다 (<see cref="_nextScript"/>). 그 전투가 가져가면 비고, 그 뒤의 전투는 단계의 고르기로 돌아간다.</summary>
    /// <param name="script">대본.</param>
    /// <param name="bossStartHealth">보스의 시작 체력 — GIF · 스크린샷이 전환을 찍으려고(설계 2026-10-01 조각1 §2.5). 없으면 최대 체력.</param>
    /// <param name="actions">칸 대본 — 움직임을 찍는 GIF 가 쓴다(설계 2026-10-01 조각3 §4). 없으면 계획 대본의 규칙 조종기다.</param>
    public void SetNextScript(IReadOnlyList<ScriptPlan> script, int? bossStartHealth = null, IReadOnlyList<string>? actions = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        _nextScript = script;
        _nextBossStartHealth = bossStartHealth;
        _nextActions = actions;
        Log.Info("run", $"next_script={ScriptText(script)}" + (bossStartHealth is { } h ? $" boss_start_health={h}" : ""));
    }

    /// <summary>
    /// 다음 전투 하나의 보스 시작 체력만 정한다 — 대본 없이 단계의 조종기(망)로 선다(설계 2026-10-01 조각8 §3). 페이즈마다의 망 보스를 찍는 GIF 가 쓴다:
    /// 형태는 시작 체력이 고른다. 그 전투가 가져가면 빈다.
    /// </summary>
    public void SetNextStart(int bossStartHealth)
    {
        _nextScript = null;
        _nextActions = null;
        _nextBossStartHealth = bossStartHealth;
        Log.Info("run", $"next_start boss_start_health={bossStartHealth}");
    }

    /// <summary>다음 전투의 보스 시작 체력을 가져가며 비운다 — <see cref="TakeScript"/> 와 같이 <c>Battle</c> 만 부른다.</summary>
    public int? TakeBossStartHealth()
    {
        int? health = _nextBossStartHealth;
        _nextBossStartHealth = null;
        return health;
    }

    /// <summary>칸 대본을 가져가며 비운다 — <see cref="TakeScript"/> 와 같이 <c>Battle</c> 만 부른다.</summary>
    public IReadOnlyList<string>? TakeActions()
    {
        IReadOnlyList<string>? actions = _nextActions;
        _nextActions = null;
        return actions;
    }

    /// <summary>
    /// 대본의 로그 꼴 — 칸마다 <c>쉬기초:동작</c>, 끊으면 <c>&gt;지점:잇는 동작</c> 을 붙인다(예: <c>0.8:3연격&gt;0:돌진</c>). 사람이 grep 한다.
    /// </summary>
    public static string ScriptText(IReadOnlyList<ScriptPlan> script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var cells = new string[script.Count];
        for (int i = 0; i < cells.Length; i++)
        {
            ScriptPlan p = script[i];
            cells[i] = p.Next is null
                ? $"{p.RestSeconds:0.##}:{p.Move}"
                : $"{p.RestSeconds:0.##}:{p.Move}>{p.CancelPoint}:{p.Next}";
        }

        return string.Join(',', cells);
    }

    /// <summary>
    /// 대본 칸을 가져가며 비운다 — 전투를 세우는 <c>Battle</c> 만 부른다. 비었으면 null 이다. 가져가며 비우는 이유: 칸이 남으면 재시도의
    /// 전투까지 대본으로 서서 "재시도마다 다른 보스"(설계 §4.4)가 조용히 꺼진다.
    /// </summary>
    public IReadOnlyList<ScriptPlan>? TakeScript()
    {
        IReadOnlyList<ScriptPlan>? script = _nextScript;
        _nextScript = null;
        return script;
    }

    /// <summary>
    /// 판을 처음으로 — 시도 기록을 비우고 런이 오른다(설계 §4.4). 타이틀로 나갈 때와 클리어 뒤 [처음부터] 에 부른다. 세션 시드와 시도 번호는
    /// 안 돌아간다. 보스전이 하나라(설계 2026-09-29 조각1 §1) 되돌릴 단계는 없다.
    /// </summary>
    public void ResetRun()
    {
        History.Clear();
        Log.Debug("run", $"history_cleared attempts={History.Attempts}");
    }

    /// <summary>
    /// 세션 시드 (#72 · 설계 §4.4). <c>--session-seed=N</c> 이 있으면 그것이고(스모크 · 스크린샷은 <c>tools/build.sh</c> 가
    /// 51 을 넘긴다 — 실행마다 같은 판을 찍으려고), 없으면 벽시계의 마이크로초를 <see cref="Det.Mix64"/> 로 섞는다.
    /// <b>벽시계는 규칙 층에서만 금지다</b>(CLAUDE.md §4) — 여기는 Godot 쪽이고, 규칙은 뽑힌 수를 받기만 한다.
    /// </summary>
    private static ulong SessionSeed(string[] args) =>
        CmdArgs.UInt64(args, "--session-seed=") ?? Det.Mix64((ulong)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMicrosecond));

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
        // 전투는 두 번 선다 — 첫째만 대본(_tourScript)이다. smoke 가 둘째 전투가 새 시도 · 새 시드 · 단계의 고르기로 섰는지를 본다
        // (대본 칸이 가져가며 비워졌다는 증명 · 설계 §4.4). 전에는 단계 점프 키(debug_stage_2)가 둘째 전투를 세웠다 — 보스전이 하나가 되며 걷었다.
        bool scripted = false;
        foreach (Scene scene in new[] { Scene.Play, Scene.Battle, Scene.Battle, Scene.Credits, Scene.Title })
        {
            Log.Trace("scene", $"tour step={scene} frame={Engine.GetProcessFrames()}");
            if (scene == Scene.Battle && !scripted)
            {
                SetNextScript(_tourScript);
                scripted = true;
            }

            GoTo(scene);
            await ToSignal(GetTree().CreateTimer(_tourStepSeconds), SceneTreeTimer.SignalName.Timeout);
        }

        Log.Marker("tour", "tour=done");
        GetTree().Quit();
    }
}
