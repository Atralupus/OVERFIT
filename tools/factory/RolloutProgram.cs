using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Factory;

/// <summary>
/// 공장 콘솔의 <c>--rollout</c> — 학습의 일꾼 (설계 2026-10-01 조각4 §6). 인자 · 파일 · 매니페스트뿐이다 — 판을 도는 것은 <see cref="RolloutRun"/> 과
/// <see cref="RolloutWriter"/>(테스트 프로젝트도 링크한다). 끝까지 가면 <c>[rollout][M] rollout=done</c>.
/// </summary>
internal static class RolloutProgram
{
    internal const string Usage = """
        tools/build.sh rollout [인자…]  — 학습의 일꾼: 보스를 망 조종기로 돌려 경험을 쓴다 (설계 2026-10-01 조각4)

          --weights=FILE|none  정책망 가중치 JSON (기본 none — 열린 칸에 같은 확률)
          --controller=K       net(기본) · rule · random — rule · random 은 비교의 대조군이라 결정을 안 적는다(episodes.csv 만)
          --learner=SIDE       boss(기본) · fighter — 배우는 쪽. 그쪽의 결정만 적는다 (설계 2026-10-01 조각6)
          --opponents=LIST     상대들(쉼표) — 판마다 하나를 고른다. 보스가 배우면 fleet · 파이터 망 JSON, 파이터가 배우면 rule · random · none · 보스 망 JSON
          --episodes=N         판 수 (기본 64)
          --seed=N             시드 (기본 0) — 판의 시드 · 상대 봇이 (시드, 판)에서 나온다
          --threads=N          동시에 도는 판 수 (기본 코어 수) — 결과는 스레드 수와 무관하다
          --train=FILE         학습 설정 (기본 ml/rl/train.json) — 보상의 가중치
          --out=DIR            출력 폴더 (기본 out/rollout/<시드>-<판 수>)
          --data=DIR · --fleet=FILE · --commit=SHA · --log-level=L  공장과 같다
        """;

    public static int Run(string[] args)
    {
        ulong seed = CmdArgs.UInt64(args, "--seed=") ?? 0;
        int episodes = Program.Int(args, "--episodes=") ?? 64;
        int threads = Program.Int(args, "--threads=") ?? Environment.ProcessorCount;
        string weights = CmdArgs.Text(args, "--weights=") ?? "none";
        string controllerText = CmdArgs.Text(args, "--controller=") ?? "net";
        string learnerText = CmdArgs.Text(args, "--learner=") ?? "boss";
        bool fighterLearns = learnerText == "fighter";
        string[] opponents = (CmdArgs.Text(args, "--opponents=") ?? (fighterLearns ? "rule" : "fleet")).Split(',', StringSplitOptions.RemoveEmptyEntries);
        if ((learnerText is not ("boss" or "fighter")) || !Enum.TryParse(controllerText, ignoreCase: true, out RolloutController kind)
            || !Enum.IsDefined(kind) || opponents.Length == 0)
        {
            Console.Error.WriteLine($"인자가 틀렸다 — controller={controllerText} learner={learnerText} opponents={string.Join(',', opponents)}");
            Console.Error.Write(Usage);
            return 2;
        }
        string trainPath = CmdArgs.Text(args, "--train=") ?? Path.Combine("ml", "rl", "train.json");
        string data = CmdArgs.Text(args, "--data=") ?? Path.Combine("overfit", "data");
        string fleetPath = CmdArgs.Text(args, "--fleet=") ?? Path.Combine("tools", "factory", "fleet.json");
        string commit = CmdArgs.Text(args, "--commit=") ?? "unknown";
        string outDir = CmdArgs.Text(args, "--out=") ?? Path.Combine("out", "rollout", $"{seed}-{episodes}");
        if (episodes < 1 || threads < 1)
        {
            Console.Error.WriteLine($"인자가 틀렸다 — episodes={episodes} threads={threads}");
            Console.Error.Write(Usage);
            return 2;
        }

        FactoryTables tables;
        RewardDef reward;
        RewardDef fighterReward;
        Matchup[] matchups;
        PolicyNet? net = null;
        string weightsSha = "none";
        string digest;
        try
        {
            tables = FactoryTables.Load(data, fleetPath);
            digest = DataDigest.Of(name => File.ReadAllBytes(Path.Combine(data, name)));
            TrainConfig train = JsonData<TrainConfig>.ParseOne(File.ReadAllText(trainPath), trainPath);
            reward = train.Reward;
            fighterReward = train.FighterReward ?? train.Reward;
            if (weights != "none")
            {
                net = fighterLearns ? FighterNet(tables, weights) : BossNet(tables, weights);
                weightsSha = Program.Hex(SHA256.HashData(File.ReadAllBytes(weights)));
            }

            matchups = Array.ConvertAll(opponents, o => fighterLearns
                ? new Matchup(RolloutSide.Fighter, Boss(tables, o), new FighterSpec(FighterKind.Net, net))
                : new Matchup(RolloutSide.Boss, new BossSpec(kind, net), o == "fleet" ? new FighterSpec(FighterKind.Fleet, null)
                    : new FighterSpec(FighterKind.Net, o == "none" ? null : FighterNet(tables, o))));
        }
        catch (Exception e) when (e is DataException or IOException or UnauthorizedAccessException)
        {
            Log.Error("rollout", $"data_unreadable — {e.Message}");
            return 1;
        }

        Say($"start seed={seed} episodes={episodes} threads={threads} controller={kind} weights={weights} data_digest={digest[..12]} commit={commit} out={outDir}");
        RolloutSummary sum;
        try
        {
            sum = RolloutWriter.Run(tables, matchups, reward, fighterReward, seed, episodes, threads, outDir);
        }
        catch (AggregateException e)
        {
            Exception first = e.InnerExceptions[0];
            Log.Error("rollout", $"episode_crashed type={first.GetType().Name} — {first.Message}");
            return 1;
        }

        double seconds = Math.Max(sum.Seconds, 1e-9);
        Say($"done episodes={sum.Episodes} rows={sum.Rows} boss_wins={sum.BossWins} ticks={sum.Ticks} seconds={seconds:0.00}"
            + $" decisions_per_s={sum.Rows / seconds:0} ticks_per_s={sum.Ticks / seconds:0}");
        Manifest(Path.Combine(outDir, "manifest.json"), sum, seed, threads, weights, weightsSha, digest, commit, tables, learnerText, opponents);
        Log.Marker("rollout", "rollout=done");
        return 0;
    }

