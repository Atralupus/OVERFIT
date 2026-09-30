using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 결과 화면의 리포트 (#122 · 설계 2026-09-29 조각1 §4.5). 옛 망이 걷혀 확률 줄이 없다 — 머리(계획 수 · 캔슬 수) · 이 판의 회피 · 동작마다 나온
/// 횟수와 맞은 횟수 · 캔슬마다 한 줄이다.
/// </summary>
public class PickReportTests
{
    private static readonly string[] _roster = ["3연격", "점프 공격", "엇박 3연격", "돌진", "잡기", "올려베기"];

    private static DodgeEvent Event(DodgeVerb verb) =>
        new("3연격", verb, HitVerdict.Dodged, 0, 1, false, 100, false, true, true, true, true);

    private static List<DodgeEvent> Events(params (DodgeVerb Verb, int Count)[] counts) =>
        [.. counts.SelectMany(c => Enumerable.Repeat(Event(c.Verb), c.Count))];

    [Fact]
    public void 리포트는_머리와_이_판의_회피와_동작마다_나옴_맞음이다()
    {
        IReadOnlyList<string> lines = PickReport.Lines(
            "uniform",
            _roster,
            3,
            [],
            Events((DodgeVerb.Dash, 15), (DodgeVerb.Guard, 10), (DodgeVerb.Parry, 5)),
            ["3연격", "엇박 3연격", "3연격"],
            [new("3연격", true), new("엇박 3연격", true), new("3연격", false)]);

        lines.ShouldBe(
        [
            "보스가 계획을 무작위로 골랐습니다 — 계획 3개 · 캔슬 0번",
            "이 판의 회피 30건: 대시 15 · 가드 10 · 패리 5",
            "3연격 — 2번 나옴 · 1번 맞음",
            "점프 공격 — 안 나옴",
            "엇박 3연격 — 1번 나옴 · 1번 맞음",
            "돌진 — 안 나옴",
            "잡기 — 안 나옴",
            "올려베기 — 안 나옴",
        ]);
    }

    [Fact]
    public void 리포트의_머리는_계획_수와_캔슬_수다()
    {
        // 계획은 고른 수(끝나지 않은 마지막 계획도 든다) · 캔슬은 실제로 끊은 수다 — 탈진으로 못 쓴 캔슬은 안 센다(설계 §3.2).
        (string From, string To)[] cancels = [("3연격", "돌진"), ("엇박 3연격", "잡기")];

        PickReport.Lines("uniform", _roster, 7, cancels, [], [], [])[0].ShouldBe("보스가 계획을 무작위로 골랐습니다 — 계획 7개 · 캔슬 2번");
    }

    [Fact]
    public void 캔슬마다_한_줄이다()
    {
        // 같은 짝은 한 줄로 센다 — 많은 순, 같으면 명부 순(끊은 동작 · 이은 동작). 동작마다의 줄 뒤에 선다.
        (string From, string To)[] cancels =
        [
            ("엇박 3연격", "올려베기"),
            ("3연격", "돌진"),
            ("3연격", "잡기"),
            ("3연격", "돌진"),
        ];

        IReadOnlyList<string> lines = PickReport.Lines("uniform", _roster, 9, cancels, [], ["3연격", "돌진"], []);

        lines.Skip(2 + _roster.Length).ShouldBe(["3연격 → 돌진 — 2번", "3연격 → 잡기 — 1번", "엇박 3연격 → 올려베기 — 1번"]);
    }

    [Fact]
    public void 대본으로_선_판은_그렇다고_적는다()
    {
        // GIF · 스크린샷 · 순회의 판은 대본(script)으로 선다 — 무작위라고 적으면 리포트가 거짓말을 한다.
        PickReport.Lines("script", _roster, 2, [], [], ["3연격"], [])[0].ShouldBe("보스가 정해진 대본대로 골랐습니다 — 계획 2개 · 캔슬 0번");
    }

    [Fact]
    public void 회피가_없으면_그렇다고_적는다()
    {
        PickReport.Lines("uniform", _roster, 1, [], [], [], [])[1].ShouldBe("이 판의 회피가 없습니다");
    }

    [Fact]
    public void 회피는_많은_순이고_같으면_대시_점프_패리_가드_거리_무대응_순이다()
    {
        List<DodgeEvent> events = Events((DodgeVerb.None, 1), (DodgeVerb.Spacing, 2), (DodgeVerb.Guard, 1), (DodgeVerb.Jump, 2));

        PickReport.Lines("uniform", _roster, 1, [], events, [], [])[1].ShouldBe("이 판의 회피 6건: 점프 2 · 거리 2 · 가드 1 · 무대응 1");
    }
}
