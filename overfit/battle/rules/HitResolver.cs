using System;

namespace Overfit.Battle.Rules;

/// <summary>판정 하나가 파이터에게 어떻게 끝났나.</summary>
public enum HitVerdict
{
    /// <summary>
    /// <b>사거리 밖</b>이라 안 닿았다 — 도망쳐 피한 것이다(그 거리를 대시가 만들었을 수도 있다).
    ///
    /// <para>
    /// 안쪽(<see cref="MissedTooClose"/>)과 <b>따로</b> 둔다 (이슈 #46). 한 갈래였을 때는
    /// 파고들어 피한 것과 도망쳐 피한 것이 계측에서 같은 한 점이었다 — 그 둘은 성향이 정반대고
    /// 봉인할 것도 정반대라, 뭉개면 2단계가 정반대 변종을 뽑는다.
    /// </para>
    /// </summary>
    MissedTooFar,

    /// <summary>
    /// <b>안쪽 주머니</b>라 안 닿았다 — 파고들어 피한 것이다.
    /// 값으로 도달하려면 패턴의 <c>distance[0] &gt; 0</c> 이어야 한다 (지금은 점프 강타의 착지 충격 하나).
    /// </summary>
    MissedTooClose,

    /// <summary>높이가 어긋났다 — 점프로 넘었거나 대공 아래 서 있었다.</summary>
    MissedByHeight,

    /// <summary>맞았다.</summary>
    Hit,

    /// <summary>닿았지만 무적이 먹었다 — <b>대시로</b> 피한 것이다.</summary>
    Dodged,

    /// <summary>닿았지만 <b>정확</b> 패리가 받았다. 피해 0 · 보스 경직.</summary>
    Parried,

    /// <summary>
    /// 닿았고 <b>부정확</b> 패리가 받았다 — 늦게(또는 너무 일찍) 눌렀다. 피해의 절반을 내상으로
    /// 받고 지상이면 굳는다. <b>이 값이 있어서 계측이 셋을 가른다</b>: 정확(Parried) ·
    /// 늦음(ParriedLate) · 무반응(Hit + Verb=None). 전에는 뒤의 둘이 같은 점이었다.
    /// </summary>
    ParriedLate,

    /// <summary>
    /// 닿았고 <b>가드</b>가 받아냈다 (이슈 #47). 피해는 <c>guard_chip_ratio</c> 만 흘러 들어오고
    /// 값은 <b>스태미나</b>로 낸다 — 그 값이 피해에 비례하므로 무거운 한 방이 가드를 깬다.
    /// </summary>
    Guarded,

    /// <summary>
    /// 닿았고 가드가 <b>깨졌다</b> (이슈 #47). 두 길로 온다 — 스태미나가 모자랐거나,
    /// <c>guard_break</c> 판정이었거나. 어느 쪽이든 <b>전액</b>이고 <c>guard_break_lock</c> 동안 굳는다.
    /// 둘을 한 값으로 두는 것은 일부러다: 플레이어가 겪는 것도 화면이 말하는 것도 같은 "깨졌다" 이고,
    /// 왜 깨졌는지는 그 순간의 스태미나가 이미 말한다.
    /// </summary>
    GuardBroken,
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

        // 안과 밖을 **갈라서** 말한다. 둘 다 "거리 때문에 안 맞았다" 지만 플레이어가 한 일은
        // 정반대다 — 뭉치면 DistanceBias 가 파고든 사람을 "멀리서 싸운다" 로 읽는다 (이슈 #46).
        double distance = Math.Abs(fighter.X - bossX);
        if (distance > box.MaxDistance)
        {
            return HitVerdict.MissedTooFar;
        }

        if (distance < box.MinDistance)
        {
            return HitVerdict.MissedTooClose;
        }

        // 몸통은 발밑(Y)에서 키만큼 위까지다. 판정 구간과 겹쳐야 닿는다 —
        // 낮은 판정은 점프로 넘고, 대공은 지상이 안전하다.
        double bodyLow = fighter.Y;
        double bodyHigh = fighter.Y + fighter.BodyHeight;
        if (bodyHigh < box.LowHeight || bodyLow > box.HighHeight)
        {
            return HitVerdict.MissedByHeight;
        }

