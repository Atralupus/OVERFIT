using System;
using Godot;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 승패 결과 화면. <b>씬을 자기가 안 바꾼다</b> — 버튼이 눌렸다고 알릴 뿐이고,
/// 어디로 갈지는 <c>Battle</c> 이 <see cref="Game.GoTo"/> 로 정한다 (CLAUDE.md — 전환은 전부 라우터를 지난다).
///
/// <para>
/// 이게 없던 때는 죽어도 게임이 끝나지 않았다. 틱만 멈추고 렌더는 영원히 돌아,
/// 플레이어는 멈춘 화면을 보며 빠져나갈 길이 없었다 — 이긴 경우도 똑같았다.
/// </para>
/// </summary>
public partial class BattleResult : Control
{
    /// <summary>이겼을 때의 제목 색. 진 쪽과 <b>색으로</b> 갈려야 글을 읽기 전에 안다.</summary>
    private static readonly Color _winColor = new(0.43f, 0.91f, 0.72f);

    private static readonly Color _loseColor = new(0.93f, 0.45f, 0.45f);

    private Label _outcome = null!;
    private Label _detail = null!;
    private Button _again = null!;
    private Button _title = null!;

    public override void _Ready()
    {
        _outcome = GetNode<Label>("%Outcome");
        _detail = GetNode<Label>("%Detail");
        _again = GetNode<Button>("%Again");
        _title = GetNode<Button>("%Title");

        // 판이 끝나기 전에는 없는 화면이다. 씬 파일에도 숨겨 두지만 여기서도 못 박는다 —
        // 한쪽만 고치면 "결과가 처음부터 떠 있다" 가 된다.
        Visible = false;
    }

    /// <summary>버튼이 무엇을 할지 정한다. 이 노드는 그 둘을 부르기만 한다.</summary>
    public void Bind(Action onAgain, Action onTitle)
    {
        ArgumentNullException.ThrowIfNull(onAgain);
        ArgumentNullException.ThrowIfNull(onTitle);
        _again.Pressed += onAgain;
        _title.Pressed += onTitle;
    }

    /// <summary>결과를 띄운다. <paramref name="againLabel"/> 은 이겼는지에 따라 달라진다.</summary>
    public void Reveal(bool won, string headline, string detail, string againLabel)
    {
        _outcome.Text = headline;
        _outcome.AddThemeColorOverride("font_color", won ? _winColor : _loseColor);
        _detail.Text = detail;
        _again.Text = againLabel;
        Visible = true;

        // 키보드만으로도 빠져나갈 수 있어야 한다 — 마우스를 안 쓰고 플레이하는 게임이다.
        _again.GrabFocus();
        Log.Info("scene", $"result shown won={won} again=\"{againLabel}\"");
    }
}
