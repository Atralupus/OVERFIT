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

    /// <summary>
    /// -1 왼쪽 · +1 오른쪽. 목표를 모르는 채 태어나므로 <see cref="BattleSim"/> 이 세우자마자
    /// <see cref="Face"/> 로 맞춘다 — 여기 기본값이 있는 것은 이 값이 <b>0 이 되는 순간이 없게</b>
    /// 하기 위해서다(0 이면 뷰가 어느 쪽도 못 그린다).
    ///
    /// <para>
    /// <b>판정은 이것을 안 본다</b> — <see cref="HitResolver"/> 는 거리를 <c>Math.Abs</c> 로 재서
    /// 좌우가 대칭이다. 그래도 뷰가 아니라 규칙이 정하는 이유는, 뷰가 스스로 좌표를 보고 정하면
    /// "같은 시드면 같은 결과" 가 그림까지 덮지 못하기 때문이다(이슈 #36).
    /// </para>
    /// </summary>
    public int Facing { get; private set; } = -1;

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

    /// <summary>
    /// 목표 쪽으로 몸을 돌린다. <b>패턴이 도는 동안에는 아무 일도 안 한다.</b>
    ///
    /// <para>
    /// 그 잠금이 이 메서드의 존재 이유다. 스윙 도중에 따라 돌면 <b>예고가 거짓말이 된다</b> —
    /// 예고를 보고 왼쪽으로 피했는데 보스가 휙 돌아 따라오면, 이 게임에서 패리를 가르치는
    /// 유일한 수단이 무너진다. 백장의 <c>이단 올려베기</c> 는 "칼이 땅에 있나 떠 있나" 가
    /// 설계 전부라 특히 그렇다.
    /// </para>
    ///
    /// <para>
    /// 잠금을 부르는 쪽(<see cref="BattleSim"/>)이 아니라 여기 두는 이유는 이것이 보스의 불변식이기
    /// 때문이다 — 호출 자리가 하나 늘 때마다 같은 조건을 베껴 적으면 언젠가 한 곳이 빠진다.
    /// </para>
    /// </summary>
    /// <param name="targetX">바라볼 지점. 보통 파이터의 x 다.</param>
    public void Face(double targetX)
    {
        if (CurrentPattern is not null)
        {
            return;
        }

        // 정확히 겹치면 보던 쪽을 유지한다. 몸 충돌이 없어져(이슈 #27) 겹치는 일이 흔한데,
        // 여기서 한쪽을 고르면 겹쳐 있는 동안 스프라이트가 틱마다 파닥인다.
        int toward = Math.Sign(targetX - X);
        if (toward != 0)
        {
            Facing = toward;
        }
    }

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
