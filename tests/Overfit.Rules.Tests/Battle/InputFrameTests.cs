using System.Collections.Generic;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 히트스톱 동안 누른 키를 넘기는 규칙 (#71 · 설계 §1 「대화로 정한 것」) — "히트스톱 동안 누른 키는 버리지 않는다 · 히트스톱이 끝난
/// 첫 틱에 넘긴다". 히트스톱은 씬(<c>Battle</c>)의 것이라 판 위에서는 못 걸고, 넘기는 규칙(<see cref="InputFrame.Carry"/>)은 여기서 잰다.
/// </summary>
public class InputFrameTests
{
    [Fact]
    public void 앞서_누른_엣지는_다음에_실린다()
    {
        // 엣지 넷은 둘 중 하나라도 눌렀으면 눌린 것이다 — 멈춘 프레임 여럿에 걸쳐 누른 것도 다 모인다.
        var held = new InputFrame(0, false, Dash: true, false, false);
        held = InputFrame.Carry(held, new InputFrame(0, false, false, false, Attack: true));

        InputFrame carried = InputFrame.Carry(held, default);

        carried.Dash.ShouldBeTrue();
        carried.Attack.ShouldBeTrue();
        carried.Jump.ShouldBeFalse();
        carried.Parry.ShouldBeFalse();
    }

    [Fact]
    public void 레벨은_앞의_값을_안_들고_온다()
    {
        // 이동과 가드는 누르고 있는 동안이 전부다(설계 §5.2 · §5.4) — 멈춘 동안 누르다 뗀 가드를 들고 오면 뗀 손이 가드를 든다.
        var held = new InputFrame(1, false, false, false, false, GuardHeld: true);
        var now = new InputFrame(-1, false, false, false, false, GuardHeld: false);

        InputFrame carried = InputFrame.Carry(held, now);

        carried.Move.ShouldBe((sbyte)-1);
        carried.GuardHeld.ShouldBeFalse();
    }

    [Fact]
    public void 실을_것이_없으면_지금_입력_그대로다()
    {
        // 히트스톱이 없는 틱은 이 규칙을 타도 한 글자도 안 달라야 한다 — 판이 히트스톱과 무관하게 같아야 봇과 사람이 같은 판을 산다.
        var frames = new List<InputFrame>
        {
            default,
            new(1, Jump: true, false, false, false),
            new(-1, false, Dash: true, Parry: true, Attack: true, GuardHeld: true),
        };

        foreach (InputFrame now in frames)
        {
            InputFrame.Carry(default, now).ShouldBe(now);
        }
    }

    [Fact]
    public void 히트스톱에_누른_J_는_끝난_첫_틱에_되받아치기가_된다()
    {
        // 받아치면 보스가 무너지고 그 틱에 히트스톱이 걸린다(설계 §4.3). 받아친 것을 보고 곧장 누른 J 는 그 7프레임에 떨어지기 쉽다 —
        // 넘기면 끝난 첫 틱에 되받아치기 1타가 선다(전에는 버려져 "눌렀는데 안 나간" 칼이었다). 씬이 하는 일을 여기서 그대로 한다:
        // 멈춘 프레임에는 틱을 안 밀고 입력만 모은다.
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 0.2),
            PatternIds = new[] { "3연격" },
            Patterns = TestConfigs.Patterns(),
            Seed = 1,
            MaxTicks = 60 * 60,
        });
        double standoff = sim.Boss.HalfWidth + sim.Fighter.HalfWidth;
        for (int i = 0; i < 600 && sim.Boss.X - sim.Fighter.X > standoff; i++)
        {
            sim.Tick(new InputFrame(1, false, false, false, false));
        }

        (sim.Boss.X - sim.Fighter.X).ShouldBeLessThanOrEqualTo(standoff, "파이터가 보스 앞까지 못 걸어갔다");

        for (int i = 0; i < 600 && !sim.Boss.Exhausted; i++)
        {
            bool press = sim.NextActiveIn is <= 4 * BattleSim.Dt && sim.Fighter.Action == FighterAction.Idle;
            sim.Tick(new InputFrame(0, false, false, Parry: press, false));
        }

        sim.Boss.Exhausted.ShouldBeTrue("받아치지 못했다 — 이 테스트가 히트스톱 자리를 못 본다");
        sim.Fighter.Action.ShouldBe(FighterAction.Parry);

        InputFrame held = default;
        for (int frame = 0; frame < 7; frame++)
        {
            held = InputFrame.Carry(held, new InputFrame(0, false, false, false, Attack: frame == 2));
        }

        sim.Tick(InputFrame.Carry(held, default));

        sim.Fighter.Action.ShouldBe(FighterAction.Attack, "히트스톱에 누른 J 가 끝난 첫 틱에 되받아치기가 안 됐다");
    }
}
