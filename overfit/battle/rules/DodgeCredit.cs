using System;

namespace Overfit.Battle.Rules;

/// <summary>
/// 회피 수단마다 <b>언제 시작했나</b>를 들고 있다가, 판정 하나가 끝나면 그 결과를 <b>무엇의 공으로</b> 돌릴지
/// 정한다 — 관측(<see cref="DodgeEvent"/>)의 verb · 타이밍 오차 · 방향이 여기서 나온다.
///
/// <para>
/// <see cref="BattleSim"/> 에서 떼어 냈다 (이슈 #59 · 1번 PR 최종 리뷰). 한 판을 미는 일과 공을 돌리는 일은
/// 따로 바뀐다 — 조작이 바뀌면(2연격 · 가드 · 패리) 여기가, 보스가 바뀌면 <see cref="BattleSim"/> 이 바뀐다.
/// 한 파일에 둘 때는 주석을 빼고 413줄이라 "이 파일이 무엇을 하는가" 를 한 문장으로 못 말했다(CLAUDE.md §7).
/// </para>
///
/// <para>
/// 회피 수단마다 <b>따로</b> 시작 시각을 들고 있는다(초, NaN = 지금 그 수단이 없다).
/// 슬롯이 하나였을 때는 "가장 최근에 시작한 행동" 이 판정을 다 가져갔다 —
/// 점프로 넘긴 지면쓸기가 같이 눌러둔 패리의 공이 되어, 데모 10건 중 4건이
/// 엉뚱한 verb 로 기록됐고 parry_rate 까지 그 실패로 오염됐다.
/// </para>
/// </summary>
public sealed class DodgeCredit
{
    private double _dashStartedAt = double.NaN;

    private double _parryStartedAt = double.NaN;

    private double _jumpStartedAt = double.NaN;

    /// <summary>
    /// 가드가 선 시각(초, NaN = 지금 가드가 아니다) — 이슈 #47 · 설계 §5.2.
    ///
    /// <para>
    /// 패리 칸과 <b>따로</b> 둔다 — 둘은 다른 키 · 다른 행동이다(설계 §5.2 · §5.3). 가드 칸은 ↓ 를 누르고 서 있는
    /// 내내 살아 있고(연속타를 여러 대 받아내므로 한 대가 기록을 소비하면 안 된다), 놓으면 지워진다.
    /// </para>
    /// </summary>
    private double _guardStartedAt = double.NaN;

    private int _dashDirection;

    /// <summary>
    /// 대시를 <b>시작하기 직전</b>의 몸통과 그때의 보스 자리 (null = 대시 중이 아니다).
    /// "그 자리에 서 있었으면 이 판정에 맞았나" 를 판정마다 물어보는 반사실(counterfactual)이다 —
    /// 맞았을 것이면 대시가 빼낸 것이고, 거기서도 안 맞았으면 간격이다 (이슈 #46).
    ///
    /// <para>
    /// 대시 중이라는 것만으로는 부족하다. 사거리 100 짜리 판정 앞에서 960px 떨어져 대시하면
    /// 대시는 돌지만 그 거리는 대시가 만든 것이 아니다 — 그것까지 대시의 공으로 돌리면
    /// <c>dash_timing_bias</c> 가 "판정을 피한 대시" 가 아닌 것들로 채워진다.
    /// 옛 반사실은 거리 하나(보스 중심에서)를 띠와 견줬고, 이제는 몸통을 모양에 댄다 (이슈 #59).
    /// </para>
    /// </summary>
    private HitRect? _dashStartBody;

    private double _dashStartBossX;

    /// <summary>지금 도는 대시의 방향 — +1 보스 쪽(안) · -1 반대(밖) · 0 대시가 아니다.</summary>
    public int DashDirection => _dashDirection;

