using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Overfit.Factory;

/// <summary>CSV 파일 하나 — 머리를 쓰고, 묶음마다 이어 쓰며 sha256 과 줄 수를 같이 센다(다 쓴 뒤 다시 읽지 않는다).</summary>
internal sealed class CsvFile : IDisposable
{
    private readonly FileStream _stream;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private string? _sha256;

    public CsvFile(string path, string header)
    {
        _stream = File.Create(path);
        Append(Encoding.UTF8.GetBytes(header + "\n"));
    }

    /// <summary>머리를 뺀 줄 수.</summary>
    public long Rows { get; private set; }

    /// <summary>파일 전체(머리 포함)의 sha256 — 처음 읽을 때 닫는다.</summary>
    public string Sha256 => _sha256 ??= Program.Hex(_hash.GetHashAndReset());

    public void Write(StringBuilder text, int rows)
    {
        Append(Encoding.UTF8.GetBytes(text.ToString()));
        Rows += rows;
    }

    public void Dispose()
    {
        _stream.Dispose();
        _hash.Dispose();
    }

    private void Append(byte[] bytes)
    {
        _hash.AppendData(bytes);
        _stream.Write(bytes);
    }
}
