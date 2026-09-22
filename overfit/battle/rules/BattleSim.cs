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

    // 회피 수단마다 **따로** 시작 시각을 들고 있는다(초, NaN = 지금 그 수단이 없다).
    // 슬롯이 하나였을 때는 "가장 최근에 시작한 행동" 이 판정을 다 가져갔다 —
    // 점프로 넘긴 지면쓸기가 같이 눌러둔 패리의 공이 되어, 데모 10건 중 4건이
    // 엉뚱한 verb 로 기록됐고 parry_rate 까지 그 실패로 오염됐다.
    private double _dashStartedAt = double.NaN;

    private double _parryStartedAt = double.NaN;

    private double _jumpStartedAt = double.NaN;

    private int _dashDirection;

    public BattleSim(BattleSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        // 빈 명부는 여기서 막는다. 그대로 받으면 첫 패턴을 고를 때 Det.RollInt(n: 0) 이
        // ArgumentOutOfRangeException 으로 터진다 — 판이 한참 돈 뒤라, 무엇이 잘못됐는지가
        // 그 스택에 안 적힌다. 세울 때 거절하면 부른 자리가 그대로 남는다.
        if (setup.PatternIds.Count == 0)
        {
            throw new ArgumentException("패턴 명부가 비었다 — 한 판을 세울 수 없다", nameof(setup));
        }

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

    /// <summary>
    /// 지금부터 다음 active 판정까지 남은 시간(초). 패턴이 없거나 더 올 active 가 없으면 null.
    ///
    /// <para>
    /// <see cref="Boss.CurrentPattern"/> 만으로는 "패턴이 돈다" 는 것만 알지 "지금이 언제인가" 는 모른다 —
    /// 그것만 보고 반응하면 윈드업 내내 무작정 움직이게 된다. 봇이 판정 직전에 반응하려고 이것을 본다.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>이것은 완전 정보다.</b> 사람은 선딜 모션을 보고 반응하지 다음 판정까지 남은 초를
    /// 정확히 알지 못한다. 헤드리스 구동용 최소 봇에는 괜찮지만, <b>학습 데이터를 만드는 봇 함대는
    /// 반응 지연과 잡음을 반드시 넣어야 한다</b> — 안 그러면 망이 "초인이 어떻게 실패하는가" 를
    /// 배우고, 그건 사람에게 아무 의미가 없다.
    /// </para>
    /// </summary>
    public double? NextActiveIn
    {
        get
        {
            if (_runner is null || _current is null)
            {
                return null;
            }

            foreach (PatternStep step in _current.Timeline)
            {
                if (step.Kind == "active" && step.T > _runner.Elapsed)
                {
                    return step.T - _runner.Elapsed;
                }
            }

            return null;
        }
    }

    /// <summary>한 틱 민다. 판이 끝났으면 결과를, 아니면 null 을 돌려준다.</summary>
    public BattleOutcome? Tick(InputFrame input)
    {
        Ticks++;
        // 틱 시작의 접지 상태. "이번 틱에 땅에서 떨어졌는가" 는 이것과 비교해야만 알 수 있다 —
        // 공중에서 점프를 또 눌러도 Fall 이 물리적으로는 무시하지만, 그 입력만 보면 구별이 안 된다.
        bool wasGrounded = Fighter.Grounded;
        Fighter.Tick(input, Dt);
        Separate();
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

    /// <summary>
    /// 몸이 겹쳤을 때 파이터가 서야 할 x. <b>순수 함수라 테스트가 경계를 직접 못 박는다.</b>
    ///
    /// <para>
    /// 있던 쪽을 그대로 유지한다 — 반대로 넘기면 보스를 관통하는 순간이동이 된다.
    /// 정확히 겹친 자리(<c>fighterX == bossX</c>)는 매 틱 밀어내는 동안엔 나올 수 없지만,
    /// 나오더라도 <b>왼쪽으로 고정</b>한다. 부동소수 0 의 부호나 난수로 가르면 같은 시드가
    /// 다른 판을 내고 리플레이 골든이 재현되지 않는다.
    /// </para>
    /// </summary>
    public static double SeparatedX(double fighterX, double bossX, double minGap) =>
        bossX + ((fighterX > bossX ? 1 : -1) * minGap);

    /// <summary>
    /// 보스 반폭 + 파이터 반폭. 두 몸이 겹치지 않는 최소 중심 거리다.
    /// <b>수치를 손으로 안 적는다</b> — 캐릭터마다 반폭이 다르고(26~36) data/fighters.json 이 진실이다.
    /// </summary>
    private double MinGap => Boss.HalfWidth + Fighter.HalfWidth;

    /// <summary>
    /// 두 몸을 떼어 놓는다. 밀리는 쪽은 <b>파이터</b>다 — 보스는 4배 크고, 플레이어에게 밀리는
    /// 보스는 그림이 틀렸다.
    ///
    /// <para>
    /// 이게 없던 때 파이터는 보스 몸(반폭 120) 안에 섰고 데모 평균 교전거리가 85px 였다.
    /// 붙는 사람과 떨어지는 사람이 둘 다 ≈0 으로 수렴해 <c>distance_bias</c> 축이 상수였다 —
    /// 죽은 입력은 망의 용량만 먹고 아무것도 가르치지 않는다.
    /// </para>
    ///
    /// <para>
    /// 대시가 보스를 뚫고 나가던 것도 여기서 막힌다. 그건 <b>의도한 결과다</b> —
    /// "안으로 파고들기" 가 순간이동이 아니라 실제 자리 싸움이 되어야 그 판단이 축에 잡힌다.
    /// 무적은 위치가 아니라 행동 시계로 도므로 막혀도 그대로다.
    /// </para>
    ///
    /// <para>
    /// <b>지상에서만 민다.</b> 공중에서도 밀던 때는 보스가 붙으면 플레이어가 벽 쪽으로 밀리고
    /// 빠져나갈 길이 아예 없었다 — 할 수 있는 것이 없는 상태는 패턴을 읽는 게임이 아니다.
    /// 보스 키는 480px 이고 점프 정점은 176px 라, "넘어간다" 는 높이로 넘는 것이 아니라
    /// <b>공중에서 가로로 지나가는 것</b>이다. 2D 액션의 관례고, 지상 간격은 그대로라
    /// <c>distance_bias</c> 축이 재는 교전 거리는 한 px 도 안 바뀐다 —
    /// 공중 판정은 <c>DodgeEvent.Airborne</c> 이 따로 싣는다.
    /// </para>
    /// </summary>
    private void Separate()
    {
        if (!Fighter.Grounded)
        {
            return;
        }

        double minGap = MinGap;
        if (Math.Abs(Fighter.X - Boss.X) >= minGap)
        {
            return;
        }

        Fighter.PushOutTo(SeparatedX(Fighter.X, Boss.X, minGap));
    }

    /// <summary>보스: 쉬는 중이면 다가가고, 패턴 중이면 타임라인을 민다.</summary>
    private void AdvanceBoss()
    {
        if (_runner is null)
        {
            _gapLeft -= Dt;
            // 파이터의 중심이 아니라 **자기 쪽으로 minGap 떨어진 자리**를 목표로 한다.
            // 중심을 노리면 보스가 파이터를 그대로 걸어 지나가 몸이 겹친다.
            Boss.Approach(Fighter.X + ((Boss.X >= Fighter.X ? 1 : -1) * MinGap), Dt);
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
            Log.Debug("boss", () => $"pattern_end id={Boss.CurrentPattern} tick={Ticks}");
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
            // 간격을 되돌려 놓고 나간다. 안 그러면 _gapLeft 가 0 이하로 남아 다음 틱에도
            // 곧장 이 갈래로 떨어져, 유효한 id 가 뽑힐 때까지 매 틱 [E] 를 쏟는다 —
            // 헤드리스 판정이 읽는 로그가 그것으로 뒤덮인다.
            _gapLeft = Boss.PatternGap;
            Log.Error("boss", $"pattern_missing id={id}");
            return;
        }

        _current = def;
        _runner = new PatternRunner(def);
        Boss.CurrentPattern = id;
        Log.Debug("boss", () => $"pattern_begin id={id} pick={_picks} tick={Ticks}");
    }

    /// <summary>
    /// 이번 틱에 시작된 회피 행동의 시각을 그 수단의 칸에 적고, 끝난 수단의 칸은 지운다.
    /// <b>판정이 설 때 이것과의 차이가 타이밍 오차가 된다.</b>
    ///
    /// <para>
    /// 수단마다 칸이 따로다. 하나로 합치면 나중에 시작한 행동이 앞선 행동을 덮어써서,
    /// 정작 판정을 피하게 한 수단의 시각이 사라진다.
    /// </para>
    /// </summary>
    /// <param name="input">이번 틱의 입력. 점프가 눌렸는지를 본다.</param>
    /// <param name="wasGrounded">이번 틱이 시작될 때(<see cref="Fighter.Tick"/> 이전) 접지 상태.
    /// 점프 엣지 검출에 쓴다 — <see cref="Fighter.Grounded"/> 만 보면 "떨어진 순간"과
    /// "이미 공중인데 또 눌렀다"를 구별할 수 없다.</param>
    private void RememberDodgeStart(InputFrame input, bool wasGrounded)
    {
        double now = Ticks * Dt;

        // 시작한 행동은 **그 행동이 끝났을 때만** 지운다. Land 에서 지우면 대시 한 번의 무적이
        // 막은 연속타 중 첫 대가 기록을 소비해 버려 나머지가 "회피 수단 없음" 으로 기록된다 —
        // 근거가 없는 게 아니라 **잘못 붙는다.**
        if (Fighter.Action == FighterAction.Dash && Fighter.ActionElapsed <= Dt)
        {
            _dashStartedAt = now;
            // 보스 쪽으로 갔으면 안(+1), 반대면 밖(-1)
            _dashDirection = Math.Sign(Fighter.Facing * (Boss.X - Fighter.X)) >= 0 ? 1 : -1;
        }
        else if (Fighter.Action != FighterAction.Dash)
        {
            _dashStartedAt = double.NaN;
            _dashDirection = 0;
        }

        if (Fighter.Action == FighterAction.Parry && Fighter.ActionElapsed <= Dt)
        {
            _parryStartedAt = now;
        }
        else if (Fighter.Action != FighterAction.Parry)
        {
            _parryStartedAt = double.NaN;
        }

        if (input.Jump && wasGrounded && !Fighter.Grounded)
        {
            _jumpStartedAt = now;
        }
        else if (Fighter.Grounded)
        {
            _jumpStartedAt = double.NaN;
        }
    }

    /// <summary>
    /// 이 판정을 <b>무엇이</b> 그렇게 만들었나. 결과가 이미 답을 들고 있다 —
    /// 무적이 먹었으면 대시, 패리가 받았으면 패리, 높이가 어긋났으면 점프다.
    /// 그 순간 돌고 있던 행동으로 추측하지 않는다.
    /// </summary>
    private (DodgeVerb Verb, double StartedAt) Credit(HitVerdict verdict) => verdict switch
    {
        HitVerdict.Dodged => (DodgeVerb.Dash, _dashStartedAt),
        HitVerdict.Parried => (DodgeVerb.Parry, _parryStartedAt),

        // 높이로 빗나갔다. 점프 기록이 있으면 점프가 넘긴 것이고, 없으면 대공 판정 아래에
        // 그냥 서 있었던 것이다 — 후자를 점프로 세면 jump_reliance 가 **정반대 행동**으로 부푼다.
        HitVerdict.MissedByHeight => double.IsNaN(_jumpStartedAt)
            ? (DodgeVerb.None, double.NaN)
            : (DodgeVerb.Jump, _jumpStartedAt),

        // 거리로 빗나갔다. 행동이 아니라 서 있던 자리가 피하게 했으므로 타이밍이 없다.
        HitVerdict.MissedByRange => (DodgeVerb.Spacing, double.NaN),

        // 맞았다 — 무엇을 시도했다 실패했는지를 남긴다.
        _ => MostRecentAction(),
    };

    /// <summary>
    /// 지금 돌고 있는 회피 행동 중 <b>가장 늦게</b> 시작한 것. 맞은 판정에만 쓴다 —
    /// 겹쳐 있으면 그 판정을 겨냥한 쪽이 더 나중이다.
    /// 동시 시작은 대시 → 패리 → 점프 순으로 **고정**한다. 순서를 안 박아두면 같은 시드가
    /// 다른 라벨을 내 학습 데이터가 재현되지 않는다.
    /// </summary>
    private (DodgeVerb Verb, double StartedAt) MostRecentAction()
    {
        DodgeVerb verb = DodgeVerb.None;
        double at = double.NaN;

        if (!double.IsNaN(_dashStartedAt))
        {
            verb = DodgeVerb.Dash;
            at = _dashStartedAt;
        }

        if (!double.IsNaN(_parryStartedAt) && (double.IsNaN(at) || _parryStartedAt > at))
        {
            verb = DodgeVerb.Parry;
            at = _parryStartedAt;
        }

        if (!double.IsNaN(_jumpStartedAt) && (double.IsNaN(at) || _jumpStartedAt > at))
        {
            verb = DodgeVerb.Jump;
            at = _jumpStartedAt;
        }

        return (verb, at);
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
        (DodgeVerb verb, double startedAt) = Credit(verdict);
        double error = double.IsNaN(startedAt) ? 0 : startedAt - now;
        int direction = verb == DodgeVerb.Dash ? _dashDirection : 0;

        _events.Add(new DodgeEvent(
            PatternId: Boss.CurrentPattern ?? "?",
            Verb: verb,
            Verdict: verdict,
            TimingError: error,
            Direction: direction,
            Airborne: !Fighter.Grounded,
            Distance: Math.Abs(Fighter.X - Boss.X),
            GreedWindow: Fighter.Action == FighterAction.Attack,

            // 태그를 아는 것은 여기뿐이다. 의존도 축은 "고를 수 있었는데 그걸 골랐나" 라서
            // 이 셋이 없으면 만들어지지 않는다.
            DashAvailable: _current.Tags.DashWindow > 0,
            JumpAvailable: _current.Tags.Jumpable,
            ParryAvailable: _current.Tags.Parryable));

        // 지연 오버로드다. 이 줄은 **판정 하나마다** 나오고, 데이터 공장은 한 판에 10~150 판정을
        // 수백만 판 돌린다 — 즉시 오버로드면 LOG_LEVEL=off 여도 포맷 비용을 전부 낸다.
        Log.Info("dodge", () => $"pattern={Boss.CurrentPattern} verb={verb} verdict={verdict}"
            + $" err={error:0.000} dir={direction} air={!Fighter.Grounded} hp={Fighter.Health}");
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
            Log.Debug("strike", () => $"hit boss_hp={Boss.Health} gap={gap:0} tick={Ticks}");
        }

        _struckThisSwing = true;
    }

    private bool _struckThisSwing;
}
