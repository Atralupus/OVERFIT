using System;
using System.Globalization;

namespace PreReLU.Core;

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
