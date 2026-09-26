using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Core;

public class CmdArgsTests
{
    private static readonly string[] _args = { "--tour", "--log-level=trace", "--seed=17", "--who=신경망", "--empty=" };

    [Fact]
    public void 깃발은_있고_없음만_본다()
    {
        CmdArgs.Has(_args, "--tour").ShouldBeTrue();
        CmdArgs.Has(_args, "--shots").ShouldBeFalse();
        // 접두사가 아니라 통째로 같아야 한다 — `--tour` 를 찾는데 `--tourist` 가 걸리면 안 된다.
        CmdArgs.Has(new[] { "--tourist" }, "--tour").ShouldBeFalse();
    }

    [Fact]
    public void 숫자는_로케일과_무관하게_읽는다()
    {
        CmdArgs.Double(_args, "--seed=").ShouldBe(17.0);
        CmdArgs.Double(_args, "--missing=").ShouldBeNull();
        // 숫자가 아니면 그 인자는 없는 것으로 친다 — 거짓 0 을 돌려주면 조용히 다른 판이 된다.
        CmdArgs.Double(new[] { "--seed=많이" }, "--seed=").ShouldBeNull();
        CmdArgs.Double(new[] { "--x=-1.5" }, "--x=").ShouldBe(-1.5);
    }

    [Fact]
    public void 시드는_64비트를_그대로_읽는다()
    {
        // 시도 시드는 Hash64 의 출력이라 거의 다 2^53 을 넘는다 (#72 · 설계 §4.4). Double 로 읽으면 2^53 + 1 이 2^53 이 되어
        // 로그의 seed= 를 데모에 그대로 넘겨도 다른 판이 돈다 — 마지막 줄이 그 손실을 보인다.
        CmdArgs.UInt64(new[] { "--seed=9007199254740993" }, "--seed=").ShouldBe(9007199254740993UL);
        CmdArgs.UInt64(new[] { "--seed=18446744073709551615" }, "--seed=").ShouldBe(ulong.MaxValue);
        CmdArgs.UInt64(_args, "--seed=").ShouldBe(17UL);
        ((ulong)CmdArgs.Double(new[] { "--seed=9007199254740993" }, "--seed=")!.Value).ShouldBe(9007199254740992UL);
    }

    [Fact]
    public void 시드가_아닌_값은_없는_것으로_친다()
    {
        // 거짓 0 이나 잘린 값을 돌려주면 조용히 다른 판이 된다 — Double 과 같은 규약이다.
        CmdArgs.UInt64(new[] { "--seed=-1" }, "--seed=").ShouldBeNull();
        CmdArgs.UInt64(new[] { "--seed=1.5" }, "--seed=").ShouldBeNull();
        CmdArgs.UInt64(new[] { "--seed=18446744073709551616" }, "--seed=").ShouldBeNull();
        CmdArgs.UInt64(new[] { "--seed=많이" }, "--seed=").ShouldBeNull();
        CmdArgs.UInt64(_args, "--missing=").ShouldBeNull();
    }

    [Fact]
    public void 문자열은_한글도_그대로_돌려준다()
    {
        CmdArgs.Text(_args, "--who=").ShouldBe("신경망");
        CmdArgs.Text(_args, "--log-level=").ShouldBe("trace");
    }

    [Fact]
    public void 값이_비었으면_없는_것으로_친다()
    {
        CmdArgs.Text(_args, "--empty=").ShouldBeNull();
        CmdArgs.Text(_args, "--nope=").ShouldBeNull();
    }

    [Fact]
    public void 같은_키가_여럿이면_첫_번째를_쓴다()
    {
        string[] twice = { "--seed=1", "--seed=2" };
        CmdArgs.Double(twice, "--seed=").ShouldBe(1.0);
        CmdArgs.Text(twice, "--seed=").ShouldBe("1");
    }
}
