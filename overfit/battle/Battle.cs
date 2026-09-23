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

    /// <summary>패턴 표. 뷰가 <b>태그</b>(지금은 parryable)를 그리는 데만 쓴다 — 규칙은 시뮬레이션이 본다.</summary>
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
    /// </summary>
    private int _hitstopLeft;

    private double _shakeLeft;
    private double _shakeAmp;

    // 틱 전후를 견줘 사건을 찾는다. 규칙 층에 뷰용 콜백을 달지 않기 위한 값들이다.
    private int _lastFighterHealth;
    private int _lastBossHealth;
    private int _lastEventCount;
    private bool _lastAttackActive;
    private bool _walking;

    /// <summary>지난 틱의 차지 단계. 늘어난 순간이 "단계가 올랐다" 는 사건이다 — 규칙 층에 콜백을 안 달고 여기서 견준다.</summary>
    private int _lastChargeTier;

    /// <summary>이 판에서 가드가 깨진 횟수 (이슈 #47). <b>스크린샷이 그 순간을 노리는 데만 쓴다.</b></summary>
    private int _guardBreaks;

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
    /// 지금 가드 자세인가 (이슈 #47). 위와 같이 <b>디버그 전용 읽기</b>다 — 가드는 누름에서
    /// 0.30초 뒤에 서므로 프레임을 세서 노리면 parry_duration 을 고치는 순간 조용히 어긋난다.
    /// </summary>
    public bool FighterGuarding => !_broken && !_over && _sim.Fighter.Guarding;

    /// <summary>
    /// 지금까지 가드가 깨진 횟수. 위와 같이 디버그 전용 읽기다 — 붕괴는 <b>사건</b>이라 상태로는
    /// 못 본다(0.9초 고정은 부정확 패리의 고정과 같은 모양이라 구별이 안 된다).
    /// 늘어난 그 순간이 셔터를 누를 때다.
    /// </summary>
    public int FighterGuardBreaks => _guardBreaks;

    /// <summary>
    /// 지금 도는 패턴에 <b>가드 불가</b> 판정이 있나 (<c>has_guard_break</c>). 위와 같이 디버그 전용 읽기다 —
    /// 危 예고가 화면에서 구별되는지를 증명하려면 그 패턴의 선딜을 기다려야 한다.
    /// </summary>
    public bool BossGuardBreak => !_broken && !_over && Current()?.Tags.HasGuardBreak == true;

    /// <summary>
    /// 보스의 남은 체력. 위와 같이 디버그 전용 읽기다 — 줄어든 직후가 <b>흰 피격 실루엣</b>이 뜨는
    /// 순간이고(이슈 #28), 그건 0.2초뿐이라 벽시계로 노리면 대부분 놓친다.
    /// </summary>
    public int BossHealth => _broken ? 0 : _sim.Boss.Health;

    /// <summary>
    /// 지금 도는 패턴이 <b>패리 불가</b>인가. 위와 같이 디버그 전용 읽기다 —
    /// 크림슨 예고가 화면에서 구별되는지를 스크린샷으로 증명하려면 그 순간을 기다려야 한다.
    /// </summary>
    public bool BossUnparryable => !_broken && !_over && !CurrentParryable();

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

        // 차지 자세는 attack 시트의 **선딜 마지막 장**이다 — 칼이 나가는 프레임(blade) 바로 앞.
        // 뷰가 fighters.json 을 직접 읽지 않게 여기서 건네준다.
        _fighterView.Load(_fighterConfig.Sprite, _fighterConfig.AttackAnimBladeFrame - 1);
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
                // 정확과 부정확은 **다른 피드백**이어야 한다. 히트스톱은 정확에만 준다 —
                // 시간을 세우는 것은 "완전히 받아냈다" 의 표현이고, 절반 흘린 것에 주면 거짓말이다.
                switch (_sim.Events[i].Verdict)
                {
                    case HitVerdict.Parried:
                        ParryLanded();
                        break;

                    case HitVerdict.ParriedLate:
                        _fighterView.ParryImprecise();
                        break;

                    // 버텨낸 것과 깨진 것은 **다른 연출**이어야 한다 (이슈 #47). 같으면 화면은
                    // "막았다" 만 말하고 "무너졌다" 는 안 말하는데, 그 뒤 0.9초는 아무것도 못 한다.
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

        if (_sim.Fighter.Health < _lastFighterHealth)
        {
            _fighterView.Hit();
            ShakeFor(1.0);
        }

        if (_sim.Boss.Health < _lastBossHealth)
        {
            _bossView.Hit();
        }

        // 판정이 서는 **그 틱**에만 한 번. 계속 참인 동안 매 프레임 섬광을 내면 번쩍임이 아니라 조명이 된다.
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
    /// 패리가 받아냈다. 섬광 + 스파크 + 히트스톱. <b>실패에는 아무것도 없다</b> —
    /// 없음이 곧 피드백이라, 실패용 연출을 만들면 "막았는지" 가 오히려 흐려진다.
    /// </summary>
    private void ParryLanded()
    {
        _fighterView.ParrySuccess();
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
            _sim.Fighter.Parrying,
            _sim.Fighter.PreciseParryWindow <= 0
                ? 0
                : _sim.Fighter.SinceParryPress / _sim.Fighter.PreciseParryWindow,
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
            CurrentParryable(),
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
            FighterAction.Parry => FighterPose.Parry,
            FighterAction.Attack => FighterPose.Attack,
            FighterAction.Charge => FighterPose.Charge,
            FighterAction.Guard => FighterPose.Guard,
            _ => _walking ? FighterPose.Run : FighterPose.Idle,
        };
    }

    /// <summary>
    /// 지금 도는 패턴을 받아칠 수 있나 (이슈 #27 · 패리 불가는 크림슨으로 예고한다).
    /// 패턴이 안 도는 중이면 <b>받아칠 수 있는 쪽</b>으로 둔다 — 쉬는 보스를 붉게 칠하면
    /// "지금 뭔가 온다" 는 거짓말이 된다.
    /// </summary>
    private bool CurrentParryable() =>
        _sim.Boss.CurrentPattern is not string id
        || !_patterns.TryGetValue(id, out PatternDef? def)
        || def.Tags.Parryable;

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

        // 가드 불가는 **태그**에서 온다 (이슈 #47). 선딜에는 아직 어느 판정이 올지가 아니라
        // "무엇이 오는가" 만 정해져 있으므로, 타임라인이 아니라 그 요약을 읽는다.
        return new BossTell(
            def.Tell.Id, def.Tell.X * _sim.Boss.Facing, def.Tell.Y, def.Tell.Length, def.Tags.HasGuardBreak);
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
