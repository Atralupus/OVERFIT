using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>망 조종기가 적은 결정 하나 (설계 2026-10-01 조각4 §4) — 학습의 일꾼이 보상을 붙여 쓴다.</summary>
/// <param name="Tick">판의 틱 — 보상을 다음 줄까지의 체력 변화로 잰다.</param>
/// <param name="Observation">관측.</param>
/// <param name="Mask">열린 칸.</param>
/// <param name="Action">뽑은 칸.</param>
/// <param name="LogProb">뽑은 칸의 로그 확률.</param>
/// <param name="Value">가치망의 예상.</param>
public sealed record NetStep(int Tick, IReadOnlyList<double> Observation, IReadOnlyList<bool> Mask, int Action, double LogProb, double Value);

/// <summary>
/// 망 조종기 (설계 2026-10-01 조각4 §4) — 결정마다 정책망을 돌려 열린 칸의 소프트맥스에서 <c>Det.Roll01(seed, BossControl, k1: 결정 번호)</c> 로 뽑는다.
/// 같은 시드 · 같은 판이면 같은 칸이다. 가중치가 없으면(첫 바퀴) 열린 칸에 같은 확률이다. 열린 칸이 하나뿐인 결정(자유로워짐)은 적지 않는다 — 고를 것이
/// 없다. 폭탄에는 반응 장치를 안 켠다 — 끊을지는 망이 고른다.
///
/// <para>
/// <b>학습의 일꾼이 쓴다</b> — 결정을 적는다. 게임은 형태마다의 망을 쓰는 <see cref="FormNetController"/> 다(뽑기는 같은 <see cref="MaskedSampler"/>).
/// </para>
/// </summary>
public sealed class NetController : IBossController
{
    private readonly PolicyNet? _net;
    private readonly ulong _seed;
    private readonly List<NetStep> _steps = new();

    /// <param name="net">정책망 — 없으면 열린 칸에 같은 확률(가치 0).</param>
    /// <param name="seed">뽑기의 시드.</param>
    public NetController(PolicyNet? net, ulong seed)
    {
        _net = net;
        _seed = seed;
    }

    public bool ReactsToBombs => false;

    public bool WantsObservation => true;

    /// <summary>적은 결정들, 순서대로.</summary>
    public IReadOnlyList<NetStep> Steps => _steps;

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

        IReadOnlyList<double> obs = decision.Observation ?? throw new InvalidOperationException("망 조종기에 관측이 안 왔다 — WantsObservation");
        (double[]? logits, double value) = _net is null ? (null, 0.0) : _net.Forward(obs);

        (int action, double logProb) = MaskedSampler.Sample(logits, mask, Det.Roll01(_seed, Det.Domain.BossControl, k1: decision.Number));
        var maskCopy = new bool[mask.Count];
        for (int i = 0; i < maskCopy.Length; i++)
        {
            maskCopy[i] = mask[i];
        }

        _steps.Add(new NetStep(decision.Sight.Tick, obs, maskCopy, action, logProb, value));
        return action;
    }
}
