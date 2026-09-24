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
    /// 패턴의 <b>마무리</b>를 패리로 받아쳤을 때 굳는 시간(초) — 이 게임에 <b>하나뿐인</b> 경직이다
    /// (이슈 #53).
    ///
    /// <para>
    /// 전에는 경직이 둘이었다: 평범한 패리의 0.5초와 가드 불가를 받아친 1.6초. 평범한 쪽을 없앤
    /// 것이 이 이슈의 절반이다 — 1·2타를 패리하면 패턴 타임라인이 0.5초 서고 뷰의 히트스톱까지
    /// 겹쳐, <b>같은 패턴인데 3타가 올 때까지의 시간이 매번 달랐다</b>(0.90초 → 1.52초).
    /// 그게 유저가 말한 "딜레이가 매번 다르다" 이고, 리듬이 흔들리면 외울 것이 없어진다.
    /// 이제 앞의 연타를 받아친 상은 <b>피해 0 · 기 +1 · 공중 대시 회복</b>이고 박자는 고정이다.
    /// </para>
    ///
    /// <para>
    /// 남은 하나는 <b>마무리</b>(<see cref="HitBox.Finisher"/>)에 걸린다. 마무리는 아홉 변종 전부
    /// 빨간 가드 불가라 1단계부터 이 상이 서고, 마무리에 거는 것이 안전하기도 하다: 그 뒤에는 올
    /// 판정이 없어서 타임라인이 서도 미룰 것이 없다. 손으로 단 깃발(guard_break)이 아니라 타임라인의
    /// 자리에 거는 이유는 <see cref="HitBox.Finisher"/> 에 적어 두었다.
    /// </para>
    ///
    /// <para>
    /// 길이는 <b>최대 차지 한 번이 이 경직 안에 들어가는가</b>로 정해진다. 전에는
    /// 경직 + <see cref="PatternGap"/> 을 합쳐서 쟀는데, 그러면 패턴 간격을 고치는 날
    /// 이 상이 조용히 사라진다. <c>BossDataTests</c> 가 그 산수를 <c>fighters.json</c> 과 대조한다.
    /// </para>
    /// </summary>
    public required double FinisherParryStagger { get; init; }

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
    /// 발바닥 높이 (바닥 0). <b>지금은 늘 0 이다</b> — 뛰어오르는 패턴(설계 §4.2)이 들어올 때 움직인다.
    /// 판정은 이것을 모양을 놓는 자리로 쓴다(<see cref="Placement"/>).
    /// </summary>
    public double Y { get; }

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
    /// 굳었나. <b>패턴을 취소하지 않고 세운다</b> — 취소로 하면 연속타 패턴이 첫 대만 받아내도
    /// 통째로 지워져, 조작을 맞추는 이슈(#27)가 밸런스를 통째로 바꾸게 된다.
    /// </summary>
    public bool Staggered => _staggerLeft > 0;

    /// <summary>
    /// <b>패턴의 마무리를 받아쳤다</b> (이슈 #53). 굳는 길이는 데이터(bosses.json)가 정한다.
    ///
    /// <para>
    /// 부르는 자리가 하나뿐인 것이 계약이다 — 앞의 연타를 패리해도 보스는 <b>안 굳는다.</b>
    /// 굳으면 패턴 타임라인이 서고, 그 순간 같은 패턴의 박자가 플레이어마다 · 시도마다 달라진다.
    /// </para>
    /// </summary>
    public void Stagger() => _staggerLeft = _config.FinisherParryStagger;

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
