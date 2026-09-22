using Godot;
using Overfit.Core;

namespace Overfit.Credits;

/// <summary>
/// 크레딧 화면. 씬 전환은 자기가 하지 않고 <c>Game.GoTo</c> 에 맡긴다.
///
/// <para>
/// <b>목록을 여기 적지 않는다.</b> <c>data/credits.json</c> 을 읽어 화면을 짓는다 —
/// 팩 이름·링크·라이선스를 씬에도 문서에도 손으로 적어 두면 팩이 바뀌는 날 한쪽만 고쳐지고,
/// 그 어긋남은 아무 테스트도 빨갛게 하지 않은 채 라이선스 주장만 거짓으로 만든다.
/// 이 저장소는 같은 종류의 어긋남에 여러 번 물렸다(태그와 기하 · 없는 애니메이션 이름 · 49 와 50).
/// </para>
/// </summary>
public partial class Credits : Control
{
    public const string CreditsPath = "res://data/credits.json";

    /// <summary>역할 칸의 너비(px). 이름들이 한 줄에 맞춰 서야 네 항목이 표로 읽힌다.</summary>
    private const int _roleWidth = 200;

    private static readonly Color _dim = new(0.60f, 0.65f, 0.74f);
    private static readonly Color _accent = new(0.43f, 0.91f, 0.72f);
    private static readonly Color _link = new(0.53f, 0.76f, 1.00f);

    public override void _Ready()
    {
        Log.Info("scene", "credits ready");
        GetNode<Button>("%BackButton").Pressed += OnBackPressed;
        Fill(GetNode<VBoxContainer>("%Entries"));
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // 크레딧은 막다른 화면이다. 나가는 길이 버튼 하나뿐이면 패드·키보드로 읽던 사람이 갇힌다.
        if (e.IsActionPressed("ui_cancel"))
        {
            Log.Debug("scene", "credits input=ui_cancel");
            OnBackPressed();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>데이터를 읽어 줄을 짓는다. 못 읽으면 <c>[E]</c> — 헤드리스 판정이 그 한 줄로 실패시킨다.</summary>
    private static void Fill(VBoxContainer box)
    {
        CreditsData data;
        try
        {
            data = JsonData<CreditsData>.ParseOne(Balance.ReadText(CreditsPath), CreditsPath);
        }
        catch (DataException e)
        {
            // 크레딧이 비어도 게임은 돈다 — 그래서 조용히 지나갈 수 있는 자리다.
            // 하지만 출처를 못 읽는 것은 규칙 위반이다. 저장소의 라이선스 주장이 이 파일 위에 서 있다.
            Log.Error("credits", $"fatal {e.Message}");
            return;
        }

        foreach (CreditEntry entry in data.Entries)
        {
            box.AddChild(Row(entry));
        }

        Log.Debug("credits", $"entries={data.Entries.Count} path={CreditsPath}");
    }

    /// <summary>한 팩 = 세 줄. 역할·이름·만든 사람·라이선스 / 링크 / 덧붙임.</summary>
    private static VBoxContainer Row(CreditEntry entry)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 4);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 20);
        Label role = Text(entry.Role, 26, _dim);
        role.CustomMinimumSize = new Vector2(_roleWidth, 0);
        role.HorizontalAlignment = HorizontalAlignment.Right;
        head.AddChild(role);
        head.AddChild(Text(entry.Name, 34, Colors.White));
        head.AddChild(Text($"— {entry.Creator}", 26, _dim));
        head.AddChild(Text(entry.License, 26, _accent));
        row.AddChild(head);

        row.AddChild(Indent(LinkTo(entry.Url)));

        if (!string.IsNullOrWhiteSpace(entry.Note))
        {
            row.AddChild(Indent(Text(entry.Note!, 22, _dim)));
        }

        return row;
    }

    /// <summary>링크. 눌러서 열 수 있어야 크레딧이 쓸모가 있다 — 못 누르는 주소는 옮겨 적어야 한다.</summary>
    private static LinkButton LinkTo(string url)
    {
        var link = new LinkButton
        {
            Text = url,
            Underline = LinkButton.UnderlineMode.Always,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        link.AddThemeFontSizeOverride("font_size", 24);
        link.AddThemeColorOverride("font_color", _link);
        link.AddThemeColorOverride("font_hover_color", _accent);
        link.Pressed += () => Open(url);
        return link;
    }

    private static void Open(string url)
    {
        Log.Info("credits", $"open url={url}");
        Error err = OS.ShellOpen(url);
        if (err != Error.Ok)
        {
            // 브라우저가 안 뜨는 것은 환경 문제지 규칙 위반이 아니다 — [W] 로 남기고 화면은 그대로 둔다.
            Log.Warn("credits", $"open_failed url={url} err={err}");
        }
    }

    /// <summary>역할 칸만큼 밀어 넣는다. 링크와 덧붙임이 이름 아래에 걸려야 어느 팩의 것인지 읽힌다.</summary>
    private static HBoxContainer Indent(Control inner)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 20);
        line.AddChild(new Control { CustomMinimumSize = new Vector2(_roleWidth, 0) });
        line.AddChild(inner);
        return line;
    }

    private static Label Text(string text, int size, Color color)
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static void OnBackPressed()
    {
        Log.Info("scene", "credits action=back");
        Game.Instance.GoTo(Game.Scene.Title);
    }
}
