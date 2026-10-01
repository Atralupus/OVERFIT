using System;
using System.Collections.Generic;
using System.Globalization;

namespace Overfit.Battle.Rules;

/// <summary>
/// 결과 화면의 페이즈 줄 (설계 2026-10-01 조각1 §3) — 페이즈마다 걸린 시간. 페이즈의 시작은 앞 전환을 <b>시작한</b> 틱이다: 전환(무적 1.5초)은 뒤
/// 페이즈의 시간에 든다 — 유저에게 전환은 다음 페이즈가 시작되는 장면이다. 못 간 페이즈는 — 이다.
/// </summary>
public static class FormReport
{
    /// <summary>한 줄 — 형태가 하나면 빈 줄이다(결과 화면이 거른다).</summary>
    /// <param name="shifts">전환을 시작한 틱들(<see cref="BossForms.Shifts"/>).</param>
    /// <param name="ticks">판의 길이 — 마지막에 간 페이즈는 판이 끝난 틱까지다.</param>
    /// <param name="forms">형태 수(<see cref="BossForms.Count"/>).</param>
    public static string Line(IReadOnlyList<int> shifts, int ticks, int forms)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        if (forms <= 1)
        {
            return "";
        }

        var cells = new List<string>(forms);
        for (int f = 0; f < forms; f++)
        {
            if (f > shifts.Count)
            {
                cells.Add($"{f + 1} —");
                continue;
            }

            int from = f == 0 ? 0 : shifts[f - 1];
            int to = f < shifts.Count ? shifts[f] : ticks;
            cells.Add($"{f + 1} {Clock(to - from)}");
        }

        return "페이즈 " + string.Join(" · ", cells);
    }

    /// <summary>틱 → <c>분:초</c> — 60틱이 1초다(<see cref="BattleSim.Dt"/>). 초 아래는 버린다.</summary>
    private static string Clock(int ticks)
    {
        int seconds = ticks / 60;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }
}
