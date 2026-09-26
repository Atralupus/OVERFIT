using System;
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
/// 이벤트로 밀어주지 않는다. 대신 <see cref="BattleCues"/> 가 <b>틱 전후를 견줘</b> 알아낸다 — 체력이 줄었나,
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

    /// <summary>
    /// 판정 보기 (설계 §6.1). <b><c>--debug-collisions</c> 로 띄웠을 때만</b> 선다 — 아니면 null.
    /// Godot 의 Visible Collision Shapes 는 실행 중에 못 켠다(SceneTree.debug_collisions_hint 문서)라
    /// 켜고 띄웠을 때만 노드를 세운다. 안 켰으면 사각형을 옮기는 비용도 없다.
    /// </summary>
    private HitboxDebug? _hitboxDebug;

    // 최대 체력을 리터럴로 들지 않는다 — 시뮬레이션을 세운 바로 그 설정에서 읽는다.
    // 수치는 데이터(fighters.json · bosses.json)에 있고, 뷰는 그것을 베끼지 않는다.
    private FighterConfig _fighterConfig = null!;
    private BossConfig _bossConfig = null!;

    private FeelBalance _feel = null!;

    private int _stage;
    private bool _hasNextStage;

    /// <summary>이 전투의 시도 — 번호와 시드 (#72 · 설계 §4.4). 설 때 열고, 끝나면 그 판의 관측과 함께 기록에 붙인다.</summary>
    private (int Number, ulong Seed) _attempt;

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
    /// 남는 차이는 사람이 벽시계로 <c>hitstop_frames</c> 만큼 더 쉰다는 것 하나이고, 그것이 걸리는 자리는
    /// <b>보스가 무너지는 순간</b>이다(#72) — 1.5초짜리 탈진 안이라 아무 판단도 안 민다. 그동안 누른 키는
    /// 버리지 않고 끝난 첫 틱에 넘긴다(<see cref="_carried"/> · #71).
    /// </para>
    /// </summary>
    private int _hitstopLeft;

    /// <summary>
    /// 히트스톱 동안 누른 엣지 (#71 · 설계 §1 「대화로 정한 것」). 세운 프레임에는 시뮬레이션이 안 돌아 입력을 받을 틱이 없다 —
    /// 모아 두었다가 히트스톱이 끝난 첫 틱의 입력에 싣는다(<see cref="InputFrame.Carry"/>). 받아친 것을 보고 곧장 누른 J(되받아치기)가
    /// 그 7프레임에 떨어지는 일이 흔하다 — 전에는 버려져 "눌렀는데 안 나간" 칼이 됐다.
    /// </summary>
    private InputFrame _carried;

    private double _shakeLeft;
    private double _shakeAmp;

    /// <summary>틱 전후를 견줘 사건을 찾아 뷰에 알린다 (<see cref="BattleCues"/>). 규칙 층에 뷰용 콜백을 달지 않기 위한 자리다.</summary>
    private BattleCues _cues = null!;

    /// <summary>
    /// 판이 끝났나. <b>디버그 전용 읽기</b> — <c>tools/build.sh shots</c> 의 <c>ShotRunner</c> 가
    /// 셔터를 누를 때를 보는 데만 쓴다. 벽시계로 기다리면 패턴 주기(0.8초 간격 + 3연격 3.25초 또는 점프 공격 1.5초)와
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
    /// 이 틱에 규칙이 보스에게 파이터 칼을 <b>대 봤나</b> (설계 §6.1 · §9) — <see cref="BossSwingTested"/> 의 파이터 쪽이다. 위와 같이
    /// 디버그 전용 읽기다. 대 본 틱은 곧 <b>칼이 지나가는 프레임</b>이라(fighters.json 의 combo 한 칸의 blade_frame), 스크린샷이
    /// "칼이 보이는가" 를 증명하려면 <b>프레임 수를 세지 않고 규칙에게 묻는다</b> — 세어 두면 공격 타이밍을 고치는 순간 조용히 어긋나
    /// 선딜 자세만 찍힌다. 이슈 #38 전의 스크린샷이 그랬다.
    ///
    /// <para>
    /// 칼의 창이 살아 있나(<c>Fighter.AttackActive</c>)로 물으면 모자라다: 칼은 한 번 닿으면 그 틱에 끝나(<c>BattleSim.Strike</c>) 다음
    /// 틱부터 채운 사각형이 없는데 창은 계속 산다. 그렇게 물어 찍던 때, 걸어 들어오는 보스에게 첫 칼이 창의 첫 틱에 닿아
    /// (gap=-82 · tick 213) 판정 보기의 <c>battle-5-attack</c> 이 흰 궤적만 찍혔다 (#72).
    /// </para>
    /// </summary>
    public bool FighterSwingTested => !_broken && !_over && _sim.FighterTestedRects.Count > 0;

    /// <summary>
    /// 지금 칼질이 몇 번째인가 (0 = 1타). 디버그 전용 읽기다 — 스크린샷이 2타를 노리려면 규칙에게 물어야 한다.
    /// 프레임을 세면 2타의 선딜을 데이터에서 고치는 날 조용히 다른 순간이 찍힌다.
    /// </summary>
    public int FighterComboStep => _broken || _over ? 0 : _sim.Fighter.ComboStep;

    /// <summary>
    /// 지금 <b>가드</b>인가(↓ 를 누르고 있다 · 설계 §5.2). 위와 같이 디버그 전용 읽기다 — 규칙에게 물어보는
    /// 규약은 그대로 둔다: 프레임을 세면 입력이 한 틱 밀리는 날 조용히 어긋난다.
    /// </summary>
    public bool FighterGuarding => !_broken && !_over && _sim.Fighter.Guarding;

    /// <summary>
    /// 지금 패리 행동 중인가 — 커밋(0.333초)과 그 뒤 패리 뒤 경직(0.25초 · #82)을 합친 0.583초다. 디버그 전용 읽기다 —
    /// 패리는 이제 누르는 것 한 번이라(설계 §5.3) 스크린샷이 그 사이를 노리려면 규칙에게 물어야 한다(<c>battle-4-parry</c> 는
    /// 참이 된 뒤 10프레임 — 커밋의 한가운데다).
    /// </summary>
    public bool FighterParrying => !_broken && !_over && _sim.Fighter.Action == FighterAction.Parry;

    /// <summary>
    /// 지금까지 가드가 깨진 횟수. 위와 같이 디버그 전용 읽기다 — 붕괴는 <b>사건</b>이라 상태로는
    /// 못 본다. 늘어난 그 순간이 셔터를 누를 때다.
    /// </summary>
    public int FighterGuardBreaks => _broken ? 0 : _cues.GuardBreaks;

    /// <summary>
    /// 지금까지 <b>받아친</b> 횟수 (이슈 #53). 위와 같이 디버그 전용 읽기다 — 받아친 것도 사건이라
    /// 상태로는 못 노린다. 연출이 일부러 약해진 뒤로는 더 그렇다: 고리 하나가 0.17초 떴다 사라진다.
    /// </summary>
    public int FighterParries => _broken ? 0 : _cues.Parries;

    /// <summary>
    /// 보스가 <b>탈진했나</b> (#72 · 설계 §4.3). 위와 같이 디버그 전용 읽기다 — 받아친 상이 화면에서 "무너졌다" 로
    /// 읽히는지를 증명하려면 그 1.5초 안에서 셔터를 눌러야 한다. 프레임을 세지 않는 이유는 늘 같다: 탈진 길이는
    /// 데이터라 세어 두면 그 값을 고치는 날 이 장이 조용히 다른 순간을 찍는다.
    /// </summary>
    public bool BossExhausted => !_broken && !_over && _sim.Boss.Exhausted;

    /// <summary>
    /// 보스의 경직 게이지가 얼마나 찼나 0~1 (#71 · 설계 §4.5). 위와 같이 디버그 전용 읽기다 — "게이지가 반쯤 찬 장" 은 칼이 몇 번
    /// 닿았는지가 아니라 게이지를 보고 찍는다: 칼질마다의 경직도는 데이터라 세어 두면 그 값을 고치는 날 다른 장이 찍힌다.
    /// </summary>
    public double BossPoise => _broken || _over || _sim.Poise.Max <= 0 ? 0 : _sim.Poise.Value / _sim.Poise.Max;

    /// <summary>파이터가 탈진했나 (#71 · 설계 §5.5). 위와 같이 디버그 전용 읽기다 — 탈진한 장은 규칙에게 물어 찍는다.</summary>
    public bool FighterExhausted => !_broken && !_over && _sim.Fighter.Exhausted;

    /// <summary>
    /// 파이터가 새 행동을 받나 — 칼질 · 대시 · 패리(행동 뒤 경직까지 · #82)도 탈진도 아니다. 위와 같이 디버그 전용 읽기다 — 스크린샷이
    /// 칼질을 다시 누를 때를 규칙에게 묻는다. 벽시계 간격(0.4초)으로 누르던 때, 칼질 뒤 경직이 들자 둘째 J 가 1타의 경직에 떨어져 2타가 됐다.
    /// </summary>
    public bool FighterFree =>
        !_broken && !_over && _sim.Fighter.Action == FighterAction.Idle && !_sim.Fighter.Exhausted;

    /// <summary>
    /// 보스의 남은 체력. 위와 같이 디버그 전용 읽기다 — 줄어든 직후가 보스가 <b>희게 번쩍이는</b> 순간이고(#71 ·
    /// <c>hit_flash.gdshader</c>), 그건 <c>feel.boss_hit_flash_seconds</c>(0.12초)뿐이라 벽시계로 노리면 대부분 놓친다.
    /// </summary>
    public int BossHealth => _broken ? 0 : _sim.Boss.Health;

    /// <summary>
    /// 지금 도는 패턴 id. 위와 같이 디버그 전용 읽기다 — 스크린샷이 <b>패턴마다 다른 그림</b>(3연격의 칼 · 점프 공격의
    /// 도약)을 증명하려면 "지금 어느 패턴인가" 를 보고 셔터를 눌러야 한다. 패턴은 무작위로 뽑힌다.
    /// </summary>
    public string? BossPattern => _broken || _over ? null : _sim.Boss.CurrentPattern;

    /// <summary>
    /// 보스의 발바닥 높이 (#72 · 설계 §4.2). 위와 같이 디버그 전용 읽기다 — 공중의 점프 공격을 찍으려면 정점 근처에서
    /// 셔터를 눌러야 하고, 도약 시각은 데이터(<c>motion.air</c>)라 프레임을 세면 그 값을 고치는 날 땅이 찍힌다.
    /// </summary>
    public double BossY => _broken || _over ? 0 : _sim.Boss.Y;

    /// <summary>
    /// 이 틱에 규칙이 파이터에게 보스 판정을 <b>대 봤나</b> (설계 §6.1). 위와 같이 디버그 전용 읽기다 — 판정 보기(<c>HITBOXES=1</c>)의
    /// 사진이 흰 궤적 위의 채운 사각형과 실효 몸통 색을 보이려면 사각형을 그리는 그 틱에 셔터를 눌러야 한다. 창이 산 동안
    /// (<c>SwingLive</c>)으로는 모자라다: 땅에 선 몸은 3연격과 착지 띠에 창의 첫 틱에 닿고, 닿은 판정은 그 틱에 끝나 틱 사이에
    /// 한 번도 "살아 있다" 로 안 읽힌다.
    /// </summary>
    public bool BossSwingTested => !_broken && !_over && _sim.BossTestedRects.Count > 0;

    /// <summary>
    /// 다음 판정까지 남은 시간(초). 더 올 판정이 없으면 0. 위와 같이 디버그 전용 읽기다 —
    /// 선딜의 <b>어디쯤인지</b>를 보고 셔터를 눌러야 하는 장면이 있다(이슈 #36 의 방향 잠금:
    /// 선딜이 넉넉히 남았을 때 지나가야 "안 돌아본다" 가 증명되고, 끝자락에 지나가면
    /// 잠긴 몸이 아니라 판정이 선 순간이 찍힌다 — 실제로 그렇게 찍혔다. 그때는 판정 충격파가 화면을 덮었고, 그 링은 #81 로 걷었다).
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

        // 데이터 다섯은 데모와 같은 자리에서 읽는다(BattleTables). 판정 모양도 데이터다 (이슈 #59) — 규칙은 파일을 모른다.
        BattleTables data = BattleTables.Load();

        // 아레나 폭 · 한 판의 상한 · 기본 보스 · 고정 캐릭터는 balance.json 이 정한다. 전에는 그 값들이
        // 게임 · 데모 · 테스트 다섯 곳에 리터럴로 흩어져 있었고 이미 갈려 있었다.
        BattleBalance battle = Balance.Data.Battle;

        // 캐릭터 하나로 계속 간다 (이슈 #22 — 3택을 만들지 않는다). 누구인지는 데이터가 정한다.
        if (!data.Fighters.TryGetValue(battle.Fighter, out FighterConfig? fighter))
        {
            Log.Error("battle", $"fighter_missing id={battle.Fighter}");
            _broken = true;
            return;
        }

        if (!data.Bosses.TryGetValue(battle.Boss, out BossConfig? boss))
        {
            Log.Error("battle", $"boss_missing id={battle.Boss}");
            _broken = true;
            return;
        }

        _fighterConfig = fighter;
        _bossConfig = boss;

        // 단계는 Autoload 가 들고 있다 — 씬은 다시 시작할 때마다 새로 만들어지므로 여기 두면 사라진다.
        _stage = Game.Instance.Stage;
        _hasNextStage = data.Stages.ContainsKey((_stage + 1).ToString(CultureInfo.InvariantCulture));

        // 시도 하나를 연다 (#72 · 설계 §4.4) — 번호가 오르고 시드가 새로 나와 재시도마다 순서가 대개 달라진다. 명부와 고르기는
        // data/stages.json 이 정하고, 고르기는 그때까지의 기록으로 한 번 세운다(데모와 같은 자리 — StageRoster.Setup).
        RunHistory history = Game.Instance.History;
        _attempt = history.Open();
        if (StageRoster.Setup(data.Stages, _stage, _attempt.Seed, history.Records) is not { } stage)
        {
            _broken = true; // [E] 는 StageRoster 가 남겼다
            return;
        }

        Log.Info("run", $"attempt={_attempt.Number} stage={_stage} seed={_attempt.Seed} picker={stage.PickerId} history={history.Records.Count}");

        _sim = new BattleSim(new BattleSetup
        {
            Arena = new Arena(battle.ArenaWidth),
            Fighter = _fighterConfig,
            HitShapes = data.Shapes,
            Boss = _bossConfig,
            PatternIds = stage.PatternIds,
            Patterns = data.Patterns,
            Seed = _attempt.Seed,
            Picker = stage.Picker,
            MaxTicks = battle.MaxTicks,
        });

        _cues = new BattleCues(_sim, _fighterView, _bossView, ShakeFor, StartHitstop);

        // 칼질마다의 시트(시작하는 장 · 칼이 나가는 장 · 속도)를 건넨다 (이슈 #54 · #59).
        _fighterView.Load(
            _fighterConfig.Sprite,
            Swings(_fighterConfig),
            new SwingSheet(_fighterConfig.ParryAnim, _fighterConfig.ParryAnimFps, 0, 0),
            _fighterConfig.ParryAnimFrames);
        _bossView.Load(_bossConfig.Sprite);

        if (GetTree().DebugCollisionsHint)
        {
            _hitboxDebug = new HitboxDebug();
            _world.AddChild(_hitboxDebug);
        }

        Log.Info("scene", $"battle ready stage={_stage} fighter={battle.Fighter} patterns={stage.PatternIds.Count}");
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

        // 히트스톱. 시계를 늘이지 않고 **세운다** — 이 프레임엔 시뮬레이션이 한 틱도 안 간다. 그 사이 누른 엣지는
        // 버리지 않고 모아 끝난 첫 틱에 넘긴다(#71) — 게임은 멈췄어도 손은 안 멈췄다.
        if (_hitstopLeft > 0)
        {
            _carried = InputFrame.Carry(_carried, Read());
            _hitstopLeft--;
            if (_hitstopLeft == 0)
            {
                Freeze(false);
            }

            return;
        }

        InputFrame input = InputFrame.Carry(_carried, Read());

        // 넘긴 **엣지**가 있을 때만 적는다. _carried 에는 레벨(이동 · 가드)도 모이는데 넘기는 것은 엣지뿐이다 — 통째로 default 와 견주면
        // 방향이나 ↓ 를 붙든 채 멈춤을 지난 것만으로 아무것도 안 넘긴 hitstop_carry 가 찍힌다.
        if (_carried.Jump || _carried.Dash || _carried.Parry || _carried.Attack)
        {
            Log.Debug("battle", $"hitstop_carry jump={_carried.Jump} dash={_carried.Dash} parry={_carried.Parry} attack={_carried.Attack} tick={_sim.Ticks + 1}");
        }

        _carried = default;

        BattleOutcome? outcome = _sim.Tick(input);
        _cues.Observe(input);

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
        // 이동과 가드만 레벨이다 — 누르고 있으면 계속 가야 한다.
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
        // 가드만 레벨이다(↓ 를 누르고 있는 동안 · 설계 §5.2). 패리는 누르는 것 한 번이다(0.333초 커밋 · 설계 §5.3).
        return new InputFrame(
            move,
            Input.IsActionJustPressed("jump"),
            Input.IsActionJustPressed("dash"),
            Input.IsActionJustPressed("parry"),
            Input.IsActionJustPressed("attack"),
            GuardHeld: Input.IsActionPressed("guard"));
    }

    /// <summary>
    /// 히트스톱을 건다 — 보스가 무너지는 틱에 <see cref="BattleCues"/> 가 부른다. 시계를 늘이지 않고 <b>세운다</b>
    /// (<see cref="_hitstopLeft"/>).
    /// </summary>
    private void StartHitstop()
    {
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

        // 끝까지 간 시도만 기록에 붙는다 — 이긴 판도(1단계를 이긴 판이 곧 1단계 기록이다 · 설계 §4.4). 판마다 한 번이고,
        // 관측이 살아 있는 마지막 자리가 여기다.
        Game.Instance.History.Record(new AttemptRecord(_attempt.Number, _stage, _attempt.Seed, outcome, [.. _sim.Events]));
        Log.Debug("run", $"recorded attempt={_attempt.Number} outcome={outcome} events={_sim.Events.Count}");
    }

    /// <summary>
    /// 결과 화면을 띄운다. 이겼으면 여기서 단계가 오른다 — 그래야 [다음 단계] 가 정말 다음을 연다.
    ///
    /// <para>
    /// 단계 진행만 남기고 캐릭터 3택 · 스탯 강화는 만들지 않는다(이슈 #22). 단계는 성장 루프가 아니라
    /// 보스 설계의 축이다 — 보스는 두 단계이고(#72 · 설계 §4) 2단계를 이기면 클리어다. 다음 단계가
    /// <c>stages.json</c> 에 없으면 클리어라, 단계 수를 여기 적지 않는다.
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
                ? $"{_stage}단계 돌파 — 다음은 {_stage + 1}단계"
                : $"{_stage}단계 · 보스 체력 {_sim.Boss.Health}/{_bossConfig.MaxHealth} 남음";

        // 이긴 판에서 [다시] 는 거짓말이다 — 단계가 이미 올랐으므로 같은 판이 아니다.
        string againLabel = cleared ? "처음부터" : won ? "다음 단계" : "다시";

        _result.Reveal(won, headline, detail, againLabel);
    }

    private void OnAgain()
    {
        // 클리어했으면 판을 처음으로 되돌린다 — 안 그러면 없는 3단계를 달라고 하게 된다.
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
            _sim.Fighter.Exhausted,
            // 남은 스태미나를 **비율로** 넘긴다 (이슈 #47) — 최대값의 사본을 뷰에 두면
            // fighters.json 이 움직이는 순간 가드 링이 거짓말을 한다.
            _fighterConfig.MaxStamina <= 0 ? 0 : _sim.Fighter.Stamina / _fighterConfig.MaxStamina,
            _sim.Fighter.Stiff));

        _bossView.Show(new BossFrame(
            _sim.Boss.X,
            _sim.Boss.Y,
            _sim.Boss.Facing,
            Phase(),
            _sim.NextActiveIn,
            _sim.Boss.Exhausted,
            _sim.BossStep?.Anim,
            _sim.BossStep?.Frame));

        _hud.Show(new HudFrame(
            _sim.Fighter.Health,
            _fighterConfig.MaxHealth,
            _sim.Fighter.Stamina,
            _fighterConfig.MaxStamina,
            _sim.Fighter.Exhausted,
            _sim.Boss.Health,
            _bossConfig.MaxHealth,
            _sim.Poise.Max <= 0 ? 0 : _sim.Poise.Value / _sim.Poise.Max,
            _sim.Boss.ExhaustLeft));

        _hitboxDebug?.Show(
            _sim.BossTestedRects,
            _sim.BossNextRects,
            _sim.FighterTestedRects,
            _sim.Fighter.Body,
            _sim.Boss.Body,
            HitboxDebug.FighterColor(_sim.FighterDefense));
    }

    /// <summary>규칙의 행동 → 뷰의 자세. 이 변환을 아는 것은 둘 다 아는 여기뿐이다.</summary>
    private FighterPose Pose()
    {
        if (_over && _outcome == BattleOutcome.Lose)
        {
            return FighterPose.Death;
        }

        // 탈진은 행동보다 먼저다(#71) — 탈진한 파이터는 Idle 이지만 서 있는 것이 아니라 굳어 있다.
        if (_sim.Fighter.Exhausted)
        {
            return FighterPose.Exhausted;
        }

        return _sim.Fighter.Action switch
        {
            FighterAction.Dash => FighterPose.Dash,
            FighterAction.Attack => FighterPose.Attack,
            FighterAction.Parry => FighterPose.Parry,
            FighterAction.Guard => FighterPose.Guard,
            _ => _cues.Walking ? FighterPose.Run : FighterPose.Idle,
        };
    }

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

    /// <summary>규칙의 칼질 칸 → 뷰의 시트. 뷰가 fighters.json 을 직접 안 읽게 여기서 옮겨 준다.</summary>
    private static SwingSheet[] Swings(FighterConfig fighter)
    {
        var sheets = new SwingSheet[fighter.Combo.Count];
        for (int i = 0; i < sheets.Length; i++)
        {
            ComboStepDef s = fighter.Combo[i];
            sheets[i] = new SwingSheet(s.Anim, s.Fps, s.StartFrame, s.BladeFrame);
        }

        return sheets;
    }
}
