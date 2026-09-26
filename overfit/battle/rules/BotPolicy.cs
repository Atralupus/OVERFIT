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
///
/// <para>
/// <b>스태미나가 값보다 많을 때만 누른다 — 스스로 탈진하지 않는다</b> (#71 · 설계 §5.4 · <see cref="Fighter.Affords"/>). 규칙은 남아 있으면
/// 모자라도 마지막 한 번을 허락하고(§5.5) 값이 딱 맞는 한 번은 0 에 닿아 탈진하지만, 봇은 둘 다 안 쓴다. 뽑기(좌표)는 그대로 굴린다:
/// 옛 규칙에서는 모자란 누름을 규칙이 버렸고, 이제 그 자리를 봇이 먼저 버린다. 재 본 16 시드(실제 캐릭터 · 1단계)에서 행동의 값으로
/// 든 탈진은 없고, 31337 만 516틱부터 누름이 갈려 같은 끝(1414틱 · HP 62)에 닿는다. 데모의 시드 51 은 한 틱도 안 다르다.
/// </para>
/// </summary>
public sealed class BotPolicy
{
    /// <summary>
    /// 대시·패리를 걸 창(초). 무적창(0.14) · 패리창(0.133)보다 좁게 잡아 판정이 서는 순간까지
    /// 창이 열려 있게 한다 — 일찍 걸면 창을 놓쳐 커밋 안에서 맞는다 (설계 §5.3).
    /// </summary>
    private const double _lateReact = 0.10;

    /// <summary>
    /// 몇 패턴에 한 번 가드로 받을까. <b>수치가 아니라 봇의 성향이라</b> 데이터가 아니라 여기 있다
    /// (<see cref="_lateReact"/> 와 같은 자리다) — fighters.json 의 어느 캐릭터 값도 아니고,
    /// 학습 데이터용 봇 함대는 이 값을 파라미터로 받는다.
    /// 셋에 하나면 한 판(패턴 20~40회)에 가드가 여러 번 들어가 계측이 실제로 도는지 보인다.
    /// </summary>
    private const int _guardOdds = 3;

    /// <summary>
    /// 몇 칼질에 한 번 2타를 이을까. <see cref="_guardOdds"/> 와 같은 자리 · 같은 이유다 — 봇의 성향이지 캐릭터의 수치가
    /// 아니고, 봇 함대는 이것을 파라미터로 받는다. 둘에 하나(반반)인 이유: 한쪽만 나오면 그 칸(2타를 이은 칼질 · 1타로
    /// 끝낸 칼질)이 데이터에 없는 것과 같다.
    /// </summary>
    private const int _chainOdds = 2;

    private readonly ulong _seed;
    private int _decisions;

    /// <summary>지난 틱에 돌던 패턴 id (null = 쉬는 중). 바뀌는 순간이 "새 패턴" 이다.</summary>
    private string? _lastPattern;

    /// <summary>지금까지 본 패턴 수. 가드 주사위의 좌표다.</summary>
    private int _patterns;

    /// <summary>이번 패턴을 가드로 받기로 했나.</summary>
    private bool _guardThis;

    /// <summary>지금까지 시작한 칼질의 수. 2타를 이을지 고르는 좌표의 키다.</summary>
    private int _swings;

    /// <summary>
    /// 이번 칼질에 2타를 이을 작정인가. 1타를 <b>누를 때</b> 정한다 — 사람은 1타를 누를 때 이미 2타를 정해 두고,
    /// 매 틱 다시 뽑으면 "이을 작정이었나" 가 기록에 안 남는다.
    /// </summary>
    private bool _chainThis;

    public BotPolicy(ulong seed) => _seed = seed;

    /// <summary>이번 틱에 무엇을 할지. <see cref="BattleSim"/> 의 상태만 보고 정한다.</summary>
    public InputFrame Next(BattleSim sim)
    {
        ArgumentNullException.ThrowIfNull(sim);

        double gap = Math.Abs(sim.Fighter.X - sim.Boss.X) - sim.Boss.HalfWidth;
        sbyte move = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);

        // 가드는 **패턴이 시작할 때** 정한다 — 틱마다 마음이 바뀌면 버티는 일이 없다.
        DecideGuard(sim);

