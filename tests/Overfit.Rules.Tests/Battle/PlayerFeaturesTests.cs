using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 망의 입력 한 자리 (#108 · 설계 2026-09-28 §4.7). 공장과 게임이 이 한 함수를 부른다 — 둘이 따로 지으면 학습과 추론이 조용히 어긋난다.
/// </summary>
public class PlayerFeaturesTests
{
    private static DodgeEvent Event(
        DodgeVerb verb,
        HitVerdict verdict,
        double distance,
        double timingError = 0,
        int direction = 0,
        bool airborne = false,
        bool greedWindow = false,
        bool dash = true,
        bool jump = true,
        bool parry = true) =>
        new("3연격", verb, verdict, timingError, direction, airborne, distance, greedWindow, dash, jump, parry, GuardAvailable: true);

    /// <summary>
    /// 손으로 지은 관측 열 건 — <b>열아홉 칸의 값이 서로 다르게</b> 골랐다. 두 칸이 같은 값이면 그 둘을 맞바꾼 실수가 안 잡힌다.
    /// 개수: 전체 10 · 대시 3 · 점프 1 · 패리 2 · 가드 4 · 무너진 가드 0 · 점프를 고를 수 있었던 5 · 패리를 고를 수 있었던 7.
    /// </summary>
    private static readonly DodgeEvent[] _hand =
    [
        Event(DodgeVerb.Dash, HitVerdict.Dodged, 100, timingError: -0.05, direction: 1, greedWindow: true),
        Event(DodgeVerb.Dash, HitVerdict.Dodged, 120, timingError: -0.03, direction: 1, greedWindow: true),
        Event(DodgeVerb.Dash, HitVerdict.Dodged, 140, timingError: -0.01, direction: -1, jump: false),
        Event(DodgeVerb.Jump, HitVerdict.MissedByHeight, 200, timingError: 0.07, airborne: true),
        Event(DodgeVerb.Parry, HitVerdict.Parried, 30, greedWindow: true),
        Event(DodgeVerb.Parry, HitVerdict.Hit, 50, jump: false),
        Event(DodgeVerb.Guard, HitVerdict.Guarded, 60),
        Event(DodgeVerb.Guard, HitVerdict.Guarded, 70, dash: false, jump: false, parry: false),
        Event(DodgeVerb.Guard, HitVerdict.Guarded, 80, jump: false, parry: false),
        Event(DodgeVerb.Guard, HitVerdict.Guarded, 90, dash: false, parry: false),
    ];

    private static AttemptRecord Record(int number, params DodgeEvent[] events) =>
        new(number, 1, (ulong)number, BattleOutcome.Lose, events);

    [Fact]
    public void 이름은_열아홉_개이고_스펙의_순서다()
    {
        // 열 순서가 계약이다 — network.json 의 features 가 이 이름들을 같은 순서로 싣고 불러올 때 대 본다(설계 §4.7 · §5.6).
        PlayerFeatures.Names.ShouldBe(
        [
            "dash_timing_bias",
            "dash_timing_var",
            "dash_direction_bias",
            "jump_timing_bias",
            "jump_reliance",
            "airborne_at_impact",
            "parry_rate",
            "parry_reliance",
            "greed",
            "distance_bias",
            "guard_rate",
            "samples",
            "dash_samples",
            "jump_samples",
            "parry_samples",
            "guard_samples",
            "guard_broken_samples",
            "jump_choice_samples",
            "parry_choice_samples",
        ]);
    }

    [Fact]
    public void 축과_개수를_제_칸에_싣는다()
    {
        double[] features = PlayerFeatures.From(PlayerAxes.From(_hand));

        // 기대값은 축의 정의에서 손으로 셈했다 — PlayerAxes 를 다시 부르면 칸을 맞바꾼 실수가 기대값에도 같이 들어간다.
        var expected = new Dictionary<string, double>
        {
            ["dash_timing_bias"] = -0.03,
            ["dash_timing_var"] = (0.0004 + 0 + 0.0004) / 3,
            ["dash_direction_bias"] = 1.0 / 3,
            ["jump_timing_bias"] = 0.07,
            ["jump_reliance"] = 1.0 / 5,
            ["airborne_at_impact"] = 1.0 / 10,
            ["parry_rate"] = 1.0 / 2,
            ["parry_reliance"] = 2.0 / 7,
            ["greed"] = 3.0 / 10,
            ["distance_bias"] = 94,
            ["guard_rate"] = 4.0 / 10,
            ["samples"] = 10,
            ["dash_samples"] = 3,
            ["jump_samples"] = 1,
            ["parry_samples"] = 2,
            ["guard_samples"] = 4,
            ["guard_broken_samples"] = 0,
            ["jump_choice_samples"] = 5,
            ["parry_choice_samples"] = 7,
        };

        features.Length.ShouldBe(PlayerFeatures.Names.Count);
        for (int i = 0; i < features.Length; i++)
        {
            string name = PlayerFeatures.Names[i];
            features[i].ShouldBe(expected[name], 1e-12, $"{i}번 칸 {name}");
        }

        // 손 관측이 칸을 정말 가르나 — 같은 값이 둘이면 이 테스트가 그 둘의 맞바꿈을 못 본다.
        features.Distinct().Count().ShouldBe(features.Length, "손 관측의 칸 값이 겹친다 — 관측을 다시 고르세요");
    }

    [Fact]
    public void 기록은_붙인_순서로_이어_접는다()
    {
        // 기록마다 축을 내 평균하지 않는다 — 관측을 이어 한 번에 접는다. 순서도 붙인 그대로라 합의 순서까지 같다(비트까지).
        AttemptRecord first = Record(1, _hand[..4]);
        AttemptRecord second = Record(2, _hand[4..]);

        double[] joined = PlayerFeatures.From([first, second]);

        joined.ShouldBe(PlayerFeatures.From(PlayerAxes.From(_hand)));
    }

    [Fact]
    public void 기록이_없으면_전부_0()
    {
        // 첫 시도는 기록이 없다 — NaN 이 아니라 0 이어야 표준화 뒤에도 망의 입력이 선다(PlayerAxes 와 같은 약속).
        double[] features = PlayerFeatures.From(Array.Empty<AttemptRecord>());

        features.Length.ShouldBe(19);
        features.ShouldAllBe(v => v == 0);
    }
}
