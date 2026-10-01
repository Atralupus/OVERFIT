using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 열린 칸의 소프트맥스에서 하나 뽑기 — 보스 망 조종기와 파이터 망 조종기가 같이 쓴다(설계 2026-10-01 조각4 §4 · 조각6). 로짓이 없으면 열린 칸에 같은 확률이다.
/// exp 는 <see cref="DetMath.Exp"/> 다(설계 2026-10-01 조각7 §3) — 사칙연산뿐이라 플랫폼이 달라도 같은 칸이다. 게임과 학습이 같은 함수를 쓴다.
/// </summary>
public static class MaskedSampler
{
    /// <param name="logits">칸마다 로짓 — 없으면 같은 확률.</param>
    /// <param name="mask">열린 칸.</param>
    /// <param name="u">[0, 1) 의 난수.</param>
    /// <returns>뽑은 칸과 그 로그 확률.</returns>
    public static (int Action, double LogProb) Sample(IReadOnlyList<double>? logits, IReadOnlyList<bool> mask, double u)
    {
        ArgumentNullException.ThrowIfNull(mask);
        double max = double.NegativeInfinity;
        int last = 0;
        for (int i = 0; i < mask.Count; i++)
        {
            if (mask[i])
            {
                max = Math.Max(max, logits?[i] ?? 0);
                last = i;
            }
        }

        var p = new double[mask.Count];
        double sum = 0;
        for (int i = 0; i < mask.Count; i++)
        {
            p[i] = mask[i] ? DetMath.Exp((logits?[i] ?? 0) - max) : 0;
            sum += p[i];
        }

        double left = u * sum;
        int action = last;
        for (int i = 0; i < mask.Count; i++)
        {
            if (!mask[i])
            {
                continue;
            }

            left -= p[i];
            if (left < 0)
            {
                action = i;
                break;
            }
        }

        return (action, Math.Log(p[action] / sum));
    }
}
