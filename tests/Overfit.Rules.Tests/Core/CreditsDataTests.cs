using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Core;

/// <summary>
/// 크레딧은 <c>overfit/data/credits.json</c> 하나에서 온다 — 화면도 문서도 그것을 읽는다.
/// 여기서 보는 것은 "파싱되는가" 가 아니라 <b>크레딧으로 쓸 만한가</b> 다:
/// 링크가 링크인가 · 라이선스 칸이 비지 않았는가 · 같은 팩이 두 번 적히지 않았는가.
///
/// <para>
/// 이 검사가 없으면 실수는 조용하다. 크레딧 화면은 게임 진행에 아무 영향이 없어서
/// 링크 하나가 <c>htp://</c> 로 깨져도 스모크도 데모도 초록이다.
/// </para>
/// </summary>
public class CreditsDataTests
{
    private static CreditsData Load() =>
        JsonData<CreditsData>.ParseOne(File.ReadAllText(Path.Combine("data", "credits.json")), "credits.json");

    [Fact]
    public void 실제_credits_json_이_읽힌다()
    {
        CreditsData data = Load();

        data.Entries.ShouldNotBeEmpty("크레딧이 비면 화면이 빈 채로 뜬다");
    }

    [Fact]
    public void 모든_항목이_열_수_있는_링크를_갖는다()
    {
        // 화면이 OS.ShellOpen 에 그대로 넘긴다. http 가 아닌 글자를 넘기면 아무 일도 안 일어나고
        // 그 무반응은 로그에도 안 남는다 — 그래서 여기서 막는다.
        foreach (CreditEntry entry in Load().Entries)
        {
            entry.Url.ShouldStartWith("https://", Case.Sensitive, $"{entry.Name}: 링크가 https 가 아니다");
        }
    }

    [Fact]
    public void 모든_항목에_만든_사람과_라이선스가_적혀_있다()
    {
        foreach (CreditEntry entry in Load().Entries)
        {
            entry.Name.ShouldNotBeNullOrWhiteSpace();
            entry.Role.ShouldNotBeNullOrWhiteSpace();
            entry.Creator.ShouldNotBeNullOrWhiteSpace($"{entry.Name}: 만든 사람이 비었다 — 크레딧이 아니다");
            entry.License.ShouldNotBeNullOrWhiteSpace($"{entry.Name}: 라이선스가 비었다");
        }
    }

    [Fact]
    public void 같은_팩이_두_번_적히지_않는다()
    {
        List<CreditEntry> entries = Load().Entries;

        entries.Select(e => e.Name).Distinct().Count().ShouldBe(entries.Count, "같은 이름이 두 번 있다");
        entries.Select(e => e.Url).Distinct().Count().ShouldBe(entries.Count, "같은 링크가 두 번 있다");
    }

    [Fact]
    public void 항목의_필수_키가_빠지면_어느_항목의_무슨_키인지_알려준다()
    {
        // 한 항목이라도 반쯤 적히면 화면에 빈칸이 뜬다. 부팅을 멈추되 **어디가 빈지**를 말해야
        // 한 번에 고친다 (JsonData 의 RequiredKeys 가 배열 인덱스까지 내려간다).
        const string Half = """
            { "entries": [ { "role": "배경", "name": "어떤 팩", "url": "https://example.com" } ] }
            """;

        Should.Throw<DataException>(() => JsonData<CreditsData>.ParseOne(Half, "고친것"))
            .Message.ShouldContain("entries[0].creator");
    }
}
