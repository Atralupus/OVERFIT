using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 판의 입력을 줄로 접는다 (설계 2026-09-29 조각1 §4.3) — <c>Battle</c> 이 <see cref="BattleSim.Tick"/> 에 넘긴 입력(히트스톱 동안 모은 엣지를 실은
/// <b>뒤</b>, 규칙이 받은 그대로)을 틱마다 받아, 같은 입력이 이어지는 틱을 한 칸 <c>[코드, 틱 수]</c> 로 만든다. 시도 기록 한 줄이 싣고, 되살리기
/// (<see cref="Replay"/>)가 <see cref="Play"/> 로 틱마다 그대로 다시 넣는다.
///
/// <para>
/// <b>코드는 계약이다</b> — 이동 +1 을 <c>&lt;&lt; 5</c> · 점프 1 · 대시 2 · 패리 4 · 공격 8 · 가드 16 을 더한 0 ~ 95. 디스크에 남는 형식이라 한 번 쓴
/// 줄은 이 값으로 읽힌다: 비트 하나를 옮기면 옛 줄이 다른 판으로 되살아난다(<c>Det</c> 의 상수와 같은 대우). 입력 칸을 하나 더하면 96 위에 새 비트를
/// 얹는다 — 있는 비트는 안 옮긴다.
/// </para>
///
/// <para>
/// <b>크기(잰 값)</b> — 데모 봇 · 함대 봇으로 60판씩 재면 칸이 틱당 0.08 ~ 0.10(많아야 0.19), 글자가 틱당 0.5 ~ 0.75바이트다. 가장 긴 판(600초 ·
/// 36000틱)이 약 27KB 라 한 줄에 싣는다.
/// </para>
/// </summary>
public sealed class InputTape
{
    /// <summary>코드의 가짓수 — 이동 셋 × 버튼 다섯의 켜고 끔.</summary>
    public const int Codes = 96;

    private const int _jump = 1;
    private const int _dash = 2;
    private const int _parry = 4;
    private const int _attack = 8;
    private const int _guard = 16;
    private const int _moveShift = 5;

    private readonly List<int[]> _runs = new();

    /// <summary>접은 칸들 — <c>[코드, 틱 수]</c>, 넣은 순서로.</summary>
    public IReadOnlyList<int[]> Runs => _runs;

    /// <summary>넣은 틱 수 — 칸의 틱 수의 합.</summary>
    public int Ticks { get; private set; }

    /// <summary>입력 하나의 코드.</summary>
    /// <exception cref="ArgumentOutOfRangeException">이동이 −1 · 0 · +1 밖이다 — 규칙은 받지만(<c>Fighter</c> 가 곱한다) 코드에 자리가 없다.</exception>
    public static int Code(InputFrame input)
    {
        if (input.Move is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input), input.Move, "이동은 −1 · 0 · +1 뿐이다");
        }

        return ((input.Move + 1) << _moveShift)
            | (input.Jump ? _jump : 0)
            | (input.Dash ? _dash : 0)
            | (input.Parry ? _parry : 0)
            | (input.Attack ? _attack : 0)
            | (input.GuardHeld ? _guard : 0);
    }

    /// <summary>코드 하나의 입력.</summary>
    /// <exception cref="ArgumentOutOfRangeException">0 ~ 95 밖이다.</exception>
    public static InputFrame Frame(int code)
    {
        if (code is < 0 or >= Codes)
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "코드는 0 ~ 95 다");
        }

        return new InputFrame(
            (sbyte)((code >> _moveShift) - 1),
            (code & _jump) != 0,
            (code & _dash) != 0,
            (code & _parry) != 0,
            (code & _attack) != 0,
            (code & _guard) != 0);
    }

    /// <summary>한 틱의 입력을 넣는다 — 앞 칸과 같으면 그 칸이 한 틱 길어진다.</summary>
    public void Add(InputFrame input)
    {
        int code = Code(input);
        if (_runs.Count > 0 && _runs[^1][0] == code)
        {
            _runs[^1][1]++;
        }
        else
        {
            _runs.Add([code, 1]);
        }

        Ticks++;
    }

    /// <summary>
    /// 칸들의 틀린 곳 — 모양(<c>[코드, 틱 수]</c> 둘) · 코드(0 ~ 95) · 틱 수(1 이상). 첫 틀린 칸을 말하고, 없으면 null. 줄을 읽는 쪽
    /// (<see cref="AttemptLog.Parse"/>)이 파일:줄을 붙여 멈춘다.
    /// </summary>
    public static string? Problem(IReadOnlyList<int[]> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        for (int i = 0; i < runs.Count; i++)
        {
            int[]? run = runs[i];
            string shown = run is null ? "null" : $"[{string.Join(", ", run)}]";
            if (run is not { Length: 2 })
            {
                return $"{i}번 칸 {shown} — [코드, 틱 수] 둘이 아니다";
            }

            if (run[0] is < 0 or >= Codes)
            {
                return $"{i}번 칸 {shown} — 코드가 0 ~ 95 밖이다";
            }

            if (run[1] < 1)
            {
                return $"{i}번 칸 {shown} — 틱 수가 1 보다 작다";
            }
        }

        return null;
    }

    /// <summary>칸들을 틱마다의 입력으로 편다 — 넣은 입력 그대로.</summary>
    /// <exception cref="ArgumentException">틀린 칸이 있다(<see cref="Problem"/>) — 한 틱도 내기 전에 멈춘다.</exception>
    public static IEnumerable<InputFrame> Play(IReadOnlyList<int[]> runs)
    {
        if (Problem(runs) is { } problem)
        {
            throw new ArgumentException(problem, nameof(runs));
        }

        return Unfold(runs);
    }

    private static IEnumerable<InputFrame> Unfold(IReadOnlyList<int[]> runs)
    {
        foreach (int[] run in runs)
        {
            InputFrame input = Frame(run[0]);
            for (int i = 0; i < run[1]; i++)
            {
                yield return input;
            }
        }
    }
}
