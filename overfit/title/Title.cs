using Godot;
using Overfit.Core;

namespace Overfit.Title;

/// <summary>타이틀 화면. 씬 전환은 자기가 하지 않고 <see cref="Game.GoTo"/> 에 맡긴다.</summary>
public partial class Title : Control
{
    /// <summary>
    /// 조작 안내 한 줄. 액션 이름은 <c>project.godot</c> 의 <c>[input]</c> 과 같아야 한다.
    ///
    /// <para>
    /// <b>키 글자를 여기 적지 않는다.</b> <see cref="InputMap"/> 에서 뽑는다 —
    /// 손으로 적은 안내는 바인딩이 바뀌는 순간 거짓말이 되고, 그 거짓은 아무 테스트도 빨갛게 하지 않는다.
    /// </para>
    /// </summary>
    private static readonly (string Label, string[] Actions)[] _rows =
    {
        ("이동", new[] { "move_left", "move_right" }),
        ("점프", new[] { "jump" }),
        ("대시", new[] { "dash" }),
        ("패리", new[] { "parry" }),
        ("공격", new[] { "attack" }),
    };

    public override void _Ready()
    {
        Log.Info("scene", "title ready");
        GetNode<Button>("%StartButton").Pressed += OnStartPressed;
        FillControls(GetNode<GridContainer>("%Controls"));
    }

    private static void OnStartPressed()
    {
        Log.Info("scene", "title action=start");
        Game.Instance.GoTo(Game.Scene.Battle);
    }

    /// <summary>InputMap 을 읽어 "이름 → 키" 두 칸짜리 줄들을 채운다.</summary>
    private static void FillControls(GridContainer grid)
    {
        foreach ((string label, string[] actions) in _rows)
        {
            grid.AddChild(Cell(label, HorizontalAlignment.Right, new Color(0.60f, 0.65f, 0.74f)));
            grid.AddChild(Cell(Keys(actions), HorizontalAlignment.Left, Colors.White));
        }

        Log.Debug("scene", $"title controls rows={_rows.Length}");
    }

    /// <summary>액션들의 첫 키 바인딩을 사람이 읽는 글자로. 없는 액션은 조용히 건너뛰지 않고 드러낸다.</summary>
    private static string Keys(string[] actions)
    {
        var parts = new System.Collections.Generic.List<string>();
        foreach (string action in actions)
        {
            if (!InputMap.HasAction(action))
            {
                // 규칙 위반은 아니지만 안내가 비는 것은 알아야 한다 — 액션 이름이 오타면 여기서 드러난다.
                Log.Warn("scene", $"title action_missing name={action}");
                continue;
            }

            foreach (InputEvent e in InputMap.ActionGetEvents(action))
            {
                if (e is InputEventKey key)
                {
                    parts.Add(key.AsTextKeycode());
                    break;
                }
            }
        }

        return parts.Count == 0 ? "?" : string.Join("  ", parts);
    }

    private static Label Cell(string text, HorizontalAlignment align, Color color)
    {
        var label = new Label { Text = text, HorizontalAlignment = align };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }
}
