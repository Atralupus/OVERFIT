using System.Collections.Generic;
using PreReLU.Battle.Rules;
using Shouldly;
using Xunit;

namespace PreReLU.Rules.Tests.Battle;

public class PatternRunnerTests
{
    private const double _dt = 1.0 / 60.0;

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
            Feint = false,
            MultiHit = 1,
            Tracking = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0.00, Kind = "windup" },
            new() { T = 0.45, Kind = "active", Distance = new[] { 0.0, 260.0 }, Height = new[] { 0.0, 200.0 }, Damage = 18 },
            new() { T = 0.60, Kind = "recover" },
            new() { T = 0.90, Kind = "end" },
        },
    };

    [Fact]
    public void 선딜_동안에는_판정이_없다()
    {
        var runner = new PatternRunner(Slash());
        for (int i = 0; i < 20; i++)   // 0.33초
        {
            runner.Tick(_dt).ShouldBeEmpty();
        }
    }

    [Fact]
    public void 판정은_구간_내내가_아니라_딱_한_번_선다()
    {
        // 그래야 multi_hit 을 타임라인의 active 개수로 셀 수 있고,
        // 한 번 휘두른 칼에 여러 번 맞는 일이 없다.
        var runner = new PatternRunner(Slash());
        int emitted = 0;
        while (!runner.Finished)
        {
            emitted += runner.Tick(_dt).Count;
        }

        emitted.ShouldBe(1);
    }

    [Fact]
    public void 판정의_기하가_타임라인_그대로_나온다()
    {
        var runner = new PatternRunner(Slash());
        HitBox box = default;
        while (!runner.Finished)
        {
            IReadOnlyList<HitBox> hits = runner.Tick(_dt);
            if (hits.Count > 0)
            {
                box = hits[0];
            }
        }

        box.MinDistance.ShouldBe(0.0);
        box.MaxDistance.ShouldBe(260.0);
        box.LowHeight.ShouldBe(0.0);
        box.HighHeight.ShouldBe(200.0);
        box.Damage.ShouldBe(18);
    }

    [Fact]
    public void 마지막_단계를_지나면_끝난다()
    {
        var runner = new PatternRunner(Slash());
        for (int i = 0; i < 60; i++)   // 1초 — Duration 0.9 를 넘는다
        {
            runner.Tick(_dt);
        }

        runner.Finished.ShouldBeTrue();
        runner.Elapsed.ShouldBeGreaterThanOrEqualTo(0.9);
    }

    [Fact]
    public void 끝난_뒤_더_돌려도_판정이_안_난다()
    {
        var runner = new PatternRunner(Slash());
        while (!runner.Finished)
        {
            runner.Tick(_dt);
        }

        for (int i = 0; i < 60; i++)
        {
            runner.Tick(_dt).ShouldBeEmpty();
        }
    }
}
