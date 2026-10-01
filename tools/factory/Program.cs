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
/// 실행은 <c>tools/build.sh factory [인자…]</c> 다. 옛 망의 평가 모드(<c>--evaluate</c> · #114)와 겨냥 표(<c>--check-targeting</c>)는 옛 망과 같이
/// 걷었다(설계 2026-09-29 조각1 §6) — 공장은 봇 함대가 보스와 싸운 기록을 짓는 자리로 남는다(밸런스를 재고, 조각 4 의 망이 배울 재료).
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
        tools/build.sh factory [인자…]  — 봇 함대가 보스와 싸운 기록을 짓는다 (#108 · 설계 2026-09-28 §4)

          --fleet-seed=N      함대 시드 (기본 0) — 봇의 성향 · 세션 시드가 여기서 나온다
          --from=N --to=M     봇 번호 N ~ M-1 (기본 0 ~ 1000)
          --tries=N           봇 한 대의 시도 상한 — 이기면 멈춘다 (기본 5)
          --threads=N         동시에 도는 봇 수 (기본 코어 수) — 결과는 스레드 수와 무관하다
          --chunk=N           한 번에 들고 있는 봇 수 (기본 1000)
          --out=DIR           출력 폴더 (기본 out/factory/<시드>-<from>-<to>)
          --data=DIR          전투 데이터 (기본 overfit/data)
          --fleet=FILE        함대 설정 (기본 tools/factory/fleet.json)
          --commit=SHA        매니페스트에 적을 커밋 (build.sh 가 넣는다)
          --log-level=L       규칙 층의 로그 레벨 (기본 warn) — debug 는 --threads=1 과 같이 쓴다
        """;

    private static readonly object _gate = new();
    private static int _errors;

    public static int Main(string[] args)
    {
        if (CmdArgs.Has(args, "--help") || CmdArgs.Has(args, "-h"))
        {
            Console.Out.Write(CmdArgs.Has(args, "--rollout") ? RolloutProgram.Usage : _usage);
            return 0;
        }

        // 스레드를 띄우기 전에 한 번 — 레벨과 싱크는 정적이다.
        Log.Level = CmdArgs.Text(args, "--log-level=") is { } text && Log.TryParseLevel(text, out LogLevel level) ? level : LogLevel.Warn;
        Log.Sink = Write;

        // 학습의 일꾼(설계 2026-10-01 조각4 §6) — 같은 콘솔 · 같은 싱크 · 같은 [E] 판정이다.
        if (CmdArgs.Has(args, "--rollout"))
        {
            int code = RolloutProgram.Run(args);
            return _errors > 0 ? 1 : code;
        }

        ulong fleetSeed = CmdArgs.UInt64(args, "--fleet-seed=") ?? 0;
        int from = Int(args, "--from=") ?? 0;
        int to = Int(args, "--to=") ?? from + 1000;
        int tries = Int(args, "--tries=") ?? 5;
        int threads = Int(args, "--threads=") ?? Environment.ProcessorCount;
        int chunk = Int(args, "--chunk=") ?? 1000;
        string data = CmdArgs.Text(args, "--data=") ?? Path.Combine("overfit", "data");
        string fleetPath = CmdArgs.Text(args, "--fleet=") ?? Path.Combine("tools", "factory", "fleet.json");
        string commit = CmdArgs.Text(args, "--commit=") ?? "unknown";
        string outDir = CmdArgs.Text(args, "--out=") ?? Path.Combine("out", "factory", $"{fleetSeed}-{from}-{to}");
        if (to <= from || tries < 1 || threads < 1 || chunk < 1)
        {
            Console.Error.WriteLine($"인자가 틀렸다 — from={from} to={to} tries={tries} threads={threads} chunk={chunk}");
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

        IReadOnlyList<string> roster = StageRoster.For(tables.Stages, 1);
        var stats = new FactoryStats(roster);
        Directory.CreateDirectory(outDir);
        Say($"start fleet_seed={fleetSeed} bots={from}..{to - 1} tries={tries} threads={threads} chunk={chunk}"
            + $" data_digest={digest[..12]} fleet={fleetSha[..12]} commit={commit} out={outDir}");

        var watch = Stopwatch.StartNew();
        using (var samples = new CsvFile(Path.Combine(outDir, "samples.csv"), SampleCsv.SamplesHeader))
        using (var bots = new CsvFile(Path.Combine(outDir, "bots.csv"), SampleCsv.BotsHeader))
        {
            try
            {
                FactoryBatch.Run(from, to, threads, bot => BotRun.Run(fleetSeed, bot, tables, tries), results =>
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
            Say($"done bots={stats.Bots} won={stats.Won} samples={stats.Samples}"
                + $" ticks={stats.Ticks} seconds={seconds:0.0} bots_per_s={stats.Bots / seconds:0.0} ticks_per_s={stats.Ticks / seconds:0}");

            foreach (SlotRate slot in stats.Slots)
            {
                Say($"base pattern={slot.Pattern} samples={slot.Samples} hits={slot.Hits} rate={slot.Rate:0.000}");
            }

            Manifest(Path.Combine(outDir, "manifest.json"), new ManifestFacts(
                fleetSeed, from, to, tries, threads, fleetSha, commit, digest, samples, bots, seconds), stats);
            Say($"wrote samples={samples.Rows} bots={bots.Rows} out={outDir}");

            Log.Marker("factory", "factory=done");
            return _errors > 0 ? 1 : 0;
        }
    }

    /// <summary>매니페스트의 사실들 — 인자 · 해시 · 파일.</summary>
    private sealed record ManifestFacts(
        ulong FleetSeed, int From, int To, int Tries, int Threads, string FleetSha, string Commit, string DataDigest,
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
        json.WriteNumber("tries", facts.Tries);
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
        json.WriteNumber("won", stats.Won);
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
        json.WriteEndObject();
    }

    /// <summary>싱크 — 잠그고 한 줄씩 쓴다. <c>[E]</c> 를 센다(공장의 실패 조건).</summary>
    internal static void Write(LogLevel level, string line)
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

    internal static int? Int(string[] args, string prefix) => CmdArgs.UInt64(args, prefix) is { } value ? checked((int)value) : null;

    internal static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
