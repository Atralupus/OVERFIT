using System.Collections.Generic;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class PatternRunnerTests
{
    /// <summary>패턴 하나를 혼자 세운다 — 판정은 판을 세울 때처럼 <see cref="BossHits"/> 가 짓는다.</summary>
    private static PatternRunner Runner(PatternDef def) =>
        new(def, BossHits.Of(def, TestConfigs.HitShapes(), TestConfigs.Fighter()));

    private static PatternDef Slash() => new()
    {
        Tags = new PatternTags
        {
            DashWindow = 0.18,
            DashDirection = "out",
            Jumpable = false,
            AntiAir = false,
            Parryable = true,
            ParryWindow = 0.12,
            PunishGreed = true,
            Reach = "mid",
            MultiHit = 1,
            Tracking = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0.00, Kind = "windup" },
            new() { T = 0.45, Kind = "active", Band = new[] { 0.0, 260.0, 0.0, 200.0 }, Damage = 18 },
            new() { T = 0.60, Kind = "recover" },
            new() { T = 0.90, Kind = "end" },
        },
    };

    [Fact]
    public void 선딜_동안에는_판정이_없다()
    {
        var runner = Runner(Slash());
        for (int i = 0; i < 20; i++)   // 0.33초
        {
            runner.Tick().ShouldBeEmpty();
        }
    }

    [Fact]
    public void 판정은_구간_내내가_아니라_딱_한_번_선다()
    {
        // 그래야 multi_hit 을 타임라인의 active 개수로 셀 수 있고,
        // 한 번 휘두른 칼에 여러 번 맞는 일이 없다.
        var runner = Runner(Slash());
        int emitted = 0;
        while (!runner.Finished)
        {
            emitted += runner.Tick().Count;
        }

        emitted.ShouldBe(1);
    }

    [Fact]
    public void 판정의_기하가_타임라인_그대로_나온다()
    {
        var runner = Runner(Slash());
        HitBox box = default;
        while (!runner.Finished)
        {
            IReadOnlyList<HitBox> hits = runner.Tick();
            if (hits.Count > 0)
            {
                box = hits[0];
            }
        }

        box.Shape.Local.ShouldBe(new[] { new HitRect(0, 260, 0, 200), new HitRect(-260, 0, 0, 200) });
        box.Damage.ShouldBe(18);
    }

    [Fact]
    public void 마지막_단계를_지나면_끝난다()
    {
        var runner = Runner(Slash());
        for (int i = 0; i < 60; i++)   // 1초 — Duration 0.9 를 넘는다
        {
            runner.Tick();
        }

        runner.Finished.ShouldBeTrue();
        runner.Ticks.ShouldBe(54, "끝(0.9초)은 54틱에 든다");
    }

    [Fact]
    public void 끝난_뒤_더_돌려도_판정이_안_난다()
    {
        var runner = Runner(Slash());
        while (!runner.Finished)
        {
            runner.Tick();
        }

        for (int i = 0; i < 60; i++)
        {
            runner.Tick().ShouldBeEmpty();
        }
    }

    /// <summary>
    /// 3연격의 박자만 옮긴 패턴 — 칼을 든 f0 · f1(0.725) · 판정 0.85 · 1.55 · 2.65 · 끝 3.25초 (설계 §4.1).
    /// 모양은 아무것이나다 — 여기서 재는 것은 단계가 드는 틱이다.
    /// </summary>
    private static PatternDef ThreeBeat() => new()
    {
        Tags = new PatternTags
        {
            DashWindow = 0.2,
            DashDirection = "out",
            Jumpable = false,
            AntiAir = false,
            Parryable = true,
            ParryWindow = 0.18,
            PunishGreed = false,
            Reach = "far",
            MultiHit = 3,
            Tracking = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0.0, Kind = "windup" },
            new() { T = 0.725, Kind = "windup" },
            new() { T = 0.85, Kind = "active", Band = new[] { 0.0, 400.0, 0.0, 300.0 }, Damage = 8, ActiveSeconds = 0.125 },
            new() { T = 0.975, Kind = "recover" },
            new() { T = 1.55, Kind = "active", Band = new[] { 0.0, 400.0, 0.0, 300.0 }, Damage = 8, ActiveSeconds = 0.125 },
            new() { T = 2.65, Kind = "active", Band = new[] { 0.0, 400.0, 0.0, 300.0 }, Damage = 14, ActiveSeconds = 0.125 },
            new() { T = 3.25, Kind = "end" },
        },
    };

    [Fact]
    public void 단계는_세울_때_정한_정수_틱에_든다()
    {
        // 설계 §3.6 ⑤ — 러너가 틱을 센다(패턴의 첫 틱이 1). 1/60 을 더해 가던 때는 1.55초 판정이 93틱이 아니라 94틱에,
        // 2.65초가 159틱이 아니라 160틱에 섰다(0.85초는 51틱 그대로). 0.725초는 43.5틱이라 0 에서 먼 쪽인 44틱이다.
        var runner = Runner(ThreeBeat());
        var fired = new List<int>();
        int raised = 0;
        while (!runner.Finished)
        {
            if (runner.Tick().Count > 0)
            {
                fired.Add(runner.Ticks);
            }

            if (raised == 0 && runner.Step is { T: 0.725 })
            {
                raised = runner.Ticks;
            }
        }

        raised.ShouldBe(44);
        fired.ShouldBe(new[] { 51, 93, 159 });
        runner.Ticks.ShouldBe(195, "끝(3.25초)은 195틱에 든다");
    }

    [Fact]
    public void 세운_틱에는_시계도_단계도_안_간다()
    {
        // 러너가 따르는 것은 HoldClock 하나다 (설계 §8.1) — 참인 틱에는 시계를 안 밀고, 그 사이에 때가 된 단계에도 안 든다.
        var runner = Runner(ThreeBeat());
        for (int i = 0; i < 50; i++)
        {
            runner.Tick();
        }

        for (int i = 0; i < 20; i++)
        {
            runner.Tick(holdClock: true).ShouldBeEmpty();
        }

        runner.Ticks.ShouldBe(50, "세운 틱에 시계가 갔다");
        runner.Tick().Count.ShouldBe(1, "풀린 첫 틱(시계 51)에 1타가 안 섰다");
    }

    /// <summary>
    /// 움직임을 단 단계 뒤에 <b>같은 시각</b>(0.5초)의 판정이 선 패턴 — 돌진 뒤의 3타(설계 §4.6)가 이 모양이다.
    /// 움직임 id 는 가짜다 — 러너는 움직임을 안 돌리고 id 도 안 본다.
    /// </summary>
    private static PatternDef MotionThenHit() => new()
    {
        Tags = ThreeBeat().Tags,
        Timeline = new List<PatternStep>
        {
            new() { T = 0.0, Kind = "windup" },
            new() { T = 0.5, Kind = "windup", Motion = new MotionDef { Id = "가짜" } },
            new() { T = 0.5, Kind = "active", Band = new[] { 0.0, 400.0, 0.0, 300.0 }, Damage = 5 },
            new() { T = 1.0, Kind = "end" },
        },
    };

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void 움직임_뒤의_단계는_T_가_같아도_움직임이_끝난_다음_틱에_든다(int held)
    {
        // 설계 §4.6 · §8.1 — 돌진이 틱 A 에 끝나면 A + 1 부터 시계가 다시 가고, 그 뒤의 단계는 T 가 같아도 그때 든다.
        // 3번 PR 에는 시계를 세우는 움직임이 없으니 가짜로 잰다: 움직임을 단 단계에 든 뒤 held 틱 동안 시계를 세운다
        // (held 0 은 돌진 없이 곧장 닿은 경우다).
        var runner = Runner(MotionThenHit());
        int calls = 0;
        while (runner.StartedMotion is null && calls < 120)
        {
            runner.Tick().ShouldBeEmpty("움직임을 단 단계와 같은 틱에 판정까지 섰다");
            calls++;
        }

        calls.ShouldBe(30, "0.5초 단계가 30틱에 안 들었다");
        for (int i = 0; i < held; i++)
        {
            runner.Tick(holdClock: true).ShouldBeEmpty();
        }

        runner.Tick().Count.ShouldBe(1, "움직임이 끝난 다음 틱에 판정이 안 섰다");
        runner.Ticks.ShouldBe(31);
    }

    [Fact]
    public void 실제_1단계_패턴은_스펙의_틱에_판정과_도약을_낸다()
    {
        // 설계 §4.1 · §4.2 의 틱 표 그대로다 — 3연격의 판정은 51 · 93 · 159틱에 서고 195틱에 끝난다. 점프 공격은 24틱에 뛰고
        // 60틱에 착지 판정이 서고 90틱에 끝난다. 틱은 패턴의 첫 틱을 1로 센다(§3.6 ⑤).
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();

        (List<int> Hits, List<int> Motions, int End) Run(string id)
        {
            PatternRunner runner = Runner(patterns[id]);
            var hits = new List<int>();
            var motions = new List<int>();
            while (!runner.Finished)
            {
                if (runner.Tick().Count > 0)
                {
                    hits.Add(runner.Ticks);
                }

                if (runner.StartedMotion is not null)
                {
                    motions.Add(runner.Ticks);
                }
            }

            return (hits, motions, runner.Ticks);
        }

        (List<int> comboHits, List<int> comboMotions, int comboEnd) = Run("3연격");
        comboHits.ShouldBe(new[] { 51, 93, 159 });
        comboMotions.ShouldBeEmpty();
        comboEnd.ShouldBe(195);

        (List<int> leapHits, List<int> leapMotions, int leapEnd) = Run("점프 공격");
        leapHits.ShouldBe(new[] { 60 });
        leapMotions.ShouldBe(new[] { 24 });
        leapEnd.ShouldBe(90);
    }
}
