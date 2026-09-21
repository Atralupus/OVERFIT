using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>한 판의 끝.</summary>
public enum BattleOutcome
{
    Win,
    Lose,
}

/// <summary>한 판을 세우는 데 필요한 전부.</summary>
public sealed class BattleSetup
{
    public required Arena Arena { get; set; }

    public required FighterConfig Fighter { get; set; }

    public required BossConfig Boss { get; set; }

    /// <summary>이 단계의 보스가 쓰는 패턴 id 들. 단계가 오를수록 길어진다 (2 · 3 · 5 · 7 · 10).</summary>
    public required IReadOnlyList<string> PatternIds { get; set; }

    public required IReadOnlyDictionary<string, PatternDef> Patterns { get; set; }

    public required ulong Seed { get; set; }

    /// <summary>이 틱을 넘기면 시간 초과로 패배. <b>한 판이 반드시 끝나게 하는 안전장치다.</b></summary>
    public required int MaxTicks { get; set; }
}

/// <summary>
/// 전투 한 판. <b>여기만이 파이터와 보스를 동시에 안다.</b>
///
/// <para>
/// 패턴 선택은 지금 <b>무작위</b>다. 일부러다 — 나중에 망이 이 자리를 갈아끼울 때
/// 무작위가 대조군이 된다. 망이 정말 일하는지 증명할 방법이 그것 말고 없다.
/// 무작위지만 <see cref="Det"/> 로 뽑으므로 같은 시드는 같은 순서를 낸다.
/// </para>
/// </summary>
public sealed class BattleSim
{
    /// <summary>고정 60틱. 벽시계를 안 본다 — 그래야 헤드리스로 수백만 판을 돌려도 같은 결과다.</summary>
    public const double Dt = 1.0 / 60.0;

    private readonly BattleSetup _setup;
    private PatternRunner? _runner;
    private PatternDef? _current;
    private double _gapLeft;
    private int _picks;

    public BattleSim(BattleSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _setup = setup;
        Fighter = new Fighter(setup.Fighter, setup.Arena, setup.Arena.Width * 0.25);
        Boss = new Boss(setup.Boss, setup.Arena, setup.Arena.Width * 0.75);
        _gapLeft = setup.Boss.PatternGap;
    }

    public Fighter Fighter { get; }

    public Boss Boss { get; }

    /// <summary>지금까지 진행한 틱 수.</summary>
    public int Ticks { get; private set; }

    /// <summary>한 틱 민다. 판이 끝났으면 결과를, 아니면 null 을 돌려준다.</summary>
    public BattleOutcome? Tick(InputFrame input)
    {
        Ticks++;
        Fighter.Tick(input, Dt);
        AdvanceBoss();
        Strike();

        if (!Boss.Alive)
        {
            Log.Info("result", $"win ticks={Ticks} fighter_hp={Fighter.Health}");
            return BattleOutcome.Win;
        }

        if (!Fighter.Alive)
        {
            Log.Info("result", $"lose reason=dead ticks={Ticks} boss_hp={Boss.Health}");
            return BattleOutcome.Lose;
        }

        if (Ticks >= _setup.MaxTicks)
        {
            Log.Info("result", $"lose reason=timeout ticks={Ticks} boss_hp={Boss.Health}");
            return BattleOutcome.Lose;
        }

        return null;
    }

    /// <summary>보스: 쉬는 중이면 다가가고, 패턴 중이면 타임라인을 민다.</summary>
    private void AdvanceBoss()
    {
        if (_runner is null)
        {
            _gapLeft -= Dt;
            Boss.Approach(Fighter.X, Dt);
            if (_gapLeft <= 0)
            {
                Begin();
            }

            return;
        }

        foreach (HitBox box in _runner.Tick(Dt))
        {
            Land(box);
        }

        if (_runner.Finished)
        {
            Log.Debug("boss", $"pattern_end id={Boss.CurrentPattern} tick={Ticks}");
            _runner = null;
            _current = null;
            Boss.CurrentPattern = null;
            _gapLeft = Boss.PatternGap;
        }
    }

    /// <summary>다음 패턴을 고른다. 무작위이되 시드·도메인·뽑은 횟수로 좌표를 조회한다.</summary>
    private void Begin()
    {
        int index = Det.RollInt(_setup.Seed, Det.Domain.PatternPick, _setup.PatternIds.Count, k1: _picks);
        _picks++;
        string id = _setup.PatternIds[index];
        if (!_setup.Patterns.TryGetValue(id, out PatternDef? def))
        {
            Log.Error("boss", $"pattern_missing id={id}");
            return;
        }

        _current = def;
        _runner = new PatternRunner(def);
        Boss.CurrentPattern = id;
        Log.Debug("boss", $"pattern_begin id={id} pick={_picks} tick={Ticks}");
    }

    /// <summary>보스의 판정 하나를 파이터에게 대고, 맞았으면 깎는다.</summary>
    private void Land(HitBox box)
    {
        HitVerdict verdict = HitResolver.Resolve(Fighter, Boss.X, box, _current!.Tags);
        if (verdict == HitVerdict.Hit)
        {
            Fighter.TakeDamage(box.Damage);
        }

        Log.Debug("dodge", $"pattern={Boss.CurrentPattern} verdict={verdict} hp={Fighter.Health} tick={Ticks}");
    }

    /// <summary>파이터의 공격이 보스에 닿았는가. 판정이 선 틱에만 한 번 본다.</summary>
    private void Strike()
    {
        if (!Fighter.AttackActive || _struckThisSwing)
        {
            if (!Fighter.AttackActive)
            {
                _struckThisSwing = false;
            }

            return;
        }

        double gap = Math.Abs(Fighter.X - Boss.X) - Boss.HalfWidth;
        if (gap <= Fighter.AttackReach)
        {
            Boss.TakeDamage(Fighter.AttackDamage);
            Log.Debug("strike", $"hit boss_hp={Boss.Health} gap={gap:0} tick={Ticks}");
        }

        _struckThisSwing = true;
    }

    private bool _struckThisSwing;
}
