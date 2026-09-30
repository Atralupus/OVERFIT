using System;
using System.Collections.Generic;
using System.Linq;
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
///
/// <para>
/// 되살리기 (#112 · 설계 2026-09-29 조각1 §4.4) — <c>--history=&lt;파일&gt; --attempt=N</c> 이면 봇 대신 그 시도의 <b>저장한 입력</b>을 틱마다 넣어
/// 판 전체를 다시 세우고 기록과 견준다(<see cref="Replay"/>). <c>--record=&lt;파일&gt;</c> 이면 봇의 판을 게임과 같은 모양의 시도 한 줄로 덧붙인다 —
/// 게임 없이 되살리기를 확인하는 길이다.
/// </para>
/// </summary>
public partial class BattleDemo : Node
{
    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();

        // 시드는 **시도 시드 그 자체**다 — 게임을 안 타므로 세션도 번호도 없다. 64비트 그대로 읽는다 (#72 · 설계 §4.4): 게임
        // 로그의 [run][I] attempt=… seed=X 를 --seed=X 로 넘기면 그 시도의 보스 계획이 되살아난다(판은 봇이 싸운다). 전에는 Double 로 읽어
        // 2^53 을 넘는 시드(시도 시드는 거의 다 그렇다)가 다른 판을 돌렸다.
        ulong seed = CmdArgs.UInt64(args, "--seed=") ?? 51;
        // 보스전은 하나다(설계 2026-09-29 조각1 §1) — stages.json 의 유일한 키. 옛 기록(두 단계 시절)을 되살리면 그 줄의 단계를 쓰고,
        // StageRoster 가 범위 밖을 가장 가까운 단계로 잘라 [W] 를 남긴다.
        int stage = 1;

        // 게임과 같은 자리에서 읽는다 — 둘이 다른 판을 세우지 않게 (BattleTables). 되살리기가 대본을 되찾고 지문을 대 보므로 먼저 읽는다.
        BattleTables data = BattleTables.Load();

        // 되살리기 — 게임이 남긴 시도 기록(user://attempts/<세션 시드>.jsonl)에서 그 시도의 시드 · 단계와 **같은 런의 앞 기록**을 읽어 판과
        // 고르기를 다시 세운다. 대본으로 선 시도(GIF · 스크린샷 · 순회)는 기록된 계획이 곧 대본이다.
        IReadOnlyList<AttemptRecord> history = [];
        IReadOnlyList<ScriptPlan>? script = null;
        AttemptEntry? replay = null;
        if (CmdArgs.Text(args, "--history=") is { } historyPath && CmdArgs.UInt64(args, "--attempt=") is { } wanted)
        {
            List<AttemptEntry> entries;
            try
            {
                entries = AttemptFile.Read(historyPath);
            }
            catch (DataException e)
            {
                Log.Error("battle-demo", $"history_unreadable {e.Message}");
                GetTree().Quit(1);
                return;
            }

            replay = entries.FirstOrDefault(e => (ulong)e.Record.Number == wanted);
            if (replay is null)
            {
                Log.Error("battle-demo", $"replay_missing attempt={wanted} path={historyPath} attempts={entries.Count}");
                GetTree().Quit(1);
                return;
            }

            // 입력이 없는 줄(5/8 까지의 게임)은 판을 되살릴 수 없다 — 봇으로 대신 싸우면 기록과 다른 판을 "되살렸다" 고 말하게 된다(Review Focus 3).
            if (replay.Inputs is null)
            {
                Log.Error("battle-demo", $"replay_no_inputs attempt={wanted} path={historyPath} — 입력이 없는 옛 줄이라 판을 되살릴 수 없다");
                GetTree().Quit(1);
                return;
            }

            seed = replay.Record.Seed;
            stage = replay.Record.Stage;
            history = [.. entries.Where(e => e.SessionSeed == replay.SessionSeed && e.Run == replay.Run && e.Record.Number < replay.Record.Number).Select(e => e.Record)];
            if (replay.PickerId == "script")
            {
                script = Replay.Script(replay.Plans, StageRoster.For(data.Stages, stage), data.Patterns, out string? problem);
                if (script is null)
                {
                    Unbuilt(replay, data.DataSha256, problem);
                    GetTree().Quit(1);
                    return;
                }
            }

            Log.Info("battle-demo", $"replay attempt={wanted} run={replay.Run} history={history.Count} seed={seed} stage={stage} picker={replay.PickerId}"
                + $" plans={replay.Plans.Count} inputs={replay.Inputs.Count}");
        }

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

        // 단계 명부와 고르기는 data/stages.json 이 정하고, 게임과 같은 자리에서 세운다(StageRoster.Setup). 기록은 되살리기가 아니면 비어 있다 —
        // 지금 고르기는 기록을 안 읽으므로 시드만으로 게임의 그 시도와 같은 계획이 선다.
        if (StageRoster.Setup(data.Stages, stage, seed, history, data.Patterns, BattleSim.RestTicks(boss), Balance.Data.Picker, script) is not { } setup)
        {
            GetTree().Quit(1);
            return;
        }

        Log.Info("battle-demo", $"start seed={seed} fighter={fighterId} stage={setup.Stage} patterns={setup.PatternIds.Count} picker={setup.PickerId}");

