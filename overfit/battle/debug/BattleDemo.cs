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
        int stage = (int)(CmdArgs.Double(args, "--stage=") ?? 1);

        Dictionary<string, FighterConfig> fighters = Load<FighterConfig>("res://data/fighters.json");
        Dictionary<string, BossConfig> bosses = Load<BossConfig>("res://data/bosses.json");
        Dictionary<string, PatternDef> patterns = Load<PatternDef>("res://data/patterns.json");
        Dictionary<string, StageDef> stages = Load<StageDef>("res://data/stages.json");

        BattleBalance battle = Balance.Data.Battle;

        // 기본 캐릭터도 데이터다. 전에는 여기 "중검" 이 리터럴로 박혀 있었다 — 게임이 쓰는 캐릭터를
        // 바꾸면 데모는 조용히 옛 캐릭터로 계속 돌았을 것이다. 보스가 정확히 그렇게 갈렸던 적이 있다.
        // --fighter= 는 남긴다: 다른 캐릭터로 돌려보는 것은 데모의 일이다.
        string fighterId = CmdArgs.Text(args, "--fighter=") ?? battle.Fighter;

        if (!fighters.TryGetValue(fighterId, out FighterConfig? fighter))
        {
            Log.Error("battle-demo", $"fighter_missing id={fighterId}");
            GetTree().Quit(1);
            return;
        }

        // 보스도 데이터다. 전에는 여기서 BossConfig 를 손으로 만들었고, 그 사본이 게임 쪽과
        // 갈려 있었다 — 데모는 boss_test 를, 게임은 boss_grym 을 그렸다.
        if (!bosses.TryGetValue(battle.Boss, out BossConfig? boss))
        {
            Log.Error("battle-demo", $"boss_missing id={battle.Boss}");
            GetTree().Quit(1);
            return;
        }

        // 단계 명부는 data/stages.json 이 정한다. patterns.json 의 키 순서에서 앞 N 개를 자르던
        // 옛 방식은 패턴을 파일 맨 위에 끼워 넣는 것만으로 같은 단계를 다른 전투로 바꿨다.
        IReadOnlyList<string> ids = StageRoster.For(stages, stage);

        Log.Info("battle-demo", $"start seed={seed} fighter={fighterId} stage={stage} patterns={ids.Count}");

        var sim = new BattleSim(new BattleSetup
        {
            Arena = new Arena(battle.ArenaWidth),
            Fighter = fighter,
            Boss = boss,
            PatternIds = ids,
            Patterns = patterns,
            Seed = seed,
            MaxTicks = battle.MaxTicks,
        });

        var bot = new BotPolicy(seed);
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(bot.Next(sim));
        }

        PlayerAxes axes = PlayerAxes.From(sim.Events);
        // 수단별 건수를 축 옆에 같이 찍는다. parry_late_n(부정확 패리)은 축이 아니라 개수다 —
        // 정확 · 부정확 · 무반응 셋이 한 줄에서 갈려 보여야 계측이 갈랐다는 말을 할 수 있다. samples 만 보면 "관측 10건" 이 "대시 3건으로 낸 분산"
        // 까지 보증하는 것처럼 읽힌다. 의존도 축은 한 겹 더 얇다 — 그 수단이 가능했고 **다른
        // 수단도 가능했던** 판정만 분모라, jump_rel_n · parry_rel_n 을 따로 찍는다.
        Log.Info("axes", $"samples={axes.Samples} dash_n={axes.DashSamples} jump_n={axes.JumpSamples}"
            + $" parry_n={axes.ParrySamples} parry_late_n={axes.ParryLateSamples}"
            // charge_n 은 "모아 둔 칼을 들고 있다 판정을 맞은" 건수다 (이슈 #40) — greed 비율만으로는
            // 휘두르다 맞은 0.5초와 모으고 선 2.4초가 한 점이라, 욕심의 깊이가 이 칸에만 있다.
            + $" charge_n={axes.ChargedGreedSamples}"
            + $" jump_rel_n={axes.JumpChoiceSamples}"
            + $" parry_rel_n={axes.ParryChoiceSamples} dash_bias={axes.DashTimingBias:0.000}"
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
