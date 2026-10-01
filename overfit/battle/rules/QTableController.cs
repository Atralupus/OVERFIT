using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// Q 표 보스 (설계 2026-10-01 조각8 §1) — 비교 관문의 대조군. 결정마다 관측을 칸 키로 잘라 표에서 고른다(<see cref="QTable.Pick"/>). 열린 칸이 하나뿐인
/// 결정(자유로워짐)은 적지 않는다 — 망과 같다. 폭탄 반응 장치는 안 켠다(망과 같은 조건).
/// </summary>
public sealed class QTableController : IBossController
{
    private readonly QTable _table;
    private readonly BossObservation _layout;
    private readonly ulong _seed;
    private readonly double _epsilon;
    private readonly List<NetStep> _steps = new();
    private readonly List<string> _keys = new();

    /// <param name="table">표 — 학습 중이면 얼린 표다(바퀴 끝에 학습기가 고친다).</param>
    /// <param name="layout">관측의 칸 배치.</param>
    /// <param name="seed">뽑기의 시드.</param>
    /// <param name="epsilon">탐색의 확률 — 관문은 0.</param>
    public QTableController(QTable table, BossObservation layout, ulong seed, double epsilon)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(layout);
        _table = table;
        _layout = layout;
        _seed = seed;
        _epsilon = epsilon;
    }

    public bool ReactsToBombs => false;

    public bool WantsObservation => true;

    /// <summary>적은 결정들 — 일꾼이 보상을 붙인다.</summary>
    public IReadOnlyList<NetStep> Steps => _steps;

    /// <summary>결정마다의 칸 키 — <see cref="Steps"/> 와 같은 순서.</summary>
    public IReadOnlyList<string> Keys => _keys;

    public int Decide(BossDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        IReadOnlyList<bool> mask = decision.Mask;
        int open = 0;
        int only = BossActions.Wait;
        for (int i = 0; i < mask.Count; i++)
        {
            if (mask[i])
            {
                open++;
                only = i;
            }
        }

        if (open <= 1)
        {
            return only;
        }

        IReadOnlyList<double> obs = decision.Observation ?? throw new InvalidOperationException("Q 표 조종기에 관측이 안 왔다 — WantsObservation");
        string key = QTable.Key(obs, _layout, _table.Shape);
        int action = _table.Pick(key, mask, _seed, decision.Number, _epsilon);
        var maskCopy = new bool[mask.Count];
        for (int i = 0; i < maskCopy.Length; i++)
        {
            maskCopy[i] = mask[i];
        }

        _steps.Add(new NetStep(decision.Sight.Tick, obs, maskCopy, action, 0, 0));
        _keys.Add(key);
        return action;
    }
}
