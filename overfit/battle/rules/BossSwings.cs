using System;
using System.Collections.Generic;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 보스의 <b>산 판정</b>과 그 <b>관측</b> — 창이 열려 있는 휘두름을 틱마다 몸에 대 보고, 끝난 휘두름마다
/// 관측(<see cref="DodgeEvent"/>)을 하나 남긴다 (이슈 #59 · 설계 §3.5).
///
/// <para>
/// <see cref="BattleSim"/> 에서 떼어 냈다 (#72 · 설계 §10 의 3번). 한 판을 미는 일(보스의 패턴 · 파이터의 칼 ·
/// 승패)과 산 판정을 대고 관측을 짓는 일은 따로 바뀐다 — 3번 PR 은 관측이 어느 틱을 말하는지(설계 §3.6 ①),
/// 탈진이 창을 끊는 일(§3.5 5)을 여기에 얹는다. 한 파일에 둘 때는 그것을 얹기 전에 이미 주석 빼고 355줄이었다
/// (CLAUDE.md §7).
/// </para>
/// </summary>
public sealed class BossSwings
{
    private readonly Fighter _fighter;
    private readonly Boss _boss;

    /// <summary>회피 수단마다의 시작 시각과 공 돌리기 (<see cref="DodgeCredit"/>). 관측을 지을 때 묻는다.</summary>
    private readonly DodgeCredit _credit;

    private readonly List<DodgeEvent> _events = new();

    /// <summary>
    /// 살아 있는 보스 판정들 (이슈 #59 · 설계 §3.5). 보통 0~1개다. 러너가 판정을 내는 틱에 들어오고,
    /// 몸에 닿거나 창이 닫히면 나간다.
    /// </summary>
    private readonly List<LiveSwing> _live = new();

    /// <summary>
    /// 이 틱에 파이터에게 대 본 보스 판정 — (모양, 놓은 자리). <b>기록만 한다</b> (이슈 #59 · 설계 §6.1).
    /// 사각형으로 펴는 것은 디버그 표시가 물을 때(<see cref="TestedRects"/>)다: 봇이 수백만 판을 돌리는
    /// 동안 이 목록은 용량을 다시 쓸 뿐 새로 할당하지 않는다.
    /// </summary>
    private readonly List<(HitShape Shape, Placement At)> _tested = new();

    /// <summary>관측을 짓는 틱의 번호 — <see cref="Resolve"/> 가 받는다.</summary>
    private int _tick;

    /// <summary>이 틱에 받아친 판정이 있었나 — <see cref="Resolve"/> 가 돌려준다.</summary>
    private bool _parried;

