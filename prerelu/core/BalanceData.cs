using System.Text.Json.Serialization;

namespace PreReLU.Core;

// data/balance.json 의 DTO. 키 이름은 snake_case 로 자동 변환된다 (BaseAttack → base_attack).
// `required` 가 붙은 키가 빠지면 부팅이 실패하고 빠진 키가 전부 나열된다 (RequiredKeys).
// Godot 을 모르는 순수 C# — 규칙 클래스가 그대로 쓴다.
//
// 아직 게임 수치가 없다. 규칙이 생기면 `public required XxxConfig Xxx { get; init; }` 를 여기 더하고
// balance.json 에 같은 이름의 절을 만든다. **수치를 C# 상수로 쓰지 않는다** — 그 약속의 자리가 여기다.

/// <summary>모든 수치의 진실 원천. <c>data/balance.json</c> 하나가 이 모양이다.</summary>
public sealed class BalanceData
{
    [JsonPropertyName("_version")]
    public int Version { get; init; }
}
