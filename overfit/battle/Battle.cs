using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Overfit.Battle.Rules;
using Overfit.Battle.View;
using Overfit.Core;

namespace Overfit.Battle;

/// <summary>
/// 전투 씬. <b>규칙을 하나도 담지 않는다</b> — <see cref="BattleSim"/> 을 고정 틱으로 돌리고
/// 그 결과를 뷰에 넘길 뿐이다. 그래서 같은 전투가 창 없이도 똑같이 돈다.
///
/// <para>
/// 뷰가 알아야 하는 <b>사건</b>(맞았다 · 패리가 받았다 · 판정이 섰다)은 시뮬레이션이
/// 이벤트로 밀어주지 않는다. 대신 여기서 <b>틱 전후를 견줘</b> 알아낸다 — 체력이 줄었나,
/// 회피 관측이 늘었나. 규칙 층에 뷰용 콜백을 달면 그 콜백이 곧 규칙의 일부가 되고,
/// 헤드리스 봇이 그걸 들고 다니게 된다.
/// </para>
/// </summary>
public partial class Battle : Node2D
{
    private BattleSim _sim = null!;
    private FighterView _fighterView = null!;
    private BossView _bossView = null!;
    private BattleHud _hud = null!;
    private BattleResult _result = null!;
    private Node2D _world = null!;
    private Vector2 _worldHome;

    // 최대 체력을 리터럴로 들지 않는다 — 시뮬레이션을 세운 바로 그 설정에서 읽는다.
    // 수치는 데이터(fighters.json · bosses.json)에 있고, 뷰는 그것을 베끼지 않는다.
    private FighterConfig _fighterConfig = null!;
    private BossConfig _bossConfig = null!;

    /// <summary>패턴 표. 뷰가 <b>태그</b>(지금은 has_guard_break)와 예고를 그리는 데만 쓴다 — 규칙은 시뮬레이션이 본다.</summary>
    private Dictionary<string, PatternDef> _patterns = null!;
    private FeelBalance _feel = null!;

    private int _stage;
    private bool _hasNextStage;

    private bool _over;
    private BattleOutcome _outcome;
    private double _resultIn;
    private bool _resultShown;

    /// <summary>데이터가 어긋나 판을 못 세웠다. 시뮬레이션이 없는 채로 틱을 돌리거나 그리지 않게 막는다.</summary>
    private bool _broken;

    /// <summary>
    /// 남은 히트스톱(프레임). <b>0 보다 크면 그 물리 프레임에 <c>BattleSim.Tick</c> 을 안 부른다.</b>
    /// <c>BattleSim.Dt</c> 는 절대 안 건드린다 — 한 틱의 길이가 달라지면 같은 입력이 다른 판을 내고
    /// 리플레이도 학습 데이터도 통째로 못 쓰게 된다. 여기서는 시계를 늘이는 게 아니라 <b>세운다.</b>
    ///
    /// <para>
    /// <b>규칙 층이 아니라 여기 있는 것은 판단이다</b> (이슈 #53). 세우는 것이라 시뮬레이션이
    /// 지나가는 상태의 열은 걸든 안 걸든 한 칸도 안 다르다 — 헤드리스 봇과 사람이 <b>같은 판</b>을
    /// 살고, 그래서 학습 데이터에 sim-to-real 간극이 안 생긴다. 규칙으로 옮기면 반대로 이 숫자가
    /// 리플레이의 일부가 되어, 손맛을 눈으로 고칠 때마다 지금까지의 리플레이가 못 쓰게 된다.
    /// 남는 차이는 사람이 벽시계로 <c>hitstop_frames</c> 만큼 더 쉰다는 것 하나이고, 이제 그것이
    /// 걸리는 자리는 <b>3타를 받아친 순간</b> 하나뿐이다 — 2.3초짜리 경직 안이라 아무 판단도 안 민다.
    /// </para>
    /// </summary>
    private int _hitstopLeft;

    private double _shakeLeft;
    private double _shakeAmp;

    // 틱 전후를 견줘 사건을 찾는다. 규칙 층에 뷰용 콜백을 달지 않기 위한 값들이다.
    private int _lastFighterHealth;
    private int _lastBossHealth;
    private int _lastEventCount;

    /// <summary>지난 프레임까지 지나간 헛스윙 수 (이슈 #48). 관측 수와 <b>같은 규약</b>이다 —
    /// 규칙 층은 뷰를 모르므로 사건을 값의 차이로 읽는다.</summary>
    private int _lastFeintCount;
    private bool _lastAttackActive;

    /// <summary>지난 틱에 칼질(공격 · 차지) 중이었나. 꺼졌다 켜진 틱이 새 칼질이다 (이슈 #54).</summary>
    private bool _lastSwinging;
    private bool _walking;

