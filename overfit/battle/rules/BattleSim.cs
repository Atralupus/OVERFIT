using System;
using System.Collections.Generic;
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
    /// <c>HoldClock</c> 이 이 판을 거쳐 러너에 닿는지를 진짜 움직임으로는 못 잰다.
    /// </summary>
    public Func<MotionDef, MotionBounds, IBossMotion?>? Motions { get; set; }

    /// <summary>
    /// 패턴 고르기 (#72 · 설계 §4.4) — <b>선택</b>이다. 비우면 (<see cref="Seed"/>, <see cref="PatternIds"/>) 위의
    /// <see cref="UniformPicker"/> 라, 고르기를 모르는 테스트의 판이 그대로 선다. 게임과 데모는 단계의 <c>picker</c> 로
    /// 등록표(<see cref="PatternPickers"/>)에서 세워 넣는다.
    /// </summary>
    public IPatternPicker? Picker { get; set; }

    /// <summary>이 틱을 넘기면 시간 초과로 패배. <b>한 판이 반드시 끝나게 하는 안전장치다.</b></summary>
    public required int MaxTicks { get; set; }
}

/// <summary>
/// 전투 한 판 — 보스의 패턴 · 움직임 · 파이터의 칼 · 경직 게이지 · 탈진 · 승패를 한 틱씩 민다.
///
/// <para>
/// 보스의 판정을 파이터 몸에 대고, 그 결과를 몸에 싣고, 관측을 짓는 것은 여기가 아니라 <see cref="BossSwings"/> 다
/// (<see cref="BossSwings.Resolve"/> · #72 · 설계 §10 의 3번). 여기는 러너가 낸 판정을 거기 열고(<see cref="BossSwings.Open"/>)
/// 받아쳤다는 답을 받아 탈진 루틴을 부른다 — 같은 틱의 순서(보스 판정 → 파이터의 칼 → 끊기 · 설계 §3.5 5)가 여기 있다.
/// </para>
///
/// <para>
/// 패턴 선택은 <see cref="IPatternPicker"/> 한 자리다 (#72 · 설계 §4.4). 지금은 <b>무작위</b>(<c>uniform</c>)뿐이다. 일부러다 —
/// 나중에 망이 구현 하나를 더할 때 무작위가 대조군이 된다. 망이 정말 일하는지 증명할 방법이 그것 말고 없다.
/// 무작위지만 <see cref="Det"/> 로 뽑으므로 같은 시드는 같은 순서를 낸다.
/// </para>
/// </summary>
public sealed class BattleSim
{
    /// <summary>고정 60틱. 벽시계를 안 본다 — 그래야 헤드리스로 수백만 판을 돌려도 같은 결과다.</summary>
    public const double Dt = 1.0 / 60.0;

    private readonly BattleSetup _setup;

    /// <summary>패턴 고르기 — <see cref="BattleSetup.Picker"/>, 비었으면 시드 위의 uniform.</summary>
    private readonly IPatternPicker _picker;

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

    /// <summary>다음 패턴까지 남은 쉬는 틱 (설계 §3.6 ⑤ — 간격도 틱으로 센다: 0.8초 = 48틱).</summary>
    private int _gapLeft;
    private int _picks;

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

    public BattleSim(BattleSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        // 빈 명부는 여기서 막는다. 고르기가 낼 칸이 없다 — 전에는 첫 패턴을 고를 때 Det.RollInt(n: 0) 이
        // ArgumentOutOfRangeException 으로 터졌는데 판이 한참 돈 뒤라, 무엇이 잘못됐는지가 그 스택에 안 적혔다.
        // 넘겨받은 고르기(#72)라면 터지지도 않고 간격마다 pick_out_of_range 만 쌓으며 보스 없는 판이 돈다.
        // 세울 때 거절하면 부른 자리가 그대로 남는다.
        if (setup.PatternIds.Count == 0)
        {
            throw new ArgumentException("패턴 명부가 비었다 — 한 판을 세울 수 없다", nameof(setup));
        }

        _setup = setup;
        _picker = setup.Picker ?? new UniformPicker(setup.Seed, setup.PatternIds.Count);
        _swords = Swords(setup);
        _hits = BossHits.Resolve(setup.PatternIds, setup.Patterns, setup.HitShapes, setup.Fighter);
        Fighter = new Fighter(setup.Fighter, setup.Arena, setup.Arena.Width * 0.25);
        Boss = new Boss(setup.Boss, setup.Arena, setup.Arena.Width * 0.75);
        _swings = new BossSwings(Fighter, Boss, _credit);
        _poise = PoiseGauge.For(setup.Boss);

        // 보스는 파이터를 모른 채 태어난다 — 첫 프레임부터 맞으려면 여기서 한 번 맞춰야 한다.
        // 한 틱 뒤로 미루면 전투가 시작되는 그 그림에서 보스가 등을 보인다.
        Boss.Face(Fighter.X);
        _gapLeft = GapTicks;
    }

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

