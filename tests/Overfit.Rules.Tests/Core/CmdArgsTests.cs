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
