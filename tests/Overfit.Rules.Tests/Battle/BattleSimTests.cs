using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class BattleSimTests
{
    private static Dictionary<string, PatternDef> Patterns() => TestConfigs.Patterns();

    /// <summary>
    /// 기본 판. <paramref name="bossHealth"/> 와 <paramref name="maxTicks"/> 만 준다 —
    /// 체력은 "이기지 못하게" 해서 시간 초과를 강제할 때, 상한은 테스트가 초 단위로 끝나야 할 때다.
    /// 나머지는 전부 실제 데이터다.
    /// </summary>
    private static BattleSetup Setup(int? bossHealth = null, int maxTicks = 60 * 120) => new()
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighter(),
        Boss = TestConfigs.Boss(maxHealth: bossHealth),
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

    /// <summary>
    /// 보스가 두고 서는 간격. 보스 반폭 + 파이터 반폭이다 — 손으로 적지 않는다.
    /// <b>더 이상 벽이 아니다</b>(이슈 #27 · 몸 충돌 제거) — 보스가 스스로 지키는 거리일 뿐이라
    /// 파이터는 이 안으로 걸어 들어갈 수 있다.
    /// </summary>
    private static double Standoff() => TestConfigs.Boss().HalfWidth + TestConfigs.Fighter().HalfWidth;

    /// <summary>패턴이 안 도는 판. 보스가 다가오는 것만 본다 (간격이 커서 첫 패턴 전에 끝난다).</summary>
    private static BattleSim Chaser() => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighter(),
        Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 400, patternGap: 1000),
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

        sim.Boss.X.ShouldBe(sim.Fighter.X + Standoff(), 0.001, "보스가 간격을 두고 서지 않는다");
    }

    [Fact]
    public void 파이터가_보스_몸을_통과한다()
    {
        // 몸 충돌을 걷었다(이슈 #27). 나인 솔즈처럼 적을 **그냥 지나갈 수 있어야** 한다 —
        // 밀어내기가 남아 있으면 보스가 붙는 순간 파이터는 벽 쪽으로 밀리고 빠져나갈 길이 없다.
        // 그 대가로 distance_bias 축을 겹침으로 세우던 방법은 잃는다 — 대신 패턴의 안전 거리대
        // (충격파의 distance[0]=220)가 "붙어야 안전" 을 만든다. 설계 문서 §6 에 적어 뒀다.
        var sim = Chaser();
        double standoff = Standoff();
        bool inside = false;

        for (int i = 0; i < 600; i++)
        {
            sim.Tick(new InputFrame(1, false, Dash: i % 13 == 0, false, false));
            if (sim.Fighter.Grounded && Math.Abs(sim.Fighter.X - sim.Boss.X) < standoff - 1e-9)
            {
                inside = true;
            }
        }

        inside.ShouldBeTrue("지상에서 보스 몸에 막혔다 — 몸 충돌이 아직 남아 있다");
    }

    /// <summary>보스가 제자리에 서 있고 패턴도 안 도는 판. <b>몸 충돌만</b> 본다.</summary>
    private static BattleSim StillBoss() => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighter(),
        Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 1000),
        PatternIds = new[] { "횡베기" },
        Patterns = Patterns(),
        Seed = 1,
        MaxTicks = 60 * 60,
    });

    /// <summary>보스를 지나칠 때까지 오른쪽으로 걷는다. 도중에 보스 몸 안을 지났는지까지 확인한다.</summary>
    private static void WalkPastBoss(BattleSim sim)
    {
        bool inside = false;
        for (int i = 0; i < 300; i++)
        {
            sim.Tick(new InputFrame(1, false, false, false, false));
            if (Math.Abs(sim.Fighter.X - sim.Boss.X) < Standoff() - 1e-9)
            {
                inside = true;
            }
        }

        inside.ShouldBeTrue("보스 몸 안을 한 번도 안 지났다 — 이 테스트가 통과를 안 본다");
    }

    [Fact]
    public void 지상에서도_보스를_가로질러_반대편으로_간다()
    {
        // 전에는 공중에서만 통과했다 — 점프가 유일한 탈출구였다. 이제 지상에서도 지나간다.
        // 보스는 여전히 자기 간격(Standoff)을 지키려 하지만 그건 **자기 걸음**일 뿐이라
        // 파이터를 밀지 않는다.
        var sim = StillBoss();
        WalkPastBoss(sim);

        sim.Fighter.X.ShouldBeGreaterThan(sim.Boss.X, "지상에서 보스를 지나 반대편으로 못 갔다");
    }

    [Fact]
    public void 착지해도_다시_안_밀려난다()
    {
        // 공중 통과가 "지상은 그대로" 였던 때는 착지하는 순간 다시 밀려났다. 몸 충돌이 통째로
        // 없어졌으므로 보스 몸 안에서 착지해도 그 자리에 선다 — 그래야 "통과한다" 가 참말이다.
        var sim = StillBoss();
        double standoff = Standoff();

        // 보스 몸 **안까지만** 걸어 들어간 뒤 멈춘다(보스는 moveSpeed 0 이라 안 비킨다).
        for (int i = 0; i < 300; i++)
        {
            bool inside = Math.Abs(sim.Fighter.X - sim.Boss.X) < standoff * 0.5;
            sim.Tick(new InputFrame((sbyte)(inside ? 0 : 1), Jump: i == 0, false, false, false));
        }

        sim.Fighter.Grounded.ShouldBeTrue("아직 공중이다 — 이 테스트가 착지를 안 본다");
        Math.Abs(sim.Fighter.X - sim.Boss.X)
            .ShouldBeLessThan(standoff, "착지하자마자 보스 몸 밖으로 밀려났다");
    }

    [Fact]
    public void 보스_몸을_지나는_대시도_무적이_그대로_돈다()
    {
        // 무적은 **위치가 아니라 행동 시계**로 돈다. 몸 충돌이 있던 때는 "벽에 막혀도 무적은
        // 그대로" 를 못박았고, 지금은 반대쪽 — 몸을 통과해 반대편으로 나가도 그대로다.
        // 어느 쪽이든 깨지면 "파고들어야 사는" 패턴(충격파)을 아무도 못 피한다.
        var setup = new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            // 걷는 틱 수와 patternGap 은 **짝이다** — 걸어 붙는 동안 패턴이 서면 대시가 아니라
            // 걷기가 판정을 받는다. 그래서 132틱(= 2.2초)으로 둘을 맞춰 둔다.
            // 거리는 데이터에서 온다 — 보스 반폭이 바뀌어도 검사는 한 글자도 안 바뀐다.
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 2.2),
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

        // 132틱 동안 오른쪽으로 걸어 보스 몸 안으로 들어간다(패턴은 2.2초 뒤에 선다).
        for (int i = 0; i < 132; i++)
        {
            sim.Tick(new InputFrame(1, false, false, false, false));
        }

        Math.Abs(sim.Fighter.X - sim.Boss.X)
            .ShouldBeLessThan(Standoff(), "보스 몸 안까지 못 걸었다 — 이 테스트가 통과하는 대시를 안 본다");

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
        // 이 축이 존재하는 이유 자체다. "값이 0 이 아니다" 로는 부족하고 **둘이 갈려야** 한다.
        // 몸 충돌을 걷은 뒤에도(이슈 #27) 갈리는지가 여기서 증명된다 — 붙는 봇은 보스 몸 안으로
        // 들어가고 떨어지는 봇은 제 간격을 지킨다. 축을 살리는 것은 이제 겹침이 아니라
        // **거리를 고를 이유**(충격파의 안쪽 안전지대)다.
        PlayerAxes hugger = Engage(0);
        PlayerAxes spacer = Engage(500);

        hugger.Samples.ShouldBeGreaterThan(0);
        spacer.Samples.ShouldBeGreaterThan(0);
        // 몸 충돌이 없어졌으므로 붙는 봇은 보스 몸 **안**까지 들어간다 — 그게 통과의 증거다.
        hugger.DistanceBias.ShouldBeLessThan(Standoff(), "붙는 봇이 보스 몸 안으로 못 들어갔다");
        spacer.DistanceBias.ShouldBeGreaterThan(hugger.DistanceBias + 200,
            "붙는 봇과 떨어지는 봇이 같은 거리로 수렴한다 — 축이 못 가른다");
    }

    /// <summary>봇이 판정을 기다리며 잡는 자리(px). 대시 사거리보다 멀어야 "파고든다" 가 성립한다.</summary>
    private const double _dashHold = 450;

    /// <summary>
    /// 판정 직전에 대시하는 봇 한 판. <paramref name="inward"/> 면 보스 쪽을, 아니면 반대쪽을 보고 뛴다.
    /// 대시는 <b>바라보는 쪽으로만</b> 가므로 어디를 보고 있었나가 곧 방향이다.
    /// </summary>
    private static PlayerAxes DashingBot(bool inward)
    {
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            Boss = TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = new[] { "횡베기", "지면쓸기", "충격파" },
            Patterns = Patterns(),
            Seed = 51,
            MaxTicks = 60 * 20,
        });

        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            var toward = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);
            double gap = Math.Abs(sim.Fighter.X - sim.Boss.X);
            double? left = sim.NextActiveIn;

            // 몸 충돌이 없어진 뒤로(이슈 #27) "매 틱 보스 쪽으로" 는 봇이 아니라 진동이 된다 —
            // 보스 중심에 얹혀 오가면 안팎이 틱마다 뒤집혀 방향 라벨이 성향이 아니라 부산물이 된다.
            // 그래서 ① 평소에는 대역 안에 자리를 잡고 ② 판정이 다가오면 **뛸 쪽을 먼저 본 뒤**
            // ③ 직전에 뛴다. 대시는 바라보는 쪽으로만 가므로 ②가 곧 방향이다.
            bool aiming = left is double lead && lead <= 0.30;
            sbyte move = aiming
                ? (inward ? toward : (sbyte)-toward)
                : gap < _dashHold ? (sbyte)-toward : toward;
            bool soon = left is double remaining && remaining <= 0.10;
            outcome = sim.Tick(new InputFrame(move, false, Dash: soon, false, false));
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
    /// 충격파 한 방을 보스로부터 <paramref name="standoff"/> px 떨어진 자리에서 맞아 본다.
    /// 패턴은 2.0초 뒤에 서므로 그 전에 자리를 잡는다. <b>걸음 수가 아니라 거리로 준다</b> —
    /// 몸 충돌이 없어져 "끝까지 걸으면 붙는다" 가 더는 참이 아니다(지나쳐 버린다).
    /// </summary>
    private static DodgeEvent Shockwave(double standoff)
    {
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 2.0),
            PatternIds = new[] { "충격파" },
            Patterns = Patterns(),
            Seed = 1,
            MaxTicks = 60 * 10,
        });

        for (int i = 0; i < 300 && sim.Events.Count == 0; i++)
        {
            double gap = Math.Abs(sim.Fighter.X - sim.Boss.X);
            sim.Tick(new InputFrame((sbyte)(gap > standoff ? 1 : 0), false, false, false, false));
        }

        return sim.Events.Single();
    }

    [Fact]
    public void 충격파는_붙은_쪽만_살려준다()
    {
        // 안쪽 220px 이 비어 있는 유일한 패턴이고, **몸 충돌을 걷은 지금 distance_bias 축을
        // 살려 두는 것이 이 하나다**(이슈 #27 · 설계 문서 §6). 충돌이 있던 때는 "밀려나서" 거리가
        // 갈렸지만 이제는 "붙는 것이 정답인 패턴이 있어서" 갈린다 — 후자가 판단이고 전자는 부산물이다.
        // 주머니가 닫히면(patterns.json 의 220 이 내려가면) 축은 조용히 다시 죽는다 — 여기서 빨개진다.
        DodgeEvent hugging = Shockwave(60);
        DodgeEvent spacing = Shockwave(400);

        hugging.Verdict.ShouldBe(HitVerdict.MissedByRange, "붙었는데 충격파에 맞았다 — 안쪽 주머니가 닫혔다");
        hugging.Distance.ShouldBeLessThan(220);

        spacing.Verdict.ShouldBe(HitVerdict.Hit, "떨어져 있는데 안 맞았다 — 이 테스트가 주머니를 안 본다");
        spacing.Distance.ShouldBeGreaterThan(220);
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
    public void 빈_명부는_첫_뽑기가_아니라_판을_세울_때_거절한다()
    {
        // 빈 목록을 그대로 받으면 Begin 의 Det.RollInt(n: 0) 이 터진다 — 첫 패턴이 설 때까지
        // 아무 일도 없다가, 판이 도는 도중에 C# 예외로 나온다. 그 예외는 우리 로그 형식이
        // 아니라 엔진 ERROR 블록으로만 보인다. 세우는 자리에서 막는다.
        BattleSetup setup = Setup();
        setup.PatternIds = System.Array.Empty<string>();

        Should.Throw<ArgumentException>(() => new BattleSim(setup));
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
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 10 * BattleSim.Dt),
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
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighter(),
        Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 3 * BattleSim.Dt),
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

    // ── 2단계 패리와 계측 (이슈 #27) ─────────────────────────────────────────

    /// <summary>
    /// 패리 가능한 판정 하나를 <paramref name="pressAt"/> 틱에 패리해 보고, 그 관측과
    /// <b>판정이 선 틱</b>을 같이 돌려준다. 그 틱을 손으로 안 적는 이유는 간격 소진이
    /// 부동소수 누적에 걸려 한 틱씩 밀릴 수 있기 때문이다 — 박아 두면 타임라인을 건드릴 때마다
    /// 무관한 실패가 난다.
    /// </summary>
    private static (DodgeEvent Event, int Tick) ParryAt(int? pressAt)
    {
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 2000 },
            height: new double[] { 0, 300 },
            parryable: true,
            at: 24 * BattleSim.Dt));

        for (int i = 1; i <= 30 && sim.Events.Count == 0; i++)
        {
            sim.Tick(new InputFrame(0, false, false, Parry: i == pressAt, false));
        }

        return (sim.Events.Single(), sim.Ticks);
    }

    [Fact]
    public void 정확_부정확_무반응이_서로_다른_관측이_된다()
    {
        // **이 브랜치가 답하는 숙제다.** 전에는 "늦게 눌렀다" 와 "아무것도 안 했다" 가 같은 점
        // (Verb=None · TimingError=0)이었다 — 패리 행동이 끝나면 시작 시각이 사라졌기 때문이다.
        // 부정확 단계가 그 중간을 만들고, 계측이 셋을 가른다. 셋이 다시 뭉치면 여기서 빨개진다.
        const int early = 12;
        (DodgeEvent precise, int hitTick) = ParryAt(26);   // 판정 코앞 — 정확 창(0.133) 안
        (DodgeEvent late, _) = ParryAt(early);             // 0.2초쯤 전 — 정확은 놓쳤고 부정확 창 안
        (DodgeEvent none, _) = ParryAt(null);              // 아무것도 안 했다

        precise.Verdict.ShouldBe(HitVerdict.Parried);
        precise.Verb.ShouldBe(DodgeVerb.Parry);
        precise.TimingError.ShouldBe((26 - hitTick) * BattleSim.Dt, 1e-9);

        late.Verdict.ShouldBe(HitVerdict.ParriedLate);
        late.Verb.ShouldBe(DodgeVerb.Parry, "부정확도 고른 수단은 패리다 — 의존도 축의 분자가 그것이다");
        late.TimingError.ShouldBe((early - hitTick) * BattleSim.Dt, 1e-9);
        late.TimingError.ShouldBeLessThan(-TestConfigs.Fighter().ParryPreciseWindow,
            "정확 창 안에서 누른 것이 부정확으로 기록됐다 — 이 테스트가 중간 단계를 안 본다");

        none.Verdict.ShouldBe(HitVerdict.Hit);
        none.Verb.ShouldBe(DodgeVerb.None);
        none.TimingError.ShouldBe(0);

        // 셋이 **서로 다른 점**인지 직접 못박는다. 하나씩 보면 다 맞는데 둘이 같은 값으로
        // 뭉쳐 있는 실패가 실제로 있었다.
        var points = new HashSet<(HitVerdict, DodgeVerb, double)>
        {
            (precise.Verdict, precise.Verb, precise.TimingError),
            (late.Verdict, late.Verb, late.TimingError),
            (none.Verdict, none.Verb, none.TimingError),
        };
        points.Count.ShouldBe(3, "정확 · 부정확 · 무반응이 같은 점으로 뭉쳤다");

        // 축 집계까지 따라가는지도 본다 — 관측이 갈려도 집계가 뭉치면 망은 못 본다.
        PlayerAxes axes = PlayerAxes.From(new[] { precise, late, none });
        axes.ParrySamples.ShouldBe(2);
        axes.ParryLateSamples.ShouldBe(1);
        axes.ParryRate.ShouldBe(0.5, 1e-9, "부정확을 성공으로 세면 패리 성공률이 거짓이 된다");
    }

    [Fact]
    public void 부정확_패리는_절반만_맞고_굳는다()
    {
        // 피해가 그대로면 "받아냈다" 가 관측에만 있고 판에는 없는 말이 된다.
        (DodgeEvent late, _) = ParryAt(12);
        late.Verdict.ShouldBe(HitVerdict.ParriedLate);

        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 2000 },
            height: new double[] { 0, 300 },
            parryable: true,
            at: 24 * BattleSim.Dt));
        for (int i = 1; i <= 30; i++)
        {
            sim.Tick(new InputFrame(0, false, false, Parry: i == 12, false));
        }

        // OneHit 의 피해는 5 — 절반은 반올림해 3 이다.
        sim.Fighter.Health.ShouldBe(TestConfigs.Fighter().MaxHealth - 3);
        sim.Fighter.InternalDamage.ShouldBe(3);
        sim.Fighter.Locked.ShouldBeTrue();
        sim.Fighter.Qi.ShouldBe(1);
    }

    [Fact]
    public void 정확_패리는_보스를_굳히고_공중_대시를_돌려준다()
    {
        // 보상 구조의 두 반쪽이다 (나인 솔즈). 굳는 동안 보스는 걷지도 타임라인을 밀지도 않는다.
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 2000 },
            height: new double[] { 0, 300 },
            parryable: true,
            at: 24 * BattleSim.Dt));

        for (int i = 1; i <= 30 && sim.Events.Count == 0; i++)
        {
            sim.Tick(new InputFrame(0, false, false, Parry: i == 24, false));
        }

        sim.Events.Single().Verdict.ShouldBe(HitVerdict.Parried);
        sim.Boss.Staggered.ShouldBeTrue();
        sim.Fighter.Health.ShouldBe(TestConfigs.Fighter().MaxHealth, "정확 패리인데 깎였다");

        double bossX = sim.Boss.X;
        for (int i = 0; i < 20; i++)
        {
            sim.Tick(default);
        }

        sim.Boss.X.ShouldBe(bossX, 1e-9, "굳었는데 보스가 걸었다");
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
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 3 * BattleSim.Dt),
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
