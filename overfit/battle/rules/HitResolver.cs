using System;

namespace Overfit.Battle.Rules;

/// <summary>판정 하나가 파이터에게 어떻게 끝났나.</summary>
public enum HitVerdict
{
    /// <summary>거리가 안 닿았다 — 간격으로 피한 것이다.</summary>
    MissedByRange,

    /// <summary>높이가 어긋났다 — 점프로 넘었거나 대공 아래 서 있었다.</summary>
    MissedByHeight,

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
/// 판정 결과가 <b>무엇으로 피했는지까지</b> 말해야 한다. 안 맞은 이유가 거리인지 높이인지를
/// 여기서 버리면 <c>BattleSim</c> 은 "그 순간 무슨 행동 중이었나" 로 추측할 수밖에 없고,
/// 그 추측은 실제로 틀렸다 — 점프로 넘긴 지면쓸기가 같이 눌러둔 패리의 공으로 기록됐다.
/// 이유는 여기서 이미 계산돼 있으니 버리지 않고 실어 보낸다.
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
            return HitVerdict.MissedByRange;
        }

        // 몸통은 발밑(Y)에서 키만큼 위까지다. 판정 구간과 겹쳐야 닿는다 —
        // 낮은 판정은 점프로 넘고, 대공은 지상이 안전하다.
        double bodyLow = fighter.Y;
        double bodyHigh = fighter.Y + fighter.BodyHeight;
        if (bodyHigh < box.LowHeight || bodyLow > box.HighHeight)
        {
            return HitVerdict.MissedByHeight;
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
