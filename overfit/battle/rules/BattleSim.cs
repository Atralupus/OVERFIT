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

    private readonly List<DodgeEvent> _events = new();

    /// <summary>회피 행동이 시작된 시각(초). 타이밍 오차를 재려고 들고 있는다.</summary>
    private double _actionStartedAt = double.NaN;

    private DodgeVerb _actionVerb = DodgeVerb.None;

    private int _actionDirection;

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

    /// <summary>이 판에서 일어난 회피 관측 전부. <see cref="PlayerAxes.From"/> 에 그대로 넣는다.</summary>
    public IReadOnlyList<DodgeEvent> Events => _events;

    /// <summary>한 틱 민다. 판이 끝났으면 결과를, 아니면 null 을 돌려준다.</summary>
    public BattleOutcome? Tick(InputFrame input)
    {
        Ticks++;
        // 틱 시작의 접지 상태. "이번 틱에 땅에서 떨어졌는가" 는 이것과 비교해야만 알 수 있다 —
        // 공중에서 점프를 또 눌러도 Fall 이 물리적으로는 무시하지만, 그 입력만 보면 구별이 안 된다.
        bool wasGrounded = Fighter.Grounded;
        Fighter.Tick(input, Dt);
        RememberDodgeStart(input, wasGrounded);
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

    /// <summary>
    /// 이번 틱에 회피 행동이 시작됐으면 그 시각과 방향을 적어 두고, 이미 적어 둔 행동이
    /// 끝났으면 잊는다. <b>판정이 설 때 이것과의 차이가 타이밍 오차가 된다.</b>
    /// </summary>
    /// <param name="input">이번 틱의 입력. 점프가 눌렸는지를 본다.</param>
    /// <param name="wasGrounded">이번 틱이 시작될 때(<see cref="Fighter.Tick"/> 이전) 접지 상태.
    /// 점프 엣지 검출에 쓴다 — <see cref="Fighter.Grounded"/> 만 보면 "떨어진 순간"과
    /// "이미 공중인데 또 눌렀다"를 구별할 수 없다.</param>
    private void RememberDodgeStart(InputFrame input, bool wasGrounded)
    {
        double now = Ticks * Dt;
        if (Fighter.Action == FighterAction.Dash && Fighter.ActionElapsed <= Dt)
        {
            _actionVerb = DodgeVerb.Dash;
            _actionStartedAt = now;
            // 보스 쪽으로 갔으면 안(+1), 반대면 밖(-1)
            _actionDirection = Math.Sign(Fighter.Facing * (Boss.X - Fighter.X)) >= 0 ? 1 : -1;
        }
        else if (Fighter.Action == FighterAction.Parry && Fighter.ActionElapsed <= Dt)
        {
            _actionVerb = DodgeVerb.Parry;
            _actionStartedAt = now;
            _actionDirection = 0;
        }
        else if (input.Jump && wasGrounded && !Fighter.Grounded)
        {
            _actionVerb = DodgeVerb.Jump;
            _actionStartedAt = now;
            _actionDirection = 0;
        }
        // 시작한 행동이 끝났을 때 잊는다. **Land 에서 지우지 않는다** — 대시 한 번의 무적이
        // 연속타 여러 대를 막을 수 있는데, 첫 대가 소비해 버리면 나머지가 "회피 수단 없음" 으로
        // 기록되어 근거가 없는 게 아니라 **잘못 붙는다.**
        else if (_actionVerb switch
        {
            DodgeVerb.Dash => Fighter.Action != FighterAction.Dash,
            DodgeVerb.Parry => Fighter.Action != FighterAction.Parry,
            DodgeVerb.Jump => Fighter.Grounded,
            _ => false,
        })
        {
            _actionStartedAt = double.NaN;
            _actionVerb = DodgeVerb.None;
            _actionDirection = 0;
        }
    }

    /// <summary>보스의 판정 하나를 파이터에게 대고, 맞았으면 깎는다.</summary>
    private void Land(HitBox box)
    {
        HitVerdict verdict = HitResolver.Resolve(Fighter, Boss.X, box, _current!.Tags);
        if (verdict == HitVerdict.Hit)
        {
            Fighter.TakeDamage(box.Damage);
        }

        double now = Ticks * Dt;
        DodgeVerb verb = double.IsNaN(_actionStartedAt) ? DodgeVerb.None : _actionVerb;
        double error = verb == DodgeVerb.None ? 0 : _actionStartedAt - now;

        _events.Add(new DodgeEvent(
            PatternId: Boss.CurrentPattern ?? "?",
            Verb: verb,
            Verdict: verdict,
            TimingError: error,
            Direction: verb == DodgeVerb.Dash ? _actionDirection : 0,
            Airborne: !Fighter.Grounded,
            Distance: Math.Abs(Fighter.X - Boss.X),
            GreedWindow: Fighter.Action == FighterAction.Attack));

        Log.Info("dodge", $"pattern={Boss.CurrentPattern} verb={verb} verdict={verdict}"
            + $" err={error:0.000} dir={_actionDirection} air={!Fighter.Grounded} hp={Fighter.Health}");
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
