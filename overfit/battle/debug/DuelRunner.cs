using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Battle.Debug;

/// <summary>
/// 학습한 파이터 망과 게임의 보스(형태마다의 망)가 한 판을 끝까지 싸운다 (이슈 #167 — 유저: "최종 강화학습된 유저와 보스의 3페이즈 싸움을 전체를 영상으로").
/// <c>tools/build.sh duel</c> 이 창과 Movie Maker 로 띄우고 영상으로 엮는다. 파이터는 <see cref="FighterNetDriver"/> 가 몬다 — 학습의 일꾼과 같은 운전수 ·
/// 같은 늦춤(보스를 0.2초 늦게 본다)이다. 판이 끝나고 결과 화면을 잠깐 보인 뒤 끝낸다.
/// </summary>
public partial class DuelRunner : Node
{
    /// <summary>판이 끝난 뒤 결과 화면을 보이는 길이(초).</summary>
    private const double _tail = 3.0;

    private readonly SceneDriver _drive;

    public DuelRunner() => _drive = new SceneDriver(this, "duel");

    /// <summary>파이터 망 JSON 의 경로 — <c>--duel=</c>.</summary>
    public string FighterNet { get; set; } = "";

    /// <summary>파이터 운전수의 뽑기 시드.</summary>
    public ulong Seed { get; set; } = 1;

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        await _drive.Frames(1);
        BattleTables data = BattleTables.Load();
        var roster = StageRoster.For(data.Stages, 1);
        PolicyNet net;
        try
        {
            net = PolicyNet.Parse(await File.ReadAllTextAsync(FighterNet), FighterNet, new FighterObservation(roster.Count).Size, FighterActions.Count, roster);
        }
        catch (Exception e) when (e is DataException or IOException)
        {
            Log.Error("duel", $"fighter_net_unreadable path={FighterNet} reason={e.Message}");
            GetTree().Quit(1);
            return;
        }

        var driver = new FighterNetDriver(net, Seed, roster, Balance.Data.Battle.ArenaWidth);
        Overfit.Battle.Battle? battle = await _drive.NewBattle();
        if (battle is null)
        {
            Log.Error("duel", "battle_missing");
            GetTree().Quit(1);
            return;
        }

        battle.Pilot = driver.Next;
        Log.Info("duel", $"start fighter={FighterNet} seed={Seed}");
        while (!battle.Over)
        {
            await _drive.Frames(1);
        }

        Log.Info("duel", $"over ticks={battle.SimTicks}");
        await ToSignal(GetTree().CreateTimer(_tail), SceneTreeTimer.SignalName.Timeout);
        Log.Marker("duel", "duel=done");
        GetTree().Quit();
    }
}
