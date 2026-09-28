using System;
using System.Security.Cryptography;
using Godot;
using Overfit.Battle.Rules;

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

    /// <summary>2단계의 고르기가 읽는 망 (#112 · 설계 2026-09-28 §6.1) — 필수 데이터다.</summary>
    public const string NetworkPath = "res://data/network.json";

    private BalanceData? _data;
    private PlayerNet? _net;

    public static Balance Instance { get; private set; } = null!;

    /// <summary>모든 수치의 진실 원천. 부팅에 성공한 뒤에만 유효하다.</summary>
    public static BalanceData Data => Instance._data
        ?? throw new System.InvalidOperationException("balance.json 이 로드되지 않았다");

    /// <summary>망 — 부팅에 성공한 뒤에만 유효하다.</summary>
    public static PlayerNet Net => Instance._net
        ?? throw new System.InvalidOperationException("network.json 이 로드되지 않았다");

    /// <summary>망과 고르기의 수치 — 판을 세우는 자리(<c>StageRoster.Setup</c>)에 넘긴다.</summary>
    public static NetworkContext Network => new(Net, Data.Picker);

    /// <summary><c>network.json</c> 의 sha256 — 시도 기록이 싣는다. 되살릴 때 지금과 다르면 다른 망이다.</summary>
    public static string NetworkSha256 { get; private set; } = "";

    public bool Loaded { get; private set; }

    public override void _Ready()
    {
        Instance = this;
        LogSink.Install();

        try
        {
            _data = JsonData<BalanceData>.ParseOne(ReadText(BalancePath), BalancePath);
            Log.Info("data", $"loaded path={BalancePath} version={_data.Version}");
            _net = LoadNetwork();
            Loaded = _net is not null;
        }
        catch (DataException e)
        {
            Log.Error("data", $"fatal {e.Message}");
        }

        if (!Loaded)
        {
            // 이 프레임에 멈춘다. 깨진 데이터로 계속 가면 뒤에서 터지고, 그 스택은 원인을 안 가리킨다. 모양이 틀린 망은 PlayerNet 이 [net][E] 를 남겼다.
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// 망을 읽는다 (#112 · 설계 2026-09-28 §6.1 · §5.6). 필수 키가 빠지면 <see cref="DataException"/>, 모양이 틀리면 null(<c>[net][E]</c>). 망이 배운
    /// 게임의 지문(<c>trained_on.data_digest</c>)이 지금의 <see cref="DataDigest"/> 와 다르면 <c>[W]</c> — 이상하지만 계속 간다: 실패로 두면 수치 하나를
    /// 고칠 때마다 몇 시간의 공장이 막는다. 규칙 코드의 변화는 리플레이 골든이 잡는다.
    /// </summary>
    private static PlayerNet? LoadNetwork()
    {
        byte[] bytes = FileAccess.GetFileAsBytes(NetworkPath);
        if (bytes.Length == 0)
        {
            throw new DataException($"{NetworkPath}: 파일을 열 수 없다 — {FileAccess.GetOpenError()}");
        }

        PlayerNet? net = PlayerNet.Load(System.Text.Encoding.UTF8.GetString(bytes), NetworkPath);
        if (net is null)
        {
            return null;
        }

        NetworkSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Log.Info("net", $"loaded heads={net.Heads.Count} features={PlayerFeatures.Names.Count} trained_on={net.TrainedOn.Commit}");
        string now = DataDigest.Of(name => FileAccess.GetFileAsBytes($"res://data/{name}"));
        if (now != net.TrainedOn.DataDigest)
        {
            Log.Warn("net", $"stale data_digest={now} trained_on={net.TrainedOn.DataDigest}");
        }

        return net;
    }

    /// <summary>
    /// <c>res://</c> 파일을 문자열로. 못 읽으면 <see cref="DataException"/>.
    ///
    /// <para>
    /// <b>public 인 이유:</b> 여기가 "Godot 이 파일을 읽고, Godot 을 모르는 <see cref="JsonData{T}"/> 가 파싱한다" 는
    /// 경계 그 자체다. 데이터 파일을 읽는 다른 화면(크레딧)이 같은 네 줄을 다시 쓰면
    /// Android 의 <c>res://</c> 처럼 플랫폼 사정이 바뀔 때 고칠 곳이 둘이 된다.
    /// </para>
    /// </summary>
    public static string ReadText(string path)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            throw new DataException($"{path}: 파일을 열 수 없다 — {FileAccess.GetOpenError()}");
        }

        return file.GetAsText();
    }
}
