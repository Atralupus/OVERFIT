using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Core;

/// <summary>
/// 수치는 코드가 아니라 <c>overfit/data/*.json</c> 에 있다. 이 테스트는 그 파일들이
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
    public void 연출_수치도_balance_json_에_있다()
    {
        // 뷰만 읽는 값이라 규칙 테스트가 하나도 안 건드린다 — 그래서 블록이 통째로 사라져도
        // 조용하다. `required` 가 부팅을 멈추게 하지만, 그 실패는 Godot 을 띄워야만 보인다.
        // 여기서 Godot 없이 초 단위로 본다.
        BalanceData data = JsonData<BalanceData>.ParseOne(ReadData("balance.json"), "balance.json");

        data.Feel.HitstopFrames.ShouldBeInRange(1, 20, "히트스톱이 프레임 단위를 벗어났다");
        data.Feel.DashGhostInterval.ShouldBeGreaterThan(0, "잔상 간격이 0 이면 프레임마다 잔상이 쏟아진다");
        data.Feel.SparkCount.ShouldBeGreaterThan(0);
        data.Feel.DeathHoldSeconds.ShouldBeGreaterThan(0, "사망 애니메이션을 볼 시간이 없다");
        data.Feel.BossHitFlashSeconds.ShouldBeGreaterThan(0, "보스의 흰 플래시가 안 보인다 — 때린 것이 닿았는지가 화면에 안 남는다");
    }

    [Fact]
    public void DTO_가_안_읽는_키가_balance_json_에_없다()
    {
        // JsonData 는 DTO 가 모르는 키를 조용히 버린다. 부팅의 `required` 검사는 "DTO 가 원하는 키가 JSON 에 있나" 한 방향만
        // 보므로, 프로퍼티를 걷고 JSON 의 키를 남기면 아무도 안 읽는 수치가 진실 원천에 조용히 남는다.
        // 보스 공격의 링 키 넷(tell_ring_from · tell_ring_to · boss_ring_offset_y · boss_ring_to)을 걷을 때(#81) DTO 만 먼저
        // 뺀 상태로 규칙 테스트를 돌렸더니 389건 중 이것 하나만 빨갰다 — 다른 테스트는 그 상태를 못 본다.
        using JsonDocument doc = JsonData.ParseDocument(ReadData("balance.json"), "balance.json");
        JsonElement root = doc.RootElement;

        var unread = new List<string>();
        unread.AddRange(UnreadKeys(root, typeof(BalanceData), ""));
        unread.AddRange(UnreadKeys(root.GetProperty("battle"), typeof(BattleBalance), "battle."));
        unread.AddRange(UnreadKeys(root.GetProperty("feel"), typeof(FeelBalance), "feel."));

        unread.ShouldBeEmpty("DTO 에 짝이 없는 키 — 아무도 안 읽는 수치다. 프로퍼티와 키는 같이 걷는다");
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

    /// <summary><paramref name="obj"/> 의 데이터 키(주석 <c>_…</c> 은 빼고) 중 <paramref name="dto"/> 의 프로퍼티와 짝이 없는 것.</summary>
    private static IEnumerable<string> UnreadKeys(JsonElement obj, Type dto, string prefix)
    {
        HashSet<string> known = dto.GetProperties().Select(RequiredKeys.JsonName).ToHashSet();
        return obj.EnumerateObject()
            .Select(p => p.Name)
            .Where(k => !JsonData.IsMetaKey(k) && !known.Contains(k))
            .Select(k => prefix + k);
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