    private static void Manifest(
        string path, RolloutSummary sum, ulong seed, int threads, string weights, string weightsSha, string digest, string commit, FactoryTables tables,
        string learner, string[] opponents)
    {
        using FileStream stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        json.WriteStartObject();
        json.WriteString("_comment", "학습의 일꾼의 매니페스트 (설계 2026-10-01 조각4 §6). 같은 인자 · 가중치 · 커밋 · 데이터면 두 파일의 sha256 이 스레드 수와 무관하게 같다.");
        json.WriteNumber("seed", seed);
        json.WriteNumber("episodes", sum.Episodes);
        json.WriteNumber("threads", threads);
        json.WriteString("learner", learner);
        json.WriteStartArray("opponents");
        foreach (string o in opponents)
        {
            json.WriteStringValue(o);
        }

        json.WriteEndArray();
        json.WriteString("weights", weights);
        json.WriteString("weights_sha256", weightsSha);
        json.WriteString("commit", commit);
        json.WriteString("data_digest", digest);
        json.WriteNumber("obs", sum.Obs);
        json.WriteNumber("actions", sum.Actions);
        json.WriteStartArray("roster");
        foreach (string id in tables.Roster)
        {
            json.WriteStringValue(id);
        }

        json.WriteEndArray();
        json.WriteNumber("boss_max_health", tables.Boss.MaxHealth);
        json.WriteNumber("fighter_max_health", tables.Fighter.MaxHealth);
        json.WriteNumber("rows", sum.Rows);
        json.WriteNumber("boss_wins", sum.BossWins);
        json.WriteNumber("ticks", sum.Ticks);
        json.WriteNumber("seconds", Math.Round(sum.Seconds, 3));
        json.WriteNumber("decisions_per_second", Math.Round(sum.Rows / Math.Max(sum.Seconds, 1e-9), 1));
        json.WriteStartObject("sha256");
        json.WriteString("steps", sum.StepsSha256);
        json.WriteString("episodes", sum.EpisodesSha256);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static PolicyNet BossNet(FactoryTables tables, string path) => PolicyNet.Parse(
        File.ReadAllText(path), path, new BossObservation(tables.Roster.Count).Size, new BossActions(tables.Roster).Count, tables.Roster);

    private static PolicyNet FighterNet(FactoryTables tables, string path) => PolicyNet.Parse(
        File.ReadAllText(path), path, new FighterObservation(tables.Roster.Count).Size, FighterActions.Count, tables.Roster);

    /// <summary>파이터가 배울 때의 보스 상대 — rule · random · none(안 배운 망) · 보스 망 JSON.</summary>
    private static BossSpec Boss(FactoryTables tables, string spec) => spec switch
    {
        "rule" => new BossSpec(RolloutController.Rule, null),
        "random" => new BossSpec(RolloutController.Random, null),
        "none" => new BossSpec(RolloutController.Net, null),
        _ => new BossSpec(RolloutController.Net, BossNet(tables, spec)),
    };

    private static void Say(string message) => Program.Write(LogLevel.Info, $"[rollout][I] {message}");
}

/// <summary><c>ml/rl/train.json</c> 의 모양 — 조각 5 가 PPO 수치를 더한다.</summary>
public sealed class TrainConfig
{
    public required RewardDef Reward { get; init; }

    /// <summary>파이터 망의 보상 (설계 2026-10-01 조각6 §1.3) — 없으면 보스의 것과 같은 가중치.</summary>
    public RewardDef? FighterReward { get; init; }
}
