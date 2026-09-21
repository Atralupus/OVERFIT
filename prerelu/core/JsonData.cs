using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PreReLU.Core;

/// <summary>
/// 데이터 파일이 잘못됐을 때. 부팅을 멈춘다.
/// 메시지에 잘못된 키·id 를 <b>전부</b> 담는다 — 한 번에 하나씩 고치게 하지 않기 위해서다.
/// </summary>
public sealed class DataException : Exception
{
    public DataException(string message) : base(message)
    {
    }
}

/// <summary>
/// JSON 문자열 → DTO 공통 로더. Godot 을 모른다.
/// 파일은 뷰 층(<c>Balance</c>)이 <c>Godot.FileAccess</c> 로 읽어 문자열로 넘긴다 —
/// Android 의 <c>res://</c> 는 APK 안이라 <c>System.IO</c> 로는 못 읽는다.
/// </summary>
public static class JsonData
{
    /// <summary>
    /// C# PascalCase 프로퍼티 ↔ JSON snake_case 키. 딕셔너리 키(한글 id)는 그대로 둔다.
    /// enum 도 snake_case 문자열이다 (<c>AttackSpeed</c> ↔ <c>"attack_speed"</c>) — 숫자는 받지 않는다.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    /// <summary><c>_</c> 로 시작하는 키는 사람을 위한 주석(<c>_comment</c> <c>_units</c> …)이다. 데이터로 세지 않는다.</summary>
    public static bool IsMetaKey(string key) => key.StartsWith('_');

    /// <summary>루트 객체의 데이터 키 목록. 부팅 로그용.</summary>
    public static List<string> TopLevelKeys(string json, string source)
    {
        using JsonDocument doc = ParseDocument(json, source);
        var keys = new List<string>();
        foreach (JsonProperty p in doc.RootElement.EnumerateObject())
        {
            if (!IsMetaKey(p.Name))
            {
                keys.Add(p.Name);
            }
        }

        return keys;
    }

    internal static JsonDocument ParseDocument(string json, string source)
    {
        try
        {
            JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            // ValueKind 를 Dispose 전에 붙잡아 둔다 — dispose 된 doc 을 읽으면 ObjectDisposedException 이 새고,
            // 그러면 손상 갈래를 타려던 쪽(catch (DataException))이 통째로 지나쳐 부팅이 죽는다 — 실제로 밟은 버그다.
            JsonValueKind kind = doc.RootElement.ValueKind;
            if (kind != JsonValueKind.Object)
            {
                doc.Dispose();
                throw new DataException($"{source}: 루트가 객체가 아니다 ({kind})");
            }

            return doc;
        }
        catch (JsonException e)
        {
            throw new DataException($"{source}: JSON 문법 오류 line={e.LineNumber} pos={e.BytePositionInLine} — {e.Message}");
        }
    }

    internal static T Deserialize<T>(JsonElement element, string where)
    {
        try
        {
            return element.Deserialize<T>(Options)
                ?? throw new DataException($"{where}: null");
        }
        catch (JsonException e)
        {
            throw new DataException($"{where}: 값 형식 오류 path={e.Path} — {e.Message}");
        }
    }
}

/// <summary>
/// 파일 하나를 DTO 로. 두 가지 모양을 지원한다.
/// <list type="bullet">
/// <item><see cref="ParseOne"/> — 파일 전체가 DTO 하나 (balance.json)</item>
/// <item><see cref="ParseTable"/> — 루트 객체의 키가 id, 값이 DTO (콘텐츠 표). 한글 id 는 string 그대로</item>
/// </list>
/// 두 모양 모두 <c>required</c> 프로퍼티가 빠진 키를 <b>전부</b> 모아 <see cref="DataException"/> 을 던진다.
/// </summary>
public static class JsonData<T>
{
    public static T ParseOne(string json, string source)
    {
        using JsonDocument doc = JsonData.ParseDocument(json, source);
        List<string> missing = RequiredKeys.Missing(typeof(T), doc.RootElement, "");
        if (missing.Count > 0)
        {
            throw new DataException($"{source}: 필수 키 누락 {missing.Count}개 — {string.Join(", ", missing)}");
        }

        return JsonData.Deserialize<T>(doc.RootElement, source);
    }

