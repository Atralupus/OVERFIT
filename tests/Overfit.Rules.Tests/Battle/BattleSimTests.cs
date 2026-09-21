using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class BattleSimTests
{
    private static Dictionary<string, PatternDef> Patterns() =>
        JsonData<PatternDef>.ParseTable(File.ReadAllText(Path.Combine("data", "patterns.json")), "patterns.json");

    private static BossConfig BossConfig(int health = 200) => new()
    {
        MaxHealth = health,
        MoveSpeed = 160,
        HalfWidth = 120,
        PatternGap = 0.8,
        Sprite = "boss_test",
    };

    private static BattleSetup Setup(int bossHealth = 200, int maxTicks = 60 * 120) => new()
    {
        Arena = new Arena(1920),
        Fighter = TestConfigs.Fighter(),
        Boss = BossConfig(bossHealth),
        PatternIds = new[] { "횡베기", "지면쓸기" },
        Patterns = Patterns(),
        Seed = 51,
        MaxTicks = maxTicks,
    };

    /// <summary>아무것도 안 하고 서 있는다 — 보스에게 맞기만 한다.</summary>
    private static BattleOutcome RunIdle(BattleSim sim)
    {
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(default);
        }

        return outcome.Value;
    }

    [Fact]
    public void 가만히_있으면_진다()
    {
        var sim = new BattleSim(Setup());

        RunIdle(sim).ShouldBe(BattleOutcome.Lose);
        sim.Fighter.Health.ShouldBe(0);
    }

    [Fact]
    public void 보스_체력이_0_이_되면_이긴다()
    {
        var sim = new BattleSim(Setup(bossHealth: 1));
        sim.Boss.TakeDamage(1);

        sim.Tick(default).ShouldBe(BattleOutcome.Win);
    }

    [Fact]
    public void 시간이_다_되면_진다()
    {
        // 무한 루프로 매달리지 않게 판을 끊는다 — 데이터 공장이 수백만 판을 돌리려면
        // 한 판이 반드시 끝나야 한다.
        var sim = new BattleSim(Setup(bossHealth: 999_999, maxTicks: 60));

        RunIdle(sim).ShouldBe(BattleOutcome.Lose);
        sim.Ticks.ShouldBe(60);
    }

    [Fact]
    public void 보스가_플레이어_쪽으로_다가온다()
    {
        // 파이터는 보스 왼쪽(480 vs 1440)에 선다. 다가온다는 것은 보스 X 가 **줄어든다**는 뜻이다.
        // 파이터를 세워 두는 이유는 그래야 "누가 다가갔나" 가 안 섞이기 때문이다.
        var sim = new BattleSim(Setup());
        double start = sim.Boss.X;

        for (int i = 0; i < 120; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.X.ShouldBeLessThan(start);
    }

    [Fact]
    public void 공격이_닿으면_보스_체력이_준다()
    {
        var sim = new BattleSim(Setup());
        int before = sim.Boss.Health;
        // 보스가 오른쪽(1440)에 있으니 걸어가야 붙는다. 60틱 주기 공격은 스태미나가
        // 버틴다(실측 최소 88/100) — 30틱 주기는 회복(~8.7/주기)보다 비용(12)이 커서
        // 스태미나가 바닥나 판정이 조용히 안 선다.
        // 실측 125틱에 첫 타격이 들어간다 — 여유를 두고 2배인 250틱까지 돈다.
        for (int i = 0; i < 250; i++)
        {
            sim.Tick(new InputFrame(1, false, false, false, Attack: i % 60 == 0));
        }

        sim.Boss.Health.ShouldBeLessThan(before);
    }

    [Fact]
    public void 같은_시드와_같은_입력은_같은_판을_만든다()
    {
        // 리플레이 골든의 뼈대다. 이게 깨지면 학습 데이터도 재현이 안 된다.
        var inputs = new InputFrame[600];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i] = new InputFrame(
                (sbyte)(i % 11 < 5 ? 1 : -1),
                Jump: i % 37 == 0,
                Dash: i % 23 == 0,
                Parry: i % 29 == 0,
                Attack: i % 17 == 0);
        }

        var a = new BattleSim(Setup());
        var b = new BattleSim(Setup());
        foreach (InputFrame input in inputs)
        {
            a.Tick(input);
            b.Tick(input);
        }

        a.Fighter.X.ShouldBe(b.Fighter.X);
        a.Fighter.Health.ShouldBe(b.Fighter.Health);
        a.Boss.X.ShouldBe(b.Boss.X);
        a.Boss.Health.ShouldBe(b.Boss.Health);
    }

    [Fact]
    public void 다른_시드는_다른_패턴_순서를_만든다()
    {
        BattleSetup one = Setup();
        BattleSetup two = Setup();
        two.Seed = 99;

        var a = new BattleSim(one);
        var b = new BattleSim(two);
        var seenA = new List<string>();
        var seenB = new List<string>();
        for (int i = 0; i < 900; i++)
        {
            a.Tick(default);
            b.Tick(default);
            if (a.Boss.CurrentPattern is { } pa && (seenA.Count == 0 || seenA[^1] != pa))
            {
                seenA.Add(pa);
            }

            if (b.Boss.CurrentPattern is { } pb && (seenB.Count == 0 || seenB[^1] != pb))
            {
                seenB.Add(pb);
            }
        }

        seenA.ShouldNotBeEmpty();
        seenB.ShouldNotBeEmpty();
        seenA.ShouldNotBe(seenB);
    }

    [Fact]
    public void 단계가_쓰는_패턴만_나온다()
    {
        var setup = Setup();
        setup.PatternIds = new[] { "지면쓸기" };
        var sim = new BattleSim(setup);

        for (int i = 0; i < 900; i++)
        {
            sim.Tick(default);
            if (sim.Boss.CurrentPattern is { } id)
            {
                id.ShouldBe("지면쓸기");
            }
        }
    }

    [Fact]
    public void 전투가_회피_관측을_남긴다()
    {
        var sim = new BattleSim(Setup());
        for (int i = 0; i < 900; i++)
        {
            sim.Tick(new InputFrame(0, false, Dash: i % 41 == 0, false, false));
        }

        sim.Events.ShouldNotBeEmpty();
        PlayerAxes axes = PlayerAxes.From(sim.Events);
        axes.Samples.ShouldBe(sim.Events.Count);
    }

    [Fact]
    public void 없는_패턴_id_는_매_틱_에러를_쏟지_않는다()
    {
        // Begin 이 간격을 안 되돌린 채 나가면 _gapLeft 가 0 이하로 남아 다음 틱에도 곧장
        // 같은 갈래로 떨어진다 — 유효한 id 가 뽑힐 때까지 매 틱 [E] 다. 판은 끝나지만
        // judge_headless 가 읽는 로그가 그것으로 뒤덮인다.
        using var log = new LogCapture();
        var setup = new BattleSetup
        {
            Arena = new Arena(1920),
            Fighter = TestConfigs.Fighter(),
            Boss = new BossConfig
            {
                MaxHealth = 999_999,
                MoveSpeed = 0,
                HalfWidth = 120,
                PatternGap = 10 * BattleSim.Dt,
                Sprite = "boss_test",
            },
            PatternIds = new[] { "없는패턴" },
            Patterns = new Dictionary<string, PatternDef>(),
            Seed = 1,
            MaxTicks = 60 * 5,
        };

        var sim = new BattleSim(setup);
        for (int i = 0; i < 60; i++)
        {
            sim.Tick(default);
        }

        int errors = log.Lines.Count(l => l.Contains("pattern_missing", System.StringComparison.Ordinal));
        errors.ShouldBeLessThanOrEqualTo(7, "간격을 안 되돌려 매 틱 에러를 쏟고 있다");
    }

    /// <summary>판정 하나짜리 패턴. 기하와 태그를 부르는 쪽이 정한다.</summary>
    private static PatternDef OneHit(double[] distance, double[] height, bool parryable, double at) => new()
    {
        Tags = new PatternTags
        {
            DashWindow = 0.14,
            DashDirection = "out",
            Jumpable = false,
            AntiAir = false,
            Parryable = parryable,
            ParryWindow = parryable ? 0.12 : 0,
            PunishGreed = false,
            Reach = "far",
            Feint = false,
            MultiHit = 1,
            Tracking = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = at, Kind = "active", Distance = distance, Height = height, Damage = 5 },
            new() { T = at + (6 * BattleSim.Dt), Kind = "end" },
        },
    };

    /// <summary>보스를 제자리에 세우고 <paramref name="pattern"/> 하나만 돌리는 판.</summary>
    private static BattleSim OnePattern(PatternDef pattern) => new(new BattleSetup
    {
        Arena = new Arena(1920),
        Fighter = TestConfigs.Fighter(),
        Boss = new BossConfig
        {
            MaxHealth = 999_999,
            MoveSpeed = 0,
            HalfWidth = 120,
            PatternGap = 3 * BattleSim.Dt,
            Sprite = "boss_test",
        },
        PatternIds = new[] { "단타" },
        Patterns = new Dictionary<string, PatternDef> { ["단타"] = pattern },
        Seed = 1,
        MaxTicks = 60 * 5,
    });

    [Fact]
    public void 점프로_넘긴_판정은_같이_눌러둔_패리가_아니라_점프로_기록된다()
    {
        // 이 브랜치의 최우선 버그다. 회피 행동 칸이 하나였을 때는 **가장 최근에 시작한 행동**이
        // 판정을 가져갔다. 그래서 점프로 넘긴 낮은 판정이 그 뒤에 누른 패리의 공으로 기록됐고
        // (out/demo.log 10건 중 4건), 그 패리는 "실패한 패리" 로도 세어져 parry_rate 까지 깎았다.
        // 안 맞은 이유는 HitResolver 가 이미 알고 있다 — 높이가 어긋났으면 점프다.
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 2000 },
            height: new double[] { 0, 70 },
            parryable: true,
            at: 6 * BattleSim.Dt));

        for (int i = 1; i <= 14; i++)
        {
            // 1틱: 점프(공중으로) → 7틱: 패리(점프보다 **나중에** 시작한다). 판정은 9틱 언저리다.
            sim.Tick(new InputFrame(0, Jump: i == 1, false, Parry: i == 7, false));
        }

        sim.Events.Count.ShouldBe(1);
        DodgeEvent e = sim.Events[0];
        e.Verdict.ShouldBe(HitVerdict.MissedByHeight);
        e.Verb.ShouldBe(DodgeVerb.Jump, "점프가 넘긴 판정인데 나중에 시작한 패리가 공을 가져갔다");
        e.Airborne.ShouldBeTrue();
        e.TimingError.ShouldBeLessThan(0);
    }

    [Fact]
    public void 거리로_빗나간_판정은_어떤_행동에도_안_붙는다()
    {
        // 간격 덕에 그냥 안 닿은 것이다. 그 순간 돌던 대시·패리의 공으로 적으면
        // dash_timing_bias 가 "판정을 피한 대시" 가 아닌 것들로 채워진다.
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 100 },
            height: new double[] { 0, 300 },
            parryable: true,
            at: 6 * BattleSim.Dt));

        for (int i = 1; i <= 14; i++)
        {
            // 파이터는 480, 보스는 1440 에 서 있다 — 대시를 해도 100 안쪽으로는 못 들어간다.
            sim.Tick(new InputFrame(0, false, Dash: i == 2, Parry: i == 8, false));
        }

        sim.Events.Count.ShouldBe(1);
        DodgeEvent e = sim.Events[0];
        e.Verdict.ShouldBe(HitVerdict.MissedByRange);
        e.Verb.ShouldBe(DodgeVerb.Spacing);
        e.TimingError.ShouldBe(0);
        e.Direction.ShouldBe(0);
    }

    [Fact]
    public void 맞은_판정은_그때_돌고_있던_행동을_남긴다()
    {
        // 피하지 못한 것도 데이터다 — "무엇을 시도했다 실패했나" 가 없으면
        // 망은 "무엇을 못 피하나" 를 배울 수 없다.
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 2000 },
            height: new double[] { 0, 300 },
            parryable: false,
            at: 6 * BattleSim.Dt));

        for (int i = 1; i <= 14; i++)
        {
            sim.Tick(new InputFrame(0, false, false, Parry: i == 8, false));
        }

        sim.Events.Count.ShouldBe(1);
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Hit);
        sim.Events[0].Verb.ShouldBe(DodgeVerb.Parry);
    }

    [Fact]
    public void 한_번의_대시가_연속타_두_대를_모두_설명한다()
    {
        // 대시 무적(0.14초) 한 번이 멀티히트 판정 두 개를 다 덮도록 타임라인을 짠다.
        // Land 가 첫 판정에서 회피 행동을 지워 버리면 두 번째 판정은 "아무것도 안 했다"로
        // 잘못 기록된다 — 근거가 없는 게 아니라 잘못 붙는 사고다. 그래서 행동은 Land 가 아니라
        // RememberDodgeStart 가 그 행동이 끝났을 때만 지운다.
        var pattern = new PatternDef
        {
            Tags = new PatternTags
            {
                DashWindow = 0.14,
                DashDirection = "either",
                Jumpable = false,
                AntiAir = false,
                Parryable = false,
                ParryWindow = 0,
                PunishGreed = false,
                Reach = "close",
                Feint = false,
                MultiHit = 2,
                Tracking = false,
            },
            Timeline = new List<PatternStep>
            {
                new() { T = 3 * BattleSim.Dt, Kind = "active", Distance = new double[] { 0, 2000 }, Height = new double[] { 0, 300 }, Damage = 5 },
                new() { T = 6 * BattleSim.Dt, Kind = "active", Distance = new double[] { 0, 2000 }, Height = new double[] { 0, 300 }, Damage = 5 },
                new() { T = 10 * BattleSim.Dt, Kind = "end" },
            },
        };

        var setup = new BattleSetup
        {
            Arena = new Arena(1920),
            Fighter = TestConfigs.Fighter(),
            Boss = new BossConfig
            {
                MaxHealth = 999_999,
                MoveSpeed = 0,
                HalfWidth = 120,
                PatternGap = 3 * BattleSim.Dt,
                Sprite = "boss_test",
            },
            PatternIds = new[] { "멀티히트" },
            Patterns = new Dictionary<string, PatternDef> { ["멀티히트"] = pattern },
            Seed = 1,
            MaxTicks = 60 * 5,
        };

        var sim = new BattleSim(setup);
        // 패턴 시작(3틱째)과 같은 틱에 대시 — 대시 무적이 3·6틱째 판정을 둘 다 덮는다.
        for (int i = 1; i <= 12; i++)
        {
            sim.Tick(new InputFrame(0, false, Dash: i == 3, false, false));
        }

        sim.Events.Count.ShouldBe(2);
        sim.Events[0].Verb.ShouldBe(DodgeVerb.Dash);
        sim.Events[1].Verb.ShouldBe(DodgeVerb.Dash);
    }
}
