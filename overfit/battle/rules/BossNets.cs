using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 게임의 망 보스를 세운다 (설계 2026-10-01 조각7 §2 · §5) — 게임(<c>Battle</c>)과 데모(<c>BattleDemo</c>)가 이 한 자리에서 세운다. 따로 세우면 되살리기가 게임의 그
/// 시도와 다른 조종기로 돌 수 있다.
/// </summary>
public static class BossNets
{
    /// <summary>
    /// 형태마다의 망 JSON 으로 <see cref="FormNetController"/> 를 세운다. 망의 관측 · 칸 · 명부가 판과 다르면 <see cref="Overfit.Core.DataException"/> 이다.
    /// </summary>
    /// <param name="texts">망 JSON — 형태 1 부터.</param>
    /// <param name="sources">망의 이름(경로) — 틀렸을 때 어느 파일인지 말한다. <paramref name="texts"/> 와 길이가 같다.</param>
    /// <param name="roster">판의 명부 — 망이 배운 명부와 같아야 한다.</param>
    /// <param name="seed">뽑기의 시드 — 시도 시드다. 같은 시드면 같은 판이라 되살리기가 다시 선다.</param>
    public static IBossController Create(IReadOnlyList<string> texts, IReadOnlyList<string> sources, IReadOnlyList<string> roster, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(roster);
        if (texts.Count != sources.Count)
        {
            throw new ArgumentException($"망 {texts.Count} 개에 이름 {sources.Count} 개", nameof(sources));
        }

        var actions = new BossActions(roster);
        int obs = new BossObservation(roster.Count).Size;
        var nets = new PolicyNet[texts.Count];
        for (int i = 0; i < nets.Length; i++)
        {
            nets[i] = PolicyNet.Parse(texts[i], sources[i], obs, actions.Count, roster);
        }

        return new FormNetController(nets, actions, seed);
    }
}
