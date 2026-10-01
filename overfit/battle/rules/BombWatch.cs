using System;
using System.Collections.Generic;
using System.Globalization;

namespace Overfit.Battle.Rules;

/// <summary>
/// 보스가 폭탄 던지기를 보고 · 알고 · 끊고 · 멈칫하는 것 (설계 2026-09-30 조각2 §2). 판(<see cref="BattleSim"/>)이 틱마다 묻고, 끊는 것 · 돌아서는
/// 것 · 동작을 세우는 것은 판이 한다 — 동작을 걷고 세우는 길이 판 하나여야 끊긴 동작이 무언가를 남기지 않는다(<c>BattleSim.ClearPattern</c>).
///
/// <para>
/// <b>늘 끊으려 한다.</b> "언제 장전하나" 같은 조건이 여기 없다 — 알면 끊을 자리가 올 때 끊는다. 끊을 자리가 언제 오는지는 데이터(캔슬 지점)와
/// 흐름(쉬기 · 달리기 · 동작의 끝)이 정한다. 배우는 것은 끊을지 말지가 아니라 끊을 수 있게 미리 서 있기이고, 그것은 조각 4 의 예측이 계획으로 한다.
/// </para>
///
/// <para>
/// 탈진 중에도 본다 — 못 움직일 뿐이다. 아는 것은 던지기가 도는 동안뿐이다: 놓거나 끊기면 알아챔이 사라진다.
/// </para>
/// </summary>
public sealed class BombWatch
{
    private readonly int _delayTicks;
    private readonly int _hesitateTicks;

    /// <summary>멈칫이 남은 틱 — 0 이면 멈칫이 아니다.</summary>
    private int _hesitateLeft;

    public BombWatch(BombReactionDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        _delayTicks = BattleSim.TicksFor(def.DelaySeconds);
        _hesitateTicks = BattleSim.TicksFor(def.HesitateSeconds);
        Move = def.Move;
    }

    /// <summary>끊은 뒤 세울 동작의 id (<c>bomb_reaction.move</c>).</summary>
    public string Move { get; }

    /// <summary>지금 던지기를 본 틱 — 던지기가 선 틱이다. 한 번도 안 봤으면 null.</summary>
    public int? SeenAt { get; private set; }

    /// <summary>이 던지기에 이미 끊었나 — 놓을 때 끊지 못했으면 <c>bomb_late</c> 를 남기는 데 쓴다.</summary>
    public bool Reacted { get; private set; }

    /// <summary>멈칫 중인가 — 끊은 틱부터 반응의 동작이 서기 전까지.</summary>
    public bool Hesitating => _hesitateLeft > 0;

    /// <summary>반응의 동작이 도는 중인가 — 그 동안은 새로 안 끊는다(§2.2). 판이 동작을 세울 때 켜고 걷을 때 끈다.</summary>
    public bool InReaction { get; private set; }

    /// <summary>파이터가 던지기를 시작했다 — 이 틱에 본다.</summary>
    public void See(int tick)
    {
        SeenAt = tick;
        Reacted = false;
    }

    /// <summary>지금 아나 — 던지기가 돌고(<paramref name="throwing"/>) 본 틱에서 반응 지연이 지났다.</summary>
    public bool Aware(int tick, bool throwing) => throwing && Knew(tick);

    /// <summary>
    /// 본 던지기를 알았나 — 던지기가 끝났어도 본 틱에서 반응 지연이 지났으면 참이다. 놓는 틱에는 던지기가 이미 끝나 <see cref="Aware"/> 가 거짓이라,
    /// "알았지만 끊을 자리가 안 왔다"(<c>bomb_late</c>)는 이것으로 가른다.
    /// </summary>
    public bool Knew(int tick) => SeenAt is int seen && tick >= seen + _delayTicks;

    /// <summary>이 틱에 처음 알았나 — 로그 <c>bomb_seen</c> 이 한 번만 남는다.</summary>
    public bool JustAware(int tick, bool throwing) => throwing && SeenAt is int seen && tick == seen + _delayTicks;

    /// <summary>끊었다 — 멈칫을 센다.</summary>
    public void React()
    {
        Reacted = true;
        _hesitateLeft = _hesitateTicks;
    }

