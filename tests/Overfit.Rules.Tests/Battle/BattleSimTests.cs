using System.Collections.Generic;
using System.IO;
using Overfit.Battle.Rules;
using Overfit.Core;
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
}
