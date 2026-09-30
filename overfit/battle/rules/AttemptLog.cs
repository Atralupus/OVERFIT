using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 디스크에 남기는 시도 하나 (#112 · 설계 2026-09-28 §6.5 · 2026-09-29 조각1 §4.3) — <c>user://attempts/&lt;세션 시드&gt;.jsonl</c> 의 한 줄이다.
/// 되살리기(데모의 <c>--history</c> · <c>--attempt</c> · <see cref="Replay"/>)의 재료다 — 입력이 있으면 판 전체가 다시 선다.
///
/// <para>
/// 옛 망이 쓰던 칸(갈래 <c>arm</c> · 망의 결정 <c>decision</c> · <c>network_sha256</c> · 입력 19칸 <c>features</c>)은 조각 1 에서 걷었다(설계 2026-09-29
/// 조각1 §6). 선 동작의 순서(<c>drawn</c>)는 고른 계획(<c>plans</c>)이 대신한다. 그 칸이 든 옛 줄도 읽힌다 — <c>JsonData</c> 가 모르는 키를 버리고,
/// 계획은 비고 입력 · 지문은 null 이다(되살리기만 못 한다).
/// </para>
/// </summary>
/// <param name="SessionSeed">세션 시드 — 파일 이름이기도 하다.</param>
/// <param name="Run">런 번호(<see cref="RunHistory.Run"/>) — 같은 런의 앞 기록을 찾는 열쇠다.</param>
/// <param name="Record">시도 — 번호 · 실제로 싸운 단계 · 시드 · 결과 · 관측 전부.</param>
/// <param name="PickerId">그 판을 세운 고르기 id(대본이면 <c>script</c>).</param>
/// <param name="Plans">그 판에서 고른 계획들, 고른 순서로(<see cref="BattleSim.PlanEntries"/>). 5/8 까지의 줄에는 없다(빈 목록).</param>
/// <param name="Ticks">판의 길이.</param>
/// <param name="Instances">
/// 그 판의 사례와 라벨(<see cref="InstanceTracker"/> — 공장과 같은 정의 · 끝나지 않은 사례는 빠진다) (#114). 관측만으로는 같은 패턴이 연달아 선 사례의
/// 경계를 모른다. #113 의 줄에는 없다(null).
/// </param>
/// <param name="Inputs">판의 입력 — <see cref="InputTape.Runs"/> 의 <c>[코드, 틱 수]</c> 칸들. 5/8 까지의 줄에는 없다(null).</param>
/// <param name="DataSha256">판을 세운 데이터의 지문(<see cref="DataDigest"/>) — 되살린 판이 다를 때 데이터가 바뀌었는지를 가른다. 5/8 까지의 줄에는 없다(null).</param>
/// <param name="Bombs">
/// 그 판의 던지기들(<see cref="BattleSim.BombRecords"/> · 설계 2026-09-30 조각2 §4). 조각 2 의 4/5 전의 줄에는 없다(null) — 안 던진 판(빈 목록)과 가른다:
/// 2/5 · 3/5 의 게임은 던졌어도 적지 않아, 되살리기가 그 줄의 폭탄은 안 견준다(<see cref="Replay.Verdict"/>).
/// </param>
public sealed record AttemptEntry(
    ulong SessionSeed, int Run, AttemptRecord Record, string PickerId, IReadOnlyList<PlanEntry> Plans, int Ticks,
    IReadOnlyList<PatternInstance>? Instances = null, IReadOnlyList<int[]>? Inputs = null, string? DataSha256 = null,
    IReadOnlyList<BombRecord>? Bombs = null);

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
            Plans = entry.Plans,
            Outcome = r.Outcome,
            Ticks = entry.Ticks,
            Events = r.Events,
            Instances = entry.Instances,
            DataSha256 = entry.DataSha256,
            Bombs = entry.Bombs,
            Inputs = entry.Inputs,
        };
        return JsonSerializer.Serialize(line, _options);
    }

    /// <summary>
    /// 한 줄을 읽는다. 깨졌거나 필수 키가 빠지거나 입력의 칸이 틀리면(<see cref="InputTape.Problem"/>) <see cref="DataException"/> —
    /// <paramref name="source"/>(파일:줄)를 싣는다. 입력은 읽을 때 본다: 되살리기가 판 한가운데서 예외로 죽으면 어느 줄의 어느 칸인지가 안 남는다.
    /// </summary>
    public static AttemptEntry Parse(string line, string source)
    {
        AttemptLine l = JsonData<AttemptLine>.ParseOne(line, source);
        if (l.Inputs is { } inputs && InputTape.Problem(inputs) is { } problem)
        {
            throw new DataException($"{source}: inputs 가 틀렸다 — {problem}");
        }

        var record = new AttemptRecord(l.Attempt, l.Stage, l.Seed, l.Outcome, l.Events);
        return new AttemptEntry(l.SessionSeed, l.Run, record, l.Picker, l.Plans ?? [], l.Ticks, l.Instances, l.Inputs, l.DataSha256, l.Bombs);
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

        /// <summary>5/8 까지의 줄에는 없다 — 그 줄의 <c>drawn</c> 은 모르는 키라 버린다.</summary>
        public IReadOnlyList<PlanEntry>? Plans { get; init; }

        public required BattleOutcome Outcome { get; init; }

        public required int Ticks { get; init; }

        public required IReadOnlyList<DodgeEvent> Events { get; init; }

        public IReadOnlyList<PatternInstance>? Instances { get; init; }

        public string? DataSha256 { get; init; }

        /// <summary>조각 2 의 4/5 전의 줄에는 없다(null) — 입력 바로 앞이다: 입력 다음으로 길 수 있다(한 판 열 개).</summary>
        public IReadOnlyList<BombRecord>? Bombs { get; init; }

        /// <summary>줄의 끝이다 — 가장 길어(판 하나에 수백 칸) 사람이 머리를 먼저 읽는다.</summary>
        public IReadOnlyList<int[]>? Inputs { get; init; }
    }
}
