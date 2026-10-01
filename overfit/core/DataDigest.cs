using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Overfit.Core;

/// <summary>
/// 판을 세우는 데이터의 지문 — <c>overfit/data/</c> 의 여섯의 sha256 (#108 · 설계 2026-09-28 §5.6 · 2026-09-29 조각1 §4.3). 공장이 매니페스트에
/// 싣고(어느 게임에서 지은 표본인지), 시도 기록 한 줄이 싣는다(<c>data_sha256</c>) — 되살린 판이 기록과 다를 때 결정론이 깨졌는지(지문이 같다 ·
/// <c>[E]</c>) 데이터가 바뀌었는지(다르다 · <c>[W]</c>)를 가른다. 규칙 <b>코드</b>의 변화는 리플레이 골든이 잡는다.
///
/// <para>
/// <c>balance.json</c> 이 든다(조각 1 §4.3) — 판을 세우는 수치(<c>max_ticks</c> · <c>picker</c>)가 거기 있다. 전에는 뷰 수치(<c>feel</c>)가 섞여
/// 있다고 뺐다: 화면 흔들림 하나를 고쳐도 지문이 바뀐다. 그 값은 되살리기에서 <c>[W]</c> 일 뿐이고 판이 같으면 그대로 일치다 — 빼면
/// <c>cancel_percent</c> 를 바꾼 판이 결정론 위반(<c>[E]</c>)으로 잘못 읽힌다.
/// </para>
///
/// <para>
/// 파일마다 <c>이름 \0 길이 \0 바이트</c> 를 잇는다 — 바이트만 이으면 한 파일의 끝 바이트를 다음 파일의 머리로 옮겨도 같은 해시다. 파일은 바이트
/// 그대로 읽는다: 줄 끝을 바꿔 체크아웃하는 기기(윈도의 autocrlf)는 다른 지문을 내는데, 그 기기가 만든 판도 바이트가 다른 데이터를 읽은 것이다.
/// </para>
/// </summary>
public static class DataDigest
{
    /// <summary>
    /// 지문에 드는 파일 — 이름 순이다. 판을 세우는 데이터 다섯(<c>BattleTables</c>) · 그 수치(<c>balance.json</c>) · 형태마다의 보스 망 셋(설계 2026-10-01 조각7 §5 —
    /// 망이 바뀐 뒤의 옛 시도는 데이터가 바뀐 것이다).
    /// </summary>
    public static IReadOnlyList<string> Files { get; } =
        ["balance.json", "boss_net/form1.json", "boss_net/form2.json", "boss_net/form3.json", "bosses.json", "fighters.json", "hitboxes.json",
            "patterns.json", "stages.json"];

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
