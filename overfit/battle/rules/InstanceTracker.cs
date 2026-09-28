using System;
using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>사례 하나 — 패턴이 한 번 선 것과 그 사례에 맞았나(망의 라벨).</summary>
/// <param name="PatternId">그 패턴의 id.</param>
/// <param name="Hit">그 사례의 관측 중 하나라도 <c>Hit</c> · <c>GuardBroken</c> · <c>Grabbed</c> 인가.</param>
public readonly record struct PatternInstance(string PatternId, bool Hit);

/// <summary>
/// 판을 사례로 가른다 (#108 · 설계 2026-09-28 §4.3). 판을 미는 쪽이 틱마다(<c>BattleSim.Tick</c> 뒤) 지금의 패턴과 관측 수를 넘기고, 판이 끝나면
/// 관측 목록으로 라벨을 붙인다. 관측(<see cref="DodgeEvent"/>)의 모양은 안 바꾼다 — 사례의 경계는 판 밖에서 지금 패턴의 바뀜으로만 잰다.
///
/// <para>
/// <b>공장과 게임이 이 한 자리를 같이 쓴다</b>(#114). 공장은 학습의 라벨을, 게임(<c>Battle</c>)은 시도 기록의 사례를 여기서 짓는다 — sim-to-real 이
/// 사람의 사례를 봇의 사례와 같은 정의로 견줘야 해서다. 사람의 기록만 보고 파이썬이 따로 가르면 같은 패턴이 연달아 선 사례의 경계를 모른다(좁힌
/// 명부에서는 흔하다) — 그래서 처음엔 공장에만 있던 이 파일을 규칙 층으로 옮겼다.
/// </para>
///
/// <para>
/// <b>사례는 패턴이 서는 틱에 선다</b> — 지금 패턴이 다른 값(대개 null)에서 id 로 바뀌는 틱이다. 판은 패턴 사이에 늘 쉬는 틱을 두어 같은 패턴이
/// 연달아 뽑혀도 갈린다(uniform 은 같은 칸을 이어 뽑는다). 사례의 관측은 그 사례가 선 틱부터 다음 사례가 서기 전까지 나온 관측 중 <b>그 id 인 것</b>이다
/// — 앞 패턴의 판정 창이 쉬는 틱까지 남아 늦게 나온 관측은 id 가 가른다.
/// </para>
///
/// <para>
/// <b>끝나지 않은 사례는 버린다</b> — 판이 그 사례 도중에 끝났는데(보스가 쓰러짐 · 시간 초과) 안 맞았으면 "끝까지 버텼나" 를 모른다. 맞아서 쓰러진
/// 사례는 1 로 남는다. 보스가 탈진해 끊긴 사례는 패턴이 null 로 돌아가므로 끝난 사례다 — 파이터가 이긴 교환이라 0 이다.
/// </para>
/// </summary>
public sealed class InstanceTracker
{
    /// <summary>사례마다 (id, 그 사례가 선 틱 앞의 관측 수) — 그 수가 사례의 관측이 시작하는 번호다.</summary>
    private readonly List<(string Id, int FirstEvent)> _starts = new();

    /// <summary>지난 틱의 패턴 — 바뀜을 잰다.</summary>
    private string? _pattern;

    /// <summary>지난 틱이 끝난 자리의 관측 수 — 이번 틱에 사례가 서면 그 사례의 관측이 여기서 시작한다(선 틱에 난 관측도 든다).</summary>
    private int _eventsBefore;

    /// <summary>한 틱을 본다 — <c>BattleSim.Tick</c> 뒤에 그 판의 <c>Boss.CurrentPattern</c> 과 <c>Events.Count</c> 를 넘긴다.</summary>
    public void Observe(string? pattern, int eventCount)
    {
        if (pattern is not null && !string.Equals(pattern, _pattern, StringComparison.Ordinal))
        {
            _starts.Add((pattern, _eventsBefore));
        }

        _pattern = pattern;
        _eventsBefore = eventCount;
    }

    /// <summary>
    /// 판이 끝난 뒤 사례들을 선 순서로 낸다 — <paramref name="events"/> 는 그 판의 관측 전부(<c>BattleSim.Events</c>)다. 마지막 사례는 판이 끝날 때
    /// 패턴이 아직 서 있었으면 끝나지 않은 것이다 — 맞았을 때만 남긴다.
    /// </summary>
    public List<PatternInstance> Finish(IReadOnlyList<DodgeEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var instances = new List<PatternInstance>(_starts.Count);
        for (int i = 0; i < _starts.Count; i++)
        {
            (string id, int first) = _starts[i];
            bool last = i == _starts.Count - 1;
            int end = last ? events.Count : _starts[i + 1].FirstEvent;
            bool hit = false;
            for (int k = first; k < end && !hit; k++)
            {
                hit = string.Equals(events[k].PatternId, id, StringComparison.Ordinal) && IsHit(events[k].Verdict);
            }

            bool finished = !last || _pattern is null;
            if (finished || hit)
            {
                instances.Add(new PatternInstance(id, hit));
            }
        }

        return instances;
    }

    /// <summary>피해를 제대로 받았거나 붙들렸다 — 막아 낸 가드(칩 피해)는 막은 것이다(설계 §4.3).</summary>
    public static bool IsHit(HitVerdict verdict) => verdict is HitVerdict.Hit or HitVerdict.GuardBroken or HitVerdict.Grabbed;
}
