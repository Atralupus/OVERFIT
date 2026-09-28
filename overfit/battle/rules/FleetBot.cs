using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 봇 함대의 봇 — 성향(<see cref="BotTraits"/>)을 받아 싸운다 (#104 · 설계 2026-09-28 §3). 데이터 공장이 이 봇으로 망의 학습 데이터를 짓는다.
/// 사람과 같은 입력 통로(<see cref="InputFrame"/>)로 낸다.
///
/// <para>
/// <b>사람처럼 늦고 흔들린다.</b> 봇은 판정이 언제 서는지 안다(<see cref="BattleSim.NextActiveIn"/> — 선딜을 읽는 사람의 눈이다). 그러나
/// <b>선딜이 시작하고 제 반응 지연이 지나야</b> 누를 수 있다 — 선딜이 반응보다 짧으면 늦는다. 누르는 틱 = 판정 틱 + 편향 + 잡음. 이것이 없으면 망이
/// "초인이 어떻게 실패하는가" 를 배운다(<see cref="BattleSim"/> 의 주석이 못박아 둔 것이다).
/// </para>
///
/// <para>
/// <b>계획은 판정마다 한 번 뽑고, 누를 틱만 매 틱 다시 센다.</b> 수단 · 리듬 · 대시 방향 · 잡음은 그 판정의 성격이라 한 번이다 — 매 틱 다시 뽑으면
/// 긴 선딜 내내 주사위를 굴려 성향이 흐려진다(최소 봇의 점프가 그랬다). 누를 틱은 돌진처럼 판정 시각이 움직이는 패턴이 있어 매 틱 다시 센다.
/// </para>
///
/// <para>
/// <b>리듬형</b>은 그 판정을 눈으로 안 보고 기준 패턴의 박자(<see cref="BeatTable"/>)로 잰다 — 여는 그림이 같은 패턴에서만. 시각표가 모자란
/// 판정(점프 3연속의 둘째 착지부터)은 눈으로 잰다.
/// </para>
///
/// <para>
/// 난수는 <see cref="Det"/> 로만 — 행동 선택은 <c>FleetAct</c>, 잡음은 <c>FleetTiming</c>. 같은 성향 · 같은 시드는 같은 판이다. 스태미나가 값보다
/// 많을 때만 누른다(<see cref="Fighter.Affords"/>) — 스스로 탈진하는 성향은 두지 않는다(설계 §13).
/// </para>
/// </summary>
public sealed class FleetBot
{
    private readonly BotTraits _traits;
    private readonly ulong _seed;
    private readonly BeatTable _beats;
    private readonly int _reactionTicks;
    private readonly int _jumpLeadTicks;

    /// <summary>지난 틱의 패턴(null = 쉬는 중). 바뀌는 순간이 새 사례다.</summary>
    private string? _pattern;

    /// <summary>지금 사례가 선 틱 — 리듬형의 시각표가 여기서 잰다.</summary>
    private int _patternStart;

    /// <summary>지금까지의 사례 수 — 가드 주사위의 키다.</summary>
    private int _patterns;

    /// <summary>이번 사례를 가드로 받기로 했나 — 가드는 누르고 있는 동안이라 사례 내내 붙든다.</summary>
    private bool _guardThis;

    /// <summary>이번 사례에서 끝난 판정의 수 — 리듬형이 시각표의 몇째 칸을 볼지.</summary>
    private int _hitIndex;

    private bool _wasLive;
    private string? _lastKind;

    /// <summary>지금 판정의 선딜이 시작한 틱 — 반응 지연은 여기서 잰다.</summary>
    private int _windupStart;

    private Plan? _plan;

    /// <summary>지금까지 뽑은 계획의 수 — 행동 주사위와 잡음의 키다.</summary>
    private int _plans;

    private int _swings;
    private bool _chainThis;

    /// <param name="traits">성향.</param>
    /// <param name="seed">시도 시드 — 판과 같은 시드를 준다(최소 봇과 같은 차림).</param>
    /// <param name="beats">리듬형의 시각표.</param>
    /// <param name="fighter">이 봇이 모는 캐릭터 — 점프를 판정 앞 얼마에 누를지를 솟는 시간에서 잰다.</param>
    public FleetBot(BotTraits traits, ulong seed, BeatTable beats, FighterConfig fighter)
    {
        ArgumentNullException.ThrowIfNull(traits);
        ArgumentNullException.ThrowIfNull(beats);
        ArgumentNullException.ThrowIfNull(fighter);
        _traits = traits;
        _seed = seed;
        _beats = beats;
        _reactionTicks = Ticks(traits.ReactionSeconds);
        _jumpLeadTicks = Ticks(traits.JumpLead * fighter.JumpVelocity / Arena.Gravity);
    }

    /// <summary>이번 틱에 무엇을 할지. <see cref="BattleSim"/> 의 상태만 보고 정한다.</summary>
    public InputFrame Next(BattleSim sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        Track(sim);

        Fighter fighter = sim.Fighter;
        double gap = Math.Abs(fighter.X - sim.Boss.X) - sim.Boss.HalfWidth;
        sbyte toward = (sbyte)(fighter.X < sim.Boss.X ? 1 : -1);

        // 칼질은 끝까지 커밋이다 — 이을 작정이면 한 번 더 누를 뿐이다(최소 봇과 같은 자리 · 설계 2026-09-24 §5.1).
        if (fighter.Action == FighterAction.Attack)
        {
            bool press = _chainThis && fighter.ComboStep == 0 && !fighter.ComboQueued && fighter.Affords(FighterAction.Attack);
            return new InputFrame(0, false, false, false, Attack: press);
        }

        // 창이 살아 있는 동안은 "판정이 지금" 이다 — NextActiveIn 은 판정이 서는 틱에 null 이 된다(최소 봇의 주석 · #72).
        double? left = sim.SwingLive ? 0 : sim.NextActiveIn;
        if (sim.Boss.CurrentPattern is not null && !sim.Boss.Exhausted && left is { } remaining)
        {
            return Defend(sim, remaining, gap, toward);
        }

        // 후딜(남은 판정 없이 패턴이 도는 빈 시간)과 보스의 탈진은 들어가 칠 때다 — 멀리 서서 기다리는 사람도.
        return Offend(sim, gap, toward, punish: sim.Boss.CurrentPattern is not null || sim.Boss.Exhausted);
    }

    /// <summary>사례 · 선딜의 시작 · 끝난 판정을 센다. 틱마다 가장 먼저 부른다.</summary>
    private void Track(BattleSim sim)
    {
        string? now = sim.Boss.CurrentPattern;
        if (!string.Equals(now, _pattern, StringComparison.Ordinal))
        {
            _pattern = now;
            _plan = null;
            _hitIndex = 0;
            _wasLive = false;
            _lastKind = null;
            if (now is not null)
            {
                _patterns++;
                _patternStart = sim.Ticks;
                _windupStart = sim.Ticks;
                _guardThis = Det.Roll01(_seed, Det.Domain.FleetAct, k1: _patterns, k2: 0) < _traits.Guard;
            }
        }

        // 선딜은 그림이 보이기 시작하는 자리다. 첫 판정의 선딜은 사례가 선 틱이다 — 러너의 단계는 패턴이 선 틱에 아직 비어 있다가 다음 틱에
        // 선딜이 되므로, 그것을 새 선딜로 세면 반응이 한 틱 늦게 잰다(재 봄 · 시험 패턴에서 36 → 37틱). 그 뒤로는 판정 · 후딜 다음에 오는
        // 선딜만 새로 잰다 — 선딜 단계가 이어지면(3연격의 0.0 · 0.725) 첫 단계에서 잰다.
        string? kind = sim.BossStep?.Kind;
        if (kind == "windup" && _lastKind is not null && _lastKind != "windup")
        {
            _windupStart = sim.Ticks;
        }

        _lastKind = kind;

        bool live = sim.SwingLive;
        if (_wasLive && !live)
        {
            _hitIndex++;
            _plan = null;
        }

        _wasLive = live;
    }

    private InputFrame Defend(BattleSim sim, double remaining, double gap, sbyte toward)
    {
        if (_guardThis)
        {
            return new InputFrame(0, false, false, false, false, GuardHeld: true);
        }

        Plan plan = _plan ??= NewPlan();
        if (plan.Greedy)
        {
            // 이 판정을 안 피하고 칼을 넣는다 — 욕심 축(GreedWindow)이 재는 바로 그 사람이다.
            return Offend(sim, gap, toward, punish: true);
        }

        if (plan.Pressed)
        {
            return default;
        }

        int expected = sim.Ticks + (int)Math.Round(remaining / BattleSim.Dt);
        IReadOnlyList<double>? beat = plan.Rhythm ? _beats.For(sim.Boss.CurrentPattern!) : null;
        if (beat is not null && _hitIndex < beat.Count)
        {
            expected = _patternStart + Ticks(beat[_hitIndex]);
        }

        int press = expected - (plan.Verb == DodgeVerb.Jump ? _jumpLeadTicks : 0) + plan.OffsetTicks;
        if (sim.Ticks < Math.Max(press, _windupStart + _reactionTicks))
        {
            return default;
        }

        return Press(sim.Fighter, plan, toward);
    }

    /// <summary>
    /// 계획의 수단을 누른다. <b>행동이 실제로 서는 틱에만</b> 눌렀다고 적는다 — 굳었거나(탈진 · 붙들림) 다른 행동 중이면 그 틱의 누름은 안 먹으므로,
    /// 적어 버리면 그 판정의 회피를 통째로 건너뛴다. 조건은 규칙이 누름을 받는 자리(<c>Fighter.Begin</c> · <c>Fall</c>)와 같다.
    /// </summary>
    private static InputFrame Press(Fighter fighter, Plan plan, sbyte toward)
    {
        bool free = !fighter.Locked && fighter.Action is FighterAction.Idle or FighterAction.Guard;
        switch (plan.Verb)
        {
            case DodgeVerb.Dash:
                if (!free || !fighter.Affords(FighterAction.Dash))
                {
                    return default;
                }

                // 대시는 바라보는 쪽으로만 간다(Fighter.Move) — 바깥이면 이 틱에 돌아서고 다음 틱에 누른다.
                sbyte want = plan.Inward ? toward : (sbyte)-toward;
                if (fighter.Facing != want)
                {
                    return new InputFrame(want, false, false, false, false);
                }

                plan.Pressed = true;
                return new InputFrame(0, false, Dash: true, false, false);

            case DodgeVerb.Jump:
                if (fighter.Locked || fighter.Action != FighterAction.Idle || !fighter.Grounded)
                {
                    return default;
                }

                plan.Pressed = true;
                return new InputFrame(0, Jump: true, false, false, false);

            default:
                if (!free || !fighter.Affords(FighterAction.Parry))
                {
                    return default;
                }

                plan.Pressed = true;
                return new InputFrame(0, false, false, Parry: true, false);
        }
    }

    /// <summary>
    /// 칠 때. 멈출 간격 = 들어가 칠 때면 칼 사거리, 쉬는 동안이면 사거리 + 기다리는 간격. 쉬는 동안 기다리는 봇은 멈출 간격의 절반보다
    /// 가까우면 물러선다 — 그 사이에서는 선다(경계에서 틱마다 돌아서면 바라보는 쪽이 흔들려 대시의 방향이 거짓말을 한다).
    /// </summary>
    private InputFrame Offend(BattleSim sim, double gap, sbyte toward, bool punish)
    {
        Fighter fighter = sim.Fighter;
        double reach = sim.FighterReach;
        double stand = punish ? reach : reach + _traits.RestGap;
        if (gap > stand)
        {
            return new InputFrame(toward, false, false, false, false);
        }

        if (!punish && _traits.RestGap > 0)
        {
            return gap < reach + (_traits.RestGap / 2) ? new InputFrame((sbyte)-toward, false, false, false, false) : default;
        }

        if (gap > reach || fighter.Action != FighterAction.Idle || !fighter.Affords(FighterAction.Attack))
        {
            return default;
        }

        // 2타를 이을지는 1타를 누를 때 정한다 — 사람은 1타를 누를 때 이미 2타를 정해 둔다(최소 봇과 같다).
        _swings++;
        _chainThis = Det.Roll01(_seed, Det.Domain.FleetAct, k1: _swings, k2: 5) < _traits.Chain;
        return new InputFrame(0, false, false, false, Attack: true);
    }

    /// <summary>판정 하나의 계획 — 욕심 · 수단 · 리듬 · 대시 방향 · 잡음을 <b>한 번</b> 뽑는다. 키는 계획 번호다.</summary>
    private Plan NewPlan()
    {
        _plans++;
        double Roll(int kind) => Det.Roll01(_seed, Det.Domain.FleetAct, k1: _plans, k2: kind);

        double pick = Roll(2) * (_traits.Dash + _traits.Jump + _traits.Parry);
        DodgeVerb verb = pick < _traits.Dash ? DodgeVerb.Dash
            : pick < _traits.Dash + _traits.Jump ? DodgeVerb.Jump
            : DodgeVerb.Parry;
        return new Plan
        {
            Greedy = Roll(1) < _traits.Greed,
            Verb = verb,
            Rhythm = Roll(3) < _traits.Rhythm,
            Inward = Roll(4) < _traits.DashInward,
            OffsetTicks = Ticks(_traits.BiasSeconds + BotNoise.Sample(_seed, _plans, _traits.JitterSeconds)),
        };
    }

    /// <summary>
    /// 초 → 틱. <see cref="BattleSim.TicksFor"/> 와 같은 반올림(0 에서 먼 쪽)이되 부호와 0 을 허락한다 — 편향은 음수(먼저)일 수 있고,
    /// TicksFor 는 길이라 1 틱보다 짧아지지 않는다.
    /// </summary>
    private static int Ticks(double seconds) => (int)Math.Round(seconds / BattleSim.Dt, MidpointRounding.AwayFromZero);

    private sealed class Plan
    {
        public required bool Greedy { get; init; }

        public required DodgeVerb Verb { get; init; }

        public required bool Rhythm { get; init; }

        public required bool Inward { get; init; }

        public required int OffsetTicks { get; init; }

        public bool Pressed { get; set; }
    }
}
