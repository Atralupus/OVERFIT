using System;
using System.Collections.Generic;
using Overfit.Battle.Rules;

namespace Overfit.Battle.View;

/// <summary>
/// 바닥을 따라 퍼지는 흰 충격파의 <b>모양</b> (#83) — 그리는 노드(<c>LandingWave</c>)의 순수 절반이다. Godot 을 모르므로 규칙 테스트
/// 프로젝트가 링크해 돌린다(<c>FloorWaveTests</c>) — <c>core/Log.cs</c> 와 <c>LogSink.cs</c> 의 가름과 같다.
///
/// <para>
/// <b>모양은 규칙이 이 틱에 대 본 보스 판정 사각형에서 읽는다</b>(<c>BattleSim.BossTestedRects</c>). 높이는 그 판정의 높이, 퍼지는 끝은 그
/// 판정의 가로 끝을 아레나 <c>[0, 폭]</c> 으로 자른 것이다 — "보이는 것이 곧 맞는 것". 점프 공격의 착지는 <c>band [0, 1920, 0, 60]</c> 인데
/// 그 숫자를 여기 옮겨 적지 않는다: 적으면 데이터를 고치는 날 띠가 판정과 다른 높이를 그린다. 판정 보기(<c>HitboxDebug</c>)가 사각형을 받아
/// 그리기만 하는 것과 같은 이유다.
/// </para>
///
/// <para>
/// <b>아레나로 자른다</b> (리뷰 m4). 착지 띠는 보스 발 ± 1920 이라 아레나 밖으로 한참 나간다 — 거기는 아무도 못 서고 화면에도 없다.
/// 자르기 전에는 앞머리가 그 끝을 향해 달려 화면에서 두세 프레임 만에 사라졌고, 퍼지는 것이 아니라 번쩍인 것으로 읽혔다.
/// </para>
///
/// <para>
/// <b>언제 서나 — 바닥 전체를 덮는 판정이다.</b> 바닥에 닿은(아래끝이 바닥) 사각형들을 가로로 이어 붙여, 이어진 한 줄이 바닥
/// <c>[0, 폭]</c> 을 다 덮으면 그것이 충격파다. 패턴 id(<c>점프 공격</c>)로 가르지 않는다 — 2단계의 점프 ×3(설계 §4.8)처럼 같은 띠를
/// 쓰는 패턴이 오면 저절로 서고, 바닥을 다 덮지 못하는 칼(3연격)에는 안 선다. 이어졌는지를 보는 것은 가운데가 빈 띠(안쪽 주머니)
/// 때문이다: 양끝만 보면 바닥을 다 덮지만 보스 발밑이 비어, 거기서 퍼지는 충격파는 안 맞는 자리를 칠해 보인다.
/// </para>
/// </summary>
/// <param name="Origin">퍼지기 시작하는 x (월드) — 착지한 보스의 발 중심.</param>
/// <param name="Left">판정의 왼끝 x (월드) — 아레나 안으로 자른 것. 왼쪽 앞머리가 다 퍼지면 여기 닿고, 밑깔개도 여기까지다.</param>
/// <param name="Right">판정의 오른끝 x (월드) — 아레나 안으로 자른 것.</param>
/// <param name="Height">판정의 높이(px) — 바닥에서 위로. 이어진 사각형 중 가장 낮은 윗끝이라 띠 안의 모든 자리가 실제로 맞는다.</param>
public readonly record struct FloorWave(double Origin, double Left, double Right, double Height)
{
    /// <summary>바닥의 높이. 규칙의 좌표계가 정한 값이다(위가 + · 바닥 0 · <c>Arena</c>) — 노브가 아니다.</summary>
    private const double _floor = 0;

    /// <summary>
    /// 이 틱에 대 본 보스 판정 사각형(월드)에서 바닥 전체를 덮는 띠를 찾는다. 없으면 null — 쉬는 틱 · 칼 판정 · 공중의 띠.
    /// </summary>
    /// <param name="tested">규칙이 이 틱에 파이터에게 대 본 보스 판정 사각형 (월드).</param>
    /// <param name="feetX">보스의 발 중심 x — 충격파가 여기서 출발한다. 아레나로 자른 판정 밖이면 그 안으로 당긴다.</param>
    /// <param name="floorWidth">바닥의 폭(아레나 폭). 이만큼을 다 덮어야 바닥 전체다.</param>
    public static FloorWave? Find(IReadOnlyList<HitRect> tested, double feetX, double floorWidth)
    {
        ArgumentNullException.ThrowIfNull(tested);

        // 대부분의 틱은 대 본 판정이 없다 — 목록을 만들지 않고 돌아간다.
        List<HitRect>? onFloor = null;
        foreach (HitRect r in tested)
        {
            if (r.Y0 <= _floor && r.Y1 > _floor)
            {
                (onFloor ??= new List<HitRect>()).Add(r);
            }
        }

        if (onFloor is null)
        {
            return null;
        }

        onFloor.Sort(static (a, b) => a.X0.CompareTo(b.X0));

        // 왼쪽부터 이어 붙인다. 가장자리만 닿아도 이어진 것이다 — 규칙의 겹침(HitRect.Overlaps)이 경계를 맞은 것으로 치는 것과 같다.
        // 좌우 대칭 띠의 두 장은 보스 발 중심에서 정확히 맞닿는다.
        HitRect run = onFloor[0];
        for (int i = 1; i <= onFloor.Count; i++)
        {
            if (i < onFloor.Count && onFloor[i].X0 <= run.X1)
            {
                HitRect next = onFloor[i];
                run = new HitRect(run.X0, Math.Max(run.X1, next.X1), run.Y0, Math.Min(run.Y1, next.Y1));
                continue;
            }

            if (run.X0 <= 0 && run.X1 >= floorWidth)
            {
                // 아레나 밖은 못 서고 안 보인다 — 끝을 아레나로 자른다. 출발점도 자른 끝 사이다: 밖에서 출발하면 앞머리가 거꾸로 달린다.
                double left = Math.Max(run.X0, 0);
                double right = Math.Min(run.X1, floorWidth);
                return new FloorWave(Math.Clamp(feetX, left, right), left, right, run.Y1 - _floor);
            }

            if (i < onFloor.Count)
            {
                run = onFloor[i];
            }
        }

        return null;
    }

    /// <summary>
    /// 퍼진 몫(<paramref name="progress"/> — 퍼지는 시간 중 지난 몫, 0 ~ 1)에서 두 앞머리의 x. 0 이면 둘 다 발밑이고 1 이면 판정의 양끝이다.
    ///
    /// <para>
    /// <b>고르게 달린다</b>(지난 몫만큼 간다). 전에는 처음이 빠른 곡선(1 − (1 − t)²)이었는데 창(8틱)이 짧아 한 프레임에 네 몫 중 하나를
    /// 가 버렸고, 끝이 화면 밖이던 때와 겹쳐 퍼지는 것이 두세 프레임만 보였다(리뷰 m4). "첫 틱에 다 맞는다" 는 밑깔개(<c>LandingWave</c>)가
    /// 말하므로 앞머리는 창 내내 달리는 것이 보이기만 하면 된다 — <c>FloorWaveTests</c> 가 창의 틱마다 간 몫을 잰다.
    /// </para>
    /// </summary>
    public (double Left, double Right) Fronts(double progress)
    {
        double t = Math.Clamp(progress, 0, 1);
        return (Origin + ((Left - Origin) * t), Origin + ((Right - Origin) * t));
    }
}
