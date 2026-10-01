using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Factory;

/// <summary>
/// 공장 콘솔의 <c>--qtrain</c> — Q 표 보스의 학습 (설계 2026-10-01 조각8 §1). 인자 · 파일뿐이다 — 바퀴는 <see cref="QTrain"/>(테스트도 링크한다).
/// 끝까지 가면 <c>[qtrain][M] qtrain=done</c>.
/// </summary>
internal static class QTrainProgram
{
    internal const string Usage = """
        tools/build.sh qtrain [인자…]  — Q 표 보스의 학습 (설계 2026-10-01 조각8 §1)

          --opponents=LIST     상대들(쉼표) — fleet · 파이터 망 JSON. 판마다 하나를 시드로 고른다
          --iterations=N       바퀴 수 (기본 train.json 의 qtable.iterations)
          --threads=N          동시에 도는 판 수 (기본 코어 수) — 표는 스레드 수와 무관하다
          --train=FILE         학습 설정 (기본 ml/rl/train.json) — reward · ppo.gamma · qtable
          --out=DIR            출력 폴더 (기본 out/qtable/default) — qtable.json · metrics.csv
          --data=DIR · --fleet=FILE · --commit=SHA · --log-level=L  공장과 같다
        """;

    public static int Run(string[] args)
    {
        int threads = Program.Int(args, "--threads=") ?? Environment.ProcessorCount;
        string[] opponents = (CmdArgs.Text(args, "--opponents=") ?? "fleet").Split(',', StringSplitOptions.RemoveEmptyEntries);
        string trainPath = CmdArgs.Text(args, "--train=") ?? Path.Combine("ml", "rl", "train.json");
        string data = CmdArgs.Text(args, "--data=") ?? Path.Combine("overfit", "data");
        string fleetPath = CmdArgs.Text(args, "--fleet=") ?? Path.Combine("tools", "factory", "fleet.json");
        string commit = CmdArgs.Text(args, "--commit=") ?? "unknown";
        string outDir = CmdArgs.Text(args, "--out=") ?? Path.Combine("out", "qtable", "default");
        if (threads < 1 || opponents.Length == 0)
        {
            Console.Error.Write(Usage);
            return 2;
        }

        FactoryTables tables;
        TrainConfig train;
        QTrainDef def;
        FighterSpec[] specs;
        try
        {
            tables = FactoryTables.Load(data, fleetPath);
            train = JsonData<TrainConfig>.ParseOne(File.ReadAllText(trainPath), trainPath);
            def = train.Qtable ?? throw new DataException($"{trainPath}: qtable 이 없다");
            if (train.Ppo is null)
            {
                throw new DataException($"{trainPath}: ppo.gamma 가 없다 — Q 표는 PPO 와 같은 할인을 쓴다");
            }

            specs = Array.ConvertAll(opponents, o => o == "fleet"
                ? new FighterSpec(FighterKind.Fleet, null)
                : new FighterSpec(FighterKind.Net, RolloutProgram.FighterNet(tables, o)));
        }
        catch (Exception e) when (e is DataException or IOException or UnauthorizedAccessException)
        {
            Log.Error("qtrain", $"data_unreadable — {e.Message}");
            return 1;
        }

        int iterations = Program.Int(args, "--iterations=") ?? def.Iterations;
        if (iterations != def.Iterations)
        {
            def = new QTrainDef
            {
                Seed = def.Seed,
                Iterations = iterations,
                Episodes = def.Episodes,
                Alpha = def.Alpha,
                EpsilonStart = def.EpsilonStart,
                EpsilonEnd = def.EpsilonEnd,
                DistanceEdges = def.DistanceEdges,
                Air = def.Air,
            };
        }

        Directory.CreateDirectory(outDir);
        Say($"start iterations={def.Iterations} episodes={def.Episodes} threads={threads} opponents={opponents.Length} commit={commit} out={outDir}");
        QTable table = QTrain.NewTable(tables, def);
        var watch = Stopwatch.StartNew();
        string metricsPath = Path.Combine(outDir, "metrics.csv");
        using (var csv = new StreamWriter(metricsPath, false, new UTF8Encoding(false)))
        {
            csv.Write("iteration,epsilon,episodes,boss_win,return,rows,states,seconds\n");
            for (int it = 0; it < def.Iterations; it++)
            {
                QIteration r = QTrain.Iterate(tables, table, def, it, specs, train.Reward, train.Ppo.Gamma, threads);
                double win = (double)r.BossWins / Math.Max(1, r.Episodes);
                csv.Write(string.Create(CultureInfo.InvariantCulture,
                    $"{it},{QTrain.Epsilon(def, it):0.####},{r.Episodes},{win:0.####},{r.Return:0.######},{r.Rows},{table.States},{watch.Elapsed.TotalSeconds:0.0}\n"));
                csv.Flush();
                if (it % 10 == 0 || it == def.Iterations - 1)
                {
                    Say(string.Create(CultureInfo.InvariantCulture,
                        $"iteration={it} epsilon={QTrain.Epsilon(def, it):0.###} boss_win={win:0.###} return={r.Return:+0.000;-0.000} states={table.States} seconds={watch.Elapsed.TotalSeconds:0}"));
                    File.WriteAllText(Path.Combine(outDir, "qtable.json"), table.ToJson());
                }
            }
        }

        File.WriteAllText(Path.Combine(outDir, "qtable.json"), table.ToJson());
        Say($"done states={table.States} seconds={watch.Elapsed.TotalSeconds:0}");
        Log.Marker("qtrain", "qtrain=done");
        return 0;
    }

    private static void Say(string message) => Program.Write(LogLevel.Info, $"[qtrain][I] {message}");
}
