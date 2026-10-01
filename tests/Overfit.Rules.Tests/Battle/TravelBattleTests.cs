using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 판 위의 움직임 — 물러서기 · 넘어 뛰기 · 뒤로 뛰기 (설계 2026-10-01 조각3 §0). 대본 조종기(<see cref="ScriptActions"/>)로 칸을 못박는다. 판은 파이터 480 ·
/// 보스 1440 에서 서고 첫 쉬기 결정은 12틱이다. 빠르기 840 은 틱당 14px 다.
/// </summary>
public class TravelBattleTests
{
    private static readonly InputFrame _bomb = new(0, false, false, false, false, Bomb: true);

    private sealed class Recording(IBossController inner) : IBossController
    {
        public List<BossDecision> Seen { get; } = new();

        public bool ReactsToBombs => inner.ReactsToBombs;

        public int Decide(BossDecision decision)
        {
            Seen.Add(decision);
            return inner.Decide(decision);
        }
    }

    private static IReadOnlyList<string> Roster => StageRoster.For(TestConfigs.Stages(), 1);

    private static BattleSim Sim(IBossController controller, int? startHealth = null) => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighters()[TestConfigs.Balance().Battle.Fighter],
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(),
        BossStartHealth = startHealth,
        PatternIds = Roster,
        Patterns = TestConfigs.Patterns(),
        Seed = 51,
        Controller = controller,
        MaxTicks = TestConfigs.MaxTicks(),
    });

    private static Recording Script(params string[] actions) => new(new ScriptActions(new BossActions(Roster), actions));

    private static int Crouch => BattleSim.TicksFor(TestConfigs.Boss().Movement.LeapCrouchSeconds);

    private static int Air => BattleSim.TicksFor(TestConfigs.Boss().Movement.LeapAirSeconds);

    [Fact]
    public void 물러서기는_고른_틱부터_틱당_14px_멀어지고_파이터를_본다()
    {
        Recording c = Script("retreat", "retreat");
        BattleSim sim = Sim(c);
        TestConfigs.UntilTick(sim, 35);
        (sim.Boss.X, sim.Boss.Facing).ShouldBe((1440 + (14 * 24), -1));

        // 결정은 그 틱의 걸음 뒤다(다가가기의 달리기와 같다) — 36틱의 걸음까지 가고 기다리기를 고른 뒤로는 안 움직인다.
        TestConfigs.UntilTick(sim, 37);
        sim.Boss.X.ShouldBe(1440 + (14 * 25), "기다리기를 고른 뒤에도 물러섰다");
        c.Seen.Select(d => (d.Point, d.Sight.Tick)).Skip(1).Take(3).ShouldBe(new[]
        {
            (DecisionPoint.Rest, 12), (DecisionPoint.Retreat, 24), (DecisionPoint.Retreat, 36),
        });
    }

    [Fact]
    public void 아레나_끝에_닿으면_그_틱에_묻고_다가가기와_물러서기를_가린다()
    {
        // 1440 에서 395 를 가면 끝(1835)이다 — 틱당 14 로 29번째 걸음(12 + 28 = 40틱)에 닿는다.
        Recording c = Script("retreat", "retreat", "retreat");
        BattleSim sim = Sim(c);
        TestConfigs.UntilTick(sim, 60);

        BossDecision arrived = c.Seen.First(d => d.Point == DecisionPoint.Arrived);
        arrived.Sight.Tick.ShouldBe(40);
        sim.Boss.X.ShouldBe(1835);
        (arrived.Mask[BossActions.Approach], arrived.Mask[BossActions.Retreat]).ShouldBe((false, false));
    }

    [Fact]
    public void 넘어_뛰기는_웅크린_뒤_파이터_너머_몸_간격에_내리고_착지가_자유로워짐이다()
    {
        Recording c = Script("leap_over");
        BattleSim sim = Sim(c);
        int land = 12 + Crouch + Air;
        TestConfigs.UntilTick(sim, 12 + Crouch - 1);
        (sim.Boss.X, sim.Boss.Y).ShouldBe((1440.0, 0.0), "웅크리는 동안 움직였다");
        TestConfigs.UntilTick(sim, 12 + Crouch + (Air / 2));
        sim.Boss.Y.ShouldBeGreaterThan(200);
        TestConfigs.UntilTick(sim, land);

        (sim.Boss.X, sim.Boss.Y).ShouldBe((480.0 - 115, 0.0));
        BossDecision freed = c.Seen.Last(d => d.Point == DecisionPoint.Freed);
        freed.Sight.Tick.ShouldBe(land);
        sim.Events.ShouldBeEmpty("점프가 판정을 남겼다");
    }

    [Fact]
    public void 뒤로_뛰기는_파이터에게서_600_자기_쪽에_파이터를_본_채_내린다()
    {
        // 보스 1440 · 파이터 480 — 자기 쪽 600 은 1080 이다(보스가 파이터 쪽으로 온다 — 거리만 600 으로 맞춘다).
        BattleSim sim = Sim(Script("leap_back"));
        TestConfigs.UntilTick(sim, 12 + Crouch + Air);
        (sim.Boss.X, sim.Boss.Y, sim.Boss.Facing).ShouldBe((1080.0, 0.0, -1));
    }

    [Fact]
    public void 뛰는_중에_전환하면_높이만_따라_내리고_착지의_자유로워짐은_전환이_끝날_때다()
    {
        // 930 에서 1틱에 던진 폭탄은 120틱에 떨어진다. 보스는 기다리기 일곱(12 ~ 84) 뒤 96틱에 뒤로 뛰어 111 ~ 147틱에 떠 있다.
        Recording c = Script([.. Enumerable.Repeat("wait", 7), "leap_back"]);
        BattleSim sim = Sim(c, startHealth: 930);
        sim.Tick(_bomb);
        TestConfigs.UntilTick(sim, 120);

        sim.Forms.Shifting.ShouldBeTrue("폭탄이 문턱을 안 넘겼다");
        sim.Boss.Y.ShouldBeGreaterThan(0, "땅에서 전환했다 — 이 테스트가 공중을 안 본다");
        int shiftEnd = 120 + BattleSim.TicksFor(TestConfigs.Boss().Forms.ShiftSeconds);
        TestConfigs.UntilTick(sim, shiftEnd - 1);
        sim.Boss.Y.ShouldBe(0, "전환 안에 안 내렸다");
        c.Seen.Where(d => d.Point == DecisionPoint.Freed).Select(d => d.Sight.Tick).ShouldBe(new[] { 0 }, "공중에서 끊긴 점프가 착지의 자유로워짐을 냈다");
        TestConfigs.UntilTick(sim, shiftEnd);
        c.Seen[^1].Point.ShouldBe(DecisionPoint.Freed);
    }

    [Fact]
    public void 대본_조종기는_자유로워짐에_기다리고_캔슬_지점은_계속하고_다_쓰면_기다린다()
    {
        var a = new BossActions(Roster);
        var s = new ScriptActions(a, ["leap_over", "3연격"]);
        bool[] all = Enumerable.Repeat(true, a.Count).ToArray();
        BossSight sight = new(0, 1, 1440, -1, 1200, new FighterSnapshot(480, 0, 1, FighterAction.Idle, false, 220, 10), []);
        s.Decide(new BossDecision(DecisionPoint.Freed, 0, 0, all, sight, null, null)).ShouldBe(BossActions.Wait);
        s.Decide(new BossDecision(DecisionPoint.Cancel, 1, 0, all, sight, "3연격", 0)).ShouldBe(BossActions.Continue);
        s.Decide(new BossDecision(DecisionPoint.Rest, 2, 12, all, sight, null, null)).ShouldBe(BossActions.LeapOver);
        s.Decide(new BossDecision(DecisionPoint.Rest, 3, 24, all, sight, null, null)).ShouldBe(BossActions.Move(Roster.ToList().IndexOf("3연격")));
        s.Decide(new BossDecision(DecisionPoint.Rest, 4, 36, all, sight, null, null)).ShouldBe(BossActions.Wait);
    }
}
