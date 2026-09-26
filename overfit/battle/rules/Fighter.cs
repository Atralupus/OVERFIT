using System;

namespace Overfit.Battle.Rules;

/// <summary>파이터가 지금 하는 것. 하나만 할 수 있다 — 행동 중에는 다른 행동을 못 시작한다.</summary>
public enum FighterAction
{
    Idle,

    /// <summary>
    /// 대시 — <b>대시 뒤 경직</b>(<c>dash_recover</c> · #82)까지가 대시다. 경직 동안은 제자리에 선 채(<see cref="Fighter.Stiff"/>) 행동 ·
    /// 이동 · 점프 · 가드가 막히고, 그 사이 맞은 판정은 대시의 것이다(<see cref="DodgeCredit"/>).
    /// </summary>
    Dash,

    /// <summary>
    /// 칼질 중이다 — 2연격의 몇 번째 칼인지는 <see cref="Fighter.ComboStep"/> 이 말한다 (설계 §5.1).
    /// <b>끝까지 커밋한다</b>: 도는 동안 가드 · 패리 · 대시 · 이동이 안 된다. 맞아도 안 끊긴다. 이어지는 칼이 없으면 <b>칼질 뒤 경직</b>
    /// (<c>combo[].stiff</c> · #82)까지가 칼질이다 — 1타의 경직 중에 누른 J 만은 곧장 2타가 된다.
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
/// 플레이어 상태 기계. <b>보스를 모른다</b> — 보스의 판정을 이 몸에 대고 결과를 싣는 것은 <see cref="BossSwings"/>
/// (<see cref="BossSwings.Resolve"/>), 이 칼을 보스에 대는 것은 <see cref="BattleSim"/> 이다.
/// 그래야 FighterActionTests 가 보스 없이 돈다.
/// </summary>
public sealed class Fighter
{
    private readonly FighterConfig _config;
    private readonly Arena _arena;

    /// <summary>탈진의 길이(틱) — <c>exhaust_seconds</c> 를 세울 때 한 번 바꾼다(반올림은 <see cref="BattleSim.TicksFor"/> 한 곳).</summary>
    private readonly int _exhaustTicks;

    /// <summary>
    /// 남은 탈진 틱 (설계 §5.5). <b>틱으로 센다</b> — 1.1초 = 66틱 동안 정확히 아무것도 못 한다. 전에는 붕괴 고정을 초로 빼 가며
    /// 셌다(1.1 − 66 × 1/60 이 −9.5e−16 이라 66틱이었던 것은 우연이다).
    /// </summary>
    private int _exhaustLeft;

    /// <summary>
    /// 칼질 칸마다 그 칼질 뒤 경직의 길이(틱) — <c>combo[].stiff</c> 를 세울 때 한 번 바꾼다 (#82). <b>초를 더해 가며 세지 않는다</b>:
    /// 1/60 을 더해 가는 칼질의 시계는 1타(0.25초)를 15틱이 아니라 16틱에 끝낸다(15번 더한 값이 0.24999999999999997 이다). 경직까지
    /// 그렇게 세면 데이터의 0.40 이 몇 틱인지를 부동소수가 정한다.
    /// </summary>
    private readonly int[] _stiffTicks;

    /// <summary>대시 뒤 경직의 길이(틱) — <c>dash_recover</c> 를 세울 때 한 번 바꾼다 (#82).</summary>
    private readonly int _dashRecoverTicks;

    /// <summary>
    /// 남은 행동 뒤 경직 틱 (#82). 0 이 아니면 지금 행동(칼질 · 대시)은 제 시간을 다 돌았고 경직만 남았다. <b>행동은 그대로다</b> —
    /// <see cref="Action"/> 이 Attack · Dash 인 채라 커밋이 막던 것(행동 · 이동 · 점프 · 가드)이 그대로 막히고, 스태미나도 안 찬다
    /// (Idle 이 아니다). 경직의 마지막 틱에 행동이 끝난다(<see cref="End"/>).
    /// </summary>
    private int _stiffLeft;

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
        _exhaustTicks = BattleSim.TicksFor(config.ExhaustSeconds);
        _stiffTicks = new int[config.Combo.Count];
        for (int i = 0; i < _stiffTicks.Length; i++)
        {
            _stiffTicks[i] = StiffTicks(config.Combo[i].Stiff);
        }

