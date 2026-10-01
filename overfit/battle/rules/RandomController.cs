using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 무작위 조종기 (설계 2026-10-01 조각2 §3.2) — 열린 칸 중 하나를 시드 난수로 고른다. 시험과 학습의 첫 상대다. 좌표가 결정 번호라 같은 시드 · 같은 판이면
/// 같은 칸이다. 폭탄에는 반응하지 않는다 — 끊을지는 고른 칸이 정한다.
/// </summary>
public sealed class RandomController : IBossController
{
    private readonly ulong _seed;

    public RandomController(ulong seed) => _seed = seed;

    public bool ReactsToBombs => false;

    public int Decide(BossDecision decision)
    {
        System.ArgumentNullException.ThrowIfNull(decision);
        int open = 0;
        foreach (bool m in decision.Mask)
        {
            open += m ? 1 : 0;
        }

        int pick = Det.RollInt(_seed, Det.Domain.BossControl, System.Math.Max(1, open), k1: decision.Number);
        for (int i = 0; i < decision.Mask.Count; i++)
        {
            if (decision.Mask[i] && pick-- == 0)
            {
                return i;
            }
        }

        return BossActions.Wait;
    }
}
