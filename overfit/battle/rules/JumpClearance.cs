using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// "점프로 넘을 수 있었나" 를 <b>그 판정이 선 자리에서</b> 잰다 (#85 · 설계 §7.3) — 관측의 <c>JumpAvailable</c> 이 이것이다.
///
/// <para>
/// 그 판정의 창이 열린 틱에 파이터가 선 가로 자리에서 <b>제자리로 한 번 뛰었다면</b>, 떠 있는 동안 몸이 모양의 어느 사각형과도
/// 안 겹치는 틱이 창의 틱 수만큼 이어지나. 발 높이는 이 파이터로 이산 점프를 실제로 돌려 쟀다(<see cref="Fighter"/> 가 틱마다
/// 적분하는 그대로다 — 식(v²/2g)으로 재면 몇 px 가 어긋나고 그만큼 "넘을 수 있다" 가 거짓이 된다).
/// </para>
///
/// <para>
/// <b>전에는 모양 전체의 윗끝 하나로 쟀다</b> (#72 — <c>BossHits.TicksAbove(fighter, shape.Bounds.Y1)</c>). 발이 외곽 상자의 윗끝 위에
/// 창 내내 있을 수 있나였고, 3연격은 "1타만 넘는다" 가 나왔다. 실제 판정은 몸과 사각형 하나하나를 견주므로 그 말이 틀렸다 —
/// 2타의 윗끝(346.5)은 보스 등 뒤와 칼끝에만 있고 보스 앞 115 에서 몸에 닿는 칸은 104.5 까지라, 보스 앞에서는 뛰어 넘는다.
/// 3타도 바짝 붙으면(보스 중심에서 +167 안) 넘는다. 관측이 거짓을 실으면 "점프로 피할 수 있었는데 안 뛰었다" 가 틀리게 적혀
/// 점프 의존도의 분모가 거짓이 된다(v0.7.0 릴리즈 노트를 검증하다 찾았다 · #85).
/// </para>
///
/// <para>
/// 서서도 안 맞는 자리(사거리 밖 · 초승달 안쪽)는 떠 있는 동안에도 대개 안 닿아 참이다 — 뛰어도 산다는 뜻이다. 그 자리의 관측은
/// 수단이 간격(<c>Spacing</c>)이라 점프를 "골랐다" 로 세지 않는다. 서서는 맞는 자리에서 뛰어 빗나갔으면 빗나간 이유가 높이든 거리 · 틈이든
/// 점프의 공이다(<c>DodgeCredit</c> — 분모와 분자가 같은 자리를 잰다). 판을 세울 때 한 번 짓고(<c>BattleSim</c>), 창이 열릴 때마다 묻는다 —
/// 판정 하나에 사각형 수 × 떠 있는 틱(30 × 60) 번 견주고 할당은 없다.
/// </para>
/// </summary>
public sealed class JumpClearance
{
    /// <summary>누른 틱부터 땅에 닿기 전까지 틱마다의 발 높이. 첫 칸이 누른 틱이다.</summary>
    private readonly double[] _feet;

    private readonly double _halfWidth;
    private readonly double _height;

    public JumpClearance(FighterConfig fighter)
    {
        ArgumentNullException.ThrowIfNull(fighter);

        // 가로는 안 쓴다 — 벽에 안 막히게 넓은 방 한가운데서 제자리로 뛴다.
        var body = new Fighter(fighter, new Arena(1_000_000), 500_000);
        var feet = new List<double>();
        body.Tick(new InputFrame(0, Jump: true, false, false, false), BattleSim.Dt);
        while (!body.Grounded)
        {
            feet.Add(body.Y);
            body.Tick(default, BattleSim.Dt);
        }

        _feet = feet.ToArray();
        _halfWidth = fighter.HalfWidth;
        _height = fighter.Height;
    }

    /// <summary>한 번 뛰어 떠 있는 틱 수 — 누른 틱부터 땅에 닿기 전까지. 실제 캐릭터(1220 · 중력 2400)는 60틱이다.</summary>
    public int AirTicks => _feet.Length;

    /// <summary>
    /// <paramref name="fighterX"/> 에서 제자리로 뛰면, 떠 있는 동안 몸이 <paramref name="at"/> 에 놓인 모양의 어느 사각형과도 안
    /// 겹치는 틱이 <b>가장 길게 몇 틱 이어지나</b>. 겹침은 판정이 묻는 그 함수(<see cref="ShapeHit.Overlaps"/>)로 잰다 — 가장자리도 친다:
    /// 발이 사각형 윗끝과 같으면 닿은 것이다.
    /// </summary>
    public int ClearRun(HitShape shape, Placement at, double fighterX)
    {
        ArgumentNullException.ThrowIfNull(shape);

        int best = 0, run = 0;
        foreach (double y in _feet)
        {
            var body = new HitRect(fighterX - _halfWidth, fighterX + _halfWidth, y, y + _height);
            run = ShapeHit.Overlaps(shape, at, body) ? 0 : run + 1;
            best = Math.Max(best, run);
        }

        return best;
    }

    /// <summary>
    /// 점프 한 번으로 창 내내 몸이 모양 밖에 있을 수 있나 — <see cref="ClearRun"/> 이 창의 틱 수 이상인가. 창의 틱 수는 부르는 쪽이
    /// <see cref="BattleSim.TicksFor"/> 로 바꿔 넘긴다(반올림은 거기 한 곳이다).
    /// </summary>
    public bool Clears(HitShape shape, Placement at, double fighterX, int windowTicks) =>
        ClearRun(shape, at, fighterX) >= windowTicks;
}
