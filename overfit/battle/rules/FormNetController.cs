using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 게임의 망 보스 (설계 2026-10-01 조각7 · 우산 §2 · §6) — 형태(페이즈)마다 다른 정책망으로 고른다. 1페이즈는 거의 안 배운 망, 2페이즈는 중간, 3페이즈는 최종이다
/// (<c>overfit/data/boss_net/</c> · <c>picks.json</c>). 형태는 결정이 보는 판(<see cref="BossSight.Form"/>)에서 읽으므로 전환이 끝나는 틱의 자유로워짐부터 새 망이다.
///
/// <para>
/// 뽑기는 학습과 같은 분포다 — 열린 칸의 소프트맥스에서 시드로(<see cref="MaskedSampler"/> · exp 는 <see cref="DetMath.Exp"/>). 가장 높은 칸만 고르면 거의 안 배운
/// 1페이즈가 한 칸만 되풀이해 "멍청한" 보스가 아니라 "고장 난" 보스가 된다. 폭탄 반응 장치는 안 켠다 — 끊을지는 망이 고른다.
/// </para>
/// </summary>
public sealed class FormNetController : IBossController
{
    private readonly IReadOnlyList<PolicyNet> _forms;
    private readonly BossActions _actions;
    private readonly ulong _seed;

    /// <param name="forms">형태마다의 망 — 형태 1 부터 순서대로.</param>
    /// <param name="actions">칸 배치 — 로그의 이름.</param>
    /// <param name="seed">뽑기의 시드.</param>
    public FormNetController(IReadOnlyList<PolicyNet> forms, BossActions actions, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentNullException.ThrowIfNull(actions);
        if (forms.Count == 0)
        {
            throw new ArgumentException("형태의 망이 하나도 없다", nameof(forms));
        }

        _forms = forms;
        _actions = actions;
        _seed = seed;
    }

    public bool ReactsToBombs => false;

    public bool WantsObservation => true;

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

        int form = Math.Clamp(decision.Sight.Form, 1, _forms.Count);
        IReadOnlyList<double> obs = decision.Observation ?? throw new InvalidOperationException("망 조종기에 관측이 안 왔다 — WantsObservation");
        (double[] logits, double value) = _forms[form - 1].Forward(obs);
        (int action, double logProb) = MaskedSampler.Sample(logits, mask, Det.Roll01(_seed, Det.Domain.BossControl, k1: decision.Number));
        if (Log.IsEnabled(LogLevel.Debug))
        {
            Log.Debug("boss", $"net form={form} act={_actions.Name(action)} p={DetMath.Exp(logProb):0.000} value={value:+0.000;-0.000} tick={decision.Sight.Tick}");
        }

        return action;
    }
}
