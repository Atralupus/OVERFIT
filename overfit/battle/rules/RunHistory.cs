using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 끝까지 간 시도 하나 (#72 · 설계 §4.4). 전투 한 번이 시도 하나다 — 재시도 · 다음 단계 · 처음부터 · 디버그 단계 점프 모두.
/// </summary>
/// <param name="Number">시도 번호 — 세션 안에서 1부터 오른다.</param>
/// <param name="Stage">그 시도의 단계.</param>
/// <param name="Seed">시도 시드 — <see cref="RunHistory.SeedOf"/>. 로그의 <c>seed=</c> 와 같다.</param>
/// <param name="Outcome">결과. 이긴 판도 붙는다 — 1단계를 이긴 판이 곧 "1단계 기록" 이다.</param>
/// <param name="Events">그 판의 회피 관측 전부 — 망이 읽을 재료다.</param>
public sealed record AttemptRecord(int Number, int Stage, ulong Seed, BattleOutcome Outcome, IReadOnlyList<DodgeEvent> Events);

/// <summary>
/// 세션의 시도 번호와 기록 (#72 · 설계 §4.4). <c>Game</c>(Autoload)이 하나를 든다 — 씬이 다시 서도 산다. 로직을 여기 두는
/// 이유는 <c>Game.cs</c> 에 두면 테스트 밖으로 나가서다(규칙 층 · csproj 에 링크).
///
/// <para>
/// <b>시도 시드</b> = <c>Det.Hash64(세션 시드, Det.Domain.Attempt, k1: 시도 번호)</c>. 세션 시드는 게임을 켤 때 한 번 뽑고
/// (<c>--session-seed=N</c> 으로 고정), 시도 번호와 함께 <b>처음부터 해도 안 돌아간다</b> — 그래야 한 세션 안에서
/// (세션 시드, 번호)가 하나뿐이고, 로그의 <c>seed=</c> 하나가 한 판을 가리킨다.
/// </para>
///
/// <para>
/// <b>디스크에 남기지 않는다</b> — 게임을 끄면 사라진다. 저장과, 기록을 읽는 고르기(망)의 판을 되살리는 길은 망 PR 이 정한다.
/// </para>
/// </summary>
public sealed class RunHistory
{
    private readonly List<AttemptRecord> _records = new();

    public RunHistory(ulong sessionSeed) => SessionSeed = sessionSeed;

    public ulong SessionSeed { get; }

    /// <summary>지금까지 연 시도의 수 — 곧 마지막 시도의 번호다. 끝까지 안 간 시도도 센다.</summary>
    public int Attempts { get; private set; }

    /// <summary>끝까지 간 시도들, 붙인 순서대로. 처음부터 하면 비운다(<see cref="Clear"/>).</summary>
    public IReadOnlyList<AttemptRecord> Records => _records;

    /// <summary>
    /// 시도를 하나 연다 — 번호가 오르고 그 번호의 시드를 돌려준다. 전투가 설 때마다 한 번이다. 끝까지 안 간 전투(F9 ·
    /// 단계 점프 · 도중에 타이틀)도 번호는 쓴다: 번호를 되돌리면 같은 시드가 두 판에 붙는다.
    /// </summary>
    public (int Number, ulong Seed) Open()
    {
        Attempts++;
        return (Attempts, SeedOf(Attempts));
    }

    /// <summary>그 번호의 시도 시드. 상태를 안 읽는 좌표 조회다(<see cref="Det"/>).</summary>
    public ulong SeedOf(int attempt) => Det.Hash64(SessionSeed, Det.Domain.Attempt, k1: attempt);

    /// <summary>끝까지 간 시도를 붙인다 — 전투가 끝나는 곳(<c>Battle.Finish</c>)에서 판마다 한 번.</summary>
    public void Record(AttemptRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records.Add(record);
    }

    /// <summary>처음부터 — 기록만 비운다. 새 런은 1단계에서 다시 잰다. 세션 시드와 번호는 그대로다.</summary>
    public void Clear() => _records.Clear();
}
