using System.Collections.Generic;
using Godot;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Battle;

/// <summary>
/// 시도 기록을 디스크에 덧붙이고 읽는다 (#112 · 설계 2026-09-28 §6.5) — <c>user://attempts/&lt;세션 시드&gt;.jsonl</c>, 한 줄이 시도 하나다. 줄을 짓고 읽는 것은
/// 순수한 <see cref="AttemptLog"/> 이고 여기는 파일만 다룬다(Godot 과 순수 C# 의 경계 — <see cref="Balance.ReadText"/> 와 같은 가름).
///
/// <para>
/// 자동으로 어디에도 안 보낸다 — 되살리기(<c>tools/build.sh demo</c> 의 <c>--history</c>)가 사람이 건넨 파일을 읽는다.
/// 쓰기에 실패하면 <c>[W]</c> 다 — 기록이 빠질 뿐 규칙은 어기지 않았다.
/// </para>
/// </summary>
public static class AttemptFile
{
    public const string Dir = "user://attempts";

    /// <summary>세션의 파일 — 세션 시드가 이름이다.</summary>
    public static string PathFor(ulong sessionSeed) => $"{Dir}/{sessionSeed}.jsonl";

    /// <summary>한 줄을 세션의 파일(<see cref="PathFor"/>)에 덧붙인다. 쓴 파일의 경로(실패하면 null).</summary>
    public static string? Append(AttemptEntry entry)
    {
        System.ArgumentNullException.ThrowIfNull(entry);
        return AppendTo(PathFor(entry.SessionSeed), entry);
    }

    /// <summary>
    /// 한 줄을 <paramref name="path"/> 에 덧붙인다 — <c>user://</c> 든 디스크의 절대 경로든. 데모의 <c>--record</c> 가 봇의 판을 사람이 고른 파일에
    /// 남긴다(되살리기를 게임 없이 확인하는 길). 쓴 파일의 경로(실패하면 null).
    /// </summary>
    public static string? AppendTo(string path, AttemptEntry entry)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        System.ArgumentNullException.ThrowIfNull(entry);
        Error made = DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path.GetBaseDir()));
        if (made is not (Error.Ok or Error.AlreadyExists))
        {
            Log.Warn("run", $"log_failed path={path} err={made}");
            return null;
        }

        using FileAccess? file = FileAccess.FileExists(path)
            ? FileAccess.Open(path, FileAccess.ModeFlags.ReadWrite)
            : FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            Log.Warn("run", $"log_failed path={path} err={FileAccess.GetOpenError()}");
            return null;
        }

        file.SeekEnd();
        file.StoreString(AttemptLog.Line(entry) + "\n");
        return path;
    }

    /// <summary>파일의 줄 전부 — 빈 줄은 건너뛴다. 깨진 줄은 <see cref="DataException"/>(파일:줄).</summary>
    public static List<AttemptEntry> Read(string path)
    {
        string text = Balance.ReadText(path);
        var entries = new List<AttemptEntry>();
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length > 0)
            {
                entries.Add(AttemptLog.Parse(lines[i], $"{path}:{i + 1}"));
            }
        }

        return entries;
    }
}
