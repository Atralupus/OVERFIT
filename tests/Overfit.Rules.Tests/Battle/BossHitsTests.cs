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

    [Fact]
    public void 판정의_답은_단계의_키에서_오고_없으면_받는다()
    {
        // 설계 §8.1 — 판정의 답 dash · guard · parry 는 없으면 태그대로(받는다)이고, 적으면 그 판정만 좁힌다. 판을 세울 때 판정에 싣는다 —
        // 규칙(HitResolver)과 관측(BossSwings.BuildEvent)이 같은 값을 읽게.
        HitBox?[] hits = BossHits.Of(new PatternDef
        {
            Tags = TestConfigs.Sweep(100, 0).Tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0.5, Kind = "active", Band = new double[] { 0, 1920, 0, 60 }, Damage = 5, ActiveSeconds = 0.125 },
                new()
                {
                    T = 1.0, Kind = "active", Band = new double[] { 0, 1920, 0, 60 }, Damage = 5, ActiveSeconds = 0.125,
                    Dash = false, Guard = false, Parry = false,
                },
                new() { T = 1.5, Kind = "end" },
            },
        }, TestConfigs.HitShapes());

        HitBox open = hits[0]!.Value;
        (open.Dashable, open.Guardable, open.Parryable).ShouldBe((true, true, true), "답이 없는 판정이 무언가를 안 받는다");
        HitBox closed = hits[1]!.Value;
        (closed.Dashable, closed.Guardable, closed.Parryable).ShouldBe((false, false, false), "적은 답이 판정에 안 실렸다");
    }

    [Fact]
    public void 관측의_대시_가드_패리_가능은_판정의_답이다()
    {
        // 설계 §7.3 — 대시 · 가드 · 패리의 "고를 수 있었나" 는 판정 단위다. 태그에서 가져오면 한 패턴 안에서 1타는 다 되고 잡기는 점프만
        // 되는 자리(1타 잡기)에서 거짓을 싣는다 — 대시 의존도의 분모가 "대시로는 못 피하는 판정" 으로 부푼다. 태그로는 셋 다 되는 패턴에서
        // 둘째 판정만 셋을 막는다. 가만히 선 파이터가 둘 다 맞는다.
        PatternTags tags = new()
        {
            DashWindow = 0.2,
            DashDirection = "either",
            Jumpable = true,
            AntiAir = false,
            Parryable = true,
            ParryWindow = 0.18,
            PunishGreed = false,
            Reach = "far",
            MultiHit = 2,
            Tracking = false,
        };
        var pattern = new PatternDef
        {
            Tags = tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0.5, Kind = "active", Band = new double[] { 0, 2000, 0, 60 }, Damage = 5, ActiveSeconds = 0.125 },
                new()
                {
                    T = 1.0, Kind = "active", Band = new double[] { 0, 2000, 0, 60 }, Damage = 5, ActiveSeconds = 0.125,
                    Dash = false, Guard = false, Parry = false,
                },
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

        sim.Events.Count.ShouldBe(2, "두 판정의 관측이 안 섰다");
        sim.Events.Select(e => (e.DashAvailable, e.GuardAvailable, e.ParryAvailable))
            .ShouldBe(new[] { (true, true, true), (false, false, false) }, "관측이 판정의 답이 아니라 태그를 실었다");
    }

    [Theory]
    [InlineData(FighterAction.Guard)]
    [InlineData(FighterAction.Dash)]
    public void 판정_보기의_몸통_색은_이_틱에_대_본_판정의_답을_본다(FighterAction action)
    {
        // 설계 §6.1 — 판정 보기가 칠하는 몸통 색(BattleSim.FighterDefense)은 이 틱에 대 본 판정(BossSwings.TestedBox)의 답까지 본다. 가드를 안
        // 받는 판정에 ↓ 를 붙든 채 맞은 틱에도 파이터는 가드 중이고, 대시를 안 받는 판정에 무적 창 한가운데서 맞은 틱에도 무적이다 — 판정을
        // 안 넘기면 그 틱의 색이 "가드" · "무적" 이라 맞은 몸에 거짓말을 한다. HitResolverTests.실효_방어는_판정의_답도_본다 는 Effective 만 보고,
        // 여기는 판정 보기가 실제로 읽는 자리를 본다.
        var pattern = new PatternDef
        {
            Tags = TestConfigs.Sweep(100, 0).Tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0, Kind = "windup" },
                new()
                {
                    T = 0.5, Kind = "active", Band = new double[] { 0, 2000, 0, 60 }, Damage = 5, ActiveSeconds = 0.125,
                    Dash = action == FighterAction.Dash ? false : null,
                    Guard = action == FighterAction.Guard ? false : null,
                },
                new() { T = 1.0, Kind = "end" },
            },
        };
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 0.2),
            PatternIds = new[] { "하나" },
            Patterns = new Dictionary<string, PatternDef> { ["하나"] = pattern },
            Seed = 1,
            MaxTicks = 60 * 10,
        });

        // 가드는 판이 설 때부터 붙든다. 대시는 창이 열리기 한 틱 앞에 누른다 — 창의 첫 틱이 무적 창 한가운데다.
        var hold = new InputFrame(0, false, false, false, false, GuardHeld: action == FighterAction.Guard);
        for (int i = 0; i < 600 && sim.NextActiveIn is null or > 2 * BattleSim.Dt; i++)
        {
            sim.Tick(hold);
        }

        sim.Tick(action == FighterAction.Dash ? new InputFrame(0, false, Dash: true, false, false) : hold);
        for (int i = 0; i < 10 && sim.Events.Count == 0; i++)
        {
            sim.Tick(hold);
        }

        sim.Events.Single().Verdict.ShouldBe(HitVerdict.Hit, "답이 막은 수단이 판정을 받았다");
        (action == FighterAction.Guard ? sim.Fighter.Guarding : sim.Fighter.Invulnerable)
            .ShouldBeTrue("맞은 틱에 파이터 쪽 수단이 안 서 있다 — 이 테스트가 몸통 색을 못 가른다");
        sim.FighterDefense.ShouldBe(Defense.None, $"{action} 을 안 받는 판정에 맞은 틱의 몸통 색이 {action} 이다 — 판정의 답을 안 봤다");
    }
}
