using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 판의 입력을 줄로 접는다 (설계 2026-09-29 조각1 §4.3) — 시도 기록 한 줄에 싣고 되살리기(§4.4)가 틱마다 그대로 다시 넣는다. 코드는 디스크에
/// 남는 형식이라 <b>계약</b>이다: 한 번 쓴 줄은 코드를 바꾸면 다른 판이 된다.
/// </summary>
public class InputTapeTests
{
    /// <summary>입력 96 가지 전부 — 이동 셋 × 버튼 다섯의 켜고 끔.</summary>
    private static IEnumerable<InputFrame> Every()
    {
        for (sbyte move = -1; move <= 1; move++)
        {
            for (int bits = 0; bits < 32; bits++)
            {
                yield return new InputFrame(
                    move, (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0);
            }
        }
    }

    private static InputFrame Idle => new(0, false, false, false, false);

    [Fact]
    public void 코드는_96_가지_입력을_하나씩_왕복한다()
    {
        InputFrame[] every = [.. Every()];

        int[] codes = [.. every.Select(InputTape.Code)];

        codes.Distinct().Count().ShouldBe(96, "두 입력이 한 코드로 접혔다");
        codes.ShouldAllBe(code => code >= 0 && code < InputTape.Codes);
        every.Select(f => InputTape.Frame(InputTape.Code(f))).ShouldBe(every);

        // 형식의 닻 — 이동 +1 을 << 5 · 점프 1 · 대시 2 · 패리 4 · 공격 8 · 가드 16. 디스크의 줄이 이 값으로 읽힌다.
        InputTape.Code(Idle).ShouldBe(32);
        InputTape.Code(new InputFrame(-1, Jump: true, false, false, false)).ShouldBe(1);
        InputTape.Code(new InputFrame(1, false, false, false, Attack: true)).ShouldBe(72);
        InputTape.Code(new InputFrame(0, false, Dash: true, Parry: true, false, GuardHeld: true)).ShouldBe(54);
        InputTape.Code(new InputFrame(1, true, true, true, true, true)).ShouldBe(95);
    }

    [Fact]
    public void 같은_입력이_이어지면_한_칸이다()
    {
        var tape = new InputTape();
        var right = new InputFrame(1, false, false, false, false);
        var jump = new InputFrame(0, Jump: true, false, false, false);

        foreach (InputFrame input in new[] { Idle, Idle, Idle, right, right, Idle, jump, Idle })
        {
            tape.Add(input);
        }

        // 점프는 엣지라 한 틱뿐이다 — 앞뒤의 선 입력과 같은 칸이 아니다.
        tape.Runs.ShouldBe([[32, 3], [64, 2], [32, 1], [33, 1], [32, 1]]);
        tape.Ticks.ShouldBe(8);
    }

    [Fact]
    public void Play_는_넣은_입력을_그대로_낸다()
    {
        // 판처럼 섞는다 — 오래 걷고 · 한 틱 누르고 · 같은 것을 여러 번. 칸 수가 틱 수보다 훨씬 적어야 한 줄에 싣는다(§4.3).
        var inputs = new List<InputFrame>();
        foreach (InputFrame input in Every())
        {
            inputs.AddRange(Enumerable.Repeat(input, 1 + (InputTape.Code(input) % 7)));
        }

        var tape = new InputTape();
        foreach (InputFrame input in inputs)
        {
            tape.Add(input);
        }

        InputTape.Play(tape.Runs).ShouldBe(inputs);
        tape.Runs.Count.ShouldBe(96);
        InputTape.Play([]).ShouldBeEmpty();
    }

    [Fact]
    public void 틀린_코드는_거절한다()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => InputTape.Frame(-1));
        Should.Throw<ArgumentOutOfRangeException>(() => InputTape.Frame(96));

        // 이동은 −1 · 0 · +1 뿐이다 — 2 를 받으면 파이터가 두 배로 걷는데(Fighter) 코드에는 그 자리가 없다.
        Should.Throw<ArgumentOutOfRangeException>(() => InputTape.Code(new InputFrame(2, false, false, false, false)));

        // 줄에서 읽은 칸 — 모양 · 코드 · 틱 수. 어느 칸이 왜 틀렸는지 말한다.
        InputTape.Problem([[32, 3], [96, 1]]).ShouldBe("1번 칸 [96, 1] — 코드가 0 ~ 95 밖이다");
        InputTape.Problem([[32, 0]]).ShouldBe("0번 칸 [32, 0] — 틱 수가 1 보다 작다");
        InputTape.Problem([[32]]).ShouldBe("0번 칸 [32] — [코드, 틱 수] 둘이 아니다");
        InputTape.Problem([[32, 3], [33, 1]]).ShouldBeNull();
        Should.Throw<ArgumentException>(() => InputTape.Play([[32, 1], [-1, 2]])).Message.ShouldContain("1번 칸");
    }
}
