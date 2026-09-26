using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 보스 판정을 판을 세울 때 짓는 일 (#72 · 설계 §8.1 · §7.3) — 모양을 찾고, 점프로 넘을 수 있는지를 이 판의 파이터로 잰다.
/// </summary>
public class BossHitsTests
{
    [Fact]
    public void 실제_캐릭터의_점프는_착지_띠_위에_53틱_1타_궤적_위에_27틱_있다()
    {
        // 설계 §4.2 · §4.1 의 산수 그대로다 — 이산 점프(jump_velocity 1220 · 중력 2400 · 1/60초)는 누른 틱부터 60틱 떠 있고,
        // 발이 60 위에 있는 것은 53틱(누른 틱 + 3 ~ + 55), 236.5(3연격 1타 궤적의 윗끝) 위는 27틱(+ 16 ~ + 42)이다. 정점은
        // 정확히 300 이라 346.5(2타)는 한 틱도 못 넘는다. 이 숫자들이 스펙의 점프 누름 창(46틱 · 20틱)을 떠받친다.
        FighterConfig c = TestConfigs.Fighters().Values.Single();

        BossHits.TicksAbove(c, 60).ShouldBe(53);
        BossHits.TicksAbove(c, 236.5).ShouldBe(27);
        BossHits.TicksAbove(c, 300).ShouldBe(0, "정점이 300 을 넘는다");
        BossHits.TicksAbove(c, 346.5).ShouldBe(0);
    }

    [Fact]
    public void 점프_가능은_발이_창_내내_윗끝_위에_있을_수_있을_때만_참이다()
    {
        // 설계 §7.3 — "점프 한 번으로 창 내내 발이 모양 위에 있을 수 있나" = 윗끝 위의 틱 수 ≥ 창의 틱 수. 정점이 윗끝을 넘는
        // 것만으로는 모자라다: 창이 길면 발이 내려와 닿는다. 같은 높이 60 띠를 창 8틱과 창 60틱(53틱보다 길다)으로 잰다.
        FighterConfig c = TestConfigs.Fighters().Values.Single();
        Dictionary<string, HitShape> shapes = TestConfigs.HitShapes();

        HitBox Hit(double activeSeconds) => BossHits.Of(new PatternDef
        {
            Tell = TestConfigs.Tell(),
            Tags = TestConfigs.Sweep(100, 0).Tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0.5, Kind = "active", Band = new double[] { 0, 1920, 0, 60 }, Damage = 12, ActiveSeconds = activeSeconds },
                new() { T = 2.0, Kind = "end" },
            },
        }, shapes, c)[0]!.Value;

        Hit(0.125).Jumpable.ShouldBeTrue("창 8틱 동안 발이 60 위에 있을 수 있는데 못 넘는다고 한다");
        Hit(1.0).Jumpable.ShouldBeFalse("창 60틱은 발이 60 위에 있는 53틱보다 긴데 넘는다고 한다");
    }

    [Fact]
    public void 관측의_점프_가능은_태그가_아니라_그_판정에서_온다()
    {
        // 설계 §7.3 — 태그 jumpable 은 패턴 단위라 한 패턴 안에서 판정마다 답이 다르면 거짓을 싣는다(3연격은 1타만 넘는다).
        // 이 패턴은 태그가 jumpable: true 인데, 1타는 높이 60 띠(기준 파이터가 창 8틱 내내 넘는다)이고 2타는 400 띠(정점이 못 닿는다)다.
        // 가만히 선 파이터가 둘 다 맞는다 — 관측의 JumpAvailable 은 판정마다 참 · 거짓이다. 태그를 실으면 둘 다 참이다.
        var pattern = new PatternDef
        {
            Tell = TestConfigs.Tell(),
            Tags = new PatternTags
            {
                DashWindow = 0.2,
                DashDirection = "either",
                Jumpable = true,
                AntiAir = false,
                Parryable = false,
                ParryWindow = 0,
                PunishGreed = false,
                Reach = "far",
                Feint = false,
                MultiHit = 2,
                Tracking = false,
                HasGuardBreak = false,
            },
            Timeline = new List<PatternStep>
            {
                new() { T = 0.5, Kind = "active", Band = new double[] { 0, 2000, 0, 60 }, Damage = 5, ActiveSeconds = 0.125 },
                new() { T = 1.0, Kind = "active", Band = new double[] { 0, 2000, 0, 400 }, Damage = 5, ActiveSeconds = 0.125 },
                new() { T = 1.5, Kind = "end" },
            },
        };
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 0.2),
            PatternIds = new[] { "둘" },
            Patterns = new Dictionary<string, PatternDef> { ["둘"] = pattern },
            Seed = 1,
            MaxTicks = 60 * 10,
        });

        for (int i = 0; i < 180 && sim.Events.Count < 2; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Select(e => e.Verdict).ShouldBe(new[] { HitVerdict.Hit, HitVerdict.Hit }, "가만히 선 파이터가 두 판정을 다 맞지 않았다");
        sim.Events.Select(e => e.JumpAvailable).ShouldBe(new[] { true, false }, "관측의 점프 가능이 판정이 아니라 태그를 실었다");
    }
}