        // 칼질 중이면 할 일은 하나다 — 이을 작정이면 한 번 더 누른다 (설계 §5.1 · §5.4: 2연격도 엣지 두 번이다).
        // **패턴 갈래보다 먼저 본다**: 칼질은 끝까지 커밋이라 판정이 와도 할 수 있는 것이 없다.
        if (sim.Fighter.Action == FighterAction.Attack)
        {
            bool press = _chainThis && sim.Fighter.ComboStep == 0 && !sim.Fighter.ComboQueued
                && sim.Fighter.Affords(FighterAction.Attack);
            return new InputFrame(0, false, false, false, Attack: press);
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
        // ① 탈진한 보스 (#72 · 설계 §4.3) — 패턴이 끊겨 아무것도 안 온다. 받아낸 뒤가 내 차례라는 것이
        //    패리의 상이고(나인 솔즈), 그 상을 쓰는 곳은 회피가 아니라 공격이다(설계 §5.4 — 지금의 "굳은 보스" 규칙 그대로).
        // ② 후딜 — 남은 판정도 산 창도 없으면(NextActiveIn == null · SwingLive 거짓) 패턴은 돌지만 빈 시간이다.
        //    사람은 마지막 판정이 지나간 그 순간부터 칼을 넣는다 — 봇이 패턴이 끝나기를 기다리면
        //    그 빈 시간을 문 앞에서 버린다.
        //
        // **창이 살아 있는 동안은 "판정이 지금" 이다** (#72 · 설계 §3.6 ④). NextActiveIn 은 판정이 서는 틱에 "다음 판정" 이기를
        // 그쳐 null 이 되는데, 창은 8틱을 산다 — 그것만 보던 봇은 창의 첫 틱에 가드를 풀고 칼을 눌러 남은 틱에 맞았다.
        double? remaining = sim.SwingLive ? 0 : sim.NextActiveIn;
        if (sim.Boss.CurrentPattern is not null && !sim.Boss.Exhausted && remaining is not null)
        {
            // **행동 갈래보다 먼저 본다.** 아래에 맡기면 가드(Idle 이 아니다) 동안 default(누름 없음)가
            // 나가 레벨이 꺼지고, 가드는 서자마자 풀린다 — 차지가 같은 자리에서 같은 이유로 깨졌다.
            //
            // 가드로 받기로 한 패턴이면 ↓ 를 **붙든다** — 가드는 누르고 있는 동안이다 (설계 §5.2). 서 있기만 하면 되므로
            // 판정 직전을 노릴 필요가 없고, 그래서 봇의 반응 창(_lateReact)과 무관하게 패턴 내내 든다.
            if (_guardThis)
            {
                return new InputFrame(0, false, false, false, false, GuardHeld: true);
            }

            if (sim.Fighter.Action != FighterAction.Idle)
            {
                return default;
            }

            _decisions++;
            int pick = Det.RollInt(_seed, Det.Domain.BotChoice, 3, k1: _decisions);
            if (pick != 1 && remaining > _lateReact)
            {
                // 아직 이르다 — 대시·패리를 지금 걸면 판정 전에 창이 닫힌다. 다음 틱에 다시 본다.
                return default;
            }

            return pick switch
            {
                0 => sim.Fighter.Affords(FighterAction.Dash) ? new InputFrame(0, false, Dash: true, false, false) : default,
                1 => new InputFrame(0, Jump: true, false, false, false),
                // 패리는 누르는 것 한 번이다 — 0.333초 커밋 동안 앞 0.133초가 창이라 붙들 것이 없다 (설계 §5.3).
                _ => sim.Fighter.Affords(FighterAction.Parry) ? new InputFrame(0, false, false, Parry: true, false) : default,
            };
        }

        // 쉬는 동안에는 붙어서 때린다.
        if (gap > sim.FighterReach)
        {
            return new InputFrame(move, false, false, false, false);
        }

        // 2타를 이을지는 **1타를 누를 때** 좌표 조회로 정한다 (_chainOdds). 값이 모자라면 좌표만 쓰고 안 누른다(머리 주석).
        _swings++;
        _chainThis = Det.RollInt(_seed, Det.Domain.BotCombo, _chainOdds, k1: _swings) == 0;
        return sim.Fighter.Affords(FighterAction.Attack) ? new InputFrame(0, false, false, false, Attack: true) : default;
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
}
