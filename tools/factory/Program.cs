using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Factory;

/// <summary>
/// 데이터 공장의 콘솔 (#108 · 설계 2026-09-28 §4) — 인자 · 파일 · 매니페스트 · 로그 싱크뿐이다. 표본을 짓는 것은 링크된 순수 파일들이다
/// (<see cref="BotRun"/> · <see cref="FactoryBatch"/> · <see cref="SampleCsv"/> · <see cref="FactoryStats"/> — 테스트 프로젝트도 링크한다).
/// 실행은 <c>tools/build.sh factory [인자…]</c> 다. <c>--evaluate</c> 면 평가 모드다(<see cref="EvaluateMode"/> · #114 — <c>tools/build.sh evaluate</c>).
///
/// <para>
/// <b>로그.</b> <see cref="Log"/> 는 정적이라 스레드를 띄우기 <b>전에</b> 레벨(기본 <c>warn</c>)과 싱크(잠금 · <c>[E]</c> 세기)를 한 번 정한다. 규칙 층이
/// <c>[E]</c> 를 하나라도 내면 공장은 실패로 끝난다 — 그 줄을 삼키면 망이 규칙 위반의 판을 배운다(헤드리스 판정과 같은 불변식 · CLAUDE.md §5).
/// 공장 자신의 줄(<c>[factory][I]</c>)은 레벨을 안 거친다: 레벨은 전역이라 info 로 내리면 규칙 층이 판마다 <c>[stage][I]</c> · <c>[result][I]</c> 를
/// 쏟는다. 끝까지 가면 <c>[factory][M] factory=done</c>.
/// </para>
/// </summary>
public static class Program
{
    private const string _usage = """
        tools/build.sh factory [인자…]  — 봇 함대로 망의 학습 데이터를 짓는다 (#108 · 설계 2026-09-28 §4)

          --fleet-seed=N      함대 시드 (기본 0) — 봇의 성향 · 세션 시드가 여기서 나온다
          --from=N --to=M     봇 번호 N ~ M-1 (기본 0 ~ 1000)
          --stage1-tries=N    1단계 시도 상한 (기본 5)
          --stage2-tries=N    2단계 시도 상한 (기본 5)
          --threads=N         동시에 도는 봇 수 (기본 코어 수) — 결과는 스레드 수와 무관하다
          --chunk=N           한 번에 들고 있는 봇 수 (기본 1000)
          --out=DIR           출력 폴더 (기본 out/factory/<시드>-<from>-<to>)
          --data=DIR          전투 데이터 (기본 overfit/data)
          --fleet=FILE        함대 설정 (기본 tools/factory/fleet.json)
          --commit=SHA        매니페스트에 적을 커밋 (build.sh 가 넣는다)
          --log-level=L       규칙 층의 로그 레벨 (기본 warn) — debug 는 --threads=1 과 같이 쓴다
          --check-targeting   원본 겨냥 표의 한 줄이라도 기저율 이하면 실패(종료 코드 1)

        tools/build.sh evaluate [인자…]  — 같은 봇에게 망 보스와 무작위 보스 (#114 · 설계 2026-09-28 §7.1)

          --evaluate          평가 모드 (build.sh evaluate 가 넣는다) — 1단계 한 번 · 2단계를 망 · 무작위 갈래로 한 번씩
          --fleet-seed=N      기본은 망이 배운 함대 시드(network.json 의 trained_on)
          --from=N --to=M     기본은 망이 배운 봇 다음부터 5만 대 — 배운 봇과 겹치면 멈춘다
          --out=DIR           출력 폴더 (기본 out/evaluate/<시드>-<from>-<to>)
          (그 밖의 인자는 위와 같다 — --check-targeting 은 없다)
        """;

    private static readonly object _gate = new();
    private static int _errors;

