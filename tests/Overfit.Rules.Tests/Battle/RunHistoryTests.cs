using System;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 시도와 그 기록 (#72 · 설계 §4.4). 전투 한 번이 시도 하나이고, 시도마다 시드가 새로 나온다 — 유저: "학습은 보스가 미리
/// 정해지지말고 재시도 할때마다 달라지게". 기록은 끝까지 간 시도만 싣는다.
/// </summary>
public class RunHistoryTests
{
    private static AttemptRecord Finished(int number, int stage, BattleOutcome outcome) =>
        new(number, stage, Det.Hash64(7, Det.Domain.Attempt, k1: number), outcome, Array.Empty<DodgeEvent>());

    [Fact]
    public void 시도_시드는_세션_시드와_시도_번호의_좌표다()
    {
        // 16800346292054821908 은 이 알고리즘을 파이썬으로 따로 구현해 얻었다(DetTests 의 골든 벡터와 같은 방법).
        // tools/build.sh 가 smoke · shots 에 --session-seed=51 을 넘기므로 smoke 의 첫 전투가 이 시드로 선다.
        var history = new RunHistory(51);

        (int number, ulong seed) = history.Open();

        number.ShouldBe(1);
        seed.ShouldBe(16800346292054821908UL);
        seed.ShouldBe(Det.Hash64(51, Det.Domain.Attempt, k1: 1));
        history.Open().ShouldBe((2, Det.Hash64(51, Det.Domain.Attempt, k1: 2)));
    }

    [Fact]
    public void 처음부터_하면_기록만_비우고_번호와_세션_시드는_그대로다()
    {
        // 번호가 1 로 돌아가면 한 세션 안에 같은 (세션 시드, 번호) 가 둘이 되어 로그의 seed= 가 어느 판인지 모른다.
        var history = new RunHistory(7);
        history.Open();
        history.Record(Finished(1, 1, BattleOutcome.Lose));

        history.Clear();

        history.Records.ShouldBeEmpty();
        history.SessionSeed.ShouldBe(7UL);
        history.Open().Number.ShouldBe(2);
    }

    [Fact]
    public void 기록은_끝난_시도만_붙인_순서대로_싣고_이긴_판도_붙는다()
    {
        // 2번은 끝까지 안 갔다(단계 점프 · F9 · 도중에 타이틀) — 결과가 없어 안 붙지만 번호는 이미 올랐다.
        // 1단계를 이긴 판이 곧 "1단계 기록" 이라 이긴 판도 붙는다.
        var history = new RunHistory(7);
        history.Open();
        history.Record(Finished(1, 1, BattleOutcome.Lose));
        history.Open();
        history.Open();
        history.Record(Finished(3, 1, BattleOutcome.Win));

        history.Attempts.ShouldBe(3);
        history.Records.Count.ShouldBe(2);
        history.Records[0].Number.ShouldBe(1);
        history.Records[1].ShouldBe(Finished(3, 1, BattleOutcome.Win));
    }
}
