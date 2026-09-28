using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Factory;

/// <summary>
/// 공장의 평가 모드 (#114 · 설계 2026-09-28 §7.1) — <c>tools/build.sh evaluate</c>. 학습에 안 쓴 봇마다 <see cref="EvalRun"/> 을 돌려 세 CSV
/// (<see cref="EvalCsv"/>)와 매니페스트를 쓴다. 지표와 관문은 파이썬(<c>ml/validate.py</c>)이 이 파일들로 잰다 — 부트스트랩 · ECE 는 넘파이가 한다.
///
/// <para>
/// <b>학습에 쓴 봇으로 평가하지 않는다.</b> 기본 범위는 망이 배운 함대 시드(<c>network.json</c> 의 <c>trained_on</c>)에서 배운 봇 다음부터다. 학습의 원본은
/// 봇 0 부터 짓는다(<c>tools/build.sh train</c> 의 기본 원본 · <c>trained_on.bots</c> 가 그 수) — 같은 함대 시드로 그 아래를 물으면 멈춘다.
/// </para>
/// </summary>
internal static class EvaluateMode
{
    /// <summary>기본 봇 수 — 습관형 하나가 3천 대 남짓 · 고르게 약한 봇이 천 대 가까이 2단계에 간다(4번 PR 의 시험 몫으로 가늠했다).</summary>
    private const int _defaultBots = 50_000;

