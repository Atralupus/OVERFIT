using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Core;

/// <summary>
/// 판을 세우는 데이터의 지문 (#108 · 설계 2026-09-28 §5.6 · 2026-09-29 조각1 §4.3). 공장의 매니페스트와 시도 기록 한 줄이 같은 정의를 불러야
/// "그 판을 세운 데이터가 지금의 데이터인가" 가 선다 — 되살리기가 판이 다를 때 결정론이 깨졌는지(<c>[E]</c>) 데이터가 바뀌었는지(<c>[W]</c>)를 이것으로 가른다.
/// </summary>
public class DataDigestTests
{
    /// <summary>작은 가짜 데이터 여섯 — 마지막 파일에 한글을 넣어 이름 · 길이가 글자 수가 아니라 UTF-8 바이트로 세어지는지도 본다.</summary>
    private static Dictionary<string, byte[]> Sample() => new(StringComparer.Ordinal)
    {
        ["balance.json"] = Encoding.UTF8.GetBytes("{\"battle\":{\"max_ticks\":36000}}"),
        ["bosses.json"] = Encoding.UTF8.GetBytes("{\"a\":1}"),
        ["fighters.json"] = Encoding.UTF8.GetBytes("{\"b\":2}"),
        ["hitboxes.json"] = Encoding.UTF8.GetBytes("{}"),
        ["patterns.json"] = Encoding.UTF8.GetBytes("{\"c\":[1,2]}"),
        ["stages.json"] = Encoding.UTF8.GetBytes("{\"1\":{\"이름\":\"둘\"}}"),
    };

    [Fact]
    public void 판을_세우는_데이터_여섯을_이름_순으로_읽는다()
    {
        // balance.json 이 든다(설계 2026-09-29 조각1 §4.3) — 판을 세우는 수치(max_ticks · picker)가 거기 있다. 뷰 수치(feel)도 섞여 있어 화면
        // 흔들림 하나에 지문이 바뀌지만, 되살리기에서 그 값은 [W](데이터가 바뀌었다)일 뿐이고 판이 같으면 그대로 일치다.
        var read = new List<string>();
        Dictionary<string, byte[]> files = Sample();

        DataDigest.Of(name =>
        {
            read.Add(name);
            return files[name];
        });

        read.ShouldBe(new[] { "balance.json", "bosses.json", "fighters.json", "hitboxes.json", "patterns.json", "stages.json" });
        DataDigest.Files.ShouldBe(read);
    }

    [Fact]
    public void 다이제스트는_파이썬과_같다()
    {
        // 기대값은 파이썬 hashlib 로 따로 셈했다(파일마다 이름 \0 길이 \0 바이트 — DetTests 의 골든과 같은 방법). 같은 코드로 기대값을 내면
        // 정의의 실수가 양쪽에 같이 들어 초록이 된다. balance.json 이 들기 전(다섯)의 값은 96548b94… 였다.
        Dictionary<string, byte[]> files = Sample();

        DataDigest.Of(name => files[name]).ShouldBe("103b7ef603b1616e98495aece15c452fb465ee4462d7ee5b6650a8c74c4b3ac5");
    }

    [Fact]
    public void 다이제스트는_파일_하나만_바뀌어도_바뀐다()
    {
        Dictionary<string, byte[]> files = Sample();
        string before = DataDigest.Of(name => files[name]);

        files["patterns.json"] = Encoding.UTF8.GetBytes("{\"c\":[1,3]}");

        DataDigest.Of(name => files[name]).ShouldNotBe(before);
    }

    [Fact]
    public void 다이제스트는_경계를_가른다()
    {
        // 앞 파일의 끝 바이트를 뒤 파일의 머리로 옮긴다 — 바이트만 이으면 같은 열이라 같은 해시다. 이름과 길이가 둘을 가른다.
        Dictionary<string, byte[]> files = Sample();
        string before = DataDigest.Of(name => files[name]);

        byte[] bosses = files["bosses.json"];
        files["bosses.json"] = bosses[..^1];
        files["fighters.json"] = [bosses[^1], .. files["fighters.json"]];

        DataDigest.Of(name => files[name]).ShouldNotBe(before);
    }

    [Fact]
    public void 지금의_데이터로_선다()
    {
        // 테스트의 출력 폴더 data/ 가 overfit/data 의 복사본이다 — 여섯이 다 있고 지문이 16진 64자로 선다.
        string digest = DataDigest.Of(name => File.ReadAllBytes(Path.Combine("data", name)));

        digest.Length.ShouldBe(64);
        digest.ShouldMatch("^[0-9a-f]{64}$");
    }
}