    public static int Main(string[] args)
    {
        if (CmdArgs.Has(args, "--help") || CmdArgs.Has(args, "-h"))
        {
            Console.Out.Write(_usage);
            return 0;
        }

        // 스레드를 띄우기 전에 한 번 — 레벨과 싱크는 정적이다. 두 모드가 같이 쓴다.
        Log.Level = CmdArgs.Text(args, "--log-level=") is { } text && Log.TryParseLevel(text, out LogLevel level) ? level : LogLevel.Warn;
        Log.Sink = Write;
        if (CmdArgs.Has(args, "--evaluate"))
        {
            return EvaluateMode.Run(args);
        }

        ulong fleetSeed = CmdArgs.UInt64(args, "--fleet-seed=") ?? 0;
        int from = Int(args, "--from=") ?? 0;
        int to = Int(args, "--to=") ?? from + 1000;
        int stage1Tries = Int(args, "--stage1-tries=") ?? 5;
        int stage2Tries = Int(args, "--stage2-tries=") ?? 5;
        int threads = Int(args, "--threads=") ?? Environment.ProcessorCount;
        int chunk = Int(args, "--chunk=") ?? 1000;
        string data = CmdArgs.Text(args, "--data=") ?? Path.Combine("overfit", "data");
        string fleetPath = CmdArgs.Text(args, "--fleet=") ?? Path.Combine("tools", "factory", "fleet.json");
        string commit = CmdArgs.Text(args, "--commit=") ?? "unknown";
        string outDir = CmdArgs.Text(args, "--out=") ?? Path.Combine("out", "factory", $"{fleetSeed}-{from}-{to}");
        bool checkTargeting = CmdArgs.Has(args, "--check-targeting");
        if (to <= from || stage1Tries < 1 || stage2Tries < 1 || threads < 1 || chunk < 1)
        {
            Console.Error.WriteLine($"인자가 틀렸다 — from={from} to={to} stage1-tries={stage1Tries} stage2-tries={stage2Tries} threads={threads} chunk={chunk}");
            Console.Error.Write(_usage);
            return 2;
        }

        FactoryTables tables;
        string digest;
        string fleetSha;
        try
        {
            tables = FactoryTables.Load(data, fleetPath);
            digest = DataDigest.Of(name => File.ReadAllBytes(Path.Combine(data, name)));
            fleetSha = Hex(SHA256.HashData(File.ReadAllBytes(fleetPath)));
        }
        catch (Exception e) when (e is DataException or IOException or UnauthorizedAccessException)
        {
            Log.Error("factory", $"data_unreadable data={data} fleet={fleetPath} — {e.Message}");
            return 1;
        }

        IReadOnlyList<string> roster = StageRoster.For(tables.Stages, 2);
        var stats = new FactoryStats(roster, tables.Fleet.Targeting);
        Directory.CreateDirectory(outDir);
        Say($"start fleet_seed={fleetSeed} bots={from}..{to - 1} tries={stage1Tries}/{stage2Tries} threads={threads} chunk={chunk}"
            + $" data_digest={digest[..12]} fleet={fleetSha[..12]} commit={commit} out={outDir}");

        var watch = Stopwatch.StartNew();
        using (var samples = new CsvFile(Path.Combine(outDir, "samples.csv"), SampleCsv.SamplesHeader))
        using (var bots = new CsvFile(Path.Combine(outDir, "bots.csv"), SampleCsv.BotsHeader))
        {
            try
            {
                FactoryBatch.Run(from, to, threads, bot => BotRun.Run(fleetSeed, bot, tables, stage1Tries, stage2Tries), results =>
                {
                    var sampleText = new StringBuilder();
                    var botText = new StringBuilder();
                    int sampleRows = 0;
                    foreach (BotResult result in results)
                    {
                        stats.Add(result);
                        SampleCsv.AppendBot(botText, result);
                        foreach (FactorySample sample in result.Samples)
                        {
                            SampleCsv.AppendSample(sampleText, sample);
                        }

                        sampleRows += result.Samples.Count;
                    }

                    samples.Write(sampleText, sampleRows);
                    bots.Write(botText, results.Count);
                    Say($"progress bots={stats.Bots}/{to - from} samples={stats.Samples} seconds={watch.Elapsed.TotalSeconds:0.0}");
                }, chunk);
            }
            catch (AggregateException e)
            {
                // 봇 하나의 예외가 병렬 루프를 멈췄다 — 우리 코드의 버그다. 첫 예외를 [E] 로 남기고 표지 없이 끝낸다.
                Exception first = e.InnerExceptions[0];
                Log.Error("factory", $"bot_crashed type={first.GetType().Name} — {first.Message}");
                return 1;
            }

            watch.Stop();
            double seconds = Math.Max(watch.Elapsed.TotalSeconds, 1e-9);
            Say($"done bots={stats.Bots} reached_stage2={stats.ReachedStage2} won_stage2={stats.WonStage2} samples={stats.Samples}"
                + $" ticks={stats.Ticks} seconds={seconds:0.0} bots_per_s={stats.Bots / seconds:0.0} ticks_per_s={stats.Ticks / seconds:0}");

            bool below = Report(stats);
            Manifest(Path.Combine(outDir, "manifest.json"), new ManifestFacts(
                fleetSeed, from, to, stage1Tries, stage2Tries, threads, fleetSha, commit, digest, samples, bots, seconds), stats);
            Say($"wrote samples={samples.Rows} bots={bots.Rows} out={outDir}");

            Log.Marker("factory", "factory=done");
            if (_errors > 0)
            {
                return 1;
            }

            return checkTargeting && below ? 1 : 0;
        }
    }

    /// <summary>기저율과 원본 겨냥 표를 찍는다. 기저율 이하인 줄이 있으면 참 — <c>[W]</c> 로 남기고(이상하지만 계속 간다) 관문을 켜면 실패다.</summary>
    private static bool Report(FactoryStats stats)
    {
        foreach (SlotRate slot in stats.Slots)
        {
            Say($"base pattern={slot.Pattern} samples={slot.Samples} hits={slot.Hits} rate={slot.Rate:0.000}");
        }

        bool below = false;
        foreach (TargetingResult row in stats.Targeting)
        {
            string line = $"habit={Habit(row.Row.Habit)} pattern={row.Row.Pattern} min_rhythm={row.Row.MinRhythm:0.##} bots={row.Bots}"
                + $" samples={row.Samples} rate={row.Rate:0.000} base={row.BaseRate:0.000} diff={row.Diff:+0.000;-0.000;0.000}";
            if (row.Above)
            {
                Say($"targeting {line} ok");
            }
            else
            {
                below = true;
                Log.Warn("factory", $"targeting_below {line} — 이 습관형이 겨냥 패턴에 기저율보다 더 안 맞는다(봇의 모형이나 패턴의 겨냥을 보라)");
            }
        }

        return below;
    }

