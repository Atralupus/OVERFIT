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

        if (!fighters.TryGetValue(fighterId, out FighterConfig? fighter))
        {
            Log.Error("battle-demo", $"fighter_missing id={fighterId}");
            GetTree().Quit(1);
            return;
        }

        // 단계가 쓰는 패턴 수: 2 · 3 · 5 · 7 · 10. 프로토타입은 패턴이 여섯이라 3단계까지 돈다.
        int[] counts = { 2, 3, 5, 7, 10 };
        int want = counts[System.Math.Clamp(stage - 1, 0, counts.Length - 1)];
        var ids = new List<string>();
        foreach (string id in patterns.Keys)
        {
            if (ids.Count < want)
            {
                ids.Add(id);
            }
        }

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
        Log.Info("axes", $"samples={axes.Samples} dash_bias={axes.DashTimingBias:0.000}"
            + $" dash_var={axes.DashTimingVar:0.000} dash_dir={axes.DashDirectionBias:0.00}"
            + $" jump_bias={axes.JumpTimingBias:0.000} jump_rel={axes.JumpReliance:0.00}"
            + $" air={axes.AirTimeRatio:0.00} parry_rate={axes.ParryRate:0.00}"
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
