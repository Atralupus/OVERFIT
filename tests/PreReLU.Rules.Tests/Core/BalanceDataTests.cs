using System.Collections.Generic;
using System.IO;
using PreReLU.Core;
using Shouldly;
using Xunit;

namespace PreReLU.Rules.Tests.Core;

/// <summary>
/// 수치는 코드가 아니라 <c>prerelu/data/*.json</c> 에 있다. 이 테스트는 그 파일들이
/// <b>실제로</b> DTO 로 읽히는지를 Godot 없이 확인한다 — csproj 가 출력 폴더의 <c>data/</c> 로 복사한다.
/// </summary>
public class BalanceDataTests
{
    private static string ReadData(string name) => File.ReadAllText(Path.Combine("data", name));

    [Fact]
    public void 실제_balance_json_이_읽힌다()
    {
        BalanceData data = JsonData<BalanceData>.ParseOne(ReadData("balance.json"), "balance.json");

        data.Version.ShouldBe(1);
    }

    [Fact]
    public void 밑줄로_시작하는_키는_주석이라_데이터로_세지_않는다()
    {
        List<string> keys = JsonData.TopLevelKeys(ReadData("balance.json"), "balance.json");

        keys.ShouldNotContain("_comment");
        keys.ShouldNotContain("_version");
    }

    [Fact]
    public void 루트가_객체가_아니면_거절한다()
    {
        Should.Throw<DataException>(() => JsonData<BalanceData>.ParseOne("[1, 2]", "고친것"))
            .Message.ShouldContain("루트가 객체가 아니다");
    }

    [Fact]
    public void 문법이_깨지면_어디서_깨졌는지_알려준다()
    {
        Should.Throw<DataException>(() => JsonData<BalanceData>.ParseOne("{ \"a\": }", "고친것"))
            .Message.ShouldContain("JSON 문법 오류");
    }

    [Fact]
    public void 필수_키가_빠지면_빠진_것을_전부_나열한다()
    {
        // 한 번에 하나씩 고치게 하지 않는다 — System.Text.Json 은 첫 번째에서 멈추지만 RequiredKeys 는 전부 모은다.
        DataException thrown = Should.Throw<DataException>(
            () => JsonData<Probe>.ParseOne("{ \"kept\": 1 }", "고친것"));

        thrown.Message.ShouldContain("필수 키 누락 2개");
        thrown.Message.ShouldContain("first_missing");
        thrown.Message.ShouldContain("second_missing");
    }

    [Fact]
    public void 표는_중복_id_를_잡는다()
    {
        // JSON 에 같은 키가 두 번 있으면 조용히 덮어쓰이는 것이 기본 동작이다. 그것을 막는다.
        Should.Throw<DataException>(
            () => JsonData<Entry>.ParseTable("{ \"가\": {\"n\": 1}, \"가\": {\"n\": 2} }", "고친것"))
            .Message.ShouldContain("중복 id");
    }

    [Fact]
    public void 표는_한글_id_를_그대로_쓴다()
    {
        Dictionary<string, Entry> table = JsonData<Entry>.ParseTable(
            "{ \"_comment\": \"주석\", \"신경망\": {\"n\": 7} }", "고친것");

        table.Keys.ShouldHaveSingleItem().ShouldBe("신경망");
        table["신경망"].N.ShouldBe(7);
    }

    /// <summary>필수 키 누락을 보기 위한 시험용 DTO. 게임 데이터가 아니다.</summary>
    public sealed class Probe
    {
        public int Kept { get; init; }

        public required int FirstMissing { get; init; }

        public required int SecondMissing { get; init; }
    }

    /// <summary>표 파싱을 보기 위한 시험용 DTO. 게임 데이터가 아니다.</summary>
    public sealed class Entry
    {
        public required int N { get; init; }
    }
}