    /// <summary>지난 틱의 차지 단계. 늘어난 순간이 "단계가 올랐다" 는 사건이다 — 규칙 층에 콜백을 안 달고 여기서 견준다.</summary>
    private int _lastChargeTier;

    /// <summary>이 판에서 가드가 깨진 횟수 (이슈 #47). <b>스크린샷이 그 순간을 노리는 데만 쓴다.</b></summary>
    private int _guardBreaks;

    /// <summary>이 판에서 받아친 횟수 (이슈 #53). 위와 같이 스크린샷 전용이다.</summary>
    private int _parries;

    /// <summary>
    /// 판이 끝났나. <b>디버그 전용 읽기</b> — <c>tools/build.sh shots</c> 의 <c>ShotRunner</c> 가
    /// 셔터를 누를 때를 보는 데만 쓴다. 벽시계로 기다리면 패턴 주기(0.8초 간격 + 1.65~1.90초 패턴)와
    /// 어긋나 매번 다른 순간이 찍힌다 — 그러면 스크린샷이 "무엇이 보이는가" 를 증명하지 못한다.
    /// </summary>
    public bool Over => _over;

    /// <summary>결과 화면이 떴나. 위와 같이 디버그 전용 읽기다.</summary>
    public bool ResultVisible => _resultShown;

    /// <summary>보스가 선딜 중인가(아직 올 판정이 있다). 위와 같이 디버그 전용 읽기다.</summary>
    public bool BossWindingUp => !_broken && !_over && Phase() == BossPhase.Windup;

    /// <summary>파이터의 남은 체력. 위와 같이 디버그 전용 읽기다 — 줄어든 직후가 피격 순간이다.</summary>
    public int FighterHealth => _broken ? 0 : _sim.Fighter.Health;

    /// <summary>
    /// 공격 판정이 선 틱인가. 위와 같이 디버그 전용 읽기다 — 이 순간이 곧 <b>칼이 지나가는
    /// 프레임</b>이라(fighters.json 의 attack_anim_blade_frame), 스크린샷이 "칼이 보이는가" 를
    /// 증명하려면 프레임 수를 세는 대신 이것을 보고 셔터를 눌러야 한다. 세어 두면 공격 타이밍을
    /// 고치는 순간 조용히 어긋나 선딜 자세만 찍힌다 — 이슈 #38 전의 스크린샷이 그랬다.
    /// </summary>
    public bool FighterAttackActive => !_broken && !_over && _sim.Fighter.AttackActive;

    /// <summary>
    /// 지금 차지를 모으고 있나. 위와 같이 <b>디버그 전용 읽기</b>다 — 스크린샷이 "모으는 것이
    /// 보이는가" 를 증명하려면 규칙에게 물어보고 셔터를 눌러야 한다. 프레임 수를 세면
    /// 차지 시간을 데이터에서 고치는 순간 조용히 어긋난다(이슈 #38 에서 밟은 그 실패다).
    /// </summary>
    public bool FighterCharging => !_broken && !_over && _sim.Fighter.Charging;

    /// <summary>
    /// 차지를 얼마나 모았나(0~1). 위와 같이 디버그 전용 읽기다 — <b>중간 차지</b>를 찍으려면
    /// "모으는 중" 만으로는 모자라고 어디쯤인지를 알아야 한다. 프레임을 세는 대신 이것을 본다.
    /// </summary>
    public double FighterChargeProgress => _broken || _over ? 0 : _sim.Fighter.ChargeProgress;

    /// <summary>
    /// 차지가 <b>최대</b>에 닿았나. 위와 같이 디버그 전용 읽기다 — "모으는 중" 과 "다 모았다" 가
    /// 화면에서 갈리는지는 두 장을 나란히 놓아야만 증명된다.
    /// </summary>
    public bool FighterChargeMaxed => !_broken && !_over && _sim.Fighter.ChargeMaxed;

    /// <summary>
    /// 지금 <b>방어 자세</b>인가. 위와 같이 디버그 전용 읽기다 — 자세는 누르는 그 틱에 서지만
    /// (이슈 #53) 규칙에게 물어보는 규약은 그대로 둔다: 프레임을 세면 입력이 한 틱 밀리는 날
    /// 조용히 어긋난다.
    /// </summary>
    public bool FighterGuarding => !_broken && !_over && _sim.Fighter.Guarding;

    /// <summary>
    /// 지금까지 가드가 깨진 횟수. 위와 같이 디버그 전용 읽기다 — 붕괴는 <b>사건</b>이라 상태로는
    /// 못 본다. 늘어난 그 순간이 셔터를 누를 때다.
    /// </summary>
    public int FighterGuardBreaks => _guardBreaks;