        var battleSetup = new BattleSetup
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
        };

        BattleSim sim;
        if (replay is { Inputs: { } inputs })
        {
            sim = Replay.Run(battleSetup, inputs);
        }
        else
        {
            sim = new BattleSim(battleSetup);
            var bot = new BotPolicy(seed);
            var tape = new InputTape();
            var instances = new InstanceTracker();
            BattleOutcome? outcome = null;
            while (outcome is null)
            {
                InputFrame input = bot.Next(sim);
                tape.Add(input);
                outcome = sim.Tick(input);
                instances.Observe(sim.Boss.CurrentPattern, sim.Events.Count);
            }

            // 게임이 남기는 줄과 같은 모양으로 덧붙인다 — 세션 시드는 데모의 시드 · 런 1 · 시도 1 이라 --attempt=1 로 되살린다(같은 파일에 여러 판을
            // 덧붙이면 첫 줄을 찾는다 — 확인할 때는 새 파일에 남긴다).
            if (CmdArgs.Text(args, "--record=") is { } recordPath)
            {
                var entry = new AttemptEntry(
                    seed, 1, new AttemptRecord(1, setup.Stage, seed, outcome.Value, [.. sim.Events]), setup.PickerId, sim.PlanEntries, sim.Ticks,
                    instances.Finish(sim.Events), [.. tape.Runs], data.DataSha256);
                if (AttemptFile.AppendTo(recordPath, entry) is { } written)
                {
                    Log.Info("battle-demo", $"recorded attempt=1 plans={entry.Plans.Count} inputs={tape.Runs.Count} ticks={sim.Ticks} path={written}");
                }
            }
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
            // guard_n · guard_broken_n 은 **개수**다 (이슈 #47) — 축은 끝의 guard_rate(#104)다. 둘을 같이 찍는 이유는
            // "버텨냈다" 와 "버티다 무너졌다" 가 결과가 정반대이기 때문이다 — 한 칸만 보면
            // 가드가 도는지는 알아도 그것이 일하는지는 모른다.
            + $" guard_n={axes.GuardSamples} guard_broken_n={axes.GuardBrokenSamples}"
            + $" jump_rel_n={axes.JumpChoiceSamples}"
            + $" parry_rel_n={axes.ParryChoiceSamples} dash_bias={axes.DashTimingBias:0.000}"
            + $" dash_var={axes.DashTimingVar:0.000} dash_dir={axes.DashDirectionBias:0.00}"
            + $" jump_bias={axes.JumpTimingBias:0.000} jump_rel={axes.JumpReliance:0.00}"
            + $" air_impact={axes.AirborneAtImpactRatio:0.00} parry_rate={axes.ParryRate:0.00}"
            + $" parry_rel={axes.ParryReliance:0.00} greed={axes.Greed:0.00} dist={axes.DistanceBias:0}"
            + $" guard_rate={axes.GuardRate:0.00}");

        if (replay is not null)
        {
            Replayed(replay, sim, data.DataSha256);
        }

        Log.Marker("battle-demo", $"battle-demo=done outcome={sim.Result?.ToString() ?? "none"} ticks={sim.Ticks} events={sim.Events.Count}");
        GetTree().Quit();
    }

    /// <summary>
    /// 되살린 판이 기록과 같은가 (설계 2026-09-29 조각1 §4.4) — 계획 전부 · 틱 수 · 결과 · 관측. 다르면 데이터의 지문이 까닭을 가른다: 같으면 결정론이
    /// 깨진 것이라 <c>[E]</c>, 다르면 데이터가 바뀌어 다른 판일 수 있어 <c>[W]</c> 다. 줄에는 기록/지금 값을 나란히 싣는다(<see cref="Replay.Compare"/>).
    /// </summary>
    private static void Replayed(AttemptEntry replay, BattleSim sim, string dataSha256)
    {
        string compare = Replay.Compare(replay, sim);
        switch (Replay.Verdict(replay, sim, dataSha256))
        {
            case ReplayVerdict.Match:
                Log.Info("battle-demo", $"replay_match attempt={replay.Record.Number} {compare}");
                break;
            case ReplayVerdict.DataChanged:
                Log.Warn("battle-demo", $"replay_data_changed attempt={replay.Record.Number} {compare}"
                    + $" data_logged={Short(replay.DataSha256)} data_now={Short(dataSha256)}");
                break;
            case ReplayVerdict.Mismatch:
                Log.Error("battle-demo", $"replay_mismatch attempt={replay.Record.Number} {compare} data={Short(dataSha256)}");
                break;
            case ReplayVerdict.NoInputs:
                // _Ready 가 판을 세우기 전에 멈춘다 — 여기 오면 그 길이 빠진 것이다.
                Log.Error("battle-demo", $"replay_no_inputs attempt={replay.Record.Number}");
                break;
        }
    }

    /// <summary>
    /// 대본으로 선 시도의 대본을 못 세웠다 — 기록의 동작이나 캔슬 지점이 지금 데이터에 없다(<see cref="Replay.Script"/>). 지문이 다르면 데이터가 바뀐
    /// 것이라 <c>[W]</c>, 같으면 줄이 데이터와 안 맞는 것이라 <c>[E]</c> 다. 판은 안 세운다.
    /// </summary>
    private static void Unbuilt(AttemptEntry replay, string dataSha256, string? problem)
    {
        if (string.Equals(replay.DataSha256, dataSha256, StringComparison.Ordinal))
        {
            Log.Error("battle-demo", $"replay_mismatch attempt={replay.Record.Number} reason=script {problem}");
            return;
        }

        Log.Warn("battle-demo", $"replay_data_changed attempt={replay.Record.Number} reason=script {problem}"
            + $" data_logged={Short(replay.DataSha256)} data_now={Short(dataSha256)}");
    }

    /// <summary>지문의 앞 12자 — 로그에서 둘이 같은지 · 다른지만 보면 된다.</summary>
    private static string Short(string? sha) => sha is null ? "none" : sha[..Math.Min(12, sha.Length)];
}