    public BossSwings(Fighter fighter, Boss boss, DodgeCredit credit)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(boss);
        ArgumentNullException.ThrowIfNull(credit);
        _fighter = fighter;
        _boss = boss;
        _credit = credit;
    }

    /// <summary>이 판에서 일어난 회피 관측 전부. <see cref="PlayerAxes.From"/> 에 그대로 넣는다.</summary>
    public IReadOnlyList<DodgeEvent> Events => _events;

    /// <summary>
    /// 이 틱에 규칙이 파이터에게 <b>대 본</b> 보스 판정 사각형 (월드) — 디버그 표시용 (이슈 #59 · 설계 §6.1).
    /// 표시가 이것을 받아 그리기만 하므로, 판정이 틀린 자리에 서면 화면도 그 틀린 자리를 보여 준다.
    /// </summary>
    public IReadOnlyList<HitRect> TestedRects
    {
        get
        {
            var rects = new List<HitRect>();
            foreach ((HitShape shape, Placement at) in _tested)
            {
                rects.AddRange(shape.Place(at));
            }

            return rects;
        }
    }

    /// <summary>
    /// 러너가 방금 낸 판정을 <b>살려 둔다</b> (이슈 #59). 창이 몇 틱이든 대는 곳은 <see cref="Resolve"/> 하나다.
    /// 태그와 패턴 id 를 지금 받아 두는 것은 러너가 이 틱 끝에 끝나면 패턴과 <c>CurrentPattern</c> 이
    /// 지워지기 때문이다 — 마지막 판정이 end 와 같은 틱에 서면 그 관측이 "?" 패턴으로 남는다.
    /// </summary>
    public void Open(HitBox box, PatternTags tags, string patternId) =>
        _live.Add(new LiveSwing(box, tags, patternId, BattleSim.TicksFor(box.ActiveSeconds)));

    /// <summary>
    /// 살아 있는 판정을 전부 이 틱에 대 본다. 끝난 것은 빼고 산 것은 순서대로 남긴다. <b>받아친 판정이 있었으면 true</b> —
    /// 보스를 탈진시키는 것은 부르는 쪽(<c>BattleSim</c> 의 탈진 루틴)이다: 같은 틱의 순서가 보스 판정 → 파이터의 칼 → 끊기라서다
    /// (설계 §3.5 5).
    /// </summary>
    /// <param name="tick">이 틱의 번호 — <c>BattleSim.Ticks</c>. 관측의 시각이 여기서 나온다.</param>
    public bool Resolve(int tick)
    {
        _tick = tick;
        _parried = false;
        _tested.Clear();
        var at = new Placement(_boss.X, _boss.Y, _boss.Facing);

        int kept = 0;
        for (int i = 0; i < _live.Count; i++)
        {
            LiveSwing swing = _live[i];
            _tested.Add((swing.Box.Shape, at));
            if (!Step(swing, at))
            {
                _live[kept++] = swing;
            }
        }

        _live.RemoveRange(kept, _live.Count - kept);
        return _parried;
    }

    /// <summary>
    /// 열린 창을 <b>관측 없이</b> 버린다 (#72 · 설계 §3.5 5). 보스가 탈진해 패턴이 끊기면 그 판정에는 결과가 없다 —
    /// 지어내면 창이 열린 첫 틱의 빗나간 이유(대개 거리)가 그대로 나가, 모양 안에 서 있던 사람도 "거리로 빗나갔다" 로
    /// 계측에 들어간다. 그 한 줄이 곧 시도 기록이라 망의 입력으로 간다. 그래서 로그 한 줄만 남긴다.
    /// </summary>
    /// <param name="tick">끊는 틱.</param>
    /// <param name="reason">왜 끊나 — <c>exhaust</c>(보스가 탈진했다).</param>
    public void Cut(int tick, string reason)
    {
        foreach (LiveSwing swing in _live)
        {
            Log.Debug("boss", $"cut_swing id={swing.PatternId} tick={tick} reason={reason}");
        }

        _live.Clear();
    }

    /// <summary>
    /// 살아 있는 판정 하나를 이 틱에 대 본다. 끝났으면 true.
    ///
    /// <para>
    /// 몸에 닿는 순간(맞음 · 패리 · 가드 · 붕괴) 그 휘두름은 끝난다 — <b>한 번 휘두르면 한 번만 맞는다.</b>
    /// 무적이 먹은 틱은 넘어가고 창은 계속 산다: 무적이 창보다 먼저 풀리면 그 뒤 틱에 맞는다(다크소울과 같다).
    /// 창이 닫힐 때까지 안 닿았으면 관측을 <b>하나</b> 남긴다 — 무적이 먹었으면 <b>그 틱에 지어 둔</b>
    /// 관측(<see cref="LiveSwing.DodgeSnapshot"/>), 아니면 마지막 틱의 빗나간 이유다.
    /// </para>
    /// </summary>
    private bool Step(LiveSwing swing, Placement at)
    {
        HitVerdict verdict = HitResolver.Resolve(_fighter, at, swing.Box, swing.Tags);
        swing.TicksLeft--;

        switch (verdict)
        {
            case HitVerdict.Hit or HitVerdict.Parried or HitVerdict.Guarded or HitVerdict.GuardBroken:
                Land(swing, verdict);
                return true;

            case HitVerdict.Dodged:
                // 처음 무적이 먹은 틱에서만 짓는다 (이슈 #59 · 리뷰 라운드 1) — 그 틱의 크레딧(대시
                // 시작 시각·방향)이 아직 살아 있다. 창이 몇 틱 더 사는 동안 다시 Dodged 여도 안 다시
                // 짓는다: 미뤘다 나중에 지으면 그새 대시가 끝나 DodgeCredit 의 대시 시각이 NaN 이 됐을 수 있고,
                // 그러면 "0초 전에 프레임 퍼펙트로 피했다" 는 거짓 관측이 나간다.
                swing.DodgeSnapshot ??= BuildEvent(swing, swing.Box, verdict);
                break;

            default:
                swing.LastMiss = verdict;
                break;
        }

        if (swing.TicksLeft > 0)
        {
            return false;
        }

        if (swing.DodgeSnapshot is { } snapshot)
        {
            Commit(snapshot);
        }
        else
        {
            Land(swing, swing.LastMiss);
        }

        return true;
    }

    /// <summary>
    /// 판정의 결과를 몸에 싣는다 — 맞음 · 패리 · 가드 · 붕괴의 부작용. 회피(Dodged)와 빗나감은
    /// 아무것도 안 한다(<c>default</c> 갈래). <see cref="Land"/> 와 <see cref="Step"/> 의 무적 스냅샷
    /// 양쪽에서 같은 부작용을 내야 하므로 <see cref="BuildEvent"/>(관측 짓기)와 갈라 둔다.
    /// </summary>
    private void ApplyVerdict(HitBox box, HitVerdict verdict)
    {
        switch (verdict)
        {
            case HitVerdict.Hit:
                _fighter.TakeDamage(box.Damage);
                break;

            case HitVerdict.Parried:
                // **어느 타든** 받아치면 보스가 탈진한다 (#72 · 설계 §4.3) — 전에는 마무리를 받아쳤을 때만 굳었다(이슈 #53).
                // 탈진은 여기서 안 건다: 같은 틱에 파이터의 칼이 먼저 돌아야 하고(설계 §3.5 5), 탈진 루틴은 BattleSim 하나다.
                _fighter.ParryPrecise();
                _parried = true;
                break;

            case HitVerdict.Guarded:
                _fighter.GuardChip(box.Damage);
                break;

            case HitVerdict.GuardBroken:
                _fighter.GuardBreak(box.Damage);
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// 이 판정의 관측을 짓는다 — <b>부른 그 틱</b>의 크레딧 · 방향 · 공중 · 거리 · 욕심을 그대로 담는다.
    /// 아직 스트림에 남기지는 않는다(<see cref="Commit"/> 이 한다) — Dodged 는 무적이 먹은 틱에 지어
    /// 두었다가 창이 닫힐 때 그대로 내보내야 하기 때문이다(<see cref="Step"/>).
    /// </summary>
    private DodgeEvent BuildEvent(LiveSwing swing, HitBox box, HitVerdict verdict)
    {
        double now = _tick * BattleSim.Dt;
        (DodgeVerb verb, double startedAt) = _credit.Credit(verdict, box, _boss);
        double error = double.IsNaN(startedAt) ? 0 : startedAt - now;
        int direction = verb == DodgeVerb.Dash ? _credit.DashDirection : 0;

        return new DodgeEvent(
            PatternId: swing.PatternId,
            Verb: verb,
            Verdict: verdict,
            TimingError: error,
            Direction: direction,
            Airborne: !_fighter.Grounded,
            Distance: Math.Abs(_fighter.X - _boss.X),
            // 칼질 중이면 욕심이다 — 1타든 2타든 (설계 §7.2). 2타는 1초를 서 있는 칼이라 정확히 이 축의 이야기다.
            GreedWindow: _fighter.Action == FighterAction.Attack,

            // 태그를 아는 것은 여기뿐이다. 의존도 축은 "고를 수 있었는데 그걸 골랐나" 라서
            // 이 셋이 없으면 만들어지지 않는다.
            DashAvailable: swing.Tags.DashWindow > 0,
            JumpAvailable: swing.Tags.Jumpable,
            ParryAvailable: swing.Tags.Parryable,

            // 뒤의 둘만 **태그가 아니라 판정**에서 온다 (이슈 #53). guard_break 도 마무리도
            // 판정 단위라 같은 패턴 안에서 대마다 값이 다르다 — 태그(has_guard_break)를 읽으면
            // 1·2타까지 "못 막는 판정" 으로 실려 계측이 거짓말을 한다.
            GuardAvailable: !box.GuardBreak,
            Finisher: box.Finisher);
    }

    /// <summary>관측을 확정한다 — 스트림에 남기고 로그 한 줄을 찍는다. <see cref="_events"/> 에 붙는 곳은 여기뿐이다.</summary>
    private void Commit(DodgeEvent evt)
    {
        _events.Add(evt);

        // 지연 오버로드다. 이 줄은 **판정 하나마다** 나오고, 데이터 공장은 한 판에 10~150 판정을
        // 수백만 판 돌린다 — 즉시 오버로드면 LOG_LEVEL=off 여도 포맷 비용을 전부 낸다.
        // qi 를 같이 찍는다. 정확·부정확이 둘 다 기를 주므로 이 줄만 보고 "받아냈나" 를 셀 수 있고,
        // 내상은 hp 에 이미 반영돼 있어 두 줄을 견주면 얼마를 흘렸는지가 나온다.
        // dist 를 뺐던 때는 이 줄만으로 verb 를 검산할 수 없었다 — "거리로 빗나갔다" 가 맞는 말인지
        // 보려면 그 순간의 거리가 있어야 하고, 잘못 붙은 verb 를 잡아낸 방법이 정확히 그 검산이다.
        //
        // air · dist 는 **관측 자신의 값**(evt)을 찍는다 (이슈 #59 · 최종 리뷰). 미룬 Dodged 는 무적이
        // 먹은 틱에 지어 두고 창이 닫히는 틱에 여기로 오므로, 그때의 라이브 값을 읽으면 한 줄에 두 틱이 섞인다 —
        // 땅에서 사거리 안에서 피한 관측이 "공중 · 사거리 밖" 으로 찍혔다. hp · qi · stam 은 관측에 없는 값이라
        // 지금 값이다: 판정의 결과가 몸에 실린 뒤의 잔량이다.
        Log.Info("dodge", () => $"pattern={evt.PatternId} verb={evt.Verb} verdict={evt.Verdict}"
            + $" err={evt.TimingError:0.000} dir={evt.Direction} air={evt.Airborne}"
            + $" dist={evt.Distance:0} hp={_fighter.Health} qi={_fighter.Qi}"
            // stam 을 같이 찍는다 (이슈 #47). 가드의 값은 체력이 아니라 스태미나로 나가므로,
            // 이 칸이 없으면 로그만 보고 "왜 깨졌나" 를 못 읽는다 — 붕괴는 남은 값이 모자란 것이다.
            + $" stam={_fighter.Stamina:0}");
    }

    /// <summary>판정 하나가 끝났다 — 결과를 몸에 싣고 관측을 남긴다.</summary>
    private void Land(LiveSwing swing, HitVerdict verdict)
    {
        ApplyVerdict(swing.Box, verdict);
        Commit(BuildEvent(swing, swing.Box, verdict));
    }

    /// <summary>
    /// 살아 있는 판정 하나 (이슈 #59). 러너가 판정을 내는 순간의 태그와 패턴 id 를 <b>들고 다닌다</b> —
    /// 창이 러너보다 오래 살 수 있고, 러너가 끝나면 <c>BattleSim</c> 의 패턴이 지워진다.
    /// </summary>
    private sealed class LiveSwing
    {
        public LiveSwing(HitBox box, PatternTags tags, string patternId, int ticks)
        {
            Box = box;
            Tags = tags;
            PatternId = patternId;
            TicksLeft = ticks;
        }

        public HitBox Box { get; }

        public PatternTags Tags { get; }

        public string PatternId { get; }

        /// <summary>남은 틱. 대 볼 때마다 하나씩 준다.</summary>
        public int TicksLeft { get; set; }

        /// <summary>
        /// 무적이 <b>처음</b> 먹은 틱에 지어 둔 관측 (이슈 #59 · 리뷰 라운드 1). 창이 안 닿고 닫히면
        /// 이것이 그대로 나간다 — <b>그 틱의</b> 크레딧(대시 시작 시각 · 방향) · 공중 · 거리로 지었으므로,
        /// 창이 그 뒤로 몇 틱을 더 살아 무적이 풀려도(<see cref="DodgeCredit"/> 의 라이브 대시 시각이 NaN 이 돼도)
        /// 이 기록은 안 바뀐다. null 이면 아직 한 번도 안 먹었다.
        /// </summary>
        public DodgeEvent? DodgeSnapshot { get; set; }

        /// <summary>마지막으로 빗나간 이유 — 창이 닫힐 때 무적이 한 번도 안 먹었으면 이것이 답이다.</summary>
        public HitVerdict LastMiss { get; set; } = HitVerdict.MissedTooFar;
    }
}
