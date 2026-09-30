using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 시도 기록의 한 줄 (#112 · 설계 2026-09-28 §6.5 · 2026-09-29 조각1 §4.3) — 디스크의 <c>user://attempts/&lt;세션 시드&gt;.jsonl</c> 이 이 한 줄씩이다.
/// 되살리기(<see cref="Replay"/>)의 재료라 <b>그대로 되읽혀야</b> 한다 — double 은 비트까지, 입력은 칸 하나까지.
/// </summary>
public class AttemptLogTests
{
    private static readonly DodgeEvent[] _events =
    [
        new("엇박 3연격", DodgeVerb.Parry, HitVerdict.Hit, 0.1 + 0.2, -1, true, 123.456789012345, true, true, false, true, true),
        new("잡기", DodgeVerb.Dash, HitVerdict.Grabbed, -0.05000000000000002, 1, false, 0, false, false, true, false, false),
        new("빠른 3연격", DodgeVerb.Guard, HitVerdict.GuardBroken, 0, 0, false, 1e-300, false, true, true, false, true),
    ];

    private static AttemptEntry Entry() => new(
        SessionSeed: ulong.MaxValue - 7,
        Run: 3,
        Record: new AttemptRecord(12, 2, 16800346292054821908UL, BattleOutcome.Lose, _events),
        PickerId: "uniform",
        Plans: [new PlanEntry(0.8, "잡기", null, null), new PlanEntry(0.1 + 0.2, "3연격", 1.3, "돌진", Run: true), new PlanEntry(1.2, "잡기", null, null)],
        Ticks: 2345);

    private static void ShouldMatch(AttemptEntry back, AttemptEntry entry)
    {
        (back.SessionSeed, back.Run, back.PickerId, back.Ticks).ShouldBe((entry.SessionSeed, entry.Run, entry.PickerId, entry.Ticks));
        (back.Record.Number, back.Record.Stage, back.Record.Seed, back.Record.Outcome)
            .ShouldBe((entry.Record.Number, entry.Record.Stage, entry.Record.Seed, entry.Record.Outcome));
        back.Record.Events.SequenceEqual(entry.Record.Events).ShouldBeTrue("관측이 되읽히지 않았다");
        back.Plans.ShouldBe(entry.Plans);
        back.DataSha256.ShouldBe(entry.DataSha256);
        if (entry.Inputs is null)
        {
            back.Inputs.ShouldBeNull();
        }
        else
        {
            back.Inputs.ShouldNotBeNull().ShouldBe(entry.Inputs);
        }
    }

    [Fact]
    public void 시도를_한_줄로_쓰고_그대로_읽는다()
    {
        AttemptEntry entry = Entry();

        string line = AttemptLog.Line(entry);

        line.ShouldNotContain('\n');
        line.ShouldContain("\"picker\":\"uniform\"");
        line.ShouldContain("빠른 3연격", Case.Sensitive, "한글 id 는 그대로 싣는다 — 사람이 grep 한다");
        ShouldMatch(AttemptLog.Parse(line, "시험"), entry);
    }

    [Fact]
    public void 입력과_계획과_지문이_왕복한다()
    {
        // 되살리기의 재료 셋(설계 2026-09-29 조각1 §4.3) — 계획은 id 와 초로(명부의 칸 번호는 명부가 바뀌면 다른 동작을 가리킨다), 입력은
        // [코드, 틱 수] 칸으로, 지문은 판을 세운 데이터의 sha256 이다. 쉬기의 0.1 + 0.2 는 비트까지 돌아와야 한다(0.30000000000000004).
        const string sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        AttemptEntry entry = Entry() with { Inputs = [[32, 3], [33, 1], [95, 2]], DataSha256 = sha };

        string line = AttemptLog.Line(entry);

        line.ShouldContain("\"plans\":[{\"rest\":0.8,\"move\":\"잡기\",\"cancel\":null,\"next\":null,\"run\":false},"
            + "{\"rest\":0.30000000000000004,\"move\":\"3연격\",\"cancel\":1.3,\"next\":\"돌진\",\"run\":true}");
        line.ShouldContain($"\"data_sha256\":\"{sha}\"");
        line.ShouldEndWith("\"inputs\":[[32,3],[33,1],[95,2]]}", Case.Sensitive, "입력은 줄의 끝이다 — 가장 길어 사람이 머리를 먼저 읽는다");
        ShouldMatch(AttemptLog.Parse(line, "시험"), entry);
    }

    [Fact]
    public void 달리기가_없는_6_8_의_계획도_읽힌다()
    {
        // 6/8 의 게임이 남긴 계획에는 달리기 칸이 없다(7/8 에서 더했다) — 안 달린 계획으로 읽는다. 그때는 보스가 달리지 않았다.
        string line = AttemptLog.Line(Entry()).Replace(",\"run\":false", "", System.StringComparison.Ordinal)
            .Replace(",\"run\":true", "", System.StringComparison.Ordinal);
        line.ShouldNotContain("\"run\":true");
        line.ShouldNotContain("\"run\":false");

        AttemptLog.Parse(line, "6/8 줄").Plans.Select(p => p.Run).ShouldBe([false, false, false]);
    }

    [Fact]
    public void 입력이_없는_옛_줄도_읽힌다()
    {
        // 5/8 까지의 게임이 남긴 줄 — 계획 대신 선 동작의 순서(drawn)가 있고 입력 · 지문이 없다. 읽히되(모르는 키 drawn 은 버린다) 계획은 비고
        // 입력이 null 이다 — 되살리기가 그 줄을 NoInputs 로 가른다(ReplayTests).
        const string old = "{\"session_seed\":51,\"run\":1,\"attempt\":3,\"stage\":1,\"seed\":9131751153949564229,\"picker\":\"uniform\","
            + "\"drawn\":[\"3연격\",\"돌진\"],\"outcome\":\"win\",\"ticks\":900,\"events\":[],"
            + "\"instances\":[{\"pattern_id\":\"3연격\",\"hit\":false}]}";

        AttemptEntry back = AttemptLog.Parse(old, "옛 줄");

        (back.Record.Number, back.PickerId, back.Record.Outcome, back.Ticks).ShouldBe((3, "uniform", BattleOutcome.Win, 900));
        back.Plans.ShouldBeEmpty();
        back.Inputs.ShouldBeNull();
        back.DataSha256.ShouldBeNull();
        back.Instances.ShouldNotBeNull().ShouldBe([new PatternInstance("3연격", false)]);
    }

    [Fact]
    public void 사례를_싣고_그대로_읽는다()
    {
        // 같은 패턴이 연달아 선 두 사례가 따로 남는다 — 관측만으로는 그 경계를 모른다.
        PatternInstance[] instances = [new("잡기", true), new("잡기", false), new("3연격", false)];
        AttemptEntry entry = Entry() with { Instances = instances };

        string line = AttemptLog.Line(entry);

        line.ShouldContain("\"instances\":[{\"pattern_id\":\"잡기\",\"hit\":true},{\"pattern_id\":\"잡기\",\"hit\":false}");
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
        back.Plans.ShouldBeEmpty();
        back.Inputs.ShouldBeNull();
        back.Instances.ShouldBeNull();
    }

    [Fact]
    public void 깨진_줄은_어느_줄인지_말한다()
    {
        Should.Throw<DataException>(() => AttemptLog.Parse("{\"run\": 1}", "attempts.jsonl:7")).Message.ShouldContain("attempts.jsonl:7");
        Should.Throw<DataException>(() => AttemptLog.Parse("{깨짐", "attempts.jsonl:8")).Message.ShouldContain("attempts.jsonl:8");

        // 입력의 틀린 칸은 읽을 때 멈춘다 — 되살리기가 판 한가운데서 예외로 죽으면 어느 줄의 어느 칸인지가 안 남는다.
        // 코드는 0 ~ 191 이다 — 폭탄 칸(설계 2026-09-30 조각2 §4)이 96 위에 얹혀 96 은 이제 폭탄을 누른 선 입력이다.
        string line = AttemptLog.Line(Entry() with { Inputs = [[32, 3], [192, 1]] });
        DataException e = Should.Throw<DataException>(() => AttemptLog.Parse(line, "attempts.jsonl:9"));
        e.Message.ShouldContain("attempts.jsonl:9");
        e.Message.ShouldContain("1번 칸 [192, 1]");
    }
}
