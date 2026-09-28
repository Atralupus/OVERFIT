using System;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 시도 기록의 한 줄 (#112 · 설계 2026-09-28 §6.5) — 디스크의 <c>user://attempts/&lt;세션 시드&gt;.jsonl</c> 이 이 한 줄씩이다. 되살리기와 sim-to-real 의
/// 재료라 <b>그대로 되읽혀야</b> 한다 — double 은 비트까지.
/// </summary>
public class AttemptLogTests
{
    private static readonly DodgeEvent[] _events =
    [
        new("엇박 3연격", DodgeVerb.Parry, HitVerdict.Hit, 0.1 + 0.2, -1, true, 123.456789012345, true, true, false, true, true),
        new("1타 잡기", DodgeVerb.Dash, HitVerdict.Grabbed, -0.05000000000000002, 1, false, 0, false, false, true, false, false),
        new("점프 3연속", DodgeVerb.Guard, HitVerdict.GuardBroken, 0, 0, false, 1e-300, false, true, true, false, true),
    ];

    private static AttemptEntry Entry(PickDecision? decision, string? arm) => new(
        SessionSeed: ulong.MaxValue - 7,
        Run: 3,
        Record: new AttemptRecord(12, 2, 16800346292054821908UL, BattleOutcome.Lose, _events, arm),
        PickerId: "network",
        Drawn: ["1타 잡기", "점프 3연속", "1타 잡기"],
        Ticks: 2345,
        Decision: decision,
        NetworkSha256: "88b7f3823cce6c9fad6c67816c686addc3dd5d384286c5f56073c643efeb899e");

    private static void ShouldMatch(AttemptEntry back, AttemptEntry entry)
    {
        (back.SessionSeed, back.Run, back.PickerId, back.Ticks, back.NetworkSha256).ShouldBe((entry.SessionSeed, entry.Run, entry.PickerId, entry.Ticks, entry.NetworkSha256));
        (back.Record.Number, back.Record.Stage, back.Record.Seed, back.Record.Outcome, back.Record.Arm)
            .ShouldBe((entry.Record.Number, entry.Record.Stage, entry.Record.Seed, entry.Record.Outcome, entry.Record.Arm));
        back.Record.Events.SequenceEqual(entry.Record.Events).ShouldBeTrue("관측이 되읽히지 않았다");
        back.Drawn.ShouldBe(entry.Drawn);
    }

    [Fact]
    public void 망_갈래의_시도를_한_줄로_쓰고_그대로_읽는다()
    {
        var decision = new PickDecision(
            PickDecision.ModeNarrowed, PickDecision.ReasonHabit, 57, [1, 3, 4], 4, [0.1, 1.9 / 3, -0.25, 2.5, -1e-9], [0.2, 0.3, -0.4, 0.5, 1.0 / 7]);
        AttemptEntry entry = Entry(decision, "network");

        string line = AttemptLog.Line(entry);

        line.ShouldNotContain('\n');
        line.ShouldContain("\"arm\":\"network\"");
        line.ShouldContain("\"picker\":\"network\"");
        line.ShouldContain("1타 잡기", Case.Sensitive, "한글 id 는 그대로 싣는다 — 사람이 grep 한다");
        AttemptEntry back = AttemptLog.Parse(line, "시험");
        ShouldMatch(back, entry);
        PickDecision got = back.Decision.ShouldNotBeNull();
        (got.Mode, got.Reason, got.Samples, got.Breathing).ShouldBe((decision.Mode, decision.Reason, decision.Samples, decision.Breathing));
        got.Narrowed.ShouldBe(decision.Narrowed);
        got.Logits.ShouldNotBeNull().Select(BitConverter.DoubleToInt64Bits).ShouldBe(decision.Logits!.Select(BitConverter.DoubleToInt64Bits));
        got.Lifts.ShouldNotBeNull().Select(BitConverter.DoubleToInt64Bits).ShouldBe(decision.Lifts!.Select(BitConverter.DoubleToInt64Bits));
    }

    [Fact]
    public void 무작위_갈래와_갈래_없는_단계도_그대로_읽는다()
    {
        foreach (string? arm in new[] { "uniform", null })
        {
            AttemptEntry entry = Entry(null, arm);

            AttemptEntry back = AttemptLog.Parse(AttemptLog.Line(entry), "시험");

            ShouldMatch(back, entry);
            back.Decision.ShouldBeNull();
        }
    }

    [Fact]
    public void 시도를_시작할_때의_입력과_사례를_싣고_그대로_읽는다()
    {
        // sim-to-real(#114 · 설계 §7.3)의 재료 — 입력 19칸과 사례의 라벨을 게임이 공장과 같은 코드로 한 번 짓는다. 파이썬이 따로 지으면 두 벌이 갈린다.
        // 같은 패턴이 연달아 선 두 사례가 따로 남는다 — 관측만으로는 그 경계를 모른다.
        double[] features = [.. Enumerable.Range(0, PlayerFeatures.Names.Count).Select(i => ((i + 0.1) / 3) - 2)];
        PatternInstance[] instances = [new("1타 잡기", true), new("1타 잡기", false), new("3연격", false)];
        AttemptEntry entry = Entry(null, "uniform") with { Features = features, Instances = instances };

        string line = AttemptLog.Line(entry);

        line.ShouldContain("\"instances\":[{\"pattern_id\":\"1타 잡기\",\"hit\":true},{\"pattern_id\":\"1타 잡기\",\"hit\":false}");
        AttemptEntry back = AttemptLog.Parse(line, "시험");
        ShouldMatch(back, entry);
        back.Features.ShouldNotBeNull().Select(BitConverter.DoubleToInt64Bits).ShouldBe(features.Select(BitConverter.DoubleToInt64Bits));
        back.Instances.ShouldNotBeNull().ShouldBe(instances);
    }

    [Fact]
    public void 입력과_사례가_없는_옛_줄도_읽는다()
    {
        // #113 의 게임이 남긴 줄에는 둘이 없다 — 읽히고 null 이다. sim-to-real 은 그런 시도를 건너뛰고 몇 개인지 적는다.
        const string old = "{\"session_seed\":51,\"run\":1,\"attempt\":2,\"stage\":2,\"seed\":9131751153949564229,\"picker\":\"network\",\"arm\":\"uniform\","
            + "\"drawn\":[\"3연격\"],\"outcome\":\"lose\",\"ticks\":120,\"events\":[],\"network_sha256\":\"88b7f3823cce6c9fad6c67816c686addc3dd5d384286c5f56073c643efeb899e\"}";

        AttemptEntry back = AttemptLog.Parse(old, "옛 줄");

        (back.Record.Number, back.Record.Arm, back.Record.Outcome).ShouldBe((2, "uniform", BattleOutcome.Lose));
        back.Features.ShouldBeNull();
        back.Instances.ShouldBeNull();
    }

    [Fact]
    public void 깨진_줄은_어느_줄인지_말한다()
    {
        Should.Throw<DataException>(() => AttemptLog.Parse("{\"run\": 1}", "attempts.jsonl:7")).Message.ShouldContain("attempts.jsonl:7");
        Should.Throw<DataException>(() => AttemptLog.Parse("{깨짐", "attempts.jsonl:8")).Message.ShouldContain("attempts.jsonl:8");
    }
}
