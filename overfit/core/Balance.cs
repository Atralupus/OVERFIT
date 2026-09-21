using Godot;

namespace Overfit.Core;

/// <summary>
/// 데이터 로더 Autoload. <c>project.godot</c> 의 <c>[autoload]</c> 에서 <see cref="Game"/> 보다 앞에 등록된다 —
/// 뒤의 Autoload 가 부팅 때 바로 쓸 수 있게.
///
/// <para>
/// 여기가 Godot 과 순수 C# 의 경계다. 파일은 <c>Godot.FileAccess</c> 로 읽어 문자열로 만들고
/// (Android 의 <c>res://</c> 는 APK 안이라 <c>System.IO</c> 로 못 읽는다), 파싱·검증은 Godot 을 모르는
/// <see cref="JsonData{T}"/> 가 한다.
/// </para>
///
/// 데이터가 깨졌으면 <c>[data][E] fatal</c> 을 찍고 종료 코드 1 로 멈춘다 —
/// 헤드리스 판정이 <c>[E]</c> 를 실패로 보므로 조용히 지나가지 않는다.
/// </summary>
public partial class Balance : Node
{
    public const string BalancePath = "res://data/balance.json";

    private BalanceData? _data;

    public static Balance Instance { get; private set; } = null!;

    /// <summary>모든 수치의 진실 원천. 부팅에 성공한 뒤에만 유효하다.</summary>
    public static BalanceData Data => Instance._data
        ?? throw new System.InvalidOperationException("balance.json 이 로드되지 않았다");

    public bool Loaded { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        LogSink.Install();

        try
        {
            _data = JsonData<BalanceData>.ParseOne(ReadText(BalancePath), BalancePath);
            Loaded = true;
            Log.Info("data", $"loaded path={BalancePath} version={_data.Version}");
        }
        catch (DataException e)
        {
            Log.Error("data", $"fatal {e.Message}");
            // 이 프레임에 멈춘다. 깨진 데이터로 계속 가면 뒤에서 터지고, 그 스택은 원인을 안 가리킨다.
            GetTree().Quit(1);
        }
    }

    /// <summary><c>res://</c> 파일을 문자열로. 못 읽으면 <see cref="DataException"/>.</summary>
    private static string ReadText(string path)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            throw new DataException($"{path}: 파일을 열 수 없다 — {FileAccess.GetOpenError()}");
        }

        return file.GetAsText();
    }
}