        _dashRecoverTicks = StiffTicks(config.DashRecover);
    }

    /// <summary>
    /// 경직(초)을 틱으로. 반올림은 <see cref="BattleSim.TicksFor"/> 한 곳이다 — 다만 <b>0 은 0 틱</b>이다. TicksFor 는 0 이하를 한 틱으로
    /// 올리는데(판정 창 · 시각에는 0 틱이 없다), 경직의 0 은 "경직이 없다" 는 뜻이다: 손맛을 보며 데이터에서 끄는 자리다.
    /// </summary>
    private static int StiffTicks(double seconds) => seconds > 0 ? BattleSim.TicksFor(seconds) : 0;

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

    /// <summary>
    /// 탈진했나 (#71 · 설계 §5.5) — 스태미나를 다 썼거나(행동의 값 · 가드로 막다가) 가드가 깨졌다. 붕괴도 탈진이다.
    /// <c>exhaust_seconds</c> 동안 굳는다(<see cref="Locked"/>). 뷰가 take-hit 와 탈진 색을 이것으로 그린다.
    /// </summary>
    public bool Exhausted => _exhaustLeft > 0;

    /// <summary>
    /// 굳어 있나 — 행동 · 이동 · 점프 · 가드가 전부 막힌다. 지금 굳는 길은 탈진 하나다(<see cref="Exhausted"/>): 이 둘을 가르는 것은
    /// "왜 굳었나" 와 "무엇이 막히나" 가 다른 질문이라서다 — 규칙의 막음은 이것을 보고, 그림은 까닭(탈진)을 본다. 행동 뒤 경직
    /// (<see cref="Stiff"/> · #82)은 굳음이 아니다 — 행동이 아직 도는 것이라 행동의 커밋이 막는다.
    /// </summary>
    public bool Locked => Exhausted;

    /// <summary>
    /// 행동 뒤 경직 중인가 (#82) — 칼질(<c>combo[].stiff</c>)이나 대시(<c>dash_recover</c>)가 제 시간을 다 돌고 경직만 남았다. 행동은
    /// 그대로라(<see cref="Action"/>) 막는 것은 이것을 안 본다. 뷰가 칼질의 마지막 장 · 대시의 마지막 자세를 붙드는 데 쓴다.
    /// </summary>
    public bool Stiff => _stiffLeft > 0;

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

    /// <summary>지금 칼질이 보스의 경직 게이지를 채우는 양 (#71) — 2타가 1타보다 크다. 게이지를 채우는 것은 <c>BattleSim</c> 이다.</summary>
    public int AttackPoise => Step.Poise;

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

    /// <summary>스태미나를 깎는다. 0 아래로는 안 내려간다. <b>탈진을 부르지 않는다</b> — 0 에 닿은 행동이 끝나는 틱과 가드가 부른다.</summary>
    public void Spend(double amount) => Stamina = Math.Max(0, Stamina - amount);

    /// <summary>
    /// 이 행동을 해도 스태미나가 0 에 안 닿나 — 값보다 <b>많이</b> 있나. 규칙은 모자라도 마지막 한 번을 허락하지만(설계 §5.5 ·
    /// <see cref="CanStart"/>) 봇은 이것이 참일 때만 누른다(설계 §5.4) — 스스로 탈진하지 않는다. <c>≥</c> 가 아닌 이유: 값과 스태미나가
    /// 딱 같으면 0 에 닿아 탈진한다(#71 의 "스스로 탈진하지 않는다" 가 이긴다). 값이 없는 행동(가드 · 서기)은 늘 참이다.
    /// </summary>
    public bool Affords(FighterAction action) => Cost(action) <= 0 || Stamina > Cost(action);

