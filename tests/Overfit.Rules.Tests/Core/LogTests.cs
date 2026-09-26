using Overfit.Core;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Core;

public class LogTests
{
    [Theory]
    [InlineData(LogLevel.Trace, "T")]
    [InlineData(LogLevel.Debug, "D")]
    [InlineData(LogLevel.Info, "I")]
    [InlineData(LogLevel.Warn, "W")]
    [InlineData(LogLevel.Error, "E")]
    public void 형식은_태그와_레벨_표시를_앞에_둔다(LogLevel level, string mark)
    {
        using var capture = new LogCapture();

        switch (level)
        {
            case LogLevel.Trace: Log.Trace("boot", "k=v"); break;
            case LogLevel.Debug: Log.Debug("boot", "k=v"); break;
            case LogLevel.Info: Log.Info("boot", "k=v"); break;
            case LogLevel.Warn: Log.Warn("boot", "k=v"); break;
            default: Log.Error("boot", "k=v"); break;
        }

        capture.Lines.ShouldHaveSingleItem().ShouldBe($"[boot][{mark}] k=v");
    }

    [Fact]
    public void 레벨보다_낮은_줄은_안_찍는다()
    {
        using var capture = new LogCapture(LogLevel.Warn);

        Log.Debug("boot", "숨는다");
        Log.Info("boot", "이것도 숨는다");
        Log.Warn("boot", "보인다");

        capture.Lines.ShouldHaveSingleItem().ShouldBe("[boot][W] 보인다");
    }

    // 이 단언이 헤드리스 판정 전체를 떠받친다. 표지가 레벨에 걸리면 `LOG_LEVEL=error` 로 돌린 실행이
    // 전부 통과인데도 "완료 표지가 없습니다" 로 죽는다 — tools/build.sh 의 judge_headless ② 가 이 줄을 본다.
    [Fact]
    public void 완료_표지는_레벨을_무시하고_항상_찍는다()
    {
        using var capture = new LogCapture(LogLevel.Off);

        Log.Error("boot", "이것조차 숨는다");
        Log.Marker("tour", "tour=done");

        capture.Lines.ShouldHaveSingleItem().ShouldBe("[tour][M] tour=done");
    }

    [Fact]
    public void 비싼_메시지는_레벨이_꺼져_있으면_안_만든다()
    {
        using var capture = new LogCapture(LogLevel.Info);
        int calls = 0;

        Log.Debug("boot", () => { calls++; return "비싸다"; });
        calls.ShouldBe(0);

        using var louder = new LogCapture(LogLevel.Debug);
        Log.Debug("boot", () => { calls++; return "비싸다"; });
        calls.ShouldBe(1);
    }

    [Fact]
    public void 비싼_Info_도_레벨이_꺼져_있으면_안_만든다()
    {
        // BossSwings.Land 는 판정마다 한 줄을 Info 로 남긴다 — 수백만 판 × 10~150 판정이면
        // LOG_LEVEL=off 여도 그 포맷 비용을 다 낸다. 지연 오버로드가 없으면 그걸 피할 방법이 없다.
        using var capture = new LogCapture(LogLevel.Off);
        int calls = 0;

        Log.Info("boot", () => { calls++; return "비싸다"; });
        Log.Warn("boot", () => { calls++; return "비싸다"; });
        Log.Error("boot", () => { calls++; return "비싸다"; });

        calls.ShouldBe(0);
        capture.Lines.ShouldBeEmpty();
    }

    [Fact]
    public void 지연_오버로드도_같은_형식으로_찍는다()
    {
        // 지연이라고 형식이 달라지면 judge_headless 의 ^\[tag\]\[E\] 앵커가 그 줄을 놓친다.
        using var capture = new LogCapture(LogLevel.Trace);

        Log.Info("boot", () => "k=v");
        Log.Warn("boot", () => "k=v");
        Log.Error("boot", () => "k=v");

        capture.Lines.ShouldBe(new[] { "[boot][I] k=v", "[boot][W] k=v", "[boot][E] k=v" });
    }

    [Theory]
    [InlineData("trace", LogLevel.Trace)]
    [InlineData("TRACE", LogLevel.Trace)]
    [InlineData("Warn", LogLevel.Warn)]
    [InlineData("off", LogLevel.Off)]
    public void 문자열_레벨을_대소문자_무시하고_읽는다(string text, LogLevel expected)
    {
        Log.TryParseLevel(text, out LogLevel level).ShouldBeTrue();
        level.ShouldBe(expected);
    }

    [Fact]
    public void 모르는_레벨_문자열은_거절한다()
    {
        Log.TryParseLevel("시끄럽게", out _).ShouldBeFalse();
    }

    [Fact]
    public void 싱크가_없으면_조용히_버린다()
    {
        using var capture = new LogCapture();
        Log.Sink = null;

        Log.Info("boot", "받을 곳이 없다");
        Log.Marker("tour", "이것도");

        capture.Lines.ShouldBeEmpty();
    }
}
