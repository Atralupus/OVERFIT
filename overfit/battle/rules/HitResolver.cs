using System;

namespace Overfit.Battle.Rules;

/// <summary>판정 하나가 파이터에게 어떻게 끝났나.</summary>
public enum HitVerdict
{
    /// <summary>
    /// 몸이 판정 모양의 <b>가로 범위 밖</b>이라 안 닿았다 — 멀어서 피한 것이다(그 거리를 대시가 만들었을 수도 있다).
    ///
    /// <para>
    /// 모양 <b>안쪽의 빈 곳</b>(<see cref="MissedByGap"/>)과 <b>따로</b> 둔다 (이슈 #46 · #59). 한 갈래였을 때는
    /// 파고들어 피한 것과 도망쳐 피한 것이 계측에서 같은 한 점이었다 — 그 둘은 성향이 정반대고
    /// 봉인할 것도 정반대라, 뭉개면 정반대 변종이 뽑힌다.
    /// </para>
    /// </summary>
    MissedTooFar,

    /// <summary>
    /// 판정 모양의 외곽 상자 <b>안인데 빈 칸</b>이라 안 닿았다 — 초승달 안쪽 같은 곳 (이슈 #59 · 설계 §7.1).
    /// 옛 "안쪽 주머니"(<c>MissedTooClose</c>, 이슈 #46)가 이것의 한 경우다: 좌우 대칭 띠 두 장 사이의 빈 곳이다.
    /// </summary>
    MissedByGap,

    /// <summary>높이가 어긋났다 — 점프로 넘었거나 대공 아래 서 있었다.</summary>
    MissedByHeight,

    /// <summary>맞았다.</summary>
    Hit,

    /// <summary>닿았지만 무적이 먹었다 — <b>대시로</b> 피한 것이다.</summary>
    Dodged,

    /// <summary>
    /// 닿았지만 <b>패리</b>가 받아쳤다 — 누름에서 <c>parry_precise_window</c> 안에 판정이 섰다.
    /// 피해 0 이고, <b>어느 타든</b> 보스가 탈진한다 (#72 · 설계 §4.3) — 하던 패턴이 그 자리에서 끊긴다.
    /// 전에는 가드 불가인 마무리를 받아쳤을 때만 굳었다(이슈 #53).
    /// </summary>
    Parried,

    /// <summary>
    /// 닿았고 <b>가드</b>가 받아냈다 (이슈 #47 · #53). 피해는 <c>guard_chip_ratio</c> 만 흘러 들어오고
    /// 값은 <b>스태미나</b>로 낸다 — 그 값이 피해에 비례하므로 무거운 한 방이 가드를 깬다.
    ///
    /// <para>
    /// ↓ 를 누르고 있던 판정이 여기로 온다 (설계 §5.2). 창을 놓친 패리는 여기가 아니라 <see cref="Hit"/> 다 (설계 §5.3).
    /// </para>
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
    public static HitVerdict Resolve(Fighter fighter, Placement at, HitBox box, PatternTags tags)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(tags);

        // 몸통을 모양에 댄다 (이슈 #59 · 설계 §3.5). 안 닿으면 왜 안 닿았는지를 그대로 싣는다 —
        // 멀어서 · 넘어서 · 틈에 서서는 플레이어가 한 일이 서로 다르고, 그 셋을 가르는 것이 계측이다.
        switch (ShapeHit.Test(box.Shape, at, fighter.Body))
        {
            case ShapeContact.TooFar:
                return HitVerdict.MissedTooFar;
            case ShapeContact.ByHeight:
                return HitVerdict.MissedByHeight;
            case ShapeContact.ByGap:
                return HitVerdict.MissedByGap;
            default:
                break;
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

        // 패리가 가드보다 **먼저**다 — 둘은 다른 행동이라(설계 §5.3) 같은 틱에 둘 다 참일 수 없지만,
        // 무적 → 패리 → 가드 순을 고정해 둔다: 나중에 겹치는 수단이 생겨도 판정이 안 흔들린다.
        // 가드 불가를 창 안에서 받으면 그건 먹힌 것이 아니라 **설계가 시키는 답**이다(받아쳐라).
        //
        // 이슈 #47 은 반대 순서였다 — 그때는 가드가 패리 뒤에 섰다.
        if (tags.Parryable && Within(fighter.SinceParryPress, fighter.PreciseParryWindow, tags.ParryWindow))
        {
            return HitVerdict.Parried;
        }

        // ↓ 를 누르고 있으면 막는다 (설계 §5.2). 창을 놓친 패리는 여기 안 온다 — 패리와 가드는 다른 행동이라
        // 패리 커밋 중에는 가드가 아니고, 그 판정은 맨몸에 떨어진다(설계 §5.3).
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
