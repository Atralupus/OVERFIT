using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 보스 판정을 판을 세울 때 짓는 일 (#72 · 설계 §8.1 · §7.3) — 모양을 찾는다. 점프로 넘을 수 있는지는 판정이 아니라 자리의 것이라
/// 창이 열릴 때 잰다(#85 · <c>JumpClearanceTests</c>).
/// </summary>
public class BossHitsTests
{
    [Fact]
    public void 관측의_점프_가능은_태그가_아니라_그_판정에서_온다()
    {
        // 설계 §7.3 — 태그 jumpable 은 패턴 단위라 한 패턴 안에서 판정마다 답이 다르면 거짓을 싣는다(3연격의 2타는 등 뒤에서 못 넘는다).
        // 이 패턴은 태그가 jumpable: true 인데, 1타는 높이 60 띠(기준 파이터가 창 8틱 내내 넘는다)이고 2타는 400 띠(정점이 못 닿는다)다.
        // 가만히 선 파이터가 둘 다 맞는다 — 관측의 JumpAvailable 은 판정마다 참 · 거짓이다. 태그를 실으면 둘 다 참이다.
        var pattern = new PatternDef
        {
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
                MultiHit = 2,
                Tracking = false,
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
