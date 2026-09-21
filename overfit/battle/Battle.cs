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
    private const double _dt = BattleSim.Dt;

    private BattleSim _sim = null!;
    private FighterView _fighterView = null!;
    private BossView _bossView = null!;
    private BattleHud _hud = null!;

    // 최대 체력을 리터럴로 들지 않는다 — 시뮬레이션을 세운 바로 그 설정에서 읽는다.
    // 수치는 데이터(fighters.json · 여기 BossConfig)에 있고, 뷰는 그것을 베끼지 않는다.
    private FighterConfig _fighterConfig = null!;
    private BossConfig _bossConfig = null!;

    private double _accumulated;
    private bool _over;

    public override void _Ready()
    {
        _fighterView = GetNode<FighterView>("%FighterView");
        _bossView = GetNode<BossView>("%BossView");
        _hud = GetNode<BattleHud>("%Hud");

        Dictionary<string, FighterConfig> fighters = Load<FighterConfig>("res://data/fighters.json");
        Dictionary<string, PatternDef> patterns = Load<PatternDef>("res://data/patterns.json");
        var ids = new List<string>(patterns.Keys);

        _fighterConfig = fighters["중검"];
        _bossConfig = new BossConfig
        {
            MaxHealth = 200,
            MoveSpeed = 160,
            HalfWidth = 120,
            PatternGap = 0.8,
            Sprite = "boss_test",
        };

        _sim = new BattleSim(new BattleSetup
        {
            Arena = new Arena(1920),
            Fighter = _fighterConfig,
            Boss = _bossConfig,
            PatternIds = ids.GetRange(0, System.Math.Min(2, ids.Count)),
            Patterns = patterns,
            Seed = 51,
            MaxTicks = 60 * 180,
        });

        _fighterView.Load(_fighterConfig.Sprite);
        _bossView.Load("boss_grym");
        Log.Info("scene", "battle ready");
    }

    public override void _Process(double delta)
    {
        if (_over)
        {
            return;
        }

        // 고정 틱으로만 민다. 프레임 시간을 그대로 넣으면 기계마다 다른 판이 된다.
        _accumulated += delta;
        while (_accumulated >= _dt)
        {
            _accumulated -= _dt;
            BattleOutcome? outcome = _sim.Tick(Read());
            if (outcome is { } done)
            {
                _over = true;
                Log.Info("scene", $"battle over outcome={done} ticks={_sim.Ticks}");
                break;
            }
        }

        RenderFrame();
    }

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

        // 넷은 엣지다 — 이번 프레임에 "눌렸나"(IsActionJustPressed) 를 본다. IsKeyPressed(레벨)로
        // 읽으면 누르고 있는 동안 매 틱 발동해 InputFrame 의 계약(엣지)이 깨진다.
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
