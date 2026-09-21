using System;
using System.Runtime.CompilerServices;
using Godot;

namespace PreReLU.Core;

/// <summary>
/// <see cref="Log"/> 의 Godot 쪽 절반. 출력(<c>GD.Print</c>)과 레벨 결정(유저 인자 · 환경변수 · 디버그 빌드)이 여기 있다.
///
/// <para>
/// <b>언제 꽂히나.</b> <see cref="AutoInstall"/> 이 모듈 초기화자라 이 어셈블리의 어떤 코드보다 먼저 돈다 —
/// Autoload 첫 주자인 <c>Balance</c> 가 부팅 로그를 남기기 전이므로 설치 전 로그가 없다(유실 0).
/// 꽂는 일은 델리게이트 두 개를 대입하는 것뿐이라 이 시점에 네이티브 호출이 없고, 실제 <c>OS.*</c> 호출은
/// 첫 로그(레벨 결정) · 첫 Warn(푸시 여부) 때로 미룬다.
/// <c>Game._Ready</c> 첫 줄도 <see cref="Install"/> 을 부른다(멱등) — 부팅 경로에서 눈에 보이라고 남긴 명시적 호출이다.
/// </para>
/// </summary>
public static class LogSink
{
    private static bool _installed;

    private static bool? _pushToGodot;

    /// <summary>Godot 채널(PushWarning/PushError)에 올릴지. 창 실행·에디터는 true, 헤드리스는 false. 처음 Warn/Error 때 한 번 정하고 남긴다.</summary>
    private static bool PushToGodot
    {
        get
        {
            if (_pushToGodot is { } decided)
            {
                return decided;
            }

            string display = DisplayServer.GetName();
            bool headless = display == "headless";
            _pushToGodot = !headless;
            Log.Debug("boot", $"log push_to_godot={(headless ? "false" : "true")} reason={(headless ? "headless" : "windowed")} display={display}"
                + $" editor={OS.HasFeature("editor")}");
            return !headless;
        }
    }

    /// <summary>Log 에 Godot 출력과 레벨 판정을 꽂는다. 여러 번 불러도 안전하다.</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Log.Sink = Print;
        Log.LevelResolver = ResolveInitialLevel;
    }

    // CA2255: 모듈 초기화자는 애플리케이션 코드에서만 쓰라는 경고다. 여기가 바로 그 애플리케이션이고(라이브러리가 아니다),
    // "첫 로그보다 먼저"를 보장할 다른 자리가 없다 — Autoload 는 Balance 가 먼저 돌면서 이미 로그를 남긴다.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void AutoInstall() => Install();
#pragma warning restore CA2255

    private static void Print(LogLevel level, string line)
    {
        GD.Print(line);

        // 경고·에러는 순서가 보존되는 출력 스트림(위 한 줄)에 더해 Godot 채널에도 올린다 (MCP errors · 에디터 디버거).
        // 헤드리스는 뺀다 — PushWarning/PushError 가 C# 백트레이스를 통째로 찍어 로그를 읽을 수 없게 된다.
        if (level < LogLevel.Warn || !PushToGodot)
        {
            return;
        }

        if (level == LogLevel.Warn)
        {
            GD.PushWarning(line);
        }
        else
        {
            GD.PushError(line);
        }
    }

    private static LogLevel ResolveInitialLevel()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--log-level=", StringComparison.Ordinal) && Log.TryParseLevel(arg["--log-level=".Length..], out LogLevel fromArg))
            {
                return fromArg;
            }
        }

        string env = OS.GetEnvironment("PRERELU_LOG_LEVEL");
        if (!string.IsNullOrEmpty(env) && Log.TryParseLevel(env, out LogLevel fromEnv))
        {
            return fromEnv;
        }

        return OS.IsDebugBuild() ? LogLevel.Debug : LogLevel.Info;
    }
}
