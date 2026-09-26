using System;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 판정 창 (이슈 #59 · 설계 §3.5) — 판정은 한 틱이 아니라 <b>창</b> 동안 산다. 창 안에서 몸에 닿으면
/// 그 휘두름은 끝나고(한 번만 맞는다), 창이 닫힐 때까지 안 닿으면 관측을 하나 남긴다.
/// </summary>
public class LiveSwingTests
{
    [Fact]
    public void 창은_초를_틱으로_반올림한다()
    {
        BattleSim.TicksFor(0).ShouldBe(1, "옛 패턴(창 0)은 한 틱이다");
        BattleSim.TicksFor(0.125).ShouldBe(8, "8fps 한 장 = 7.5틱 — 반올림은 한 방향으로");
        BattleSim.TicksFor(1.0 / 6.0).ShouldBe(10);
    }

    [Fact]
    public void 대시_무적이_창_중간에_풀리면_그_뒤에_맞는다()
    {
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0.5);
        TestConfigs.UntilNear(sim);

        sim.Tick(new InputFrame(0, false, true, false, false));   // 판정이 서기 한두 틱 전에 대시가 선다
        for (int i = 0; i < 40 && sim.Events.Count == 0; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(1, "한 번 휘두르면 관측은 하나다");
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Hit, "무적 8틱이 30틱 창보다 먼저 풀렸는데 안 맞았다");
    }

    [Fact]
    public void 창이_한_틱이면_같은_대시가_피한다()
    {
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0);
        TestConfigs.UntilNear(sim);

        sim.Tick(new InputFrame(0, false, true, false, false));
        for (int i = 0; i < 5 && sim.Events.Count == 0; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(1);
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Dodged, "옛 패턴의 한 틱 판정은 그대로 피해져야 한다");
    }

    [Fact]
    public void 한_번_휘두르면_한_번만_맞는다()
    {
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0.5);
        int before = sim.Fighter.Health;
        TestConfigs.UntilFired(sim);

        for (int i = 0; i < 40; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(1);
        sim.Fighter.Health.ShouldBe(before - 7, "30틱 동안 서 있었는데 두 번 이상 맞았다");
    }

    [Fact]
    public void 창이_닫힐_때까지_안_닿으면_빗나감_하나만_남긴다()
    {
        // 사거리 50 — 파이터(480)는 보스(1440)와 960 떨어져 한 번도 안 닿는다. 창은 30틱이다:
        // 선 틱이 첫 틱이고, 그 뒤 28틱은 조용하고, 그다음 틱(30번째)에 창이 닫히며 관측이 하나 나온다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 50, activeSeconds: 0.5);
        TestConfigs.UntilFired(sim);
        sim.Events.Count.ShouldBe(0, "창이 막 섰는데 관측이 먼저 나왔다");

        for (int i = 0; i < 28; i++)
        {
            sim.Tick(default);
            sim.Events.Count.ShouldBe(0, $"창이 살아 있는데({i + 2}틱째) 관측이 먼저 나왔다");
        }

        sim.Tick(default);   // 30번째 틱 — 창이 닫힌다
        sim.Events.Count.ShouldBe(1);
        sim.Events[0].Verdict.ShouldBe(HitVerdict.MissedTooFar);
    }

    [Fact]
    public void 패턴이_끝나는_틱에_선_판정도_제_이름으로_남는다()
    {
        // end 가 active 와 같은 0.5초다. 러너가 그 틱에 끝나 _current 와 CurrentPattern 이 지워져도,
        // 판정은 낼 때 잡아 둔 태그와 이름으로 대져야 한다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 5000, activeSeconds: 0, endAt: 0.5);
        TestConfigs.UntilFired(sim);   // 판정이 선 틱 — 러너도 그 틱에 끝났다

        sim.Events.Count.ShouldBe(1);
        sim.Events[0].PatternId.ShouldBe(TestConfigs.SweepId);
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 창이_닫히기_전에_대시가_끝나도_피한_틱의_크레딧으로_남는다()
    {
        // 보스를 등지고(Facing=-1) 대시하면 무적(0.14초 = 8틱) 동안은 아직 사거리(1120) 안이라
        // Dodged 지만, 대시(0.18초 · 367px)가 등진 방향으로 계속 밀어내 곧 사거리 밖(MissedTooFar)이
        // 되고, 창(30틱)이 닫힐 때는 대시가 완전히 끝나 있다(대시는 11틱 안에 끝난다). 창이 닫히는
        // 그 틱의 라이브 DodgeCredit(대시 시각 · 방향)으로 다시 크레딧을 매기면 이미 NaN/0 이 된 뒤라
        // "0초 전에 프레임 퍼펙트로 피했다" 는 거짓 관측이 나간다(이슈 #59 · 리뷰 라운드 1) —
        // 무적이 처음 먹은 틱의 크레딧을 지어 둬야 맞다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 1120, activeSeconds: 0.5);

        sim.Tick(new InputFrame(-1, false, false, false, false));   // 보스를 등진다 (Facing = -1)
        TestConfigs.UntilNear(sim);
        sim.Tick(new InputFrame(0, false, true, false, false));     // 대시 — 등진 채라 보스 반대(밖)로 튄다

        for (int i = 0; i < 40 && sim.Events.Count == 0; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(1, "한 번 휘두르면 관측은 하나다");
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Dodged, "무적 동안 사거리 안이었다 — 대시가 끝난 뒤 사거리 밖으로 밀려났다고 미스가 되면 안 된다");
        sim.Events[0].Verb.ShouldBe(DodgeVerb.Dash);
        sim.Events[0].TimingError.ShouldBeLessThan(0, "대시는 피한 그 틱보다 먼저 시작됐다 — 창이 닫힌 틱 기준으로 다시 재면 0 이 된다");
        sim.Events[0].Direction.ShouldBe(-1, "보스를 등지고 뛰었다 — 밖이다. 창이 닫힌 뒤에 다시 재면 대시가 끝나 0 이 된다");
    }

    [Fact]
    public void 미룬_회피의_로그_줄은_관측한_틱의_공중과_거리를_찍는다()
    {
        // 미룬 Dodged 는 무적이 처음 먹은 틱에 관측을 지어 두고 창이 닫히는 틱에 확정한다(Commit) — 그 사이 몸은
        // 움직인다. 확정하는 틱의 라이브 값을 찍으면 한 줄에 두 틱이 섞인다: 관측은 "사거리 안에서 땅에서 대시로
        // 피했다" 인데 air= · dist= 는 창이 닫힐 때의 자리를 말한다. 설계 §9 의 4번(데모 로그 감사)이 [dodge] 줄을
        // 그 순간의 기하와 대조하는데, 그 기하가 이 둘이다 (이슈 #59 · 최종 리뷰). 셋업은 위 테스트와 같다 — 등지고
        // 대시해 사거리 밖으로 밀려나고, 대시가 끝난 뒤 한 번 뛰어서 창이 닫힐 때는 공중이다.
        using var log = new LogCapture(LogLevel.Info);
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 1120, activeSeconds: 0.5);

        sim.Tick(new InputFrame(-1, false, false, false, false));   // 보스를 등진다 (Facing = -1)
        TestConfigs.UntilNear(sim);
        sim.Tick(new InputFrame(0, false, true, false, false));     // 대시 — 등진 채라 보스 반대(밖)로 튄다
        for (int i = 0; i < 40 && sim.Events.Count == 0; i++)
        {
            sim.Tick(new InputFrame(0, Jump: i == 15, false, false, false));   // 대시(11틱)가 끝난 뒤 한 번 뛴다
        }

        sim.Events.Count.ShouldBe(1);
        DodgeEvent seen = sim.Events[0];
        seen.Verdict.ShouldBe(HitVerdict.Dodged);

        // 두 틱이 정말 갈리는지부터 — 안 갈리면 아래 단언은 아무것도 안 본다.
        seen.Airborne.ShouldBeFalse("땅에서 대시로 피했다");
        sim.Fighter.Grounded.ShouldBeFalse("창이 닫히는 틱에 공중이 아니다 — 셋업이 움직였다");
        $"{Math.Abs(sim.Fighter.X - sim.Boss.X):0}".ShouldNotBe($"{seen.Distance:0}",
            "창이 닫히는 틱의 거리가 관측과 같다 — 셋업이 두 틱을 못 가른다");

        string line = log.Lines.Single(l => l.StartsWith("[dodge]", StringComparison.Ordinal));
        line.ShouldContain($" air={seen.Airborne} ", Case.Sensitive, "공중이 관측이 아니라 창이 닫힌 틱의 값이다");
        line.ShouldContain($" dist={seen.Distance:0} ", Case.Sensitive, "거리가 관측이 아니라 창이 닫힌 틱의 값이다");
    }

    [Fact]
    public void 빗나감은_창이_열린_틱에서_재어_창_안에서_끝난_대시의_공으로_남는다()
    {
        // 설계 §3.6 ① — 빗나감은 창이 **열린 틱**의 이유와 수단이다. 닫히는 틱에 재던 때는 창(30틱) 안에서 대시(11틱)가 끝난
        // 사람이 Spacing 으로 적혔다 — #46 이 고친 편향이 창이 길어지며 돌아온 것이다. 사거리 1000 · 파이터는 보스에서 967 이라
        // 대시 전 몸(안끝 937)은 띠 안이고, 보스를 등지고 뛴 대시가 창이 열리기 전에 띠 밖으로 데려간다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 1000, activeSeconds: 0.5);
        sim.Tick(new InputFrame(-1, false, false, false, false));   // 보스를 등진다
        TestConfigs.UntilWindup(sim);
        while (sim.NextActiveIn is > 5 * BattleSim.Dt)
        {
            sim.Tick(default);
        }

        sim.Tick(new InputFrame(0, false, Dash: true, false, false));   // 칼이 서기 4틱 전의 대시
        for (int i = 0; i < 60 && sim.Events.Count == 0; i++)
        {
            sim.Tick(default);
        }

        sim.Fighter.Action.ShouldNotBe(FighterAction.Dash, "창이 닫힐 때 대시가 아직 돈다 — 이 테스트가 끝난 대시를 안 본다");
        DodgeEvent e = sim.Events.Single();
        e.Verdict.ShouldBe(HitVerdict.MissedTooFar);
        e.Verb.ShouldBe(DodgeVerb.Dash, "창 안에서 끝난 대시가 간격의 공이 됐다 — 빗나감을 닫히는 틱에 쟀다");
        e.Direction.ShouldBe(-1);
        e.TimingError.ShouldBe(-4 * BattleSim.Dt, 1e-9, "오차의 기준이 칼이 선 틱이 아니다");
    }

    [Fact]
    public void 칼이_선_뒤에_시작한_수단은_양의_오차로_남는다()
    {
        // 설계 §3.6 ① — 오차의 기준은 결과와 무관하게 창이 열린 틱이다. 창이 여러 틱을 살므로 칼이 선 **뒤에** 시작한 수단이 생기고,
        // 그것은 양수로 남는다(창이 한 틱이던 때는 구조상 양수가 없었다). 창이 열릴 때 파이터는 사거리(900) 밖이고, 다음 틱에
        // 뛰며 걸어 들어가 창 안에서 맞는다 — 맞은 판정은 그때 돌던 수단(점프)을 싣는다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 900, activeSeconds: 0.5);
        TestConfigs.UntilFired(sim);
        sim.Events.Count.ShouldBe(0, "창이 열린 틱에 닿았다 — 이 테스트가 창 안의 뒤 틱을 안 본다");

        sim.Tick(new InputFrame(1, Jump: true, false, false, false));
        for (int i = 0; i < 20 && sim.Events.Count == 0; i++)
        {
            sim.Tick(new InputFrame(1, false, false, false, false));
        }

        DodgeEvent e = sim.Events.Single();
        e.Verdict.ShouldBe(HitVerdict.Hit);
        e.Verb.ShouldBe(DodgeVerb.Jump);
        e.TimingError.ShouldBe(BattleSim.Dt, 1e-9, "칼이 선 다음 틱에 뛴 것이 양수로 안 남았다");
    }

    [Fact]
    public void 산_창은_닫힐_때까지_살아_있다고_말한다()
    {
        // 설계 §3.6 ④ — BattleSim.SwingLive. 판정이 선 틱에 NextActiveIn 은 null 이 되지만 창은 30틱을 산다 — 마지막(30번째)
        // 틱에 대 보고 닫힌다. 그래서 창의 1 ~ 29틱째 뒤에는 살아 있고 30틱째 뒤에는 없다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 50, activeSeconds: 0.5);
        sim.SwingLive.ShouldBeFalse();
        TestConfigs.UntilFired(sim);
        sim.NextActiveIn.ShouldBeNull("판정이 선 틱인데 아직 다음 판정이라고 한다");

        for (int i = 1; i < 30; i++)
        {
            sim.SwingLive.ShouldBeTrue($"창 {i}틱째 뒤인데 산 창이 없다고 한다");
            sim.Tick(default);
        }

        sim.SwingLive.ShouldBeFalse("창이 닫혔는데 산 창이 있다고 한다");
    }

    [Fact]
    public void 판이_끝날_때_열린_창은_관측을_안_남기고_로그만_남긴다()
    {
        // 설계 §3.6 ③ — 판을 끝낸 그 한 대는 닿은 것이라 관측이 있고, 남은 창에는 결과가 없다. 지어내면 그 한 줄이 시도 기록으로 간다.
        // 판정은 42틱에 서고 창은 30틱인데 판은 50틱에 시간이 다 된다.
        using var log = new LogCapture();
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 50, activeSeconds: 0.5, maxTicks: 50);
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(default);
        }

        outcome.ShouldBe(BattleOutcome.Lose);
        sim.Ticks.ShouldBe(50);
        sim.Events.ShouldBeEmpty("판이 끝날 때 열린 창이 관측을 지어냈다");
        log.Lines.ShouldContain($"[boss][D] cut_swing id={TestConfigs.SweepId} tick=50 reason=end");
    }

    [Fact]
    public void 패리를_못_받는_산_판정_앞의_패리는_실효_방어가_아니다()
    {
        // 설계 §6.1 — 판정 보기의 몸통 색은 산 판정에 대한 **실효** 상태다. 산 판정이 없으면 파이터 쪽 상태 그대로다.
        // Sweep 은 패리를 못 받는다 — 점프 공격의 착지(설계 §4.2)가 이 모양이다.
        BattleSim idle = TestConfigs.SweepSim(maxDistance: 50, activeSeconds: 0.5);
        idle.Tick(new InputFrame(0, false, false, Parry: true, false));
        idle.SwingLive.ShouldBeFalse();
        idle.FighterDefense.ShouldBe(Defense.Parrying, "산 판정이 없는데 파이터의 패리 창을 안 칠한다");

        BattleSim sim = TestConfigs.SweepSim(maxDistance: 50, activeSeconds: 0.5);
        TestConfigs.UntilFired(sim);
        sim.Tick(new InputFrame(0, false, false, Parry: true, false));
        sim.SwingLive.ShouldBeTrue();
        sim.Fighter.Parrying.ShouldBeTrue("파이터 쪽 창이 안 열렸다 — 이 테스트가 실효를 못 가른다");
        sim.FighterDefense.ShouldBe(Defense.None, "패리를 못 받는 판정 앞에서 패리 창이라고 칠한다");
    }

    [Fact]
    public void 닿아서_끝난_판정의_틱에도_몸통_색은_그_판정에_대한_실효_방어다()
    {
        // 설계 §6.1 · Review Focus 1 — 착지 띠(패리 불가)는 땅에 선 몸에 **첫 틱에** 닿아 그 틱에 끝난다. 판정 보기는 그 틱에 대 본
        // 사각형을 그리므로 몸통 색도 같은 판정을 봐야 한다: 산 판정만 보면 닿아 끝난 바로 그 틱에 색이 파이터 쪽 패리 창(노랑)으로
        // 돌아간다. 사거리 2000 의 Sweep(패리 불가)이 판정 3틱 전에 K 를 누른 파이터에게 첫 틱에 닿는다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 2000, activeSeconds: 0.5);
        TestConfigs.UntilWindup(sim);
        while (sim.NextActiveIn is > 3 * BattleSim.Dt)
        {
            sim.Tick(default);
        }

        sim.Tick(new InputFrame(0, false, false, Parry: true, false));
        for (int i = 0; i < 10 && sim.Events.Count == 0; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Single().Verdict.ShouldBe(HitVerdict.Hit, "패리를 못 받는 판정을 받아쳤다");
        sim.SwingLive.ShouldBeFalse("닿은 판정이 아직 산다 — 이 테스트가 닿아 끝난 틱을 안 본다");
        sim.BossTestedRects.ShouldNotBeEmpty("닿은 틱에 대 본 사각형이 없다 — 판정 보기가 그 틱에 아무것도 안 그린다");
        sim.Fighter.Parrying.ShouldBeTrue("파이터 쪽 창이 닫혔다 — 이 테스트가 실효를 못 가른다");
        sim.FighterDefense.ShouldBe(Defense.None, "닿아 끝난 판정의 틱에 패리 창이라고 칠한다 — 그 틱에 그린 띠와 다른 말을 한다");
    }
}