    /// <summary>매니페스트의 사실들 — 인자 · 해시 · 파일.</summary>
    private sealed record ManifestFacts(
        ulong FleetSeed, int From, int To, int Stage1Tries, int Stage2Tries, int Threads, string FleetSha, string Commit, string DataDigest,
        CsvFile Samples, CsvFile Bots, double Seconds);

    /// <summary><c>manifest.json</c> — 설계 §4.4 의 칸. 한글 id 는 그대로 싣는다(사람이 읽는다).</summary>
    private static void Manifest(string path, ManifestFacts facts, FactoryStats stats)
    {
        using FileStream stream = File.Create(path);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        json.WriteStartObject();
        json.WriteString("_comment", "데이터 공장의 매니페스트 (#108 · 설계 2026-09-28 §4.4). 같은 인자 · 커밋 · 데이터면 두 CSV 의 sha256 이 스레드 수와 무관하게 같다.");
        json.WriteNumber("fleet_seed", facts.FleetSeed);
        json.WriteNumber("bot_from", facts.From);
        json.WriteNumber("bot_to", facts.To);
        json.WriteNumber("stage1_tries", facts.Stage1Tries);
        json.WriteNumber("stage2_tries", facts.Stage2Tries);
        json.WriteString("fleet_sha256", facts.FleetSha);
        json.WriteString("commit", facts.Commit);
        json.WriteString("data_digest", facts.DataDigest);
        json.WriteStartObject("rows");
        json.WriteNumber("samples", facts.Samples.Rows);
        json.WriteNumber("bots", facts.Bots.Rows);
        json.WriteEndObject();
        json.WriteStartObject("sha256");
        json.WriteString("samples", facts.Samples.Sha256);
        json.WriteString("bots", facts.Bots.Sha256);
        json.WriteEndObject();
        json.WriteNumber("threads", facts.Threads);
        json.WriteNumber("seconds", Math.Round(facts.Seconds, 3));
        json.WriteNumber("ticks", stats.Ticks);
        json.WriteNumber("ticks_per_second", Math.Round(stats.Ticks / facts.Seconds));
        json.WriteNumber("bots_per_second", Math.Round(stats.Bots / facts.Seconds, 2));
        json.WriteNumber("reached_stage2", stats.ReachedStage2);
        json.WriteNumber("won_stage2", stats.WonStage2);
        json.WriteStartArray("base_rates");
        foreach (SlotRate slot in stats.Slots)
        {
            json.WriteStartObject();
            json.WriteString("pattern", slot.Pattern);
            json.WriteNumber("samples", slot.Samples);
            json.WriteNumber("hits", slot.Hits);
            json.WriteNumber("rate", slot.Rate);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("targeting");
        foreach (TargetingResult row in stats.Targeting)
        {
            json.WriteStartObject();
            json.WriteString("habit", Habit(row.Row.Habit));
            json.WriteString("pattern", row.Row.Pattern);
            json.WriteNumber("min_rhythm", row.Row.MinRhythm);
            json.WriteNumber("bots", row.Bots);
            json.WriteNumber("samples", row.Samples);
            json.WriteNumber("hits", row.Hits);
            json.WriteNumber("rate", row.Rate);
            json.WriteNumber("base", row.BaseRate);
            json.WriteNumber("diff", row.Diff);
            json.WriteBoolean("above", row.Above);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>지금까지 난 <c>[E]</c> 의 수 — 평가 모드도 이것으로 실패를 가른다.</summary>
    internal static int Errors
    {
        get
        {
            lock (_gate)
            {
                return _errors;
            }
        }
    }

    /// <summary>인자가 틀렸을 때 — 무엇이 틀렸는지와 쓰는 법.</summary>
    internal static int Usage(string problem)
    {
        Console.Error.WriteLine(problem);
        Console.Error.Write(_usage);
        return 2;
    }

    /// <summary>싱크 — 잠그고 한 줄씩 쓴다. <c>[E]</c> 를 센다(공장의 실패 조건).</summary>
    private static void Write(LogLevel level, string line)
    {
        lock (_gate)
        {
            if (level == LogLevel.Error)
            {
                _errors++;
            }

            Console.Out.WriteLine(line);
        }
    }

    /// <summary>공장 자신의 줄 — 레벨을 안 거친다(위 설명).</summary>
    internal static void Say(string message) => Write(LogLevel.Info, $"[factory][I] {message}");

    internal static string Habit(BotHabit habit) => habit.ToString().ToLowerInvariant();

    internal static int? Int(string[] args, string prefix) => CmdArgs.UInt64(args, prefix) is { } value ? checked((int)value) : null;

    internal static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
