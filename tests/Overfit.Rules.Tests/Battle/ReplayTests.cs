using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 되살리기 (설계 2026-09-29 조각1 §4.4) — 저장한 입력으로 판 <b>전체</b>를 다시 세운다. 계획 전부 · 틱 수 · 결과 · 관측이 같아야 한다. 봇이 싸운
/// 판을 기록 한 줄로 쓰고 읽어 되살린다 — 게임이 디스크에 남기고 데모가 읽는 길 그대로다(<c>Battle</c> → <see cref="AttemptLog"/> → <c>BattleDemo</c>).
/// </summary>
public class ReplayTests
{
    /// <summary>함대 봇의 판에 끊긴 계획이 든 시드 — 캔슬의 되살리기까지 본다(<see cref="봇이_싸운_판을_줄로_쓰고_읽어_다시_돌리면_같은_판이다"/> 가 확인한다).</summary>
    private const ulong _seed = 7;

    private const string _sha = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string _otherSha = "2222222222222222222222222222222222222222222222222222222222222222";

    /// <summary>
    /// 함대 봇이 한 판을 끝까지 싸우고, 게임처럼 <see cref="BattleSim.Tick"/> 에 넘긴 입력을 모아 기록 한 줄을 짓는다. 돌려주는 줄은 <b>쓰고 읽은</b>
    /// 것이다 — 디스크를 거친 값으로 되살린다.
    /// </summary>
    private static (AttemptEntry Entry, BattleSim Sim) Fight(ulong seed, IReadOnlyList<ScriptPlan>? script = null)
    {
        BattleSim sim = FleetPlay.Sim(1, seed, script);
        FleetBot bot = FleetPlay.Bot(FleetPlay.Mid, seed);
        var tape = new InputTape();
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            InputFrame input = bot.Next(sim);
            tape.Add(input);
            outcome = sim.Tick(input);
        }