    /// <summary>
    /// 이번 틱에 시작된 회피 행동의 시각을 그 수단의 칸에 적고, 끝난 수단의 칸은 지운다.
    /// <b>판정이 설 때 이것과의 차이가 타이밍 오차가 된다.</b>
    ///
    /// <para>
    /// 수단마다 칸이 따로다. 하나로 합치면 나중에 시작한 행동이 앞선 행동을 덮어써서,
    /// 정작 판정을 피하게 한 수단의 시각이 사라진다.
    /// </para>
    /// </summary>
    /// <param name="now">이번 틱의 시각(초) — <c>BattleSim.Ticks × Dt</c>.</param>
    /// <param name="input">이번 틱의 입력. 점프가 눌렸는지를 본다.</param>
    /// <param name="wasGrounded">이번 틱이 시작될 때(<see cref="Fighter.Tick"/> 이전) 접지 상태.
    /// 점프 엣지 검출에 쓴다 — <see cref="Fighter.Grounded"/> 만 보면 "떨어진 순간"과
    /// "이미 공중인데 또 눌렀다"를 구별할 수 없다.</param>
    /// <param name="wasX">이번 틱이 시작될 때(<see cref="Fighter.Tick"/> 이전) 파이터의 자리.
    /// 대시가 시작된 틱에는 이미 한 틱을 이동한 뒤라, 대시 <b>전</b>의 거리는 이것으로만 잡힌다.</param>
    /// <param name="wasY">이번 틱이 시작될 때의 발바닥 높이. 공중 대시의 반사실이 이것을 쓴다.</param>
    /// <param name="fighter">이번 틱을 이미 민 파이터.</param>
    /// <param name="boss">아직 이번 틱을 안 민 보스 — 대시를 시작한 틱의 보스 자리가 이것이다.</param>
    public void Remember(
        double now, InputFrame input, bool wasGrounded, double wasX, double wasY, Fighter fighter, Boss boss)
    {
        ArgumentNullException.ThrowIfNull(fighter);
        ArgumentNullException.ThrowIfNull(boss);

        // 시작한 행동은 **그 행동이 끝났을 때만** 지운다. Land 에서 지우면 대시 한 번의 무적이
        // 막은 연속타 중 첫 대가 기록을 소비해 버려 나머지가 "회피 수단 없음" 으로 기록된다 —
        // 근거가 없는 게 아니라 **잘못 붙는다.**
        if (fighter.Action == FighterAction.Dash && fighter.ActionElapsed <= BattleSim.Dt)
        {
            _dashStartedAt = now;
            // 보스 쪽으로 갔으면 안(+1), 반대면 밖(-1)
            _dashDirection = Math.Sign(fighter.Facing * (boss.X - fighter.X)) >= 0 ? 1 : -1;
            // 보스는 아직 이번 틱을 안 밀었으므로(AdvanceBoss 는 뒤에 온다) 둘 다 틱 시작의 자리다.
            _dashStartBody = new HitRect(
                wasX - fighter.HalfWidth, wasX + fighter.HalfWidth, wasY, wasY + fighter.BodyHeight);
            _dashStartBossX = boss.X;
        }
        else if (fighter.Action != FighterAction.Dash)
        {
            _dashStartedAt = double.NaN;
            _dashDirection = 0;
            _dashStartBody = null;
        }

        // 패리 칸은 **패리 행동이 도는 동안** 산다 (이슈 #59 · 설계 §5.3). 패리는 이제 0.333초 커밋이라 그 동안이
        // "이 누름이 겨냥한 판정" 이 성립하는 구간이다 — 대시의 공이 대시 행동이 도는 동안인 것과 같은 경계다.
        // 옛 경계는 누름의 기억 창(0.5초)이었고 스펙이 그 창을 지웠다. 창을 놓치고 커밋 안에서 맞은 판정은
        // 그대로 이 누름의 시도로 남는다 — "늦어서 못 받았다" 가 "아무것도 안 했다" 와 같은 점이 되지 않게.
        if (fighter.Action == FighterAction.Parry && fighter.ActionElapsed <= BattleSim.Dt)
        {
            _parryStartedAt = now;
        }
        else if (fighter.Action != FighterAction.Parry)
        {
            _parryStartedAt = double.NaN;
        }

        // 가드 칸은 **누름이 아니라 서 있는 동안**을 잡는다. 서 있는 내내 살아 있고
        // (연속타를 여러 대 받아내므로 한 대가 기록을 소비하면 안 된다), 놓으면 지워진다.
        if (fighter.Guarding)
        {
            if (double.IsNaN(_guardStartedAt))
            {
                _guardStartedAt = now;
            }
        }
        else
        {
            _guardStartedAt = double.NaN;
        }

        if (input.Jump && wasGrounded && !fighter.Grounded)
        {
            _jumpStartedAt = now;
        }
        else if (fighter.Grounded)
        {
            _jumpStartedAt = double.NaN;
        }
    }

