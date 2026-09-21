using System;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 헤드리스로 전투를 끝까지 몰고 가는 <b>최소</b> 봇. 잘하라고 만든 것이 아니라
/// "전투가 끝까지 도는가"를 창 없이 보려고 만든 것이다.
///
/// <para>
/// ⚠ <b>이것은 학습 데이터용 봇이 아니다.</b> 파라미터로 성향을 바꾸는 봇 함대는 스펙 5 의 몫이다.
/// 다만 회피 수단 셋을 <b>전부</b> 쓰도록 만든다 — 한 수단만 쓰는 봇은 나머지 축을 영원히 0 으로 만들고,
/// 그러면 계측이 제대로 도는지조차 확인할 수 없다.
/// </para>
///
/// <para>
/// 난수는 <see cref="Det"/> 로만 뽑는다. 벽시계도 <c>Random</c> 도 없으므로 같은 시드는 같은 판이다.
/// </para>
/// </summary>
public sealed class BotPolicy
{
    /// <summary>대시·패리를 걸 창(초). 무적창(0.14) · 패리창(0.10~0.12)보다 좁게 잡아
    /// 판정이 서는 순간까지 창이 열려 있게 한다 — 일찍 걸면 판정 전에 창이 닫혀 그냥 맞는다.</summary>
    private const double _lateReact = 0.10;

    private readonly ulong _seed;
    private int _decisions;

    public BotPolicy(ulong seed) => _seed = seed;

    /// <summary>이번 틱에 무엇을 할지. <see cref="BattleSim"/> 의 상태만 보고 정한다.</summary>
    public InputFrame Next(BattleSim sim)
    {
        ArgumentNullException.ThrowIfNull(sim);

        double gap = Math.Abs(sim.Fighter.X - sim.Boss.X) - sim.Boss.HalfWidth;
        sbyte move = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);

        // 패턴이 돌고 있으면 셋 중 하나로 반응한다. 무엇을 고를지는 좌표 조회로 정한다 —
        // 호출 순서에 값이 끌려다니지 않아 같은 시드가 같은 판을 만든다.
        //
        // ⚠ CurrentPattern 만 보고 매 틱 다시 고르지 않는다 — 그러면 윈드업 내내 아무 때나
        // 걸어서 판정이 서기도 전에 무적창·패리창이 닫혀 버린다. NextActiveIn(남은 시간)을 봐서
        // 대시·패리는 판정 직전에 걸고, 점프는 미리 떠야 높이가 나니 곧장 쓴다.
        //
        // 행동 중(Dash·Parry·Attack)에는 새로 고르지 않는다 — 그건 Fighter.Begin 이 어차피
        // 무시하므로 막을 필요는 없지만, 접지 여부는 **걸지 않는다**: 공중에서도 대시·패리를
        // 다시 걸 수 있어야 점프가 늦게 뜬 판정을 막판에 대시로 덮을 수 있다.
        if (sim.Boss.CurrentPattern is not null)
        {
            if (sim.Fighter.Action != FighterAction.Idle)
            {
                return default;
            }

            _decisions++;
            int pick = Det.RollInt(_seed, Det.Domain.BotChoice, 3, k1: _decisions);
            if (pick != 1 && (sim.NextActiveIn is not double remaining || remaining > _lateReact))
            {
                // 아직 이르다 — 대시·패리를 지금 걸면 판정 전에 창이 닫힌다. 다음 틱에 다시 본다.
                return default;
            }

            return pick switch
            {
                0 => new InputFrame(0, false, Dash: true, false, false),
                1 => new InputFrame(0, Jump: true, false, false, false),
                _ => new InputFrame(0, false, false, Parry: true, false),
            };
        }

        // 쉬는 동안에는 붙어서 때린다.
        if (gap > sim.Fighter.AttackReach)
        {
            return new InputFrame(move, false, false, false, false);
        }

        return new InputFrame(0, false, false, false, Attack: true);
    }
}