        var entry = new AttemptEntry(
            SessionSeed: seed,
            Run: 1,
            Record: new AttemptRecord(1, 1, seed, outcome.Value, [.. sim.Events]),
            PickerId: script is null ? "uniform" : "script",
            Plans: sim.PlanEntries,
            Ticks: sim.Ticks,
            Inputs: tape.Runs,
            DataSha256: _sha);
        return (AttemptLog.Parse(AttemptLog.Line(entry), "시험"), sim);
    }

    [Fact]
    public void 봇이_싸운_판을_줄로_쓰고_읽어_다시_돌리면_같은_판이다()
    {
        (AttemptEntry entry, BattleSim sim) = Fight(_seed);
        sim.Cancels.Count.ShouldBeGreaterThan(0, "끊긴 계획이 없는 판이다 — 캔슬의 되살리기를 못 본다(시드를 바꿔라)");
        sim.Events.Count.ShouldBeGreaterThan(0);

        BattleSim again = Replay.Run(FleetPlay.Setup(1, entry.Record.Seed), entry.Inputs.ShouldNotBeNull());

        (again.Ticks, again.Result).ShouldBe((sim.Ticks, sim.Result));
        again.PlanEntries.ShouldBe(sim.PlanEntries);
        again.Drawn.ShouldBe(sim.Drawn);
        ReplayGoldenTests.Digest(again.Events).ShouldBe(ReplayGoldenTests.Digest(sim.Events));
        Replay.Verdict(entry, again, _sha).ShouldBe(ReplayVerdict.Match);
    }

    [Fact]
    public void 대본으로_선_시도는_기록된_계획이_대본이다()
    {
        // 게임의 대본은 돌며 되풀이된다 — 기록은 그 판에서 고른 계획을 고른 만큼 싣는다. 되살리기는 그 목록을 대본으로 세운다: 같은 판이면 같은
        // 번호에서 같은 계획을 고르므로 기록 한 바퀴로 판 전체가 선다. 캔슬 지점은 초로 적혀 있어 지금 정의의 칸으로 되찾는다(Review Focus 4).
        ScriptPlan[] script = [new(0.4, "3연격", 0, "돌진", Run: true), new(1.2, "잡기"), new(0.8, "엇박 3연격", 1, "올려베기", Run: true)];
        (AttemptEntry entry, BattleSim sim) = Fight(_seed, script);
        sim.Plans.Count.ShouldBeGreaterThan(script.Length, "대본이 한 바퀴를 안 돌았다");

        IReadOnlyList<ScriptPlan>? rebuilt = Replay.Script(entry.Plans, FleetPlay.Roster(1), FleetPlay.Patterns, out string? problem);

        problem.ShouldBeNull();
        rebuilt.ShouldNotBeNull().Take(script.Length).ShouldBe(script);
        BattleSim again = Replay.Run(FleetPlay.Setup(1, entry.Record.Seed, rebuilt), entry.Inputs.ShouldNotBeNull());
        again.PlanEntries.ShouldBe(sim.PlanEntries);
        Replay.Verdict(entry, again, _sha).ShouldBe(ReplayVerdict.Match);
    }

    [Fact]
    public void 대본을_못_세우면_까닭을_말한다()
    {
        // 기록의 동작이나 캔슬 지점이 지금 데이터에 없다 — 데이터가 바뀌었다. 판을 세우지 않고 까닭을 돌려준다(데모가 지문으로 [W] · [E] 를 가른다).
        PlanEntry[] plans = [new(0.8, "3연격", 9.99, "돌진")];

        Replay.Script(plans, FleetPlay.Roster(1), FleetPlay.Patterns, out string? problem).ShouldBeNull();
        problem.ShouldBe("0번 계획 — 3연격 에 9.99초 캔슬 지점이 없다");

        Replay.Script([new(0.8, "없는 동작", null, null)], FleetPlay.Roster(1), FleetPlay.Patterns, out problem).ShouldBeNull();
        problem.ShouldNotBeNull().ShouldContain("없는 동작");

        Replay.Script([], FleetPlay.Roster(1), FleetPlay.Patterns, out problem).ShouldBeNull();
        problem.ShouldBe("기록에 계획이 없다");
    }

    [Fact]
    public void 입력이_없는_줄은_NoInputs_다()
    {
        // 5/8 까지의 게임이 남긴 줄은 입력이 없다 — 읽히되(AttemptLogTests) 판을 되살릴 수 없다(Review Focus 3). 같은 시드로 선 판이 우연히 같아
        // 보여도 일치라 하지 않는다: 입력 없이 선 판은 그 줄의 판이 아니다.
        (AttemptEntry entry, _) = Fight(_seed);
        AttemptEntry old = entry with { Inputs = null, DataSha256 = null };

        Replay.Verdict(old, Replay.Run(FleetPlay.Setup(1, _seed), []), _sha).ShouldBe(ReplayVerdict.NoInputs);
        Replay.Verdict(old, Replay.Run(FleetPlay.Setup(1, _seed), entry.Inputs.ShouldNotBeNull()), _sha).ShouldBe(ReplayVerdict.NoInputs);
    }

    [Fact]
    public void 지문이_다르면_DataChanged_다()
    {
        // balance.json 의 max_ticks 를 줄였다 — 기록의 판이 끝나기 전에 시간이 다 된다. 판이 다른 까닭이 데이터에 있을 수 있으면(지문이 다르면)
        // DataChanged([W]), 지문이 같은데 다르면 결정론이 깨진 것이다(Mismatch · [E]).
        (AttemptEntry entry, _) = Fight(_seed);
        IReadOnlyList<int[]> inputs = entry.Inputs.ShouldNotBeNull();
        BattleSetup shorter = FleetPlay.Setup(1, _seed);
        shorter.MaxTicks = entry.Ticks / 2;

        BattleSim cut = Replay.Run(shorter, inputs);

        (cut.Ticks, cut.Result).ShouldBe((entry.Ticks / 2, BattleOutcome.Lose));
        Replay.Verdict(entry, cut, _otherSha).ShouldBe(ReplayVerdict.DataChanged);
        Replay.Verdict(entry, cut, _sha).ShouldBe(ReplayVerdict.Mismatch);

        // 데이터가 바뀌어도 판이 같으면 일치다 — 지문은 판이 다를 때 그 까닭을 가를 뿐이다.
        Replay.Verdict(entry, Replay.Run(FleetPlay.Setup(1, _seed), inputs), _otherSha).ShouldBe(ReplayVerdict.Match);
    }

    [Fact]
    public void 입력이_다하면_거기서_멈춘다()
    {
        // 판이 안 끝났으면 결과가 없다 — 기록의 결과와 견주면 다르다. 끝까지 간 기록의 입력은 판이 끝나는 틱에 꼭 다한다.
        BattleSim sim = Replay.Run(FleetPlay.Setup(1, _seed), [[32, 60]]);

        (sim.Ticks, sim.Result).ShouldBe((60, (BattleOutcome?)null));
    }
}
