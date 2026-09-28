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
    public void 깨진_줄은_어느_줄인지_말한다()
    {
        Should.Throw<DataException>(() => AttemptLog.Parse("{\"run\": 1}", "attempts.jsonl:7")).Message.ShouldContain("attempts.jsonl:7");
        Should.Throw<DataException>(() => AttemptLog.Parse("{깨짐", "attempts.jsonl:8")).Message.ShouldContain("attempts.jsonl:8");
    }
}