    /// <summary>멈칫 한 틱 — 다 셌으면 참이다(판이 이 틱에 반응의 동작을 세운다).</summary>
    public bool HesitateTick() => --_hesitateLeft <= 0;

    /// <summary>반응의 동작이 섰다.</summary>
    public void Begin() => InReaction = true;

    /// <summary>보스가 하던 것을 걷는다 — 멈칫도 반응의 동작도 끝난다(동작이 끝났거나 · 탈진했거나 · 끊었거나).</summary>
    public void Stop()
    {
        _hesitateLeft = 0;
        InReaction = false;
    }

    /// <summary>
    /// 이 틱이 끊을 자리인가 (§2.2) — 던지기를 알고 · 반응의 동작 중이 아니고, 동작이 없으면(쉬기 · 달리기) 아무 틱이, 동작이면 러너가 캔슬 지점에 드는
    /// 틱이다(계획이 고른 지점이 아니어도 · 잇는 동작의 지점이어도). 조건표가 없다 — 지점을 데이터에 더하면 그대로 끊을 자리가 된다. 탈진은 판이 거른다.
    /// </summary>
    /// <param name="tick">판의 틱.</param>
    /// <param name="throwing">파이터가 던지는 중인가.</param>
    /// <param name="move">지금 동작 — 쉬기 · 달리기면 null.</param>
    /// <param name="next">러너가 이번 틱에 들 틱(러너의 틱 + 1).</param>
    /// <param name="held">움직임이 패턴 시계를 세웠나 — 세운 틱에는 러너가 안 가므로 지점에 안 든다(지점은 움직임 밖이다 · PatternDataTests).</param>
    public bool CutSpot(int tick, bool throwing, PatternDef? move, int next, bool held) =>
        Aware(tick, throwing) && !InReaction && (move is null || (!held && IsCutPoint(move, next)));

    /// <summary>
    /// 다음 끊을 자리 — <c>bomb_late</c> 의 <c>next=</c> (§2.6). 동작 중이면 러너의 다음 캔슬 지점, 없으면 동작이 끝나는 러너 틱이다(반응의 동작은 지점을
    /// 안 쓴다). 탈진이면 <c>exhausted</c> 다. 쉬기 · 달리기에서는 알 때 끊으므로 동작이 없는 채로 여기 오는 것은 반응의 동작이 데이터에 없을 때뿐이다(<c>-</c>).
    /// </summary>
    public string NextChance(bool exhausted, PatternDef? move, int next) =>
        exhausted ? "exhausted"
        : move is null ? "-"
        : NextSpot(move, next, points: !InReaction).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// 동작 <paramref name="def"/> 의 러너가 <paramref name="tick"/> 틱에 들려는 것이 캔슬 지점인가 — 판은 그 틱의 단계에 들지 않고 끊는다. 조종기의 캔슬
    /// 결정과 같은 틱이다(<c>BattleSim.CancelPointAt</c>). 보스가 아는 동안 틱마다 불리므로 인덱스로 돈다 — 목록 인터페이스 위의 foreach 는 부를 때마다
    /// 열거자를 힙에 만든다(<c>BattleSim.Strike</c> 의 매 틱 할당과 같은 까닭).
    /// </summary>
    private static bool IsCutPoint(PatternDef def, int tick)
    {
        IReadOnlyList<CancelPointDef> points = def.CancelPoints ?? [];
        for (int i = 0; i < points.Count; i++)
        {
            if (BattleSim.TicksFor(points[i].T) == tick)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 러너 <paramref name="tick"/> 부터 가장 가까운 끊을 자리 — 다음 캔슬 지점의 러너 틱이다. 지점이 안 남았거나 지점을 못 쓰면(<paramref name="points"/> 가
    /// 거짓 — 반응의 동작) 동작이 끝나는 러너 틱이다: 끊는 것은 그 다음 틱의 쉬기에서다.
    /// </summary>
    private static int NextSpot(PatternDef def, int tick, bool points)
    {
        int next = BattleSim.TicksFor(def.Timeline[^1].T);
        if (!points)
        {
            return next;
        }

        IReadOnlyList<CancelPointDef> cancels = def.CancelPoints ?? [];
        for (int i = 0; i < cancels.Count; i++)
        {
            int at = BattleSim.TicksFor(cancels[i].T);
            if (at >= tick && at < next)
            {
                next = at;
            }
        }

        return next;
    }
}
