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
        var held = new InputFrame(0, false, Dash: true, false);
        held = InputFrame.Carry(held, new InputFrame(0, false, false, Attack: true));
        held = InputFrame.Carry(held, new InputFrame(0, false, false, false, Bomb: true));

        InputFrame carried = InputFrame.Carry(held, default);

        carried.Dash.ShouldBeTrue();
        carried.Attack.ShouldBeTrue();
        // 폭탄도 엣지다 (설계 2026-09-30 조각2 §4) — 히트스톱 동안 누른 L 이 사라지면 "눌렀는데 안 던진" 폭탄이 된다.
        carried.Bomb.ShouldBeTrue();
        carried.Jump.ShouldBeFalse();
    }

    [Fact]
    public void 레벨은_앞의_값을_안_들고_온다()
    {
        // 이동과 가드는 누르고 있는 동안이 전부다(설계 §5.2 · §5.4) — 멈춘 동안 누르다 뗀 가드를 들고 오면 뗀 손이 가드를 든다.
        var held = new InputFrame(1, false, false, false, GuardHeld: true);
        var now = new InputFrame(-1, false, false, false, GuardHeld: false);

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
            new(1, Jump: true, false, false),
            new(-1, false, Dash: true, Attack: true, GuardHeld: true),
            new(0, false, false, false, Bomb: true),
        };

        foreach (InputFrame now in frames)
        {
            InputFrame.Carry(default, now).ShouldBe(now);
        }
    }
}
