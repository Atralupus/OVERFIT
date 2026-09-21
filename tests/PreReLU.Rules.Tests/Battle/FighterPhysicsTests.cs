using System;
using PreReLU.Battle.Rules;
using Shouldly;
using Xunit;

namespace PreReLU.Rules.Tests.Battle;

public class FighterPhysicsTests
{
    private const double _dt = 1.0 / 60.0;

    private static Fighter Spawn(double x = 960) => new(TestConfigs.Fighter(), new Arena(1920), x);

    private static void Run(Fighter f, InputFrame input, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            f.Tick(input, _dt);
        }
    }

    [Fact]
    public void 가만히_두면_바닥에_서_있다()
    {
        Fighter f = Spawn();
        Run(f, default, 60);

        f.Y.ShouldBe(0);
        f.Grounded.ShouldBeTrue();
    }

    [Fact]
    public void 오른쪽_입력은_오른쪽으로_옮긴다()
    {
        Fighter f = Spawn();
        Run(f, new InputFrame(1, false, false, false, false), 60);

        // 420 px/s 로 1초. 틱 누적이라 정확히 420 은 아니다
        f.X.ShouldBe(960 + 420, 1.0);
        f.Facing.ShouldBe(1);
    }

    [Fact]
    public void 아레나_밖으로_못_나간다()
    {
        Fighter f = Spawn(x: 100);
        Run(f, new InputFrame(-1, false, false, false, false), 120);

        // 왼쪽 벽은 x=0 이고 몸 절반(30)이 걸린다
        f.X.ShouldBe(30);
    }

    [Fact]
    public void 점프하면_올라갔다_내려와_착지한다()
    {
        Fighter f = Spawn();
        f.Tick(new InputFrame(0, true, false, false, false), _dt);

        f.Grounded.ShouldBeFalse();
        double peak = 0;
        for (int i = 0; i < 120; i++)
        {
            f.Tick(default, _dt);
            peak = Math.Max(peak, f.Y);
            if (f.Grounded)
            {
                break;
            }
        }

        peak.ShouldBeGreaterThan(100);
        f.Grounded.ShouldBeTrue();
        f.Y.ShouldBe(0);
    }

    [Fact]
    public void 공중에서는_다시_점프_못_한다()
    {
        Fighter f = Spawn();
        var jump = new InputFrame(0, true, false, false, false);
        f.Tick(jump, _dt);
        double afterFirst = f.VelocityY;

        f.Tick(jump, _dt);

        // 중력만 먹었지 다시 안 솟는다
        f.VelocityY.ShouldBeLessThan(afterFirst);
    }

    [Fact]
    public void 같은_입력_시퀀스는_같은_궤적을_만든다()
    {
        var inputs = new InputFrame[240];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i] = new InputFrame((sbyte)(i % 7 < 3 ? 1 : -1), i % 31 == 0, false, false, false);
        }

        Fighter a = Spawn();
        Fighter b = Spawn();
        foreach (InputFrame input in inputs)
        {
            a.Tick(input, _dt);
            b.Tick(input, _dt);
        }

        a.X.ShouldBe(b.X);
        a.Y.ShouldBe(b.Y);
    }
}
