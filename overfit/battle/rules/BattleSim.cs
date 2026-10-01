using System;
using System.Collections.Generic;
using System.Globalization;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>한 판의 끝.</summary>
public enum BattleOutcome
{
    Win,
    Lose,
}

/// <summary>한 판을 세우는 데 필요한 전부.</summary>
public sealed class BattleSetup
{
    public required Arena Arena { get; set; }

    public required FighterConfig Fighter { get; set; }

    public required BossConfig Boss { get; set; }

    /// <summary>
    /// 판정 모양 표 — <c>hitboxes.json</c> (이슈 #59). 파이터의 칼질이 id 로 가리키는 모양을 여기서 찾는다.
    /// 규칙 층은 파일을 안 읽는다(Godot 을 모른다) — 부르는 쪽(게임 · 데모 · 테스트)이 읽어 넘긴다.
    /// </summary>
    public required IReadOnlyDictionary<string, HitShape> HitShapes { get; set; }

    /// <summary>이 단계의 보스가 쓰는 패턴 id 들 — <c>stages.json</c> 의 명부. 이 순서가 곧 뽑기 좌표다.</summary>
    public required IReadOnlyList<string> PatternIds { get; set; }

    public required IReadOnlyDictionary<string, PatternDef> Patterns { get; set; }

    /// <summary>시도 시드 (#72 · 설계 §4.4). 게임에서는 <c>RunHistory.Open</c> 이, 데모에서는 <c>--seed</c> 가 준다.</summary>
    public required ulong Seed { get; set; }

    /// <summary>
    /// 움직임 등록표 (#72 · 설계 §8.1) — <b>선택</b>이다. 비우면 <see cref="BossMotions.Create"/> 다. 테스트가 가짜 움직임을 넣는
    /// 자리다: 3번 PR 에는 패턴 시계를 세우는 움직임이 없어(도약의 <see cref="MotionStep.HoldClock"/> 은 늘 거짓이다), 움직임의
    /// <c>HoldClock</c> 이 이 판을 거쳐 러너에 닿는지를 가짜로만 잴 수 있었다. 돌진(#78)이 그 첫 진짜 움직임이 된 뒤에도 남는 까닭은
    /// 진짜 움직임이 안 내는 값 — 끝났다면서 시계를 세우는 한 걸음 — 을 판에 넣어 보는 자리여서다(<c>BossMotionTests</c>).
    /// </summary>
    public Func<MotionDef, MotionBounds, IBossMotion?>? Motions { get; set; }

    /// <summary>
    /// 계획 고르기 (#72 · 설계 2026-09-29 조각1 §4.1) — <b>선택</b>이다. 비우면 (<see cref="Seed"/>, <see cref="PatternIds"/>) 위의
    /// <see cref="UniformPlanPicker"/> 인데 <b>끊지도 달리지도 않는다</b>(<c>cancel_percent</c> · <c>run_percent</c> 0) — 쉬기는 보스의
    /// <c>rest_seconds</c> 에서 고른다. 고르기를 모르는 테스트의 판이 캔슬 · 달리기 없이 선다. 게임 · 데모 · 공장 · 골든은 단계의 <c>picker</c> 로
    /// 등록표(<see cref="PatternPickers"/>)에서 세워 넣는다(<c>StageRoster.Setup</c> — <c>balance.json</c> 의 수치로).
    /// </summary>
    public IPlanPicker? Picker { get; set; }

    /// <summary>이 틱을 넘기면 시간 초과로 패배. <b>한 판이 반드시 끝나게 하는 안전장치다.</b></summary>
    public required int MaxTicks { get; set; }

    /// <summary>보스의 시작 체력 — <b>대본으로 선 판만</b> 쓴다(GIF · 스크린샷이 전환을 찍으려고 · 설계 2026-10-01 조각1 §2.5). 없으면 최대 체력.</summary>
    public int? BossStartHealth { get; set; }
}

/// <summary>
/// 전투 한 판 — 보스의 패턴 · 움직임 · 파이터의 칼 · 폭탄과 보스의 반응 · 경직 게이지 · 탈진 · 승패를 한 틱씩 민다.
///
/// <para>
/// 보스의 판정을 파이터 몸에 대고, 그 결과를 몸에 싣고, 관측을 짓는 것은 여기가 아니라 <see cref="BossSwings"/> 다
/// (<see cref="BossSwings.Resolve"/> · #72 · 설계 §10 의 3번). 여기는 러너가 낸 판정을 거기 열고(<see cref="BossSwings.Open"/>)
/// 받아쳤다는 답을 받아 탈진 루틴을 부른다 — 같은 틱의 순서(보스 판정 → 파이터의 칼 → 끊기 · 설계 §3.5 5)가 여기 있다.
/// </para>
///
/// <para>
/// 보스가 다음을 고르는 것은 <see cref="IPlanPicker"/> 한 자리다 (#72 · 설계 2026-09-29 조각1 §4.1) — <b>계획</b>(쉬기 · 첫 동작 · 캔슬 지점 ·
/// 잇는 동작)을 통째로 고르고, 흐름(쉬기 → 첫 동작 → 끊고 잇기 → 다음 계획)은 <see cref="PlanFlow"/> 가 센다. 게임의 단계는 지금 <b>무작위</b>
/// (<c>uniform</c>)뿐이다. 일부러다 — 나중에 망이 구현 하나를 더할 때 무작위가 대조군이 된다. 망이 정말 일하는지 증명할 방법이 그것 말고 없다.
/// 무작위지만 <see cref="Det"/> 로 뽑으므로 같은 시드는 같은 계획을 낸다. 대본(<c>script</c> · #78)은 단계의 고르기가 아니다 — GIF · 스크린샷 ·
/// 순회가 계획을 고정하는 데만 쓴다.
/// </para>
///
/// <para>
/// 보스가 폭탄 던지기를 보고 끊으려 하는 것(설계 2026-09-30 조각2 §2)은 <see cref="BombWatch"/> 가 가리고(알았나 · 끊을 자리인가 · 멈칫이 남았나),
/// 하던 것을 걷고 · 돌아서고 · 반응의 동작을 세우는 것은 여기다 — 동작을 걷고 세우는 길이 판 하나여야 끊긴 동작이 무언가를 남기지 않는다
/// (<see cref="ClearPattern"/>).
/// </para>
/// </summary>
public sealed class BattleSim
{
    /// <summary>고정 60틱. 벽시계를 안 본다 — 그래야 헤드리스로 수백만 판을 돌려도 같은 결과다.</summary>
    public const double Dt = 1.0 / 60.0;

    private readonly BattleSetup _setup;

    /// <summary>
    /// 칼질 단계마다의 칼 — <c>hitboxes.json</c> 에서 그림의 흰 궤적으로 뽑은 모양 (이슈 #59 · 설계 §5.1).
    /// 판을 세울 때 한 번 찾는다(<see cref="Swords"/>). 칼질마다 모양이 다르다 — 1타와 2타는 다른 장의 궤적이다.
    /// </summary>
    private readonly HitShape[] _swords;

    /// <summary>
    /// 명부의 패턴마다 타임라인 칸별 보스 판정 — 판을 세울 때 한 번 짓는다(<see cref="BossHits"/> · #72 · 설계 §8.1). 모양이 없는
    /// 판정은 그 자리에서 전부 모아 거절한다.
    /// </summary>
    private readonly Dictionary<string, HitBox?[]> _hits;

    private PatternRunner? _runner;
    private PatternDef? _current;

    /// <summary>계획의 흐름 — 쉬기 · 첫 동작 · 캔슬 · 잇는 동작 (설계 2026-09-29 조각1 §3.3). 쉬는 틱도 틱으로 센다(설계 §3.6 ⑤).</summary>
    private readonly PlanFlow _flow;

    /// <summary>선 동작 id 들, 선 순서 — <see cref="Drawn"/>. 잇는 동작도 든다.</summary>
    private readonly List<string> _drawn = new();

    /// <summary>실제로 끊은 캔슬들 — <see cref="Cancels"/>.</summary>
    private readonly List<(string From, string To)> _cancels = new();

    /// <summary>
    /// 지금 도는 움직임 (설계 §8.1) — 러너가 움직임을 단 단계에 들 때 서고, 스스로 끝났다고 말하면 걷는다.
    /// 러너가 아니라 여기가 돌리는 이유: 움직임은 파이터의 X 를 읽는데 러너는 플레이어를 모른다.
    /// </summary>
    private IBossMotion? _motion;

    /// <summary>움직임이 시작된 뒤의 틱 — <see cref="MotionContext.Tick"/> 로 넘긴다.</summary>
    private int _motionTick;

    /// <summary>지난 틱의 움직임이 이 틱의 패턴 시계를 세웠나 (<see cref="MotionStep.HoldClock"/>).</summary>
    private bool _holdClock;

