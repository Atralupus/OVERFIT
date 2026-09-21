using System;

namespace Overfit.Core;

public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Off = 5,
}

/// <summary>
/// 구조화 로그. 형식은 <c>[tag][L] key=value ...</c> — grep 과 MCP get_debug_output 으로 읽기 위해서다.
/// 태그는 늘어난다. 지금 쓰는 것: <c>boot data scene tour result boss strike dodge</c>.
///
/// <para>
/// <b>이 클래스는 Godot 을 모른다.</b> 출력은 <see cref="Sink"/>, 레벨 결정은 <see cref="LevelResolver"/> 로 주입한다 —
/// Godot 쪽 구현은 <c>core/LogSink.cs</c> 이고 모듈 초기화 때 스스로 꽂는다.
/// 규칙 층이 Log 를 부르므로, 이 파일이 Godot 을 참조하면 규칙 전체가 Godot 없이는 못 돈다.
/// </para>
///
/// <para>
/// 레벨은 낮을수록 시끄럽다. 디버깅은 전부 로그로 하므로 <b>많이 남기고 레벨로 거른다.</b>
/// <list type="bullet">
/// <item>Trace — 매 틱 값 (위치 · 잔량 · 거리). 켜면 프레임당 수십 줄</item>
/// <item>Debug — 판단과 전이 (왜 이 선택인지 · 상태 머신)</item>
/// <item>Info — 유저가 보는 사건 (씬 전환 · 승패)</item>
/// <item>Warn — 이상하지만 계속 간다 (데이터 누락 기본값)</item>
/// <item>Error — 규칙 위반 (없는 id · 음수 값). 헤드리스 판정이 이걸 보면 실패시킨다</item>
/// </list>
/// 레벨이 아닌 것이 하나 있다 — <see cref="Marker"/> 의 <c>[M]</c> 은 헤드리스 완료 표지라 레벨과 무관하게 항상 찍는다.
/// </para>
///
/// 기본값: 디버그 빌드 Debug, 릴리스 Info. <c>--log-level=trace</c> 유저 인자 ·
/// <c>OVERFIT_LOG_LEVEL</c> 환경변수 · <see cref="Level"/> 대입으로 바꾼다.
/// </summary>
public static class Log
{
    private static readonly string[] _levelMarks = { "T", "D", "I", "W", "E" };

    private static LogLevel? _level;

    /// <summary>
    /// 완성된 한 줄(<c>[tag][L] message</c>)을 받아 내보낸다. 꽂히기 전 로그는 버려진다 —
    /// Godot 에서는 모듈 초기화가 어떤 게임 코드보다 먼저 꽂으므로 유실이 없다.
    /// </summary>
    public static Action<LogLevel, string>? Sink { get; set; }

    /// <summary>
    /// 초기 레벨을 정하는 방법. 처음 <see cref="Level"/> 을 읽을 때 한 번 부른다.
    /// 없으면 <see cref="LogLevel.Debug"/>.
    /// </summary>
    public static Func<LogLevel>? LevelResolver { get; set; }

    public static LogLevel Level
    {
        get => _level ??= LevelResolver?.Invoke() ?? LogLevel.Debug;
        set => _level = value;
    }

    public static bool IsEnabled(LogLevel level) => level >= Level;

    public static void Trace(string tag, string message) => Write(LogLevel.Trace, tag, message);

    public static void Debug(string tag, string message) => Write(LogLevel.Debug, tag, message);

    public static void Info(string tag, string message) => Write(LogLevel.Info, tag, message);

    public static void Warn(string tag, string message) => Write(LogLevel.Warn, tag, message);

    public static void Error(string tag, string message) => Write(LogLevel.Error, tag, message);

    /// <summary>
    /// 헤드리스 완료 표지 — <c>[tag][M] …</c>. <b>로그 레벨과 무관하게 항상 찍는다.</b>
    ///
    /// <para>
    /// 이건 사건 로그가 아니라 검증 프로토콜이다. <c>tools/build.sh</c> 의 <c>judge_headless</c> 가
    /// 이 줄 하나로 "게임이 끝까지 갔는가"를 판정한다. 레벨에 걸리게 두면 <c>LOG_LEVEL=warn</c> 이
    /// 전부 통과인데도 거짓 실패를 낸다.
    /// </para>
    ///
    /// <see cref="Write"/> 와 달리 레벨 검사를 건너뛰지만, 출력은 똑같이 <see cref="Sink"/> 를 탄다 —
    /// 이 클래스는 Godot 을 모른다. 싱크에 넘기는 레벨은 Info 다: 표지는 경고도 에러도 아니다.
    /// </summary>
    public static void Marker(string tag, string message) => Sink?.Invoke(LogLevel.Info, $"[{tag}][M] {message}");

    /// <summary>메시지를 만드는 비용이 클 때. 레벨이 꺼져 있으면 람다를 부르지 않는다.
    /// 다섯 레벨 모두 짝이 있다 — 하나만 빠지면 그 자리에서 즉시 오버로드가 조용히 골라진다.</summary>
    public static void Trace(string tag, Func<string> message)
    {
        if (IsEnabled(LogLevel.Trace))
        {
            Write(LogLevel.Trace, tag, message());
        }
    }

    public static void Debug(string tag, Func<string> message)
    {
        if (IsEnabled(LogLevel.Debug))
        {
            Write(LogLevel.Debug, tag, message());
        }
    }

    /// <summary>
    /// 지연 <see cref="Info(string, string)"/>. <b>Info 도 뜨거운 자리가 있다</b> — <c>BattleSim.Land</c> 는 보스 판정
    /// 하나마다 한 줄을 남기는데, 학습 데이터 공장은 한 판에 10~150 판정을 수백만 판 돌린다.
    /// 즉시 오버로드만 있으면 <c>LOG_LEVEL=off</c> 로 돌려도 그 포맷 비용을 전부 낸다.
    /// </summary>
    public static void Info(string tag, Func<string> message)
    {
        if (IsEnabled(LogLevel.Info))
        {
            Write(LogLevel.Info, tag, message());
        }
    }

    /// <summary>지연 <see cref="Warn(string, string)"/>. 대칭을 위해 둔다 — 없으면 뜨거운 자리에서 레벨만 올려도
    /// 즉시 오버로드가 조용히 골라진다.</summary>
    public static void Warn(string tag, Func<string> message)
    {
        if (IsEnabled(LogLevel.Warn))
        {
            Write(LogLevel.Warn, tag, message());
        }
    }

    /// <summary>지연 <see cref="Error(string, string)"/>. 대칭을 위해 둔다.</summary>
    public static void Error(string tag, Func<string> message)
    {
        if (IsEnabled(LogLevel.Error))
        {
            Write(LogLevel.Error, tag, message());
        }
    }

    /// <summary>문자열로 온 레벨(<c>--log-level=trace</c> · 환경변수)을 읽는다.</summary>
    public static bool TryParseLevel(string text, out LogLevel level) => Enum.TryParse(text, ignoreCase: true, out level);

    private static void Write(LogLevel level, string tag, string message)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        Action<LogLevel, string>? sink = Sink;
        if (sink is null)
        {
            return;
        }

        sink(level, $"[{tag}][{_levelMarks[(int)level]}] {message}");
    }
}
