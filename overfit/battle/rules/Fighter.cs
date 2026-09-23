using System;

namespace Overfit.Battle.Rules;

/// <summary>파이터가 지금 하는 것. 하나만 할 수 있다 — 행동 중에는 다른 행동을 못 시작한다.</summary>
public enum FighterAction
{
    Idle,
    Dash,
    Parry,
    Attack,

    /// <summary>
    /// 공격 키를 누른 채 모으고 있다 (이슈 #40). <b>다른 행동과 달리 시간이 안 끝낸다</b> —
    /// 손가락을 떼는 것이 끝이고, 그 순간 <see cref="Attack"/> 으로 넘어간다.
    /// </summary>
    Charge,
}

/// <summary>
/// 플레이어 상태 기계. <b>보스를 모른다</b> — 둘을 아는 것은 <c>BattleSim</c>(아직 없음, Task 5) 하나다.
/// 그래야 이 테스트가 보스 없이 돈다.
/// </summary>
public sealed class Fighter
{
    private readonly FighterConfig _config;
    private readonly Arena _arena;

    /// <summary>
    /// 마지막 패리 <b>누름</b>에서 흐른 시간(초). 무한대면 아직 한 번도 안 눌렀다.
    ///
    /// <para>
    /// 행동(<see cref="FighterAction.Parry"/>)의 시계와 <b>따로 둔다.</b> 부정확 창(0.5초)이
    /// 패리 행동(0.26~0.34초)보다 길어서, 행동이 끝났다고 이 시계를 멈추면 창의 뒷부분이
    /// 통째로 사라진다 — 그 뒷부분이 정확히 "늦게 눌렀다" 를 "아무것도 안 했다" 와 가르는 자리다.
    /// </para>
    /// </summary>
    private double _sinceParryPress = double.PositiveInfinity;

    /// <summary>연타 사슬의 길이. 앞 누름의 부정확 창 안에서 또 누르면 자란다.</summary>
    private int _parryChain;

    private double _lockLeft;

    /// <summary>공중에서 대시를 이미 썼나. 착지하거나 정확 패리를 성공하면 풀린다.</summary>
    private bool _airDashUsed;

    /// <summary>
    /// 모으고 있는 차지 / 돌고 있는 스윙의 단계. <b>둘을 한 칸에 둔다</b> — 재는 것이 같은 것
    /// ("이 칼질을 얼마나 모았나")이고, 나누면 판정이 서는 순간 계측이 어느 칸을 봐야 하는지가
    /// 상태에 따라 갈린다. 모으는 동안은 매 틱 다시 계산되고, 놓는 순간 그 값으로 굳는다.
    /// </summary>
    private int _tier;

    /// <summary>
    /// <b>이번</b> 칼질의 남은 선딜(초). 그냥 누르면 데이터의 선딜 그대로이고, 붙들고 있었으면
    /// 그만큼 깎여 0 이 된다 — <b>붙드는 것이 곧 선딜이기 때문이다</b> (이슈 #40).
    ///
    /// <para>
    /// 설정값(<c>attack_windup</c>)을 그대로 안 읽고 칼질마다 들고 있는 이유가 여기다. 그림이 먼저
    /// 그렇게 말하고 있었다: 차지 자세는 attack 시트의 <b>선딜 마지막 장</b>(칼을 끝까지 뒤로 뺀 그림)이라,
    /// 놓은 뒤에 선딜을 처음부터 또 기다리면 같은 동작을 두 번 감는 셈이다. 규칙으로도 그 편이 옳다 —
    /// 그래야 2초 차지가 칼 닿기까지 2.4166초가 아니라 <b>2.0833초</b>가 되어 백장의 빈 시간
    /// (1.90~2.40초)에 실제로 들어간다.
    /// </para>
    /// </summary>
    private double _windup;

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
    /// <b>정확</b> 패리가 막아주는 창 안인가. 위와 같이 <b>캐릭터 쪽의 창</b>이다 — 패리 불가 패턴이나
    /// 더 좁은 <c>parry_window</c> 를 가진 패턴 앞에서는 이것이 참이어도 못 막는다.
    /// 부정확 창은 행동보다 오래 살아서 이 값으로는 안 보인다 (<see cref="SinceParryPress"/>).
    /// </summary>
    public bool Parrying => _sinceParryPress < PreciseParryWindow;

