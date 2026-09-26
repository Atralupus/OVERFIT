using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 움직임이 한 틱에 받는 것 (설계 §8.1). 파이터의 X 는 <c>BattleSim</c> 이 넣는다 — 파이터가 이번 틱을 먼저 움직인 뒤의
/// 값이다. 러너는 플레이어를 모르므로(그래서 패턴 테스트가 플레이어 없이 돈다) 움직임을 돌리는 것도 러너가 아니다.
/// </summary>
/// <param name="BossX">보스 발 중심 x.</param>
/// <param name="BossY">보스 발바닥 높이.</param>
/// <param name="Facing">보스가 보는 쪽.</param>
/// <param name="FighterX">파이터 발 중심 x — 이번 틱을 민 뒤다.</param>
/// <param name="Tick">움직임이 시작된 뒤의 틱 — 시작한 틱이 0 이다.</param>
public readonly record struct MotionContext(double BossX, double BossY, int Facing, double FighterX, int Tick);

/// <summary>움직임이 한 틱에 내는 것 — 보스가 설 자리와 보는 쪽, 끝났나, 패턴 시계를 세우나.</summary>
/// <param name="X">보스 발 중심 x.</param>
/// <param name="Y">보스 발바닥 높이.</param>
/// <param name="Facing">보스가 볼 쪽. 0 이면 그대로 둔다.</param>
/// <param name="Finished">이 틱으로 움직임이 끝났나. 끝난 틱의 자리가 곧 선 자리다.</param>
/// <param name="HoldClock">
/// 다음 틱에 패턴 시계를 세우나 (설계 §8.1). 참인 동안 러너는 시계를 안 밀고 뒤 단계에 안 든다 — 도착 시각이
/// 파이터 자리에 달린 움직임(돌진 · 5번 PR)이 뒤 단계를 기다리게 하는 자리다. 도약은 늘 거짓이다.
/// </param>
public readonly record struct MotionStep(double X, double Y, int Facing, bool Finished, bool HoldClock);

/// <summary>움직임이 판을 세울 때 받는 것 — 보스가 설 수 있는 가로 범위와, 보스와 파이터의 몸 둘 폭.</summary>
/// <param name="MinX">보스 중심의 왼쪽 끝 (반폭).</param>
/// <param name="MaxX">보스 중심의 오른쪽 끝 (아레나 폭 − 반폭).</param>
/// <param name="Standoff">보스 반폭 + 파이터 반폭 — 두 몸이 막 닿는 중심 사이 거리.</param>
public readonly record struct MotionBounds(double MinX, double MaxX, double Standoff);

/// <summary>
/// 보스의 움직임 하나 (설계 §8.1). 움직임을 단 단계에 들 때 하나가 서고, 스스로 끝났다고 말할 때까지 틱마다 불린다 —
/// 도약은 뜬 시간 뒤(그 사이의 단계들을 건너 착지까지), 돌진(5번 PR)은 닿을 때.
/// </summary>
public interface IBossMotion
{
    MotionStep Tick(MotionContext context);
}

/// <summary>
/// 움직임 등록표 — id → 구현 (CLAUDE.md §2). 수치는 타임라인 단계의 <c>motion</c> 에 있고(<see cref="MotionDef"/>),
/// 움직임을 하나 더할 때 이 표에 한 줄을 더한다 — 러너와 <c>BattleSim</c> 은 안 연다.
/// </summary>
public static class BossMotions
{
    private static readonly Dictionary<string, Func<MotionDef, MotionBounds, IBossMotion>> _table =
        new(StringComparer.Ordinal)
        {
            ["leap"] = (def, bounds) => new LeapMotion(def, bounds),
        };

    /// <summary>등록된 id 들 — 데이터 테스트가 <c>patterns.json</c> 의 <c>motion.id</c> 를 여기와 대 본다.</summary>
    public static IReadOnlyCollection<string> Ids => _table.Keys;

    /// <summary>움직임 하나를 세운다. 모르는 id 면 null — 부르는 쪽이 <c>[E]</c> 를 남긴다.</summary>
    public static IBossMotion? Create(MotionDef def, MotionBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(def);
        return _table.TryGetValue(def.Id, out Func<MotionDef, MotionBounds, IBossMotion>? make) ? make(def, bounds) : null;
    }
}
