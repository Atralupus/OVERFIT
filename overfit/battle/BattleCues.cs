using System;
using Overfit.Battle.Rules;
using Overfit.Battle.View;

namespace Overfit.Battle;

/// <summary>
/// 방금 지난 틱에서 <b>무슨 일이 일어났나</b>를 값의 차이로 읽어 뷰에 알린다 — 체력이 줄었나, 회피 관측이 늘었나,
/// 보스가 탈진에 들었나, 새 칼질이 섰나.
///
/// <para>
/// 규칙 층은 뷰를 모르므로 콜백이 없다 — 있으면 그 콜백이 곧 규칙의 일부가 되고, 헤드리스 봇이 그걸 들고 다니게
/// 된다. 그래서 여기서 <b>틱 전후를 견준다</b>. 화면 흔들림과 히트스톱은 씬(<see cref="Battle"/>)의 것이라 넘겨받은
/// 두 동작으로 부른다.
/// </para>
///
/// <para>
/// <see cref="Battle"/> 에서 떼어 냈다 (#71). <c>Battle.cs</c> 는 주석 빼고 387줄이었고 4번 PR 이 히트스톱 동안의 입력 ·
/// 게이지 바 · 파이터 탈진의 그림을 얹는다(CLAUDE.md §7). 한 판을 미는 일(물리 틱 · 히트스톱 · 결과 화면)과
/// 틱의 차이에서 사건을 찾는 일은 따로 바뀐다.
/// </para>
/// </summary>
public sealed class BattleCues
{
    private readonly BattleSim _sim;
    private readonly FighterView _fighterView;
    private readonly BossView _bossView;

    /// <summary>화면을 흔든다 — 배수를 받는다(1.0 이 피격). 씬이 World 노드를 흔든다.</summary>
    private readonly Action<double> _shake;

    /// <summary>히트스톱을 건다 — 씬이 물리 틱을 세우고 그림을 멈춘다.</summary>
    private readonly Action _hitstop;

    private int _lastFighterHealth;
    private int _lastBossHealth;
    private int _lastEventCount;

    private bool _lastAttackActive;

    /// <summary>지난 틱에 칼질 중이었나. 꺼졌다 켜진 틱이 새 칼질이다 (이슈 #54).</summary>
    private bool _lastSwinging;

    /// <summary>지난 틱의 칼질 번호. 1타가 끝나는 틱에 이어진 2타는 행동이 Attack 그대로라, 이 번호가 바뀐 것으로 본다.</summary>
    private int _lastComboStep;

    /// <summary>지난 틱에 패리 중이었나. 꺼졌다 켜진 틱이 새 패리다 — 칼질과 같은 규약이다.</summary>
    private bool _lastParrying;

    /// <summary>지난 틱에 보스가 탈진해 있었나. 꺼졌다 켜진 틱이 <b>무너지는 순간</b>이다 — 히트스톱이 거기 걸린다.</summary>
    private bool _lastBossExhausted;