    /// <summary>
    /// 지금까지 <b>받아친</b> 횟수 (이슈 #53). 위와 같이 디버그 전용 읽기다 — 받아친 것도 사건이라
    /// 상태로는 못 노린다. 연출이 일부러 약해진 뒤로는 더 그렇다: 고리 하나가 0.17초 떴다 사라진다.
    /// </summary>
    public int FighterParries => _parries;

    /// <summary>
    /// <b>다음 판정</b>이 가드 불가인가. 위와 같이 디버그 전용 읽기다 — 빨강 · 危 예고가 화면에서
    /// 구별되는지를 증명하려면 그것이 실제로 떠 있는 순간을 기다려야 한다.
    ///
    /// <para>
    /// <b>패턴 태그가 아니라 다음 판정을 본다</b> (이슈 #53). 태그(<c>has_guard_break</c>)로 기다리면
    /// 이제 아홉 변종 전부의 선딜 어디서나 참이라 1·2타 앞에서 셔터가 눌리고, 그 장은 "빨간 3타
    /// 예고" 라는 이름으로 호박색 1타를 찍는다.
    /// </para>
    /// </summary>
    public bool BossGuardBreak => !_broken && !_over && _sim.NextActiveGuardBreak;

    /// <summary>
    /// 보스가 <b>굳어 있나</b> (이슈 #53). 위와 같이 디버그 전용 읽기다 — 마무리를 받아친 상이
    /// 화면에서 "지쳤다" 로 읽히는지를 증명하려면 그 2.3초 안에서 셔터를 눌러야 한다.
    /// 프레임을 세지 않는 이유는 늘 같다: 경직 길이는 데이터라 세어 두면 그 값을 고치는 날
    /// 이 장이 조용히 다른 순간을 찍는다.
    /// </summary>
    public bool BossStaggered => !_broken && !_over && _sim.Boss.Staggered;

    /// <summary>
    /// 보스의 남은 체력. 위와 같이 디버그 전용 읽기다 — 줄어든 직후가 <b>흰 피격 실루엣</b>이 뜨는
    /// 순간이고(이슈 #28), 그건 0.2초뿐이라 벽시계로 노리면 대부분 놓친다.
    /// </summary>
    public int BossHealth => _broken ? 0 : _sim.Boss.Health;

    /// <summary>
    /// 지금까지 지나간 <b>헛스윙</b> 수 (이슈 #48). 위와 같이 디버그 전용 읽기다 — 헛스윙은
    /// 0.34초짜리 <b>사건</b>이라 상태로는 못 노린다. 늘어난 그 순간이 셔터를 누를 때다.
    /// </summary>
    public int BossFeints => _sim.Feints;

    /// <summary>
    /// 지금 도는 패턴 id. 위와 같이 디버그 전용 읽기다 — 스크린샷이 <b>패턴마다 다른 예고</b>를
    /// 증명하려면 "지금 어느 패턴인가" 를 보고 셔터를 눌러야 한다. 같은 패턴을 세 번 찍으면
    /// 세 장이 똑같고, 그건 증명이 아니라 우연이다.
    /// </summary>
    public string? BossPattern => _broken || _over ? null : _sim.Boss.CurrentPattern;

    /// <summary>
    /// 다음 판정까지 남은 시간(초). 더 올 판정이 없으면 0. 위와 같이 디버그 전용 읽기다 —
    /// 선딜의 <b>어디쯤인지</b>를 보고 셔터를 눌러야 하는 장면이 있다(이슈 #36 의 방향 잠금:
    /// 선딜이 넉넉히 남았을 때 지나가야 "안 돌아본다" 가 증명되고, 끝자락에 지나가면
    /// 잠금이 아니라 충격파가 찍힌다 — 실제로 그렇게 찍혔다).
    /// </summary>
    public double BossNextActiveIn => _broken || _over ? 0 : _sim.NextActiveIn ?? 0;

