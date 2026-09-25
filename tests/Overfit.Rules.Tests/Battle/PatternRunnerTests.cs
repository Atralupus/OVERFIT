using System.Collections.Generic;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class PatternRunnerTests
{
    private const double _dt = 1.0 / 60.0;

    private static PatternDef Slash() => new()
    {
        Tell = TestConfigs.Tell(),
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
            HasGuardBreak = false,
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

        box.Shape.Local.ShouldBe(new[] { new HitRect(0, 260, 0, 200), new HitRect(-260, 0, 0, 200) });
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

    /// <summary>
    /// 헛스윙(<c>feint</c>)이 든 패턴. 판정이 없는 <b>박자 하나</b>가 진짜 판정 앞에 선다 —
    /// 이슈 #48 의 <c>III-역린</c> 이 이 모양이다.
    /// </summary>
    private static PatternDef Feinted() => new()
    {
        Tell = TestConfigs.Tell(),
        Tags = new PatternTags
        {
            DashWindow = 0.18,
            DashDirection = "out",
            Jumpable = false,
            AntiAir = false,
            Parryable = true,
            ParryWindow = 0.12,
            PunishGreed = false,
            Reach = "close",
            Feint = true,
            MultiHit = 1,
            Tracking = false,
            HasGuardBreak = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0.00, Kind = "windup" },
            new() { T = 0.30, Kind = "feint" },
            new() { T = 0.45, Kind = "active", Distance = new[] { 0.0, 260.0 }, Height = new[] { 0.0, 200.0 }, Damage = 18 },
            new() { T = 0.60, Kind = "recover" },
            new() { T = 0.90, Kind = "end" },
        },
    };

    [Fact]
    public void 헛스윙은_판정을_안_낸다()
    {
        // **damage 0 판정으로 흉내 내지 않는다** (이슈 #48). 0 짜리 판정도 HitBox 라
        // BattleSim 이 관측을 한 건 남기고, 그러면 "맞지 않았다" 가 일어난 적 없는 판정으로
        // 계측에 쌓인다 — 회피 기록을 읽어 변종을 고르는 것이 이 게임의 전부라 그 오염이 치명적이다.
        var runner = new PatternRunner(Feinted());
        int emitted = 0;
        while (!runner.Finished)
        {
            emitted += runner.Tick(_dt).Count;
        }

        emitted.ShouldBe(1, "헛스윙이 판정으로 샜다");
    }

    [Fact]
    public void 헛스윙이_지나간_것은_셀_수_있다()
    {
        // 판정을 안 내는 것만으로는 화면이 헛스윙을 모른다 — 안 보이는 헛스윙은 미끼가 아니다.
        // 그래서 <b>개수</b>로 싣는다: 뷰는 이 값이 는 틱에 칼이 지나간 연출을 낸다.
        var runner = new PatternRunner(Feinted());
        runner.Feints.ShouldBe(0);

        while (!runner.Finished)
        {
            runner.Tick(_dt);
        }

        runner.Feints.ShouldBe(1);
    }

    /// <summary>판정이 셋인 패턴. <b>마무리</b>는 마지막 active 하나다.</summary>
    private static PatternDef Triple() => new()
    {
        Tell = TestConfigs.Tell(),
        Tags = new PatternTags
        {
            DashWindow = 0.18,
            DashDirection = "out",
            Jumpable = false,
            AntiAir = false,
            Parryable = true,
            ParryWindow = 0.12,
            PunishGreed = false,
            Reach = "mid",
            Feint = false,
            MultiHit = 3,
            Tracking = false,
            HasGuardBreak = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0.00, Kind = "windup" },
            new() { T = 0.20, Kind = "active", Distance = new[] { 0.0, 260.0 }, Height = new[] { 0.0, 200.0 }, Damage = 8 },
            new() { T = 0.40, Kind = "active", Distance = new[] { 0.0, 260.0 }, Height = new[] { 0.0, 200.0 }, Damage = 8 },
            new() { T = 0.60, Kind = "active", Distance = new[] { 0.0, 260.0 }, Height = new[] { 0.0, 200.0 }, Damage = 14 },
            new() { T = 0.72, Kind = "recover" },
            new() { T = 0.90, Kind = "end" },
        },
    };

    [Fact]
    public void 마지막_판정만_마무리다()
    {
        // **마무리가 이 계열의 상이 걸리는 자리다** (이슈 #53) — 받아치면 보스가 굳고,
        // 그 경직 하나에 2연격이 들어간다. 앞의 연타에 같은 상을 주면 타임라인이
        // 패턴 도중에 서서 3타가 오는 시각이 매번 달라진다(유저가 말한 "딜레이가 매번 다르다").
        //
        // **데이터에 손으로 적지 않고 타임라인에서 뽑는다.** finisher: true 를 사람이 달면
        // 판정을 하나 끼워 넣는 날 옛 마무리에 그 표가 남고, 그 거짓말은 테스트가 아니라
        // 플레이 중에만 보인다.
        var runner = new PatternRunner(Triple());
        var finishers = new List<bool>();

        while (!runner.Finished)
        {
            foreach (HitBox box in runner.Tick(_dt))
            {
                finishers.Add(box.Finisher);
            }
        }

        finishers.ShouldBe(new[] { false, false, true });
    }

    [Fact]
    public void 판정이_하나뿐이면_그것이_마무리다()
    {
        // 경계다. "마지막" 을 "두 번째부터" 로 잘못 짜면 단타 패턴에 상이 영영 안 걸린다.
        var runner = new PatternRunner(Slash());
        var finishers = new List<bool>();

        while (!runner.Finished)
        {
            foreach (HitBox box in runner.Tick(_dt))
            {
                finishers.Add(box.Finisher);
            }
        }

        finishers.ShouldBe(new[] { true });
    }
}
