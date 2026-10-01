using System;

namespace Overfit.Battle.Rules;

/// <summary>
/// 결정 경로의 exp (설계 2026-10-01 조각7 §3) — 사칙연산과 정확한 연산(<see cref="Math.Round(double, MidpointRounding)"/> · <see cref="Math.ScaleB"/>)만 쓴다.
/// <see cref="Math.Exp"/> 는 플랫폼의 수학 라이브러리라 마지막 비트가 다를 수 있다 — 그러면 같은 시드에도 망 보스의 뽑기가 다른 칸으로 갈 수 있어 "같은 시드면 같은
/// 판"(CLAUDE.md §4)을 못 덮는다.
///
/// <para>
/// x = k·ln2 + r(|r| ≤ ln2/2) 로 줄이고 e^r 을 14차 테일러(호너)로, 2^k 를 지수 비트로(<see cref="Math.ScaleB"/>) 곱한다. ln2 는 두 조각(높은 쪽은 아래
/// 비트가 0)이라 k·ln2 의 빼기에서 자리를 안 잃는다. 오차는 <see cref="Math.Exp"/> 와 상대 1e-14 안이다(<c>GameNetTests</c>).
/// </para>
/// </summary>
public static class DetMath
{
    private const double _ln2Hi = 6.93147180369123816490e-01;
    private const double _ln2Lo = 1.90821492927058770002e-10;
    private const double _invLn2 = 1.44269504088896338700e+00;

    public static double Exp(double x)
    {
        if (double.IsNaN(x))
        {
            return x;
        }

        if (x < -745.2)
        {
            return 0;
        }

        if (x > 709.7)
        {
            return double.PositiveInfinity;
        }

        double k = Math.Round(x * _invLn2, MidpointRounding.ToEven);
        double r = (x - (k * _ln2Hi)) - (k * _ln2Lo);
        double p = 1;
        for (int n = 14; n >= 1; n--)
        {
            p = 1 + (r * p / n);
        }

        return Math.ScaleB(p, (int)k);
    }
}
