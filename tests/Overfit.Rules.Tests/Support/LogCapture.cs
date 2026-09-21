using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Rules.Tests.Support;

/// <summary>
/// <see cref="Log"/> 의 출력을 가로채 리스트에 모은다. <c>using</c> 범위를 벗어나면 원래 싱크와 레벨을 돌려놓는다.
///
/// <para>
/// ⚠ <see cref="Log.Sink"/> 와 <see cref="Log.Level"/> 은 <b>전역</b>이다. 그래서 테스트 병렬 실행을 껐다
/// (<c>xunit.runner.json</c>) — 안 그러면 한쪽의 Dispose 가 다른 쪽의 싱크를 걷어간다.
/// </para>
/// </summary>
public sealed class LogCapture : IDisposable
{
    private readonly Action<LogLevel, string>? _previousSink;
    private readonly LogLevel _previousLevel;
    private readonly List<string> _lines = new();

    public LogCapture(LogLevel level = LogLevel.Trace)
    {
        _previousSink = Log.Sink;
        _previousLevel = Log.Level;
        Log.Level = level;
        Log.Sink = (_, line) => _lines.Add(line);
    }

    /// <summary>모인 줄. <c>[tag][L] message</c> 형식 그대로다.</summary>
    public IReadOnlyList<string> Lines => _lines;

    public void Dispose()
    {
        Log.Sink = _previousSink;
        Log.Level = _previousLevel;
    }
}
