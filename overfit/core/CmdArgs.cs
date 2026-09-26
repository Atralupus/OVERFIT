using System;
using System.Globalization;

namespace Overfit.Core;

/// <summary>
/// <c>--키=값</c> 유저 인자 파싱. 순수 C# — 문자열 배열만 받고 Godot 을 모른다
/// (인자를 얻는 <c>OS.GetCmdlineUserArgs</c> 는 부르는 쪽 몫).
/// 씬 · 데모 · 디버그 도구가 같은 규칙으로 읽는다.
/// </summary>
public static class CmdArgs
{
    /// <summary><c>--키</c> 가 있나. 값이 없는 깃발용.</summary>
    public static bool Has(string[] args, string name)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Array.IndexOf(args, name) >= 0;
    }

    /// <summary><c>--키=숫자</c> 를 찾아 돌려준다. 없거나 숫자가 아니면 null. 로케일과 무관하게 InvariantCulture 로 읽는다.</summary>
    public static double? Double(string[] args, string prefix)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal)
                && double.TryParse(arg[prefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>--키=정수</c> 를 <b>64비트 그대로</b> 읽는다 (#72 · 설계 §4.4). 없거나 부호 없는 10진 정수가 아니면(부호 · 소수점 ·
    /// 2^64 이상) null — 거짓 0 이나 잘린 값을 돌려주면 조용히 다른 판이 된다. 시드용이다: <see cref="Double"/> 은 2^53 을
    /// 넘는 비트를 잃는데, 시도 시드는 <c>Det.Hash64</c> 의 출력이라 거의 다 그 위다 — 로그의 <c>seed=</c> 를 데모에
    /// 그대로 넘겨도 다른 판이 돌았다.
    /// </summary>
    public static ulong? UInt64(string[] args, string prefix)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal)
                && ulong.TryParse(arg.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out ulong value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// <c>--키=값</c> 을 찾아 <b>문자열 그대로</b> 돌려준다. 없거나 값이 비었으면 null.
    /// 한글 값이 그대로 온다 — 인코딩 변환을 하지 않는다.
    /// </summary>
    public static string? Text(string[] args, string prefix)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (string arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal) && arg.Length > prefix.Length)
            {
                return arg[prefix.Length..];
            }
        }

        return null;
    }
}
