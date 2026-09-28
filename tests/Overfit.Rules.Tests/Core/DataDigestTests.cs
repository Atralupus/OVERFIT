using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Core;

/// <summary>
/// 망이 배운 게임의 지문 (#108 · 설계 2026-09-28 §5.6). 공장 · 학습 · 게임이 같은 정의를 불러야 "배운 게임이 지금의 게임인가" 가 선다.
/// </summary>
public class DataDigestTests
{
    /// <summary>작은 가짜 데이터 다섯 — 마지막 파일에 한글을 넣어 이름 · 길이가 글자 수가 아니라 UTF-8 바이트로 세어지는지도 본다.</summary>
    private static Dictionary<string, byte[]> Sample() => new(StringComparer.Ordinal)
    {
        ["bosses.json"] = Encoding.UTF8.GetBytes("{\"a\":1}"),
        ["fighters.json"] = Encoding.UTF8.GetBytes("{\"b\":2}"),
        ["hitboxes.json"] = Encoding.UTF8.GetBytes("{}"),
        ["patterns.json"] = Encoding.UTF8.GetBytes("{\"c\":[1,2]}"),
        ["stages.json"] = Encoding.UTF8.GetBytes("{\"1\":{\"이름\":\"둘\"}}"),
    };

    [Fact]
    public void 전투_데이터_다섯을_이름_순으로_읽는다()
    {
        // balance.json 은 없다 — 뷰 수치가 섞여 있어 화면 흔들림 하나에도 경보가 난다(설계 §5.6).
        var read = new List<string>();
        Dictionary<string, byte[]> files = Sample();

        DataDigest.Of(name =>
        {
            read.Add(name);
            return files[name];
        });

        read.ShouldBe(new[] { "bosses.json", "fighters.json", "hitboxes.json", "patterns.json", "stages.json" });
        DataDigest.Files.ShouldBe(read);
    }

    [Fact]
    public void 다이제스트는_파이썬과_같다()
    {
        // 기대값은 파이썬 hashlib 로 따로 셈했다(파일마다 이름 \0 길이 \0 바이트 — DetTests 의 골든과 같은 방법). 학습(ml/)이 같은 정의를
        // 파이썬으로 부를 때 이 값이 다리다.
        Dictionary<string, byte[]> files = Sample();

        DataDigest.Of(name => files[name]).ShouldBe("96548b94f5a82985d698fe706fda96a1cc18a119d71102a1ac406e773ce53b98");
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
        // 테스트의 출력 폴더 data/ 가 overfit/data 의 복사본이다 — 다섯이 다 있고 지문이 16진 64자로 선다.
        string digest = DataDigest.Of(name => File.ReadAllBytes(Path.Combine("data", name)));

        digest.Length.ShouldBe(64);
        digest.ShouldMatch("^[0-9a-f]{64}$");
    }
}
