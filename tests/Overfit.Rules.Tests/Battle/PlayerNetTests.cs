using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Overfit.Battle.Rules;
using Overfit.Core;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 게임 안의 망 (#112 · 설계 2026-09-28 §6.1 · §5.7). 로짓은 골든과 <b>같은 연산 순서</b>라 파이썬(표준 라이브러리) == 골든 파일 == C# 이
/// 비트까지 선다 — 허용 오차 없이 <c>==</c> 로 본다. 모양이 틀린 파일은 규칙 위반이라 <c>[E]</c> 다.
/// </summary>
public class PlayerNetTests
{
    [Fact]
    public void 골든의_로짓과_비트까지_같다()
    {
        // ml/golden.py 가 network.json 으로 셈한 스무 입력의 로짓(NetworkGolden.json) — check 가 파이썬 쪽을 매 커밋 다시 셈한다.
        PlayerNet net = TestNets.Real();
        using JsonDocument golden = JsonDocument.Parse(File.ReadAllText(Path.Combine("Battle", "NetworkGolden.json")));
        JsonElement cases = golden.RootElement.GetProperty("cases");

        cases.GetArrayLength().ShouldBe(20);
        foreach (JsonElement c in cases.EnumerateArray())
        {
            double[] input = c.GetProperty("input").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            double[] expected = c.GetProperty("logits").EnumerateArray().Select(v => v.GetDouble()).ToArray();

            double[] got = net.Logits(input);

            got.Select(BitConverter.DoubleToInt64Bits).ShouldBe(expected.Select(BitConverter.DoubleToInt64Bits), $"입력 {string.Join(",", input.Take(3))}…");
        }
    }

    [Fact]
    public void 지금의_망의_머리는_2단계_명부다()
    {
        // 머리의 순서 = 뽑기 좌표의 칸(설계 §5.2). 학습 뒤에 명부를 바꾸면 망의 칸이 다른 패턴을 가리킨다 — 여기서 멈춘다.
        PlayerNet net = TestNets.Real();

        net.Heads.ShouldBe(StageRoster.For(TestConfigs.Stages(), 2));
        net.Baseline.Count.ShouldBe(net.Heads.Count);
        net.TrainedOn.Bots.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void 로짓은_표준화_뒤_입력_순서로_더하고_마지막_층을_빼고_ReLU_다()
    {
        // 손으로 셈한 작은 망 — 은닉 둘(하나는 음수라 ReLU 가 0 으로 자른다) · 출력 다섯. 평균 · 편차로 표준화한 뒤다.
        int n = PlayerFeatures.Names.Count;
        double[] mean = new double[n];
        double[] std = Enumerable.Repeat(1.0, n).ToArray();
        mean[0] = 1;
        std[0] = 2;
        double[] hidden0 = new double[n];
        double[] hidden1 = new double[n];
        hidden0[0] = 3;
        hidden0[1] = 1;
        hidden1[1] = -1;
        double[][] output = Enumerable.Range(0, 5).Select(h => new[] { 1.0 + h, 10.0 }).ToArray();
        string json = TestNets.Json(mean: mean, std: std, layers: [([hidden0, hidden1], [0.5, -0.25]), (output, [0, 0, 0, 0, 0.125])]);
        PlayerNet net = PlayerNet.Load(json, "시험 망").ShouldNotBeNull();
        double[] x = new double[n];
        x[0] = 5;
        x[1] = 2;

        double[] logits = net.Logits(x);

        // z0 = (5 - 1) / 2 = 2 · z1 = 2 → 은닉0 = 0.5 + 3·2 + 1·2 = 8.5 · 은닉1 = −0.25 − 2 → ReLU 0.
        logits.ShouldBe(new[] { 8.5, 17.0, 25.5, 34.0, 42.625 });
    }

    [Fact]
    public void 입력의_이름이_PlayerFeatures_와_다르면_거절한다()
    {
        // 열 순서가 계약이다(설계 §4.7) — 칸을 바꿔 학습한 망을 옛 입력으로 돌리면 틀린 칸을 읽고도 수를 낸다.
        using var log = new LogCapture();
        string[] swapped = [.. PlayerFeatures.Names];
        (swapped[0], swapped[1]) = (swapped[1], swapped[0]);

        PlayerNet.Load(TestNets.Json(features: swapped), "시험 망").ShouldBeNull();

        log.Lines.ShouldContain(l => l.StartsWith("[net][E] shape", StringComparison.Ordinal) && l.Contains("features", StringComparison.Ordinal));
    }

    [Fact]
    public void 층의_모양이_안_이어지면_거절한다()
    {
        using var log = new LogCapture();
        int n = PlayerFeatures.Names.Count;
        double[][] first = [new double[n], new double[n], new double[n]];
        double[][] wrong = Enumerable.Range(0, 5).Select(_ => new double[2]).ToArray(); // 앞 층은 셋을 낸다

        PlayerNet.Load(TestNets.Json(layers: [(first, new double[3]), (wrong, new double[5])]), "시험 망").ShouldBeNull();

        log.Lines.ShouldContain(l => l.StartsWith("[net][E] shape", StringComparison.Ordinal) && l.Contains("layer=1", StringComparison.Ordinal));
    }

    [Fact]
    public void 편차가_0_이하거나_기저율의_수가_머리와_다르면_거절한다()
    {
        using var log = new LogCapture();
        double[] std = Enumerable.Repeat(1.0, PlayerFeatures.Names.Count).ToArray();
        std[3] = 0;

        PlayerNet.Load(TestNets.Json(std: std), "시험 망").ShouldBeNull();
        PlayerNet.Load(TestNets.Json(baseline: [0, 0, 0]), "시험 망").ShouldBeNull();

        log.Lines.Count(l => l.StartsWith("[net][E] shape", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Fact]
    public void 필수_키가_빠지면_빠진_키를_말하고_멈춘다()
    {
        // 게임은 network.json 을 필수 데이터로 읽는다(설계 §5.6) — 빠진 키는 모양이 아니라 데이터 손상이라 부팅이 멈춘다(DataException).
        Should.Throw<DataException>(() => PlayerNet.Load(TestNets.Json(omit: "baseline"), "시험 망")).Message.ShouldContain("baseline");
    }
}