    public static Dictionary<string, T> ParseTable(string json, string source)
    {
        using JsonDocument doc = JsonData.ParseDocument(json, source);
        var table = new Dictionary<string, T>();
        var missing = new List<string>();
        var duplicates = new List<string>();
        var notObjects = new List<string>();

        foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
        {
            string id = entry.Name;
            if (JsonData.IsMetaKey(id))
            {
                continue;
            }

            if (table.ContainsKey(id))
            {
                duplicates.Add(id);
                continue;
            }

            if (entry.Value.ValueKind != JsonValueKind.Object)
            {
                notObjects.Add(id);
                continue;
            }

            int before = missing.Count;
            missing.AddRange(RequiredKeys.Missing(typeof(T), entry.Value, id));
            if (missing.Count == before)
            {
                table[id] = JsonData.Deserialize<T>(entry.Value, $"{source}:{id}");
            }
        }

        var problems = new List<string>();
        if (missing.Count > 0)
        {
            problems.Add($"필수 키 누락 {missing.Count}개 — {string.Join(", ", missing)}");
        }

        if (duplicates.Count > 0)
        {
            problems.Add($"중복 id — {string.Join(", ", duplicates)}");
        }

        if (notObjects.Count > 0)
        {
            problems.Add($"객체가 아닌 항목 — {string.Join(", ", notObjects)}");
        }

        if (problems.Count > 0)
        {
            throw new DataException($"{source}: {string.Join(" / ", problems)}");
        }

        return table;
    }
}

/// <summary>
/// DTO 의 <c>required</c> 프로퍼티를 기준으로 JSON 에 빠진 키를 찾는다.
/// 첫 번째에서 멈추는 System.Text.Json 과 달리 <b>전부</b> 모은다. 중첩 객체 · 딕셔너리 값 · 리스트 원소까지 내려간다.
/// 진실 원천은 DTO 하나다 — 필수 키 목록을 따로 적지 않는다.
/// </summary>
public static class RequiredKeys
{
    public static List<string> Missing(Type type, JsonElement element, string path)
    {
        var missing = new List<string>();
        Descend(type, element, path, missing);
        return missing;
    }

    public static string JsonName(PropertyInfo prop)
    {
        JsonPropertyNameAttribute? explicitName = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
        return explicitName?.Name ?? JsonData.Options.PropertyNamingPolicy!.ConvertName(prop.Name);
    }

    private static void CollectObject(Type type, JsonElement element, string path, List<string> missing)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return; // 형식 불일치는 역직렬화가 path 와 함께 잡는다
        }

        foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null || prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            string name = JsonName(prop);
            string childPath = path.Length == 0 ? name : $"{path}.{name}";
            if (!element.TryGetProperty(name, out JsonElement child))
            {
                if (prop.GetCustomAttribute<RequiredMemberAttribute>() != null)
                {
                    missing.Add(childPath);
                }

                continue;
            }

            Descend(prop.PropertyType, child, childPath, missing);
        }
    }

    private static void Descend(Type type, JsonElement element, string path, List<string> missing)
    {
        if (IsLeaf(type))
        {
            return;
        }

        if (type.IsArray)
        {
            DescendArray(type.GetElementType()!, element, path, missing);
            return;
        }

        if (type.IsGenericType)
        {
            Type def = type.GetGenericTypeDefinition();
            Type[] args = type.GetGenericArguments();
            if (def == typeof(Dictionary<,>) || def == typeof(IReadOnlyDictionary<,>) || def == typeof(IDictionary<,>))
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty p in element.EnumerateObject())
                    {
                        if (!JsonData.IsMetaKey(p.Name))
                        {
                            Descend(args[1], p.Value, $"{path}.{p.Name}", missing);
                        }
                    }
                }

                return;
            }

            if (args.Length == 1 && typeof(IEnumerable).IsAssignableFrom(type))
            {
                DescendArray(args[0], element, path, missing);
                return;
            }
        }

        CollectObject(type, element, path, missing);
    }

    private static void DescendArray(Type elementType, JsonElement element, string path, List<string> missing)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        int i = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            Descend(elementType, item, $"{path}[{i}]", missing);
            i++;
        }
    }

    private static bool IsLeaf(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(JsonElement);
    }
}
