using System;

namespace Overfit.Battle.Rules;

/// <summary>파이터가 지금 하는 것. 하나만 할 수 있다 — 행동 중에는 다른 행동을 못 시작한다.</summary>
public enum FighterAction
{
    Idle,
    Dash,

    /// <summary>
    /// 칼질 중이다 — 2연격의 몇 번째 칼인지는 <see cref="Fighter.ComboStep"/> 이 말한다 (설계 §5.1).
    /// <b>끝까지 커밋한다</b>: 도는 동안 가드 · 패리 · 대시 · 이동이 안 된다. 맞아도 안 끊긴다.
    /// </summary>
    Attack,

    /// <summary>
    /// 패리 — 누르면 0.333초 커밋이고 앞 0.133초만 받아친다 (설계 §5.3). 커밋 동안 가드 · 패리 · 대시 · 이동이 안 된다 —
    /// <b>받아쳤으면</b> 그 뒤의 J 만은 곧장 1타가 된다(되받아치기). 창 밖에서 맞으면 <b>그냥 맞는다</b> — 가드가 아니다.
    /// </summary>
    Parry,

    /// <summary>
    /// ↓ (또는 S) 를 누르고 있는 동안의 <b>가드</b> (설계 §5.2). 시간이 끝내지 않고 손가락이 끝낸다 — 놓는 틱에
    /// 풀린다. 땅에서만 서고, 커밋이 아니라 자세라 그 위에서 바로 공격 · 패리 · 대시로 넘어간다.
    /// </summary>
    Guard,
}

/// <summary>
/// 플레이어 상태 기계. <b>보스를 모른다</b> — 둘을 아는 것은 <see cref="BattleSim"/> 하나다.
/// 그래야 FighterActionTests 가 보스 없이 돈다.
/// </summary>
public sealed class Fighter
{
    private readonly FighterConfig _config;
    private readonly Arena _arena;

    private double _lockLeft;

    /// <summary>공중에서 대시를 이미 썼나. 착지하거나 패리를 성공하면 풀린다.</summary>
    private bool _airDashUsed;

    /// <summary>
    /// 지금 도는 칼질이 2연격의 몇 번째인가 (0 = 1타). <b>칼질이 끝나면 0 으로 돌아온다</b> — 안 지우면 다음에
    /// 누른 한 대가 2타로 시작한다.
    /// </summary>
    private int _step;

    /// <summary>
    /// 1타 도중 공격을 또 눌렀나 (설계 §5.1). 누른 순간 2타를 세우지 않고 <b>기억만</b> 한다 — 1타는 끝까지
    /// 커밋이고, 이어지는 것은 1타가 끝나는 그 틱이다.
    /// </summary>
    private bool _comboQueued;

    /// <summary>
    /// 지금 도는 패리가 받아쳤나 (<see cref="ParryPrecise"/>). 참이면 그 커밋 안의 J 가 곧장 1타다(<see cref="Begin"/> —
    /// 되받아치기). <b>새 행동이 시작될 때 지운다</b> — 안 지우면 한 번 받아친 뒤로는 헛친 패리도 J 를 받는다.
    /// 패리가 아닐 때의 값은 아무도 안 읽는다.
    /// </summary>
    private bool _parryLanded;

