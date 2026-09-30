using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>던지기 하나가 어떻게 끝났나 (설계 2026-09-30 조각2 §4) — 기록 줄에는 소문자 이름이다.</summary>
public enum BombOutcome
{
    /// <summary>놓았고 보스에게 떨어졌다 — 보스가 끊으려 했지만 늦은 것도 든다(그때는 <see cref="BombRecord.React"/> 가 있다).</summary>
    Landed,

    /// <summary>보스의 반응 동작이 놓기 전에 끊었다.</summary>
    Cut,

    /// <summary>다른 판정이 놓기 전에 끊었다 — 보스가 끊기 전에 하던 동작에 맞았다.</summary>
    Hit,

    /// <summary>판이 먼저 끝났다 — 던지는 중이거나 나는 중에.</summary>
    End,
}

/// <summary>
/// 던지기 하나 (설계 2026-09-30 조각2 §4) — 시도 기록 한 줄의 <c>bombs</c> 칸이다. 조각 4 가 "무엇을 보고 던지나" 를 읽는 자리다.
/// </summary>
/// <param name="At">던진 틱 — L 을 누른 틱(선딜의 첫 틱)이다.</param>
/// <param name="During">그때 보스가 하던 동작 id — 쉬기 · 달리기 · 멈칫 · 탈진이면 null. 파이터를 먼저 미는 틱이라 그 틱에 서는 동작은 아직 아니다.</param>
/// <param name="Outcome">어떻게 끝났나.</param>
/// <param name="React">보스가 이 던지기에 끊은 틱 — 안 끊었으면 null. <see cref="BombOutcome.Landed"/> 인데 값이 있으면 끊으려 했지만 늦은 것이다.</param>
public sealed record BombRecord(int At, string? During, BombOutcome Outcome, int? React);

/// <summary>
/// 판의 던지기를 적는 장부 (설계 2026-09-30 조각2 §4) — <see cref="BattleSim"/> 이 사건마다 한 줄씩 부른다. 던질 때 한 줄을 열고(결과는 판이 끝날
/// 때까지 안 정해지면 <see cref="BombOutcome.End"/>), 보스가 끊으면 그 틱을, 놓기 전에 끊기면 결과를, 놓으면 나는 줄에 넣는다. 나는 폭탄은 나는 시간이
/// 같아 놓은 순서로 떨어진다 — 떨어진 수만큼 가장 먼저 놓은 것부터 맞힘이다.
/// </summary>
public sealed class BombLedger
{
    private readonly List<BombRecord> _records = new();

    /// <summary>놓았고 아직 안 떨어진 던지기들의 칸, 놓은 순서로.</summary>
    private readonly Queue<int> _flying = new();

    /// <summary>손에 든 던지기의 칸 — 안 던지는 중이면 −1.</summary>
    private int _throwing = -1;

    /// <summary>이 판의 던지기들, 던진 순서로.</summary>
    public IReadOnlyList<BombRecord> Records => _records;

    /// <summary>던지기가 섰다.</summary>
    public void Throw(int tick, string? during)
    {
        _throwing = _records.Count;
        _records.Add(new BombRecord(tick, during, BombOutcome.End, null));
    }

    /// <summary>보스가 손에 든 던지기에 끊었다 — 보스는 던지기가 도는 동안만 알므로 끊는 것은 늘 손에 든 던지기다.</summary>
    public void React(int tick)
    {
        if (_throwing >= 0)
        {
            _records[_throwing] = _records[_throwing] with { React = tick };
        }
    }

    /// <summary>놓기 전에 끊겼다 — 반응의 동작이면 <see cref="BombOutcome.Cut"/>, 아니면 <see cref="BombOutcome.Hit"/>.</summary>
    public void Lost(bool byReaction)
    {
        if (_throwing >= 0)
        {
            _records[_throwing] = _records[_throwing] with { Outcome = byReaction ? BombOutcome.Cut : BombOutcome.Hit };
            _throwing = -1;
        }
    }

    /// <summary>놓았다 — 날기 시작한다.</summary>
    public void Released()
    {
        if (_throwing >= 0)
        {
            _flying.Enqueue(_throwing);
            _throwing = -1;
        }
    }

    /// <summary>나는 폭탄 <paramref name="count"/> 개가 떨어졌다 — 가장 먼저 놓은 것부터.</summary>
    public void Landed(int count)
    {
        for (int i = 0; i < count && _flying.Count > 0; i++)
        {
            int k = _flying.Dequeue();
            _records[k] = _records[k] with { Outcome = BombOutcome.Landed };
        }
    }
}
