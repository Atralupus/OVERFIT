using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>관측의 칸 표 (설계 2026-10-01 조각4 §2) — 칸의 자리가 계약이다. 바꾸면 다시 학습한다.</summary>
public class BossObservationTests
{
    private static readonly int _f = Enum.GetValues<FighterAction>().Length;

    private static FighterSnapshot Fighter(double x, FighterAction action = FighterAction.Idle) =>
        new(x, 30, 1, action, false, 110, 7, 0.5, 0);

    private static ObservationInput Input(int moveIndex = -1, IReadOnlyList<BombFlight>? bombs = null) => new(
        DecisionPoint.Rest, 1920,
        BossX: 1440, BossY: 60, BossFacing: -1, BossHealth: 900, BossMaxHealth: 1200, Form: 2, FormCount: 3, PoiseRatio: 0.25, Exhausted: false, Shifting: false,
        Travel: TravelState.Retreat, MoveIndex: moveIndex, MoveProgress: 0.5, TicksToCancel: moveIndex < 0 ? -1 : 30,
        Fighter: Fighter(480, FighterAction.Guard), Recent: Enumerable.Range(0, BossObservation.Recent).Select(i => Fighter(480, (FighterAction)(i % _f))).ToArray(),
        FighterMaxHealth: 220, Bombs: bombs ?? []);

    [Fact]
    public void 칸_수는_표의_합이다()
    {
        // 지점 6 · 보스 10 · 움직임 4 · 동작 N + 3 · 파이터 8 + F · 최근 K × F · 폭탄 M × 3.
        var o = new BossObservation(7);
        o.Size.ShouldBe(6 + 10 + 4 + (7 + 3) + (8 + _f) + (BossObservation.Recent * _f) + (BossObservation.Items * 3));
        (BossObservation.Recent, BossObservation.Items).ShouldBe((8, 4));
    }

    [Fact]
    public void 칸의_자리와_정규화가_표대로다()
    {
        var o = new BossObservation(7);
        double[] v = o.Encode(Input(moveIndex: 2, bombs: [new BombFlight(800, 0, 0.25)]));
        v.Length.ShouldBe(o.Size);

        // 지점: 쉬기는 둘째 칸.
        v[..6].ShouldBe(new double[] { 0, 1, 0, 0, 0, 0 });
        // 보스: x/W · y/300 · 보는 쪽 · 체력 · 형태 원핫(2) · 게이지 · 탈진 · 전환.
        v[6..16].ShouldBe(new[] { 0.75, 0.2, -1, 0.75, 0, 1, 0, 0.25, 0, 0 });
        // 움직임: 물러서기.
        v[16..20].ShouldBe(new double[] { 0, 0, 1, 0 });
        // 동작: 없음 + 명부 7 원핫(칸 2) · 진행 · 캔슬까지(30/60).
        v[20..28].ShouldBe(new double[] { 0, 0, 0, 1, 0, 0, 0, 0 });
        (v[28], v[29]).ShouldBe((0.5, 0.5));
        // 파이터: (480 − 1440)/1920 · 30/300 · 보는 쪽 · 보스 쪽을 보나 · 체력 · 스태미나 · 폭탄 · 던지기 · 행동 원핫(가드).
        int f = 30;
        v[f..(f + 8)].ShouldBe(new[] { -0.5, 0.1, 1, 1, 0.5, 0.5, 0.7, 0 });
        v[(f + 8)..(f + 8 + _f)].ShouldBe(Enumerable.Range(0, _f).Select(i => i == (int)FighterAction.Guard ? 1.0 : 0.0).ToArray());
        // 최근 K: i 번째 모습의 행동이 i % F.
        int r = f + 8 + _f;
        for (int i = 0; i < BossObservation.Recent; i++)
        {
            v[(r + (i * _f))..(r + ((i + 1) * _f))].Sum().ShouldBe(1);
            v[r + (i * _f) + (i % _f)].ShouldBe(1);
        }

        // 폭탄: 첫 칸만 — 있음 · 난 몫 · (800 − 1440)/1920.
        int b = r + (BossObservation.Recent * _f);
        v[b..(b + 3)].ShouldBe(new[] { 1, 0.25, -1.0 / 3 }, 1e-12);
        v[(b + 3)..].ShouldAllBe(x => x == 0);
    }

    [Fact]
    public void 동작이_없으면_없음_칸이고_캔슬까지는_0_이다()
    {
        double[] v = new BossObservation(7).Encode(Input());
        (v[20], v[28], v[29]).ShouldBe((1.0, 0.0, 0.0));
    }

    [Fact]
    public void 폭탄은_넷까지만_싣는다()
    {
        var o = new BossObservation(7);
        double[] v = o.Encode(Input(bombs: Enumerable.Range(0, 6).Select(i => new BombFlight(100 * i, 0, 0.1)).ToArray()));
        v.Length.ShouldBe(o.Size);
    }
}
