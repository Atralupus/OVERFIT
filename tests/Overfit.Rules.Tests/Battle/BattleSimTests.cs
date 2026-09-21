using System;
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

    /// <summary>몸 충돌이 허용하는 최소 간격. 보스 반폭 + 파이터 반폭이다 — 손으로 적지 않는다.</summary>
    private static double MinGap() => BossConfig().HalfWidth + TestConfigs.Fighter().HalfWidth;

    /// <summary>패턴이 안 도는 판. 보스가 다가오는 것만 본다 (간격이 커서 첫 패턴 전에 끝난다).</summary>
    private static BattleSim Chaser() => new(new BattleSetup
    {
        Arena = new Arena(1920),
        Fighter = TestConfigs.Fighter(),
        Boss = new BossConfig
        {
            MaxHealth = 999_999,
            MoveSpeed = 400,
            HalfWidth = 120,
            PatternGap = 1000,
            Sprite = "boss_test",
        },
        PatternIds = new[] { "횡베기" },
        Patterns = Patterns(),
        Seed = 1,
        MaxTicks = 60 * 60,
    });

    [Fact]
    public void 보스는_파이터_중심이_아니라_간격을_두고_선다()
    {
        // 전에는 파이터의 정확한 중심을 목표로 걸어와서, 보스 반폭(120) 안쪽에 파이터가 섰다 —
        // 데모 실측 평균 교전거리가 85px 였다. 붙는 사람과 떨어지는 사람이 둘 다 ≈0 으로 수렴하니
        // distance_bias 축이 상수였다.
        var sim = Chaser();

        for (int i = 0; i < 300; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.X.ShouldBe(sim.Fighter.X + MinGap(), 0.001, "보스가 간격을 두고 서지 않는다");
    }

    [Fact]
    public void 파이터가_보스_몸_안으로_못_들어간다()
    {
        // 밀어내는 쪽은 **파이터**다. 보스는 4배 크고, 플레이어에게 밀리는 보스는 그림이 틀렸다.
        var sim = Chaser();
        double minGap = MinGap();

        for (int i = 0; i < 600; i++)
        {
            // 계속 오른쪽으로 밀어붙이고 주기적으로 대시로 파고든다.
            sim.Tick(new InputFrame(1, false, Dash: i % 13 == 0, false, false));
            Math.Abs(sim.Fighter.X - sim.Boss.X)
                .ShouldBeGreaterThanOrEqualTo(minGap - 1e-9, $"{i}틱에 파이터가 보스 몸 안에 있다");
        }

        // 벽에 붙어 버려 "못 들어간 게 아니라 못 다가간" 것이면 위 단언이 공허하다.
        sim.Fighter.X.ShouldBe(sim.Boss.X - minGap, 0.001, "파이터가 보스에 붙지도 못했다");
    }

    [Fact]
    public void 몸에_막혀도_대시_무적은_그대로_돈다()
    {
        // 대시가 보스를 뚫고 지나가던 때는 "안으로 파고들기" 가 순간이동이었다. 이제 벽에 선다 —
        // 그런데 무적까지 같이 죽으면 "파고들어야 사는" 패턴(충격파)을 아무도 못 피한다.
        // 무적은 위치가 아니라 행동 시계로 돌므로 막혀도 그대로여야 한다.
        var setup = new BattleSetup
        {
            Arena = new Arena(1920),
            Fighter = TestConfigs.Fighter(),
            Boss = new BossConfig
            {
                MaxHealth = 999_999,
                MoveSpeed = 0,
                HalfWidth = 120,
                PatternGap = 2.0,
                Sprite = "boss_test",
            },
            PatternIds = new[] { "단타" },
            Patterns = new Dictionary<string, PatternDef>
            {
                ["단타"] = OneHit(
                    distance: new double[] { 0, 2000 },
                    height: new double[] { 0, 300 },
                    parryable: false,
                    at: 6 * BattleSim.Dt),
            },
            Seed = 1,
            MaxTicks = 60 * 5,
        };
        var sim = new BattleSim(setup);

        // 120틱 동안 오른쪽으로 걸어 보스 몸에 붙는다(패턴은 2.0초 뒤에 선다).
        for (int i = 0; i < 120; i++)
        {
            sim.Tick(new InputFrame(1, false, false, false, false));
        }

        sim.Fighter.X.ShouldBe(sim.Boss.X - MinGap(), 0.001, "붙지 못했다 — 이 테스트가 막힌 대시를 안 본다");

        // 막힌 채로 보스 쪽으로 대시. 판정은 여섯 틱 뒤에 선다.
        for (int i = 0; i < 12; i++)
        {
            sim.Tick(new InputFrame(0, false, Dash: i == 0, false, false));
        }

        sim.Events.Count.ShouldBe(1);
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Dodged, "몸에 막힌 대시가 무적을 잃었다");
        sim.Events[0].Verb.ShouldBe(DodgeVerb.Dash);
        sim.Fighter.Health.ShouldBe(TestConfigs.Fighter().MaxHealth);
    }

    /// <summary>
    /// 한 판을 끝까지 돌리고 평균 교전 거리를 낸다.
    /// <paramref name="standoff"/> 가 0 이면 붙는 봇(계속 보스 쪽으로), 아니면 그 거리를 지키는 봇이다.
    /// </summary>
    private static PlayerAxes Engage(double standoff)
    {
        var sim = new BattleSim(Setup(bossHealth: 999_999, maxTicks: 60 * 20));
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            double gap = Math.Abs(sim.Fighter.X - sim.Boss.X);
            var toward = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);
            sbyte move = standoff <= 0 ? toward : gap < standoff ? (sbyte)-toward : (sbyte)0;
            outcome = sim.Tick(new InputFrame(move, false, false, false, false));
        }

        return PlayerAxes.From(sim.Events);
    }

    [Fact]
    public void 붙는_봇과_떨어지는_봇을_거리_축이_가른다()
    {
        // 이 축이 존재하는 이유 자체다. 몸 충돌이 없던 때는 붙는 쪽도 떨어지는 쪽도 보스 몸
        // 안(≈0)으로 수렴해 distance_bias 가 상수였다 — 죽은 입력은 망의 용량만 먹고
        // 아무것도 가르치지 않는다. "값이 0 이 아니다" 로는 부족하고 **둘이 갈려야** 한다.
        PlayerAxes hugger = Engage(0);
        PlayerAxes spacer = Engage(500);

        hugger.Samples.ShouldBeGreaterThan(0);
        spacer.Samples.ShouldBeGreaterThan(0);
        hugger.DistanceBias.ShouldBeGreaterThanOrEqualTo(MinGap(), "붙는 봇이 아직 보스 몸 안에 있다");
        spacer.DistanceBias.ShouldBeGreaterThan(hugger.DistanceBias + 200,
            "붙는 봇과 떨어지는 봇이 같은 거리로 수렴한다 — 축이 못 가른다");
    }

    /// <summary>
    /// 판정 직전에 대시하는 봇 한 판. <paramref name="inward"/> 면 보스 쪽을, 아니면 반대쪽을 보고 뛴다.
    /// 대시는 <b>바라보는 쪽으로만</b> 가므로 어디를 보고 있었나가 곧 방향이다.
    /// </summary>
    private static PlayerAxes DashingBot(bool inward)
    {
        var sim = new BattleSim(new BattleSetup
        {
            Arena = new Arena(1920),
            Fighter = TestConfigs.Fighter(),
            Boss = BossConfig(999_999),
            PatternIds = new[] { "횡베기", "지면쓸기", "충격파" },
            Patterns = Patterns(),
            Seed = 51,
            MaxTicks = 60 * 20,
        });

        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            var toward = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);
            bool soon = sim.NextActiveIn is double remaining && remaining <= 0.10;
            outcome = sim.Tick(new InputFrame(inward ? toward : (sbyte)-toward, false, Dash: soon, false, false));
        }

        return PlayerAxes.From(sim.Events);
    }

    [Fact]
    public void 파고드는_봇과_도망가는_봇을_대시_방향_축이_가른다()
    {
        // 여섯 패턴이 전부 distance[0]=0 이던 때는 안으로 가는 것이 정답인 상황이 아예 없었고,
        // 그나마 나오던 -1 은 파이터가 보스 몸 안에 서서 부호가 뒤집힌 것이었다 — 성향이 아니라
        // 겹침의 부산물이다. 이제 축이 양끝을 다 쓴다.
        PlayerAxes inward = DashingBot(inward: true);
        PlayerAxes outward = DashingBot(inward: false);

        inward.DashSamples.ShouldBeGreaterThan(0);
        outward.DashSamples.ShouldBeGreaterThan(0);
        inward.DashDirectionBias.ShouldBeGreaterThan(0.5, "안으로만 뛰었는데 축이 안 따라온다");
        outward.DashDirectionBias.ShouldBeLessThan(-0.5, "밖으로만 뛰었는데 축이 안 따라온다");
    }

    /// <summary>
    /// 충격파 한 방을 <paramref name="walkTicks"/> 만큼 보스 쪽으로 걸어간 자리에서 맞아 본다.
    /// 패턴은 2.0초 뒤에 서므로 그 전에 자리를 잡는다.
    /// </summary>
    private static DodgeEvent Shockwave(int walkTicks)
    {
        var sim = new BattleSim(new BattleSetup
        {
            Arena = new Arena(1920),
            Fighter = TestConfigs.Fighter(),
            Boss = new BossConfig
            {
                MaxHealth = 999_999,
                MoveSpeed = 0,
                HalfWidth = 120,
                PatternGap = 2.0,
                Sprite = "boss_test",
            },
            PatternIds = new[] { "충격파" },
            Patterns = Patterns(),
            Seed = 1,
            MaxTicks = 60 * 10,
        });

        for (int i = 0; i < 300 && sim.Events.Count == 0; i++)
        {
            sim.Tick(new InputFrame((sbyte)(i < walkTicks ? 1 : 0), false, false, false, false));
        }

        return sim.Events.Single();
    }

    [Fact]
    public void 충격파는_붙은_쪽만_살려준다()
    {
        // 안쪽 220px 이 비어 있는 유일한 패턴이다. 몸 충돌이 허용하는 최소 간격(150)과 220 사이의
        // 70px 주머니로 **파고들어야** 산다 — 그 결정이 dash_direction_bias 가 재려는 바로 그것이다.
        // 두 수치는 다른 파일에 있다(patterns.json 의 220 · fighters.json 과 보스 반폭의 150).
        // 어느 한쪽이 움직여 주머니가 닫히면 축은 조용히 다시 죽는다 — 여기서 빨개지게 한다.
        DodgeEvent hugging = Shockwave(300);
        DodgeEvent spacing = Shockwave(80);

        hugging.Verdict.ShouldBe(HitVerdict.MissedByRange, "붙었는데 충격파에 맞았다 — 안쪽 주머니가 닫혔다");
        hugging.Distance.ShouldBe(MinGap(), 0.001);

        spacing.Verdict.ShouldBe(HitVerdict.Hit, "떨어져 있는데 안 맞았다 — 이 테스트가 주머니를 안 본다");
        spacing.Distance.ShouldBeGreaterThan(220);
    }

    [Fact]
    public void 정확히_겹친_자리는_고정된_쪽으로_민다()
    {
        // 정상 플레이에서는 나올 수 없는 자리다(매 틱 밀어내므로). 그래도 부동소수 0 의 부호나
        // 난수로 가르지 않는다 — 한 번이라도 갈리면 리플레이 골든이 재현되지 않는다.
        BattleSim.SeparatedX(480, 500, 150).ShouldBe(350, 1e-9);   // 왼쪽에 있었으면 왼쪽으로
        BattleSim.SeparatedX(520, 500, 150).ShouldBe(650, 1e-9);   // 오른쪽에 있었으면 오른쪽으로
        BattleSim.SeparatedX(500, 500, 150).ShouldBe(350, 1e-9);   // 정확히 겹치면 왼쪽으로 고정
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
    public void 관측이_그_판정에_무엇이_가능했는지를_같이_싣는다()
    {
        // PlayerAxes.From 은 이벤트 목록만 받는다 — 구조상 패턴 태그에 손이 안 닿는다.
        // 그래서 "이 판정을 무엇으로 피할 수 있었나" 를 방출 시점에 실어 보낸다.
        // 없으면 의존도 축은 사용 비율로밖에 못 만들어지고, 그건 스펙 8절의 정의가 아니다.
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 2000 },
            height: new double[] { 0, 300 },
            parryable: true,
            at: 6 * BattleSim.Dt));

        for (int i = 1; i <= 14; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(1);
        DodgeEvent e = sim.Events[0];
        e.DashAvailable.ShouldBeTrue("dash_window=0.14 인데 대시가 없었다고 실렸다");
        e.ParryAvailable.ShouldBeTrue("parryable=true 인데 패리가 없었다고 실렸다");
        e.JumpAvailable.ShouldBeFalse("jumpable=false 인데 점프가 가능했다고 실렸다");
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
