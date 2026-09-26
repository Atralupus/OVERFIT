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
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(maxHealth: bossHealth),
        PatternIds = new[] { "3연격", "점프 공격" },
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
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 400, patternGap: 1000),
        PatternIds = new[] { "3연격" },
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
        // 그 대가로 distance_bias 축을 겹침으로 세우던 방법은 잃는다 — 대신 판정 모양의 빈 곳
        // (3연격 3타의 초승달 안쪽 · 설계 §2)이 "붙어야 안전" 을 만든다.
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
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 1000),
        PatternIds = new[] { "3연격" },
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
        // 어느 쪽이든 깨지면 보스를 뚫고 나가는 대시(3연격을 등 뒤로 빠지는 길 · 설계 §2)를 아무도 못 쓴다.
        var setup = new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
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
        // **거리를 고를 이유**(3연격을 간격으로 피하려면 427px 밖 · 설계 §2)다.
        PlayerAxes hugger = Engage(0);
        PlayerAxes spacer = Engage(500);

        hugger.Samples.ShouldBeGreaterThan(0);
        spacer.Samples.ShouldBeGreaterThan(0);
        // 몸 충돌이 없어졌으므로 붙는 봇은 보스 몸 **안**까지 들어간다 — 그게 통과의 증거다.
        hugger.DistanceBias.ShouldBeLessThan(Standoff(), "붙는 봇이 보스 몸 안으로 못 들어갔다");
        spacer.DistanceBias.ShouldBeGreaterThan(hugger.DistanceBias + 200,
            "붙는 봇과 떨어지는 봇이 같은 거리로 수렴한다 — 축이 못 가른다");
    }

    /// <summary>
    /// 판정 직전에 대시하는 봇 한 판. <paramref name="inward"/> 면 보스 쪽을, 아니면 반대쪽을 보고 뛴다.
    /// 대시는 <b>바라보는 쪽으로만</b> 가므로 어디를 보고 있었나가 곧 방향이다.
    ///
    /// <para>
    /// ⚠ <b>보스 코앞에서 뛴다</b>(<see cref="StandoffDasher"/> 와 같은 봇이다). 멀리 서서 뛰면 대시를 하든 말든 판정이
    /// 어차피 안 닿아 대시 표본이 <b>0</b> 이 되고, 이 축이 아무것도 못 가른다(한 번 그렇게 빨개졌다).
    /// </para>
    /// </summary>
    private static PlayerAxes DashingBot(bool inward) => PlayerAxes.From(StandoffDasher(outward: !inward));

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

    [Fact]
    public void 공격이_닿으면_보스_체력이_준다()
    {
        var sim = new BattleSim(Setup());
        int before = sim.Boss.Health;
        // 보스가 오른쪽(1440)에 있으니 걸어가야 붙는다. **칼이 닿는 거리에 들어온 뒤에만** 휘두른다 — 칼질은 경직까지 커밋이라
        // (1타 0.28 + 0.40초 · #82) 걸으며 60틱마다 휘두르던 옛 대본은 250틱 중 205틱을 서서 끝내 못 닿았다. 붙기까지 실측 100틱
        // 안팎이다 — 여유를 두고 250틱까지 돈다. 휘두르는 것은 칼질이 없을 때뿐이라 스태미나가 버틴다.
        for (int i = 0; i < 250 && sim.Boss.Health == before; i++)
        {
            bool near = sim.Boss.X - sim.Fighter.X <= sim.Boss.HalfWidth + sim.FighterReach;
            bool free = sim.Fighter.Action == FighterAction.Idle;
            sim.Tick(new InputFrame((sbyte)(near ? 0 : 1), false, false, false, Attack: near && free));
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
        setup.PatternIds = new[] { "점프 공격" };
        var sim = new BattleSim(setup);

        for (int i = 0; i < 900; i++)
        {
            sim.Tick(default);
            if (sim.Boss.CurrentPattern is { } id)
            {
                id.ShouldBe("점프 공격");
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
        // 빈 목록을 그대로 받았을 때 Begin 의 Det.RollInt(n: 0) 이 터졌다 — 첫 패턴이 설 때까지
        // 아무 일도 없다가, 판이 도는 도중에 C# 예외로 나왔다. 그 예외는 우리 로그 형식이
        // 아니라 엔진 ERROR 블록으로만 보인다. 세우는 자리에서 막는다.
        BattleSetup setup = Setup();
        setup.PatternIds = System.Array.Empty<string>();

        Should.Throw<ArgumentException>(() => new BattleSim(setup));
    }

    [Fact]
    public void 칼의_모양이_없으면_판을_세울_때_거절한다()
    {
        // 빈 명부와 같은 이유다 — 칼이 처음 서는 틱에 모양을 찾다 틀리면 판이 한참 돈 뒤라 무엇이 빠졌는지가
        // 그 스택에 안 남는다. 빠진 id 를 메시지에 싣는다.
        BattleSetup setup = Setup();
        setup.HitShapes = new Dictionary<string, HitShape>();

        Should.Throw<ArgumentException>(() => new BattleSim(setup)).Message.ShouldContain(TestConfigs.TestSwordId);
    }

    [Fact]
    public void 판정의_모양이_없으면_판을_세울_때_빠진_것을_전부_말한다()
    {
        // 설계 §8.1 — 보스 판정의 모양은 판을 세울 때 한 번 찾고, 빠진 id 는 전부 모아 한 번에 거절한다(칼의 모양과 같은 규약).
        // 판정이 처음 서는 틱에 찾다 틀리면 판이 한참 돈 뒤라 무엇이 빠졌는지가 스택에 안 남는다.
        var pattern = new PatternDef
        {
            Tags = TestConfigs.Sweep(100, 0).Tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 0.5, Kind = "active", Hitbox = "없는팩/attack/2", Damage = 5 },
                new() { T = 1.0, Kind = "active", Hitbox = "없는팩/attack2/2", Damage = 5 },
                new() { T = 2.0, Kind = "end" },
            },
        };
        BattleSetup setup = Setup();
        setup.PatternIds = new[] { "단타" };
        setup.Patterns = new Dictionary<string, PatternDef> { ["단타"] = pattern };

        string message = Should.Throw<ArgumentException>(() => new BattleSim(setup)).Message;
        message.ShouldContain("없는팩/attack/2");
        message.ShouldContain("없는팩/attack2/2");
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
            HitShapes = TestConfigs.HitShapes(),
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

    [Fact]
    public void 간격과_패턴의_끝도_틱으로_센다()
    {
        // 설계 §3.6 ⑤ — 쉬는 간격도 틱으로 센다. 0.2초는 12틱이라 첫 패턴은 12틱에 서고, 2.0초(120틱)짜리 패턴은
        // 러너의 120번째 틱(판의 132틱)에 끝나며, 다음 패턴은 또 12틱 뒤(144틱)에 선다. 1/60 을 빼 가던 때는
        // 0.2 가 13틱 · 2.0 이 121틱이었다 — 부동소수 누적이 간격과 끝을 한 틱씩 늘였다.
        BattleSim sim = TestConfigs.SweepSim(maxDistance: 50, activeSeconds: 0);
        var begins = new List<int>();
        var ends = new List<int>();
        string? was = null;
        for (int i = 0; i < 200; i++)
        {
            sim.Tick(default);
            if (sim.Boss.CurrentPattern != was)
            {
                (sim.Boss.CurrentPattern is null ? ends : begins).Add(sim.Ticks);
                was = sim.Boss.CurrentPattern;
            }
        }

        begins.ShouldBe(new[] { 12, 144 });
        ends.ShouldBe(new[] { 132 });
    }

    /// <summary>도약 하나에 바닥 전체를 치는 착지 — 점프 공격(설계 §4.2)의 뼈대다. 뛰는 것은 0.4초 · 내리는 것은 1.0초다.</summary>
    private static PatternDef Leaping() => new()
    {
        Tags = new PatternTags
        {
            DashWindow = 0.2,
            DashDirection = "out",
            Jumpable = true,
            AntiAir = false,
            Parryable = false,
            ParryWindow = 0,
            PunishGreed = false,
            Reach = "far",
            MultiHit = 1,
            Tracking = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0.0, Kind = "windup" },
            new() { T = 0.4, Kind = "windup", Motion = new MotionDef { Id = "leap", Height = 280, Air = 0.6 } },
            new() { T = 1.0, Kind = "active", Band = new double[] { 0, 1920, 0, 60 }, Damage = 12, ActiveSeconds = 0.125 },
            new() { T = 1.5, Kind = "end" },
        },
    };

    [Fact]
    public void 도약하는_패턴은_보스를_띄워_파이터_앞에_내리고_내린_틱에_판정을_연다()
    {
        // 설계 §4.2 · §8.1 — 움직임은 BattleSim 이 돌린다(러너는 파이터를 모른다). 뛰는 틱의 파이터(480)에서 보스 쪽으로
        // 115 앞(595)에 내리고, 내리는 바로 그 틱에 착지 판정이 선다 — 보스는 판정 창 동안 안 움직인다(§3.5 6).
        BattleSim sim = OnePattern(Leaping());
        double top = 0;
        int lastAir = 0, airTicks = 0;
        for (int i = 1; i <= 120 && sim.Events.Count == 0; i++)
        {
            sim.Tick(default);
            if (sim.Boss.Y > 0)
            {
                top = Math.Max(top, sim.Boss.Y);
                lastAir = sim.Ticks;
                airTicks++;
            }
        }

        sim.Events.Count.ShouldBe(1, "착지 판정이 안 섰다");
        airTicks.ShouldBe(35, "뜬 틱(s = 1/36 ~ 35/36)이 35 가 아니다");
        top.ShouldBe(280, 1e-9);
        sim.Ticks.ShouldBe(lastAir + 1, "내린 틱과 판정이 선 틱이 다르다");
        sim.Boss.Y.ShouldBe(0);
        sim.Boss.X.ShouldBe(sim.Fighter.X + Standoff(), 1e-9);
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Hit, "땅에 선 파이터가 바닥 전체 착지에 안 맞았다");

        // 위의 Y · X 는 틱이 다 돈 뒤의 값이라 "판정을 댄 자리"를 못 본다 — 움직임을 판정 뒤로 옮기면 착지 판정이
        // 앞 틱의 공중 자리(X ≈ 618 · Y ≈ 30)에서 서고 보스는 그 뒤에 내리는데, 위의 넷은 그대로 초록이었다.
        // 그 자리의 띠는 30~90 이라 발이 60 위인 파이터(설계 §4.2 의 "넘는" 파이터)가 맞는다. 그래서 대 본 자리를 직접 본다.
        sim.BossTestedRects.ShouldNotBeEmpty("착지 틱에 판정을 안 댔다");
        sim.BossTestedRects.Min(r => r.Y0).ShouldBe(0, "착지 판정을 공중의 자리에서 댔다 — 움직임이 판정을 댄 뒤에 돈다");
        sim.Events[0].Distance.ShouldBe(Standoff(), 1e-9, "착지 판정을 내리기 전의 X 에서 댔다 — 움직임이 판정을 댄 뒤에 돈다");
    }

    /// <summary>판정 하나짜리 패턴. 기하와 태그를 부르는 쪽이 정한다.</summary>
    private static PatternDef OneHit(
        double[] distance, double[] height, bool parryable, double at) => new()
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
                MultiHit = 1,
                Tracking = false,
            },
            Timeline = new List<PatternStep>
        {
            new() { T = at, Kind = "active", Band = new[] { distance[0], distance[1], height[0], height[1] }, Damage = 5 },
            new() { T = at + (6 * BattleSim.Dt), Kind = "end" },
        },
        };

    /// <summary>보스를 제자리에 세우고 <paramref name="pattern"/> 하나만 돌리는 판.</summary>
    private static BattleSim OnePattern(PatternDef pattern) => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighter(),
        HitShapes = TestConfigs.HitShapes(),
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
        //
        // **이 테스트가 이슈 #46 의 반례 가드다.** "대시가 돌고 있으면 거리 miss 를 대시의 공으로"
        // 라고만 고치면 여기가 빨개진다 — 대시는 돌지만 그 대시가 이 거리를 만들지 않았다
        // (960px 은 대시 전에도 사거리 100 밖이었다). 그래서 공을 돌리는 조건은 "대시 중" 이 아니라
        // **"대시 시작 자리에서는 닿았는가"** 다.
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
        e.Verdict.ShouldBe(HitVerdict.MissedTooFar);
        e.Verb.ShouldBe(DodgeVerb.Spacing, "대시가 만들지 않은 거리가 대시의 공이 됐다");
        e.TimingError.ShouldBe(0);
        e.Direction.ShouldBe(0);
    }

    [Fact]
    public void 밖으로_한_대시가_만든_거리는_대시의_공이다()
    {
        // **이 이슈가 고치는 것이다** (이슈 #46). 판정 순서가 거리 → 높이 → 대시무적이라
        // 대시로 사거리를 벗어나면 무적이 보이기도 전에 거리에서 빠진다. 실제 수치로:
        // 유효 무적 8틱 · 대시 36.67px/틱 · 서는 자리 115 에서 밖으로 나가면 250 을 4틱째 넘으므로
        // **무적 8틱 중 3틱만 Dodged** 이고 나머지는 Spacing 이었다 — 대시 의존자가 간격 의존자로
        // 기록되고, 2단계가 정반대 패턴을 뽑는다.
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 1000 },
            height: new double[] { 0, 300 },
            parryable: false,
            at: 6 * BattleSim.Dt));

        for (int i = 1; i <= 14; i++)
        {
            // 1틱: 보스 반대쪽을 본다 — 대시는 **바라보는 쪽으로만** 간다. 2틱: 대시.
            // 파이터 480 · 보스 1440 이므로 대시 전 거리는 960 으로 사거리(1000) **안**이다.
            // 그 한 줄이 이 테스트와 바로 위 반례 가드를 가른다.
            sim.Tick(new InputFrame((sbyte)(i == 1 ? -1 : 0), false, Dash: i == 2, false, false));
        }

        sim.Events.Count.ShouldBe(1);
        DodgeEvent e = sim.Events[0];
        e.Verdict.ShouldBe(HitVerdict.MissedTooFar);
        e.Distance.ShouldBeGreaterThan(1000, "대시가 사거리 밖으로 못 데려갔다 — 이 테스트가 그 자리를 안 본다");
        e.Verb.ShouldBe(DodgeVerb.Dash, "대시가 만든 거리인데 간격의 공이 됐다");
        e.Direction.ShouldBe(-1, "밖으로 뛴 대시인데 방향이 안 실렸다");
        e.TimingError.ShouldBeLessThan(0, "대시의 시작 시각이 안 실렸다");

        // 축까지 따라가는지 본다 — 관측이 갈려도 집계가 안 받으면 망은 못 본다.
        PlayerAxes axes = PlayerAxes.From(sim.Events);
        axes.DashSamples.ShouldBe(1);
        axes.DashDirectionBias.ShouldBe(-1, 1e-9, "밖으로 뛴 대시가 방향 축에 안 들어갔다");
    }

    [Fact]
    public void 대시가_만든_거리인지는_시작_자리의_점이_아니라_몸통으로_잰다()
    {
        // 반사실(이슈 #46)은 판정과 **같은 기하**로 재야 한다 (이슈 #59). 판정은 몸통(중심 ± 30)을 모양에
        // 대는데 반사실만 점(발 중심)으로 재면 경계에서 둘이 다른 말을 한다 — 대시 시작 자리에 그대로
        // 서 있었으면 **맞았을** 사람을 "거기서도 안 닿았다" 로 읽어, 대시가 만든 거리를 간격의 공으로 돌린다.
        //
        // 위 테스트와 같은 판이고 바깥끝만 1000 → 950 이다. 대시 시작 자리는 중심 967 · 몸 안끝 937 이라
        // **점은 띠 밖이고 몸통은 띠 안이다** — 경계를 그 30px 사이에 놓아야 점과 몸통이 갈린다.
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 950 },
            height: new double[] { 0, 300 },
            parryable: false,
            at: 6 * BattleSim.Dt));

        sim.Tick(new InputFrame(-1, false, false, false, false));   // 보스 반대쪽을 본다 — 다음 틱의 대시가 이 자리에서 선다
        double start = Math.Abs(sim.Fighter.X - sim.Boss.X);
        start.ShouldBeGreaterThan(950, "대시 시작 자리의 중심이 띠 안이다 — 이 테스트가 점과 몸통을 못 가른다");
        (start - sim.Fighter.HalfWidth).ShouldBeLessThanOrEqualTo(950, "대시 시작 자리의 몸통이 띠 밖이다 — 이 테스트가 점과 몸통을 못 가른다");

        for (int i = 2; i <= 14; i++)
        {
            sim.Tick(new InputFrame(0, false, Dash: i == 2, false, false));
        }

        sim.Events.Count.ShouldBe(1);
        DodgeEvent e = sim.Events[0];
        e.Verdict.ShouldBe(HitVerdict.MissedTooFar, "대시가 사거리 밖으로 못 데려갔다 — 이 테스트가 거리 miss 를 안 본다");
        e.Verb.ShouldBe(DodgeVerb.Dash, "대시 시작 자리의 몸통은 맞았을 자리인데 간격의 공이 됐다 — 반사실이 몸을 점으로 잰다");
        e.Direction.ShouldBe(-1);
    }

    [Fact]
    public void 공중에서_뛴_대시의_반사실은_그_높이의_몸통으로_잰다()
    {
        // 반사실의 몸통은 **대시를 시작한 높이**에 있다 (이슈 #59 · BattleSim 의 wasY). 낮게 쓰는 판정(바닥 ~ 70)
        // 위로 떠서 대시했으면 그 자리에 그대로 있었어도 안 맞았다 — 거리를 만든 것이 대시가 아니므로 간격이다.
        // 몸통을 바닥에 세우거나 높이를 아예 안 보면 이 판정을 "대시가 빼냈다" 로 읽어, dash_timing_bias 에
        // 판정과 무관한 대시가 섞인다. 가로로는 대시 시작 자리의 **중심까지** 띠 안(967 < 1000)이라
        // 이 테스트를 가르는 것은 높이 하나다.
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 1000 },
            height: new double[] { 0, 70 },
            parryable: false,
            at: 6 * BattleSim.Dt));

        // 1틱: 보스 반대쪽을 보며 뛴다. 8틱: 공중 대시(착지 전 한 번은 된다). 판정은 11틱에 선다.
        for (int i = 1; i <= 7; i++)
        {
            sim.Tick(new InputFrame((sbyte)(i == 1 ? -1 : 0), Jump: i == 1, false, false, false));
        }

        sim.Fighter.Y.ShouldBeGreaterThan(70, "대시 시작 높이의 몸통이 판정에 걸친다 — 이 테스트가 높이를 못 가른다");
        Math.Abs(sim.Fighter.X - sim.Boss.X).ShouldBeLessThan(1000, "대시 시작 자리가 가로로 띠 밖이다 — 이 테스트가 높이를 못 가른다");

        for (int i = 8; i <= 20 && sim.Events.Count == 0; i++)
        {
            sim.Tick(new InputFrame(0, false, Dash: i == 8, false, false));
        }

        sim.Events.Count.ShouldBe(1);
        DodgeEvent e = sim.Events[0];
        e.Verdict.ShouldBe(HitVerdict.MissedTooFar, "대시가 사거리 밖으로 못 데려갔다 — 이 테스트가 거리 miss 를 안 본다");
        e.Airborne.ShouldBeTrue();
        e.Verb.ShouldBe(DodgeVerb.Spacing, "낮은 판정 위에 떠 있던 자리에서 뛴 대시가 공을 가져갔다 — 반사실이 대시 시작 높이를 버렸다");
    }

    [Fact]
    public void 안쪽_주머니로_파고든_대시도_대시의_공이다()
    {
        // 밖으로만 고치면 반쪽이다. 안쪽 주머니(모양 안의 빈 곳 — 3타의 초승달 안쪽이 이 모양이다)로 파고들어
        // 피한 것도 **그 거리를 대시가 만들었으면** 대시다 — 여기가 간격으로 기록되면 파고드는 습관을 못 읽는다.
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            // 파이터가 주머니 앞까지 걸어갈 시간을 준다 (960 → 300 이 95틱이다).
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 100 * BattleSim.Dt),
            PatternIds = new[] { "단타" },
            Patterns = new Dictionary<string, PatternDef>
            {
                ["단타"] = OneHit(
                    distance: new double[] { 190, 2000 },
                    height: new double[] { 0, 300 },
                    parryable: false,
                    at: 6 * BattleSim.Dt),
            },
            Seed = 1,
            MaxTicks = 60 * 5,
        });

        for (int i = 1; i <= 200 && sim.Events.Count == 0; i++)
        {
            // 주머니 밖 300px 에 자리를 잡고(여기서는 닿는다) 판정 직전에 안으로 뛴다.
            double gap = Math.Abs(sim.Fighter.X - sim.Boss.X);
            bool soon = sim.NextActiveIn is double left && left <= 6 * BattleSim.Dt;
            sim.Tick(new InputFrame(
                (sbyte)(!soon && gap > 300 ? 1 : 0),
                false,
                Dash: soon && sim.Fighter.Action == FighterAction.Idle,
                false,
                false));
        }

        sim.Events.Count.ShouldBe(1);
        DodgeEvent e = sim.Events[0];
        e.Verdict.ShouldBe(HitVerdict.MissedByGap);
        e.Distance.ShouldBeLessThan(190, "대시가 주머니 안까지 못 데려갔다 — 이 테스트가 그 자리를 안 본다");
        e.Verb.ShouldBe(DodgeVerb.Dash, "파고든 대시가 간격의 공이 됐다");
        e.Direction.ShouldBe(1, "안으로 뛴 대시인데 방향이 안 실렸다");
        e.TimingError.ShouldBeLessThan(0);
    }

    /// <summary>
    /// 보스 코앞(간격 <c>Standoff</c>)에 서 있다가 판정 직전에 <b>밖으로</b> 뛰는 봇 한 판.
    /// 이슈 #46 의 산수가 서는 자리 그대로다 — 진짜 patterns.json 으로 돈다.
    ///
    /// <para>
    /// 대시는 바라보는 쪽으로만 가고 방향은 Idle 일 때만 바뀌므로, 판정이 다가오면
    /// <b>뛸 쪽을 한 틱 먼저 보고</b> 그다음 틱에 뛴다.
    /// </para>
    /// </summary>
    private static IReadOnlyList<DodgeEvent> StandoffDasher(bool outward)
    {
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = new[] { "3연격", "점프 공격" },
            Patterns = Patterns(),
            Seed = 51,
            MaxTicks = 60 * 20,
        });

        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            var toward = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);
            double gap = Math.Abs(sim.Fighter.X - sim.Boss.X);
            bool soon = sim.NextActiveIn is double left && left <= 0.10;

            sbyte move;
            bool dash = false;
            if (soon)
            {
                int want = outward ? -toward : toward;
                if (sim.Fighter.Facing == want)
                {
                    dash = true;
                    move = 0;
                }
                else
                {
                    move = (sbyte)want;
                }
            }
            else
            {
                move = (sbyte)(gap > Standoff() + 10 ? toward : 0);
            }

            outcome = sim.Tick(new InputFrame(move, false, dash, false, false));
        }

        return sim.Events;
    }

    [Fact]
    public void 밖으로_뛰는_사람이_간격_의존자로_안_읽힌다()
    {
        // **이 이슈의 주장 그 자체다** (이슈 #46). 위의 단타 테스트가 규칙을 못박는다면
        // 이것은 진짜 패턴 기하에서 그 규칙이 실제로 무엇을 바꾸는지를 잰다.
        //
        // 실측(시드 51 · 관측 19건): 고치기 전에는 대시 1 · 간격 15 였다. **일곱 번 뛴 사람이
        // 한 번 뛴 사람으로 기록되고 나머지는 간격의 공이 됐다** — 2단계는 이 기록을 보고
        // 대시가 아니라 간격을 봉인한다. 정확히 반대 패턴이다.
        IReadOnlyList<DodgeEvent> events = StandoffDasher(outward: true);
        int dashMissed = events.Count(e => e.Verb == DodgeVerb.Dash
            && e.Verdict is HitVerdict.MissedTooFar or HitVerdict.MissedByGap);
        PlayerAxes axes = PlayerAxes.From(events);

        axes.DashSamples.ShouldBeGreaterThan(0);
        dashMissed.ShouldBeGreaterThan(axes.DashSamples / 2,
            "대시 표본의 절반 이상이 간격으로 새던 자리다 — 여기가 0 이면 계측이 다시 거짓말한다");
        axes.DashDirectionBias.ShouldBeLessThan(-0.5, "밖으로만 뛰었는데 방향 축이 안 따라온다");

        // 안으로 뛰는 쪽도 이제 거리로 빠진다 (#72) — 3연격의 궤적이 그림의 모양이 되면서 1타는 등 뒤 −110 까지만 치고
        // 3타는 등 뒤를 땅에서 안 친다(설계 §2). 보스를 뚫고 나간 대시가 그 판정을 거리로 벗어나면 그것도 대시가 만든
        // 거리라 대시의 공이고, 방향은 안(+1)이다 — 거리로 빠진 판정이 방향까지 제대로 실리는지를 본다.
        IReadOnlyList<DodgeEvent> inward = StandoffDasher(outward: false);
        List<DodgeEvent> inwardMissed = inward.Where(e => e.Verb == DodgeVerb.Dash
            && e.Verdict is HitVerdict.MissedTooFar or HitVerdict.MissedByGap).ToList();
        inwardMissed.ShouldNotBeEmpty("보스를 뚫고 나간 대시가 거리로 빠진 판정이 없다 — 이 테스트가 안쪽을 안 본다");
        inwardMissed.ShouldAllBe(e => e.Direction == 1, "안으로 뛰어 거리로 빠진 대시의 방향이 안이 아니다");
        PlayerAxes.From(inward).DashDirectionBias.ShouldBeGreaterThan(0.5, "안으로만 뛰었는데 방향 축이 안 따라온다");
    }

    [Fact]
    public void 대시가_끝난_뒤에_선_판정은_간격이다()
    {
        // 이슈 #46 의 ⚠ 에 대한 답이다. 대시가 만든 거리인데 대시는 이미 끝난 자리 —
        // **경계를 대시 행동이 끝나는 곳에 둔다.** 무적(8틱)은 대시(11틱)보다 짧으므로
        // 무적 창은 통째로 대시의 공이 되고, 그 뒤로 사거리 밖에 남아 있는 것은 **그 자리에
        // 서 있기로 한 것**이라 간격이다. 유예 창을 두면 "얼마나 오래 봐주나" 라는 수치가
        // 새로 생기고(데이터에 없는 수치다) 그만큼 대시가 간격의 표본을 먹는다. 대시 행동은 대시 뒤 경직까지다(#82) — 경직 중에 선
        // 판정이 대시의 것인 것은 FighterStiffTests 가 본다. 여기 판정은 경직까지 끝난 뒤에 선다.
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 1000 },
            height: new double[] { 0, 300 },
            parryable: false,
            at: 20 * BattleSim.Dt));

        for (int i = 1; i <= 30; i++)
        {
            // 대시는 2틱에 시작해 12틱에 끝나고, 경직(6틱)까지 18틱에 끝난다. 판정은 23틱 언저리다.
            sim.Tick(new InputFrame((sbyte)(i == 1 ? -1 : 0), false, Dash: i == 2, false, false));
        }

        sim.Events.Count.ShouldBe(1);
        DodgeEvent e = sim.Events[0];
        e.Verdict.ShouldBe(HitVerdict.MissedTooFar);
        e.Verb.ShouldBe(DodgeVerb.Spacing, "대시는 이미 끝났는데 그 공이 계속 따라다닌다");
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

    // ── 방어 하나와 계측 (이슈 #27 · #53) ────────────────────────────────────

    /// <summary>
    /// 패리 가능한 판정 하나를 <paramref name="pressAt"/> 틱에 K 로 받아 본다. 관측과 <b>판정이 선 틱</b>을 돌려준다.
    /// <paramref name="at"/> 은 판정이 서는 패턴 틱이다(기본 24 — <see cref="OneAt"/>).
    /// </summary>
    private static (DodgeEvent Event, int Tick) ParryAt(int? pressAt, int at = 24) =>
        OneAt(i => new InputFrame(0, false, false, Parry: i == pressAt, false), at);

    /// <summary>같은 판정을 <paramref name="from"/> 틱부터 ↓ 를 붙들어 가드로 받아 본다 (설계 §5.2).</summary>
    private static (DodgeEvent Event, int Tick) GuardFrom(int from) =>
        OneAt(i => new InputFrame(0, false, false, false, false, GuardHeld: i >= from));

    /// <summary>
    /// 패리 가능한 판정 하나(패턴의 <paramref name="at"/> 틱째 · 기본 24)를 틱마다 <paramref name="input"/> 으로 받아 본다.
    ///
    /// <para>
    /// 판정 틱을 손으로 안 적는 이유는 간격과 시각이 바뀔 때마다 그 숫자가 같이 움직이기 때문이다 —
    /// 박아 두면 타임라인을 건드릴 때마다 무관한 실패가 난다.
    /// </para>
    /// </summary>
    private static (DodgeEvent Event, int Tick) OneAt(Func<int, InputFrame> input, int at = 24)
    {
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 2000 },
            height: new double[] { 0, 300 },
            parryable: true,
            at: at * BattleSim.Dt));

        for (int i = 1; i <= at + 6 && sim.Events.Count == 0; i++)
        {
            sim.Tick(input(i));
        }

        return (sim.Events.Single(), sim.Ticks);
    }

    [Fact]
    public void 패리_가드_무반응이_서로_다른_관측이_된다()
    {
        // **계측은 여전히 셋으로 갈린다** (이슈 #53) — 갈리는 자리만 바뀌었다.
        // 전에는 정확 · 부정확 · 무반응이었고, 지금은 **패리 · 가드 · 무반응**이다.
        // 셋이 다시 뭉치면 여기서 빨개진다.
        const int early = 12;
        (DodgeEvent parried, int hitTick) = ParryAt(26);              // 판정 코앞 — 창(0.133) 안
        (DodgeEvent guarded, _) = GuardFrom(early);                   // 일찍부터 ↓ 를 붙들고 있었다
        (DodgeEvent none, _) = ParryAt(null);                         // 아무것도 안 했다

        parried.Verdict.ShouldBe(HitVerdict.Parried);
        parried.Verb.ShouldBe(DodgeVerb.Parry);
        parried.TimingError.ShouldBe((26 - hitTick) * BattleSim.Dt, 1e-9);

        guarded.Verdict.ShouldBe(HitVerdict.Guarded);
        guarded.Verb.ShouldBe(DodgeVerb.Guard, "붙들어 막은 것을 패리로 세면 성공률이 거짓이 된다");
        guarded.TimingError.ShouldBe((early - hitTick) * BattleSim.Dt, 1e-9);
        guarded.TimingError.ShouldBeLessThan(-TestConfigs.Fighter().ParryPreciseWindow,
            "창 안에서 누른 것이 가드로 기록됐다 — 이 테스트가 두 갈래를 안 본다");

        none.Verdict.ShouldBe(HitVerdict.Hit);
        none.Verb.ShouldBe(DodgeVerb.None);
        none.TimingError.ShouldBe(0);

        // 셋이 **서로 다른 점**인지 직접 못박는다. 하나씩 보면 다 맞는데 둘이 같은 값으로
        // 뭉쳐 있는 실패가 실제로 있었다.
        var points = new HashSet<(HitVerdict, DodgeVerb, double)>
        {
            (parried.Verdict, parried.Verb, parried.TimingError),
            (guarded.Verdict, guarded.Verb, guarded.TimingError),
            (none.Verdict, none.Verb, none.TimingError),
        };
        points.Count.ShouldBe(3, "패리 · 가드 · 무반응이 같은 점으로 뭉쳤다");

        // 축 집계까지 따라가는지도 본다 — 관측이 갈려도 집계가 뭉치면 망은 못 본다.
        PlayerAxes axes = PlayerAxes.From(new[] { parried, guarded, none });
        axes.ParrySamples.ShouldBe(1);
        axes.GuardSamples.ShouldBe(1);
        axes.ParryRate.ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void 늦은_패리는_그냥_맞고_붙든_가드는_막는다()
    {
        // 스펙이 패리와 가드를 다시 갈랐다 (설계 §5.3: "그 밖이면 그냥 맞는다 — 가드가 아니다"). 같은 시각(12틱)에
        // K 를 누른 것과 ↓ 를 붙든 것이 맞은 것과 막은 것으로 갈린다. 12틱에 누른 패리는 판정(27틱 언저리)에서
        // 창 밖(0.25초)이지만 커밋(0.333초) 안이다 — 그 시도는 패리의 것으로 남는다.
        (DodgeEvent late, _) = ParryAt(12);
        (DodgeEvent guarded, _) = GuardFrom(12);

        late.Verdict.ShouldBe(HitVerdict.Hit, "창을 놓친 패리가 막았다 — 패리가 다시 가드가 됐다");
        late.Verb.ShouldBe(DodgeVerb.Parry, "눌렀다 놓친 것은 패리 시도다 — 무반응과 같은 점이면 안 된다");
        guarded.Verdict.ShouldBe(HitVerdict.Guarded);
    }

    [Fact]
    public void 경직까지_끝난_뒤에_맞은_판정은_패리의_공이_아니다()
    {
        // 이 계획이 정한 것 6 — 패리 시도의 공은 **패리 행동이 도는 동안만** 산다(대시의 공이 대시 행동이 도는 동안인 것과
        // 같은 경계). 행동이 끝난 뒤에 선 판정까지 그 누름의 시도로 세면 사람이 한 적 없는 표본이 parry 축에 섞이고,
        // "아무것도 안 하고 맞았다" 가 "일찍 누르고 맞았다" 로 기록된다. (#82) 패리 행동은 이제 **커밋 뒤 경직까지**다 — 경직 중에 맞은 판정은
        // 아래 짝 테스트가 본다. 그래서 여기는 경직까지 끝난 뒤(누른 뒤 44틱)에 판정을 세운다: 전에는 24틱짜리 판(커밋 20틱 뒤)이었다.
        FighterConfig c = TestConfigs.Fighter();
        const int press = 3;
        (DodgeEvent e, int hitTick) = ParryAt(press, at: 44);

        ((hitTick - press) * BattleSim.Dt).ShouldBeGreaterThan(c.ParryDuration + (BattleSim.TicksFor(c.ParryStiff) * BattleSim.Dt),
            "판정이 패리 행동 안에 섰다 — 이 테스트가 경직까지 끝난 뒤를 안 본다");
        e.Verdict.ShouldBe(HitVerdict.Hit);
        e.Verb.ShouldBe(DodgeVerb.None, "경직까지 끝난 누름이 이 판정의 공을 가져갔다");
        e.TimingError.ShouldBe(0);
    }

    [Fact]
    public void 커밋_뒤_경직_중에_맞은_판정은_그_패리의_시도다()
    {
        // (#82) 위 테스트의 짝 — 커밋(0.333초)은 끝났지만 패리 뒤 경직(parry_stiff) 안에 선 판정이다. 경직 동안 파이터는 서 있기로 한 것이
        // 아니라 **못 움직이는** 것이라 그 맨몸은 그 패리가 만든 것이다 — 대시 경직 중의 판정이 대시의 것인 것과 같은 규칙이다
        // (FighterStiffTests). 공의 시각은 누른 틱이라 오차가 크게 음수다: "너무 일찍 눌러 커밋에 묶였다" 가 그대로 실린다.
        // 경직이 없던 때는 이 판정이 None 이었다(아무것도 안 하고 맞은 것과 같은 점).
        FighterConfig c = TestConfigs.Fighter();
        const int press = 3;
        (DodgeEvent e, int hitTick) = ParryAt(press);

        double since = (hitTick - press) * BattleSim.Dt;
        since.ShouldBeGreaterThan(c.ParryDuration, "판정이 커밋 안에 섰다 — 이 테스트가 경직을 안 본다");
        since.ShouldBeLessThan(c.ParryDuration + (BattleSim.TicksFor(c.ParryStiff) * BattleSim.Dt), "판정이 경직 밖에 섰다");
        e.Verdict.ShouldBe(HitVerdict.Hit);
        e.Verb.ShouldBe(DodgeVerb.Parry, "패리 뒤 경직 중의 판정이 그 패리의 시도로 안 실렸다");
        e.TimingError.ShouldBeLessThan(-c.ParryDuration, "공의 시각이 누른 틱이 아니다");
    }

    [Fact]
    public void 어느_타든_받아치면_보스가_탈진하고_연격이_끊긴다()
    {
        // #72 · 설계 §4.3 — 받아치면 **어느 타든** 연격이 그 자리에서 끊기고 보스가 탈진한다. 이슈 #53 은 반대였다:
        // 앞의 연타를 받아친 상은 피해 0 뿐이고 박자를 고정했다. 스펙이 뒤집었다 — 끊기면 뒤의 박자가 없으니 흔들릴 것도 없다.
        // 기 +1 과 공중 대시 회복은 그대로다.
        BattleSim sim = ParryNthOfTwo(1);

        sim.Events[0].Verdict.ShouldBe(HitVerdict.Parried);
        sim.Boss.Exhausted.ShouldBeTrue("1타를 받아쳤는데 보스가 안 무너졌다");
        sim.Boss.CurrentPattern.ShouldBeNull("탈진했는데 패턴이 안 끊겼다");
        sim.Fighter.Health.ShouldBe(TestConfigs.Fighter().MaxHealth, "받아쳤는데 깎였다");
        sim.Fighter.Qi.ShouldBe(1);
        sim.Fighter.AirDashSpent.ShouldBeFalse();

        // 끊겼는지는 탈진이 풀린 **뒤**에야 보인다 — 탈진 90틱 동안은 끊기지 않은 러너도 서 있어 2타를 못 낸다. 그래서 다음
        // 패턴이 설 때까지 민다: 러너가 안 끊겼다면 탈진이 풀린 그 사이에 남은 2타를 낸다.
        for (int i = 0; i < 60 * 3 && (sim.Boss.Exhausted || sim.Boss.CurrentPattern is null); i++)
        {
            sim.Tick(default);
        }

        sim.Boss.CurrentPattern.ShouldNotBeNull("탈진이 풀렸는데 다음 패턴이 안 섰다");
        sim.Events.Count.ShouldBe(1, "끊긴 패턴의 2타가 왔다");
    }

    [Fact]
    public void 한_번의_대시가_연속타_두_대를_모두_설명한다()
    {
        // 대시 무적(0.14초) 한 번이 멀티히트 판정 두 개를 다 덮도록 타임라인을 짠다.
        // Land 가 첫 판정에서 회피 행동을 지워 버리면 두 번째 판정은 "아무것도 안 했다"로
        // 잘못 기록된다 — 근거가 없는 게 아니라 잘못 붙는 사고다. 그래서 행동은 Land 가 아니라
        // DodgeCredit.Remember 가 그 행동이 끝났을 때만 지운다.
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
                MultiHit = 2,
                Tracking = false,
            },
            Timeline = new List<PatternStep>
            {
                new() { T = 3 * BattleSim.Dt, Kind = "active", Band = new double[] { 0, 2000, 0, 300 }, Damage = 5 },
                new() { T = 6 * BattleSim.Dt, Kind = "active", Band = new double[] { 0, 2000, 0, 300 }, Damage = 5 },
                new() { T = 10 * BattleSim.Dt, Kind = "end" },
            },
        };

        var setup = new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
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

    // ── 가드 (이슈 #47 · #53) ────────────────────────────────────────────────

    /// <summary>
    /// 판정 하나를 <b>가드로</b> 받아 본다. 첫 틱부터 ↓ 를 붙든다 (설계 §5.2).
    /// </summary>
    private static BattleSim GuardOne()
    {
        var sim = OnePattern(OneHit(
            distance: new double[] { 0, 2000 },
            height: new double[] { 0, 300 },
            parryable: true,
            at: 30 * BattleSim.Dt));

        for (int i = 1; i <= 40 && sim.Events.Count == 0; i++)
        {
            sim.Tick(new InputFrame(0, false, false, false, false, GuardHeld: true));
        }

        return sim;
    }

    private const int _firstHit = 24;
    private const int _lastHit = 54;

    /// <summary>
    /// 판정 <b>둘</b>짜리 패턴. 단타가 아니라 두 대인 것이 요점이다: 어느 타를 받아쳤는가 하나만 다른 두 판을 만든다.
    /// </summary>
    private static PatternDef TwoHits() => new()
    {
        Tags = new PatternTags
        {
            DashWindow = 0,
            DashDirection = "out",
            Jumpable = false,
            AntiAir = false,
            Parryable = true,
            ParryWindow = 0.12,
            PunishGreed = false,
            Reach = "far",
            MultiHit = 2,
            Tracking = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = _firstHit * BattleSim.Dt, Kind = "active", Band = new double[] { 0, 2000, 0, 300 }, Damage = 5 },
            new() { T = _lastHit * BattleSim.Dt, Kind = "active", Band = new double[] { 0, 2000, 0, 300 }, Damage = 5 },
            new() { T = (_lastHit + 6) * BattleSim.Dt, Kind = "end" },
        },
    };

    /// <summary>
    /// 두 대짜리 패턴에서 <paramref name="which"/> 번째(1 또는 2)만 <b>받아친다.</b>
    /// 나머지 한 대는 그냥 맞는다 — 무엇을 받아쳤는가 하나만 다른 두 판을 만드는 것이 목적이다.
    /// </summary>
    private static BattleSim ParryNthOfTwo(int which)
    {
        var sim = OnePattern(TwoHits());
        int press = which == 1 ? _firstHit : _lastHit;

        // 여유를 넉넉히 둔다 — 패턴은 간격(3틱) 뒤에 서므로 판정은 타임라인 시각보다 그만큼
        // 늦게 온다. 누름은 그래도 창(0.12초 = 7.2틱) 안이라 시각을 안 옮겨도 받아친다.
        for (int i = 1; i <= _lastHit + 12 && sim.Events.Count < which; i++)
        {
            sim.Tick(new InputFrame(0, false, false, Parry: i == press, false));
        }

        return sim;
    }

    /// <summary>보스가 탈진해 있는 남은 틱. <b>틱을 세어 잰다</b> — 데이터 값을 손으로 안 베낀다.</summary>
    private static int ExhaustLeft(BattleSim sim)
    {
        int ticks = 0;
        while (sim.Boss.Exhausted && ticks < 60 * 10)
        {
            sim.Tick(default);
            ticks++;
        }

        return ticks;
    }

    [Fact]
    public void 가드로_받으면_깎여서_맞고_스태미나를_문다()
    {
        BattleSim sim = GuardOne();
        DodgeEvent e = sim.Events.Single();
        FighterConfig c = TestConfigs.Fighter();

        e.Verdict.ShouldBe(HitVerdict.Guarded);
        e.Verb.ShouldBe(DodgeVerb.Guard);
        e.TimingError.ShouldBeLessThan(0, "가드가 판정보다 먼저 섰는데 시각이 안 실렸다");

        // OneHit 의 피해는 5 — 0.25 는 반올림해 1 이고 값은 5 × 1.8 = 9 다.
        sim.Fighter.Health.ShouldBe(c.MaxHealth - 1);
        // 가드를 드는 값은 없다(이 계획이 정한 것 1) — 값은 막아낸 피해에 비례하는 9 뿐이다.
        sim.Fighter.Stamina.ShouldBe(c.MaxStamina - 9, 1e-9);
        sim.Fighter.Locked.ShouldBeFalse("깨지지도 않았는데 굳었다");
        sim.Fighter.Guarding.ShouldBeTrue("받아낸 가드가 풀렸다");
    }

    [Fact]
    public void 어느_타를_받아쳐도_탈진은_같은_90틱이다()
    {
        // 설계 §4.3 — 탈진은 틱으로 센다(1.5초 = 90틱 · 반올림은 BattleSim.TicksFor 한 곳). 어느 타를 받아쳤는지와 무관하게
        // 같은 상태 · 같은 길이다. 받아친 틱부터 90틱 동안 탈진이고, 받아친 뒤 90번째 틱에 풀린다.
        int expected = BattleSim.TicksFor(TestConfigs.Boss().ExhaustSeconds);
        expected.ShouldBe(90);

        BattleSim early = ParryNthOfTwo(1);
        BattleSim last = ParryNthOfTwo(2);
        early.Events[0].Verdict.ShouldBe(HitVerdict.Parried);
        last.Events[1].Verdict.ShouldBe(HitVerdict.Parried);

        ExhaustLeft(early).ShouldBe(expected);
        ExhaustLeft(last).ShouldBe(expected);
    }

    [Fact]
    public void 탈진이_풀리면_간격을_처음부터_세고_다음_패턴을_고른다()
    {
        // 설계 §4.3 — 탈진이 끝나면 패턴 간격 뒤 다음 패턴을 고른다. 탈진 동안 보스는 아무것도 안 한다: 패턴도 없고 간격도 안 준다.
        // 판이 처음 설 때와 같은 규약이다 — 쉬는 첫 틱부터 세어 간격의 마지막 틱에 다음 패턴이 선다. 탈진이 풀리는 틱이 쉬는 첫 틱이다.
        BattleSim fresh = OnePattern(TwoHits());
        while (fresh.Boss.CurrentPattern is null && fresh.Ticks < 600)
        {
            fresh.Tick(default);
        }

        BattleSim sim = ParryNthOfTwo(1);
        int free = sim.Ticks + ExhaustLeft(sim);
        while (sim.Boss.CurrentPattern is null && sim.Ticks < free + 600)
        {
            sim.Tick(default);
        }

        (sim.Ticks - free + 1).ShouldBe(fresh.Ticks, "탈진이 풀린 뒤 간격을 처음부터 안 셌다");
    }

    [Fact]
    public void 끊긴_창은_관측을_안_남기고_로그만_남긴다()
    {
        // 설계 §3.5 5 — 보스가 탈진해 패턴이 끊기면 열린 창은 결과가 없다. 지어내면 창이 열린 틱의 빗나간 이유(대개 거리)가 나가
        // "거리로 빗나갔다" 로 계측에 들어간다 — 그 한 줄이 곧 시도 기록이다. 그래서 로그 한 줄만 남긴다.
        // 앞의 판정(거리 50 · 창 30틱)은 멀리 선 파이터에게 안 닿은 채 살아 있고, 그동안 선 둘째를 받아친다.
        var pattern = new PatternDef
        {
            Tags = TwoHits().Tags,
            Timeline = new List<PatternStep>
            {
                new() { T = 12 * BattleSim.Dt, Kind = "active", Band = new double[] { 0, 50, 0, 300 }, Damage = 5, ActiveSeconds = 0.5 },
                new() { T = 24 * BattleSim.Dt, Kind = "active", Band = new double[] { 0, 2000, 0, 300 }, Damage = 5 },
                new() { T = 60 * BattleSim.Dt, Kind = "end" },
            },
        };

        using var log = new LogCapture();
        BattleSim sim = OnePattern(pattern);
        for (int i = 1; i <= 40 && sim.Events.Count == 0; i++)
        {
            bool secondSoon = sim.BossTestedRects.Count > 0 && sim.NextActiveIn is <= 4 * BattleSim.Dt;
            sim.Tick(new InputFrame(0, false, false, Parry: secondSoon, false));
        }

        sim.Events.Count.ShouldBe(1);
        sim.Events[0].Verdict.ShouldBe(HitVerdict.Parried);
        sim.Boss.Exhausted.ShouldBeTrue();
        log.Lines.ShouldContain($"[boss][D] cut_swing id=단타 tick={sim.Ticks} reason=exhaust");

        for (int i = 0; i < 40; i++)
        {
            sim.Tick(default);
        }

        sim.Events.Count.ShouldBe(1, "끊긴 창이 관측을 남겼다");
    }

    /// <summary>봇이 쓰는 반응 창(초)과 같은 값. 이 안에서 누르면 판정이 패리 창 안에 선다.</summary>
    private const double _lateReact = 0.10;

    [Fact]
    public void 실제_1단계_연격을_받아치면_곧장_누른_2연격이_탈진_안에_다_닿는다()
    {
        // **유저가 요청한 고리 한 바퀴** (#72 · 설계 §4.3): 받아친다 → 보스가 무너진다 → 곧장 누른 2연격(1타 + 2타)이 탈진 안에
        // 다 닿는다. 2번 PR 이 넘긴 자리다(#59 의 2/4 넘김) — FighterActionTests 가 되받아치기를, BossDataTests 가 초의 산수를
        // 못박지만 BattleSim 위에서 한 바퀴가 실제로 도는지는 아무도 안 봤다. **실제 캐릭터 · 보스 · 1단계 연격**으로 돈다.
        FighterConfig fighter = TestConfigs.Fighters().Values.Single();
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = fighter,
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(),
            PatternIds = new[] { "3연격" },
            Patterns = Patterns(),
            Seed = 51,
            MaxTicks = 60 * 60,
        });

        // 붙은 채 기다리다 판정이 봇의 반응 창(_lateReact) 안으로 오면 K. 받아친 다음 틱에 J, 1타 도중에 J 를 한 번 더(2타).
        int parried = 0;
        for (int t = 1; t <= 60 * 30 && parried == 0; t++)
        {
            double gap = Math.Abs(sim.Fighter.X - sim.Boss.X);
            var toward = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);
            bool press = sim.NextActiveIn is double left && left <= _lateReact && sim.Fighter.Action == FighterAction.Idle;
            sim.Tick(new InputFrame(gap > Standoff() + 10 ? toward : (sbyte)0, false, false, Parry: press, false));
            if (sim.Events.Count > 0 && sim.Events[^1].Verdict == HitVerdict.Parried)
            {
                parried = sim.Ticks;
            }
        }

        parried.ShouldBeGreaterThan(0, "1단계 연격을 한 번도 못 받아쳤다");
        int before = sim.Boss.Health;
        for (int i = 1; sim.Boss.Exhausted && i <= 60 * 5; i++)
        {
            sim.Tick(new InputFrame(0, false, false, false, Attack: i is 1 or 3));
        }

        (before - sim.Boss.Health).ShouldBe(fighter.Combo[0].Damage + fighter.Combo[1].Damage,
            "받아친 뒤 곧장 누른 2연격이 탈진 안에 다 안 닿았다");
    }

    /// <summary>실제 <c>점프 공격</c> 하나만 도는 판 — 보스는 안 죽는다. 뛰는 틱의 파이터 앞에 내린다.</summary>
    private static BattleSim Leaper() => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighter(),
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(maxHealth: 999_999),
        PatternIds = new[] { "점프 공격" },
        Patterns = Patterns(),
        Seed = 1,
        MaxTicks = 60 * 10,
    });

    [Fact]
    public void 실제_점프_공격의_착지는_가드로_막고_패리로는_못_받는다()
    {
        // 설계 §4.2 — 착지는 바닥 전체 · 높이 0 ~ 60 · 패리 불가다. 답은 가드와 점프다. 가드는 12 × 1.8 = 21.6 스태미나 ·
        // 칩 3 을 낸다. 창 안에서 누른 패리는 맨몸이다 — 받아치지 못하고 그냥 맞는다(보스도 안 무너진다).
        BattleSim guarded = Leaper();
        for (int i = 0; i < 300 && guarded.Events.Count == 0; i++)
        {
            guarded.Tick(new InputFrame(0, false, false, false, false, GuardHeld: true));
        }

        DodgeEvent g = guarded.Events.Single();
        g.Verdict.ShouldBe(HitVerdict.Guarded);
        g.ParryAvailable.ShouldBeFalse();
        g.JumpAvailable.ShouldBeTrue("착지 띠는 점프로 넘는다 — 기준 파이터도 발이 60 위에 창보다 오래 있다");
        guarded.Fighter.Health.ShouldBe(TestConfigs.Fighter().MaxHealth - 3);
        guarded.Fighter.Stamina.ShouldBe(TestConfigs.Fighter().MaxStamina - 21.6, 1e-9);

        BattleSim parried = Leaper();
        for (int i = 0; i < 300 && parried.Events.Count == 0; i++)
        {
            bool press = parried.NextActiveIn is <= 4 * BattleSim.Dt && parried.Fighter.Action == FighterAction.Idle;
            parried.Tick(new InputFrame(0, false, false, Parry: press, false));
        }

        DodgeEvent p = parried.Events.Single();
        p.Verdict.ShouldBe(HitVerdict.Hit, "패리를 못 받는 착지를 받아쳤다");
        p.Verb.ShouldBe(DodgeVerb.Parry);
        parried.Boss.Exhausted.ShouldBeFalse();
    }

    [Fact]
    public void 가드는_다른_수단을_안_고른_것으로도_세어진다()
    {
        // 의존도 축의 분모는 그대로여야 한다 — 가드로 받은 판정은 "패리를 안 골랐다" 가 맞다.
        BattleSim sim = GuardOne();
        PlayerAxes axes = PlayerAxes.From(sim.Events);

        axes.GuardSamples.ShouldBe(1);
        axes.GuardBrokenSamples.ShouldBe(0);
        axes.ParrySamples.ShouldBe(0, "가드가 패리로 세어졌다");
    }
}
