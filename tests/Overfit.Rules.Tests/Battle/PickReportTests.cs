using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 2단계 결과 화면의 패턴 리포트 (#122). 판은 <see cref="StageRoster.Setup"/> 으로 세운다 — 게임이 세우는 그 자리라 갈래와 결정이 게임과 같다. 망은
/// <see cref="TestNets"/> 의 작은 망이다(가중치 0 · 로짓이 곧 출력 편향).
/// </summary>
public class PickReportTests
{
    private static readonly string[] _roster = TestNets.Heads;

    private static Dictionary<string, StageDef> Stages() => new()
    {
        ["1"] = new() { Want = 2, Patterns = new[] { "3연격", "점프 공격" }, Picker = "uniform" },
        ["2"] = new() { Want = 5, Patterns = _roster, Picker = NetworkPicker.Id },
    };

    /// <summary>대시 · 가드 · 패리를 섞은 기록 하나 — 관측 <c>dash + guard + parry</c> 건.</summary>
    private static AttemptRecord[] Mixed(int dash, int guard, int parry)
    {
        var events = new List<DodgeEvent>();
        events.AddRange(Enumerable.Repeat(Event(DodgeVerb.Dash), dash));
        events.AddRange(Enumerable.Repeat(Event(DodgeVerb.Guard), guard));
        events.AddRange(Enumerable.Repeat(Event(DodgeVerb.Parry), parry));
        return [new AttemptRecord(1, 1, 7, BattleOutcome.Win, events)];
    }

    private static DodgeEvent Event(DodgeVerb verb) =>
        new("3연격", verb, HitVerdict.Dodged, 0, 1, false, 100, false, true, true, true, true);

    private static NetworkContext Net(double[] logits, double[]? baseline = null, int share = 70) =>
        new(TestNets.Constant(logits, baseline), TestNets.Knobs(share: share));

    private static IReadOnlyList<string> Report(
        NetworkContext network, AttemptRecord[] prior, string arm, IReadOnlyList<string> drawn, IReadOnlyList<PatternInstance> instances)
    {
        StageSetup setup = StageRoster.Setup(Stages(), 2, 51, prior, network: network, arm: arm).ShouldNotBeNull();
        return PickReport.Lines(setup, network, prior, drawn, instances);
    }

    [Fact]
    public void 좁힌_시도는_패턴마다_맞을_확률과_고른_결과와_나온_횟수를_적는다()
    {
        // 들어 올림 1.2(엇박) · 0.9(점프 3연속)가 겨냥, 남은 칸 중 로짓이 가장 낮은 돌진(−0.3)이 숨통. 잡기(0.5)도 문턱을 넘지만 겨냥은 많아야 둘이다.
        IReadOnlyList<string> lines = Report(
            Net([0.1, 0.9, -0.3, 0.5, 1.2]),
            Mixed(dash: 15, guard: 10, parry: 5),
            NetworkPicker.Id,
            ["엇박 3연격", "점프 3연속", "엇박 3연격", "1타 돌진", "엇박 3연격", "점프 3연속"],
            [new("엇박 3연격", true), new("점프 3연속", false), new("엇박 3연격", true), new("1타 돌진", false), new("엇박 3연격", false), new("점프 3연속", true)]);

        lines.ShouldBe(
        [
            "보스가 회피 기록을 읽고 패턴을 골랐습니다",
            "이 시도 전까지의 회피 30건: 대시 15 · 가드 10 · 패리 5",
            "겨냥: 평균보다 확실히 더 맞을 패턴(최대 2개) · 숨통: 겨냥 밖에서 가장 덜 맞을 패턴",
            "3연격 — 맞을 확률 52% (평균 50%) → 안 씀",
            "점프 3연속 — 맞을 확률 71% (평균 50%) → 겨냥 · 2번 나옴 · 1번 맞음",
            "1타 돌진 — 맞을 확률 43% (평균 50%) → 숨통 · 1번 나옴 · 0번 맞음",
            "1타 잡기 — 맞을 확률 62% (평균 50%) → 안 씀",
            "엇박 3연격 — 맞을 확률 77% (평균 50%) → 겨냥 · 3번 나옴 · 2번 맞음",
        ]);
    }

    [Fact]
    public void 평균은_기저율의_로짓에서_낸다()
    {
        // 기저율의 로짓 −1 → 27%. 로짓 0 → 50% 는 들어 올림 1 이라 겨냥이다.
        IReadOnlyList<string> lines = Report(
            Net([0, -1, -1, -1, -1], baseline: [-1, -1, -1, -1, -1]), Mixed(30, 0, 0), NetworkPicker.Id, ["3연격"], [new("3연격", false)]);

        lines.ShouldContain("3연격 — 맞을 확률 50% (평균 27%) → 겨냥 · 1번 나옴 · 0번 맞음");
    }

    [Fact]
    public void 무작위_갈래는_비교용_몫과_나온_횟수만_적는다()
    {
        IReadOnlyList<string> lines = Report(
            Net([0.1, 0.9, -0.3, 0.5, 1.2]),
            Mixed(dash: 15, guard: 10, parry: 5),
            StageRoster.UniformArm,
            ["3연격", "1타 잡기", "3연격"],
            [new("3연격", true), new("1타 잡기", true), new("3연격", false)]);

        lines.ShouldBe(
        [
            "이번 시도는 무작위로 골랐습니다 (비교용 30%)",
            "이 시도 전까지의 회피 30건: 대시 15 · 가드 10 · 패리 5",
            "3연격 — 2번 나옴 · 1번 맞음",
            "점프 3연속 — 안 나옴",
            "1타 돌진 — 안 나옴",
            "1타 잡기 — 1번 나옴 · 1번 맞음",
            "엇박 3연격 — 안 나옴",
        ]);
    }

    [Fact]
    public void 근거가_얇으면_그_까닭과_나온_횟수만_적는다()
    {
        IReadOnlyList<string> lines = Report(Net([0, 3, 0, 0, 0]), Mixed(dash: 8, guard: 4, parry: 0), NetworkPicker.Id, ["점프 3연속"], []);

        lines[0].ShouldBe("회피 기록이 12건뿐이라(20건 미만) 5개 패턴을 모두 썼습니다");
        lines[1].ShouldBe("이 시도 전까지의 회피 12건: 대시 8 · 가드 4");
        lines.ShouldContain("점프 3연속 — 1번 나옴 · 0번 맞음");
        lines.ShouldNotContain(l => l.Contains("맞을 확률", System.StringComparison.Ordinal));
    }

    [Fact]
    public void 도드라진_칸이_없으면_확률은_적되_고른_결과는_안_적는다()
    {
        IReadOnlyList<string> lines = Report(Net([0.1, 0.4, -2, 0.3, 0]), Mixed(30, 0, 0), NetworkPicker.Id, ["1타 돌진"], [new("1타 돌진", true)]);

        lines[0].ShouldBe("평균보다 두드러지게 맞을 패턴이 없어 5개 패턴을 모두 썼습니다");
        lines.ShouldContain("1타 돌진 — 맞을 확률 12% (평균 50%) · 1번 나옴 · 1번 맞음");
        lines.ShouldContain("3연격 — 맞을 확률 52% (평균 50%) · 안 나옴");
    }

    [Fact]
    public void 기록이_없으면_그렇다고_적는다()
    {
        // 디버그 키로 2단계에 곧장 가면 기록이 없다.
        IReadOnlyList<string> lines = Report(Net([0, 0, 0, 0, 0]), [], NetworkPicker.Id, [], []);

        lines[1].ShouldBe("이 시도 전까지의 회피 기록이 없습니다");
    }

    [Fact]
    public void 회피는_많은_순이고_같으면_대시_점프_패리_가드_거리_무대응_순이다()
    {
        var events = new List<DodgeEvent>
        {
            Event(DodgeVerb.None), Event(DodgeVerb.Spacing), Event(DodgeVerb.Spacing), Event(DodgeVerb.Guard), Event(DodgeVerb.Jump), Event(DodgeVerb.Jump),
        };
        AttemptRecord[] prior = [new AttemptRecord(1, 1, 7, BattleOutcome.Win, events)];

        IReadOnlyList<string> lines = Report(Net([0, 0, 0, 0, 0]), prior, NetworkPicker.Id, [], []);

        lines[1].ShouldBe("이 시도 전까지의 회피 6건: 점프 2 · 거리 2 · 가드 1 · 무대응 1");
    }

    [Fact]
    public void 동전이_없는_단계는_리포트가_없다()
    {
        StageSetup setup = StageRoster.Setup(Stages(), 1, 51, []).ShouldNotBeNull();

        PickReport.Lines(setup, Net([0, 0, 0, 0, 0]), [], ["3연격"], []).ShouldBeEmpty();
    }
}
