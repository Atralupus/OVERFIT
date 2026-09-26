using System;

namespace Overfit.Battle.Rules;

/// <summary>판정 하나가 파이터에게 어떻게 끝났나.</summary>
public enum HitVerdict
{
    /// <summary>
    /// 몸이 판정 모양의 <b>끝 너머</b>라 안 닿았다 — 멀어서 피한 것이다(그 거리를 대시가 만들었을 수도 있다). 끝은 모양 전체의
    /// 가로 범위이고, 그 안이라도 <b>몸 높이에서</b> 모양이 뻗은 앞끝 너머 · 보스 등 뒤로 뒤끝 너머면 여기다(#72 ·
    /// <see cref="ShapeHit.Test"/>).
    ///
    /// <para>
    /// 모양 <b>안쪽의 빈 곳</b>(<see cref="MissedByGap"/>)과 <b>따로</b> 둔다 (이슈 #46 · #59). 한 갈래였을 때는
    /// 파고들어 피한 것과 도망쳐 피한 것이 계측에서 같은 한 점이었다 — 그 둘은 성향이 정반대고
    /// 봉인할 것도 정반대라, 뭉개면 정반대 패턴이 뽑힌다.
    /// </para>
    /// </summary>
    MissedTooFar,

    /// <summary>
    /// 판정 모양의 <b>안쪽 빈 칸</b>이라 안 닿았다 — 초승달 안쪽 같은 곳 (이슈 #59 · 설계 §7.1). 몸 높이에서 모양의 끝 안쪽이거나,
    /// 보스 중심과 앞 궤적 사이(품 안)다.
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
    /// 닿았고 가드가 <b>깨졌다</b> (이슈 #47). 스태미나가 모자랐다 — 가드가 깨지는 길은 그것 하나다(설계 §5.2 · 옛
    /// <c>guard_break</c> 판정은 #72 에서 걷었다). <b>전액</b>이고 파이터가 <b>탈진</b>한다(<c>exhaust_seconds</c> · #71 · 설계 §5.5).
    /// </summary>
    GuardBroken,
}

/// <summary>
/// 파이터가 판정을 <b>무엇으로 받는가</b> — 몸이 모양에 닿았을 때 어느 갈래로 가나 (#72 · 설계 §6.1).
/// 판정 보기의 몸통 색이 이것을 칠한다.
/// </summary>
public enum Defense
{
    /// <summary>맨몸 — 닿으면 맞는다.</summary>
    None,

    /// <summary>대시 무적 — 파이터의 무적과 판정의 대시 창 중 좁은 쪽 안이다.</summary>
    Invulnerable,

    /// <summary>패리 창 — 판정이 패리를 받고, 파이터와 판정의 창 중 좁은 쪽 안이다.</summary>
    Parrying,

    /// <summary>가드 — ↓ 를 누르고 땅에 서 있다.</summary>
    Guarding,
}

/// <summary>
/// 판정 하나를 파이터에게 대본다. <b>상태를 안 바꾼다</b> — 판단만 하고 체력을 깎는 것(과 패리 · 가드 · 붕괴의 부작용)은
/// <c>BossSwings.ApplyVerdict</c> 다. 그래야 같은 판정을 여러 번 물어봐도 답이 같고 테스트가 쉽다.
///
/// <para>
/// 판정 결과가 <b>무엇으로 피했는지까지</b> 말해야 한다. 안 맞은 이유가 거리인지 높이인지를
/// 여기서 버리면 관측을 짓는 쪽(<c>BossSwings.BuildEvent</c>)은 "그 순간 무슨 행동 중이었나" 로 추측할 수밖에 없고,
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

        switch (Effective(fighter, tags))
        {
            case Defense.Invulnerable:
                return HitVerdict.Dodged;

            case Defense.Parrying:
                return HitVerdict.Parried;

            case Defense.Guarding:
                // 가드가 깨지는 길은 **스태미나 하나**다 (설계 §5.2) — 옛 guard_break 판정(빨간 마무리)은 걷었다(#72).
                return fighter.Stamina < fighter.GuardStaminaCost(box.Damage)
                    ? HitVerdict.GuardBroken
                    : HitVerdict.Guarded;

            default:
                return HitVerdict.Hit;
        }
    }

    /// <summary>
    /// 이 파이터가 이 태그의 판정 앞에서 <b>실제로</b> 무엇으로 받나 (#72 · 설계 §6.1). <see cref="Resolve"/> 가 몸이 닿은 뒤
    /// 이것으로 갈래를 고르고, 판정 보기가 몸통 색을 이것으로 칠한다 — 한 자리에서 정해야 색과 판정이 다른 말을 안 한다.
    /// <paramref name="tags"/> 가 null(대 본 판정이 없다)이면 파이터 쪽 상태 그대로다.
    /// </summary>
    public static Defense Effective(Fighter fighter, PatternTags? tags)
    {
        ArgumentNullException.ThrowIfNull(fighter);

        // 유효 창은 **패턴과 캐릭터 중 좁은 쪽**이다.
        //
        // 전에는 캐릭터 쪽만 봤다. 대공찌르기가 parry_window 0.10 을(그때 캐릭터의 0.12 보다 좁게)
        // 선언해도 아무 일도 안 일어났는데 — 그 숫자는 망의 입력이 된다. 거짓말하는 숫자는
        // 없는 숫자보다 나쁘다. "빠른 공격은 패리하기 더 어렵다" 는 진짜 설계 레버라
        // 태그를 지우는 대신 물게 했다.
        if (fighter.Action == FighterAction.Dash
            && Within(fighter.ActionElapsed, fighter.DashIFrames, tags?.DashWindow ?? double.PositiveInfinity))
        {
            return Defense.Invulnerable;
        }

        // 패리가 가드보다 **먼저**다 — 둘은 다른 행동이라(설계 §5.3) 같은 틱에 둘 다 참일 수 없지만,
        // 무적 → 패리 → 가드 순을 고정해 둔다: 나중에 겹치는 수단이 생겨도 판정이 안 흔들린다.
        //
        // 이슈 #47 은 반대 순서였다 — 그때는 가드가 패리 뒤에 섰다.
        if ((tags?.Parryable ?? true)
            && Within(fighter.SinceParryPress, fighter.PreciseParryWindow, tags?.ParryWindow ?? double.PositiveInfinity))
        {
            return Defense.Parrying;
        }

        // ↓ 를 누르고 있으면 막는다 (설계 §5.2). 창을 놓친 패리는 여기 안 온다 — 패리와 가드는 다른 행동이라
        // 패리 커밋 중에는 가드가 아니고, 그 판정은 맨몸에 떨어진다(설계 §5.3).
        //
        // parryable 태그는 여기서 **안 본다.** 패리를 못 받는 판정도 가드로는 막는다(점프 공격의 착지 · 설계 §4.2).
        return fighter.Guarding ? Defense.Guarding : Defense.None;
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
