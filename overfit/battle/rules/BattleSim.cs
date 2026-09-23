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

    /// <summary>
    /// 가드 자세가 선 시각(초, NaN = 지금 가드가 아니다) — 이슈 #47.
    /// 패리 칸과 <b>따로</b> 둔다. 가드에 들어가는 순간 누름 시계가 끝나므로(Fighter.EnterGuard)
    /// <c>_parryStartedAt</c> 은 곧 NaN 이 되고, 그러면 가드로 받은 판정의 TimingError 가 0 이 되어
    /// "아무것도 안 했다" 와 같은 점이 된다 — 부정확 패리에서 고쳤던 바로 그 붕괴다.
    /// </summary>
    private double _guardStartedAt = double.NaN;

    private int _dashDirection;

    /// <summary>
    /// 대시를 <b>시작하기 직전</b>의 교전 거리(px, NaN = 대시 중이 아니다).
    /// "이 거리를 만든 것이 대시인가" 를 판정마다 물어보는 반사실(counterfactual)이다 —
    /// 그 자리에서 판정이 닿았을 것이면 대시가 빼낸 것이고, 거기서도 안 닿았으면 간격이다.
    ///
    /// <para>
    /// 대시 중이라는 것만으로는 부족하다. 사거리 100 짜리 판정 앞에서 960px 떨어져 대시하면
    /// 대시는 돌지만 그 거리는 대시가 만든 것이 아니다 — 그것까지 대시의 공으로 돌리면
    /// <c>dash_timing_bias</c> 가 "판정을 피한 대시" 가 아닌 것들로 채워진다.
    /// </para>
    /// </summary>
    private double _dashStartDistance = double.NaN;

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

        // 보스는 파이터를 모른 채 태어난다 — 첫 프레임부터 맞으려면 여기서 한 번 맞춰야 한다.
        // 한 틱 뒤로 미루면 전투가 시작되는 그 그림에서 보스가 등을 보인다.
        Boss.Face(Fighter.X);
        _gapLeft = setup.Boss.PatternGap;
    }

    public Fighter Fighter { get; }

    public Boss Boss { get; }

    /// <summary>지금까지 진행한 틱 수.</summary>
    public int Ticks { get; private set; }

    /// <summary>이 판에서 일어난 회피 관측 전부. <see cref="PlayerAxes.From"/> 에 그대로 넣는다.</summary>
    public IReadOnlyList<DodgeEvent> Events => _events;

    /// <summary>
    /// 이 판에서 지나간 <b>헛스윙</b> 수 (이슈 #48). <b>관측이 아니다</b> — 판정이 없으므로
    /// <see cref="DodgeEvent"/> 도 없고 축도 안 움직인다.
    ///
    /// <para>
    /// 그런데도 세는 이유는 <b>화면</b> 때문이다. 안 보이는 헛스윙은 미끼가 아니라 그냥 빈 시간이고,
    /// 그러면 <c>III-역린</c> 은 아무도 안 무는 함정이 된다. 규칙 층은 뷰를 모르므로
    /// (콜백을 두면 헤드리스 봇이 그것을 들고 다닌다) 뷰가 <see cref="Events"/> 개수를 보는 것과
    /// 같은 규약으로 <b>값의 차이</b>를 읽게 한다.
    /// </para>
    /// </summary>
    public int Feints { get; private set; }

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
        // 틱 시작의 자리도 같이 잡아둔다. 대시가 시작된 틱에는 Fighter.Tick 이 이미 한 틱만큼
        // 밀어 놓은 뒤라, 여기서 안 잡으면 "대시 전에는 어디 서 있었나" 를 되돌릴 수 없다.
        double wasX = Fighter.X;
        Fighter.Tick(input, Dt);
        RememberDodgeStart(input, wasGrounded, wasX);
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
    /// 보스가 파이터에게서 두고 서는 간격. 보스 반폭 + 파이터 반폭이다.
    /// <b>벽이 아니다</b> — 파이터는 이 안으로 걸어 들어가고, 지나쳐 나간다(이슈 #27).
    /// 보스가 이 거리를 목표로 서는 이유는 <b>파이터 중심을 목표로 걸으면 둘이 완전히 겹쳐</b>
    /// 교전 거리가 늘 0 으로 수렴하기 때문이다 — 그림도 틀리고 <c>distance_bias</c> 도 상수가 된다.
    /// <b>수치를 손으로 안 적는다</b> — 캐릭터마다 반폭이 다르고(26~36) data/fighters.json 이 진실이다.
    /// </summary>
    private double Standoff => Boss.HalfWidth + Fighter.HalfWidth;

    /// <summary>보스: 굳었으면 아무것도 안 하고, 쉬는 중이면 다가가고, 패턴 중이면 타임라인을 민다.</summary>
    private void AdvanceBoss()
    {
        // 경직 시계만은 굳어 있어도 돈다 — 아니면 안 풀린다.
        Boss.Tick(Dt);
        if (Boss.Staggered)
        {
            return;
        }

        if (_runner is null)
        {
            _gapLeft -= Dt;

            // 방향은 **쉬는 동안에만** 바꾼다. 여기 두는 것 자체가 잠금의 절반이고
            // (나머지 절반은 Boss.Face 안의 가드다), 그래서 패턴이 서는 순간의 방향이
            // 그 패턴이 끝날 때까지 그대로 간다 — 예고가 거짓말이 되지 않는다.
            // **다가가는 자리와 무관하게 파이터 중심을 본다** — Standoff 는 서는 자리지 보는 곳이 아니다.
            int was = Boss.Facing;
            Boss.Face(Fighter.X);
            if (Boss.Facing != was)
            {
                Log.Debug("boss", () => $"turn facing={Boss.Facing} tick={Ticks}");
            }

            // 파이터의 중심이 아니라 **자기 쪽으로 Standoff 떨어진 자리**를 목표로 한다.
            // 중심을 노리면 보스가 파이터 위에 정확히 겹쳐 서서 교전 거리가 늘 0 이 된다.
            Boss.Approach(Fighter.X + ((Boss.X >= Fighter.X ? 1 : -1) * Standoff), Dt);
            if (_gapLeft <= 0)
            {
                Begin();
            }

            return;
        }

        // 헛스윙은 러너가 누적으로 센다 (이슈 #48). 차이를 여기서 옮기는 것은 판이 패턴을
        // 여러 번 돌기 때문이다 — 러너는 패턴마다 새로 서므로 그 값은 이번 패턴의 것뿐이다.
        int feintsBefore = _runner.Feints;
        foreach (HitBox box in _runner.Tick(Dt))
        {
            Land(box);
        }

        if (_runner.Feints > feintsBefore)
        {
            Feints += _runner.Feints - feintsBefore;
            Log.Debug("boss", () => $"feint id={Boss.CurrentPattern} n={Feints} tick={Ticks}");
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
    /// <param name="wasX">이번 틱이 시작될 때(<see cref="Fighter.Tick"/> 이전) 파이터의 자리.
    /// 대시가 시작된 틱에는 이미 한 틱을 이동한 뒤라, 대시 <b>전</b>의 거리는 이것으로만 잡힌다.</param>
    private void RememberDodgeStart(InputFrame input, bool wasGrounded, double wasX)
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
            // 보스는 아직 이번 틱을 안 밀었으므로(AdvanceBoss 는 뒤에 온다) 둘 다 틱 시작의 자리다.
            _dashStartDistance = Math.Abs(wasX - Boss.X);
        }
        else if (Fighter.Action != FighterAction.Dash)
        {
            _dashStartedAt = double.NaN;
            _dashDirection = 0;
            _dashStartDistance = double.NaN;
        }

        // 패리 칸은 **행동이 아니라 누름**을 따라 산다. 부정확 창(0.5초)이 패리 행동(0.30초)보다
        // 길어서, 행동이 끝날 때 지우면 늦게 누른 패리가 판정을 받아낸 바로 그 순간에
        // 시작 시각이 사라진다 — TimingError 가 0 이 되어 "아무것도 안 했다" 와 같은 점이 된다.
        // 그 붕괴를 없애려고 만든 것이 부정확 단계인데, 그러면 아무것도 안 고친 셈이 된다.
        if (Fighter.SinceParryPress <= Dt)
        {
            _parryStartedAt = now;
        }
        else if (Fighter.SinceParryPress > Fighter.ImpreciseParryWindow)
        {
            _parryStartedAt = double.NaN;
        }

        // 가드는 **자세**라 누름이 아니라 그 자세가 선 순간을 잡는다. 서 있는 동안 계속 살아 있고
        // (연속타를 여러 대 받아내므로 한 대가 기록을 소비하면 안 된다), 풀리면 지워진다.
        if (Fighter.Guarding)
        {
            if (double.IsNaN(_guardStartedAt))
            {
                _guardStartedAt = now;
            }
        }
        else
        {
            _guardStartedAt = double.NaN;
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
    private (DodgeVerb Verb, double StartedAt) Credit(HitVerdict verdict, HitBox box) => verdict switch
    {
        HitVerdict.Dodged => (DodgeVerb.Dash, _dashStartedAt),

        // 정확이든 부정확이든 **받아낸 것은 패리다.** 둘의 차이는 verb 가 아니라 판정(Verdict)이
        // 나른다 — verb 를 갈라 놓으면 parry_reliance("다른 수단이 있는데 패리를 골랐나")가
        // 늦게 누른 패리를 "패리를 안 골랐다" 로 세게 된다. 고른 것은 같고 결과가 다르다.
        HitVerdict.Parried or HitVerdict.ParriedLate => (DodgeVerb.Parry, _parryStartedAt),

        // 막아냈든 깨졌든 **고른 것은 가드**다 (이슈 #47) — 위와 같은 규약이고, 둘의 차이는
        // verb 가 아니라 Verdict 가 나른다. 시각은 가드가 **선** 순간이다: 누름 시각이 아니라
        // 자세가 선 시각이라야 "얼마나 오래 버티고 있었나" 가 오차로 실린다.
        HitVerdict.Guarded or HitVerdict.GuardBroken => (DodgeVerb.Guard, _guardStartedAt),

        // 높이로 빗나갔다. 점프 기록이 있으면 점프가 넘긴 것이고, 없으면 대공 판정 아래에
        // 그냥 서 있었던 것이다 — 후자를 점프로 세면 jump_reliance 가 **정반대 행동**으로 부푼다.
        HitVerdict.MissedByHeight => double.IsNaN(_jumpStartedAt)
            ? (DodgeVerb.None, double.NaN)
            : (DodgeVerb.Jump, _jumpStartedAt),

        // 거리로 빗나갔다 — 안이든 밖이든. 서 있던 자리가 피하게 했으면 간격이지만,
        // **그 자리를 대시가 만들었으면 대시다** (이슈 #46).
        HitVerdict.MissedTooFar or HitVerdict.MissedTooClose => CreditDistance(box),

        // 맞았다 — 무엇을 시도했다 실패했는지를 남긴다.
        _ => MostRecentAction(),
    };

    /// <summary>
    /// 거리로 빗나간 판정의 공을 <b>대시</b>와 <b>간격</b> 중 어디로 돌릴 것인가 (이슈 #46).
    ///
    /// <para>
    /// 고치기 전에는 무조건 간격이었다. 판정 순서가 거리 → 높이 → 대시무적이라 대시로 사거리를
    /// 벗어나면 무적이 보이기도 전에 거리에서 빠지는데, 그것을 전부 <c>Spacing</c> 으로 적고 있었다:
    /// 무적 8틱 · 대시 36.67px/틱 · 서는 자리 115 에서 밖으로 나가면 250 을 4틱째 넘으므로
    /// <b>무적 8틱 중 3틱만 <c>Dodged</c></b> 이었다. <b>대시 의존자가 간격 의존자로 기록된다</b> —
    /// 그 둘은 봉인할 것이 정반대라 2단계가 정확히 반대 변종을 뽑는다.
    /// </para>
    ///
    /// <para>
    /// 조건은 "대시 중" 이 아니라 <b>"대시 시작 자리에서는 닿았는가"</b> 다. 대시가 돌기만 하면
    /// 공을 주면, 애초에 사거리 밖에 서 있다 대시한 것까지 대시의 공이 되어
    /// <c>dash_timing_bias</c> 가 판정과 무관한 대시들로 채워진다.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>경계는 대시 행동이 끝나는 자리다.</b> 무적(0.14초 = 8틱)이 대시(0.18초 = 11틱)보다
    /// 짧으므로 무적 창은 통째로 대시의 공이 되지만, 대시가 끝난 뒤에도 사거리 밖에 남아 있는 것은
    /// <b>그 자리에 서 있기로 한 것</b>이라 간격이다. 유예 창(대시 종료 후 N초까지는 대시의 공)을
    /// 두는 쪽도 생각했고, 되돌리지 않기 위해 왜 안 두는지를 적어 둔다.
    /// </para>
    ///
    /// <para>
    /// <b>공에는 시각이 따라붙기 때문이다.</b> 이 함수가 돌려주는 시작 시각이 곧
    /// <c>TimingError</c> 이고 그것이 <c>dash_timing_bias</c> · <c>dash_timing_var</c> 의 표본이다.
    /// 연속타의 2타는 1타에서 0.35초 뒤에 서는데, 1타를 겨냥해 뛴 대시를 2타의 공으로도 돌리면
    /// <b>-0.35초짜리 표본</b>이 하나 생긴다 — "이 사람은 판정 0.35초 전에 뛴다" 는 그가 한 적 없는 말이고,
    /// 표본이 늘수록 축은 "늘 일찍 누른다" 쪽으로 끌려간다. 대시가 <b>어느 판정을 겨냥했나</b>가
    /// 성립하는 구간이 딱 행동이 도는 동안이라, 거기를 경계로 삼는다.
    /// 덤으로 데이터에 없는 수치("얼마나 오래 봐주나")를 새로 만들지 않아도 된다.
    /// </para>
    /// </summary>
    private (DodgeVerb Verb, double StartedAt) CreditDistance(HitBox box) =>
        !double.IsNaN(_dashStartedAt)
        && _dashStartDistance >= box.MinDistance
        && _dashStartDistance <= box.MaxDistance
            ? (DodgeVerb.Dash, _dashStartedAt)
            : (DodgeVerb.Spacing, double.NaN);

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
        switch (verdict)
        {
            case HitVerdict.Hit:
                Fighter.TakeDamage(box.Damage);
                break;

            case HitVerdict.Parried:
                // 보스를 굳히는 것은 여기다 — 파이터는 보스를 모른다.
                // **가드 불가를 받아치면 더 오래 굳는다** (이슈 #47): 최대 차지 한 번이 들어가는
                // 길이이고, 그 상이 "가드 불가는 받아쳐라" 를 말이 되게 한다.
                Fighter.ParryPrecise();
                Boss.Stagger(box.GuardBreak);
                break;

            case HitVerdict.ParriedLate:
                Fighter.ParryImprecise(box.Damage);
                break;

            case HitVerdict.Guarded:
                Fighter.GuardChip(box.Damage);
                break;

            case HitVerdict.GuardBroken:
                Fighter.GuardBreak(box.Damage);
                break;

            default:
                break;
        }

        double now = Ticks * Dt;
        (DodgeVerb verb, double startedAt) = Credit(verdict, box);
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
            // 모으고 선 것도 욕심이다 (이슈 #40). 차지는 휘두르는 0.5초가 아니라 최대 2.08초를
            // 무방비로 서 있는 것이라, 여기서 빼면 축이 가장 크게 건 순간에만 눈을 감는다.
            GreedWindow: Fighter.Action is FighterAction.Attack or FighterAction.Charge,
            ChargeTier: Fighter.ChargeTier,

            // 태그를 아는 것은 여기뿐이다. 의존도 축은 "고를 수 있었는데 그걸 골랐나" 라서
            // 이 셋이 없으면 만들어지지 않는다.
            DashAvailable: _current.Tags.DashWindow > 0,
            JumpAvailable: _current.Tags.Jumpable,
            ParryAvailable: _current.Tags.Parryable));

        // 지연 오버로드다. 이 줄은 **판정 하나마다** 나오고, 데이터 공장은 한 판에 10~150 판정을
        // 수백만 판 돌린다 — 즉시 오버로드면 LOG_LEVEL=off 여도 포맷 비용을 전부 낸다.
        // qi 를 같이 찍는다. 정확·부정확이 둘 다 기를 주므로 이 줄만 보고 "받아냈나" 를 셀 수 있고,
        // 내상은 hp 에 이미 반영돼 있어 두 줄을 견주면 얼마를 흘렸는지가 나온다.
        // dist 를 뺐던 때는 이 줄만으로 verb 를 검산할 수 없었다 — "거리로 빗나갔다" 가 맞는 말인지
        // 보려면 그 순간의 거리가 있어야 하고, 잘못 붙은 verb 를 잡아낸 방법이 정확히 그 검산이다.
        Log.Info("dodge", () => $"pattern={Boss.CurrentPattern} verb={verb} verdict={verdict}"
            + $" err={error:0.000} dir={direction} air={!Fighter.Grounded}"
            + $" dist={Math.Abs(Fighter.X - Boss.X):0} hp={Fighter.Health} qi={Fighter.Qi}"
            // stam 을 같이 찍는다 (이슈 #47). 가드의 값은 체력이 아니라 스태미나로 나가므로,
            // 이 칸이 없으면 로그만 보고 "왜 깨졌나" 를 못 읽는다 — 붕괴는 남은 값이 모자란 것이다.
            + $" stam={Fighter.Stamina:0}"
            + $" charge={Fighter.ChargeTier}");
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
            // 피해에는 차지 배수가 이미 들어 있다 (Fighter.AttackDamage). 여기서 곱하면
            // 곱셈이 두 곳이 되고, 그중 하나만 고치는 날이 온다.
            int damage = Fighter.AttackDamage;
            Boss.TakeDamage(damage);
            Log.Debug("strike", () =>
                $"hit boss_hp={Boss.Health} dmg={damage} charge={Fighter.ChargeTier} gap={gap:0} tick={Ticks}");
        }

        _struckThisSwing = true;
    }

    private bool _struckThisSwing;
}