    public static int Run(string[] args)
    {
        string data = CmdArgs.Text(args, "--data=") ?? Path.Combine("overfit", "data");
        string fleetPath = CmdArgs.Text(args, "--fleet=") ?? Path.Combine("tools", "factory", "fleet.json");
        string commit = CmdArgs.Text(args, "--commit=") ?? "unknown";
        FactoryTables tables;
        PlayerNet? net;
        string digest, fleetSha, networkSha;
        try
        {
            tables = FactoryTables.Load(data, fleetPath);
            byte[] networkBytes = File.ReadAllBytes(Path.Combine(data, "network.json"));
            net = PlayerNet.Load(Encoding.UTF8.GetString(networkBytes), "network.json");
            networkSha = Program.Hex(SHA256.HashData(networkBytes));
            digest = DataDigest.Of(name => File.ReadAllBytes(Path.Combine(data, name)));
            fleetSha = Program.Hex(SHA256.HashData(File.ReadAllBytes(fleetPath)));
        }
        catch (Exception e) when (e is DataException or IOException or UnauthorizedAccessException)
        {
            Log.Error("factory", $"data_unreadable data={data} fleet={fleetPath} — {e.Message}");
            return 1;
        }

        if (net is null)
        {
            return 1; // 모양이 틀린 망은 PlayerNet 이 [net][E] 를 남겼다
        }

        NetworkTrainedOn trained = net.TrainedOn;
        ulong fleetSeed = CmdArgs.UInt64(args, "--fleet-seed=") ?? trained.FleetSeed;
        int from = Program.Int(args, "--from=") ?? (fleetSeed == trained.FleetSeed ? trained.Bots : 0);
        int to = Program.Int(args, "--to=") ?? from + _defaultBots;
        int stage1Tries = Program.Int(args, "--stage1-tries=") ?? 5;
        int stage2Tries = Program.Int(args, "--stage2-tries=") ?? 5;
        int threads = Program.Int(args, "--threads=") ?? Environment.ProcessorCount;
        int chunk = Program.Int(args, "--chunk=") ?? 1000;
        string outDir = CmdArgs.Text(args, "--out=") ?? Path.Combine("out", "evaluate", $"{fleetSeed}-{from}-{to}");
        if (to <= from || stage1Tries < 1 || stage2Tries < 1 || threads < 1 || chunk < 1)
        {
            return Program.Usage($"인자가 틀렸다 — from={from} to={to} stage1-tries={stage1Tries} stage2-tries={stage2Tries} threads={threads} chunk={chunk}");
        }

        if (fleetSeed == trained.FleetSeed && from < trained.Bots)
        {
            Log.Error("factory", $"eval_overlaps_training fleet_seed={fleetSeed} from={from} trained_bots=0..{trained.Bots - 1} — 망이 배운 봇으로 평가하면 외운 것을 잰다");
            return 1;
        }

        var network = new NetworkContext(net, tables.Picker);
        int heads = net.Heads.Count;
        Directory.CreateDirectory(outDir);
        Program.Say($"evaluate fleet_seed={fleetSeed} bots={from}..{to - 1} tries={stage1Tries}/{stage2Tries} threads={threads} chunk={chunk}"
            + $" data_digest={digest[..12]} network={networkSha[..12]} trained_on={trained.Commit[..Math.Min(12, trained.Commit.Length)]} commit={commit} out={outDir}");

        var totals = new Totals();
        var watch = Stopwatch.StartNew();
        using var attempts = new CsvFile(Path.Combine(outDir, "attempts.csv"), EvalCsv.AttemptsHeader(heads));
        using var samples = new CsvFile(Path.Combine(outDir, "samples.csv"), EvalCsv.SamplesHeader);
        using var bots = new CsvFile(Path.Combine(outDir, "bots.csv"), EvalCsv.BotsHeader);
        try
        {
            FactoryBatch.Run(from, to, threads, bot => EvalRun.Run(fleetSeed, bot, tables, network, stage1Tries, stage2Tries), results =>
            {
                var attemptText = new StringBuilder();
                var sampleText = new StringBuilder();
                var botText = new StringBuilder();
                int attemptRows = 0, sampleRows = 0;
                foreach (EvalResult result in results)
                {
                    totals.Add(result);
                    EvalCsv.AppendAttempts(attemptText, result);
                    EvalCsv.AppendSamples(sampleText, result);
                    EvalCsv.AppendBot(botText, result);
                    attemptRows += result.Network.Attempts.Count + result.Uniform.Attempts.Count;
                    sampleRows += result.Network.Attempts.Sum(a => a.Instances.Count) + result.Uniform.Attempts.Sum(a => a.Instances.Count);
                }

                attempts.Write(attemptText, attemptRows);
                samples.Write(sampleText, sampleRows);
                bots.Write(botText, results.Count);
                Program.Say($"progress bots={totals.Bots}/{to - from} attempts={attempts.Rows} samples={samples.Rows} seconds={watch.Elapsed.TotalSeconds:0.0}");
            }, chunk);
        }
        catch (AggregateException e)
        {
            Exception first = e.InnerExceptions[0];
            Log.Error("factory", $"bot_crashed type={first.GetType().Name} — {first.Message}");
            return 1;
        }

        watch.Stop();
        double seconds = Math.Max(watch.Elapsed.TotalSeconds, 1e-9);
        Program.Say($"done bots={totals.Bots} reached_stage2={totals.Reached} ticks={totals.Ticks} seconds={seconds:0.0} bots_per_s={totals.Bots / seconds:0.0}");
        foreach (ArmTotals arm in new[] { totals.Network, totals.Uniform })
        {
            Program.Say($"arm={arm.Name} attempts={arm.Attempts} samples={arm.Samples} hit_rate={arm.HitRate:0.000} won={arm.Won}/{totals.Reached}"
                + $" narrowed={arm.Narrowed} thin={arm.Thin} no_habit={arm.NoHabit}");
        }

        Manifest(Path.Combine(outDir, "manifest.json"), new ManifestFacts(
            fleetSeed, from, to, stage1Tries, stage2Tries, threads, fleetSha, commit, digest, networkSha, trained, tables.Picker, attempts, samples, bots, seconds),
            totals);
        Program.Say($"wrote attempts={attempts.Rows} samples={samples.Rows} bots={bots.Rows} out={outDir}");
        Log.Marker("factory", "evaluate=done");
        return Program.Errors > 0 ? 1 : 0;
    }

    /// <summary>매니페스트의 사실들.</summary>
    private sealed record ManifestFacts(
        ulong FleetSeed, int From, int To, int Stage1Tries, int Stage2Tries, int Threads, string FleetSha, string Commit, string DataDigest,
        string NetworkSha, NetworkTrainedOn TrainedOn, PickerBalance Picker, CsvFile Attempts, CsvFile Samples, CsvFile Bots, double Seconds);