    /// <summary>지금까지 진행한 틱 수.</summary>
    public int Ticks { get; private set; }

    /// <summary>이 판에서 일어난 회피 관측 전부. <see cref="PlayerAxes.From"/> 에 그대로 넣는다.</summary>
    public IReadOnlyList<DodgeEvent> Events => _swings.Events;

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
    /// </summary>
    public double? NextActiveIn => _runner?.NextActiveIn;

    /// <summary>
    /// 보스 패턴이 지금 들어 있는 단계 — 뷰가 그 단계의 그림(<see cref="PatternStep.Anim"/> · <see cref="PatternStep.Frame"/>)을
    /// 그대로 붙든다(설계 §6). 패턴이 안 돌면 null. 규칙은 이 값을 안 읽는다.
    /// </summary>
    public PatternStep? BossStep => _runner?.Step;

    /// <summary>
    /// 이 틱에 규칙이 파이터에게 <b>대 본</b> 보스 판정 사각형 (월드) — 디버그 표시용 (이슈 #59 · 설계 §6.1).
    /// 표시가 이것을 받아 그리기만 하므로, 판정이 틀린 자리에 서면 화면도 그 틀린 자리를 보여 준다.
    /// </summary>
    public IReadOnlyList<HitRect> BossTestedRects => _swings.TestedRects;

    /// <summary>
    /// 선딜 중이면 <b>다음</b> 판정이 칠 자리 (월드) — 어디로 올지 미리 보인다. 러너가 낼 바로 그 판정(판을 세울 때 지은 것 ·
    /// <see cref="PatternRunner.NextHit"/>)을 지금 자리에 놓는다. 더 올 판정이 없으면 빈 목록.
    /// </summary>
    public IReadOnlyList<HitRect> BossNextRects =>
        _runner?.NextHit is { } hit
            ? hit.Shape.Place(new Placement(Boss.X, Boss.Y, Boss.Facing))
            : Array.Empty<HitRect>();

    /// <summary>
    /// 산 보스 판정이 있나 — 창이 열려 있는 동안 참이다 (#72 · 설계 §3.6 ④). 봇은 이 동안을 "판정이 지금" 으로 본다:
    /// <see cref="NextActiveIn"/> 은 판정이 서는 틱에 "다음 판정" 이기를 그쳐, 그것만 보던 봇이 8틱 창의 첫 틱에 가드를 풀고
    /// 남은 틱에 맞았다.
    /// </summary>
    public bool SwingLive => _swings.Live;

    /// <summary>
    /// 파이터가 지금 <b>실제로</b> 무엇으로 받나 (#72 · 설계 §6.1) — 이 틱에 대 본 판정이 있으면 그 판정의 태그와 견준 실효
    /// 상태다(<see cref="HitResolver.Effective"/>). 판정 보기의 몸통 색이 이것이다: 착지 띠(패리 불가) 앞에서 누른 패리가
    /// "패리 창" 색으로 칠해지면 그 색이 거짓말을 한다. 같은 틱의 사각형(<see cref="BossTestedRects"/>)과 같은 판정을 본다 —
    /// 닿아서 그 틱에 끝난 판정도 그 틱에는 이 색을 정한다. 대 본 판정이 없으면 파이터 쪽 상태 그대로다.
    /// </summary>
    public Defense FighterDefense => HitResolver.Effective(Fighter, _swings.TestedTags);

    /// <summary>이 틱에 규칙이 보스에게 <b>대 본</b> 파이터 칼 (월드). 안 댔으면 빈 목록.</summary>
    public IReadOnlyList<HitRect> FighterTestedRects =>
        _attackTested is { } tested ? tested.Shape.Place(tested.At) : Array.Empty<HitRect>();

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
        // 탈진에 드는 틱을 잡으려고 틱 시작의 탈진을 잡아 둔다 — 로그가 무엇이 바닥냈는지를 말한다(LogFighterExhaust).
        bool wasExhausted = Fighter.Exhausted;
        Fighter.Tick(input, Dt);

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

        if (Fighter.Exhausted && !wasExhausted)
        {
            LogFighterExhaust(guarding);
        }