    /// <summary>
    /// 이 판정을 <b>무엇이</b> 그렇게 만들었나. 결과가 이미 답을 들고 있다 —
    /// 무적이 먹었으면 대시, 패리가 받았으면 패리, 높이가 어긋났으면 점프다.
    /// 그 순간 돌고 있던 행동으로 추측하지 않는다.
    ///
    /// <para>
    /// <b>Dodged 는 무적이 먹은 그 틱에만 크레딧을 문다</b> (이슈 #59 · 리뷰 라운드 1). 창이 몇 틱 더
    /// 사는 동안 미뤘다 나중에 물으면 그새 대시가 끝나 <c>_dashStartedAt</c> 이 NaN 으로 돌아가 있을 수
    /// 있고, 그러면 "0초 전에 프레임 퍼펙트로 피했다" 는 거짓 크레딧이 나간다 — 그래서 <c>BossSwings.Step</c> 은
    /// Dodged 를 처음 본 틱에 곧장 <c>BossSwings.BuildEvent</c> 를 불러 관측을 지어 두고, 창이 닫힐 때
    /// 그 스냅샷을 그대로 내보낸다(다시 묻지 않는다).
    /// </para>
    /// </summary>
    public (DodgeVerb Verb, double StartedAt) Credit(HitVerdict verdict, HitBox box, Boss boss)
    {
        ArgumentNullException.ThrowIfNull(boss);

        return verdict switch
        {
            HitVerdict.Dodged => (DodgeVerb.Dash, _dashStartedAt),

            // 받아친 것은 패리다. 시각은 **누름**이다 — 창 안에 들어왔는가가 이 판정의 전부라,
            // 재야 하는 것은 "언제 눌렀나" 이지 "언제부터 서 있었나" 가 아니다.
            HitVerdict.Parried => (DodgeVerb.Parry, _parryStartedAt),

            // 막아냈든 깨졌든 **고른 것은 가드**다 (이슈 #47) — 둘의 차이는 verb 가 아니라
            // Verdict 가 나른다. 시각은 가드가 **선** 순간이다: 그래야 "얼마나 오래 버티고
            // 있었나" 가 오차로 실린다.
            HitVerdict.Guarded or HitVerdict.GuardBroken => (DodgeVerb.Guard, _guardStartedAt),

            // 높이로 빗나갔다. 점프 기록이 있으면 점프가 넘긴 것이고, 없으면 대공 판정 아래에
            // 그냥 서 있었던 것이다 — 후자를 점프로 세면 jump_reliance 가 **정반대 행동**으로 부푼다.
            HitVerdict.MissedByHeight => double.IsNaN(_jumpStartedAt)
                ? (DodgeVerb.None, double.NaN)
                : (DodgeVerb.Jump, _jumpStartedAt),

            // 거리로 빗나갔다 — 안이든 밖이든. 서 있던 자리가 피하게 했으면 간격이지만,
            // **그 자리를 대시가 만들었으면 대시다** (이슈 #46).
            HitVerdict.MissedTooFar or HitVerdict.MissedByGap => CreditDistance(box, boss),

            // 맞았다 — 무엇을 시도했다 실패했는지를 남긴다.
            _ => MostRecentAction(),
        };
    }

