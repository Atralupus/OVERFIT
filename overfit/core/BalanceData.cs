using System.Text.Json.Serialization;

namespace Overfit.Core;

// data/balance.json 의 DTO. 키 이름은 snake_case 로 자동 변환된다 (BaseAttack → base_attack).
// `required` 가 붙은 키가 빠지면 부팅이 실패하고 빠진 키가 전부 나열된다 (RequiredKeys).
// Godot 을 모르는 순수 C# — 규칙 클래스가 그대로 쓴다.
//
// 콘텐츠 표(캐릭터 · 보스 · 패턴 · 단계)는 여기 없다. 그것들은 id 가 키인 자기 파일을 갖는다 —
// data/fighters.json · data/bosses.json · data/patterns.json · data/stages.json.
// 여기 있는 것은 **어느 콘텐츠의 것도 아닌** 수치다.

/// <summary>
/// 한 판을 <b>세우는</b> 수치. 보스의 것도 캐릭터의 것도 아니라 콘텐츠 표 어디에도 자리가 없다.
///
/// <para>
/// 전에는 <c>new Arena(1920)</c> 이 다섯 곳, <c>MaxTicks = 60 * 180</c> 이 네 곳에 리터럴로 있었다.
/// 흩어진 수치의 대가는 조용하다 — 한 곳만 고치면 게임과 데모와 골든이 서로 다른 전투를 말한다.
/// </para>
/// </summary>
public sealed class BattleBalance
{
    /// <summary>아레나 폭(px). 보스 몸 폭의 배수여야 대시로 빠질 곳이 남는다.</summary>
    public required double ArenaWidth { get; init; }

    /// <summary>한 판의 상한(틱). 고정 60틱이므로 10800 = 180초다. 한 판이 반드시 끝나게 하는 안전장치다.</summary>
    public required int MaxTicks { get; init; }

    /// <summary>
    /// 기본 보스의 id (<c>data/bosses.json</c> 의 키).
    /// 지금은 보스가 하나다 — 단계마다 달라지면 이 키를 <c>stages.json</c> 으로 옮긴다.
    /// </summary>
    public required string Boss { get; init; }
}

/// <summary>모든 수치의 진실 원천. <c>data/balance.json</c> 하나가 이 모양이다.</summary>
public sealed class BalanceData
{
    [JsonPropertyName("_version")]
    public int Version { get; init; }

    public required BattleBalance Battle { get; init; }
}
