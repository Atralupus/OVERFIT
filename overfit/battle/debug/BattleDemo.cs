using System.Collections.Generic;
using Godot;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Battle.Debug;

/// <summary>
/// 창 없이 전투 한 판을 끝까지 돌린다. <c>tools/build.sh demo</c> 가 이것을 띄우고
/// 완료 표지 <c>[battle-demo][M] battle-demo=done</c> 로 판정한다.
///
/// <para>
/// 뷰를 하나도 안 만든다 — 규칙 층만 돌린다. 씬이 뜨는지는 <c>smoke</c> 가 따로 본다.
/// </para>
/// </summary>
public partial class BattleDemo : Node
{
    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        ulong seed = (ulong)(CmdArgs.Double(args, "--seed=") ?? 51);
        string fighterId = CmdArgs.Text(args, "--fighter=") ?? "중검";
        int stage = (int)(CmdArgs.Double(args, "--stage=") ?? 1);

        Dictionary<string, FighterConfig> fighters = Load<FighterConfig>("res://data/fighters.json");
        Dictionary<string, PatternDef> patterns = Load<PatternDef>("res://data/patterns.json");
        Dictionary<string, StageDef> stages = Load<StageDef>("res://data/stages.json");

        if (!fighters.TryGetValue(fighterId, out FighterConfig? fighter))
        {
            Log.Error("battle-demo", $"fighter_missing id={fighterId}");
            GetTree().Quit(1);
            return;
        }

        // 단계 명부는 data/stages.json 이 정한다. patterns.json 의 키 순서에서 앞 N 개를 자르던
        // 옛 방식은 패턴을 파일 맨 위에 끼워 넣는 것만으로 같은 단계를 다른 전투로 바꿨다.
        IReadOnlyList<string> ids = StageRoster.For(stages, stage);

        Log.Info("battle-demo", $"start seed={seed} fighter={fighterId} stage={stage} patterns={ids.Count}");

        var sim = new BattleSim(new BattleSetup
        {
            Arena = new Arena(1920),
            Fighter = fighter,
            Boss = new BossConfig
            {
                MaxHealth = 200,
                MoveSpeed = 160,
                HalfWidth = 120,
                PatternGap = 0.8,
                Sprite = "boss_test",
            },
            PatternIds = ids,
            Patterns = patterns,
            Seed = seed,
            MaxTicks = 60 * 180,
        });

        var bot = new BotPolicy(seed);
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(bot.Next(sim));
        }

        PlayerAxes axes = PlayerAxes.From(sim.Events);
        // 수단별 건수를 축 옆에 같이 찍는다. samples 만 보면 "관측 10건" 이 "대시 3건으로 낸 분산"
        // 까지 보증하는 것처럼 읽힌다.
        Log.Info("axes", $"samples={axes.Samples} dash_n={axes.DashSamples} jump_n={axes.JumpSamples}"
            + $" parry_n={axes.ParrySamples} dash_bias={axes.DashTimingBias:0.000}"
            + $" dash_var={axes.DashTimingVar:0.000} dash_dir={axes.DashDirectionBias:0.00}"
            + $" jump_bias={axes.JumpTimingBias:0.000} jump_rel={axes.JumpReliance:0.00}"
            + $" air_impact={axes.AirborneAtImpactRatio:0.00} parry_rate={axes.ParryRate:0.00}"
            + $" parry_rel={axes.ParryReliance:0.00} greed={axes.Greed:0.00} dist={axes.DistanceBias:0}");

        Log.Marker("battle-demo", $"battle-demo=done outcome={outcome} ticks={sim.Ticks} events={sim.Events.Count}");
        GetTree().Quit();
    }

    /// <summary>res:// 파일을 표로. Godot 과 순수 C# 의 경계라 여기서만 FileAccess 를 쓴다.</summary>
    private static Dictionary<string, T> Load<T>(string path)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return JsonData<T>.ParseTable(file.GetAsText(), path);
    }
}
