using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 파이터의 던지기 (설계 2026-09-30 조각2 §1.1 · §1.2) — L 로 던지고, 땅에서만, 커밋이고, 틱으로 센다. 놓기 전에 맞으면 끊기고 폭탄을 잃는다.
/// 보스가 없는 몸 하나로 잰다 — 판 위의 순서(놓는 틱의 판정은 늦다)는 <c>BombTests</c> 가 본다.
/// </summary>
public class FighterThrowTests
{
    private const double _dt = 1.0 / 60.0;

    private static readonly InputFrame _bomb = new(0, false, false, false, Bomb: true);

    /// <summary>↓ 를 누르고 있는 틱.</summary>
    private static readonly InputFrame _guard = new(0, false, false, false, GuardHeld: true);

    private static Fighter Spawn(double x = 960) => new(TestConfigs.Fighter(), TestConfigs.Arena(), x);

    private static BombDef Bomb => TestConfigs.Fighter().Bomb;

    /// <summary>선딜의 틱 — 누른 틱이 첫 틱이고 이 틱의 끝에 놓는다.</summary>
    private static int ThrowTicks => BattleSim.TicksFor(Bomb.ThrowSeconds);

    /// <summary>놓은 뒤 경직의 틱.</summary>
    private static int RecoverTicks => BattleSim.TicksFor(Bomb.RecoverSeconds);

    /// <summary>한 번 던지고 선 채로 끝까지 민다 — 상한을 둔다(규칙이 깨진 날 멈춰 서지 않게).</summary>
    private static void ThrowOnce(Fighter f)
    {
        f.Tick(_bomb, _dt);
        for (int i = 0; i < 600 && f.Action == FighterAction.Throw; i++)
        {
            f.Tick(default, _dt);
        }

        f.Action.ShouldBe(FighterAction.Idle, "600틱 안에 던지기가 안 끝났다");
    }

    [Fact]
    public void 누르면_선딜의_마지막_틱에_놓고_경직_뒤에_선다()
    {
        Fighter f = Spawn();

        f.Tick(_bomb, _dt);

        f.Action.ShouldBe(FighterAction.Throw);
        f.Throwing.ShouldBeTrue();
        f.BombsLeft.ShouldBe(Bomb.Count - 1, "폭탄은 누를 때 손에 든다");

        for (int t = 2; t < ThrowTicks; t++)
        {
            f.Tick(default, _dt);
            f.ThrowReleased.ShouldBeFalse($"선딜의 {t}번째 틱에 놓았다");
        }

        f.Tick(default, _dt);
        f.ThrowReleased.ShouldBeTrue($"선딜의 {ThrowTicks}번째 틱 끝에 놓아야 한다");
        f.Throwing.ShouldBeFalse();
        f.Action.ShouldBe(FighterAction.Throw, "놓은 뒤 경직도 던지기다");

        for (int t = 1; t < RecoverTicks; t++)
        {
            f.Tick(default, _dt);
            f.ThrowReleased.ShouldBeFalse("놓는 것은 한 틱의 일이다");
            f.Action.ShouldBe(FighterAction.Throw, $"경직의 {t}번째 틱에 섰다");
        }

        f.Tick(default, _dt);
        f.Action.ShouldBe(FighterAction.Idle, $"경직 {RecoverTicks}틱 뒤에 서야 한다");
    }

    [Fact]
    public void 폭탄을_다_쓰면_눌러도_안_선다()
    {
        Fighter f = Spawn();
        for (int n = 0; n < Bomb.Count; n++)
        {
            ThrowOnce(f);
        }

        f.BombsLeft.ShouldBe(0);

        f.Tick(_bomb, _dt);

        f.Action.ShouldBe(FighterAction.Idle, "남은 폭탄이 없으면 안 누른 것과 같다");
    }

    [Fact]
    public void 공중에서는_안_던진다()
    {
        Fighter f = Spawn();
        f.Tick(new InputFrame(0, Jump: true, false, false), _dt);
        f.Tick(default, _dt);
        f.Grounded.ShouldBeFalse();

        f.Tick(_bomb, _dt);

        f.Action.ShouldBe(FighterAction.Idle);
        f.BombsLeft.ShouldBe(Bomb.Count);
    }

    [Fact]
    public void 가드_중에도_던진다()
    {
        // 가드는 커밋이 아니라 자세다(설계 §5.2) — 그 위에서 바로 다른 행동으로 넘어간다.
        Fighter f = Spawn();
        f.Tick(_guard, _dt);
        f.Action.ShouldBe(FighterAction.Guard);

        f.Tick(_guard with { Bomb = true }, _dt);

        f.Action.ShouldBe(FighterAction.Throw);
    }

    [Fact]
    public void 던지는_동안_다른_것이_다_막힌다()
    {
        // 커밋이다 (설계 2026-09-30 조각2 §1.1) — 스스로 거둘 수 없어야 끊기는 싸움이 선다. 선딜과 놓은 뒤 경직 둘 다 본다.
        InputFrame[] tries =
        [
            new(1, false, false, false),
            new(0, Jump: true, false, false),
            new(0, false, Dash: true, false),
            new(0, false, false, Attack: true),
            _guard,
            _bomb,
        ];

        Fighter f = Spawn();
        f.Tick(_bomb, _dt);
        double x = f.X;
        foreach (InputFrame input in tries)
        {
            f.Tick(input, _dt);
            f.Action.ShouldBe(FighterAction.Throw, $"선딜 중에 {input} 이 던지기를 끊었다");
        }

        for (int i = 0; i < 600 && f.Throwing; i++)
        {
            f.Tick(default, _dt);
        }

        foreach (InputFrame input in tries)
        {
            f.Tick(input, _dt);
            f.Action.ShouldBe(FighterAction.Throw, $"놓은 뒤 경직 중에 {input} 이 던지기를 끊었다");
        }

        f.X.ShouldBe(x, "던지는 동안 걸었다");
        f.Grounded.ShouldBeTrue("던지는 동안 뛰었다");
        f.BombsLeft.ShouldBe(Bomb.Count - 1, "던지는 동안 또 던졌다");
    }

