using System;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>
/// 헤드리스로 전투를 끝까지 몰고 가는 <b>최소</b> 봇. 잘하라고 만든 것이 아니라
/// "전투가 끝까지 도는가"를 창 없이 보려고 만든 것이다.
///
/// <para>
/// ⚠ <b>이것은 학습 데이터용 봇이 아니다.</b> 파라미터로 성향을 바꾸는 봇 함대는 스펙 5 의 몫이다.
/// 다만 회피 수단을 <b>전부</b> 쓰도록 만든다 — 한 수단만 쓰는 봇은 나머지 축을 영원히 0 으로 만들고,
/// 그러면 계측이 제대로 도는지조차 확인할 수 없다. 가드(이슈 #47)도 같은 이유로 여기 있다:
/// 봇이 못 내는 기술은 봇 함대가 만드는 데이터에 영영 안 들어간다.
/// </para>
///
/// <para>
/// 난수는 <see cref="Det"/> 로만 뽑는다. 벽시계도 <c>Random</c> 도 없으므로 같은 시드는 같은 판이다.
/// </para>
/// </summary>
public sealed class BotPolicy
{
    /// <summary>
    /// 대시·패리를 걸 창(초). 무적창(0.14) · 패리창(0.133)보다 좁게 잡아 판정이 서는 순간까지
    /// 창이 열려 있게 한다 — 일찍 걸면 창을 놓쳐 <b>가드</b>가 된다 (이슈 #53).
    /// 연타 징벌(이슈 #27)도 여기에 걸려 있다: 이 창 안에서만 누르므로 누름은 거의 항상
    /// 받아치고, 받아친 누름은 사슬을 푼다.
    /// </summary>
    private const double _lateReact = 0.10;

    /// <summary>
    /// 몇 패턴에 한 번 가드로 받을까. <b>수치가 아니라 봇의 성향이라</b> 데이터가 아니라 여기 있다
    /// (<see cref="_lateReact"/> 와 같은 자리다) — fighters.json 의 어느 캐릭터 값도 아니고,
    /// 학습 데이터용 봇 함대는 이 값을 파라미터로 받는다.
    /// 셋에 하나면 한 판(패턴 20~40회)에 가드가 여러 번 들어가 계측이 실제로 도는지 보인다.
    /// </summary>
    private const int _guardOdds = 3;

    private readonly ulong _seed;
    private int _decisions;

    /// <summary>지난 틱에 돌던 패턴 id (null = 쉬는 중). 바뀌는 순간이 "새 패턴" 이다.</summary>
    private string? _lastPattern;

    /// <summary>지금까지 본 패턴 수. 가드 주사위의 좌표다.</summary>
    private int _patterns;

    /// <summary>이번 패턴을 가드로 받기로 했나.</summary>
    private bool _guardThis;

    /// <summary>지금까지 시작한 공격의 수. 차지 단계를 고르는 좌표의 키다.</summary>
    private int _swings;

    /// <summary>
    /// 지금 모으는 차지의 목표 시간(초). 누를 때 정해 놓고, 닿으면 놓는다.
    /// 매 틱 다시 뽑지 않는 이유는 그러면 "얼마나 모을 작정이었나" 가 기록에 안 남기 때문이다 —
    /// 사람은 누를 때 정하고 그 판단이 곧 데이터다.
    /// </summary>
    private double _chargeGoal;

    public BotPolicy(ulong seed) => _seed = seed;

    /// <summary>이번 틱에 무엇을 할지. <see cref="BattleSim"/> 의 상태만 보고 정한다.</summary>
    public InputFrame Next(BattleSim sim)
    {
        ArgumentNullException.ThrowIfNull(sim);

        double gap = Math.Abs(sim.Fighter.X - sim.Boss.X) - sim.Boss.HalfWidth;
        sbyte move = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);