        BattleOutcome? outcome = Outcome();
        if (outcome is not null)
        {
            // 판이 끝날 때 열린 창은 버린다 (#72 · 설계 §3.6 ③). 판을 끝낸 그 한 대는 닿은 것이라 관측이 있고, 남은 창에는
            // 결과가 없다 — 지어낸 한 줄이 시도 기록으로 가 망의 입력이 된다.
            _swings.Cut(Ticks, "end");
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
    /// 보스가 파이터에게서 두고 서는 간격. 보스 반폭 + 파이터 반폭이다.
    /// <b>벽이 아니다</b> — 파이터는 이 안으로 걸어 들어가고, 지나쳐 나간다(이슈 #27).
    /// 보스가 이 거리를 목표로 서는 이유는 <b>파이터 중심을 목표로 걸으면 둘이 완전히 겹쳐</b>
    /// 교전 거리가 늘 0 으로 수렴하기 때문이다 — 그림도 틀리고 <c>distance_bias</c> 도 상수가 된다.
    /// <b>수치를 손으로 안 적는다</b> — 캐릭터마다 반폭이 다르고(26~36) data/fighters.json 이 진실이다.
    /// </summary>
    private double Standoff => Boss.HalfWidth + Fighter.HalfWidth;

    /// <summary>패턴과 패턴 사이의 쉬는 틱. 반올림은 <see cref="TicksFor"/> 한 곳이다.</summary>
    private int GapTicks => TicksFor(Boss.PatternGap);

    /// <summary>
    /// 보스: 탈진했으면 아무것도 안 하고(공중에서 무너졌으면 내리기만 한다 · <see cref="Fall"/>), 쉬는 중이면 다가가고, 패턴 중이면
    /// 타임라인을 민다.
    /// </summary>
    private void AdvanceBoss()
    {
        // 탈진 시계만은 탈진해 있어도 돈다 — 아니면 안 풀린다. 풀리는 틱부터 쉬는 갈래로 간다.
        Boss.Tick();
        if (Boss.Exhausted)
        {
            // 탈진한 보스는 다가가지도 돌아서지도 않는다(설계 §4.3) — 공중에서 무너졌으면 높이만 따라 내린다.
            Fall();
            return;
        }

        // 경직 게이지는 탈진 동안 줄지도 않는다(설계 §4.3) — 무너질 때 비었고, 풀리는 틱부터 다시 센다. 칼(Strike)보다 먼저 민다:
        // 맞은 틱의 채움이 유예를 세우고, 유예는 다음 틱부터 준다(72틱 동안 그대로다).
        _poise.Tick();

        if (_runner is null)
        {
            _gapLeft--;

            // 방향은 **쉬는 동안에만** 바꾼다. 여기 두는 것 자체가 잠금의 절반이고
            // (나머지 절반은 Boss.Face 안의 가드다), 그래서 패턴이 서는 순간의 방향이
            // 그 패턴이 끝날 때까지 그대로 간다 — 예고가 거짓말이 되지 않는다. 예외는 움직임 하나다
            // (Boss.Move — 도약은 뛰는 틱에 착지 쪽으로 돌아선다 · 설계 §4.2).
            // **다가가는 자리와 무관하게 파이터 중심을 본다** — Standoff 는 서는 자리지 보는 곳이 아니다.
            int was = Boss.Facing;
            Boss.Face(Fighter.X);
            if (Boss.Facing != was)
            {
                Log.Debug("boss", () => $"turn facing={Boss.Facing} tick={Ticks}");
            }

            // 파이터의 중심이 아니라 **자기 쪽으로 Standoff 떨어진 자리**를 목표로 한다.
            // 중심을 노리면 보스가 파이터 위에 정확히 겹쳐 서서 교전 거리가 늘 0 이 된다.
            Boss.Approach(Fighter.X + ((Boss.X >= Fighter.X ? 1 : -1) * Standoff), Dt);
            if (_gapLeft <= 0)
            {
                Begin();
            }

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
    /// 패턴을 걷는다 — 끝까지 돌았든(러너의 end) 끊겼든(탈진 · <see cref="Exhaust"/>) 같은 여섯 줄이다 (#71 · #59 의 3/6 넘김 —
    /// 둘이 따로 적혀 있으면 하나만 고치는 날 끊긴 패턴이 무언가를 남긴다). 다음 패턴은 간격을 처음부터 센 뒤에 고른다.
    /// </summary>
    private void EndPattern()
    {
        _runner = null;
        _current = null;
        Boss.CurrentPattern = null;
        _motion = null;
        _holdClock = false;
        _gapLeft = GapTicks;
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
    /// 도는 움직임을 한 틱 민다 — 보스를 옮기고, 다음 틱의 패턴 시계를 세울지 받아 둔다. 파이터는 이번 틱을 이미 민
    /// 뒤다(<see cref="Tick"/> 의 순서) — 움직임이 읽는 파이터의 X 가 그 값이다(설계 §4.6).
    /// </summary>
    private void Move()
    {
        if (_motion is null)
        {
            _holdClock = false;
            return;
        }

        MotionStep step = _motion.Tick(new MotionContext(Boss.X, Boss.Y, Boss.Facing, Fighter.X, _motionTick++));
        Boss.Move(step.X, step.Y, step.Facing);
        _holdClock = !step.Finished && step.HoldClock;
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
    /// 무관하게 비운다 — 안 비우면 반쯤 찬 게이지가 탈진이 풀리자마자 한 대에 무너진다. 탈진이 풀리면 간격을 처음부터 세어 다음 패턴을
    /// 고른다.
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

        string id = Boss.CurrentPattern ?? "-";
        _swings.Cut(Ticks, "exhaust");

        // 공중에서 무너졌으면 움직임을 버리지 않고 높이만 따라 내리게 남긴다(설계 §4.2) — 전에는 버려서 보스가 무너진 높이에 떠 있었다.
        // 땅이면 남길 것이 없다: 돌진(5번 PR)처럼 땅을 가는 움직임은 그 자리에서 멈춘다.
        _fall = Boss.Y > 0 ? _motion : null;
        EndPattern();
        _poise.Empty();
        Boss.Exhaust(TicksFor(_setup.Boss.ExhaustSeconds));
        Log.Debug("boss", () => $"exhaust cause={cause} id={id} tick={Ticks}");
        if (_fall is not null)
        {
            Log.Debug("boss", () => $"exhaust_fall y={Boss.Y:0} x={Boss.X:0} tick={Ticks}");
        }
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

    /// <summary>다음 패턴을 고른다 — 고르기(<see cref="IPatternPicker"/>)에 몇 번째로 뽑는지를 넘긴다.</summary>
    private void Begin()
    {
        int draw = _picks;
        int index = _picker.Pick(draw);
        _picks++;
        if ((uint)index >= (uint)_setup.PatternIds.Count)
        {
            // 아래 pattern_missing 과 같은 이유로 간격을 되돌린다 — 매 틱 [E] 를 쏟지 않게. 예외로 두면 엔진의 ERROR 블록으로만
            // 나와 어느 고르기가 무엇을 냈는지가 안 남는다. 망이 들어오면 고르기가 데이터(기록)를 읽으므로 올 수 있는 자리다.
            _gapLeft = GapTicks;
            Log.Error("boss", $"pick_out_of_range index={index} roster={_setup.PatternIds.Count} draw={draw} tick={Ticks}");
            return;
        }

        string id = _setup.PatternIds[index];
        if (!_setup.Patterns.TryGetValue(id, out PatternDef? def))
        {
            // 간격을 되돌려 놓고 나간다. 안 그러면 _gapLeft 가 0 이하로 남아 다음 틱에도
            // 곧장 이 갈래로 떨어져, 유효한 id 가 뽑힐 때까지 매 틱 [E] 를 쏟는다 —
            // 헤드리스 판정이 읽는 로그가 그것으로 뒤덮인다.
            _gapLeft = GapTicks;
            Log.Error("boss", $"pattern_missing id={id}");
            return;
        }

        _current = def;
        _runner = new PatternRunner(def, _hits[id]);
        Boss.CurrentPattern = id;
        Log.Debug("boss", () => $"pattern_begin id={id} pick={_picks} tick={Ticks}");
    }

    /// <summary>
    /// 초를 틱으로. <b>규칙의 초→틱 반올림은 여기 한 곳이다</b> (설계 §3.5 · §3.6 ⑤) — 반 틱은 0 에서 먼 쪽으로 간다.
    /// 쓰는 곳은 여덟이다: 판정 창의 길이(<see cref="BossSwings.Open"/> · <see cref="BossHits"/> 의 점프 가능), 타임라인 단계의
    /// 시각 T(<see cref="PatternRunner"/>), 패턴 사이 간격(0.8초 = 48틱), 보스의 탈진(1.5초 = 90틱), 도약의 뜬 시간(<see cref="LeapMotion"/>),
    /// 경직 게이지의 유예(1.2초 = 72틱 · <see cref="PoiseGauge"/>), 파이터의 탈진(1.1초 = 66틱)과 행동 뒤 경직(#82 · <see cref="Fighter"/>).
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
        Boss.TakeDamage(damage);
        _struckThisSwing = true;
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

    /// <summary>이번 칼질이 이미 보스에 닿았나. 창이 닫히면(<c>AttackActive</c> 가 꺼지면) 풀린다.</summary>
    private bool _struckThisSwing;
}
