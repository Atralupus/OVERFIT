using Godot;
using Overfit.Core;

namespace Overfit.Title;

/// <summary>타이틀 화면. 씬 전환은 자기가 하지 않고 <see cref="Game.GoTo"/> 에 맡긴다.</summary>
public partial class Title : Control
{
    public override void _Ready()
    {
        Log.Info("scene", "title ready");
        GetNode<Button>("%StartButton").Pressed += OnStartPressed;
    }

    private static void OnStartPressed()
    {
        Log.Info("scene", "title action=start");
        Game.Instance.GoTo(Game.Scene.Play);
    }
}