    /// <summary>
    /// 거리로 빗나간 판정의 공을 <b>대시</b>와 <b>간격</b> 중 어디로 돌릴 것인가 (이슈 #46).
    ///
    /// <para>
    /// 고치기 전에는 무조건 간격이었다. 판정 순서가 거리 → 높이 → 대시무적이라 대시로 사거리를
    /// 벗어나면 무적이 보이기도 전에 거리에서 빠지는데, 그것을 전부 <c>Spacing</c> 으로 적고 있었다:
    /// 무적 8틱 · 대시 36.67px/틱 · 서는 자리 115 에서 밖으로 나가면 250 을 4틱째 넘으므로
    /// <b>무적 8틱 중 3틱만 <c>Dodged</c></b> 이었다. <b>대시 의존자가 간격 의존자로 기록된다</b> —
    /// 그 둘은 봉인할 것이 정반대라 2단계가 정확히 반대 변종을 뽑는다.
    /// </para>
    ///
    /// <para>
    /// 조건은 "대시 중" 이 아니라 <b>"대시 시작 자리에서는 닿았는가"</b> 다. 대시가 돌기만 하면
    /// 공을 주면, 애초에 사거리 밖에 서 있다 대시한 것까지 대시의 공이 되어
    /// <c>dash_timing_bias</c> 가 판정과 무관한 대시들로 채워진다.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>경계는 대시 행동이 끝나는 자리다.</b> 무적(0.14초 = 8틱)이 대시(0.18초 = 11틱)보다
    /// 짧으므로 무적 창은 통째로 대시의 공이 되지만, 대시가 끝난 뒤에도 사거리 밖에 남아 있는 것은
    /// <b>그 자리에 서 있기로 한 것</b>이라 간격이다. 유예 창(대시 종료 후 N초까지는 대시의 공)을
    /// 두는 쪽도 생각했고, 되돌리지 않기 위해 왜 안 두는지를 적어 둔다.
    /// </para>
    ///
    /// <para>
    /// <b>공에는 시각이 따라붙기 때문이다.</b> 이 함수가 돌려주는 시작 시각이 곧
    /// <c>TimingError</c> 이고 그것이 <c>dash_timing_bias</c> · <c>dash_timing_var</c> 의 표본이다.
    /// 연속타의 2타는 1타에서 0.35초 뒤에 서는데, 1타를 겨냥해 뛴 대시를 2타의 공으로도 돌리면
    /// <b>-0.35초짜리 표본</b>이 하나 생긴다 — "이 사람은 판정 0.35초 전에 뛴다" 는 그가 한 적 없는 말이고,
    /// 표본이 늘수록 축은 "늘 일찍 누른다" 쪽으로 끌려간다. 대시가 <b>어느 판정을 겨냥했나</b>가
    /// 성립하는 구간이 딱 행동이 도는 동안이라, 거기를 경계로 삼는다.
    /// 덤으로 데이터에 없는 수치("얼마나 오래 봐주나")를 새로 만들지 않아도 된다.
    /// </para>
    /// </summary>
    private (DodgeVerb Verb, double StartedAt) CreditDistance(HitBox box, Boss boss) =>
        !double.IsNaN(_dashStartedAt)
        && _dashStartBody is { } before
        && ShapeHit.Test(box.Shape, new Placement(_dashStartBossX, boss.Y, boss.Facing), before) == ShapeContact.Overlap
            ? (DodgeVerb.Dash, _dashStartedAt)
            : (DodgeVerb.Spacing, double.NaN);

    /// <summary>
    /// 지금 돌고 있는 회피 행동 중 <b>가장 늦게</b> 시작한 것. 맞은 판정에만 쓴다 —
    /// 겹쳐 있으면 그 판정을 겨냥한 쪽이 더 나중이다.
    /// 동시 시작은 대시 → 패리 → 점프 순으로 **고정**한다. 순서를 안 박아두면 같은 시드가
    /// 다른 라벨을 내 학습 데이터가 재현되지 않는다.
    /// </summary>
    private (DodgeVerb Verb, double StartedAt) MostRecentAction()
    {
        DodgeVerb verb = DodgeVerb.None;
        double at = double.NaN;

        if (!double.IsNaN(_dashStartedAt))
        {
            verb = DodgeVerb.Dash;
            at = _dashStartedAt;
        }

        if (!double.IsNaN(_parryStartedAt) && (double.IsNaN(at) || _parryStartedAt > at))
        {
            verb = DodgeVerb.Parry;
            at = _parryStartedAt;
        }

        if (!double.IsNaN(_jumpStartedAt) && (double.IsNaN(at) || _jumpStartedAt > at))
        {
            verb = DodgeVerb.Jump;
            at = _jumpStartedAt;
        }

        return (verb, at);
    }
}
