using System.Collections.Generic;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Battle;

/// <summary>
/// 한 판을 세우는 데이터 다섯 — 캐릭터 · 보스 · 패턴 · 단계 · 판정 모양 — 과 그 지문. <b>게임과 헤드리스 데모가 같은 자리에서 읽는다</b>
/// (#72 · #66 의 넘김).
///
/// <para>
/// 전에는 <c>Battle</c> 과 <c>BattleDemo</c> 가 같은 읽기 도우미(<c>Load&lt;T&gt;</c> · <c>LoadShapes</c>)를 한 벌씩 들고
/// 있었다 — 파일 하나를 더하면 두 곳을 같이 고쳐야 했고, 한쪽만 고치면 데모와 게임이 다른 판을 세운다.
/// 파일을 여는 것은 <see cref="Balance.ReadText"/> 한 자리다(Godot 과 순수 C# 의 경계 · 못 열면 어느 파일인지 말한다).
/// </para>
///
/// <para>
/// <c>DataSha256</c> 은 판을 세우는 데이터의 지문이다(<see cref="DataDigest"/> — 다섯과 <c>balance.json</c>). 시도 기록 한 줄이 싣고 되살리기가
/// 지금의 값과 대 본다(설계 2026-09-29 조각1 §4.3 · §4.4) — 같은 자리에서 읽어야 기록의 지문이 그 판을 세운 바로 그 바이트의 것이다.
/// </para>
/// </summary>
public sealed record BattleTables(
    Dictionary<string, FighterConfig> Fighters,
    Dictionary<string, BossConfig> Bosses,
    Dictionary<string, PatternDef> Patterns,
    Dictionary<string, StageDef> Stages,
    Dictionary<string, HitShape> Shapes,
    Dictionary<string, string> Nets,
    string DataSha256)
{
    private const string _hitboxes = "res://data/hitboxes.json";

    /// <summary>
    /// 다섯과 보스들의 망(<see cref="FormsDef.Nets"/> 의 경로 → JSON)을 읽고 지문을 낸다. 깨진 파일은 <see cref="DataException"/> 이 어느 파일의 어느 키인지까지
    /// 말한다. 망은 글로만 들고 판마다 그 판의 명부로 읽는다(<see cref="TryController"/>) — 명부가 망의 명부와 대 봐야 하는 것은 판을 세울 때다.
    /// </summary>
    public static BattleTables Load()
    {
        Dictionary<string, BossConfig> bosses = Table<BossConfig>("res://data/bosses.json");
        var nets = new Dictionary<string, string>();
        foreach (BossConfig boss in bosses.Values)
        {
            foreach (string net in boss.Forms.Nets ?? [])
            {
                nets.TryAdd(net, Balance.ReadText($"res://data/{net}"));
            }
        }

        return new(
            Table<FighterConfig>("res://data/fighters.json"),
            bosses,
            Table<PatternDef>("res://data/patterns.json"),
            Table<StageDef>("res://data/stages.json"),
            HitShapeTable.Parse(Balance.ReadText(_hitboxes), _hitboxes),
            nets,
            DataDigest.Of(name => Balance.ReadBytes($"res://data/{name}")));
    }

    /// <summary>
    /// 단계의 조종기를 세운다 (설계 2026-10-01 조각7 §2) — <c>rule</c> 이면 null(판이 <see cref="StageSetup.Picker"/> 로 규칙 조종기를 세운다) · <c>net</c> 이면
    /// 형태마다의 망. <b>게임과 데모가 이 한 자리에서 세운다</b> — 되살리기가 같은 시드로 같은 조종기에 서야 같은 판이다(§5). 망이 없거나 판과 안 맞으면
    /// <c>[E]</c> 를 남기고 거짓이다 — 던지면 <c>_Ready</c> 를 빠져나가 반쯤 선 노드를 남긴다(<see cref="StageRoster.Setup"/> 과 같은 까닭).
    /// </summary>
    public bool TryController(StageSetup setup, BossConfig boss, ulong seed, out IBossController? controller)
    {
        System.ArgumentNullException.ThrowIfNull(setup);
        System.ArgumentNullException.ThrowIfNull(boss);
        controller = null;
        if (setup.ControllerId != StageRoster.NetController)
        {
            return true;
        }

        IReadOnlyList<string> paths = boss.Forms.Nets ?? [];
        if (paths.Count != boss.Forms.Thresholds.Count + 1)
        {
            Log.Error("battle", $"net_forms_mismatch nets={paths.Count} forms={boss.Forms.Thresholds.Count + 1}");
            return false;
        }

        try
        {
            controller = BossNets.Create([.. System.Linq.Enumerable.Select(paths, p => Nets[p])], paths, setup.PatternIds, seed);
        }
        catch (DataException e)
        {
            Log.Error("battle", $"net_rejected reason={e.Message}");
            return false;
        }

        Log.Info("battle", () => $"controller=net forms={string.Join(',', paths)}");
        return true;
    }

    private static Dictionary<string, T> Table<T>(string path) =>
        JsonData<T>.ParseTable(Balance.ReadText(path), path);
}
