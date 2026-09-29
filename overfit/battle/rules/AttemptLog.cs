using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 디스크에 남기는 시도 하나 (#112 · 설계 2026-09-28 §6.5) — <c>user://attempts/&lt;세션 시드&gt;.jsonl</c> 의 한 줄이다. 되살리기(데모의
/// <c>--history</c> · <c>--attempt</c>)의 재료다.
///
/// <para>
/// 옛 망이 쓰던 칸(갈래 <c>arm</c> · 망의 결정 <c>decision</c> · <c>network_sha256</c> · 입력 19칸 <c>features</c>)은 조각 1 에서 걷었다(설계 2026-09-29
/// 조각1 §6). 그 칸이 든 옛 줄도 읽힌다 — <c>JsonData</c> 가 모르는 키를 버린다.
/// </para>
/// </summary>
/// <param name="SessionSeed">세션 시드 — 파일 이름이기도 하다.</param>
/// <param name="Run">런 번호(<see cref="RunHistory.Run"/>) — 같은 런의 앞 기록을 찾는 열쇠다.</param>
/// <param name="Record">시도 — 번호 · 실제로 싸운 단계 · 시드 · 결과 · 관측 전부.</param>
/// <param name="PickerId">그 판을 세운 고르기 id(대본이면 <c>script</c>).</param>
/// <param name="Drawn">그 판에서 선 동작 id 의 순서(<see cref="BattleSim.Drawn"/>) — 캔슬로 이은 동작도 든다. 6/8 에서 고른 계획(<c>plans</c>)이 대신한다.</param>
/// <param name="Ticks">판의 길이.</param>
/// <param name="Instances">
/// 그 판의 사례와 라벨(<see cref="InstanceTracker"/> — 공장과 같은 정의 · 끝나지 않은 사례는 빠진다) (#114). 관측만으로는 같은 패턴이 연달아 선 사례의
/// 경계를 모른다. #113 의 줄에는 없다(null).
/// </param>
public sealed record AttemptEntry(
    ulong SessionSeed, int Run, AttemptRecord Record, string PickerId, IReadOnlyList<string> Drawn, int Ticks,
    IReadOnlyList<PatternInstance>? Instances = null);

/// <summary>
/// 시도 기록의 한 줄을 짓고 읽는다 (#112 · 설계 2026-09-28 §6.5). <b>순수</b>하다 — 파일에 쓰는 것만 Godot 쪽(<c>AttemptFile</c>)이다. 키는 다른 데이터와
/// 같은 snake_case · 열거는 소문자 이름 · 한글 id 는 이스케이프 없이 그대로다(사람이 grep 한다). double 은 가장 짧은 왕복 표기라 되읽으면 비트까지 같다.
/// </summary>
public static class AttemptLog
{
    private static readonly JsonSerializerOptions _options = new(JsonData.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>한 줄(줄바꿈 없음).</summary>
    public static string Line(AttemptEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        AttemptRecord r = entry.Record;
        var line = new AttemptLine
        {
            SessionSeed = entry.SessionSeed,
            Run = entry.Run,
            Attempt = r.Number,
            Stage = r.Stage,
            Seed = r.Seed,
            Picker = entry.PickerId,
            Drawn = entry.Drawn,
            Outcome = r.Outcome,
            Ticks = entry.Ticks,
            Events = r.Events,
            Instances = entry.Instances,
        };
        return JsonSerializer.Serialize(line, _options);
    }

    /// <summary>한 줄을 읽는다. 깨졌거나 필수 키가 빠지면 <see cref="DataException"/> — <paramref name="source"/>(파일:줄)를 싣는다.</summary>
    public static AttemptEntry Parse(string line, string source)
    {
        AttemptLine l = JsonData<AttemptLine>.ParseOne(line, source);
        var record = new AttemptRecord(l.Attempt, l.Stage, l.Seed, l.Outcome, l.Events);
        return new AttemptEntry(l.SessionSeed, l.Run, record, l.Picker, l.Drawn, l.Ticks, l.Instances);
    }

    /// <summary>줄의 모양 — 필수 키가 빠지면 <c>JsonData</c> 가 전부 나열한다.</summary>
    internal sealed class AttemptLine
    {
        public required ulong SessionSeed { get; init; }

        public required int Run { get; init; }

        public required int Attempt { get; init; }

        public required int Stage { get; init; }

        public required ulong Seed { get; init; }

        public required string Picker { get; init; }

        public required IReadOnlyList<string> Drawn { get; init; }

        public required BattleOutcome Outcome { get; init; }

        public required int Ticks { get; init; }

        public required IReadOnlyList<DodgeEvent> Events { get; init; }

        public IReadOnlyList<PatternInstance>? Instances { get; init; }
    }
}
