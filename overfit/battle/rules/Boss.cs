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

    /// <summary>
    /// 몸 키(px). 반폭과 같이 <b>그려지는 몸</b>에서 잰다 — bosses.json 의 <c>_note_height</c> 에 어떻게 쟀는지 있다.
    /// 파이터의 칼이 이것에 대 본다 (이슈 #59).
    /// </summary>
    public required double Height { get; init; }

    /// <summary>패턴과 패턴 사이의 쉬는 시간(초). 이 동안 플레이어가 때릴 틈이 난다.</summary>
    public required double PatternGap { get; init; }

    /// <summary>
    /// <b>탈진</b>의 길이(초) — 받아치면 어느 타든 보스가 탈진한다 (#72 · 설계 §4.3). 틱으로는 <c>BattleSim</c> 이 바꿔 넘긴다
    /// (1.5초 = 90틱 · 보스는 <c>BattleSim</c> 을 모른다).
    ///
    /// <para>
    /// 길이는 <b>반격 2연격이 확실히 들어가는가</b>로 잰다. 받아친 패리의 커밋 안에서 누른 J 는 곧장 1타라(되받아치기)
    /// 가장 이른 J 는 받아친 다음 틱이고, 그 2연격의 2타가 창의 끝 틱에 닿기까지 0.0167 + 0.25 + 0.6667 + 0.1667 = 1.1001초다.
    /// 1.5 에서 0.3999 가 남는다 — <c>BossDataTests</c> 가 사람의 반응 여유 0.15 를 넣어 <c>fighters.json</c> 과 대조한다.
    /// 전에는 마무리를 받아쳤을 때만 굳었고(<c>finisher_parry_stagger</c> 2.3 · 이슈 #53) 앞의 연타를 받아친 상은 피해 0 뿐이었다 —
    /// 스펙이 "어느 타든 끊고 탈진" 으로 바꿨다.
    /// </para>
    /// </summary>
    public required double ExhaustSeconds { get; init; }

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

    /// <summary>남은 탈진 틱. 0 이면 탈진이 아니다.</summary>
    private int _exhaustLeft;

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
    /// 발바닥 높이 (바닥 0). 도약(설계 §4.2)이 움직이고(<see cref="Move"/>), 판정 창 동안에는 늘 땅이다 —
    /// 움직임은 창 밖에서만 돈다(설계 §3.5 6). 판정은 이것을 모양을 놓는 자리로 쓰고(<see cref="Placement"/>),
    /// 몸통도 발과 같이 올라간다(<see cref="Body"/>).
    /// </summary>
    public double Y { get; private set; }

    /// <summary>몸통 — 중심 ± 반폭, 발바닥에서 키만큼 (월드). 파이터의 칼이 이것에 대 본다 (이슈 #59).</summary>
    public HitRect Body => new(X - _config.HalfWidth, X + _config.HalfWidth, Y, Y + _config.Height);

    /// <summary>
    /// -1 왼쪽 · +1 오른쪽. 목표를 모르는 채 태어나므로 <see cref="BattleSim"/> 이 세우자마자
    /// <see cref="Face"/> 로 맞춘다 — 여기 기본값이 있는 것은 이 값이 <b>0 이 되는 순간이 없게</b>
    /// 하기 위해서다(0 이면 뷰가 어느 쪽도 못 그린다).
    ///
    /// <para>
    /// <b>판정이 이것으로 모양을 놓는다</b> (이슈 #59 · <see cref="Placement"/>). 지금 패턴은 전부 좌우 대칭 띠라
    /// (<see cref="HitShape.Band"/>) 어느 쪽을 보든 결과가 같지만, 앞으로만 치는 모양이 들어오면 이 값이 판정을
    /// 가른다. 뷰가 아니라 규칙이 정하는 이유는, 뷰가 스스로 좌표를 보고 정하면
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
    /// 탈진했나 (#72 · 설계 §4.3). 탈진한 보스는 아무것도 안 한다 — 패턴은 무너질 때 끊겼고(<c>BattleSim</c> 의 탈진 루틴),
    /// 다가가지도 돌아서지도 않는다. 맞으면 피해만 들어간다.
    /// </summary>
    public bool Exhausted => _exhaustLeft > 0;

    /// <summary>
    /// 탈진에 든다. 부르는 곳은 <c>BattleSim</c> 의 탈진 루틴 하나다 — 원인(패리 · 4번 PR 의 경직 게이지)이 몇이든
    /// 같은 상태 · 같은 그림에 닿아야 한다(설계 §4.3). 길이는 틱이다 — 반올림은 <c>BattleSim.TicksFor</c> 한 곳이다.
    /// </summary>
    public void Exhaust(int ticks) => _exhaustLeft = Math.Max(0, ticks);

    /// <summary>탈진 시계를 한 틱 민다. <b>탈진해 있어도 도는 유일한 시계다</b> — 안 그러면 안 풀린다.</summary>
    public void Tick()
    {
        if (_exhaustLeft > 0)
        {
            _exhaustLeft--;
        }
    }

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

    /// <summary>
    /// 움직임(설계 §8.1)이 정한 자리로 옮긴다. <b>패턴 중에도 돌아선다</b> — 방향 잠금(<see cref="Face"/>)의
    /// <b>유일한</b> 예외가 이것이다: 도약은 뛰는 틱에 착지 자리 쪽으로 돌아선다(설계 §4.2). 잠금을 푸는 길을
    /// 여기 하나로 두어야, 예고를 거짓말로 만드는 돌아서기가 어디서 나는지를 한 자리에서 본다.
    /// </summary>
    /// <param name="x">발 중심 x. 아레나 안으로 자른다.</param>
    /// <param name="y">발바닥 높이. 바닥 아래로는 안 간다.</param>
    /// <param name="facing">볼 쪽. 0 이면 그대로 둔다.</param>
    public void Move(double x, double y, int facing)
    {
        X = Math.Clamp(x, _config.HalfWidth, _arena.Width - _config.HalfWidth);
        Y = Math.Max(0, y);
        if (facing != 0)
        {
            Facing = Math.Sign(facing);
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