    /// <summary>
    /// 시계를 세운 움직임이 그 뒤로 더 세울 것 같은 틱 — 추정이다 (<see cref="MotionStep.HoldTicks"/> · #78). <see cref="NextActiveIn"/> 이 더한다.
    /// <b>시계를 안 세우면 0 이다</b> — <see cref="_holdClock"/> 을 거짓으로 두는 곳(<see cref="Move"/> · <see cref="EndPattern"/>)이 같이 0 으로 둔다.
    /// 그래서 읽는 쪽(<see cref="NextActiveIn"/>)은 <see cref="_holdClock"/> 을 다시 안 본다 — 전에는 둘이 따로 거르고(쓸 때 · 읽을 때) 끊긴 패턴은
    /// 이 값을 남겨 두었다(#96 · #93 리뷰 T5-M1). 읽을 때의 거름만 걷고 걷는 곳을 빠뜨리면 끊긴 돌진의 남은 틱이 다음 패턴이 선 틱의 남은 시간에
    /// 든다(BossMotionTests 가 본다).
    /// </summary>
    private int _holdTicks;

    /// <summary>도는 움직임이 끝나면 설 자리(x) — 판정 보기의 "다음 판정" 이 거기 땅에 선다 (<see cref="MotionStep.GoalX"/> · #78). 움직임이 없으면 null.</summary>
    private double? _goalX;

    /// <summary>
    /// 계획의 달리기 (설계 2026-09-29 조각1 §5.2) — 쉬기가 끝나고 계획이 달리기를 골랐을 때 서고(<see cref="StartRun"/>), 닿거나 상한을 넘기면 끝나
    /// 첫 동작을 세운다. 탈진하면 계획과 같이 걷힌다(<see cref="ClearPattern"/>).
    /// </summary>
    private readonly PlanRun _run;

    /// <summary>
    /// 공중에서 무너진 보스가 따라 내리는 움직임 (#71 · 설계 §4.2) — 끊긴 도약의 <b>높이만</b> 쓴다. 땅에서 무너졌으면 null 이다.
    /// 패턴의 움직임(<see cref="_motion"/>)과 따로 두는 이유: 패턴은 무너질 때 끊겨 러너 · 움직임 · 시계가 다 걷히는데(<see cref="EndPattern"/>)
    /// 이것만은 땅에 닿을 때까지 탈진 동안 돈다.
    /// </summary>
    private IBossMotion? _fall;

    /// <summary>
    /// 회피 수단마다의 시작 시각과 공 돌리기 (<see cref="DodgeCredit"/>). 여기는 틱마다 <see cref="DodgeCredit.Remember"/> 로
    /// 먹이기만 한다 — 관측을 지으며 묻는 것은 <see cref="BossSwings"/> 이고, 같은 인스턴스를 세울 때 넘긴다.
    /// </summary>
    private readonly DodgeCredit _credit = new();

    /// <summary>보스의 산 판정과 그 관측 (<see cref="BossSwings"/>).</summary>
    private readonly BossSwings _swings;

    /// <summary>
    /// 보스의 경직 게이지 (#71 · 설계 §4.5). 파이터의 칼이 채우고(<see cref="Strike"/>) 틱마다 줄고(<see cref="AdvanceBoss"/>) 끝까지
    /// 차면 탈진 루틴이 비운다(<see cref="Exhaust"/>). 언제 안 차고 안 주는지(결정타 · 탈진 동안)를 여기서 정한다 — 그것은 보스가
    /// 탈진해 있나와 판의 승패에 달려 있고, 게이지는 모른다.
    /// </summary>
    private readonly PoiseGauge _poise;

    /// <summary>이 틱에 보스에게 대 본 파이터 칼 — (모양, 놓은 자리). 안 댔으면 null.</summary>
    private (HitShape Shape, Placement At)? _attackTested;

    /// <summary>나는 폭탄들 (설계 2026-09-30 조각2 §1.3) — 파이터가 놓은 틱에 날리고, 날 시간이 다 되면 보스에게 떨어뜨린다.</summary>
    private readonly Bombs _bombs;

    /// <summary>보스가 던지기를 보고 · 알고 · 끊고 · 멈칫하는 것 (설계 2026-09-30 조각2 §2).</summary>
    private readonly BombWatch _watch;

    /// <summary>반응의 동작이 데이터에 없다는 <c>[E]</c> 를 이미 남겼나 — 쉬는 동안은 매 틱이 끊을 자리라 한 번만 남긴다.</summary>
    private bool _reactionMissing;

    /// <summary>이 판의 던지기들 (설계 2026-09-30 조각2 §4) — 시도 기록이 싣는다(<see cref="BombRecords"/>).</summary>
    private readonly BombLedger _ledger = new();

    /// <summary>보스의 형태 — 세 페이즈 (설계 2026-10-01 조각1 §2).</summary>
    private readonly BossForms _forms;

    public BattleSim(BattleSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        // 빈 명부는 여기서 막는다. 고르기가 낼 칸이 없다 — 전에는 첫 패턴을 고를 때 Det.RollInt(n: 0) 이
        // ArgumentOutOfRangeException 으로 터졌는데 판이 한참 돈 뒤라, 무엇이 잘못됐는지가 그 스택에 안 적혔다.
        // 넘겨받은 고르기(#72)라면 터지지도 않고 쉬기마다 plan_invalid 만 쌓으며 보스 없는 판이 돈다.
        // 세울 때 거절하면 부른 자리가 그대로 남는다.
        if (setup.PatternIds.Count == 0)
        {
            throw new ArgumentException("패턴 명부가 비었다 — 한 판을 세울 수 없다", nameof(setup));
        }

        _setup = setup;
        _swords = Swords(setup);
        // 반응의 동작(설계 2026-09-30 조각2 §2.3)은 명부에 없어도 된다 — 그 판정도 여기서 짓는다. 데이터에 없으면 건너뛰고 끊을 때 [E] 다.
        _hits = BossHits.Resolve([.. setup.PatternIds, setup.Boss.BombReaction.Move], setup.Patterns, setup.HitShapes);
        Fighter = new Fighter(setup.Fighter, setup.Arena, setup.Arena.Width * 0.25);
        Boss = new Boss(setup.Boss, setup.Arena, setup.Arena.Width * 0.75, setup.BossStartHealth);
        _forms = new BossForms(setup.Boss.Forms.Thresholds, setup.Boss.MaxHealth, Boss.Health, TicksFor(setup.Boss.Forms.ShiftSeconds));
        _swings = new BossSwings(Fighter, Boss, _credit, new JumpClearance(setup.Fighter));
        _poise = PoiseGauge.For(setup.Boss);
        _bombs = new Bombs(setup.Fighter.Bomb);
        _watch = new BombWatch(setup.Boss.BombReaction);
        _run = new PlanRun(setup.Boss);

        // 보스는 파이터를 모른 채 태어난다 — 첫 프레임부터 맞으려면 여기서 한 번 맞춰야 한다.
        // 한 틱 뒤로 미루면 전투가 시작되는 그 그림에서 보스가 등을 보인다.
        Boss.Face(Fighter.X);

        // 판이 서면 첫 계획의 쉬기부터다 (설계 2026-09-29 조각1 §3.4) — 첫 계획을 지금 고른다.
        IReadOnlyList<int> rest = RestTicks(setup.Boss);
        IPlanPicker picker = setup.Picker ?? new UniformPlanPicker(new PickerInputs(
            setup.PatternIds, setup.Patterns, rest, new PickerBalance { CancelPercent = 0, RunPercent = 0 }, Array.Empty<AttemptRecord>(), setup.Seed));
        _flow = new PlanFlow(picker, setup.PatternIds, setup.Patterns, rest, Request);
        _flow.Choose();
    }

    /// <summary>
    /// 쉬는 길이들을 틱으로 (설계 2026-09-29 조각1 §3.4) — 판과 고르기(<c>StageRoster.Setup</c>)가 같은 값을 쓰게 한 자리다. 반올림은
    /// <see cref="TicksFor"/> 한 곳이다.
    /// </summary>
    public static IReadOnlyList<int> RestTicks(BossConfig boss)
    {
        ArgumentNullException.ThrowIfNull(boss);
        var ticks = new int[boss.RestSeconds.Count];
        for (int i = 0; i < ticks.Length; i++)
        {
            ticks[i] = TicksFor(boss.RestSeconds[i]);
        }

        return ticks;
    }

    /// <summary>계획 번호 → 고르기에 넘길 것 — 이 판의 틱 · 관측 · 자리 (설계 2026-09-29 조각1 §4.1).</summary>
    private PlanRequest Request(int number) => new(number, Ticks, Events, Boss.X, Boss.Facing, Fighter.X, _forms.Form);