    public BattleCues(BattleSim sim, FighterView fighterView, BossView bossView, Action<double> shake, Action hitstop)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(fighterView);
        ArgumentNullException.ThrowIfNull(bossView);
        ArgumentNullException.ThrowIfNull(shake);
        ArgumentNullException.ThrowIfNull(hitstop);
        _sim = sim;
        _fighterView = fighterView;
        _bossView = bossView;
        _shake = shake;
        _hitstop = hitstop;
        _lastFighterHealth = sim.Fighter.Health;
        _lastBossHealth = sim.Boss.Health;
    }

    /// <summary>지난 틱에 걸었나 — 이동 입력이 있고 행동이 없었다. <c>Battle</c> 이 자세(run · idle)를 고르는 데 쓴다.</summary>
    public bool Walking { get; private set; }

    /// <summary>이 판에서 가드가 깨진 횟수 (이슈 #47). <b>스크린샷이 그 순간을 노리는 데만 쓴다.</b></summary>
    public int GuardBreaks { get; private set; }

    /// <summary>이 판에서 받아친 횟수 (이슈 #53). 위와 같이 스크린샷 전용이다.</summary>
    public int Parries { get; private set; }

    /// <summary>
    /// 방금 지난 틱을 앞 틱과 견줘 뷰에 알린다. <paramref name="input"/> 은 그 틱에 규칙이 받은 입력이다.
    /// </summary>
    public void Observe(InputFrame input)
    {
        Walking = input.Move != 0 && _sim.Fighter.Action == FighterAction.Idle;

        // 회피 관측은 끝까지 간 보스 판정 하나마다 한 건 는다 — 닿으면 닿은 틱에, 빗나가거나 무적으로 흘렸으면 창이 닫히는
        // 틱에(스펙 §3.6 ①). 탈진이나 판 끝으로 끊긴 창은 관측이 없다(§3.5). 그래서 빗나간 칼의 번쩍임과 흔들림은 창이 닫힐 때
        // 난다 — 칼이 선 순간과 어긋난다. 맞았든 빗나갔든 칼은 휘둘러졌으므로 둘은 나와야 한다. 여기서 퍼지던 충격파(링)는
        // 걷었다(#81 — 보스 공격에 동그라미를 안 그린다).
        if (_sim.Events.Count > _lastEventCount)
        {
            _bossView.ActiveNow();
            _shake(0.45);

            for (int i = _lastEventCount; i < _sim.Events.Count; i++)
            {
                DodgeEvent e = _sim.Events[i];
                switch (e.Verdict)
                {
                    // 받아쳤다 — 작은 고리와 약한 흔들림이다(요청이 "화면이 약간 흔들리고 작은 성공 표시" · 이슈 #53). 0.7 은
                    // 판정마다 도는 것(0.45)보다 조금 세고 피격(1.0)보다 훨씬 약하다. 받아치면 보스가 무너지는데(#72) 그 히트스톱과
                    // 큰 흔들림은 여기가 아니라 탈진에 드는 틱이 건다(아래) — 원인이 무엇이든 같은 탈진에 같이 걸리게.
                    case HitVerdict.Parried:
                        Parries++;
                        _fighterView.ParrySuccess();
                        _shake(0.7);
                        break;

                    // 버텨낸 것과 깨진 것은 **다른 연출**이어야 한다 (이슈 #47). 같으면 화면은
                    // "막았다" 만 말하고 "무너졌다" 는 안 말하는데, 그 뒤 탈진(exhaust_seconds · #71) 동안은 아무것도 못 한다.
                    case HitVerdict.Guarded:
                        _fighterView.GuardChip();
                        break;

                    case HitVerdict.GuardBroken:
                        _fighterView.GuardBroken();
                        GuardBreaks++;
                        break;

                    default:
                        break;
                }
            }

            _lastEventCount = _sim.Events.Count;
        }

        if (_sim.Fighter.Health < _lastFighterHealth)
        {
            _fighterView.Hit();
            _shake(1.0);
        }

        if (_sim.Boss.Health < _lastBossHealth)
        {
            _bossView.Hit();
        }

        // **보스가 무너지는 틱** (#72 · 설계 §4.3 · §7.2) — 히트스톱과 흔들림이 여기 걸린다. 관측(받아친 판정)이 아니라
        // 탈진에 드는 것을 앞 틱과 견줘 잡는다: 경직 게이지로 무너진 탈진(#71)에는 받아친 관측이 없다.
        if (_sim.Boss.Exhausted && !_lastBossExhausted)
        {
            _shake(1.0);
            _hitstop();
        }

        _lastBossExhausted = _sim.Boss.Exhausted;

        // 새 칼질이 시작된 **그 틱** (이슈 #54) — 1타든, 1타가 끝나는 틱에 이어진 2타든(설계 §5.1). 2타는 행동이
        // Attack 그대로라 "행동이 바뀌었나" 로는 못 본다: 몇 번째 칼질인지가 바뀐 것을 본다. 렌더 프레임이 아니라
        // 여기(물리 틱)서 보는 이유는 FighterView.SwingBegan 의 주석에 적었다.
        bool swinging = _sim.Fighter.Action == FighterAction.Attack;
        int step = _sim.Fighter.ComboStep;
        if (swinging && (!_lastSwinging || step != _lastComboStep))
        {
            _fighterView.SwingBegan(step);
        }

        _lastSwinging = swinging;
        _lastComboStep = step;

        bool parrying = _sim.Fighter.Action == FighterAction.Parry;
        if (parrying && !_lastParrying)
        {
            _fighterView.ParryBegan();
        }

        _lastParrying = parrying;

        // 판정이 서는 **그 틱**에만 한 번. 계속 참인 동안 매 프레임 섬광을 내면 번쩍임이 아니라 조명이 된다.
        // 그림이 칼이 나가는 장으로 맞춰 서는 것도 이 틱이다 — 시트의 시계에 맡기지 않는다(이슈 #54).
        if (_sim.Fighter.AttackActive && !_lastAttackActive)
        {
            _fighterView.AttackActive();
        }

        _lastFighterHealth = _sim.Fighter.Health;
        _lastBossHealth = _sim.Boss.Health;
        _lastAttackActive = _sim.Fighter.AttackActive;
    }
}
