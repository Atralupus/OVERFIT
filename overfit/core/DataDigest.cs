using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Overfit.Core;

/// <summary>
/// 판을 세우는 데이터의 지문 — <c>overfit/data/</c> 의 전투 데이터 다섯의 sha256 (#108 · 설계 2026-09-28 §5.6). 공장이 매니페스트에 싣는다 —
/// 어느 게임에서 지은 표본인지. 옛 망은 이것을 배운 게임의 지문(<c>trained_on</c>)으로 들고 부팅이 지금의 값과 대 봤다 — 망과 같이 걷었다
/// (설계 2026-09-29 조각1 §6).
///
/// <para>
/// <c>balance.json</c> 은 안 넣는다 — 뷰 수치(<c>feel</c>)가 섞여 있어 화면 흔들림 하나를 고쳐도 지문이 바뀐다. 규칙 <b>코드</b>의
/// 변화는 리플레이 골든이 잡는다.
/// </para>
///
/// <para>
/// 파일마다 <c>이름 \0 길이 \0 바이트</c> 를 잇는다 — 바이트만 이으면 한 파일의 끝 바이트를 다음 파일의 머리로 옮겨도 같은 해시다. 파일은 바이트
/// 그대로 읽는다: 줄 끝을 바꿔 체크아웃하는 기기(윈도의 autocrlf)는 다른 지문을 내는데, 그 기기가 만든 판도 바이트가 다른 데이터를 읽은 것이다.
/// </para>
/// </summary>
public static class DataDigest
{
    /// <summary>지문에 드는 파일 — 이름 순이다. 판을 세우는 데이터 다섯(<c>BattleTables</c>)과 같다.</summary>
    public static IReadOnlyList<string> Files { get; } = ["bosses.json", "fighters.json", "hitboxes.json", "patterns.json", "stages.json"];

    /// <summary>
    /// <see cref="Files"/> 를 그 순서로 읽어 지문을 낸다 — sha256 의 소문자 16진 64자. 읽는 길은 부르는 쪽이 준다: 게임은 <c>res://data/</c>,
    /// 공장은 디스크의 <c>overfit/data/</c>.
    /// </summary>
    public static string Of(Func<string, byte[]> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] separator = [0];
        foreach (string name in Files)
        {
            byte[] bytes = read(name);
            sha.AppendData(Encoding.UTF8.GetBytes(name));
            sha.AppendData(separator);
            sha.AppendData(Encoding.UTF8.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture)));
            sha.AppendData(separator);
            sha.AppendData(bytes);
        }

        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