    /// <summary>
    /// 칼질 단계마다 칼의 모양을 찾는다. <b>판을 세울 때</b> 한 번이다 — 칼이 처음 서는 틱에 찾다 틀리면 판이 한참
    /// 돈 뒤라 무엇이 빠졌는지가 스택에 안 남는다(빈 명부를 세울 때 거절하는 것과 같은 이유다). 빠진 것은
    /// <b>전부</b> 모아 한 번에 거절한다 — 부팅이 빠진 키를 전부 나열하는 것과 같은 규약이다.
    /// </summary>
    private static HitShape[] Swords(BattleSetup setup)
    {
        List<ComboStepDef> steps = setup.Fighter.Combo;
        if (steps.Count == 0)
        {
            throw new ArgumentException("칼질이 하나도 없다 — fighters.json 의 combo 가 비었다", nameof(setup));
        }

        var swords = new HitShape[steps.Count];
        var missing = new List<string>();
        for (int i = 0; i < steps.Count; i++)
        {
            if (setup.HitShapes.TryGetValue(steps[i].Hitbox, out HitShape? shape))
            {
                swords[i] = shape;
            }
            else
            {
                missing.Add(steps[i].Hitbox);
            }
        }

        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"칼의 판정 모양이 hitboxes.json 에 없다 — {string.Join(", ", missing)}", nameof(setup));
        }

        return swords;
    }

    /// <summary>
    /// 첫 칼질의 칼이 몸 중심에서 <b>앞으로</b> 닿는 끝(px) — 그림의 궤적에서 뽑은 모양 외곽 상자의 앞끝이다.
    /// 봇이 "붙었나" 를 이것으로 잰다. 수치를 봇 쪽에 베끼지 않는다.
    /// </summary>
    public double FighterReach => _swords[0].Bounds.X1;

    public Fighter Fighter { get; }

    public Boss Boss { get; }

    /// <summary>
    /// 보스의 경직 게이지 (#71 · 설계 §4.5) — HUD 가 보스 체력바 밑에 그린다. 규칙이 채우고 비우는 것은 이 판이다(<see cref="Strike"/> ·
    /// <see cref="Exhaust"/>). 부르는 쪽은 <see cref="PoiseGauge.Value"/> · <see cref="PoiseGauge.Max"/> 만 읽는다.
    /// </summary>
    public PoiseGauge Poise => _poise;

    /// <summary>보스의 형태 — 세 페이즈 (설계 2026-10-01 조각1 §2). 뷰 · 시도 기록 · 결과 화면이 읽는다.</summary>
    public BossForms Forms => _forms;

    /// <summary>지금까지 진행한 틱 수.</summary>
    public int Ticks { get; private set; }

    /// <summary>판이 끝났으면 그 결과 — <see cref="Tick"/> 이 돌려준 첫 결과다. 되살리기(<see cref="Replay"/>)가 기록의 결과와 견준다.</summary>
    public BattleOutcome? Result { get; private set; }

    /// <summary>이 판에서 일어난 회피 관측 전부. <see cref="PlayerAxes.From"/> 에 그대로 넣는다.</summary>
    public IReadOnlyList<DodgeEvent> Events => _swings.Events;

    /// <summary>
    /// 이 판에서 선 동작 id 의 순서 (#112 · 설계 2026-09-28 §6.5) — 잇는 동작도 든다. 결과 화면의 리포트가 동작마다 몇 번 나왔는지를 센다.
    /// 버린 계획의 동작은 안 선다.
    /// </summary>
    public IReadOnlyList<string> Drawn => _drawn;

    /// <summary>이 판에서 고른 계획들, 고른 순서로 (설계 2026-09-29 조각1 §3.3) — 버린 계획은 빠진다. 끝나지 않은 마지막 계획도 든다.</summary>
    public IReadOnlyList<BossPlan> Plans => _flow.Plans;

    /// <summary><see cref="Plans"/> 를 기록의 모양으로(id · 초 — 설계 2026-09-29 조각1 §4.3). 시도 기록이 싣고 되살리기가 견준다.</summary>
    public IReadOnlyList<PlanEntry> PlanEntries => _flow.Entries;

    /// <summary>이 판에서 실제로 끊은 캔슬들 — (끊은 동작, 이은 동작). 탈진으로 못 쓴 캔슬은 안 든다(설계 2026-09-29 조각1 §3.2).</summary>
    public IReadOnlyList<(string From, string To)> Cancels => _cancels;

    /// <summary>
    /// 보스가 계획의 달리기로 파이터 앞까지 달리는 중인가 (설계 2026-09-29 조각1 §5.2) — 뷰가 <c>run</c> 을 돈다. 닿는 틱에는 거짓이다(그 틱에 첫
    /// 동작이 선다). 달리는 몸에는 판정이 없다.
    /// </summary>
    public bool BossRunning => _run.Active;

    /// <summary>
    /// 지금부터 다음 active 판정까지 남은 시간(초). 패턴이 없거나 더 올 active 가 없으면 null.
    ///
    /// <para>
    /// <see cref="Boss.CurrentPattern"/> 만으로는 "패턴이 돈다" 는 것만 알지 "지금이 언제인가" 는 모른다 —
    /// 그것만 보고 반응하면 윈드업 내내 무작정 움직이게 된다. 봇이 판정 직전에 반응하려고 이것을 본다.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>이것은 완전 정보다.</b> 사람은 선딜 모션을 보고 반응하지 다음 판정까지 남은 초를
    /// 정확히 알지 못한다. 헤드리스 구동용 최소 봇에는 괜찮지만, <b>학습 데이터를 만드는 봇 함대는
    /// 반응 지연과 잡음을 반드시 넣어야 한다</b> — 안 그러면 망이 "초인이 어떻게 실패하는가" 를
    /// 배우고, 그건 사람에게 아무 의미가 없다.
    /// </para>
    ///
    /// <para>
    /// 패턴 시계를 세운 움직임(돌진 · #78 · 설계 §4.6)이 도는 동안에는 러너의 남은 시간에 그 움직임이 더 세울 틱(추정)을 더한다 — 돌진이면
    /// "지금 자리에서 닿기까지 남은 틱 ⌈max(0, d − S) / 60⌉ + 3타의 선딜" 이다. 도착 시각이 파이터 자리에 달려 있어 <b>추정</b>이다(설계 §11).
    /// </para>
    /// </summary>
    public double? NextActiveIn => _runner?.NextActiveIn + (_holdTicks * Dt);

    /// <summary>
    /// 보스 패턴이 지금 들어 있는 단계 — 뷰가 그 단계의 그림(<see cref="PatternStep.Anim"/> · <see cref="PatternStep.Frame"/>)을
    /// 그대로 붙든다(설계 §6). 패턴이 안 돌면 null. 규칙은 이 값을 안 읽는다.
    /// </summary>
    public PatternStep? BossStep => _runner?.Step;

    /// <summary>
    /// 이 틱에 규칙이 파이터에게 <b>대 본</b> 보스 판정 사각형 (월드). 뷰 둘이 읽는다 — 판정 보기(<c>HitboxDebug</c> · 이슈 #59 ·
    /// 설계 §6.1)와 착지 충격파(<c>FloorWave</c> · #83: 높이와 끝을 여기서 읽는다). 둘 다 이것을 받아 그리기만 하므로, 판정이 틀린 자리에
    /// 서면 화면도 그 틀린 자리를 보여 준다. 규칙은 이것을 안 읽는다.
    /// </summary>
    public IReadOnlyList<HitRect> BossTestedRects => _swings.TestedRects;

    /// <summary>
    /// 선딜 중이면 <b>다음</b> 판정이 칠 자리 (월드) — 어디로 올지 미리 보인다. 러너가 낼 바로 그 판정(판을 세울 때 지은 것 ·
    /// <see cref="PatternRunner.NextHit"/>)을 지금 자리에 놓는다 — 움직임이 도는 동안에는 그 움직임이 끝나면 설 자리의 땅이다(#78 · #59 의
    /// 3/6 넘김 · <see cref="MotionStep.GoalX"/>): 도약은 착지 자리, 돌진은 지금 파이터 앞의 멈출 자리. 보스는 판정 창 동안 안 움직이므로
    /// (설계 §3.5 6) 판정은 움직임이 끝난 자리에서 선다. 더 올 판정이 없으면 빈 목록.
    /// </summary>
    public IReadOnlyList<HitRect> BossNextRects =>
        _runner?.NextHit is { } hit
            ? hit.Shape.Place(_goalX is { } goal ? new Placement(goal, 0, Boss.Facing) : new Placement(Boss.X, Boss.Y, Boss.Facing))
            : Array.Empty<HitRect>();

    /// <summary>
    /// 산 보스 판정이 있나 — 창이 열려 있는 동안 참이다 (#72 · 설계 §3.6 ④). 봇은 이 동안을 "판정이 지금" 으로 본다:
    /// <see cref="NextActiveIn"/> 은 판정이 서는 틱에 "다음 판정" 이기를 그쳐, 그것만 보던 봇이 8틱 창의 첫 틱에 가드를 풀고
    /// 남은 틱에 맞았다.
    /// </summary>
    public bool SwingLive => _swings.Live;

    /// <summary>산 판정 중에 붙드는 판정(잡기)이 있나 (#78 · <see cref="BossSwings.LiveGrab"/>) — 뷰의 흰 구가 읽는다. 규칙은 안 읽는다.</summary>
    public bool GrabLive => _swings.LiveGrab;

    /// <summary>
    /// 이 틱에 <b>대 본</b> 보스 판정이 붙드는 판정(잡기)인가 (#78 · #83) — 착지 충격파가 거른다: 잡기의 띠는 착지와 같은 바닥 전체 모양이지만
    /// 그림은 흰 구다(<c>BattleCues</c>). <see cref="BossTestedRects"/> 와 같은 판정을 본다. 규칙은 안 읽는다.
    /// </summary>
    public bool BossTestedGrab => _swings.TestedBox is { GrabHoldSeconds: > 0 };

    /// <summary>
    /// 지금 단계 바로 다음이 판정이면 그 판정과 지난 몫 (#78 · <see cref="PatternRunner.HitAhead"/>) — 뷰가 잡기의 흰 구를 그 몫만큼 날린다.
    /// 패턴이 안 돌면 null. 규칙은 안 읽는다.
    /// </summary>
    public (HitBox Hit, double Progress)? BossHitAhead => _runner?.HitAhead;

    /// <summary>
    /// 파이터가 지금 <b>실제로</b> 무엇으로 받나 (#72 · 설계 §6.1) — 이 틱에 대 본 판정이 있으면 그 판정의 태그와 답(#78 · 대시 ·
    /// 가드 · 패리)에 견준 실효 상태다(<see cref="HitResolver.Effective"/>). 판정 보기의 몸통 색이 이것이다: 착지 띠(패리 불가) 앞에서 누른 패리가
    /// "패리 창" 색으로 칠해지면 그 색이 거짓말을 한다. 같은 틱의 사각형(<see cref="BossTestedRects"/>)과 같은 판정을 본다 —
    /// 닿아서 그 틱에 끝난 판정도 그 틱에는 이 색을 정한다. 대 본 판정이 없으면 파이터 쪽 상태 그대로다.
    /// </summary>
    public Defense FighterDefense => HitResolver.Effective(Fighter, _swings.TestedTags, _swings.TestedBox);

    /// <summary>이 틱에 규칙이 보스에게 <b>대 본</b> 파이터 칼 (월드). 안 댔으면 빈 목록.</summary>
    public IReadOnlyList<HitRect> FighterTestedRects =>
        _attackTested is { } tested ? tested.Shape.Place(tested.At) : Array.Empty<HitRect>();

    /// <summary>나는 폭탄들 (설계 2026-09-30 조각2 §1.3) — 뷰가 놓은 자리에서 보스의 지금 자리로 그린다. 규칙은 안 읽는다.</summary>
    public IReadOnlyList<BombFlight> BombsInFlight => _bombs.InFlight;

    /// <summary>이 판에서 보스에게 떨어진 폭탄 수 — 뷰가 앞 틱과 견줘 터지는 불꽃을 세운다(<c>BattleCues</c>). 규칙은 안 읽는다.</summary>
    public int BombsLanded { get; private set; }

    /// <summary>
    /// 보스가 던지기를 아나 (설계 2026-09-30 조각2 §2.1) — 던진 틱부터 반응 지연이 지나고 던지기가 도는 동안이다. 뷰가 보스 머리 위에 "!" 를 띄운다 —
    /// 끊을 자리가 아직 안 와 못 끊고 있어도 "보고 있다" 가 보인다. 규칙은 이 값 대신 <see cref="BombWatch"/> 를 묻는다.
    /// </summary>
    public bool BossAlert => _watch.Aware(Ticks, Fighter.Throwing);

    /// <summary>보스가 끊고 멈칫하는 중인가 (§2.3) — 뷰가 idle 첫 장에 세운다. 규칙은 안 읽는다.</summary>
    public bool BossHesitating => _watch.Hesitating;

    /// <summary>
    /// 이 판의 던지기들, 던진 순서로 (설계 2026-09-30 조각2 §4) — 던진 틱 · 그때 보스의 동작 · 결과 · 보스가 끊은 틱. 시도 기록이 싣고(<c>bombs</c>)
    /// 되살리기가 견주고 결과 화면이 센다. 판이 끝날 때 아직 안 정해진 던지기는 <see cref="BombOutcome.End"/> 다.
    /// </summary>
    public IReadOnlyList<BombRecord> BombRecords => _ledger.Records;

    /// <summary>한 틱 민다. 판이 끝났으면 결과를, 아니면 null 을 돌려준다.</summary>
    public BattleOutcome? Tick(InputFrame input)
    {
        Ticks++;
        // 틱 시작의 접지 상태. "이번 틱에 땅에서 떨어졌는가" 는 이것과 비교해야만 알 수 있다 —
        // 공중에서 점프를 또 눌러도 Fall 이 물리적으로는 무시하지만, 그 입력만 보면 구별이 안 된다.
        bool wasGrounded = Fighter.Grounded;
        // 틱 시작의 자리도 같이 잡아둔다. 대시가 시작된 틱에는 Fighter.Tick 이 이미 한 틱만큼
        // 밀어 놓은 뒤라, 여기서 안 잡으면 "대시 전에는 어디 서 있었나" 를 되돌릴 수 없다.
        double wasX = Fighter.X;
        double wasY = Fighter.Y;
        // 탈진 · 붙들림에 드는 틱을 잡으려고 틱 시작의 둘을 잡아 둔다 — 로그가 무엇이 바닥냈는지 · 잡혔는지를 말한다(LogFighterExhaust · LogFighterHeld).
        bool wasExhausted = Fighter.Exhausted;
        bool wasHeld = Fighter.Held;
        int bombsWere = Fighter.BombsLeft;
        Fighter.Tick(input, Dt);

        // 던지는 틱이다 — 폭탄은 보스를 향해 가므로 보스 쪽으로 돌려세운다(설계 2026-09-30 조각2 §1.1). 파이터는 보스를 모른다. 보스는 이 틱에 본다(§2.1).
        if (Fighter.BombsLeft < bombsWere)
        {
            Fighter.Face(Boss.X);
            _watch.See(Ticks);
            _ledger.Throw(Ticks, Boss.CurrentPattern);
            LogThrow();
        }

        Watch();

        // 보스 판정이 볼 가드 — 파이터를 민 **뒤**의 값이다. 막다가 든 탈진(딱 0 · 붕괴)은 판정이 이 가드를 봤을 때만 난다. 틱 시작에서
        // 잡으면 이 틱에 ↓ 를 눌러 선 가드가 깨져도 action 으로 적혔다(#71 계획 리뷰가 밟았다).
        bool guarding = Fighter.Guarding;
        _credit.Remember(Ticks * Dt, input, wasGrounded, wasX, wasY, Fighter, Boss);
        AdvanceBoss();

        // 같은 틱의 순서는 보스 판정 → 파이터의 칼 → 끊기다 (설계 §3.5 5). 받아친 틱에 파이터의 칼이 먼저 돌고,
        // 그 뒤에 보스가 무너져 남은 창을 버린다. 파이터가 게이지로 무너뜨린 틱(#71)에 보스의 칼이 먼저 닿았으면 파이터는 맞는다.
        // 원인이 둘이어도 탈진은 한 번이다 — 받아친 틱에는 파이터가 패리 커밋 중이라 칼이 안 서지만, 둘이 겹치면 패리를 원인으로 친다.
        bool parried = _swings.Resolve();
        bool broken = Strike();
        if (parried)
        {
            Exhaust("parry");
        }
        else if (broken)
        {
            Exhaust("poise");
        }

        if (Fighter.Held && !wasHeld)
        {
            LogFighterHeld();
        }

        if (Fighter.Exhausted && !wasExhausted)
        {
            LogFighterExhaust(guarding);
        }

        Bomb();
        CheckForm();

        BattleOutcome? outcome = Outcome();
        if (outcome is not null)
        {
            Result ??= outcome;
            // 판이 끝날 때 열린 창은 버린다 (#72 · 설계 §3.6 ③). 판을 끝낸 그 한 대는 닿은 것이라 관측이 있고, 남은 창에는
            // 결과가 없다 — 지어낸 한 줄이 시도 기록으로 가 망의 입력이 된다. 나는 폭탄도 버린다(설계 2026-09-30 조각2 §1.3).
            _swings.Cut(Ticks, "end");
            _bombs.Clear();
        }

        return outcome;
    }

    /// <summary>판이 끝났으면 그 결과. 보스가 먼저 죽었는지를 먼저 본다 — 같은 틱이면 이긴 것이다.</summary>
    private BattleOutcome? Outcome()
    {
        if (!Boss.Alive)
        {
            Log.Info("result", $"win ticks={Ticks} fighter_hp={Fighter.Health}");
            return BattleOutcome.Win;
        }

        if (!Fighter.Alive)
        {
            Log.Info("result", $"lose reason=dead ticks={Ticks} boss_hp={Boss.Health}");
            return BattleOutcome.Lose;
        }

        if (Ticks >= _setup.MaxTicks)
        {
            Log.Info("result", $"lose reason=timeout ticks={Ticks} boss_hp={Boss.Health}");
            return BattleOutcome.Lose;
        }

        return null;
    }

    /// <summary>
    /// 두 몸이 막 닿는 중심 사이 거리 — 보스 반폭 + 파이터 반폭. <b>벽이 아니다</b> — 파이터는 이 안으로 걸어 들어가고, 지나쳐 나간다(이슈 #27).
    /// 움직임이 받는다(<see cref="MotionBounds.Standoff"/>): 도약은 파이터 중심이 아니라 이만큼 떨어진 자리에 내린다 — 중심에 내리면 둘이 완전히
    /// 겹쳐 그림도 틀리고 <c>distance_bias</c> 도 상수가 된다. 옛 보스는 쉬는 동안 이 자리를 향해 걸었다(설계 2026-09-29 조각1 §5.1 이 걷었다).
    /// <b>수치를 손으로 안 적는다</b> — 캐릭터마다 반폭이 다르고(26~36) data/fighters.json 이 진실이다.
    /// </summary>
    private double Standoff => Boss.HalfWidth + Fighter.HalfWidth;

    /// <summary>
    /// 보스: 탈진했으면 아무것도 안 하고(공중에서 무너졌으면 내리기만 한다 · <see cref="Fall"/>), 달리는 중이면 한 걸음 가고, 쉬는 중이면 제자리에서
    /// 돌아서며 계획의 쉬기를 세고, 동작 중이면 캔슬 지점에서 끊거나 타임라인을 민다.
    /// </summary>
    private void AdvanceBoss()
    {
        // 탈진 시계만은 탈진해 있어도 돈다 — 아니면 안 풀린다. 풀리는 틱부터 쉬는 갈래로 간다.
        Boss.Tick();

        // 전환 중에는 아무것도 안 한다 — 돌아서지도 않는다(탈진과 같다 · 설계 2026-10-01 조각1 §2.2). 공중이면 높이만 따라 내린다. 쉬기도 안 센다:
        // 전환을 시작할 때 고른 계획의 쉬기는 전환이 끝난 뒤부터다.
        if (_forms.Shifting)
        {
            Fall();
            if (_forms.Tick())
            {
                Log.Info("boss", $"form={_forms.Form} tick={Ticks}");
            }

            return;
        }
        if (Boss.Exhausted)
        {
            // 탈진한 보스는 달리지도 돌아서지도 않는다(설계 §4.3) — 공중에서 무너졌으면 높이만 따라 내린다.
            Fall();
            return;
        }

        // 경직 게이지는 탈진 동안 줄지도 않는다(설계 §4.3) — 무너질 때 비었고, 풀리는 틱부터 다시 센다. 칼(Strike)보다 먼저 민다:
        // 맞은 틱의 채움이 유예를 세우고, 유예는 다음 틱부터 준다(72틱 동안 그대로다).
        _poise.Tick();

        // 멈칫 — 끊은 틱부터 반응의 동작이 서기 전까지 선 채로 던지는 쪽을 본다(설계 2026-09-30 조각2 §2.3). 쉬기를 안 센다: 계획은 끊을 때 끝났다.
        // 동작 사이라 잠금이 없다 — 돌진은 보는 쪽으로만 가므로(RushMotion) 서는 틱에 파이터 쪽을 봐야 한다.
        if (_watch.Hesitating)
        {
            FaceFighter();
            if (_watch.HesitateTick())
            {
                BeginReaction();
            }

            return;
        }

        // 던지기를 알면 끊을 자리에서 끊는다(§2.2) — 계획한 캔슬보다 먼저 본다: 같은 틱이면 반응이 이긴다.
        if (ReactNow())
        {
            React();
            return;
        }

        if (_runner is null)
        {
            if (_run.Active)
            {
                RunStep();
                return;
            }

            // 방향은 **동작 사이에만** 바꾼다(쉬기 · 달리기). 여기 두는 것 자체가 잠금의 절반이고
            // (나머지 절반은 Boss.Face 안의 가드다), 그래서 패턴이 서는 순간의 방향이
            // 그 패턴이 끝날 때까지 그대로 간다 — 예고가 거짓말이 되지 않는다. 예외는 움직임 하나다
            // (Boss.Move — 도약은 뛰는 틱에 착지 쪽으로 돌아선다 · 설계 §4.2).
            //
            // **쉬는 동안 보스는 제자리다** (설계 2026-09-29 조각1 §5.1) — 돌아서기만 한다. 전에는 파이터 앞(두 몸의 반폭)을 향해 160px/s 로
            // 다가갔는데 걷기 그림이 없어 idle 그대로 미끄러졌다. 자리를 옮기는 길은 계획의 달리기 · 돌진 · 도약 셋이다.
            FaceFighter();
            if (_flow.RestTick() is int move)
            {
                if (_flow.Runs)
                {
                    StartRun(move);
                }
                else
                {
                    Begin(move);
                }
            }

            return;
        }

        // 캔슬 지점의 틱이면 러너를 안 민다 — 그 단계에 들지 않고 끊는다(설계 2026-09-29 조각1 §3.2). 시계를 세운 틱에는 러너가 안 가므로 끊지
        // 않는다(지점은 움직임 밖이라 오지 않는 자리다 · PatternDataTests).
        if (!_holdClock && _flow.CancelAt is int at && _runner.Ticks + 1 == at)
        {
            Cancel(at);
            return;
        }

        foreach (HitBox box in _runner.Tick(_holdClock))
        {
            // 판정은 여기서 대지 않고 **살려 둔다** (이슈 #59) — 대는 곳은 BossSwings.Resolve 하나다.
            _swings.Open(box, _current!.Tags, Boss.CurrentPattern ?? "?", Ticks);
        }

        if (_runner.StartedMotion is { } motion)
        {
            StartMotion(motion);
        }

        Move();

        if (_runner.Finished)
        {
            Log.Debug("boss", () => $"pattern_end id={Boss.CurrentPattern} tick={Ticks}");
            EndPattern();
        }
    }

    /// <summary>
    /// 계획이 끝났다 — 동작을 걷고 다음 계획을 고른다. 끝까지 돌았든(러너의 end) 끊겼든(탈진 · <see cref="Exhaust"/>) 같다 — 다음 계획의 쉬기는
    /// 여기서부터 센다(탈진이면 풀린 뒤부터 · 설계 2026-09-29 조각1 §3.2).
    /// </summary>
    private void EndPattern()
    {
        ClearPattern();
        _flow.Choose();
    }

    /// <summary>
    /// 동작을 걷는다 — 계획이 끝날 때(<see cref="EndPattern"/> · 탈진)와 캔슬(<see cref="Cancel"/>)이 같은 여덟 줄이다 (#71 · #59 의 3/6 넘김 — 따로
    /// 적혀 있으면 하나만 고치는 날 끊긴 동작이 무언가를 남긴다). 달리기도 여기서 걷힌다 — 달리는 동안 탈진하면 계획이 끝난다(설계 2026-09-29
    /// 조각1 §5.2).
    /// </summary>
    private void ClearPattern()
    {
        _runner = null;
        _current = null;
        Boss.CurrentPattern = null;
        _motion = null;
        _holdClock = false;
        _holdTicks = 0;
        _goalX = null;
        _run.Clear();
        _watch.Stop();
    }

    /// <summary>
    /// 캔슬 (설계 2026-09-29 조각1 §3.2) — 러너가 지점의 단계에 들기 전에 ① 하던 동작을 걷고(쉬지 않는다) ② 파이터 쪽으로 돌아서고(방향 잠금은
    /// 동작이 도는 동안의 것이라 동작 사이인 이 틱에는 풀린다 · <see cref="Boss.Face"/>) ③ 잇는 동작을 이 틱에 세운다 — 다음 틱에 그 첫 단계에
    /// 든다. 지점은 판정 창과 움직임 밖이라(<c>PatternDataTests</c>) 걷을 것은 러너 하나다.
    /// </summary>
    /// <param name="at">끊은 러너 틱 — 로그의 <c>at=</c>.</param>
    private void Cancel(int at)
    {
        string from = Boss.CurrentPattern ?? "-";
        ClearPattern();
        Boss.Face(Fighter.X);
        Begin(_flow.Cancel());
        string to = Boss.CurrentPattern ?? "-";
        _cancels.Add((from, to));
        Log.Debug("boss", () => $"cancel id={from} at={at} next={to} facing={Boss.Facing} tick={Ticks}");
    }

    /// <summary>
    /// 이 틱에 끊나 (설계 2026-09-30 조각2 §2.2) — 끊을 자리인가는 <see cref="BombWatch.CutSpot"/> 가 가린다(쉬기 · 달리기면 아무 틱 · 동작이면 캔슬
    /// 지점). 캔슬 지점이 없는 동작은 끝나고 쉬기에 든 뒤에 온다. 탈진한 보스는 여기 안 온다(<see cref="AdvanceBoss"/>).
    ///
    /// <para>
    /// 반응의 동작이 데이터에 없으면 규칙 위반이다 — 움직임의 <c>motion_missing</c> 과 같은 대우로, 끊지 않고 <c>[E]</c> 를 남긴다(데이터 테스트가 먼저
    /// 막는다). 쉬는 동안은 매 틱이 끊을 자리라 한 판에 한 번만 남긴다.
    /// </para>
    /// </summary>
    private bool ReactNow()
    {
        if (!_watch.CutSpot(Ticks, Fighter.Throwing, _current, RunnerNext, _holdClock))
        {
            return false;
        }

        if (_hits.ContainsKey(_watch.Move))
        {
            return true;
        }

        if (!_reactionMissing)
        {
            _reactionMissing = true;
            Log.Error("boss", $"bomb_reaction_missing id={_watch.Move} tick={Ticks}");
        }

        return false;
    }

    /// <summary>
    /// 끊는다 (설계 2026-09-30 조각2 §2.3) — 하던 것(동작 · 달리기 · 쉬기)을 걷고, <b>계획을 끝내고</b>(남은 캔슬 · 잇는 동작 · 쉬기를 버린다 — 안 버리면
    /// 옛 계획의 캔슬 지점이 반응의 동작을 끊는다), 던지는 쪽으로 돌아서고(동작 사이라 잠금이 없다), 멈칫을 센다. 반응의 동작은 멈칫이 끝나는 틱에
    /// 선다(<see cref="BeginReaction"/>). 계획한 캔슬(<see cref="Cancel"/>)과 같은 장치다 — 다른 것은 어느 지점이든 쓰고 · 계획이 끝나고 · 멈칫이 낀다.
    /// </summary>
    private void React()
    {
        string from = Boss.CurrentPattern ?? (_run.Active ? "run" : "rest");
        string at = _runner is null ? "" : $" at={RunnerNext.ToString(CultureInfo.InvariantCulture)}";
        ClearPattern();
        _flow.Drop();
        Boss.Face(Fighter.X);
        _watch.React();
        _ledger.React(Ticks);
        Log.Debug("boss", () => $"bomb_react from={from}{at} tick={Ticks}");
    }

    /// <summary>
    /// 멈칫이 끝났다 — 반응의 동작(<c>bomb_reaction.move</c>)을 세운다 (§2.3). 명부 밖이어도 된다 — 판정은 판을 세울 때 지었다. 이 동작이 끝나면 여느 동작처럼
    /// 다음 계획을 고른다(<see cref="EndPattern"/>).
    /// </summary>
    private void BeginReaction()
    {
        Begin(_watch.Move);
        _watch.Begin();
    }

    /// <summary>
    /// 공중에서 무너진 보스를 한 틱 내린다 (#71 · 설계 §4.2). 끊긴 도약을 그대로 한 틱 더 밀어 <b>높이만</b> 쓴다 — 포물선의 높이를
    /// 그대로 따라 그 자리에 내린다. 가로는 멈추고 돌아서지도 않는다(탈진한 보스는 아무것도 안 한다). 착지 판정은 패턴과 같이 끊겨
    /// 안 선다. 땅에 닿으면 걷는다 — 도약이 탈진보다 짧아 늘 탈진 안에 닿는다(<c>PatternDataTests</c> 가 본다).
    /// </summary>
    private void Fall()
    {
        if (_fall is null)
        {
            return;
        }

        MotionStep step = _fall.Tick(new MotionContext(Boss.X, Boss.Y, Boss.Facing, Fighter.X, _motionTick++));
        Boss.Move(Boss.X, step.Y, 0);
        if (step.Finished || Boss.Y <= 0)
        {
            _fall = null;
            Log.Debug("boss", () => $"exhaust_landed x={Boss.X:0} tick={Ticks}");
        }
    }

    /// <summary>
    /// 러너가 방금 든 단계의 움직임을 세운다 (설계 §8.1). 등록표에 없는 id 는 규칙 위반이다 — 데이터 테스트가
    /// 먼저 막지만, 여기까지 오면 움직이지 않고 <c>[E]</c> 를 남긴다(보스는 제자리에서 패턴을 끝까지 돈다).
    /// </summary>
    private void StartMotion(MotionDef def)
    {
        var bounds = new MotionBounds(Boss.HalfWidth, _setup.Arena.Width - Boss.HalfWidth, Standoff);
        _motion = _setup.Motions is { } make ? make(def, bounds) : BossMotions.Create(def, bounds);
        _motionTick = 0;
        if (_motion is null)
        {
            Log.Error("boss", $"motion_missing id={def.Id} pattern={Boss.CurrentPattern} tick={Ticks}");
            return;
        }

        Log.Debug("boss", () => $"motion_begin id={def.Id} pattern={Boss.CurrentPattern} fighter_x={Fighter.X:0} tick={Ticks}");
    }

    /// <summary>
    /// 도는 움직임을 한 틱 민다 — 보스를 옮기고, 다음 틱의 패턴 시계를 세울지(와 더 세울 틱 · 끝나면 설 자리)를 받아 둔다.
    /// 파이터는 이번 틱을 이미 민 뒤다(<see cref="Tick"/> 의 순서) — 움직임이 읽는 파이터의 X 가 그 값이다(설계 §4.6).
    /// </summary>
    private void Move()
    {
        if (_motion is null)
        {
            _holdClock = false;
            _holdTicks = 0;
            return;
        }

        MotionStep step = _motion.Tick(new MotionContext(Boss.X, Boss.Y, Boss.Facing, Fighter.X, _motionTick++));
        Boss.Move(step.X, step.Y, step.Facing);

        // 끝난 움직임의 HoldClock 은 안 따른다 — 끝난 움직임은 여기서 걷혀 다음 틱에 시계를 풀어 줄 자리가 없다(돌진이 시계를 세우는 첫
        // 움직임이다 · #59 의 3/6 넘김 — BossMotionTests 가 못박는다).
        _holdClock = !step.Finished && step.HoldClock;
        _holdTicks = _holdClock ? step.HoldTicks : 0;
        _goalX = step.Finished ? null : step.GoalX;
        if (step.Finished)
        {
            _motion = null;
            Log.Debug("boss", () => $"motion_end x={Boss.X:0} facing={Boss.Facing} tick={Ticks}");
        }
    }

    /// <summary>
    /// <b>탈진 루틴 — 하나다</b> (#72 · #71 · 설계 §4.3). 원인이 패리든 경직 게이지든 같은 상태 · 같은 그림에 닿아야
    /// 유저가 말한 "패리당했을때와 동일하게" 가 선다. 하던 패턴이 그 자리에서 끊기고(남은 타격은 안 온다 · 움직임은 공중이면 높이만
    /// 따라 내리고 땅이면 멈춘다 — <see cref="Fall"/>), 열린 창은 관측 없이 버린다(<see cref="BossSwings.Cut"/>). 게이지는 원인과
    /// 무관하게 비운다 — 안 비우면 반쯤 찬 게이지가 탈진이 풀리자마자 한 대에 무너진다. 계획이 끝난다 — 남은 캔슬 · 잇는 동작은 버리고 다음
    /// 계획을 이 틱에 고르며, 그 쉬기는 탈진이 풀린 뒤부터 센다(설계 2026-09-29 조각1 §3.2).
    /// </summary>
    /// <param name="cause">무엇이 무너뜨렸나 — <c>parry</c> · <c>poise</c>. 로그의 <c>cause=</c> 다.</param>
    private void Exhaust(string cause)
    {
        // 탈진한 보스에게는 판정도 채움도 없어 다시 무너질 길이 없다 — 오면 규칙 위반이다.
        if (Boss.Exhausted)
        {
            Log.Error("boss", $"exhaust_reentry cause={cause} tick={Ticks}");
            return;
        }

        string id = Boss.CurrentPattern ?? (_run.Active ? "run" : "-");
        _swings.Cut(Ticks, "exhaust");

        // 공중에서 무너졌으면 움직임을 버리지 않고 높이만 따라 내리게 남긴다(설계 §4.2) — 전에는 버려서 보스가 무너진 높이에 떠 있었다.
        // 땅이면 남길 것이 없다: 돌진(#78)처럼 땅을 가는 움직임은 그 자리에서 멈춘다.
        _fall = Boss.Y > 0 ? _motion : null;
        ClearPattern();
        _poise.Empty();
        Boss.Exhaust(TicksFor(_setup.Boss.ExhaustSeconds));
        Log.Debug("boss", () => $"exhaust cause={cause} id={id} tick={Ticks}");
        if (_fall is not null)
        {
            Log.Debug("boss", () => $"exhaust_fall y={Boss.Y:0} x={Boss.X:0} tick={Ticks}");
        }

        _flow.Choose();
    }

    /// <summary>
    /// 파이터가 탈진에 든 틱 (#71 · 설계 §5.5) — 무엇이 바닥냈나를 남긴다: 막다가(<c>guard</c> — 딱 0 이 된 칩 · 붕괴)냐, 끝난 행동의
    /// 값(<c>action</c>)이냐. 보스 판정 앞(파이터를 민 뒤)에 가드였으면 막다가다 — 행동의 값으로 난 탈진은 파이터를 미는 동안(행동이 끝나는
    /// 틱) 들고 그때 파이터는 선다(가드가 아니다). 가드의 칩과 붕괴는 판정이 가드를 봐야만 난다.
    /// 관측 줄([dodge])은 막다가 난 탈진만 싣는다 — 행동의 값으로 난 탈진은 이 줄이 유일한 흔적이다. 따로 둔 메서드인 것은
    /// 람다가 <see cref="Tick"/> 의 지역 값을 붙잡으면 클로저가 메서드 입구에서 매 틱 만들어지기 때문이다(<see cref="Strike"/> 의 주석).
    /// </summary>
    private void LogFighterExhaust(bool guarding) =>
        Log.Debug("fighter", () => $"exhaust cause={(guarding ? "guard" : "action")} tick={Ticks}");

    /// <summary>
    /// 파이터가 붙들린 틱 (#78 · 설계 §4.7) — 그 틱에 탈진이 겹쳤나를 같이 남긴다. 겹치는 길은 둘이다: 탈진한 채 잡혔거나(이미 탈진),
    /// 잡기가 마지막 스태미나의 행동을 끊었다(이 줄 다음에 <c>exhaust cause=action</c> 이 같은 틱으로 이어진다). 관측 줄(<c>[dodge]</c>)이
    /// 결과 <c>Grabbed</c> 와 수단을 싣는다.
    /// </summary>
    private void LogFighterHeld() => Log.Debug("fighter", () => $"held exhausted={Fighter.Exhausted} tick={Ticks}");

    /// <summary>파이터가 폭탄을 손에 든 틱 (설계 2026-09-30 조각2 §1.1) — 남은 개수를 같이 남긴다.</summary>
    private void LogThrow() => Log.Debug("fighter", () => $"bomb_throw left={Fighter.BombsLeft} tick={Ticks}");

    /// <summary>
    /// 보스가 던지기를 지켜본 것을 남긴다 (설계 2026-09-30 조각2 §2.6) — 파이터를 민 뒤 · 보스를 밀기 전이다. 처음 안 틱(<c>bomb_seen</c>)과, 알았지만 끊을
    /// 자리가 안 와 못 끊고 놓인 틱(<c>bomb_late</c>)이다. 규칙은 여기서 아무것도 안 바꾼다 — 끊는 것은 <see cref="ReactNow"/> 다.
    /// </summary>
    private void Watch()
    {
        if (_watch.JustAware(Ticks, Fighter.Throwing))
        {
            Log.Debug("boss", () => $"bomb_seen throw={_watch.SeenAt} tick={Ticks}");
        }

        if (Fighter.ThrowReleased && _watch.Knew(Ticks) && !_watch.Reacted)
        {
            Log.Debug("boss", () => $"bomb_late next={_watch.NextChance(Boss.Exhausted, _current, RunnerNext)}"
                + $" release={Ticks} tick={Ticks}");
        }
    }

    /// <summary>러너가 이번 틱에 들 틱 — 러너의 틱 + 1. 동작이 없으면 뜻이 없다(1).</summary>
    private int RunnerNext => (_runner?.Ticks ?? 0) + 1;

    /// <summary>
    /// 폭탄의 틱 (설계 2026-09-30 조각2 §1.2 · §1.3) — 칼 뒤 · 승패 앞이다. 날 시간이 다 된 폭탄이 보스에게 떨어지고(폭탄이 보스를 죽이면 이 틱의 승패가
    /// 이긴다), 이 틱에 놓은 폭탄을 그 <b>뒤에</b> 날린다 — 그래야 놓은 틱 + 나는 틱에 떨어진다. 이 틱에 끊긴 던지기는 무엇이 끊었는지 남긴다: 끊은 판정의
    /// 관측이 방금 들어왔다(<see cref="BossSwings.Resolve"/>). 반응의 동작이 도는 중이면 <c>react=1</c> 이다 — 끊을 자리(캔슬 지점 · 쉬기 · 달리기)에는
    /// 산 판정이 없고 멈칫에도 없어서, 반응의 동작 중에 끊긴 던지기는 그 동작이 끊은 것이다. 경직 게이지는 안 채운다 — 폭탄은 탈진의 도구가 아니다.
    /// </summary>
    private void Bomb()
    {
        int landed = _bombs.Tick();
        if (landed > 0)
        {
            BombsLanded += landed;
            _ledger.Landed(landed);
            DamageBoss(landed * _bombs.Damage, "bomb");
            if (Log.IsEnabled(LogLevel.Debug))
            {
                Log.Debug("bomb", $"land dmg={landed * _bombs.Damage} boss_hp={Boss.Health} tick={Ticks}");
            }
        }

        if (Fighter.ThrowReleased)
        {
            _bombs.Launch(Fighter.X, Fighter.Y);
            _ledger.Released();
            Log.Debug("fighter", () => $"bomb_release x={Fighter.X:0} tick={Ticks}");
        }

        if (Fighter.ThrowLost)
        {
            _ledger.Lost(byReaction: _watch.InReaction);
            Log.Debug("fighter", () => $"bomb_lost by={(Events.Count > 0 ? Events[^1].PatternId : "-")} react={(_watch.InReaction ? 1 : 0)}"
                + $" tick={Ticks}");
        }
    }

    /// <summary>파이터 쪽으로 돌아선다 — 동작 사이(쉬기 · 달리기)의 틱마다. 돌아선 틱을 남긴다.</summary>
    private void FaceFighter()
    {
        int was = Boss.Facing;
        Boss.Face(Fighter.X);
        if (Boss.Facing != was)
        {
            Log.Debug("boss", () => $"turn facing={Boss.Facing} tick={Ticks}");
        }
    }

    /// <summary>
    /// 달리기를 세운다 (설계 2026-09-29 조각1 §5.2) — 쉬기가 끝난 틱이다. 돌진과 같은 움직임(등록표의 <c>run</c>)에 <c>bosses.json</c> 의 빠르기 ·
    /// 멈출 거리를 싣고 <b>이 틱부터</b> 달린다 — 이미 멈출 거리 안이면 이 걸음이 곧 끝이라 같은 틱에 첫 동작이 선다(안 달린다).
    /// </summary>
    private void StartRun(int move)
    {
        var bounds = new MotionBounds(Boss.HalfWidth, _setup.Arena.Width - Boss.HalfWidth, Standoff);
        if (!_run.Start(move, Boss, Fighter.X, bounds, Ticks))
        {
            Begin(move);
            return;
        }

        RunStep();
    }

    /// <summary>
    /// 달리기 한 걸음 (§5.2) — 파이터 쪽으로 돌아서고(동작 사이라 잠금이 없다 — 파이터가 보스를 넘어가면 돌아서서 따라간다) 앞으로만 간다(<see cref="PlanRun.Step"/>).
    /// 닿거나 상한을 넘기면 그 틱에 첫 동작을 세운다.
    /// </summary>
    private void RunStep()
    {
        FaceFighter();
        if (_run.Step(Boss, Fighter.X, Ticks) is int move)
        {
            Begin(move);
        }
    }

    /// <summary>
    /// 명부의 <paramref name="index"/> 칸 동작을 세운다 — 계획의 첫 동작(쉬기가 끝난 틱)이든 잇는 동작(캔슬한 틱)이든. 칸과 정의는 계획을 고를 때
    /// 이미 봤다(<see cref="PlanFlow"/> 가 틀린 계획을 버린다).
    /// </summary>
    private void Begin(int index) => Begin(_setup.PatternIds[index]);

    /// <summary>
    /// 동작 <paramref name="id"/> 를 세운다 — 명부의 칸(<see cref="Begin(int)"/>)이든 반응의 동작(<see cref="BeginReaction"/>)이든. 판정은 판을 세울 때
    /// 지었다(<see cref="BossHits"/>).
    /// </summary>
    private void Begin(string id)
    {
        PatternDef def = _setup.Patterns[id];
        _current = def;
        _runner = new PatternRunner(def, _hits[id]);
        Boss.CurrentPattern = id;
        _drawn.Add(id);
        Log.Debug("boss", () => $"pattern_begin id={id} tick={Ticks}");
    }

    /// <summary>
    /// 초를 틱으로. <b>규칙의 초→틱 반올림은 여기 한 곳이다</b> (설계 §3.5 · §3.6 ⑤) — 반 틱은 0 에서 먼 쪽으로 간다.
    /// 쓰는 곳은 아홉이다: 판정 창의 길이(<see cref="BossSwings.Open"/> — 점프 가능도 그 창의 틱 수로 잰다 · #85), 타임라인 단계의
    /// 시각 T(<see cref="PatternRunner"/>), 쉬는 길이(0.4 · 0.8 · 1.2초 = 24 · 48 · 72틱 · <see cref="RestTicks"/>), 보스의 탈진(1.5초 = 90틱), 도약의 뜬 시간(<see cref="LeapMotion"/>),
    /// 경직 게이지의 유예(1.2초 = 72틱 · <see cref="PoiseGauge"/>), 파이터의 탈진(1.1초 = 66틱)과 행동 뒤 경직(#82 · <see cref="Fighter"/>),
    /// 잡기가 붙드는 시간(1.0초 = 60틱 · #78 · <see cref="BossSwings"/> 가 <see cref="Fighter.Grab"/> 에 넘긴다).
    /// 8fps 한 장은 0.125초 = 7.5틱이라, 이 중 둘이 각자 반올림하면 반 틱씩 어긋난다.
    ///
    /// <para>
    /// 한 틱보다 짧은 값은 0 이하까지 전부 <b>한 틱</b>이다 — 어느 쓰임에도 0 틱이 안 나온다(행동 뒤 경직만 0 을 "경직 없음" 으로 읽어
    /// 여기 오기 전에 거른다 — <c>Fighter.StiffTicks</c>). 타임라인에서는 그래서 T = 0 인 첫 단계가 틱 1 이고, 러너는 제 첫 틱을
    /// 1 로 세므로(<see cref="PatternRunner.Ticks"/>) 그 단계는 <b>러너의 첫 틱</b>에 든다(설계 §3.6 ⑤). 한 틱(1/60초) 아래의 T 도
    /// 같은 틱이다.
    /// </para>
    /// </summary>
    public static int TicksFor(double seconds) =>
        seconds <= 0 ? 1 : Math.Max(1, (int)Math.Round(seconds / Dt, MidpointRounding.AwayFromZero));

    /// <summary>
    /// 파이터의 칼이 보스에 닿았는가 (이슈 #59 · 설계 §5.1). 칼은 <b>판정 창 동안 산다</b> — 창의 첫 틱에 안 닿아도
    /// 그 뒤 틱에 보스가 들어오면 맞고, <b>한 번 닿으면 그 칼질은 끝난다</b>(한 번 휘두르면 한 번만 맞는다).
    /// 보스의 휘두름(<see cref="BossSwings.Resolve"/>)과 같은 규칙이다. 전에는 창의 첫 틱에만 한 번 대 봤다 — 그 틱에
    /// 1px 모자라면 창이 남아 있어도 헛쳤고, 판정 보기에서는 칼이 한 프레임만 번쩍였다.
    ///
    /// <para>
    /// 닿으면 보스의 경직 게이지가 그 칼질의 경직도만큼 찬다 (#71 · 설계 §4.5) — <b>결정타는 안 채우고</b>(이긴 판에 탈진은 뜻이 없다)
    /// <b>탈진 동안에도 안 채운다</b>(풀리자마자 다시 무너지는 연속 탈진이 없다). 보스는 맞아도 하던 것을 안 멈춘다 — 규칙에서 바뀌는
    /// 것은 체력과 게이지뿐이다.
    /// </para>
    /// </summary>
    /// <returns>이 칼이 게이지를 끝까지 채웠나 — 참이면 <see cref="Tick"/> 이 끊기 자리에서 탈진시킨다.</returns>
    private bool Strike()
    {
        _attackTested = null;
        if (!Fighter.AttackActive)
        {
            _struckThisSwing = false;
            return false;
        }

        if (_struckThisSwing)
        {
            return false;
        }

        HitShape sword = _swords[Fighter.ComboStep];
        var at = new Placement(Fighter.X, Fighter.Y, Fighter.Facing);
        _attackTested = (sword, at);
        if (ShapeHit.Test(sword, at, Boss.Body) != ShapeContact.Overlap)
        {
            return false;
        }

        int damage = Fighter.AttackDamage;
        _struckThisSwing = true;
        if (!DamageBoss(damage, "strike"))
        {
            return false;
        }

        double filled = Boss.Alive && !Boss.Exhausted ? _poise.Fill(Fighter.AttackPoise) : 0;

        // 레벨을 먼저 묻고 즉시 오버로드를 쓴다 — 지연 오버로드(람다)를 여기서 쓰면 안 된다 (이슈 #59 · 최종 리뷰).
        // 람다가 지역 값(damage · gap)을 붙잡으면 컴파일러는 그 클로저를 이 블록이 아니라 **메서드 입구에서**
        // 만든다: 공격하든 안 하든 매 틱 40B 다. 입력 없이 끝까지 간 한 판(시드 51 · 옛 3단계 · 1840틱)의 규칙 쪽
        // 할당 96,016B 중 73,600B 가 이것이었고, 봇은 그런 판을 수백만 번 돈다. 두 지역 값을 이 블록 안에서
        // 선언해도 안 없어진다 — 재 보니 그대로 매 틱 40B 였다(컴파일러가 클로저 범위를 메서드 몸통으로 합친다).
        //
        // poise 는 **실제로 찬 양**이다(설계 §4.5) — 탈진 중이거나 결정타면 0, 끝에서 넘친 몫은 뺀다. gauge 는 찬 뒤의 값이다.
        if (Log.IsEnabled(LogLevel.Debug))
        {
            double gap = Math.Abs(Fighter.X - Boss.X) - Boss.HalfWidth;
            Log.Debug("strike", $"hit boss_hp={Boss.Health} dmg={damage} step={Fighter.ComboStep} gap={gap:0}"
                + $" poise={filled:0.##} gauge={_poise.Value:0.##} tick={Ticks}");
        }

        return filled > 0 && _poise.Full;
    }

    /// <summary>
    /// 보스에게 피해를 준다 — 칼과 폭탄이 지나는 한 자리 (설계 2026-10-01 조각1 §2.1 · §2.3). 전환 중이면 무적이라 안 깎고 <c>[D] shielded</c> 를
    /// 남긴다. 아니면 바닥(다음 문턱)까지만 깎는다 — 페이즈마다 깎을 체력이 정확히 300 · 500 · 400 이다(930 에서 폭탄 60 을 맞아도 900).
    /// </summary>
    /// <returns>깎았나 — 무적이면 거짓. 칼은 거짓이면 경직도 안 채운다.</returns>
    private bool DamageBoss(int amount, string source)
    {
        if (_forms.Shifting)
        {
            if (Log.IsEnabled(LogLevel.Debug))
            {
                Log.Debug("boss", $"shielded src={source} dmg={amount} tick={Ticks}");
            }

            return false;
        }

        Boss.TakeDamage(Math.Min(amount, Boss.Health - _forms.Floor));
        return true;
    }

    /// <summary>바닥에 닿았으면 전환을 시작한다 — 폭탄 뒤 · 승패 앞(§2.2). 칼과 폭탄이 같은 틱에 닿아도 한 번이다.</summary>
    private void CheckForm()
    {
        if (_forms.Reached(Boss.Health))
        {
            BeginShift();
        }
    }

    /// <summary>
    /// 전환을 시작한다 (§2.2) — 탈진과 같은 걷기다: 열린 창을 버리고 · 공중이면 높이만 따라 내리고 · 하던 것을 걷고 · 게이지를 비운다. 같은 틱에 게이지로
    /// 무너졌으면 그 탈진을 끝낸다 — 전환이 이긴다(탈진이 남으면 전환 뒤 보스가 굳은 채 선다). 계획을 고르고, 그 쉬기는 전환이 끝난 뒤부터 센다
    /// (<see cref="AdvanceBoss"/> 가 전환 동안 흐름을 안 민다). 본 던지기는 안 잊는다 — 전환이 끝나면 끊을 자리를 기다린다(§2.3).
    /// </summary>
    private void BeginShift()
    {
        _swings.Cut(Ticks, "form");
        _fall = Boss.Y > 0 ? (_motion ?? _fall) : null;
        ClearPattern();
        _poise.Empty();
        Boss.Exhaust(0);
        _forms.Begin(Ticks);
        Log.Info("boss", $"form_shift from={_forms.Form} to={_forms.Form + 1} hp={Boss.Health} tick={Ticks}");
        _flow.Choose();
    }

    /// <summary>이번 칼질이 이미 보스에 닿았나. 창이 닫히면(<c>AttackActive</c> 가 꺼지면) 풀린다.</summary>
    private bool _struckThisSwing;
}