    public override void _Ready()
    {
        _fighterView = GetNode<FighterView>("%FighterView");
        _bossView = GetNode<BossView>("%BossView");
        _hud = GetNode<BattleHud>("%Hud");
        _result = GetNode<BattleResult>("%Result");
        _world = GetNode<Node2D>("World");
        _worldHome = _world.Position;
        _feel = Balance.Data.Feel;
        _result.Bind(OnAgain, OnTitle);

        Dictionary<string, FighterConfig> fighters = Load<FighterConfig>("res://data/fighters.json");
        Dictionary<string, BossConfig> bosses = Load<BossConfig>("res://data/bosses.json");
        Dictionary<string, PatternDef> patterns = Load<PatternDef>("res://data/patterns.json");
        Dictionary<string, StageDef> stages = Load<StageDef>("res://data/stages.json");

        // 아레나 폭 · 한 판의 상한 · 기본 보스 · 고정 캐릭터는 balance.json 이 정한다. 전에는 그 값들이
        // 게임 · 데모 · 테스트 다섯 곳에 리터럴로 흩어져 있었고 이미 갈려 있었다.
        BattleBalance battle = Balance.Data.Battle;

        // 캐릭터 하나로 계속 간다 (이슈 #22 — 3택을 만들지 않는다). 누구인지는 데이터가 정한다.
        if (!fighters.TryGetValue(battle.Fighter, out FighterConfig? fighter))
        {
            Log.Error("battle", $"fighter_missing id={battle.Fighter}");
            _broken = true;
            return;
        }

        if (!bosses.TryGetValue(battle.Boss, out BossConfig? boss))
        {
            Log.Error("battle", $"boss_missing id={battle.Boss}");
            _broken = true;
            return;
        }

        _fighterConfig = fighter;
        _bossConfig = boss;
        _patterns = patterns;

        // 단계는 Autoload 가 들고 있다 — 씬은 다시 시작할 때마다 새로 만들어지므로 여기 두면 사라진다.
        _stage = Game.Instance.Stage;
        _hasNextStage = stages.ContainsKey((_stage + 1).ToString(CultureInfo.InvariantCulture));

        // 단계 명부는 data/stages.json 이 정한다 — patterns.json 의 키 순서를 쓰면 패턴을
        // 파일 맨 위에 끼워 넣는 것만으로 1단계가 다른 전투가 된다.
        IReadOnlyList<string> ids = StageRoster.For(stages, _stage);

        _sim = new BattleSim(new BattleSetup
        {
            Arena = new Arena(battle.ArenaWidth),
            Fighter = _fighterConfig,
            Boss = _bossConfig,
            PatternIds = ids,
            Patterns = patterns,
            Seed = 51,
            MaxTicks = battle.MaxTicks,
        });

        _lastFighterHealth = _sim.Fighter.Health;
        _lastBossHealth = _sim.Boss.Health;

        // 칼질이 시작하는 장과 칼이 나가는 장을 건넨다 (이슈 #54). 차지 자세는 그 사이의 **선딜 마지막 장**
        // — 칼이 나가는 장 바로 앞이다. 뷰가 fighters.json 을 직접 읽지 않게 여기서 건네준다.
        _fighterView.Load(
            _fighterConfig.Sprite, _fighterConfig.AttackAnimStartFrame, _fighterConfig.AttackAnimBladeFrame);
        _bossView.Load(_bossConfig.Sprite);
        Log.Info("scene", $"battle ready stage={_stage} fighter={battle.Fighter} patterns={ids.Count}");
    }

    /// <summary>
    /// 전투를 민다. <b><c>_Process</c> 가 아니라 여기다.</b>
    ///
    /// <para>
    /// 전에는 렌더 프레임에서 누산기로 고정 틱을 만들었는데, 입력이 <c>IsActionJustPressed</c>
    /// (렌더 프레임 하나에만 참)라 둘의 주기가 어긋났다. 144Hz 에서는 대부분의 프레임이
    /// 0틱을 돌려 엣지가 그냥 버려지고(실측 약 58% 유실), 60Hz 아래에서는 한 프레임이
    /// 두 틱을 돌며 같은 <c>Read()</c> 를 두 번 읽어 한 번 누른 것이 두 <c>InputFrame</c> 이 됐다.
    /// </para>
    ///
    /// <para>
    /// 물리 틱은 <c>project.godot</c> 이 60으로 못박고, <c>Input</c> 은 물리 콜백 안에서
    /// <b>물리 틱 기준</b>으로 엣지를 돌려준다 — 틱과 입력이 같은 시계를 타므로 누산기도
    /// 따라잡기 상한도 필요 없고, 사람이 만드는 입력 시퀀스가 봇의 것과 같은 모양이 된다.
    /// 그 동등성이 학습 데이터 계획 전체가 서 있는 자리다.
    /// </para>
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (_over || _broken)
        {
            return;
        }

        // 히트스톱. 시계를 늘이지 않고 **세운다** — 이 프레임엔 시뮬레이션이 한 틱도 안 간다.
        // 그 사이 입력 엣지는 버려지는데, 그게 히트스톱이 뜻하는 바다(게임이 멈춘 것이다).
        if (_hitstopLeft > 0)
        {
            _hitstopLeft--;
            if (_hitstopLeft == 0)
            {
                Freeze(false);
            }

            return;
        }

        InputFrame input = Read();
        BattleOutcome? outcome = _sim.Tick(input);
        Observe(input);