    public Fighter(FighterConfig config, Arena arena, double x)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(arena);
        _config = config;
        _arena = arena;
        X = x;
        Health = config.MaxHealth;
        Stamina = config.MaxStamina;
        Facing = 1;
    }

    public double X { get; private set; }

    public double Y { get; private set; }

    public double VelocityY { get; private set; }

    /// <summary>-1 왼쪽 · +1 오른쪽. 마지막으로 움직인 방향을 유지한다.</summary>
    public int Facing { get; private set; }

    public int Health { get; private set; }

    public double Stamina { get; private set; }

    public bool Grounded => Y <= 0;

    /// <summary>몸통 높이. 판정이 점프로 넘기는지 · 대공에 걸리는지를 이것으로 잰다.</summary>
    public double BodyHeight => _config.Height;

    /// <summary>몸 절반 폭. 보스가 두고 서는 간격을 <c>BattleSim</c> 이 이것으로 잰다 — 손으로 안 적는다.</summary>
    public double HalfWidth => _config.HalfWidth;

    /// <summary>
    /// 몸통 — 발 중심에서 좌우 반폭, 발바닥에서 키만큼 (월드). <b>판정은 이것과 모양을 겹쳐 본다</b>
    /// (이슈 #59 · 설계 §3.4). 옛 판정은 몸을 점(발 중심)으로 봤다.
    /// </summary>
    public HitRect Body => new(X - _config.HalfWidth, X + _config.HalfWidth, Y, Y + _config.Height);

    public FighterAction Action { get; private set; } = FighterAction.Idle;

    /// <summary>현재 행동이 시작된 뒤 흐른 시간. Idle 이면 0.</summary>
    public double ActionElapsed { get; private set; }

    /// <summary>
    /// 대시 무적 창 안인가. <b>이것은 캐릭터 쪽의 창일 뿐이다</b> — 실제로 판정을 피하는지는
    /// 패턴의 <c>dash_window</c> 와 견준 뒤에 정해진다 (<see cref="HitResolver"/>).
    /// 뷰가 "지금 무적 모션" 을 그리는 데 쓰라고 남긴다.
    /// </summary>
    public bool Invulnerable => Action == FighterAction.Dash && ActionElapsed < DashIFrames;

    /// <summary>
    /// 패리가 받아치는 창 안인가. <b>캐릭터 쪽의 창</b>이다 — 패리 불가 패턴이나 더 좁은 <c>parry_window</c> 를
    /// 가진 패턴 앞에서는 이것이 참이어도 못 받아친다(<see cref="HitResolver"/> 가 좁은 쪽을 쓴다).
    /// </summary>
    public bool Parrying => SinceParryPress < PreciseParryWindow;

    /// <summary>
    /// 가드인가 (설계 §5.2). ↓ 를 누르고 있는 동안 참이고, 놓는 틱에 거짓이다. 패리와 <b>다른 행동</b>이라 둘이
    /// 같은 틱에 참일 수 없다.
    /// </summary>
    public bool Guarding => Action == FighterAction.Guard;

    /// <summary>이 캐릭터의 대시 무적 폭(초). <see cref="HitResolver"/> 가 패턴의 창과 견준다.</summary>
    public double DashIFrames => _config.DashIFrames;

    /// <summary>패리의 창(초) — 데이터 그대로다. 연타 징벌이 깎던 때가 있었고 스펙이 그 징벌을 지웠다 (설계 §5.3).</summary>
    public double PreciseParryWindow => _config.ParryPreciseWindow;

    /// <summary>
    /// 지금 패리의 누름에서 흐른 시간(초). 패리 중이 아니면 무한대 — 패리는 커밋이라 창은 행동의 시계 그대로다.
    /// <see cref="HitResolver"/> 가 패턴의 창과 견준다.
    /// </summary>
    public double SinceParryPress => Action == FighterAction.Parry ? ActionElapsed : double.PositiveInfinity;

    /// <summary>가드가 깨져 굳어 있나. 움직이지도 뛰지도 새 행동을 시작하지도 못한다.</summary>
    public bool Locked => _lockLeft > 0;

    /// <summary>공중 대시를 이미 썼나. 착지 · 패리로 풀린다 (나인 솔즈의 보상 구조).</summary>
    public bool AirDashSpent => _airDashUsed;

    /// <summary>패리로 모은 기. 지금은 쓰는 곳이 없다 — 쓰임(스펙 7)이 생기면 그 비용이 데이터로 온다.</summary>
    public int Qi { get; private set; }

    /// <summary>공격 판정이 서 있는가. 선딜을 지나고 후딜 전 — 시간은 지금 칼질 칸의 것이다.</summary>
    public bool AttackActive => Action == FighterAction.Attack
        && ActionElapsed >= Step.Windup
        && ActionElapsed < Step.Windup + Step.Active;

    /// <summary>지금(또는 다음에 누르면) 휘두르는 칼질의 피해. 칸마다 데이터가 정한다 — 2타가 1타의 세 배다.</summary>
    public int AttackDamage => Step.Damage;

    /// <summary>
    /// 지금 도는 칼질이 몇 번째인가 (0 = 1타). 뷰가 어느 시트를 그릴지 · <see cref="BattleSim"/> 이 어느 칼 모양을
    /// 댈지를 이것으로 안다.
    /// </summary>
    public int ComboStep => _step;

    /// <summary>1타 도중 다음 칼을 눌러 두었나. 봇이 "이미 눌렀다" 를 안 되풀이하려고 본다.</summary>
    public bool ComboQueued => _comboQueued;

    /// <summary>지금 칼질의 한 칸 (<c>fighters.json</c> 의 <c>combo</c>).</summary>
    private ComboStepDef Step => _config.Combo[_step];

    public bool Alive => Health > 0;

    /// <summary>스태미나를 깎는다. 0 아래로는 안 내려간다.</summary>
    public void Spend(double amount) => Stamina = Math.Max(0, Stamina - amount);

    /// <summary>
    /// 맞았다. <b>칼질은 안 끊긴다</b> — 끝까지 커밋이다(설계 §5.1). 맞으면 끊기던 것은 차지였고, 차지는 없어졌다.
    /// </summary>
    public void TakeDamage(int amount) => Health = Math.Max(0, Health - amount);

    /// <summary>
    /// 이만한 피해를 가드로 받아내는 데 드는 스태미나. <b>피해에 비례한다</b> —
    /// 무거운 한 방이 가드를 깨는 것이 가드 퍼니쉬의 레버다 (이슈 #47).
    /// 판정기(<see cref="HitResolver"/>)가 이것과 남은 스태미나를 견줘 붕괴를 정한다.
    /// </summary>
    public double GuardStaminaCost(int fullDamage) => fullDamage * _config.GuardStaminaPerDamage;

    /// <summary>
    /// 가드가 받아냈다. 피해의 <c>guard_chip_ratio</c> 만 흘려 받고, 값은 <b>스태미나</b>로 낸다.
    /// <b>자세는 안 풀린다</b> — 놓을 때까지 버티는 것이 이 기술이다.
    ///
    /// <para>
    /// 반올림을 <see cref="MidpointRounding.AwayFromZero"/> 로 고정한다. 기본 반올림(짝수로)은
    /// 같은 비율이 홀짝에 따라 다른 규칙을 내서 리플레이가 재현되지 않는다.
    /// </para>
    /// </summary>
    /// <param name="fullDamage">막지 않았다면 받았을 피해.</param>
    public void GuardChip(int fullDamage)
    {
        Spend(GuardStaminaCost(fullDamage));

        int chip = (int)Math.Round(fullDamage * _config.GuardChipRatio, MidpointRounding.AwayFromZero);
        Health = Math.Max(0, Health - chip);
    }

    /// <summary>
    /// 가드가 <b>깨졌다.</b> 스태미나가 모자랐거나, 가드 불가 판정이 들어왔거나 —
    /// 어느 쪽이든 결과는 같다: <b>전액</b>을 맞고 <c>guard_break_lock</c> 동안 굳는다.
    /// 그 고정이 "남은 타격을 그대로 맞는 길이" 이고, 그게 가드를 고른 값이다.
    ///
    /// <para>
    /// <b>스태미나는 안 쓴다.</b> 값은 <b>막아낸 만큼</b>에 매기는 것인데 깨진 가드는 아무것도
    /// 안 막았다 — 대신 전액과 <c>guard_break_lock</c> 을 낸다. 여기서 또 깎으면 고갈로 깨진
    /// 사람이 값을 두 번 낸다.
    /// </para>
    /// </summary>
    /// <param name="fullDamage">막지 않았다면 받았을 피해. <b>그대로</b> 들어간다.</param>
    public void GuardBreak(int fullDamage)
    {
        Health = Math.Max(0, Health - fullDamage);
        _lockLeft = _config.GuardBreakLock;
        // 칼질 칸 · 눌러 둔 칼 · 받아친 표시를 여기서 안 지워도 된다: 붕괴는 가드 중에만 오고(HitResolver 의 GuardBroken 은
        // Guarding 을 본다), 가드에 들어선 그 틱에 Start 가 셋을 이미 지웠다. 가드가 아닌 곳에서 부르게 되면 여기서 지워야 한다.
        Action = FighterAction.Idle;
        ActionElapsed = 0;
    }

    /// <summary>
    /// 패리가 받아쳤다. 피해가 없고, 기가 오르고, <b>공중 대시가 즉시 돌아온다</b> — "잘 받아내면 다시 움직일 수
    /// 있다" 는 보상 구조가 패리를 쓰게 만든다(나인 솔즈). 보스를 굳히는 것은 여기가 아니다 — 판정과 보스를 둘 다
    /// 아는 곳은 <see cref="BattleSim"/> 하나다.
    ///
    /// <para>
    /// <b>커밋은 안 푼다</b> — 가드 · 패리 · 대시 · 이동은 커밋이 끝날 때까지 그대로 막힌다. 풀리는 것은 J 하나다:
    /// 이 뒤의 틱에 누른 J 는 곧장 1타가 된다(<see cref="Begin"/> — 되받아치기). <see cref="BattleSim"/> 은 이것을
    /// 파이터의 틱 <b>뒤</b> 판정에서 부르므로, 받아친 그 틱의 J 는 이미 지나갔고 되받아치기는 다음 틱부터다.
    /// </para>
    /// </summary>
    public void ParryPrecise()
    {
        Qi++;
        _airDashUsed = false;
        _parryLanded = true;
    }

    public void Tick(InputFrame input, double dt)
    {
        // Begin 을 Advance 보다 먼저 불러 행동이 시작된 틱도 경과 시간에 들어가게 한다 —
        // 안 그러면 시작 틱이 공짜가 되어 무적 창 · 패리 창 · 선딜 경계가 테스트 값보다 한 틱 늦게 닫힌다.
        Begin(input);
        Advance(dt);
        Move(input, dt);
        Fall(input, dt);
        Regen(dt);
    }

    /// <summary>진행 중인 행동의 시계를 밀고, 끝났으면 Idle 로 돌린다 — 칼질이면 눌러 둔 다음 칼로 잇는다.</summary>
    private void Advance(double dt)
    {
        // 붕괴의 고정은 **Idle 이어도 돈다** — 고정은 아예 Idle 상태에서 흐르므로 여기서 같이 멈추면 영영 안 풀린다.
        _lockLeft = Math.Max(0, _lockLeft - dt);

        if (Action == FighterAction.Idle)
        {
            return;
        }

        ActionElapsed += dt;

        // 가드는 시간이 안 끝낸다 — 손가락이 끝낸다(Begin 이 본다). 그래서 Duration 표에 자리가 없다.
        if (Action == FighterAction.Guard)
        {
            return;
        }

        if (ActionElapsed < Duration(Action))
        {
            return;
        }

        if (Action == FighterAction.Attack && Chain())
        {
            return;
        }

        Action = FighterAction.Idle;
        ActionElapsed = 0;
    }

    /// <summary>
    /// 칼질이 끝나는 틱 — 눌러 둔 다음 칼이 있으면 <b>그 틱에</b> 잇는다 (설계 §5.1: "1타가 끝나는 틱에 2타가
    /// 이어진다"). 값(<c>attack_cost</c>)은 이을 때 낸다: 누를 때 내면 1타가 끝나기 전에 스태미나가 바닥나도 2타가
    /// 선다. 모자라면 잇지 않고 선다. 이었으면 true 다.
    ///
    /// <para>
    /// 잇는 칼의 시계는 0 에서 시작한다. 이 틱은 앞 칼질의 마지막 틱이라 새 칼의 첫 틱은 다음 틱이다 —
    /// <see cref="Begin"/> 이 새 행동을 세운 틱을 경과 시간에 넣는 것과 한 틱 다르지만, 그 차이는 언제나 같아 박자가 고정이다.
    /// </para>
    /// </summary>
    private bool Chain()
    {
        bool chain = _comboQueued && _step + 1 < _config.Combo.Count && Stamina >= _config.AttackCost;
        _comboQueued = false;
        if (!chain)
        {
            _step = 0;
            return false;
        }

        Spend(_config.AttackCost);
        _step++;
        ActionElapsed = 0;
        return true;
    }

    /// <summary>행동이 저절로 끝나는 시각(초). 가드는 여기 없다 — 끝내는 것은 손가락이다.</summary>
    private double Duration(FighterAction action) => action switch
    {
        FighterAction.Dash => _config.DashDuration,
        FighterAction.Attack => Step.Windup + Step.Active + Step.Recover,
        FighterAction.Parry => _config.ParryDuration,
        _ => 0,
    };

    private double Cost(FighterAction action) => action switch
    {
        FighterAction.Dash => _config.DashCost,
        // 가드를 드는 값은 없다 (이 계획 · _note_guard) — 값은 막아낸 피해에 비례해 나간다(GuardChip).
        FighterAction.Parry => _config.ParryCost,
        FighterAction.Attack => _config.AttackCost,
        _ => 0,
    };

    /// <summary>
    /// 새 행동을 고른다. 커밋된 행동(대시 · 칼질 · 패리) 중이거나 굳었으면 입력을 버린다 — 공격 둘만 예외다: 칼질 중의
    /// 공격은 다음 칼로 기억하고, <b>받아친</b> 패리의 커밋 중의 공격은 곧장 1타를 세운다(되받아치기). 가드는 커밋이
    /// 아니라 <b>자세</b>라, 그 위에서 바로 다른 행동을 고른다(설계 §5.2).
    /// </summary>
    private void Begin(InputFrame input)
    {
        // 칼질 중에 또 누르면 다음 칼을 **기억만** 한다 (설계 §5.1). 1타는 끝까지 커밋이고, 이어지는 것은 1타가
        // 끝나는 틱이다(Chain). 다른 입력은 버린다 — 끝까지 커밋이다.
        if (Action == FighterAction.Attack)
        {
            if (input.Attack && _step + 1 < _config.Combo.Count)
            {
                _comboQueued = true;
            }

            return;
        }

        // 받아친 패리의 커밋 안에서는 J 하나만 받는다 — 되받아치기 (판정 13 · 설계 §4.3). 커밋이 막는 목록(설계 §1 ·
        // §5.1: 가드 · 패리 · 대시 · 이동)에 공격은 없고, §4.3 의 타임라인(받아치고 ~0.2초 반응 → 1타)은 이 J 를 커밋
        // 안에 떨어뜨린다: 받아치는 것이 창(0.133) 안이라 남은 커밋이 0.2초 넘게 있다. 2번 PR 의 계획은 이 J 까지 버렸다 —
        // 누른 J 가 아무 표시 없이 사라졌고, 데모(시드 51)의 봇은 마무리를 받아친 다음 틱부터 누른 J 를 커밋이 끝날 때까지
        // 14틱 내내 버렸다(최종 리뷰 I1). 받는 J 는 Idle 에서 누른 J 와 같다(Start · CanStart). 못 받아친 패리는 J 까지
        // 버린다 — 헛친 난사의 값은 커밋 전체다.
        if (Action == FighterAction.Parry)
        {
            if (_parryLanded && input.Attack && CanStart(FighterAction.Attack))
            {
                Start(FighterAction.Attack);
            }

            return;
        }

        if (Action is not (FighterAction.Idle or FighterAction.Guard) || Locked)
        {
            return;
        }

        FighterAction pressed = input.Dash ? FighterAction.Dash
            : input.Parry ? FighterAction.Parry
            : input.Attack ? FighterAction.Attack
            : FighterAction.Idle;

        // 못 하는 행동은 안 누른 것과 같다(스태미나 · 공중 대시 한 번). 누른 것이 없으면 남는 것은 ↓ 하나다 —
        // 누르고 있고 땅이면 가드, 아니면 선다 (설계 §5.2: 누르고 있는 동안 · 땅에서만).
        FighterAction wanted = pressed != FighterAction.Idle && CanStart(pressed) ? pressed
            : input.GuardHeld && Grounded ? FighterAction.Guard
            : FighterAction.Idle;

        if (wanted == Action)
        {
            return;
        }

        Start(wanted);
    }

    /// <summary>
    /// 행동을 세운다 — 값을 내고 시계를 0 에서 돌린다. 새 행동을 세우는 곳은 여기 하나다: 되받아치기도 이것을 타서
    /// Idle 에서 누른 J 와 한 글자도 안 다르다. 지난 행동의 칼질 칸 · 눌러 둔 칼 · 받아친 표시를 여기서 지운다.
    /// </summary>
    private void Start(FighterAction action)
    {
        Spend(Cost(action));
        Action = action;
        ActionElapsed = 0;
        _step = 0;
        _comboQueued = false;
        _parryLanded = false;

        if (action == FighterAction.Dash && !Grounded)
        {
            _airDashUsed = true;
        }
    }

    /// <summary>
    /// 이 행동을 지금 시작할 수 있나 — 스태미나가 되고, 대시면 공중 대시가 남아 있나.
    /// 공중 대시는 착지하거나 패리를 성공할 때까지 한 번뿐이다 (나인 솔즈) — 몸 충돌이 없어져 공중이 안전지대가 됐으므로,
    /// 무제한 공중 대시는 "공중에 떠서 계속 무적" 이라는 답 하나로 모든 패턴을 지운다.
    /// </summary>
    private bool CanStart(FighterAction action) =>
        Stamina >= Cost(action) && !(action == FighterAction.Dash && !Grounded && _airDashUsed);

    /// <summary>행동 중에는 회복하지 않는다 — 그래야 연속 행동에 값이 붙는다.</summary>
    private void Regen(double dt)
    {
        if (Action == FighterAction.Idle)
        {
            Stamina = Math.Min(_config.MaxStamina, Stamina + (_config.StaminaRegen * dt));
        }
    }

    private void Move(InputFrame input, double dt)
    {
        if (Locked)
        {
            return;
        }

        if (Action == FighterAction.Dash)
        {
            // 대시는 바라보는 쪽으로만 간다. 방향 입력을 안 받는다 — 시작 순간의 판단이 전부여야
            // dash_direction 축이 "어느 쪽으로 빠졌나"를 깨끗하게 잰다.
            X += Facing * _config.DashSpeed * dt;
        }
        else if (Action == FighterAction.Idle && input.Move != 0)
        {
            Facing = input.Move;
            X += input.Move * _config.MoveSpeed * dt;
        }

        X = Math.Clamp(X, _config.HalfWidth, _arena.Width - _config.HalfWidth);
    }

    private void Fall(InputFrame input, double dt)
    {
        // 점프는 땅에 있을 때만. 공중에서 또 눌러도 안 솟는다.
        if (input.Jump && Grounded && Action == FighterAction.Idle && !Locked)
        {
            VelocityY = _config.JumpVelocity;
        }

        VelocityY -= Arena.Gravity * dt;
        Y += VelocityY * dt;

        if (Y <= 0)
        {
            Y = 0;
            VelocityY = 0;
            _airDashUsed = false;
        }
    }
}
