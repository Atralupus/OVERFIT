using System;

namespace PreReLU.Battle.Rules;

/// <summary>판정 하나가 파이터에게 어떻게 끝났나.</summary>
public enum HitVerdict
{
    /// <summary>기하가 안 닿았다 — 거리 밖이거나 높이가 어긋났다. <b>위치나 점프로 피한 것이다.</b></summary>
    Miss,

    /// <summary>맞았다.</summary>
    Hit,

    /// <summary>닿았지만 무적이 먹었다 — <b>대시로</b> 피한 것이다.</summary>
    Dodged,

    /// <summary>닿았지만 패리가 받았다.</summary>
    Parried,
}

/// <summary>
/// 판정 하나를 파이터에게 대본다. <b>상태를 안 바꾼다</b> — 판단만 하고 체력을 깎는 것은
/// <c>BattleSim</c> 이다. 그래야 같은 판정을 여러 번 물어봐도 답이 같고 테스트가 쉽다.
///
/// <para>
/// <see cref="HitVerdict.Miss"/> 와 <see cref="HitVerdict.Dodged"/> 를 나누는 것이 중요하다.
/// 둘 다 안 맞은 것이지만 전자는 위치·점프로 피한 것이고 후자는 대시로 피한 것이라,
/// 계측이 이 둘을 구별해야 회피 수단별 의존도가 축이 된다.
/// </para>
/// </summary>
public static class HitResolver
{
    public static HitVerdict Resolve(Fighter fighter, double bossX, HitBox box, PatternTags tags)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(tags);

        double distance = Math.Abs(fighter.X - bossX);
        if (distance < box.MinDistance || distance > box.MaxDistance)
        {
            return HitVerdict.Miss;
        }

        // 몸통은 발밑(Y)에서 키만큼 위까지다. 판정 구간과 겹쳐야 닿는다 —
        // 낮은 판정은 점프로 넘고, 대공은 지상이 안전하다.
        double bodyLow = fighter.Y;
        double bodyHigh = fighter.Y + fighter.BodyHeight;
        if (bodyHigh < box.LowHeight || bodyLow > box.HighHeight)
        {
            return HitVerdict.Miss;
        }

        if (fighter.Invulnerable)
        {
            return HitVerdict.Dodged;
        }

        if (fighter.Parrying && tags.Parryable)
        {
            return HitVerdict.Parried;
        }

        return HitVerdict.Hit;
    }
}
