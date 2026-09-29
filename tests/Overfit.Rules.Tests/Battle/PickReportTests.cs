using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 결과 화면의 패턴 리포트 (#122 · 설계 2026-09-29 조각1 §4.5). 옛 망이 걷혀 확률 줄이 없다 — 머리 · 이 판의 회피 · 패턴마다 나온 횟수와 맞은 횟수다.
/// </summary>
public class PickReportTests
{
    private static readonly string[] _roster = ["3연격", "점프 공격", "엇박 3연격"];

    private static DodgeEvent Event(DodgeVerb verb) =>
        new("3연격", verb, HitVerdict.Dodged, 0, 1, false, 100, false, true, true, true, true);

    private static List<DodgeEvent> Events(params (DodgeVerb Verb, int Count)[] counts) =>
        [.. counts.SelectMany(c => Enumerable.Repeat(Event(c.Verb), c.Count))];

    [Fact]
    public void 리포트는_무작위_머리와_이_판의_회피와_패턴마다_나옴_맞음이다()
    {
        IReadOnlyList<string> lines = PickReport.Lines(
            "uniform",
            _roster,
            Events((DodgeVerb.Dash, 15), (DodgeVerb.Guard, 10), (DodgeVerb.Parry, 5)),
            ["3연격", "엇박 3연격", "3연격"],
            [new("3연격", true), new("엇박 3연격", true), new("3연격", false)]);

        lines.ShouldBe(
        [
            "보스가 패턴을 무작위로 골랐습니다",
            "이 판의 회피 30건: 대시 15 · 가드 10 · 패리 5",
            "3연격 — 2번 나옴 · 1번 맞음",
            "점프 공격 — 안 나옴",
            "엇박 3연격 — 1번 나옴 · 1번 맞음",
        ]);
    }

    [Fact]
    public void 대본으로_선_판은_그렇다고_적는다()
    {
        // GIF · 스크린샷 · 순회의 판은 대본(script)으로 선다 — 무작위라고 적으면 리포트가 거짓말을 한다.
        PickReport.Lines("script", _roster, [], ["3연격"], [])[0].ShouldBe("보스가 정해진 대본대로 골랐습니다");
    }

    [Fact]
    public void 회피가_없으면_그렇다고_적는다()
    {
        PickReport.Lines("uniform", _roster, [], [], [])[1].ShouldBe("이 판의 회피가 없습니다");
    }

    [Fact]
    public void 회피는_많은_순이고_같으면_대시_점프_패리_가드_거리_무대응_순이다()
    {
        List<DodgeEvent> events = Events((DodgeVerb.None, 1), (DodgeVerb.Spacing, 2), (DodgeVerb.Guard, 1), (DodgeVerb.Jump, 2));

        PickReport.Lines("uniform", _roster, events, [], [])[1].ShouldBe("이 판의 회피 6건: 점프 2 · 거리 2 · 가드 1 · 무대응 1");
    }
}