    /// <summary>이 캐릭터의 대시 무적 폭(초). <see cref="HitResolver"/> 가 패턴의 창과 견준다.</summary>
    public double DashIFrames => _config.DashIFrames;

    /// <summary>
    /// <b>지금</b> 유효한 정확 패리 창(초). 데이터의 값 그대로가 아니라 <b>연타 징벌이 깎은 뒤</b>다 —
    /// 두 번째 연타는 좁은 창, 세 번째부터는 0(정확 패리 불가, 부정확만)이다.
    /// <see cref="HitResolver"/> 가 패턴의 창과 견줘 좁은 쪽을 쓴다.
    /// </summary>
    public double PreciseParryWindow => _parryChain switch
    {
        <= 1 => _config.ParryPreciseWindow,
        2 => _config.ParrySpamWindow,
        _ => 0,
    };

    /// <summary>부정확 패리 창(초). 연타 징벌이 안 깎는다 — 난사의 대가는 <b>정확</b>을 잃는 것이다.</summary>
    public double ImpreciseParryWindow => _config.ParryImpreciseWindow;

    /// <summary>마지막 패리 누름에서 흐른 시간(초). 아직 안 눌렀으면 무한대.</summary>
    public double SinceParryPress => _sinceParryPress;

    /// <summary>지금 이어지고 있는 연타의 길이. 1 이면 깨끗한 한 번이다.</summary>
    public int ParryChain => _parryChain;

    /// <summary>부정확 패리에 굳어 있나. 움직이지도 뛰지도 새 행동을 시작하지도 못한다.</summary>
    public bool Locked => _lockLeft > 0;

    /// <summary>공중 대시를 이미 썼나. 착지 · 정확 패리로 풀린다 (나인 솔즈의 보상 구조).</summary>
    public bool AirDashSpent => _airDashUsed;

    /// <summary>패리로 모은 기. 지금은 쓰는 곳이 없다 — 쓰임(스펙 7)이 생기면 그 비용이 데이터로 온다.</summary>
    public int Qi { get; private set; }

    /// <summary>부정확 패리로 받은 내상의 합. 체력에서 이미 빠져 있고, 여기 따로 세는 것은 계측용이다.</summary>
    public int InternalDamage { get; private set; }

    /// <summary>공격 판정이 서 있는가. 선딜을 지나고 후딜 전.</summary>
    public bool AttackActive => Action == FighterAction.Attack
        && ActionElapsed >= _windup
        && ActionElapsed < _windup + _config.AttackActive;

    public double AttackReach => _config.AttackReach;

    /// <summary>
    /// 이 칼질의 피해. <b>차지 단계의 배수가 이미 곱해져 있다</b> — 곱셈을 부르는 쪽에 두면
    /// 때리는 자리마다 사본이 생기고, 그중 하나를 고치면 조용히 갈린다.
    ///
    /// <para>
    /// 반올림을 <see cref="MidpointRounding.AwayFromZero"/> 로 고정한다. 기본 반올림(짝수로)은
    /// 같은 비율이 홀짝에 따라 다른 규칙을 내서 리플레이가 재현되지 않는다 —
    /// <see cref="ParryImprecise"/> 의 내상과 같은 이유다.
    /// </para>
    /// </summary>
    public int AttackDamage =>
        (int)Math.Round(_config.AttackDamage * _config.ChargeTiers[_tier].DamageMultiplier, MidpointRounding.AwayFromZero);

    /// <summary>지금 모으고 있나.</summary>
    public bool Charging => Action == FighterAction.Charge;

    /// <summary>모은 시간(초). 모으는 중이 아니면 0.</summary>
    public double ChargeSeconds => Charging ? ActionElapsed : 0;

    /// <summary>
    /// 모으고 있는 차지 / 돌고 있는 스윙의 단계 (0 = 안 모았다). 배수는 이 번호가 정한다.
    /// 계측(<see cref="DodgeEvent.ChargeTier"/>)과 뷰가 같은 값을 본다.
    /// </summary>
    public int ChargeTier => _tier;

    /// <summary>차지 단계의 수. 봇이 단계를 고를 때 쓴다 — 수치를 봇 쪽에 베끼지 않는다.</summary>
    public int ChargeTierCount => _config.ChargeTiers.Count;

