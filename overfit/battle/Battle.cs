using System.Collections.Generic;
using Godot;
using Overfit.Battle.Rules;
using Overfit.Battle.View;
using Overfit.Core;

namespace Overfit.Battle;

/// <summary>
/// 전투 씬. <b>규칙을 하나도 담지 않는다</b> — <see cref="BattleSim"/> 을 고정 틱으로 돌리고
/// 그 결과를 뷰에 넘길 뿐이다. 그래서 같은 전투가 창 없이도 똑같이 돈다.
/// </summary>
public partial class Battle : Node2D
{
    private BattleSim _sim = null!;
    private FighterView _fighterView = null!;
    private BossView _bossView = null!;
    private BattleHud _hud = null!;

    // 최대 체력을 리터럴로 들지 않는다 — 시뮬레이션을 세운 바로 그 설정에서 읽는다.
    // 수치는 데이터(fighters.json · 여기 BossConfig)에 있고, 뷰는 그것을 베끼지 않는다.
    private FighterConfig _fighterConfig = null!;
    private BossConfig _bossConfig = null!;

    private bool _over;

    public override void _Ready()
    {
        _fighterView = GetNode<FighterView>("%FighterView");
        _bossView = GetNode<BossView>("%BossView");
        _hud = GetNode<BattleHud>("%Hud");

        Dictionary<string, FighterConfig> fighters = Load<FighterConfig>("res://data/fighters.json");
        Dictionary<string, PatternDef> patterns = Load<PatternDef>("res://data/patterns.json");
        Dictionary<string, StageDef> stages = Load<StageDef>("res://data/stages.json");

        // 단계 명부는 data/stages.json 이 정한다 — patterns.json 의 키 순서를 쓰면 패턴을
        // 파일 맨 위에 끼워 넣는 것만으로 1단계가 다른 전투가 된다.
        IReadOnlyList<string> ids = StageRoster.For(stages, 1);

        _fighterConfig = fighters["중검"];
        _bossConfig = new BossConfig
        {
            MaxHealth = 200,
            MoveSpeed = 160,
            HalfWidth = 120,
            PatternGap = 0.8,
            Sprite = "boss_grym",
        };

        _sim = new BattleSim(new BattleSetup
        {
            Arena = new Arena(1920),
            Fighter = _fighterConfig,
            Boss = _bossConfig,
            PatternIds = ids,
            Patterns = patterns,
            Seed = 51,
            MaxTicks = 60 * 180,
        });

        _fighterView.Load(_fighterConfig.Sprite);
        _bossView.Load(_bossConfig.Sprite);
        Log.Info("scene", "battle ready");
    }

    /// <summary>
    /// 전투를 민다. <b><c>_Process</c> 가 아니라 여기다.</b>
    ///
    /// <para>
    /// 전에는 렌더 프레임에서 누산기로 고정 틱을 만들었는데, 입력이 <c>IsActionJustPressed</c>
    /// (렌더 프레임 하나에만 참)라 둘의 주기가 어긋났다. 144Hz 에서는 대부분의 프레임이
    /// 0틱을 돌려 엣지가 그냥 버려지고(실측 약 58% 유실), 60Hz 아래에서는 한 프레임이
    /// 두 틱을 돌며 같은 <c>Read()</c> 를 두 번 읽어 한 번 누른 것이 두 <c>InputFrame</c> 이 됐다.
    /// </para>
    ///
    /// <para>
    /// 물리 틱은 <c>project.godot</c> 이 60으로 못박고, <c>Input</c> 은 물리 콜백 안에서
    /// <b>물리 틱 기준</b>으로 엣지를 돌려준다 — 틱과 입력이 같은 시계를 타므로 누산기도
    /// 따라잡기 상한도 필요 없고, 사람이 만드는 입력 시퀀스가 봇의 것과 같은 모양이 된다.
    /// 그 동등성이 학습 데이터 계획 전체가 서 있는 자리다.
    /// </para>
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (_over)
        {
            return;
        }

        BattleOutcome? outcome = _sim.Tick(Read());
        if (outcome is { } done)
        {
            _over = true;
            Log.Info("scene", $"battle over outcome={done} ticks={_sim.Ticks}");
        }
    }

    /// <summary>그리기만 한다. 규칙은 <see cref="_PhysicsProcess"/> 가 민다.</summary>
    public override void _Process(double delta) => RenderFrame();

    /// <summary>키보드를 규칙의 입력으로. <b>봇과 같은 구조체를 만든다.</b></summary>
    private static InputFrame Read()
    {
        sbyte move = 0;
        if (Input.IsKeyPressed(Key.Right) || Input.IsKeyPressed(Key.D))
        {
            move = 1;
        }
        else if (Input.IsKeyPressed(Key.Left) || Input.IsKeyPressed(Key.A))
        {
            move = -1;
        }

        // 넷은 엣지다 — "이번 물리 틱에 눌렸나"(IsActionJustPressed) 를 본다. 물리 콜백 안에서
        // 부르므로 엣지 기준이 물리 틱이고, 틱마다 정확히 한 번만 참이다.
        // IsKeyPressed(레벨)로 읽으면 누르고 있는 동안 매 틱 발동해 InputFrame 의 계약(엣지)이 깨진다.
        return new InputFrame(
            move,
            Input.IsActionJustPressed("jump"),
            Input.IsActionJustPressed("dash"),
            Input.IsActionJustPressed("parry"),
            Input.IsActionJustPressed("attack"));
    }

    // CanvasItem 에 이미 Draw() 가 있어(가상 렌더 콜백) 같은 이름을 쓰면 CS0108(가림) 경고가 난다.
    // 그 콜백을 오버라이드하는 게 아니므로 이름을 비켜 간다.
    private void RenderFrame()
    {
        _fighterView.Show(_sim.Fighter.X, _sim.Fighter.Y, _sim.Fighter.Facing,
            _sim.Fighter.Invulnerable, _sim.Fighter.Parrying);
        _bossView.Show(_sim.Boss.X, _sim.Boss.CurrentPattern);
        _hud.Show(_sim.Fighter.Health, _fighterConfig.MaxHealth, _sim.Fighter.Stamina, _fighterConfig.MaxStamina,
            _sim.Boss.Health, _bossConfig.MaxHealth);
    }

    private static Dictionary<string, T> Load<T>(string path)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return JsonData<T>.ParseTable(file.GetAsText(), path);
    }
}