        // 유효 창은 **패턴과 캐릭터 중 좁은 쪽**이다.
        //
        // 전에는 캐릭터 쪽만 봤다. 대공찌르기가 parry_window 0.10 을(그때 캐릭터의 0.12 보다 좁게)
        // 선언해도 아무 일도 안 일어났는데 — 그 숫자는 망의 입력이 된다. 거짓말하는 숫자는
        // 없는 숫자보다 나쁘다. "빠른 공격은 패리하기 더 어렵다" 는 진짜 설계 레버라
        // 태그를 지우는 대신 물게 했다.
        if (fighter.Action == FighterAction.Dash && Within(fighter.ActionElapsed, fighter.DashIFrames, tags.DashWindow))
        {
            return HitVerdict.Dodged;
        }

        // 가드가 패리보다 **먼저**다 (이슈 #47). 두 갈래가 겹칠 수 있기 때문이다 —
        // 가드는 패리와 같은 키를 붙들어 들어가므로 가드가 선 뒤에도 그 누름의 부정확 창(0.5초)이
        // 0.2초쯤 남는데, 패리 갈래가 먼저면 **가드 불가 판정이 ParriedLate 로 먹혀**
        // "가드로는 못 막는다" 는 성질이 한 번도 안 일어난다.
        //
        // 값으로는 이미 못 겹친다 — Fighter.EnterGuard 가 그 시계를 끝내기 때문이다. 그래도
        // 순서를 이렇게 두는 것은 **규칙과 구조가 같은 말을 하게** 하기 위해서다: 나중에 가드로
        // 들어오는 길이 하나 더 생겨 시계를 안 끝내더라도, 이 자리에서 다시 안 새게 된다.
        //
        // parryable 태그는 여기서 **안 본다.** 크림슨은 "받아치지 마라" 이지 "막지 마라" 가 아니다 —
        // 가드를 막는 것은 판정 쪽의 guard_break 하나뿐이다.
        if (fighter.Guarding)
        {
            // 깨지는 길이 둘이다. 스태미나가 모자라거나, 애초에 가드로는 못 막는 판정이거나.
            return box.GuardBreak || fighter.Stamina < fighter.GuardStaminaCost(box.Damage)
                ? HitVerdict.GuardBroken
                : HitVerdict.Guarded;
        }

        if (tags.Parryable)
        {
            // 창은 **행동이 아니라 누름**에 붙는다. 부정확 창(0.5초)이 패리 행동(0.26~0.34초)보다
            // 길어서, 행동이 끝났으면 못 받는다고 하면 그 뒷부분이 통째로 사라진다 —
            // 거기가 "늦게 눌렀다" 를 "아무것도 안 했다" 와 가르는 자리다.
            if (Within(fighter.SinceParryPress, fighter.PreciseParryWindow, tags.ParryWindow))
            {
                return HitVerdict.Parried;
            }

            // 부정확 창은 패턴의 창과 안 견준다. 패턴의 parry_window 는 "얼마나 정확해야 하는가"
            // 이고, 부정확 패리는 그 정확을 이미 놓친 자리이기 때문이다.
            if (fighter.SinceParryPress < fighter.ImpreciseParryWindow)
            {
                return HitVerdict.ParriedLate;
            }
        }

        return HitVerdict.Hit;
    }

    /// <summary>
    /// 행동을 시작한 지 <paramref name="elapsed"/> 가 흘렀을 때, 두 창 모두 안에 있는가.
    ///
    /// <para>
    /// 창이 0 이면 <b>한 번도 안이 아니다</b> — <c>dash_window: 0</c> 의 "대시로 못 피한다" 가
    /// 그렇게 값과 뜻이 같은 자리에 떨어진다. 길이 0 인 창으로 읽어도 결과가 같지만,
    /// 뜻은 다르다: <c>DodgeEvent.DashAvailable</c> 이 <c>dash_window &gt; 0</c> 으로
    /// "대시가 가능했나" 를 싣고 의존도 축의 분모가 그것이다.
    /// </para>
    /// </summary>
    private static bool Within(double elapsed, double fighterWindow, double patternWindow) =>
        elapsed < Math.Min(fighterWindow, patternWindow);
}