    /// <summary>그 단계에 닿는 데 걸리는 시간(초). 범위를 벗어난 번호는 양 끝으로 접는다.</summary>
    public double ChargeTierSeconds(int tier) =>
        _config.ChargeTiers[Math.Clamp(tier, 0, _config.ChargeTiers.Count - 1)].Seconds;

    /// <summary>
    /// <b>지금</b> 휘두르면 칼이 닿기까지 걸리는 시간(초) — 남은 선딜 + 판정이다.
    /// 모으고 있으면 붙든 만큼 선딜이 이미 지났으므로 <b>짧아진다</b>.
    /// 봇이 "지금 놓아도 판정 전에 닿나" 를 이것으로 잰다 — 설정값을 그대로 돌려주면
    /// 봇은 실제보다 최대 0.33초 일찍 손을 놓아, 닿을 수 있는 차지를 스스로 버린다.
    /// </summary>
    public double AttackLead =>
        Math.Max(0, _config.AttackWindup - ChargeSeconds) + _config.AttackActive;

    /// <summary>최대 차지 시간(초). <b>마지막 단계의 시간이 곧 그것</b>이라 데이터에 따로 없다.</summary>
    public double ChargeMaxSeconds => _config.ChargeTiers[^1].Seconds;

    /// <summary>
    /// 모은 진행도 0~1. <b>뷰가 자기 시계로 재게 하지 않는다</b> — 그러면 최대 시간(캐릭터마다 다르다)의
    /// 사본이 뷰에 생기고, fighters.json 이 움직이는 순간 링이 거짓말을 한다. 패리 링과 같은 규약이다.
    /// </summary>
    public double ChargeProgress =>
        !Charging || ChargeMaxSeconds <= 0 ? 0 : Math.Min(1.0, ActionElapsed / ChargeMaxSeconds);

    /// <summary>
    /// 모으고 있는 차지가 <b>최대에 닿았나</b>. 화면이 이것을 말하지 않으면 2초를 셀 방법이 없다 —
    /// 그래서 "단계가 올랐다" 가 아니라 "최대인가" 를 따로 낸다.
    /// </summary>
    public bool ChargeMaxed => Charging && _tier >= ChargeTierCount - 1;

    public bool Alive => Health > 0;

    /// <summary>스태미나를 깎는다. 0 아래로는 안 내려간다.</summary>
    public void Spend(double amount) => Stamina = Math.Max(0, Stamina - amount);

    /// <summary>
    /// 맞았다. <b>모으던 차지는 여기서 끊긴다</b> (이슈 #40).
    ///
    /// <para>
    /// 유지를 고르면 "보스의 패턴 위에 겹쳐 모으는 것" 이 가장 좋은 수가 된다 — 몇 대 맞고
    /// 최대 차지를 내는 쪽이 늘 이득이라, 언제 모을지에 판단이 없어지고 <c>greed</c> 축이
    /// 재려던 것도 같이 사라진다. 끊기더라도 <b>값은 안 돌려준다</b>: 스태미나는 누를 때
    /// 이미 나갔고, 그게 욕심의 값이다.
    /// </para>
    /// </summary>
    public void TakeDamage(int amount)
    {
        Health = Math.Max(0, Health - amount);

        if (Action == FighterAction.Charge)
        {
            Action = FighterAction.Idle;
            ActionElapsed = 0;
            _tier = 0;
        }
    }

    /// <summary>
    /// 정확 패리가 받아냈다. 피해가 없고, 기가 오르고, <b>공중 대시가 즉시 돌아온다</b> —
    /// "잘 받아내면 다시 움직일 수 있다" 는 보상 구조가 패리를 쓰게 만든다(나인 솔즈).
    /// 보스를 굳히는 것은 보스를 아는 <see cref="BattleSim"/> 이 한다.
    /// </summary>
    public void ParryPrecise()
    {
        Qi++;
        _parryChain = 0;
        _airDashUsed = false;
    }

