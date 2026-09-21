using System.Threading.Tasks;
using Godot;

namespace Overfit.Core;

/// <summary>
/// 화면을 파일로 남긴다. <b>OS 캡처가 아니라 뷰포트에서 직접 가져온다</b> —
/// 화면 기록 권한이 필요 없고, 다른 창이 겹치지 않으며, 창 위치가 어디든 프레이밍이 같다.
///
/// <para>
/// ⚠ 창이 있어야 한다. 헤드리스에서는 뷰포트에 그려진 것이 없어 빈 이미지가 나온다 —
/// 그래서 <c>tools/build.sh shots</c> 는 창을 띄워 돌린다.
/// </para>
/// </summary>
public static class Screenshot
{
    /// <summary>저장 폴더. <c>--shot-dir=</c> 로 받고, 없으면 유저 데이터 폴더.</summary>
    public static string Dir => CmdArgs.Text(OS.GetCmdlineUserArgs(), "--shot-dir=") ?? "user://shots";

    /// <summary>한 장 찍는다. 그리기가 끝난 프레임을 잡으려고 ProcessFrame 을 한 번 기다린다.</summary>
    public static async Task CaptureAsync(Node node, string name)
    {
        await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
        await RenderingServer.Singleton.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePreDraw);
        await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);

        using Image image = node.GetViewport().GetTexture().GetImage();
        string dir = Dir;
        if (DirAccess.MakeDirRecursiveAbsolute(dir) is Error mk && mk != Error.Ok && mk != Error.AlreadyExists)
        {
            Log.Error("shot", $"mkdir_failed dir={dir} err={mk}");
            return;
        }

        string path = $"{dir}/{name}.png";
        Error err = image.SavePng(path);
        if (err != Error.Ok)
        {
            Log.Error("shot", $"save_failed path={path} err={err}");
            return;
        }

        Log.Info("shot", $"saved name={name} size={image.GetWidth()}x{image.GetHeight()} path={path}");
    }
}
