using System;

namespace Overfit.Battle.Rules;

/// <summary>
/// 보스 한 종의 수치. <c>data/bosses.json</c> 의 모양이고 키는 snake_case 로 변환된다.
/// <b>여기 없는 수치를 C# 에 상수로 두지 않는다.</b>
/// </summary>
public sealed class BossConfig
{
    public required int MaxHealth { get; init; }

    public required double MoveSpeed { get; init; }

    /// <summary>몸 절반 폭. 캐릭터의 4배라 근접에서는 거의 항상 닿는다.</summary>
    public required double HalfWidth { get; init; }

    /// <summary>패턴과 패턴 사이의 쉬는 시간(초). 이 동안 플레이어가 때릴 틈이 난다.</summary>
    public required double PatternGap { get; init; }

    /// <summary>정확 패리에 굳는 시간(초). 이 동안 걷지도 않고 돌던 패턴의 타임라인도 안 민다.</summary>
    public required double StaggerSeconds { get; init; }

    public required string Sprite { get; init; }
}

/// <summary>
/// 보스 상태. <b>플레이어를 모른다</b> — 다가갈 목표 x 를 <see cref="BattleSim"/> 이 넣어준다.
/// 그래야 패턴 테스트가 플레이어 없이 돈다.
/// </summary>
public sealed class Boss
{
    private readonly BossConfig _config;
    private readonly Arena _arena;
    private double _staggerLeft;

    public Boss(BossConfig config, Arena arena, double x)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(arena);
        _config = config;
        _arena = arena;
        X = x;
        Health = config.MaxHealth;
    }

    public double X { get; private set; }

    public int Health { get; private set; }

    public bool Alive => Health > 0;

    /// <summary>지금 돌고 있는 패턴 id. 쉬는 중이면 null.</summary>
    public string? CurrentPattern { get; internal set; }

    public double HalfWidth => _config.HalfWidth;

    public double PatternGap => _config.PatternGap;

    /// <summary>
    /// 정확 패리에 굳었나. <b>패턴을 취소하지 않고 세운다</b> — 취소로 하면 연속타 패턴이
    /// 첫 대만 받아내도 통째로 지워져, 조작을 맞추는 이슈(#27)가 밸런스를 통째로 바꾸게 된다.
    /// </summary>
    public bool Staggered => _staggerLeft > 0;

    /// <summary>정확 패리가 들어왔다. 굳는 길이는 데이터(bosses.json)가 정한다.</summary>
    public void Stagger() => _staggerLeft = _config.StaggerSeconds;

    /// <summary>경직 시계를 민다. <b>굳어 있어도 도는 유일한 시계다</b> — 안 그러면 안 풀린다.</summary>
    public void Tick(double dt) => _staggerLeft = Math.Max(0, _staggerLeft - dt);

    public void TakeDamage(int amount) => Health = Math.Max(0, Health - amount);

    /// <summary>목표 쪽으로 다가간다. 패턴을 도는 동안에는 <see cref="BattleSim"/> 이 안 부른다.</summary>
    public void Approach(double targetX, double dt)
    {
        double step = _config.MoveSpeed * dt;
        double delta = targetX - X;
        if (Math.Abs(delta) <= step)
        {
            X = targetX;
        }
        else
        {
            X += Math.Sign(delta) * step;
        }

        X = Math.Clamp(X, _config.HalfWidth, _arena.Width - _config.HalfWidth);
    }
}