    [Fact]
    public void 스태미나를_안_쓰고_던지는_동안_안_찬다()
    {
        // 개수가 값이다 (설계 2026-09-30 조각2 §1.1). 행동 중에는 안 찬다 — 칼질 · 대시와 같다(Regen).
        Fighter f = Spawn();
        f.Spend(50);
        double before = f.Stamina;

        f.Tick(_bomb, _dt);
        for (int i = 0; i < 30; i++)
        {
            f.Tick(default, _dt);
        }

        f.Stamina.ShouldBe(before);
    }

    [Fact]
    public void 놓기_전에_맞으면_끊기고_폭탄을_잃는다()
    {
        Fighter f = Spawn();
        f.Tick(_bomb, _dt);
        for (int i = 0; i < 40; i++)
        {
            f.Tick(default, _dt);
        }

        f.TakeDamage(8);

        f.Action.ShouldBe(FighterAction.Idle, "폭탄을 든 손이 맞으면 놓친다");
        f.ThrowLost.ShouldBeTrue();
        f.Health.ShouldBe(TestConfigs.Fighter().MaxHealth - 8, "피해는 그대로 받는다");
        f.BombsLeft.ShouldBe(Bomb.Count - 1, "잃은 폭탄은 안 돌아온다");

        f.Tick(default, _dt);
        f.ThrowLost.ShouldBeFalse("잃은 것은 그 틱의 일이다");
        f.ThrowReleased.ShouldBeFalse("끊긴 던지기가 놓았다");

        // 맞아도 굳지 않는다 — 칼질과 같다. 곧장 다시 던질 수 있다.
        f.Tick(_bomb, _dt);
        f.Action.ShouldBe(FighterAction.Throw);
    }

    [Fact]
    public void 선딜의_마지막_틱_앞까지는_맞으면_끊긴다()
    {
        // 판에서는 파이터가 먼저 돈다 — 선딜의 마지막 틱(놓는 틱)에 닿은 판정은 이미 놓은 뒤라 못 끊는다(BombTests). 그 앞 틱까지는 끊긴다.
        Fighter f = Spawn();
        f.Tick(_bomb, _dt);
        for (int t = 2; t < ThrowTicks; t++)
        {
            f.Tick(default, _dt);
        }

        f.Throwing.ShouldBeTrue();

        f.TakeDamage(1);

        f.ThrowLost.ShouldBeTrue();
    }

    [Fact]
    public void 놓은_뒤_경직에_맞으면_그대로다()
    {
        Fighter f = Spawn();
        f.Tick(_bomb, _dt);
        for (int t = 2; t <= ThrowTicks; t++)
        {
            f.Tick(default, _dt);
        }

        f.ThrowReleased.ShouldBeTrue();

        f.TakeDamage(8);

        f.ThrowLost.ShouldBeFalse("놓은 폭탄은 이미 날아갔다");
        f.Action.ShouldBe(FighterAction.Throw, "경직은 그대로 간다");
    }

    [Fact]
    public void 잡히면_끊기고_잃는다()
    {
        Fighter f = Spawn();
        f.Tick(_bomb, _dt);

        f.Grab(25, 60);

        f.ThrowLost.ShouldBeTrue();
        f.Held.ShouldBeTrue();
        f.Action.ShouldBe(FighterAction.Idle);
    }

    [Fact]
    public void 칼질은_여전히_맞아도_안_끊긴다()
    {
        // 던지기만 예외다 (설계 2026-09-30 조각2 §1.2) — 칼질은 끝까지 커밋이다(설계 §5.1).
        Fighter f = Spawn();
        f.Tick(new InputFrame(0, false, false, Attack: true), _dt);

        f.TakeDamage(8);

        f.Action.ShouldBe(FighterAction.Attack);
        f.ThrowLost.ShouldBeFalse();
    }

    [Fact]
    public void 한_틱에_여럿을_누르면_폭탄은_마지막이다()
    {
        // 규칙의 순서 대시 → 공격 → 폭탄 (설계 2026-09-30 조각2 §1.1).
        Fighter f = Spawn();

        f.Tick(new InputFrame(0, false, false, Attack: true, Bomb: true), _dt);

        f.Action.ShouldBe(FighterAction.Attack);
        f.BombsLeft.ShouldBe(Bomb.Count);
    }

    [Fact]
    public void 판이_던지는_쪽으로_돌려세운다()
    {
        // 파이터는 보스를 모른다 — 던지는 틱에 판이 보스 쪽으로 돌려세운다(설계 2026-09-30 조각2 §1.1). 겹치면 보던 쪽 그대로다.
        Fighter f = Spawn(960);
        f.Facing.ShouldBe(1);

        f.Face(100);
        f.Facing.ShouldBe(-1);

        f.Face(960);
        f.Facing.ShouldBe(-1);
    }
}
