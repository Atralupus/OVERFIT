using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 파이터 망 조종기 (설계 2026-10-01 조각6 §1) — 봇 함대(<see cref="FleetBot"/>)처럼 틱마다 판을 보고 입력을 낸다. 6틱마다 정책망으로 칸 하나를 뽑고(열린 칸의
/// 소프트맥스 · 시드), 그 칸을 다음 결정까지 입력으로 낸다. 보스는 12틱 앞의 모습을 본다 — 파이터도 반응 속도가 아니라 읽기로 이겨야 학습한 보스가 사람에게
/// 통한다. 학습용 상대다 — 게임은 안 쓴다.
/// </summary>
public sealed class FighterNetDriver
{
    private readonly PolicyNet? _net;
    private readonly ulong _seed;
    private readonly IReadOnlyList<string> _roster;
    private readonly FighterObservation _observation;
    private readonly double _arenaWidth;
    private readonly int _decideTicks;
    private readonly BossGlance[] _ring;
    private readonly List<NetStep> _steps = new();
    private int _head;
    private int _count;
    private int _action;
    private int _sinceDecision;
    private int _decisions;

    /// <param name="net">파이터 정책망 — 없으면 열린 칸에 같은 확률.</param>
    /// <param name="seed">뽑기의 시드.</param>
    /// <param name="roster">보스전의 명부 — 보스 동작의 원핫.</param>
    /// <param name="arenaWidth">아레나 폭 — 자리의 나눗수.</param>
    /// <param name="decideTicks">결정 간격(틱).</param>
    /// <param name="delayTicks">보스를 보는 늦춤(틱).</param>
    public FighterNetDriver(PolicyNet? net, ulong seed, IReadOnlyList<string> roster, double arenaWidth, int decideTicks = 6, int delayTicks = 12)
    {
        ArgumentNullException.ThrowIfNull(roster);
        _net = net;
        _seed = seed;
        _roster = roster;
        _observation = new FighterObservation(roster.Count);
        _arenaWidth = arenaWidth;
        _decideTicks = decideTicks;
        _ring = new BossGlance[delayTicks + 1];
    }

    /// <summary>적은 결정들.</summary>
    public IReadOnlyList<NetStep> Steps => _steps;

    /// <summary>열린 칸 — 폭탄은 남았을 때만.</summary>
    public static bool[] Mask(int bombsLeft)
    {
        var mask = new bool[FighterActions.Count];
        Array.Fill(mask, true);
        mask[FighterActions.Bomb] = bombsLeft > 0;
        return mask;
    }

    /// <summary>이 틱의 입력.</summary>
    public InputFrame Next(BattleSim sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        Push(Glance(sim));
        if (_count == 1 || _sinceDecision >= _decideTicks)
        {
            Decide(sim);
            _sinceDecision = 0;
        }

        return FighterActions.Input(_action, _sinceDecision++);
    }

    private void Decide(BattleSim sim)
    {
        Fighter f = sim.Fighter;
        var self = new FighterSelf(
            f.X, f.Y, f.VelocityY, f.Facing, f.Action, f.Stiff, f.Exhausted, f.Held, f.StaminaRatio, f.Health, sim.FighterMaxHealth, f.BombsLeft,
            f.ThrowProgress, f.ComboStep, f.Grounded, f.AirDashSpent);
        double[] obs = _observation.Encode(Delayed, self, sim.BombsInFlight, _arenaWidth);
        bool[] mask = Mask(f.BombsLeft);
        (double[]? logits, double value) = _net is null ? (null, 0.0) : _net.Forward(obs);
        (int action, double logProb) = MaskedSampler.Sample(logits, mask, Det.Roll01(_seed, Det.Domain.FighterControl, k1: _decisions++));
        _action = action;
        _steps.Add(new NetStep(sim.Ticks, obs, mask, action, logProb, value));
    }

    private BossGlance Glance(BattleSim sim)
    {
        Boss b = sim.Boss;
        int move = -1;
        for (int i = 0; i < _roster.Count; i++)
        {
            move = _roster[i] == b.CurrentPattern ? i : move;
        }

        (bool leaping, _, _) = sim.BossLeap;
        TravelState travel = sim.BossRunning ? TravelState.Approach
            : sim.BossRetreating ? TravelState.Retreat
            : leaping ? TravelState.Leap
            : TravelState.None;
        bool grab = sim.BossHitAhead is { } ahead && ahead.Hit.GrabHoldSeconds > 0;
        return new BossGlance(
            b.X, b.Y, b.Facing, b.Health, sim.BossMaxHealth, sim.Forms.Form, move, FighterObservation.StepKind(sim.BossStep?.Kind), sim.NextActiveIn, grab,
            travel, b.Exhausted, sim.Forms.Shifting, sim.BossAlert);
    }

    private void Push(BossGlance g)
    {
        _head = (_head + 1) % _ring.Length;
        _ring[_head] = g;
        if (_count < _ring.Length)
        {
            _count++;
        }
    }

    private BossGlance Delayed => _ring[((_head - (_count - 1)) % _ring.Length + _ring.Length) % _ring.Length];
}
