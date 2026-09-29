using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 시도 기록의 한 줄 (#112 · 설계 2026-09-28 §6.5) — 디스크의 <c>user://attempts/&lt;세션 시드&gt;.jsonl</c> 이 이 한 줄씩이다. 되살리기의
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

    private static AttemptEntry Entry() => new(
        SessionSeed: ulong.MaxValue - 7,
        Run: 3,
        Record: new AttemptRecord(12, 2, 16800346292054821908UL, BattleOutcome.Lose, _events),
        PickerId: "uniform",
        Drawn: ["1타 잡기", "점프 3연속", "1타 잡기"],
        Ticks: 2345);

    private static void ShouldMatch(AttemptEntry back, AttemptEntry entry)
    {
        (back.SessionSeed, back.Run, back.PickerId, back.Ticks).ShouldBe((entry.SessionSeed, entry.Run, entry.PickerId, entry.Ticks));
        (back.Record.Number, back.Record.Stage, back.Record.Seed, back.Record.Outcome)
            .ShouldBe((entry.Record.Number, entry.Record.Stage, entry.Record.Seed, entry.Record.Outcome));
        back.Record.Events.SequenceEqual(entry.Record.Events).ShouldBeTrue("관측이 되읽히지 않았다");
        back.Drawn.ShouldBe(entry.Drawn);
    }

    [Fact]
    public void 시도를_한_줄로_쓰고_그대로_읽는다()
    {
        AttemptEntry entry = Entry();

        string line = AttemptLog.Line(entry);

        line.ShouldNotContain('\n');
        line.ShouldContain("\"picker\":\"uniform\"");
        line.ShouldContain("1타 잡기", Case.Sensitive, "한글 id 는 그대로 싣는다 — 사람이 grep 한다");
        ShouldMatch(AttemptLog.Parse(line, "시험"), entry);
    }

    [Fact]
    public void 사례를_싣고_그대로_읽는다()
    {
        // 같은 패턴이 연달아 선 두 사례가 따로 남는다 — 관측만으로는 그 경계를 모른다.
        PatternInstance[] instances = [new("1타 잡기", true), new("1타 잡기", false), new("3연격", false)];
        AttemptEntry entry = Entry() with { Instances = instances };

        string line = AttemptLog.Line(entry);

        line.ShouldContain("\"instances\":[{\"pattern_id\":\"1타 잡기\",\"hit\":true},{\"pattern_id\":\"1타 잡기\",\"hit\":false}");
        AttemptEntry back = AttemptLog.Parse(line, "시험");
        ShouldMatch(back, entry);
        back.Instances.ShouldNotBeNull().ShouldBe(instances);
    }

    [Fact]
    public void 옛_망의_칸이_든_줄도_읽힌다()
    {
        // 조각 1 이 옛 망을 걷기 전(#112 · #114)의 게임이 남긴 줄에는 갈래 · 망의 결정 · 망의 지문 · 입력 19칸이 있다 — 모르는 키라 버리고 읽는다.
        // 사례가 없는 #113 의 줄도 같이 본다(null).
        const string old = "{\"session_seed\":51,\"run\":1,\"attempt\":2,\"stage\":2,\"seed\":9131751153949564229,\"picker\":\"network\",\"arm\":\"uniform\","
            + "\"drawn\":[\"3연격\"],\"outcome\":\"lose\",\"ticks\":120,\"events\":[],"
            + "\"decision\":{\"mode\":\"all\",\"reason\":\"thin\",\"samples\":3,\"narrowed\":[0,1,2,3,4]},"
            + "\"network_sha256\":\"88b7f3823cce6c9fad6c67816c686addc3dd5d384286c5f56073c643efeb899e\",\"features\":[0.5,1.5]}";

        AttemptEntry back = AttemptLog.Parse(old, "옛 줄");

        (back.Record.Number, back.Record.Stage, back.PickerId, back.Record.Outcome).ShouldBe((2, 2, "network", BattleOutcome.Lose));
        back.Drawn.ShouldBe(["3연격"]);
        back.Instances.ShouldBeNull();
    }

    [Fact]
    public void 깨진_줄은_어느_줄인지_말한다()
    {
        Should.Throw<DataException>(() => AttemptLog.Parse("{\"run\": 1}", "attempts.jsonl:7")).Message.ShouldContain("attempts.jsonl:7");
        Should.Throw<DataException>(() => AttemptLog.Parse("{깨짐", "attempts.jsonl:8")).Message.ShouldContain("attempts.jsonl:8");
    }
}