        // 가드는 **패턴이 시작할 때** 정한다. 자세는 이제 누르는 그 틱에 서지만(이슈 #53)
        // 아래의 반응 창(_lateReact 0.10초) 안에서 누르면 그건 가드가 아니라 **패리**다 —
        // 창 안에 판정이 서기 때문이다. 가드를 실제로 내려면 일찍 눌러 창을 흘려보내야 하고,
        // 그 판단은 패턴마다 한 번이어야 한다: 틱마다 마음이 바뀌면 버티는 일이 없다.
        DecideGuard(sim);

        // 모으는 중이면 할 일은 하나다 — 놓을 때인가 (이슈 #40).
        // **패턴 갈래보다 먼저 본다.** 아래에 맡기면 패턴이 서는 순간 default(누름 없음)가 나가
        // 봇은 자기가 왜 놓았는지도 모른 채 모은 것을 잃는다. 여기서 두 조건으로 명시한다:
        // ① 목표만큼 모았다, ② 칼이 닿기 전에 판정이 선다(모은 채로 맞으면 전부 날아간다).
        if (sim.Fighter.Charging)
        {
            bool hold = sim.Fighter.ChargeSeconds < _chargeGoal && Safe(sim);
            return new InputFrame(0, false, false, false, false, AttackHeld: hold);
        }

        // 패턴이 돌고 있으면 셋 중 하나로 반응한다. 무엇을 고를지는 좌표 조회로 정한다 —
        // 호출 순서에 값이 끌려다니지 않아 같은 시드가 같은 판을 만든다.
        //
        // ⚠ CurrentPattern 만 보고 매 틱 다시 고르지 않는다 — 그러면 윈드업 내내 아무 때나
        // 걸어서 판정이 서기도 전에 무적창·패리창이 닫혀 버린다. NextActiveIn(남은 시간)을 봐서
        // 대시·패리는 판정 직전에 걸고, 점프는 미리 떠야 높이가 나니 곧장 쓴다.
        //
        // 행동 중(Dash·Parry·Attack)에는 새로 고르지 않는다 — 그건 Fighter.Begin 이 어차피
        // 무시하므로 막을 필요는 없지만, 접지 여부는 **걸지 않는다**: 공중에서도 대시·패리를
        // 다시 걸 수 있어야 점프가 늦게 뜬 판정을 막판에 대시로 덮을 수 있다.
        // **"패턴이 돈다" 는 것만으로 회피할 이유가 되지 않는다.** 올 판정이 있어야 피할 것이 있다.
        // 둘을 빼낸다.
        // ① 굳은 보스 — 경직 동안에는 타임라인이 안 밀리므로 아무것도 안 온다. 봇은 그 0.5초를
        //    회피로 버리고 있었다. 받아낸 뒤가 내 차례라는 것이 정확 패리의 상이고(나인 솔즈),
        //    그 상을 쓰는 곳은 회피가 아니라 공격이다.
        // ② 후딜 — 남은 판정이 없으면(NextActiveIn == null) 패턴은 돌지만 빈 시간이다.
        //    사람은 마지막 판정이 지나간 그 순간부터 모으기 시작하는데, 봇이 패턴이 끝나기를
        //    기다리면 최대 차지에 필요한 시간의 절반을 문 앞에서 버린다 — 그러면 학습 데이터에
        //    최대 차지가 영영 안 들어간다.
        if (sim.Boss.CurrentPattern is not null && !sim.Boss.Staggered && sim.NextActiveIn is not null)
        {
            // **행동 갈래보다 먼저 본다.** 아래에 맡기면 패리 동작이 도는 동안 default(누름 없음)가
            // 나가 레벨이 꺼지고, 가드는 서기도 전에 풀린다 — 차지가 같은 자리에서 같은 이유로 깨졌다.
            if (_guardThis)
            {
                // 누름은 서 있을 때 한 번, 그 뒤로는 **유지**다. 엣지가 시작하고 레벨이 붙든다.
                bool press = sim.Fighter.Action == FighterAction.Idle;
                return new InputFrame(0, false, false, Parry: press, false, ParryHeld: true);
            }

            // 패리로 선 자세는 **판정이 지나갈 때까지만** 붙든다 (이슈 #53). 놓으면 그 자리에서
            // 무방비이고, 다시 누르면 연타 사슬이 패리 창을 깎는다 — 전에는 0.30초짜리 패리
            // 행동이 그 사이를 막아 줬는데 그 행동이 없어졌다.
            // **계속** 붙들지는 않는다: 그러면 남은 연타를 전부 가드가 받아 대시·점프 표본이 마른다.
            if (sim.Fighter.Guarding)
            {
                bool keep = sim.NextActiveIn is double left && left <= _lateReact;
                return new InputFrame(0, false, false, false, false, ParryHeld: keep);
            }

            if (sim.Fighter.Action != FighterAction.Idle)
            {
                return default;
            }

            _decisions++;
            int pick = Det.RollInt(_seed, Det.Domain.BotChoice, 3, k1: _decisions);
            if (pick != 1 && (sim.NextActiveIn is not double remaining || remaining > _lateReact))
            {
                // 아직 이르다 — 대시·패리를 지금 걸면 판정 전에 창이 닫힌다. 다음 틱에 다시 본다.
                return default;
            }

            return pick switch
            {
                0 => new InputFrame(0, false, Dash: true, false, false),
                1 => new InputFrame(0, Jump: true, false, false, false),
                // 누르는 그 틱부터 붙든다 — 자세가 곧 방어라 엣지만 보내면 다음 틱에 풀린다.
                _ => new InputFrame(0, false, false, Parry: true, false, ParryHeld: true),
            };
        }

