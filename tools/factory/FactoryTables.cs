using System.Collections.Generic;
using System.IO;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Factory;

/// <summary>
/// 공장이 한 번 읽어 모든 스레드가 같이 쓰는 표 (#108 · 설계 2026-09-28 §4.1). 판을 세우는 데이터 다섯(게임의 <c>BattleTables</c> 와 같다) ·
/// <c>balance.json</c> 이 고르는 캐릭터 · 보스 · 아레나 · 상한 · 계획 고르기의 수치 · 함대 설정. <b>읽기만 한다</b> — 판(<see cref="BattleSim"/>)과 봇이 표를 안 고치므로
/// 스레드마다 사본을 둘 까닭이 없다.
/// </summary>
public sealed class FactoryTables
{
    public required IReadOnlyDictionary<string, StageDef> Stages { get; init; }

    public required IReadOnlyDictionary<string, PatternDef> Patterns { get; init; }

    public required IReadOnlyDictionary<string, HitShape> HitShapes { get; init; }

    public required FighterConfig Fighter { get; init; }

    public required BossConfig Boss { get; init; }

    public required Arena Arena { get; init; }

    public required int MaxTicks { get; init; }

    /// <summary>보스의 쉬는 길이들(틱 · <see cref="BattleSim.RestTicks"/>) — 계획 고르기가 이 중에서 고른다(설계 2026-09-29 조각1 §3.4).</summary>
    public required IReadOnlyList<int> RestTicks { get; init; }

    /// <summary>계획 고르기의 수치 — <c>balance.json</c> 의 <c>picker</c>(설계 2026-09-29 조각1 §3.5). 공장도 게임과 같은 몫으로 끊는다.</summary>
    public required PickerBalance Knobs { get; init; }

    public required FleetConfig Fleet { get; init; }

    /// <summary>리듬형이 따르는 시각표 — 함대 설정의 기준 패턴으로 한 번 짓는다.</summary>
    public required BeatTable Beats { get; init; }

    /// <summary>
    /// <paramref name="dataDir"/>(<c>overfit/data</c>)의 전투 데이터와 <paramref name="fleetPath"/>(<c>tools/factory/fleet.json</c>)를 읽는다. 깨진 파일은
    /// <see cref="DataException"/> 이 어느 파일의 어느 키인지까지 말한다 — 게임의 부팅과 같은 로더다.
    /// </summary>
    public static FactoryTables Load(string dataDir, string fleetPath)
    {
        BalanceData balance = JsonData<BalanceData>.ParseOne(Read(dataDir, "balance.json"), "balance.json");
        Dictionary<string, FighterConfig> fighters = Table<FighterConfig>(dataDir, "fighters.json");
        Dictionary<string, BossConfig> bosses = Table<BossConfig>(dataDir, "bosses.json");
        Dictionary<string, PatternDef> patterns = Table<PatternDef>(dataDir, "patterns.json");
        FleetConfig fleet = JsonData<FleetConfig>.ParseOne(File.ReadAllText(fleetPath), fleetPath);

        if (!fighters.TryGetValue(balance.Battle.Fighter, out FighterConfig? fighter))
        {
            throw new DataException($"fighters.json: balance.json 의 battle.fighter={balance.Battle.Fighter} 가 없다");
        }

        if (!bosses.TryGetValue(balance.Battle.Boss, out BossConfig? boss))
        {
            throw new DataException($"bosses.json: balance.json 의 battle.boss={balance.Battle.Boss} 가 없다");
        }

        return new FactoryTables
        {
            Stages = Table<StageDef>(dataDir, "stages.json"),
            Patterns = patterns,
            HitShapes = HitShapeTable.Parse(Read(dataDir, "hitboxes.json"), "hitboxes.json"),
            Fighter = fighter,
            Boss = boss,
            Arena = new Arena(balance.Battle.ArenaWidth),
            MaxTicks = balance.Battle.MaxTicks,
            RestTicks = BattleSim.RestTicks(boss),
            Knobs = balance.Picker,
            Fleet = fleet,
            Beats = new BeatTable(patterns, fleet.RhythmReferences),
        };
    }

    private static string Read(string dataDir, string name) => File.ReadAllText(Path.Combine(dataDir, name));

    private static Dictionary<string, T> Table<T>(string dataDir, string name) => JsonData<T>.ParseTable(Read(dataDir, name), name);
}
