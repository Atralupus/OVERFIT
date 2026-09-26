using System.Collections.Generic;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Battle;

/// <summary>
/// 한 판을 세우는 데이터 다섯 — 캐릭터 · 보스 · 패턴 · 단계 · 판정 모양. <b>게임과 헤드리스 데모가 같은 자리에서 읽는다</b>
/// (#72 · #66 의 넘김).
///
/// <para>
/// 전에는 <c>Battle</c> 과 <c>BattleDemo</c> 가 같은 읽기 도우미(<c>Load&lt;T&gt;</c> · <c>LoadShapes</c>)를 한 벌씩 들고
/// 있었다 — 파일 하나를 더하면 두 곳을 같이 고쳐야 했고, 한쪽만 고치면 데모와 게임이 다른 판을 세운다.
/// 파일을 여는 것은 <see cref="Balance.ReadText"/> 한 자리다(Godot 과 순수 C# 의 경계 · 못 열면 어느 파일인지 말한다).
/// </para>
/// </summary>
public sealed record BattleTables(
    Dictionary<string, FighterConfig> Fighters,
    Dictionary<string, BossConfig> Bosses,
    Dictionary<string, PatternDef> Patterns,
    Dictionary<string, StageDef> Stages,
    Dictionary<string, HitShape> Shapes)
{
    private const string _hitboxes = "res://data/hitboxes.json";

    /// <summary>다섯을 읽는다. 깨진 파일은 <see cref="DataException"/> 이 어느 파일의 어느 키인지까지 말한다.</summary>
    public static BattleTables Load() => new(
        Table<FighterConfig>("res://data/fighters.json"),
        Table<BossConfig>("res://data/bosses.json"),
        Table<PatternDef>("res://data/patterns.json"),
        Table<StageDef>("res://data/stages.json"),
        HitShapeTable.Parse(Balance.ReadText(_hitboxes), _hitboxes));

    private static Dictionary<string, T> Table<T>(string path) =>
        JsonData<T>.ParseTable(Balance.ReadText(path), path);
}
