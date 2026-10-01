using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Overfit.Battle.Rules;

namespace Overfit.Factory;

/// <summary>쓴 경험의 요약 — 매니페스트가 싣는다.</summary>
public sealed record RolloutSummary(int Episodes, int Rows, int Obs, int Actions, int BossWins, long Ticks, double Seconds, string StepsSha256, string EpisodesSha256);

/// <summary>
/// 학습의 일꾼이 판들을 병렬로 돌려 쓴다 (설계 2026-10-01 조각4 §6) — 공장과 같은 <see cref="FactoryBatch"/> 라 판 순서로 쓰고 스레드 수와 무관하게 같은 바이트다.
///
/// <para>
/// <c>steps.bin</c> 의 한 줄(작은 끝): <c>obs float32[D] · mask uint8[A] · action int16 · logp float32 · value float32 · reward float32 · done uint8 ·
/// span int32 · episode int32</c> — span 은 결정의 길이(틱)이고 학습기가 시간으로 할인한다(조각 5 의 최종 리뷰). 넘파이가 구조체 dtype 하나로 읽는다(<c>ml/rl/rollout.py</c>). float32 인 것은 학습에 충분하고 파일이 절반이라서다 — 게임의 망은 가중치
/// JSON 을 double 로 읽는다.
/// </para>
/// </summary>
public static class RolloutWriter
{
    public const string EpisodesHeader = "episode,habit,outcome,boss_won,ticks,boss_lost,fighter_lost,steps,reward";

    public static RolloutSummary Run(
        FactoryTables tables, PolicyNet? net, RewardDef reward, ulong seed, int episodes, int threads, string outDir, int chunk = 256,
        RolloutController kind = RolloutController.Net)
    {
        ArgumentNullException.ThrowIfNull(tables);
        Directory.CreateDirectory(outDir);
        int obs = new BossObservation(tables.Roster.Count).Size;
        int actions = new BossActions(tables.Roster).Count;
        int rows = 0;
        int wins = 0;
        long ticks = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string stepsPath = Path.Combine(outDir, "steps.bin");
        string episodesPath = Path.Combine(outDir, "episodes.csv");
        using (var bin = new BinaryWriter(File.Create(stepsPath)))
        using (var csv = new StreamWriter(episodesPath, false, new UTF8Encoding(false)))
        {
            csv.Write(EpisodesHeader + "\n");
            FactoryBatch.Run(0, episodes, threads, e => RolloutRun.Play(tables, net, reward, seed, e, kind), results =>
            {
                foreach (Episode e in results)
                {
                    double total = 0;
                    foreach (RolloutStep s in e.Steps)
                    {
                        Write(bin, s, e.Index);
                        total += s.Reward;
                    }

                    rows += e.Steps.Count;
                    wins += e.BossWon ? 1 : 0;
                    ticks += e.Ticks;
                    csv.Write(string.Create(CultureInfo.InvariantCulture,
                        $"{e.Index},{e.Habit},{e.Outcome},{(e.BossWon ? 1 : 0)},{e.Ticks},{e.BossLost},{e.FighterLost},{e.Steps.Count},{total:R}\n"));
                }
            }, chunk);
        }

        watch.Stop();
        return new RolloutSummary(episodes, rows, obs, actions, wins, ticks, watch.Elapsed.TotalSeconds, Sha(stepsPath), Sha(episodesPath));
    }

    private static void Write(BinaryWriter bin, RolloutStep s, int episode)
    {
        foreach (double x in s.Observation)
        {
            bin.Write((float)x);
        }

        foreach (bool m in s.Mask)
        {
            bin.Write((byte)(m ? 1 : 0));
        }

        bin.Write((short)s.Action);
        bin.Write((float)s.LogProb);
        bin.Write((float)s.Value);
        bin.Write((float)s.Reward);
        bin.Write((byte)(s.Done ? 1 : 0));
        bin.Write(s.Span);
        bin.Write(episode);
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
