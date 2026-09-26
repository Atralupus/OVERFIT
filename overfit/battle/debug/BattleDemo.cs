using System;
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

        // 시드는 **시도 시드 그 자체**다 — 게임을 안 타므로 세션도 번호도 없다. 64비트 그대로 읽는다 (#72 · 설계 §4.4): 게임
        // 로그의 [run][I] attempt=… seed=X 를 --seed=X 로 넘기면 그 시도의 보스 순서가 되살아난다. 전에는 Double 로 읽어
        // 2^53 을 넘는 시드(시도 시드는 거의 다 그렇다)가 다른 판을 돌렸다.
        ulong seed = CmdArgs.UInt64(args, "--seed=") ?? 51;
        int stage = (int)(CmdArgs.Double(args, "--stage=") ?? 1);

        // 게임과 같은 자리에서 읽는다 — 둘이 다른 판을 세우지 않게 (BattleTables).
        BattleTables data = BattleTables.Load();

        BattleBalance battle = Balance.Data.Battle;

        // 기본 캐릭터도 데이터다. 전에는 여기 "중검" 이 리터럴로 박혀 있었다 — 게임이 쓰는 캐릭터를
        // 바꾸면 데모는 조용히 옛 캐릭터로 계속 돌았을 것이다. 보스가 정확히 그렇게 갈렸던 적이 있다.
        // --fighter= 는 남긴다: 다른 캐릭터로 돌려보는 것은 데모의 일이다.
        string fighterId = CmdArgs.Text(args, "--fighter=") ?? battle.Fighter;

        if (!data.Fighters.TryGetValue(fighterId, out FighterConfig? fighter))
        {
            Log.Error("battle-demo", $"fighter_missing id={fighterId}");
            GetTree().Quit(1);
            return;
        }

        // 보스도 데이터다. 전에는 여기서 BossConfig 를 손으로 만들었고, 그 사본이 게임 쪽과
        // 갈려 있었다 — 데모는 boss_test 를, 게임은 boss_grym 을 그렸다.
        if (!data.Bosses.TryGetValue(battle.Boss, out BossConfig? boss))
        {
            Log.Error("battle-demo", $"boss_missing id={battle.Boss}");
            GetTree().Quit(1);
            return;
        }

        // 단계 명부와 고르기는 data/stages.json 이 정하고, 게임과 같은 자리에서 세운다(StageRoster.Setup). 기록은 비어 있다 —
        // uniform 은 기록을 안 읽으므로 시드만으로 게임의 그 시도와 같은 순서가 선다.
        if (StageRoster.Setup(data.Stages, stage, seed, Array.Empty<AttemptRecord>()) is not { } setup)
        {
            GetTree().Quit(1);
            return;
        }

        Log.Info("battle-demo", $"start seed={seed} fighter={fighterId} stage={stage} patterns={setup.PatternIds.Count} picker={setup.PickerId}");

        var sim = new BattleSim(new BattleSetup
        {
            Arena = new Arena(battle.ArenaWidth),
            Fighter = fighter,
            HitShapes = data.Shapes,
            Boss = boss,
            PatternIds = setup.PatternIds,
            Patterns = data.Patterns,
            Seed = seed,
            Picker = setup.Picker,
            MaxTicks = battle.MaxTicks,
        });

        var bot = new BotPolicy(seed);
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(bot.Next(sim));
        }

        PlayerAxes axes = PlayerAxes.From(sim.Events);
        // 수단별 건수를 축 옆에 같이 찍는다. samples 만 보면 "관측 10건" 이 "대시 3건으로 낸 분산"
        // 까지 보증하는 것처럼 읽힌다. 의존도 축은 한 겹 더 얇다 — 그 수단이 가능했고 **다른
        // 수단도 가능했던** 판정만 분모라, jump_rel_n · parry_rel_n 을 따로 찍는다.
        //
        // parry_n · guard_n · (samples - 나머지)가 **셋으로 갈린다** (이슈 #53): 패리 · 가드 ·
        // 무반응. parry_rate 가 그중 첫째의 성공률이라, 이 줄 하나로 "무엇을 골랐고 얼마나
        // 정확했나" 가 읽힌다. parry_late_n(부정확 패리)은 그 단계가 없어져 같이 빠졌다.
        Log.Info("axes", $"samples={axes.Samples} dash_n={axes.DashSamples} jump_n={axes.JumpSamples}"
            + $" parry_n={axes.ParrySamples}"
            // guard_n · guard_broken_n 도 **축이 아니라 개수**다 (이슈 #47). 둘을 같이 찍는 이유는
            // "버텨냈다" 와 "버티다 무너졌다" 가 결과가 정반대이기 때문이다 — 한 칸만 보면
            // 가드가 도는지는 알아도 그것이 일하는지는 모른다.
            + $" guard_n={axes.GuardSamples} guard_broken_n={axes.GuardBrokenSamples}"
            + $" jump_rel_n={axes.JumpChoiceSamples}"
            + $" parry_rel_n={axes.ParryChoiceSamples} dash_bias={axes.DashTimingBias:0.000}"
            + $" dash_var={axes.DashTimingVar:0.000} dash_dir={axes.DashDirectionBias:0.00}"
            + $" jump_bias={axes.JumpTimingBias:0.000} jump_rel={axes.JumpReliance:0.00}"
            + $" air_impact={axes.AirborneAtImpactRatio:0.00} parry_rate={axes.ParryRate:0.00}"
            + $" parry_rel={axes.ParryReliance:0.00} greed={axes.Greed:0.00} dist={axes.DistanceBias:0}");

        Log.Marker("battle-demo", $"battle-demo=done outcome={outcome} ticks={sim.Ticks} events={sim.Events.Count}");
        GetTree().Quit();
    }
}
