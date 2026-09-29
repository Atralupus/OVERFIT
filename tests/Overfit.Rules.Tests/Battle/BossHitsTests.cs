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
        BattleSim sim = TestConfigs.PatternSim("둘", pattern);

        for (int i = 0; i < 180 && sim.Events.Count < 2; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Select(e => e.Verdict).ShouldBe(new[] { HitVerdict.Hit, HitVerdict.Hit }, "가만히 선 파이터가 두 판정을 다 맞지 않았다");
        sim.Events.Select(e => e.JumpAvailable).ShouldBe(new[] { true, false }, "관측의 점프 가능이 판정이 아니라 태그를 실었다");
    }

    [Fact]
    public void 손으로_적은_사각형은_그대로_판정이_된다()
    {
        // 설계 2026-09-29 조각1 §2.1 — 올려베기의 판정은 그림에서 뽑지 않고 손으로 적는다(rects · 공격자 기준 [x0, x1, y0, y1] · hitboxes.json 과
        // 같은 좌표). hitbox · band 에 이은 셋째 모양이고, 판을 세울 때 그대로 판정의 사각형이 된다.
        HitBox?[] hits = BossHits.Of(Rects([0, 396, 0, 360], [-20, 0, 100, 200]), TestConfigs.HitShapes());

        hits[0].ShouldNotBeNull().Shape.Local.ShouldBe(new[] { new HitRect(0, 396, 0, 360), new HitRect(-20, 0, 100, 200) });
        hits[0]!.Value.Damage.ShouldBe(14);
    }

    [Fact]
    public void 손으로_적은_사각형이_네_수가_아니거나_뒤집혔으면_판을_못_세운다()
    {
        // 사람이 손으로 적는 수라 틀리면 판을 세울 때 멈춘다 — 모양이 없는 판정과 같은 길(모은 뒤 한 번에 거절)이다.
        Should.Throw<System.ArgumentException>(() => BossHits.Of(Rects([396, 0, 0, 360]), TestConfigs.HitShapes()))
            .Message.ShouldContain("rects");
        Should.Throw<System.ArgumentException>(() => BossHits.Of(Rects([0, 396, 0]), TestConfigs.HitShapes()))
            .Message.ShouldContain("rects");
    }

    /// <summary>판정 하나(피해 14)가 손으로 적은 <paramref name="rects"/> 로 치는 패턴.</summary>
    private static PatternDef Rects(params double[][] rects) => new()
    {
        Tags = TestConfigs.Sweep(100, 0).Tags,
        Timeline = new List<PatternStep>
        {
            new() { T = 0.5, Kind = "active", Rects = rects, Damage = 14, ActiveSeconds = 0.125 },
            new() { T = 1.0, Kind = "end" },
        },
    };

    [Fact]
    public void 판정의_답은_단계의_키에서_오고_없으면_받는다()
    {
        // 설계 §8.1 — 판정의 답 dash · guard · parry 는 없으면 태그대로(받는다)이고, 적으면 그 판정만 좁힌다. 판을 세울 때 판정에 싣는다 —
        // 규칙(HitResolver)과 관측(BossSwings.BuildEvent)이 같은 값을 읽게. 판정마다 답을 **하나씩만** 닫는다 — 셋을 한꺼번에 닫으면
        // 어느 키가 어느 칸으로 가는지를 못 가른다(parry 키가 Dashable 로 가는 배선이 셋 다 닫힌 판정에서는 똑같이 거짓이다).
        HitBox?[] hits = BossHits.Of(new PatternDef
        {
            Tags = TestConfigs.Sweep(100, 0).Tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0.5, Kind = "active", Band = new double[] { 0, 1920, 0, 60 }, Damage = 5, ActiveSeconds = 0.125 },
                new() { T = 1.0, Kind = "active", Band = new double[] { 0, 1920, 0, 60 }, Damage = 5, ActiveSeconds = 0.125, Dash = false },
                new() { T = 1.5, Kind = "active", Band = new double[] { 0, 1920, 0, 60 }, Damage = 5, ActiveSeconds = 0.125, Guard = false },
                new() { T = 2.0, Kind = "active", Band = new double[] { 0, 1920, 0, 60 }, Damage = 5, ActiveSeconds = 0.125, Parry = false },
                new() { T = 2.5, Kind = "end" },
            },
        }, TestConfigs.HitShapes());

        hits.Take(4).Select(h => (h!.Value.Dashable, h.Value.Guardable, h.Value.Parryable)).ShouldBe(new[]
        {
            (true, true, true),
            (false, true, true),
            (true, false, true),
            (true, true, false),
        }, "단계의 키가 판정의 제 칸에 안 실렸다 — 답이 없으면 받고, 적은 키는 그 수단 하나만 닫는다");
    }

    [Fact]
    public void 관측의_대시_가드_패리_가능은_판정의_답이다()
    {
        // 설계 §7.3 — 대시 · 가드 · 패리의 "고를 수 있었나" 는 판정 단위다. 태그에서 가져오면 한 패턴 안에서 1타는 다 되고 잡기는 점프만
        // 되는 자리(옛 1타 잡기 · #78)에서 거짓을 싣는다 — 대시 의존도의 분모가 "대시로는 못 피하는 판정" 으로 부푼다. 태그로는 셋 다 되는 패턴에서
        // 둘째 · 셋째 · 넷째 판정이 대시 · 가드 · 패리를 **하나씩** 막는다 — 셋을 한꺼번에 막으면 어느 답이 어느 칸으로 가는지를 못 가른다
        // (패리 가능이 Dashable 을 읽는 배선이 그대로 통과했다). 가만히 선 파이터가 넷 다 맞는다.
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
            MultiHit = 4,
            Tracking = false,
        };
        var pattern = new PatternDef
        {
            Tags = tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0.5, Kind = "active", Band = new double[] { 0, 2000, 0, 60 }, Damage = 5, ActiveSeconds = 0.125 },
                new() { T = 1.0, Kind = "active", Band = new double[] { 0, 2000, 0, 60 }, Damage = 5, ActiveSeconds = 0.125, Dash = false },
                new() { T = 1.5, Kind = "active", Band = new double[] { 0, 2000, 0, 60 }, Damage = 5, ActiveSeconds = 0.125, Guard = false },
                new() { T = 2.0, Kind = "active", Band = new double[] { 0, 2000, 0, 60 }, Damage = 5, ActiveSeconds = 0.125, Parry = false },
                new() { T = 2.5, Kind = "end" },
            },
        };
        BattleSim sim = TestConfigs.PatternSim("넷", pattern);

        for (int i = 0; i < 300 && sim.Events.Count < 4; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(4, "네 판정의 관측이 안 섰다");
        sim.Events.Select(e => e.Verdict).ShouldAllBe(v => v == HitVerdict.Hit, "가만히 선 파이터가 네 판정을 다 맞지 않았다");
        sim.Events.Select(e => (e.DashAvailable, e.GuardAvailable, e.ParryAvailable)).ShouldBe(new[]
        {
            (true, true, true),
            (false, true, true),
            (true, false, true),
            (true, true, false),
        }, "관측이 판정의 제 답이 아니라 태그나 다른 수단의 답을 실었다");
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
        BattleSim sim = TestConfigs.PatternSim("하나", pattern);

        // 가드는 판이 설 때부터 붙든다. 대시는 창이 열리기 한 틱 앞에 누른다 — 창의 첫 틱이 무적 창 한가운데다.
        var hold = new InputFrame(0, false, false, false, false, GuardHeld: action == FighterAction.Guard);
        for (int i = 0; i < 600 && sim.NextActiveIn is null or > 2 * BattleSim.Dt; i++)
        {
            sim.Tick(hold);
        }

        sim.NextActiveIn.ShouldNotBeNull("600틱 안에 패턴이 안 섰다");
        sim.NextActiveIn.Value.ShouldBeLessThanOrEqualTo(2 * BattleSim.Dt, "600틱 안에 판정이 두 틱 앞으로 안 왔다");
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