        // 쉬는 동안에는 붙어서 때린다.
        if (gap > sim.FighterReach)
        {
            return new InputFrame(move, false, false, false, false);
        }

        // 얼마나 모을지는 **누를 때** 좌표 조회로 정한다. 단계 수와 그 시간은 데이터가 알고
        // (fighters.json 의 charge_tiers) 봇은 묻기만 한다 — 수치를 여기 베끼면 캐릭터를
        // 고치는 순간 봇만 옛 값으로 계속 돈다.
        _swings++;
        int tier = Det.RollInt(_seed, Det.Domain.BotCharge, sim.Fighter.ChargeTierCount, k1: _swings);
        _chargeGoal = sim.Fighter.ChargeTierSeconds(tier);

        // 0단계는 누름 유지가 없다 — 옛 봇과 한 틱도 안 다른 그냥 한 대다.
        return new InputFrame(0, false, false, false, Attack: true, AttackHeld: _chargeGoal > 0);
    }

    /// <summary>
    /// 새 패턴이 시작됐으면 이번 것을 가드로 받을지 <b>한 번</b> 정한다 (이슈 #47).
    /// 패턴이 끝나면 가드도 놓는다 — 쉬는 시간에 버티고 있으면 스태미나가 안 차고,
    /// 그건 봇이 아무것도 못 하게 되는 길이다.
    /// </summary>
    private void DecideGuard(BattleSim sim)
    {
        string? now = sim.Boss.CurrentPattern;
        if (now == _lastPattern)
        {
            return;
        }

        _lastPattern = now;
        if (now is null)
        {
            _guardThis = false;
            return;
        }

        _patterns++;
        _guardThis = Det.RollInt(_seed, Det.Domain.BotGuard, _guardOdds, k1: _patterns) == 0;
    }

    /// <summary>
    /// 지금 모으고 있어도 <b>칼이 닿을 때까지 안 맞는가.</b>
    /// 굳은 보스는 타임라인이 안 밀리므로 안전하고, 남은 판정이 없어도(<c>null</c>) 안전하다.
    /// 아니면 판정까지 남은 시간이 "선딜 + 판정"(<see cref="Fighter.AttackLead"/>)에 반응 여유를
    /// 더한 것보다 길어야 한다 — 그보다 짧으면 지금 놓아도 칼이 판정에 먹힌다.
    /// </summary>
    private static bool Safe(BattleSim sim) =>
        sim.Boss.Staggered
        || sim.NextActiveIn is not double until
        || until > sim.Fighter.AttackLead + _lateReact;
}
