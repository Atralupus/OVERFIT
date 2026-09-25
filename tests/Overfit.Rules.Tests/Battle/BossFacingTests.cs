using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 보스가 <b>어디를 보는가</b>. 판정은 좌우 대칭이라(<c>HitResolver</c> 가 <c>Math.Abs</c> 로 잰다)
/// 이 값은 맞고 틀림을 하나도 안 바꾼다 — 그런데도 규칙 층에 두는 이유는, 뷰가 좌표를 보고
/// 스스로 정하면 "같은 시드면 같은 결과" 가 그림까지 덮지 못하기 때문이다(이슈 #36).
///
/// <para>
/// 여기서 지켜야 하는 것은 <b>잠금</b>이다. 패턴이 도는 동안 보스가 따라 돌면 예고가 거짓말이 된다 —
/// 이 게임에서 예고는 패리를 가르치는 유일한 수단이고, 내려찍기 계열은 <b>변종 아홉이 같은 칼</b>이라
/// (이슈 #48) 표지 하나로만 갈린다 — 스윙 도중에 방향이 바뀌면 그 표지가 등 뒤로 가서 통째로 무의미해진다.
/// </para>
/// </summary>
public class BossFacingTests
{
    private static readonly InputFrame _right = new(1, false, false, false, false);

    private static Boss Spawn(double x = 1000) => new(TestConfigs.Boss(), TestConfigs.Arena(), x);

    private static BattleSetup Setup() => new()
    {
        Arena = TestConfigs.Arena(),
        Fighter = TestConfigs.Fighter(),
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(),
        PatternIds = new[] { "내려찍기 I", "내려찍기 II-쐐기" },
        Patterns = TestConfigs.Patterns(),
        Seed = 51,
        MaxTicks = 60 * 120,
    };

    [Fact]
    public void 보스는_목표_쪽을_본다()
    {
        Boss boss = Spawn();

        boss.Face(500);
        boss.Facing.ShouldBe(-1);

        boss.Face(1500);
        boss.Facing.ShouldBe(1);
    }

    [Fact]
    public void 정확히_겹치면_보던_쪽을_그대로_본다()
    {
        // 목표가 자기 자리와 같으면 "어느 쪽" 이 없다. 여기서 0 이나 임의의 값을 넣으면
        // 둘이 겹치는 한 틱마다 스프라이트가 파닥인다 — 겹치는 것은 흔한 일이다(몸 충돌이 없다).
        Boss boss = Spawn();
        boss.Face(500);

        boss.Face(boss.X);

        boss.Facing.ShouldBe(-1);
    }

    [Fact]
    public void 패턴이_도는_동안에는_안_돌아선다()
    {
        Boss boss = Spawn();
        boss.Face(500);

        boss.CurrentPattern = "내려찍기 I";
        boss.Face(1500);
        boss.Facing.ShouldBe(-1, "휘두르는 중에 돌아서면 예고가 거짓말이 된다");

        // 패턴이 끝나면 다시 따라 본다.
        boss.CurrentPattern = null;
        boss.Face(1500);
        boss.Facing.ShouldBe(1);
    }

    [Fact]
    public void 판이_서는_순간_이미_파이터를_보고_있다()
    {
        // 파이터는 아레나의 25% · 보스는 75% 에 선다. 첫 프레임부터 맞아야 한다 —
        // 한 틱 뒤에 고치면 전투가 시작되는 그림에서 보스가 등을 보인다.
        var sim = new BattleSim(Setup());

        sim.Boss.Facing.ShouldBe(-1);
    }

    [Fact]
    public void 파이터가_반대편으로_돌아가면_보스가_돌아선다()
    {
        // #27 로 몸 충돌이 없어져 지나갈 수 있게 된 바로 그 자리다 — 전에는 보스가
        // 등 뒤를 향해 칼을 휘두르는 그림이었다.
        var sim = new BattleSim(Setup());

        // ① 오른쪽으로 계속 달려 보스를 지나간다.
        int ticks = 0;
        while (sim.Fighter.X <= sim.Boss.X && ticks++ < 600)
        {
            sim.Tick(_right);
        }

        sim.Fighter.X.ShouldBeGreaterThan(sim.Boss.X, "파이터가 보스를 지나가지 못했다 — 이 테스트의 전제가 깨졌다");

        // ② 쉬는 틱을 기다린다. 지나간 순간 보스가 패턴 중이면 그 패턴이 끝날 때까지는
        //    일부러 안 돈다 — 잠금이 그렇게 생겼다. 상한(4초)은 간격 0.8 + 가장 긴 패턴 1.9 보다 넉넉하다.
        ticks = 0;
        while (sim.Boss.Facing < 0 && ticks++ < 240)
        {
            sim.Tick(_right);
        }

        sim.Fighter.X.ShouldBeGreaterThan(sim.Boss.X);
        sim.Boss.Facing.ShouldBe(1);
    }

    [Fact]
    public void 패턴이_도는_동안에는_파이터가_넘어가도_안_돌아선다()
    {
        var sim = new BattleSim(Setup());

        // 보스가 다가와 붙을 때까지 서 있는다. 붙어 있어야 한 패턴 안에 지나갈 수 있다.
        for (int i = 0; i < 120; i++)
        {
            sim.Tick(default);
        }

        // **쉬는 동안에는 서 있는다.** 지나가는 순간이 반드시 패턴 중이어야 이 테스트가 잠금을 본다 —
        // 계속 걸으면 보스도 쉬는 동안 다가오므로 둘이 가장 빨리 가까워지는 때가 **쉬는 틈**이고,
        // 거기서 지나가면 보스는 그냥 돌아서면 된다(잠금이 일할 자리가 아니다).
        // 전에는 계속 걸으며 우연에 기댔는데, 계열이 하나로 바뀌며 패턴 길이(1.9 → 2.9~3.25초)와
        // 그 우연이 같이 움직였다(이슈 #48). 단언은 그대로 두고 **걷는 때만** 못박는다.
        string? running = null;
        int locked = 0;
        bool mismatched = false;
        for (int i = 0; i < 600 && sim.Tick(sim.Boss.CurrentPattern is null ? default : _right) is null; i++)
        {
            if (sim.Boss.CurrentPattern is not string id)
            {
                running = null;
                continue;
            }

            if (id != running)
            {
                running = id;
                locked = sim.Boss.Facing;
            }

            sim.Boss.Facing.ShouldBe(locked, $"패턴 {id} 이 도는 중에 보스가 따라 돌았다");

            // 잠금이 실제로 일한 순간이 있었나 — 파이터가 반대편인데 보스는 그대로 보고 있는 틱.
            mismatched |= sim.Fighter.X > sim.Boss.X != locked > 0;
        }

        mismatched.ShouldBeTrue("한 패턴이 도는 동안 파이터가 반대편으로 넘어간 적이 없다 — 잠금을 증명하지 못했다");
    }
}