    /// <summary>
    /// 부정확 패리가 받아냈다. 피해의 일부를 <b>내상</b>으로 받고, 지상이면 굳는다.
    /// 공중에서 안 굳는 것은 나인 솔즈의 규칙이다 — 공중에서 굳으면 그냥 떨어지는 것과 같다.
    /// </summary>
    /// <param name="fullDamage">막지 않았다면 받았을 피해.</param>
    public void ParryImprecise(int fullDamage)
    {
        Qi++;
        _parryChain = 0;

        // 반올림을 고정한다. 기본 반올림(짝수로)은 9 → 4, 11 → 6 처럼 홀짝에 따라 갈려
        // 같은 비율이 같은 규칙을 안 만든다 — 리플레이가 재현되려면 규칙 하나여야 한다.
        int internalDamage = (int)Math.Round(fullDamage * _config.ParryInternalRatio, MidpointRounding.AwayFromZero);
        Health = Math.Max(0, Health - internalDamage);
        InternalDamage += internalDamage;

        if (Grounded)
        {
            // 굳는 동안은 아무 행동도 못 한다. 돌던 패리도 여기서 끊는다 — 막아낸 뒤에도
            // 창이 남아 있으면 "굳었는데 아직 막고 있다" 는 두 말이 된다.
            _lockLeft = _config.ParryLock;
            Action = FighterAction.Idle;
            ActionElapsed = 0;
        }
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

    /// <summary>진행 중인 행동의 시계를 밀고, 끝났으면 Idle 로 돌린다.</summary>
    private void Advance(double dt)
    {
        // 행동과 무관한 시계 둘은 **Idle 이어도 돈다.** 부정확 패리 창(0.5초)이 패리 행동
        // (0.26~0.34초)보다 길고, 고정(0.6초)은 아예 Idle 상태에서 흐른다 — 여기서 같이
        // 멈추면 둘 다 영영 안 풀린다.
        _sinceParryPress += dt;
        _lockLeft = Math.Max(0, _lockLeft - dt);

        if (Action == FighterAction.Idle)
        {
            return;
        }

        ActionElapsed += dt;

        // 차지는 **시간이 안 끝낸다** — 손가락이 끝낸다(Begin 이 본다). 최대에 닿아도 저절로
        // 안 나가는 것은 일부러다: 저절로 나가면 "언제 놓을까" 라는 판단이 통째로 사라진다.
        if (Action == FighterAction.Charge)
        {
            _tier = TierFor(ActionElapsed);
            return;
        }

        if (ActionElapsed >= Duration(Action))
        {
            Action = FighterAction.Idle;
            ActionElapsed = 0;

            // 단계는 스윙과 같이 끝난다. 안 지우면 다음에 그냥 누른 한 대가 지난 차지의 배수를 물려받는다.
            _tier = 0;
        }
    }

    /// <summary>
    /// 이만큼 모았으면 몇 단계인가. 표는 시간 오름차순이고 <b>닿은 마지막 칸</b>이 답이다
    /// (<c>FighterDataTests</c> 가 그 순서를 지킨다).
    ///
    /// <para>
    /// 여유(epsilon)를 안 준다. 틱 누산의 부동소수 오차로 경계가 한 틱 밀릴 수는 있지만,
    /// 그 밀림은 <b>언제나 같은 방향으로 같은 만큼</b>이라 리플레이는 재현된다 — 반면 여유를
    /// 주면 "봇이 보는 경계" 와 "규칙이 쓰는 경계" 가 반 틱 어긋나 그 차이가 데이터에 섞인다.
    /// </para>
    /// </summary>
    private int TierFor(double seconds)
    {
        int tier = 0;
        for (int i = 1; i < _config.ChargeTiers.Count; i++)
        {
            if (seconds >= _config.ChargeTiers[i].Seconds)
            {
                tier = i;
            }
        }

        return tier;
    }

    /// <summary>
    /// 행동이 저절로 끝나는 시각(초). <see cref="FighterAction.Charge"/> 는 <b>여기 없다</b> —
    /// 차지를 끝내는 것은 시간이 아니라 손가락이라, <see cref="Advance"/> 가 그 갈래를 먼저 빠져나간다.
    /// </summary>
    private double Duration(FighterAction action) => action switch
    {
        FighterAction.Dash => _config.DashDuration,
        FighterAction.Parry => _config.ParryDuration,
        // 선딜은 설정값이 아니라 **이번 칼질의 남은 선딜**이다. 붙들고 있었으면 그만큼 짧다.
        FighterAction.Attack => _windup + _config.AttackActive + _config.AttackRecover,
        _ => 0,
    };

    private double Cost(FighterAction action) => action switch
    {
        FighterAction.Dash => _config.DashCost,
        FighterAction.Parry => _config.ParryCost,
        FighterAction.Attack => _config.AttackCost,
        _ => 0,
    };

    /// <summary>새 행동을 시작한다. 행동 중이거나 굳었거나 스태미나가 모자라면 입력을 버린다.</summary>
    private void Begin(InputFrame input)
    {
        // 모으는 중이면 할 일은 하나뿐이다 — 놓았는지 본다. <b>Advance 앞</b>이라 스윙의 첫 틱도
        // 경과 시간에 들어간다: 새 행동을 시작하는 것과 정확히 같은 규칙이다.
        if (Action == FighterAction.Charge)
        {
            if (!input.AttackHeld)
            {
                Swing();
            }

            return;
        }

        if (Action != FighterAction.Idle || Locked)
        {
            return;
        }

        FighterAction wanted = input.Dash ? FighterAction.Dash
            : input.Parry ? FighterAction.Parry
            : input.Attack ? FighterAction.Attack
            : FighterAction.Idle;

        // 공중 대시는 착지하거나 정확 패리를 성공할 때까지 한 번뿐이다 (나인 솔즈).
        // 몸 충돌이 없어져 공중이 안전지대가 됐으므로, 무제한 공중 대시는 "공중에 떠서
        // 계속 무적" 이라는 답 하나로 모든 패턴을 지운다.
        if (wanted == FighterAction.Dash && !Grounded && _airDashUsed)
        {
            return;
        }

        if (wanted == FighterAction.Idle || Stamina < Cost(wanted))
        {
            return;
        }

        Spend(Cost(wanted));

        // 공격을 **누른 채로** 시작하면 차지다. 그냥 누른(같은 틱에 뗀) 것은 예전 그대로
        // 곧장 스윙이라, 옛 입력 시퀀스가 한 틱도 안 밀린다.
        Action = wanted == FighterAction.Attack && input.AttackHeld ? FighterAction.Charge : wanted;
        ActionElapsed = 0;
        _tier = 0;

        // 그냥 누른 칼질은 선딜을 통째로 기다린다 — 붙든 시간이 0 이니 깎일 것이 없다.
        _windup = _config.AttackWindup;

        if (wanted == FighterAction.Dash && !Grounded)
        {
            _airDashUsed = true;
        }

        if (wanted == FighterAction.Parry)
        {
            PressParry();
        }
    }

    /// <summary>
    /// 차지를 놓았다 — 모은 만큼이 <b>이 한 번의 스윙에 굳는다.</b>
    /// 단계를 여기서 고정하는 이유는 스윙이 도는 동안 시계가 계속 가기 때문이다:
    /// 매 틱 다시 계산하면 칼이 나가는 프레임의 배수가 놓은 순간의 배수와 달라진다.
    /// </summary>
    private void Swing()
    {
        _tier = TierFor(ActionElapsed);

        // 붙들고 있던 시간이 곧 선딜이다 — 남은 만큼만 더 기다린다. 0.8초(1단계)면 이미 한참
        // 넘겼으므로 칼이 곧장 나간다. 경계를 계단이 아니라 연속으로 두는 이유는, 선딜보다
        // 짧게 붙든 경우(0.07초)가 그냥 누르기보다 느려지는 구멍을 만들지 않기 위해서다.
        _windup = Math.Max(0, _config.AttackWindup - ActionElapsed);
        Action = FighterAction.Attack;
        ActionElapsed = 0;
    }

    /// <summary>
    /// 패리를 눌렀다. <b>연타 사슬을 여기서 센다</b> — 앞 누름의 부정확 창이 아직 살아 있는데
    /// 또 눌렀으면 사슬이 자라고, 자란 만큼 정확 창이 좁아지다 사라진다.
    ///
    /// <para>
    /// "공격이 안 오는데 눌렀나" 를 보스에게 묻지 않는다 — 파이터는 보스를 모른다. 대신
    /// <b>받아낸 것이 있으면 사슬이 0 으로 풀린다</b>(<see cref="ParryPrecise"/> ·
    /// <see cref="ParryImprecise"/>). 그래서 실제로 무언가를 막은 누름은 연타로 안 세어지고,
    /// 허공에 연달아 누른 것만 벌을 받는다. 보스를 아는 코드가 없어도 같은 규칙이 선다.
    /// </para>
    /// </summary>
    private void PressParry()
    {
        _parryChain = _sinceParryPress <= _config.ParryImpreciseWindow ? _parryChain + 1 : 1;
        _sinceParryPress = 0;
    }

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
