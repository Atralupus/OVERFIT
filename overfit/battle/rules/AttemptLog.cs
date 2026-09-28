using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 디스크에 남기는 시도 하나 (#112 · 설계 2026-09-28 §6.5) — <c>user://attempts/&lt;세션 시드&gt;.jsonl</c> 의 한 줄이다. 되살리기(데모의
/// <c>--history</c> · <c>--attempt</c>)와 sim-to-real(5번 PR)의 재료다.
/// </summary>
/// <param name="SessionSeed">세션 시드 — 파일 이름이기도 하다.</param>
/// <param name="Run">런 번호(<see cref="RunHistory.Run"/>) — 같은 런의 앞 기록을 찾는 열쇠다.</param>
/// <param name="Record">시도 — 번호 · 실제로 싸운 단계 · 시드 · 결과 · 관측 전부 · 갈래.</param>
/// <param name="PickerId">단계의 고르기 id(동전이 무작위 갈래를 골라도 <c>network</c>).</param>
/// <param name="Drawn">그 판에서 뽑힌 패턴 id 의 순서(<see cref="BattleSim.Drawn"/>).</param>
/// <param name="Ticks">판의 길이.</param>
/// <param name="Decision">망 갈래면 망 고르기의 결정.</param>
/// <param name="NetworkSha256">그때의 <c>network.json</c> 의 sha256 — 되살릴 때 지금과 다르면 <c>[W]</c>(다른 망이라 다른 명부가 설 수 있다).</param>
public sealed record AttemptEntry(
    ulong SessionSeed, int Run, AttemptRecord Record, string PickerId, IReadOnlyList<string> Drawn, int Ticks, PickDecision? Decision,
    string? NetworkSha256);

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
            Arm = r.Arm,
            Drawn = entry.Drawn,
            Outcome = r.Outcome,
            Ticks = entry.Ticks,
            Events = r.Events,
            Decision = entry.Decision,
            NetworkSha256 = entry.NetworkSha256,
        };
        return JsonSerializer.Serialize(line, _options);
    }

    /// <summary>한 줄을 읽는다. 깨졌거나 필수 키가 빠지면 <see cref="DataException"/> — <paramref name="source"/>(파일:줄)를 싣는다.</summary>
    public static AttemptEntry Parse(string line, string source)
    {
        AttemptLine l = JsonData<AttemptLine>.ParseOne(line, source);
        var record = new AttemptRecord(l.Attempt, l.Stage, l.Seed, l.Outcome, l.Events, l.Arm);
        return new AttemptEntry(l.SessionSeed, l.Run, record, l.Picker, l.Drawn, l.Ticks, l.Decision, l.NetworkSha256);
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

        public string? Arm { get; init; }

        public required IReadOnlyList<string> Drawn { get; init; }

        public required BattleOutcome Outcome { get; init; }

        public required int Ticks { get; init; }

        public required IReadOnlyList<DodgeEvent> Events { get; init; }

        public PickDecision? Decision { get; init; }

        public string? NetworkSha256 { get; init; }
    }
}