        if (outcome is { } done)
        {
            Finish(done);
        }
    }

    /// <summary>그리기만 한다. 규칙은 <see cref="_PhysicsProcess"/> 가 민다.</summary>
    public override void _Process(double delta)
    {
        if (_broken)
        {
            return;
        }

        RenderFrame();
        Shake(delta);

        if (!_over || _resultShown)
        {
            return;
        }

        // 사망 애니메이션을 다 보여주고 결과를 띄운다. 죽자마자 덮으면 무엇 때문에 죽었는지가 안 남는다.
        _resultIn -= delta;
        if (_resultIn <= 0)
        {
            Reveal();
        }
    }

    /// <summary>키보드를 규칙의 입력으로. <b>봇과 같은 구조체를 만든다.</b></summary>
    private static InputFrame Read()
    {
        // 이동만 레벨이다 — 누르고 있으면 계속 가야 한다.
        // 원시 키코드가 아니라 액션으로 읽는 이유는 타이틀의 조작 안내가 InputMap 에서 글자를 뽑기 때문이다.
        // 여기서 키를 직접 보면 안내와 실제 조작이 따로 놀 수 있다.
        sbyte move = 0;
        if (Input.IsActionPressed("move_right"))
        {
            move = 1;
        }
        else if (Input.IsActionPressed("move_left"))
        {
            move = -1;
        }

        // 넷은 엣지다 — "이번 물리 틱에 눌렸나"(IsActionJustPressed) 를 본다. 물리 콜백 안에서
        // 부르므로 엣지 기준이 물리 틱이고, 틱마다 정확히 한 번만 참이다.
        // IsKeyPressed(레벨)로 읽으면 누르고 있는 동안 매 틱 발동해 InputFrame 의 계약(엣지)이 깨진다.
        //
        // 공격과 패리는 **둘 다** 싣는다 (이슈 #40 · #47). 엣지가 시작하고 레벨이 붙든다 —
        // 누른 그 틱에는 둘이 같이 참이라 차지/패리가 곧장 서고, 손을 떼면 레벨이 꺼진다.
        // 엣지를 레벨로 바꿔 한 칸으로 줄이지 않는 이유는 InputFrame 의 주석에 적어 뒀다.
        //
        // ⚠ 패리는 **여전히 엣지에서 즉시 시작한다.** 레벨을 보고 "탭인가 홀드인가" 를 기다렸다
        // 시작하면 정확 창(0.133초)이 통째로 밀려 게임의 모든 패리가 나빠진다 — 레벨은
        // 패리 동작이 끝나는 순간에만 읽히고, 그때 아직 눌려 있으면 가드로 이어진다.
        return new InputFrame(
            move,
            Input.IsActionJustPressed("jump"),
            Input.IsActionJustPressed("dash"),
            Input.IsActionJustPressed("parry"),
            Input.IsActionJustPressed("attack"),
            AttackHeld: Input.IsActionPressed("attack"),
            ParryHeld: Input.IsActionPressed("parry"));
    }

    /// <summary>
    /// 방금 지난 틱에서 <b>무슨 일이 일어났나</b>를 값의 차이로 읽어 뷰에 알린다.
    /// 규칙 층은 뷰를 모르므로 콜백이 없다 — 있으면 헤드리스 봇이 그 콜백을 들고 다니게 된다.
    /// </summary>
    private void Observe(InputFrame input)
    {
        _walking = input.Move != 0 && _sim.Fighter.Action == FighterAction.Idle;

        // 회피 관측은 보스 판정 하나마다 정확히 한 건 는다 — 늘었다는 것은 판정이 섰다는 뜻이다.
        // 맞았든 빗나갔든 칼은 휘둘러졌으므로 충격파는 나와야 한다.
        if (_sim.Events.Count > _lastEventCount)
        {
            _bossView.ActiveNow();
            ShakeFor(0.45);

            for (int i = _lastEventCount; i < _sim.Events.Count; i++)
            {
                DodgeEvent e = _sim.Events[i];
                switch (e.Verdict)
                {
                    case HitVerdict.Parried:
                        // **마무리를 받아쳤나**가 연출의 크기를 정한다 (이슈 #53) — 보스가 굳는
                        // 조건과 같은 칸이다. 규칙 층에 뷰용 콜백이 없으므로 그 사실은 관측에 실려 온다.
                        ParryLanded(finisher: e.Finisher);
                        break;

                    // 버텨낸 것과 깨진 것은 **다른 연출**이어야 한다 (이슈 #47). 같으면 화면은
                    // "막았다" 만 말하고 "무너졌다" 는 안 말하는데, 그 뒤 guard_break_lock 동안은 아무것도 못 한다.
                    case HitVerdict.Guarded:
                        _fighterView.GuardChip();
                        break;

                    case HitVerdict.GuardBroken:
                        _fighterView.GuardBroken();
                        _guardBreaks++;
                        break;

                    default:
                        break;
                }
            }

            _lastEventCount = _sim.Events.Count;
        }

        // 헛스윙은 관측을 안 남기므로 위 갈래에 안 걸린다 (이슈 #48) — 그런데 **화면에는 있어야 한다.**
        // 안 보이는 헛스윙은 미끼가 아니라 그냥 빈 시간이고, 그러면 III-역린 은 아무도 안 무는 함정이다.
        // 그림은 판정과 **다르다**: 빈 고리만 퍼지고 섬광도 흔들림도 없다(BossView.FeintNow).
        if (_sim.Feints > _lastFeintCount)
        {
            _bossView.FeintNow();
            _lastFeintCount = _sim.Feints;
        }

        if (_sim.Fighter.Health < _lastFighterHealth)
        {
            _fighterView.Hit();
            ShakeFor(1.0);
        }

        if (_sim.Boss.Health < _lastBossHealth)
        {
            _bossView.Hit();
        }

        // 새 칼질이 시작된 **그 틱** (이슈 #54). 차지 → 공격은 같은 칼질이라 안 센다.
        // 렌더 프레임이 아니라 여기(물리 틱)서 보는 이유는 FighterView.SwingBegan 의 주석에 적었다 —
        // 한 칼질이 끝난 틱과 다음 칼질이 시작한 틱이 한 렌더 프레임에 겹칠 수 있다.
        bool swinging = _sim.Fighter.Action is FighterAction.Attack or FighterAction.Charge;
        if (swinging && !_lastSwinging)
        {
            _fighterView.SwingBegan();
        }

        _lastSwinging = swinging;

        // 판정이 서는 **그 틱**에만 한 번. 계속 참인 동안 매 프레임 섬광을 내면 번쩍임이 아니라 조명이 된다.
        // 그림이 칼이 나가는 장으로 맞춰 서는 것도 이 틱이다 — 시트의 시계에 맡기지 않는다(이슈 #54).
        if (_sim.Fighter.AttackActive && !_lastAttackActive)
        {
            _fighterView.AttackActive(_sim.Fighter.ChargeTier);
        }

        // 차지 단계가 오른 **그 틱**. 모으는 중이 아니면 0 으로 되돌려 다음 차지의 첫 단계도 사건이 되게 한다.
        int tier = _sim.Fighter.Charging ? _sim.Fighter.ChargeTier : 0;
        if (tier > _lastChargeTier)
        {
            _fighterView.ChargeTierUp(_sim.Fighter.ChargeMaxed);
            Log.Debug("charge", () => $"tier={tier} max={_sim.Fighter.ChargeMaxed} tick={_sim.Ticks}");
        }

        _lastChargeTier = tier;

        _lastFighterHealth = _sim.Fighter.Health;
        _lastBossHealth = _sim.Boss.Health;
        _lastAttackActive = _sim.Fighter.AttackActive;
    }

    /// <summary>
    /// 받아쳤다 (이슈 #53). <b>1·2타에는 작은 고리와 약한 흔들림뿐</b>이고, <b>가드 불가인 3타에만</b>
    /// 히트스톱이 붙는다.
    ///
    /// <para>
    /// 전에는 모든 패리가 섬광 + 스파크 + 히트스톱 7프레임을 받았다. 그 히트스톱이 경직 0.5초와
    /// 겹쳐 <b>같은 패턴의 3타가 0.90초 뒤에 오기도 1.52초 뒤에 오기도 했다</b> — 유저가
    /// "딜레이가 매번 다르다" 고 말한 것이 이것이다. 시간을 세우는 것은 "이건 특별하다" 는 말이라,
    /// 매번 일어나는 일에 걸면 그 말이 박자를 먹는다.
    /// </para>
    ///
    /// <para>
    /// 흔들림은 <b>판정마다 도는 것(0.45)보다 조금 세고 피격(1.0)보다 훨씬 약하다.</b>
    /// 요청이 "화면이 약간 흔들리고 작은 성공 표시" 였고, 받아친 것은 맞은 것이 아니다.
    /// </para>
    /// </summary>
    /// <param name="finisher">그 판정이 패턴의 <b>마무리</b>였나. 규칙 층이 보스를 굳히는 조건과
    /// 같은 칸이다 — 화면이 서는 것과 보스가 굳는 것이 다른 조건으로 갈리면 히트스톱이 경직 없는
    /// 자리에 걸려 박자만 먹는다.</param>
    private void ParryLanded(bool finisher)
    {
        _parries++;
        _fighterView.ParrySuccess();
        ShakeFor(0.7);

        if (!finisher)
        {
            return;
        }

        _hitstopLeft = _feel.HitstopFrames;
        Freeze(true);
    }

    private void Freeze(bool frozen)
    {
        _fighterView.Freeze(frozen);
        _bossView.Freeze(frozen);
    }

    private void Finish(BattleOutcome outcome)
    {
        _over = true;
        _outcome = outcome;
        _resultIn = _feel.DeathHoldSeconds;

        // 히트스톱이 걸린 채로 끝나면 그림이 멈춘 채 남는다 — 죽는 모션을 봐야 한다.
        _hitstopLeft = 0;
        Freeze(false);

        if (outcome == BattleOutcome.Lose)
        {
            _fighterView.Die();
        }
        else
        {
            _bossView.Die();
        }

        Log.Info("scene", $"battle over outcome={outcome} stage={_stage} ticks={_sim.Ticks}");
    }

    /// <summary>
    /// 결과 화면을 띄운다. 이겼으면 여기서 단계가 오른다 — 그래야 [다음 단계] 가 정말 다음을 연다.
    ///
    /// <para>
    /// 단계 진행만 남기고 캐릭터 3택 · 스탯 강화는 만들지 않는다(이슈 #22). 단계는 성장 루프가 아니라
    /// 보스 설계의 축이다 — 단계가 오를수록 보스가 쓰는 패턴이 늘고, 그것이 게임 자체다.
    /// </para>
    /// </summary>
    private void Reveal()
    {
        _resultShown = true;
        bool won = _outcome == BattleOutcome.Win;
        bool cleared = won && !_hasNextStage;

        if (won && _hasNextStage)
        {
            Game.Instance.SetStage(_stage + 1);
        }

        string headline = cleared ? "클리어" : won ? "승리" : "패배";
        string detail = cleared
            ? $"{_stage}단계까지 전부 넘었다"
            : won
                ? $"{_stage}단계 돌파 — 다음 단계는 패턴이 늘어난다"
                : $"{_stage}단계 · 보스 체력 {_sim.Boss.Health}/{_bossConfig.MaxHealth} 남음";

        // 이긴 판에서 [다시] 는 거짓말이다 — 단계가 이미 올랐으므로 같은 판이 아니다.
        string againLabel = cleared ? "처음부터" : won ? "다음 단계" : "다시";

        _result.Reveal(won, headline, detail, againLabel);
    }

    private void OnAgain()
    {
        // 클리어했으면 판을 처음으로 되돌린다 — 안 그러면 없는 6단계를 달라고 하게 된다.
        if (_outcome == BattleOutcome.Win && !_hasNextStage)
        {
            Game.Instance.ResetRun();
        }

        Log.Info("scene", $"battle action=again stage={Game.Instance.Stage}");
        Game.Instance.GoTo(Game.Scene.Battle);
    }

    private void OnTitle()
    {
        Game.Instance.ResetRun();
        Log.Info("scene", "battle action=title");
        Game.Instance.GoTo(Game.Scene.Title);
    }

    private void ShakeFor(double scale)
    {
        _shakeLeft = _feel.ShakeSeconds * scale;
        _shakeAmp = _feel.ShakePixels * scale;
    }

    /// <summary>
    /// 화면을 흔든다. <b>World 만</b> 흔들고 HUD 는 두는 이유는 체력바가 같이 떨리면
    /// 남은 체력을 읽을 수 없기 때문이다.
    /// </summary>
    private void Shake(double delta)
    {
        if (_shakeLeft <= 0)
        {
            _world.Position = _worldHome;
            return;
        }

        _shakeLeft -= delta;
        float left = (float)Math.Max(0, _shakeLeft / _feel.ShakeSeconds);
        float amp = (float)_shakeAmp * left;

        // ⚠ 여기 난수는 **뷰 전용**이다. Det 를 안 쓴다 — 흔들림은 규칙이 아니라 그림이고,
        //   리플레이는 입력과 시드만 저장하므로 화면이 어떻게 떨렸는지는 재현 대상이 아니다.
        _world.Position = _worldHome + new Vector2(
            (GD.Randf() - 0.5f) * 2.0f * amp,
            (GD.Randf() - 0.5f) * 2.0f * amp);
    }

    // CanvasItem 에 이미 Draw() 가 있어(가상 렌더 콜백) 같은 이름을 쓰면 CS0108(가림) 경고가 난다.
    // 그 콜백을 오버라이드하는 게 아니므로 이름을 비켜 간다.
    private void RenderFrame()
    {
        _fighterView.Show(new FighterFrame(
            _sim.Fighter.X,
            _sim.Fighter.Y,
            _sim.Fighter.Facing,
            Pose(),
            _sim.Fighter.Invulnerable,
            _sim.Fighter.Locked,
            _sim.Fighter.ChargeProgress,
            _sim.Fighter.ChargeMaxed,
            // 남은 스태미나를 **비율로** 넘긴다 (이슈 #47) — 최대값의 사본을 뷰에 두면
            // fighters.json 이 움직이는 순간 가드 링이 거짓말을 한다(차지 링과 같은 규약이다).
            _fighterConfig.MaxStamina <= 0 ? 0 : _sim.Fighter.Stamina / _fighterConfig.MaxStamina));

        _bossView.Show(new BossFrame(
            _sim.Boss.X,
            _sim.Boss.Facing,
            Phase(),
            _sim.NextActiveIn,
            _sim.Boss.Staggered,
            CurrentAnim(),
            CurrentTell()));

        _hud.Show(_sim.Fighter.Health, _fighterConfig.MaxHealth, _sim.Fighter.Stamina, _fighterConfig.MaxStamina,
            _sim.Boss.Health, _bossConfig.MaxHealth);
    }

    /// <summary>규칙의 행동 → 뷰의 자세. 이 변환을 아는 것은 둘 다 아는 여기뿐이다.</summary>
    private FighterPose Pose()
    {
        if (_over && _outcome == BattleOutcome.Lose)
        {
            return FighterPose.Death;
        }

        return _sim.Fighter.Action switch
        {
            FighterAction.Dash => FighterPose.Dash,
            FighterAction.Attack => FighterPose.Attack,
            FighterAction.Charge => FighterPose.Charge,
            FighterAction.Guard => FighterPose.Guard,
            _ => _walking ? FighterPose.Run : FighterPose.Idle,
        };
    }

    /// <summary>지금 도는 패턴의 선딜 모션 이름. 패턴이 안 돌면 null.</summary>
    private string? CurrentAnim() => Current()?.Tell.Anim;

    /// <summary>
    /// 지금 도는 패턴의 예고 표지를 <b>화면 좌표로</b> 옮긴다.
    ///
    /// <para>
    /// 데이터의 <c>x</c> 는 "보스의 앞(+) 인가 뒤(-) 인가" 다. 화면의 왼/오른쪽으로 옮기려면
    /// 보스가 <b>어디를 보는지</b>를 알아야 하고, 그 답은 <see cref="Boss.Facing"/> 하나다.
    /// 안 뒤집으면 파이터가 보스 왼쪽에 설 때 "앞에 끌리는 칼" 이 등 뒤에 그려진다.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>두 x 를 견줘 여기서 다시 계산하면 안 된다</b>(이슈 #36 전에는 그랬다). 그러면 파이터가
    /// 스윙 도중에 보스를 지나가는 순간 <b>표지가 한 프레임에 반대쪽으로 튄다</b> — 몸은 잠겨 그대로인데
    /// 칼만 등 뒤로 간다. 방향을 아는 곳은 규칙 한 곳이어야 한다.
    /// </para>
    /// </summary>
    private BossTell? CurrentTell()
    {
        if (Current() is not PatternDef def)
        {
            return null;
        }

        // **다음 판정**에서 온다 (이슈 #53). 가드 불가면 예고가 호박에서 **빨강**이 되고 그 위에
        // 危 가 뜬다 — 둘은 같은 뜻("가드로 못 막는다")을 색과 모양 두 통로로 나른다(BossTell).
        //
        // ⚠ 이슈 #47 은 여기서 패턴 태그(has_guard_break)를 읽었다. 그때는 그 요약이 "무엇이
        // 오는가" 를 말하는 유일한 값이었지만, 계열이 연속타뿐인 지금 그 요약은 **선딜 내내 참**이라
        // 1·2타까지 빨갛게 칠한다 — 막을 수 있는 판정을 "못 막는다" 고 말하는 예고다.
        // 다음 판정 하나만 보면 색이 1·2타에 호박 · 3타에 빨강으로 제때 갈린다.
        return new BossTell(
            def.Tell.Id, def.Tell.X * _sim.Boss.Facing, def.Tell.Y, def.Tell.Length, _sim.NextActiveGuardBreak);
    }

    /// <summary>지금 도는 패턴의 정의. 패턴이 안 돌거나 표에 없으면 null.</summary>
    private PatternDef? Current() =>
        _sim.Boss.CurrentPattern is string id && _patterns.TryGetValue(id, out PatternDef? def) ? def : null;

    /// <summary>
    /// 보스가 패턴의 어디쯤인가. 더 올 판정이 있으면 선딜, 없으면 후딜이다 —
    /// <c>CurrentPattern</c> 하나로는 그 둘이 같은 그림이 된다.
    /// </summary>
    private BossPhase Phase()
    {
        if (_sim.Boss.CurrentPattern is null)
        {
            return BossPhase.Idle;
        }

        return _sim.NextActiveIn is null ? BossPhase.Recover : BossPhase.Windup;
    }

    private static Dictionary<string, T> Load<T>(string path)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return JsonData<T>.ParseTable(file.GetAsText(), path);
    }
}
