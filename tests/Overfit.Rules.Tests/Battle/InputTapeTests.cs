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
    /// <summary>입력 96 가지 전부 — 이동 셋 × 버튼 넷의 켜고 끔 × 폭탄(설계 2026-09-30 조각2 §4). 비트 4 는 패리의 자리였다(#168) — 건너뛴다.</summary>
    private static IEnumerable<InputFrame> Every()
    {
        foreach (bool bomb in new[] { false, true })
        {
            for (sbyte move = -1; move <= 1; move++)
            {
                for (int bits = 0; bits < 32; bits++)
                {
                    if ((bits & 4) != 0)
                    {
                        continue;
                    }

                    yield return new InputFrame(
                        move, (bits & 1) != 0, (bits & 2) != 0, (bits & 8) != 0, (bits & 16) != 0, bomb);
                }
            }
        }
    }

    private static InputFrame Idle => new(0, false, false, false);

    [Fact]
    public void 코드는_96_가지_입력을_하나씩_왕복한다()
    {
        InputFrame[] every = [.. Every()];

        int[] codes = [.. every.Select(InputTape.Code)];

        codes.Distinct().Count().ShouldBe(96, "두 입력이 한 코드로 접혔다");
        codes.ShouldAllBe(code => code >= 0 && code < InputTape.Codes);
        every.Select(f => InputTape.Frame(InputTape.Code(f))).ShouldBe(every);

        // 형식의 닻 — 이동 +1 을 << 5 · 점프 1 · 대시 2 · 공격 8 · 가드 16. 디스크의 줄이 이 값으로 읽힌다. 4 는 패리의 자리였고 비워 둔다(#168).
        InputTape.Code(Idle).ShouldBe(32);
        InputTape.Code(new InputFrame(-1, Jump: true, false, false)).ShouldBe(1);
        InputTape.Code(new InputFrame(1, false, false, Attack: true)).ShouldBe(72);
        InputTape.Code(new InputFrame(0, false, Dash: true, false, GuardHeld: true)).ShouldBe(50);
        InputTape.Code(new InputFrame(1, true, true, true, true)).ShouldBe(91);

        // 폭탄은 96 위에 얹었다 (설계 2026-09-30 조각2 §4) — 있는 비트는 안 옮긴다.
        InputTape.Code(new InputFrame(0, false, false, false, Bomb: true)).ShouldBe(128);
        InputTape.Code(new InputFrame(1, true, true, true, true, Bomb: true)).ShouldBe(187);
    }

    [Fact]
    public void 패리의_자리_4_는_읽을_때_버린다()
    {
        // #168 — 패리를 걷었다. 옛 줄의 4 는 패리를 누른 틱이라 지금 규칙에 그 입력이 없다: 다른 비트는 같은 뜻으로 읽고 4 만 버린다. 그 판은
        // 패리가 있던 규칙의 판이라 어차피 같은 판으로 안 되살아난다 — 데이터 지문이 가린다.
        for (int code = 0; code < InputTape.Codes; code++)
        {
            InputTape.Frame(code).ShouldBe(InputTape.Frame(code & ~4), $"옛 코드 {code} 의 4 가 뜻을 가졌다");
        }
    }

    [Fact]
    public void 폭탄_칸이_생기기_전의_코드는_뜻이_그대로다()
    {
        // 조각 1 이 쓴 줄은 0 ~ 95 뿐이다 — 그 코드들이 폭탄을 안 누른 같은 입력으로 읽혀야 옛 줄이 같은 판으로 되살아난다. 폭탄을 누른 입력은
        // 같은 입력의 코드에 96 을 더한 것이다.
        for (int code = 0; code < 96; code++)
        {
            InputFrame old = InputTape.Frame(code);
            old.Bomb.ShouldBeFalse($"옛 코드 {code} 가 폭탄을 누른다");
            InputTape.Frame(code + 96).ShouldBe(old with { Bomb = true });
        }
    }

    [Fact]
    public void 같은_입력이_이어지면_한_칸이다()
    {
        var tape = new InputTape();
        var right = new InputFrame(1, false, false, false);
        var jump = new InputFrame(0, Jump: true, false, false);

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
        Should.Throw<ArgumentOutOfRangeException>(() => InputTape.Frame(192));

        // 이동은 −1 · 0 · +1 뿐이다 — 2 를 받으면 파이터가 두 배로 걷는데(Fighter) 코드에는 그 자리가 없다.
        Should.Throw<ArgumentOutOfRangeException>(() => InputTape.Code(new InputFrame(2, false, false, false)));

        // 줄에서 읽은 칸 — 모양 · 코드 · 틱 수. 어느 칸이 왜 틀렸는지 말한다.
        InputTape.Problem([[32, 3], [192, 1]]).ShouldBe("1번 칸 [192, 1] — 코드가 0 ~ 191 밖이다");
        InputTape.Problem([[32, 0]]).ShouldBe("0번 칸 [32, 0] — 틱 수가 1 보다 작다");
        InputTape.Problem([[32]]).ShouldBe("0번 칸 [32] — [코드, 틱 수] 둘이 아니다");
        InputTape.Problem([[32, 3], [33, 1], [128, 1]]).ShouldBeNull();
        Should.Throw<ArgumentException>(() => InputTape.Play([[32, 1], [-1, 2]])).Message.ShouldContain("1번 칸");
    }
}
