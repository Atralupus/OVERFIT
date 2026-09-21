using Godot;
using Overfit.Core;

namespace Overfit.Play;

/// <summary>
/// 플레이 화면 자리. 아직 게임이 없다 — 라우팅과 검증 루프가 실제로 도는지 보기 위한 최소 씬이다.
/// 규칙이 생기면 <b>그 규칙은 여기 들어오지 않는다.</b> Godot 을 모르는 순수 C# 로 따로 서고,
/// 이 씬은 그 결과를 그리기만 한다 (CLAUDE.md — 규칙과 그림을 나눈다).
/// </summary>
public partial class Play : Control
{
    public override void _Ready()
    {
        Log.Info("scene", "play ready");
        GetNode<Button>("%BackButton").Pressed += OnBackPressed;
    }

    private static void OnBackPressed()
    {
        Log.Info("scene", "play action=back");
        Game.Instance.GoTo(Game.Scene.Title);
    }
}
