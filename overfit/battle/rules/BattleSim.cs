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

    /// <summary>이 단계의 보스가 쓰는 패턴 id 들. 단계가 오를수록 길어진다 (2 · 3 · 5 · 7 · 10).</summary>
    public required IReadOnlyList<string> PatternIds { get; set; }

    public required IReadOnlyDictionary<string, PatternDef> Patterns { get; set; }

    public required ulong Seed { get; set; }

    /// <summary>이 틱을 넘기면 시간 초과로 패배. <b>한 판이 반드시 끝나게 하는 안전장치다.</b></summary>
    public required int MaxTicks { get; set; }
}

/// <summary>
/// 전투 한 판. <b>여기만이 파이터와 보스를 동시에 안다.</b>
///
/// <para>
/// 패턴 선택은 지금 <b>무작위</b>다. 일부러다 — 나중에 망이 이 자리를 갈아끼울 때
/// 무작위가 대조군이 된다. 망이 정말 일하는지 증명할 방법이 그것 말고 없다.
/// 무작위지만 <see cref="Det"/> 로 뽑으므로 같은 시드는 같은 순서를 낸다.
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

    private PatternRunner? _runner;
    private PatternDef? _current;
    private double _gapLeft;
    private int _picks;

    /// <summary>회피 수단마다의 시작 시각과 공 돌리기 (<see cref="DodgeCredit"/>). 관측을 지을 때 묻는다.</summary>
    private readonly DodgeCredit _credit = new();

    /// <summary>보스의 산 판정과 그 관측 (<see cref="BossSwings"/>).</summary>
    private readonly BossSwings _swings;

    /// <summary>이 틱에 보스에게 대 본 파이터 칼 — (모양, 놓은 자리). 안 댔으면 null.</summary>
    private (HitShape Shape, Placement At)? _attackTested;

    public BattleSim(BattleSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        // 빈 명부는 여기서 막는다. 그대로 받으면 첫 패턴을 고를 때 Det.RollInt(n: 0) 이
        // ArgumentOutOfRangeException 으로 터진다 — 판이 한참 돈 뒤라, 무엇이 잘못됐는지가
        // 그 스택에 안 적힌다. 세울 때 거절하면 부른 자리가 그대로 남는다.
        if (setup.PatternIds.Count == 0)
        {
            throw new ArgumentException("패턴 명부가 비었다 — 한 판을 세울 수 없다", nameof(setup));
        }

        _setup = setup;
        _swords = Swords(setup);
        Fighter = new Fighter(setup.Fighter, setup.Arena, setup.Arena.Width * 0.25);
        Boss = new Boss(setup.Boss, setup.Arena, setup.Arena.Width * 0.75);
        _swings = new BossSwings(Fighter, Boss, _credit);

        // 보스는 파이터를 모른 채 태어난다 — 첫 프레임부터 맞으려면 여기서 한 번 맞춰야 한다.
        // 한 틱 뒤로 미루면 전투가 시작되는 그 그림에서 보스가 등을 보인다.
        Boss.Face(Fighter.X);
        _gapLeft = setup.Boss.PatternGap;
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

    /// <summary>지금까지 진행한 틱 수.</summary>
    public int Ticks { get; private set; }

    /// <summary>이 판에서 일어난 회피 관측 전부. <see cref="PlayerAxes.From"/> 에 그대로 넣는다.</summary>
    public IReadOnlyList<DodgeEvent> Events => _swings.Events;

    /// <summary>
    /// 이 판에서 지나간 <b>헛스윙</b> 수 (이슈 #48). <b>관측이 아니다</b> — 판정이 없으므로
    /// <see cref="DodgeEvent"/> 도 없고 축도 안 움직인다.
    ///
    /// <para>
    /// 그런데도 세는 이유는 <b>화면</b> 때문이다. 안 보이는 헛스윙은 미끼가 아니라 그냥 빈 시간이고,
    /// 그러면 <c>III-역린</c> 은 아무도 안 무는 함정이 된다. 규칙 층은 뷰를 모르므로
    /// (콜백을 두면 헤드리스 봇이 그것을 들고 다닌다) 뷰가 <see cref="Events"/> 개수를 보는 것과
    /// 같은 규약으로 <b>값의 차이</b>를 읽게 한다.
    /// </para>
    /// </summary>
    public int Feints { get; private set; }

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
    public double? NextActiveIn => NextActive() is { } step ? step.T - _runner!.Elapsed : null;

    /// <summary>
    /// 아직 안 지나간 첫 <c>active</c> 단계 — <b>다음 판정</b>이다. 없거나 패턴이 없으면 null.
    ///
    /// <para>
    /// 두 조회가 이 한 자리를 본다(<see cref="NextActiveIn"/> · <see cref="NextActiveGuardBreak"/>).
    /// 각자 타임라인을 훑게 두면 "다음 판정" 의 뜻이 조용히 갈리고, 그러면 링의 크기와 색이
    /// 서로 다른 대를 가리킨다.
    /// </para>
    /// </summary>
    private PatternStep? NextActive()
    {
        if (_runner is null || _current is null)
        {
            return null;
        }

        foreach (PatternStep step in _current.Timeline)
        {
            if (step.Kind == "active" && step.T > _runner.Elapsed)
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>
    /// <b>다음</b> active 판정이 가드 불가인가 (이슈 #53). 더 올 판정이 없거나 패턴이 없으면 false.
    /// 화면의 <b>빨강</b>이 이 값이다 — 빨강은 한 가지 뜻, "가드로 못 막는다 = 받아쳐라" 다.
    ///
    /// <para>
    /// <see cref="NextActiveIn"/> 과 같은 자리이고 같은 이유로 있다 — 화면이 "지금 오는 이 한 대를
    /// 막을 수 있나" 를 말해야 하기 때문이다. <c>PatternTags.HasGuardBreak</c> 로는 그 말을 못 한다:
    /// 그건 <b>패턴 단위 요약</b>이라 선딜 내내 참이고(이제 아홉 변종 전부 참이다), 그러면
    /// <b>1·2타도 빨갛게</b> 뜬다. 실제로 그렇게 떴고, 스크린샷에서 보고 고쳤다 — 막을 수 있는
    /// 판정을 "못 막는다" 고 말하는 예고는 없는 예고보다 나쁘다.
    /// </para>
    ///
    /// <para>
    /// 빨강을 마무리(<see cref="HitBox.Finisher"/>)가 아니라 이 깃발에 매다는 이유: 색이 말하는 것이
    /// "가드로 못 막는다" 이기 때문이다. 데이터에서는 둘이 언제나 같은 대이고(PatternDataTests 가
    /// 양쪽에서 못박는다) 상은 마무리에 걸리므로, 화면과 규칙이 같은 한 대를 가리킨다 — 다만 각자
    /// **자기 뜻에 맞는 칸**을 읽는다. 둘이 갈라지는 날이 오면 빨강은 여전히 "못 막는다" 를 말한다.
    /// </para>
    ///
    /// <para>
    /// 규칙 층은 이 값을 <b>안 읽는다</b>. 판정이 실제로 가드를 깨는지는 <see cref="HitBox.GuardBreak"/> 이
    /// 정하고(<see cref="BossSwings"/>), 여기 있는 것은 그 사실을 <b>미리</b> 말해 주는 예고용 조회다.
    /// </para>
    /// </summary>
    public bool NextActiveGuardBreak => NextActive() is { GuardBreak: true };

    /// <summary>
    /// 이 틱에 규칙이 파이터에게 <b>대 본</b> 보스 판정 사각형 (월드) — 디버그 표시용 (이슈 #59 · 설계 §6.1).
    /// 표시가 이것을 받아 그리기만 하므로, 판정이 틀린 자리에 서면 화면도 그 틀린 자리를 보여 준다.
    /// </summary>
    public IReadOnlyList<HitRect> BossTestedRects => _swings.TestedRects;

    /// <summary>
    /// 선딜 중이면 <b>다음</b> 판정이 칠 자리 (월드) — 어디로 올지 미리 보인다. 러너가 그 판정을 낼 때와
    /// 같은 함수(<see cref="PatternRunner.ShapeOf"/>)로 짓는다. 더 올 판정이 없으면 빈 목록.
    /// </summary>
    public IReadOnlyList<HitRect> BossNextRects =>
        NextActive() is { } step
            ? PatternRunner.ShapeOf(step).Place(new Placement(Boss.X, Boss.Y, Boss.Facing))
            : Array.Empty<HitRect>();

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
        Fighter.Tick(input, Dt);
        _credit.Remember(Ticks * Dt, input, wasGrounded, wasX, wasY, Fighter, Boss);
        AdvanceBoss();
        _swings.Resolve(Ticks);
        Strike();

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

    /// <summary>보스: 굳었으면 아무것도 안 하고, 쉬는 중이면 다가가고, 패턴 중이면 타임라인을 민다.</summary>
    private void AdvanceBoss()
    {
        // 경직 시계만은 굳어 있어도 돈다 — 아니면 안 풀린다.
        Boss.Tick(Dt);
        if (Boss.Staggered)
        {
            return;
        }

        if (_runner is null)
        {
            _gapLeft -= Dt;

            // 방향은 **쉬는 동안에만** 바꾼다. 여기 두는 것 자체가 잠금의 절반이고
            // (나머지 절반은 Boss.Face 안의 가드다), 그래서 패턴이 서는 순간의 방향이
            // 그 패턴이 끝날 때까지 그대로 간다 — 예고가 거짓말이 되지 않는다.
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

        // 헛스윙은 러너가 누적으로 센다 (이슈 #48). 차이를 여기서 옮기는 것은 판이 패턴을
        // 여러 번 돌기 때문이다 — 러너는 패턴마다 새로 서므로 그 값은 이번 패턴의 것뿐이다.
        int feintsBefore = _runner.Feints;
        foreach (HitBox box in _runner.Tick(Dt))
        {
            // 판정은 여기서 대지 않고 **살려 둔다** (이슈 #59) — 대는 곳은 BossSwings.Resolve 하나다.
            _swings.Open(box, _current!.Tags, Boss.CurrentPattern ?? "?");
        }

        if (_runner.Feints > feintsBefore)
        {
            Feints += _runner.Feints - feintsBefore;
            Log.Debug("boss", () => $"feint id={Boss.CurrentPattern} n={Feints} tick={Ticks}");
        }

        if (_runner.Finished)
        {
            Log.Debug("boss", () => $"pattern_end id={Boss.CurrentPattern} tick={Ticks}");
            _runner = null;
            _current = null;
            Boss.CurrentPattern = null;
            _gapLeft = Boss.PatternGap;
        }
    }

    /// <summary>다음 패턴을 고른다. 무작위이되 시드·도메인·뽑은 횟수로 좌표를 조회한다.</summary>
    private void Begin()
    {
        int index = Det.RollInt(_setup.Seed, Det.Domain.PatternPick, _setup.PatternIds.Count, k1: _picks);
        _picks++;
        string id = _setup.PatternIds[index];
        if (!_setup.Patterns.TryGetValue(id, out PatternDef? def))
        {
            // 간격을 되돌려 놓고 나간다. 안 그러면 _gapLeft 가 0 이하로 남아 다음 틱에도
            // 곧장 이 갈래로 떨어져, 유효한 id 가 뽑힐 때까지 매 틱 [E] 를 쏟는다 —
            // 헤드리스 판정이 읽는 로그가 그것으로 뒤덮인다.
            _gapLeft = Boss.PatternGap;
            Log.Error("boss", $"pattern_missing id={id}");
            return;
        }

        _current = def;
        _runner = new PatternRunner(def);
        Boss.CurrentPattern = id;
        Log.Debug("boss", () => $"pattern_begin id={id} pick={_picks} tick={Ticks}");
    }

    /// <summary>
    /// 판정 창 길이(초)를 틱으로. <b>반올림은 여기 한 곳이다</b> (설계 §3.5) — 8fps 한 장은 0.125초 =
    /// 7.5틱이라, 뷰와 규칙이 각자 반올림하면 반 틱씩 어긋난다. 0 이하는 한 틱 — 옛 패턴은 전부 그렇다.
    /// </summary>
    public static int TicksFor(double seconds) =>
        seconds <= 0 ? 1 : Math.Max(1, (int)Math.Round(seconds / Dt, MidpointRounding.AwayFromZero));

    /// <summary>
    /// 파이터의 칼이 보스에 닿았는가 (이슈 #59 · 설계 §5.1). 칼은 <b>판정 창 동안 산다</b> — 창의 첫 틱에 안 닿아도
    /// 그 뒤 틱에 보스가 들어오면 맞고, <b>한 번 닿으면 그 칼질은 끝난다</b>(한 번 휘두르면 한 번만 맞는다).
    /// 보스의 휘두름(<see cref="BossSwings.Resolve"/>)과 같은 규칙이다. 전에는 창의 첫 틱에만 한 번 대 봤다 — 그 틱에
    /// 1px 모자라면 창이 남아 있어도 헛쳤고, 판정 보기에서는 칼이 한 프레임만 번쩍였다.
    /// </summary>
    private void Strike()
    {
        _attackTested = null;
        if (!Fighter.AttackActive)
        {
            _struckThisSwing = false;
            return;
        }

        if (_struckThisSwing)
        {
            return;
        }

        HitShape sword = _swords[Fighter.ComboStep];
        var at = new Placement(Fighter.X, Fighter.Y, Fighter.Facing);
        _attackTested = (sword, at);
        if (ShapeHit.Test(sword, at, Boss.Body) != ShapeContact.Overlap)
        {
            return;
        }

        int damage = Fighter.AttackDamage;
        Boss.TakeDamage(damage);
        _struckThisSwing = true;

        // 레벨을 먼저 묻고 즉시 오버로드를 쓴다 — 지연 오버로드(람다)를 여기서 쓰면 안 된다 (이슈 #59 · 최종 리뷰).
        // 람다가 지역 값(damage · gap)을 붙잡으면 컴파일러는 그 클로저를 이 블록이 아니라 **메서드 입구에서**
        // 만든다: 공격하든 안 하든 매 틱 40B 다. 입력 없이 끝까지 간 한 판(시드 51 · 3단계 · 1840틱)의 규칙 쪽
        // 할당 96,016B 중 73,600B 가 이것이었고, 봇은 그런 판을 수백만 번 돈다. 두 지역 값을 이 블록 안에서
        // 선언해도 안 없어진다 — 재 보니 그대로 매 틱 40B 였다(컴파일러가 클로저 범위를 메서드 몸통으로 합친다).
        if (Log.IsEnabled(LogLevel.Debug))
        {
            double gap = Math.Abs(Fighter.X - Boss.X) - Boss.HalfWidth;
            Log.Debug("strike", $"hit boss_hp={Boss.Health} dmg={damage} step={Fighter.ComboStep} gap={gap:0} tick={Ticks}");
        }
    }

    /// <summary>이번 칼질이 이미 보스에 닿았나. 창이 닫히면(<c>AttackActive</c> 가 꺼지면) 풀린다.</summary>
    private bool _struckThisSwing;
}