    /// <summary>
    /// 맞았다. <b>칼질은 안 끊긴다</b> — 끝까지 커밋이다(설계 §5.1). 행동 뒤 경직(#82)도 안 끊긴다. 맞으면 끊기던 것은 차지였고, 차지는 없어졌다.
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
    /// <b>자세는 안 풀린다</b> — 놓을 때까지 버티는 것이 이 기술이다. 다만 값이 남은 스태미나와 <b>딱 같았으면</b> 막은 것이고
    /// (칩을 받는다) 다 썼으니 곧장 탈진한다 (#71 · 설계 §5.2 · §5.5) — 행동이 아니라서 기다릴 끝이 없다.
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
        if (Stamina <= 0)
        {
            Exhaust();
        }
    }

    /// <summary>
    /// 가드가 <b>깨졌다</b> — 스태미나가 모자랐다(깨지는 길은 그것 하나다 · 설계 §5.2).
    /// <b>전액</b>을 맞고 <b>탈진</b>한다 (#71 · 설계 §5.5 — 옛 <c>guard_break_lock</c> 의 고정을 넓힌 것이 파이터 탈진이다).
    /// 그 길이가 "남은 타격을 그대로 맞는 길이" 이고, 그게 가드를 고른 값이다.
    ///
    /// <para>
    /// <b>스태미나는 안 쓴다.</b> 값은 <b>막아낸 만큼</b>에 매기는 것인데 깨진 가드는 아무것도
    /// 안 막았다 — 대신 전액과 탈진을 낸다. 여기서 또 깎으면 고갈로 깨진 사람이 값을 두 번 낸다.
    /// </para>
    /// </summary>
    /// <param name="fullDamage">막지 않았다면 받았을 피해. <b>그대로</b> 들어간다.</param>
    public void GuardBreak(int fullDamage)
    {
        Health = Math.Max(0, Health - fullDamage);
        Exhaust();
    }

    /// <summary>
    /// 패리가 받아쳤다. 피해가 없고, 기가 오르고, <b>공중 대시가 즉시 돌아온다</b> — "잘 받아내면 다시 움직일 수
    /// 있다" 는 보상 구조가 패리를 쓰게 만든다(나인 솔즈). 보스를 무너뜨리는 것은 여기가 아니다 — 이것을 부르는
    /// <c>BossSwings.ApplyVerdict</c> 가 받아쳤다는 답을 <see cref="BossSwings.Resolve"/> 로 돌려주고, 그 답으로 탈진 루틴
    /// (<c>BattleSim.Exhaust</c> · 하나다)을 부르는 것은 <see cref="BattleSim"/> 이다(#72 · 설계 §4.3).
    ///
    /// <para>
    /// <b>커밋은 안 푼다</b> — 가드 · 패리 · 대시 · 이동은 커밋이 끝날 때까지 그대로 막힌다. 풀리는 것은 J 하나다:
    /// 이 뒤의 틱에 누른 J 는 곧장 1타가 된다(<see cref="Begin"/> — 되받아치기). 이것은 <see cref="BattleSim"/> 의 틱에서
    /// 파이터의 틱 <b>뒤</b>에 도는 보스 판정(<see cref="BossSwings.Resolve"/>)에서 불리므로, 받아친 그 틱의 J 는 이미 지나갔고
    /// 되받아치기는 다음 틱부터다.
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
        // 굳음은 틱 시작의 값으로 막는다 — Begin 이 그 값을 보고 Advance 가 그 뒤에 한 틱을 센다. Move · Fall 이 Advance 뒤의 값만
        // 보면 굳음의 마지막 틱(66번째)에 행동과 가드는 막혔는데 걷고 뛰었다(#71 계획 리뷰가 밟았다 — 옛 붕괴 고정도 같은 순서였다).
        // 이 틱에 든 굳음(끝나는 행동의 탈진)도 그 틱의 걸음부터 막는다.
        bool lockedAtStart = Locked;

        // Begin 을 Advance 보다 먼저 불러 행동이 시작된 틱도 경과 시간에 들어가게 한다 —
        // 안 그러면 시작 틱이 공짜가 되어 무적 창 · 패리 창 · 선딜 경계가 테스트 값보다 한 틱 늦게 닫힌다.
        Begin(input);
        Advance(dt);
        bool locked = lockedAtStart || Locked;
        Move(input, dt, locked);
        Fall(input, dt, locked);
        Regen(dt);
    }

    /// <summary>
    /// 진행 중인 행동의 시계를 밀고, 제 시간을 다 돌았으면 경직에 들이거나(#82) Idle 로 돌린다 — 칼질이면 눌러 둔 다음 칼로 잇는다.
    /// 경직은 틱으로 세고 마지막 틱에 행동이 끝난다(<see cref="End"/>).
    /// </summary>
    private void Advance(double dt)
    {
        // 탈진의 시계는 **Idle 이어도 돈다** — 탈진은 아예 Idle 상태에서 흐르므로 여기서 같이 멈추면 영영 안 풀린다.
        if (_exhaustLeft > 0)
        {
            _exhaustLeft--;
        }

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

        // 경직 중이다 — 행동의 시계(초)는 이미 다 돌았고 남은 것은 틱이다.
        if (_stiffLeft > 0)
        {
            _stiffLeft--;
            if (_stiffLeft == 0)
            {
                End();
            }

            return;
        }

        if (ActionElapsed < Duration(Action))
        {
            return;
        }

        // 칼질이 끝나는 틱 — 눌러 둔 다음 칼이 있으면 **그 틱에** 잇는다(경직은 안 붙는다 · 2타가 서는 틱은 경직이 없던 때와 같다).
        if (Action == FighterAction.Attack && _comboQueued && CanChain)
        {
            Chain();
            return;
        }

        // 제 시간을 다 돌았다 — 이을 것이 없으면 경직에 든다. 경직이 없는 행동(패리)이나 0 인 경직은 이 틱에 끝난다.
        _comboQueued = false;
        _stiffLeft = StiffOf(Action);
        if (_stiffLeft == 0)
        {
            End();
        }
    }

    /// <summary>
    /// 행동이 끝났다 — 경직이 있었으면 그 마지막 틱이다(#82). 칼질 칸과 눌러 둔 칼을 지운다 — 안 지우면 다음에 누른 한 대가 2타로
    /// 시작한다.
    ///
    /// <para>
    /// 행동의 값으로 0 이 됐으면 그 행동을 <b>끝까지</b> 한 뒤 — 끝나는 이 틱에 — 탈진한다(설계 §5.5 · 마지막 칼은 들어간다). "끝나는 틱"
    /// 은 <b>경직까지 끝나는 틱</b>이다: 경직 동안은 아직 그 행동이라 탈진이 안 든다. 끝나는 틱의 0 은 곧 그 행동의 값이 만든 0 이다 —
    /// 행동 중에는 경직까지 스태미나가 안 차고, 다른 값(가드의 칩)은 가드 중에만 나간다.
    /// </para>
    /// </summary>
    private void End()
    {
        Action = FighterAction.Idle;
        ActionElapsed = 0;
        _step = 0;
        _comboQueued = false;
        if (Stamina <= 0)
        {
            Exhaust();
        }
    }

    /// <summary>행동이 제 시간을 다 돈 뒤의 경직(틱) — 칼질은 그 칸의 것, 대시는 대시의 것, 패리는 없다(#82).</summary>
    private int StiffOf(FighterAction action) => action switch
    {
        FighterAction.Attack => _stiffTicks[_step],
        FighterAction.Dash => _dashRecoverTicks,
        _ => 0,
    };

    /// <summary>
    /// 탈진에 든다 (#71 · 설계 §5.5). 부르는 곳은 셋이다 — 행동의 값으로 0 이 된 행동이 경직까지 끝나는 틱(<see cref="End"/>) · 가드로
    /// 막다가 딱 0 이 된 칩(<see cref="GuardChip"/>) · 가드 붕괴(<see cref="GuardBreak"/>). 하던 것이 그 자리에서 끝나고 서서
    /// <c>exhaust_seconds</c> 를 보낸다. 지난 행동의 칼질 칸 · 경직 · 눌러 둔 칼 · 받아친 표시를 여기서 지운다: 새 행동을 세울 때
    /// (<see cref="Start"/>) 지우던 것인데, 탈진은 행동을 세우지 않고 끝내는 자리다.
    /// </summary>
    private void Exhaust()
    {
        _exhaustLeft = _exhaustTicks;
        Action = FighterAction.Idle;
        ActionElapsed = 0;
        _stiffLeft = 0;
        _step = 0;
        _comboQueued = false;
        _parryLanded = false;
    }

    /// <summary>
    /// 다음 칼을 이을 수 있나 — 다음 칸이 있고 스태미나가 <b>남아 있다</b>. 모자라도 잇고(마지막 한 번 · #71 · 설계 §5.5) <b>0 이면</b>
    /// 잇지 않고 선다 — 2번 PR 의 결정 4("모자라면 잇지 않는다")가 이것으로 바뀌었다.
    /// </summary>
    private bool CanChain => _step + 1 < _config.Combo.Count && Stamina > 0;

    /// <summary>
    /// 다음 칼을 잇는다 (설계 §5.1). 부르는 자리는 둘이다 — 칼질이 끝나는 틱에 눌러 둔 칼이 있을 때(<see cref="Advance"/>: "1타가 끝나는
    /// 틱에 2타가 이어진다")와 1타의 경직 중에 J 를 눌렀을 때(<see cref="Begin"/> · #82). 값(<c>attack_cost</c>)은 이을 때 낸다: 누를 때
    /// 내면 1타가 끝나기 전에 스태미나가 바닥나도 2타가 선다. 경직은 거기서 끝난다 — 이어 치는 사람은 서지 않는다.
    ///
    /// <para>
    /// 잇는 칼의 시계는 0 에서 시작한다. 끝나는 틱에 이은 칼은 그 틱이 앞 칼질의 마지막 틱이라 첫 틱이 다음 틱이고, 경직 중에 누른 칼은
    /// <see cref="Begin"/> 이 세운 틱이 첫 틱이다(Idle 에서 누른 J 와 같다) — 그래서 경직의 첫 틱에 누른 J 와 1타 도중 눌러 둔 J 는
    /// 같은 틱에 2타의 첫 틱을 연다.
    /// </para>
    /// </summary>
    private void Chain()
    {
        Spend(_config.AttackCost);
        _step++;
        ActionElapsed = 0;
        _stiffLeft = 0;
        _comboQueued = false;
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
    /// 새 행동을 고른다. 커밋된 행동(대시 · 칼질 · 패리 — 행동 뒤 경직까지) 중이거나 굳었으면 입력을 버린다 — 공격 셋만 예외다:
    /// 칼질 중의 공격은 다음 칼로 기억하고, 1타의 경직 중의 공격은 곧장 2타를 세우고(#82), <b>받아친</b> 패리의 커밋 중의 공격은
    /// 곧장 1타를 세운다(되받아치기). 가드는 커밋이 아니라 <b>자세</b>라, 그 위에서 바로 다른 행동을 고른다(설계 §5.2).
    /// </summary>
    private void Begin(InputFrame input)
    {
        // 칼질 중에 또 누르면 다음 칼을 **기억만** 한다 (설계 §5.1). 1타는 끝까지 커밋이고, 이어지는 것은 1타가
        // 끝나는 틱이다(Advance · Chain). 다른 입력은 버린다 — 경직까지 끝까지 커밋이다.
        //
        // 1타의 **경직 중에** 누른 J 는 기억하지 않고 **그 틱에** 2타를 세운다 (#82) — 잇는 창을 너그럽게 둔다: 1타가 끝난 뒤에 눌러도
        // 이어지고, 서는 것은 한 번만 치고 마는 사람뿐이다. 경직이 끝나기를 기다려 세우면 누른 J 가 경직만큼 늦게 나간다.
        if (Action == FighterAction.Attack)
        {
            if (input.Attack && _stiffLeft == 0 && _step + 1 < _config.Combo.Count)
            {
                _comboQueued = true;
            }
            else if (input.Attack && _stiffLeft > 0 && CanChain)
            {
                Chain();
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
    /// Idle 에서 누른 J 와 한 글자도 안 다르다. 지난 행동의 칼질 칸 · 경직 · 눌러 둔 칼 · 받아친 표시를 여기서 지운다.
    /// </summary>
    private void Start(FighterAction action)
    {
        Spend(Cost(action));
        Action = action;
        ActionElapsed = 0;
        _stiffLeft = 0;
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
    ///
    /// <para>
    /// <b>마지막 한 번은 할 수 있다</b> (#71 · 설계 §5.5 · 소울라이크). 값이 있는 행동은 스태미나가 <b>0 보다 많으면</b> 값보다 모자라도
    /// 시작하고, 값은 0 에서 멈춘다(<see cref="Spend"/>) — 그 행동이 경직까지 끝나는 틱에 탈진한다(<see cref="End"/>). 전에는 값이 모자라면
    /// 안 나가 행동으로는 0 에 닿지 않았다(100 − 14 × 7 = 2). 가드를 드는 것은 여전히 공짜다(값 0).
    /// </para>
    ///
    /// <para>
    /// 공중 대시는 착지하거나 패리를 성공할 때까지 한 번뿐이다 (나인 솔즈) — 몸 충돌이 없어져 공중이 안전지대가 됐으므로,
    /// 무제한 공중 대시는 "공중에 떠서 계속 무적" 이라는 답 하나로 모든 패턴을 지운다.
    /// </para>
    /// </summary>
    private bool CanStart(FighterAction action) =>
        (Cost(action) <= 0 || Stamina > 0) && !(action == FighterAction.Dash && !Grounded && _airDashUsed);

    /// <summary>
    /// 행동 중에는 회복하지 않는다 — 그래야 연속 행동에 값이 붙는다. <b>행동 뒤 경직도 행동이다</b>(#82 · Idle 이 아니다): 경직 동안 차면
    /// 0 에 닿은 칼질 · 대시가 경직 동안 차 올라 끝나는 틱에 탈진하지 않는다(<see cref="End"/>).
    /// </summary>
    private void Regen(double dt)
    {
        if (Action == FighterAction.Idle)
        {
            Stamina = Math.Min(_config.MaxStamina, Stamina + (_config.StaminaRegen * dt));
        }
    }

    private void Move(InputFrame input, double dt, bool locked)
    {
        if (locked)
        {
            return;
        }

        if (Action == FighterAction.Dash && _stiffLeft == 0)
        {
            // 대시는 바라보는 쪽으로만 간다. 방향 입력을 안 받는다 — 시작 순간의 판단이 전부여야
            // dash_direction 축이 "어느 쪽으로 빠졌나"를 깨끗하게 잰다. 대시 뒤 경직(#82)에는 안 간다 — 대시가 끝난 자리에 선다.
            // 경직 동안 흘러가면 경직이 대시의 사거리를 늘인다(보스 몸을 지나는 거리 · FighterDataTests).
            X += Facing * _config.DashSpeed * dt;
        }
        else if (Action == FighterAction.Idle && input.Move != 0)
        {
            Facing = input.Move;
            X += input.Move * _config.MoveSpeed * dt;
        }

        X = Math.Clamp(X, _config.HalfWidth, _arena.Width - _config.HalfWidth);
    }

    private void Fall(InputFrame input, double dt, bool locked)
    {
        // 점프는 땅에 있을 때만. 공중에서 또 눌러도 안 솟는다. 굳어 있어도 중력은 그대로다 — 막는 것은 뛰기뿐이다.
        if (input.Jump && Grounded && Action == FighterAction.Idle && !locked)
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
