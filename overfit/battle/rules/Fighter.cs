using System;

namespace Overfit.Battle.Rules;

/// <summary>파이터가 지금 하는 것. 하나만 할 수 있다 — 행동 중에는 다른 행동을 못 시작한다.</summary>
public enum FighterAction
{
    Idle,
    Dash,
    Parry,
    Attack,
}

/// <summary>
/// 플레이어 상태 기계. <b>보스를 모른다</b> — 둘을 아는 것은 <c>BattleSim</c>(아직 없음, Task 5) 하나다.
/// 그래야 이 테스트가 보스 없이 돈다.
/// </summary>
public sealed class Fighter
{
    private readonly FighterConfig _config;
    private readonly Arena _arena;

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
    /// 패리가 막아주는 창 안인가. 위와 같이 <b>캐릭터 쪽의 창</b>이다 — 패리 불가 패턴이나
    /// 더 좁은 <c>parry_window</c> 를 가진 패턴 앞에서는 이것이 참이어도 못 막는다.
    /// </summary>
    public bool Parrying => Action == FighterAction.Parry && ActionElapsed < ParryWindow;

    /// <summary>이 캐릭터의 대시 무적 폭(초). <see cref="HitResolver"/> 가 패턴의 창과 견준다.</summary>
    public double DashIFrames => _config.DashIFrames;

    /// <summary>이 캐릭터의 패리 창 폭(초). <see cref="HitResolver"/> 가 패턴의 창과 견준다.</summary>
    public double ParryWindow => _config.ParryWindow;

    /// <summary>공격 판정이 서 있는가. 선딜을 지나고 후딜 전.</summary>
    public bool AttackActive => Action == FighterAction.Attack
        && ActionElapsed >= _config.AttackWindup
        && ActionElapsed < _config.AttackWindup + _config.AttackActive;

    public double AttackReach => _config.AttackReach;

    public int AttackDamage => _config.AttackDamage;

    public bool Alive => Health > 0;

    /// <summary>스태미나를 깎는다. 0 아래로는 안 내려간다.</summary>
    public void Spend(double amount) => Stamina = Math.Max(0, Stamina - amount);

    public void TakeDamage(int amount) => Health = Math.Max(0, Health - amount);

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
        if (Action == FighterAction.Idle)
        {
            return;
        }

        ActionElapsed += dt;
        if (ActionElapsed >= Duration(Action))
        {
            Action = FighterAction.Idle;
            ActionElapsed = 0;
        }
    }

    private double Duration(FighterAction action) => action switch
    {
        FighterAction.Dash => _config.DashDuration,
        FighterAction.Parry => _config.ParryDuration,
        FighterAction.Attack => _config.AttackWindup + _config.AttackActive + _config.AttackRecover,
        _ => 0,
    };

    private double Cost(FighterAction action) => action switch
    {
        FighterAction.Dash => _config.DashCost,
        FighterAction.Parry => _config.ParryCost,
        FighterAction.Attack => _config.AttackCost,
        _ => 0,
    };

    /// <summary>새 행동을 시작한다. 행동 중이거나 스태미나가 모자라면 입력을 버린다.</summary>
    private void Begin(InputFrame input)
    {
        if (Action != FighterAction.Idle)
        {
            return;
        }

        FighterAction wanted = input.Dash ? FighterAction.Dash
            : input.Parry ? FighterAction.Parry
            : input.Attack ? FighterAction.Attack
            : FighterAction.Idle;

        if (wanted == FighterAction.Idle || Stamina < Cost(wanted))
        {
            return;
        }

        Spend(Cost(wanted));
        Action = wanted;
        ActionElapsed = 0;
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
        if (input.Jump && Grounded && Action == FighterAction.Idle)
        {
            VelocityY = _config.JumpVelocity;
        }

        VelocityY -= Arena.Gravity * dt;
        Y += VelocityY * dt;

        if (Y <= 0)
        {
            Y = 0;
            VelocityY = 0;
        }
    }
}
