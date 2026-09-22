using System.Collections.Generic;

namespace Overfit.Core;

// data/credits.json 의 DTO. Godot 을 모르는 순수 C# — 크레딧 화면이 이것으로 화면을 짓는다.
//
// **크레딧을 두 곳에 적지 않기 위해 있는 파일이다.** 출처·링크·라이선스를 씬에도 문서에도
// 손으로 적어 두면, 팩 하나가 바뀌는 날 한쪽만 고쳐진다. 그 어긋남은 아무 테스트도 빨갛게 하지 않고
// 라이선스 주장만 조용히 거짓이 된다. 그래서 진실은 JSON 하나이고, 화면도 문서도 그것을 가리킨다.

/// <summary>크레딧 한 줄 — 에셋 팩 하나.</summary>
public sealed class CreditEntry
{
    /// <summary>게임에서 무엇으로 쓰는가 (플레이어 · 보스 · 배경). 사람이 화면에서 먼저 읽는 칸이다.</summary>
    public required string Role { get; init; }

    /// <summary>팩 이름. 받는 곳에 적힌 그대로 적는다 — 우리가 부르는 별명을 적으면 검색이 안 된다.</summary>
    public required string Name { get; init; }

    /// <summary>만든 사람. CC0 라 표시 의무는 없지만, 이 줄이 이 화면이 존재하는 이유다.</summary>
    public required string Creator { get; init; }

    /// <summary>받는 곳. 화면이 그대로 열므로(<c>OS.ShellOpen</c>) 열리는 주소여야 한다.</summary>
    public required string Url { get; init; }

    /// <summary>
    /// 라이선스 이름. <b>받은 파일 안의 라이선스를 읽고 적는다</b> — 스토어 페이지 문구는 근거가 아니다.
    /// 페이지는 고쳐도 아무도 모르지만, 우리가 받은 zip 안의 한 줄은 그대로 남는다.
    /// </summary>
    public required string License { get; init; }

    /// <summary>덧붙일 말(없어도 된다). "언제부터 쓴다" 같은 상태를 화면에서 흐리지 않게 적는 칸이다.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// <c>data/credits.json</c> 전체. 리스트인 이유는 <b>순서가 곧 화면의 순서</b>이기 때문이다 —
/// id 를 키로 하는 표(<c>fighters.json</c> 같은)로 두면 무엇이 먼저 나오는지를 아무도 정하지 않은 것이 된다.
/// </summary>
public sealed class CreditsData
{
    /// <summary>화면에 나오는 순서 그대로.</summary>
    public required List<CreditEntry> Entries { get; init; }
}
