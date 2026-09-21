using System.Threading.Tasks;
using Godot;
using Overfit.Core;

namespace Overfit.Battle.Debug;

/// <summary>
/// 창을 띄운 채 씬을 돌며 스크린샷을 찍는다. <c>tools/build.sh shots</c> 가 띄운다.
///
/// <para>
/// 전투는 <b>여러 순간</b>을 찍는다 — 대기 중 한 장으로는 이 게임이 무엇인지 안 보인다.
/// 보스가 패턴을 도는 동안(붉게 물든다)과 판정이 선 뒤를 각각 잡는다.
/// </para>
/// </summary>
public partial class ShotRunner : Node
{
    /// <summary>전투에서 찍을 시점(초). 패턴 주기(선딜 0.4~0.8 · 간격 0.8)에 걸치도록 흩어 둔다.</summary>
    private static readonly double[] _battleAt = { 1.2, 2.0, 2.8, 3.6, 5.0, 7.0 };

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        Log.Info("shots", "start");

        await Wait(0.6);
        await Screenshot.CaptureAsync(this, "title");

        Game.Instance.GoTo(Game.Scene.Battle);
        double elapsed = 0;
        for (int i = 0; i < _battleAt.Length; i++)
        {
            await Wait(_battleAt[i] - elapsed);
            elapsed = _battleAt[i];
            await Screenshot.CaptureAsync(this, $"battle-{i + 1}");
        }

        Log.Marker("shots", "shots=done");
        GetTree().Quit();
    }

    private async Task Wait(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }
}