    /// <summary>
    /// <c>manifest.json</c> — 인자 · 해시 · 파일 · 갈래마다의 합. <c>network_sha256</c> 은 <c>ml/validate.py</c> 가 지금의 <c>network.json</c> 과 대 본다 —
    /// 다른 망으로 지은 평가를 지금 망의 판정으로 읽지 않게.
    /// </summary>
    private static void Manifest(string path, ManifestFacts facts, Totals totals)
    {
        using FileStream stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        json.WriteStartObject();
        json.WriteString("_comment", "평가 모드의 매니페스트 (#114 · 설계 2026-09-28 §7.1). 같은 인자 · 커밋 · 데이터 · 망이면 세 CSV 의 sha256 이 스레드 수와 무관하게 같다.");
        json.WriteNumber("fleet_seed", facts.FleetSeed);
        json.WriteNumber("bot_from", facts.From);
        json.WriteNumber("bot_to", facts.To);
        json.WriteNumber("stage1_tries", facts.Stage1Tries);
        json.WriteNumber("stage2_tries", facts.Stage2Tries);
        json.WriteString("fleet_sha256", facts.FleetSha);
        json.WriteString("commit", facts.Commit);
        json.WriteString("data_digest", facts.DataDigest);
        json.WriteString("network_sha256", facts.NetworkSha);
        json.WriteStartObject("trained_on");
        json.WriteString("commit", facts.TrainedOn.Commit);
        json.WriteNumber("fleet_seed", facts.TrainedOn.FleetSeed);
        json.WriteNumber("bots", facts.TrainedOn.Bots);
        json.WriteEndObject();
        json.WriteStartObject("picker");
        json.WriteNumber("min_samples", facts.Picker.MinSamples);
        json.WriteNumber("lift_min", facts.Picker.LiftMin);
        json.WriteNumber("max_targeted", facts.Picker.MaxTargeted);
        json.WriteEndObject();
        json.WriteStartObject("rows");
        json.WriteNumber("attempts", facts.Attempts.Rows);
        json.WriteNumber("samples", facts.Samples.Rows);
        json.WriteNumber("bots", facts.Bots.Rows);
        json.WriteEndObject();
        json.WriteStartObject("sha256");
        json.WriteString("attempts", facts.Attempts.Sha256);
        json.WriteString("samples", facts.Samples.Sha256);
        json.WriteString("bots", facts.Bots.Sha256);
        json.WriteEndObject();
        json.WriteNumber("threads", facts.Threads);
        json.WriteNumber("seconds", Math.Round(facts.Seconds, 3));
        json.WriteNumber("ticks", totals.Ticks);
        json.WriteNumber("reached_stage2", totals.Reached);
        json.WriteStartObject("arms");
        foreach (ArmTotals arm in new[] { totals.Network, totals.Uniform })
        {
            json.WriteStartObject(arm.Name);
            json.WriteNumber("attempts", arm.Attempts);
            json.WriteNumber("samples", arm.Samples);
            json.WriteNumber("hits", arm.Hits);
            json.WriteNumber("won", arm.Won);
            json.WriteNumber("narrowed", arm.Narrowed);
            json.WriteNumber("thin", arm.Thin);
            json.WriteNumber("no_habit", arm.NoHabit);
            json.WriteEndObject();
        }

        json.WriteEndObject();
        json.WriteEndObject();
    }

    /// <summary>한 갈래의 합 — 로그와 매니페스트. 판정은 파이썬이 한다(여기는 한눈에 보는 수다).</summary>
    private sealed class ArmTotals(string name)
    {
        public string Name { get; } = name;

        public long Attempts { get; private set; }

        public long Samples { get; private set; }

        public long Hits { get; private set; }

        public long Won { get; private set; }

        public long Narrowed { get; private set; }

        public long Thin { get; private set; }

        public long NoHabit { get; private set; }

        public double HitRate => Samples == 0 ? 0 : (double)Hits / Samples;

        public void Add(EvalArm arm)
        {
            Won += arm.Won ? 1 : 0;
            foreach (EvalAttempt a in arm.Attempts)
            {
                Attempts++;
                Samples += a.Instances.Count;
                Hits += a.Instances.Count(i => i.Hit);
                Narrowed += a.Decision?.Mode == PickDecision.ModeNarrowed ? 1 : 0;
                Thin += a.Decision?.Reason == PickDecision.ReasonThin ? 1 : 0;
                NoHabit += a.Decision?.Reason == PickDecision.ReasonNoHabit ? 1 : 0;
            }
        }
    }

    /// <summary>평가 전체의 합 — 묶음을 받는 쪽(한 스레드)만 고친다.</summary>
    private sealed class Totals
    {
        public ArmTotals Network { get; } = new(NetworkPicker.Id);

        public ArmTotals Uniform { get; } = new(StageRoster.UniformArm);

        public long Bots { get; private set; }

        public long Reached { get; private set; }

        public long Ticks { get; private set; }

        public void Add(EvalResult result)
        {
            Bots++;
            Reached += result.ReachedStage2 ? 1 : 0;
            Ticks += result.Ticks;
            Network.Add(result.Network);
            Uniform.Add(result.Uniform);
        }
    }
}
