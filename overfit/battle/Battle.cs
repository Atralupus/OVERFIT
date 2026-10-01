using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// 움직임마다의 그림 배속 (#78 · 설계 §4.6) — 단계가 단 움직임의 id → <c>feel</c> 의 값. 없는 움직임은 제 속도다(도약). 움직임의 등록표는
    /// 규칙의 것(<see cref="BossMotions"/>)이고 배속은 그림의 것이라 여기 따로 둔다 — 규칙은 배속을 모른다. 움직임이 늘면 줄 하나를 더한다
    /// (CLAUDE.md §2 — 늘어나는 곳에 갈래를 안 둔다).
    /// </summary>
    private static readonly Dictionary<string, Func<FeelBalance, double>> _motionAnimSpeed = new(StringComparer.Ordinal)
    {
        ["rush"] = feel => feel.RushAnimSpeed,
    };

    /// <summary>
    /// 계획의 달리기(설계 2026-09-29 조각1 §5.4) 동안 보스가 도는 그림 — 돌진의 첫 단계와 같은 <c>run</c> 이다(<c>patterns.json</c> 의 돌진). 배속은
    /// <c>feel.run_anim_speed</c> 다(돌진보다 느린 달리기). 규칙은 그림을 모른다 — 달리는 중인지(<see cref="BattleSim.BossRunning"/>)만 말한다.
    /// </summary>
    private const string _runAnim = "run";

    /// <summary>
    /// 폭탄에 반응해 끊은 뒤의 멈칫(설계 2026-09-30 조각2 §2.3 · §5) 동안 보스가 서는 그림 — idle 의 첫 장에 <b>세운다</b>. 쉬는 보스는 idle 을 제 속도로
    /// 돌아(숨 쉰다) 굳어 선 장이 "멈칫" 으로 갈린다. 규칙은 그림을 모른다 — 멈칫 중인지(<see cref="BattleSim.BossHesitating"/>)만 말한다.
    /// </summary>
    private const string _hesitateAnim = "idle";

    private BattleSim _sim = null!;
    private FighterView _fighterView = null!;
    private BossView _bossView = null!;

    /// <summary>착지의 흰 충격파 (#83). 세우는 것은 <see cref="BattleCues"/> 다 — 여기는 씬에서 찾아 넘기기만 한다.</summary>
    private LandingWave _landingWave = null!;

    private BattleHud _hud = null!;
    private BattleResult _result = null!;

    /// <summary>잡기의 흰 구 (#78 · 설계 §4.7) — World 안에 세운다(두 몸과 같이 흔들린다). 규칙의 단계와 잡힘을 받아 그리기만 한다.</summary>
    private GrabOrb _grabOrb = null!;

    /// <summary>폭탄 (설계 2026-09-30 조각2 §5) — 손 위의 폭탄 · 나는 폭탄 · 불꽃 · 연기. 흰 구와 같이 World 안에 세운다.</summary>
    private BombView _bombView = null!;

    /// <summary>보스의 알아챔 표시 "!" (설계 2026-09-30 조각2 §2.1 · §5) — 규칙이 "안다" 고 말하는 동안 보스 머리 위에 뜬다. 폭탄과 같이 World 안에 세운다.</summary>
    private AlertMark _alertMark = null!;
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

    /// <summary>
    /// 싸우는 단계 — <c>stages.json</c> 의 유일한 키다(보스전은 하나 · 설계 2026-09-29 조각1 §1). 로그의 <c>stage=</c> 와 시도 기록이 싣는다. 이기면
    /// 곧 클리어다 — 오를 다음 단계가 없다.
    /// </summary>
    private const int _stage = 1;

    /// <summary>이 전투의 시도 — 번호와 시드 (#72 · 설계 §4.4). 설 때 열고, 끝나면 그 판의 관측과 함께 기록에 붙인다.</summary>
    private (int Number, ulong Seed) _attempt;

    /// <summary>
    /// 이 전투를 세운 명부와 고르기 (#112) — 끝날 때 기록이 잘라 쓴 단계 · 고르기 id 를 여기서 읽는다(설계 2026-09-28 §6.5).
    /// </summary>
    private StageSetup _setup = null!;

    /// <summary>결과 화면의 리포트 줄들 (#122 · 설계 2026-09-29 조각1 §4.5) — 끝날 때 짓고 결과 화면이 띄운다.</summary>
    private IReadOnlyList<string> _report = [];

    /// <summary>이 판의 보스 시작 체력 — 대본으로 선 판만 있다(설계 2026-10-01 조각1 §2.5). 시도 기록이 싣는다.</summary>
    private int? _bossStartHealth;

    /// <summary>판을 사례로 가른다 — 공장과 같은 정의(<see cref="InstanceTracker"/> · #114). 틱마다 보고, 끝날 때 사례와 라벨을 기록이 싣는다.</summary>
    private InstanceTracker _instances = new();

    /// <summary>
    /// 판의 입력 (설계 2026-09-29 조각1 §4.3) — <see cref="BattleSim.Tick"/> 에 넘긴 그대로 틱마다 모은다. 끝날 때 기록이 싣고, 되살리기가 봇 대신
    /// 틱마다 다시 넣어 판 전체를 세운다(<see cref="Replay"/>).
    /// </summary>
    private readonly InputTape _tape = new();

    /// <summary>이 판을 세운 데이터의 지문(<see cref="BattleTables.DataSha256"/>) — 기록이 싣고, 되살린 판이 다를 때 까닭을 가른다.</summary>
    private string _dataSha256 = "";

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
    /// 지금까지 민 규칙의 틱 (#78). 위와 같이 디버그 전용 읽기다 — GIF 러너가 겨냥한 패턴의 틱을 세고(패턴이 선 틱에서) 그 틱에 누른다. 벽시계로
    /// 기다리면 히트스톱과 프레임의 흔들림이 박자를 민다(설계 §6.2 — "규칙의 틱을 보고").
    /// </summary>
    public int SimTicks => _broken ? 0 : _sim.Ticks;

    /// <summary>파이터가 붙들렸나 (#78 · 설계 §4.7). 위와 같이 디버그 전용 읽기다 — 흰 구가 붙든 장은 규칙에게 물어 찍는다.</summary>
    public bool FighterHeld => !_broken && !_over && _sim.Fighter.Held;

    /// <summary>
    /// 보스가 지금 든 단계의 그림 이름 (#78). 위와 같이 디버그 전용 읽기다 — 돌진 중(<c>run</c>)을 찍으려면 그 단계에 든 것을 규칙에게 물어야 한다.
    /// 돌진이 서는 시각은 1타 뒤 1.30초지만 끝나는 시각은 파이터 자리에 달려 있다.
    /// </summary>
    public string? BossStepAnim => _broken || _over ? null : _sim.BossStep?.Anim;

    /// <summary>
    /// 보스가 계획의 달리기로 달리는 중인가 (설계 2026-09-29 조각1 §5.2). 위와 같이 디버그 전용 읽기다 — 달리기 중(<c>battle-13e-run</c>)을 찍으려면
    /// 달리기에 든 것을 규칙에게 물어야 한다. 동작 밖이라 단계(<see cref="BossStepAnim"/>)가 없다.
    /// </summary>
    public bool BossRunning => !_broken && !_over && _sim.BossRunning;

    /// <summary>
    /// 파이터가 폭탄의 선딜 중인가 (설계 2026-09-30 조각2 §5). 위와 같이 디버그 전용 읽기다 — 손 위의 폭탄 장(<c>battle-14-bomb-windup</c>)을 찍으려면
    /// 던지기에 든 것을 규칙에게 물어야 한다.
    /// </summary>
    public bool FighterThrowing => !_broken && !_over && _sim.Fighter.Throwing;

    /// <summary>나는 폭탄 수 (설계 2026-09-30 조각2 §5). 위와 같이 디버그 전용 읽기다 — 나는 폭탄 장을 찍는다.</summary>
    public int BombsInFlight => _broken ? 0 : _sim.BombsInFlight.Count;

    /// <summary>보스에게 떨어진 폭탄 수 (설계 2026-09-30 조각2 §5). 위와 같이 디버그 전용 읽기다 — 터지는 불꽃 장을 찍는다.</summary>
    public int BombsLanded => _broken ? 0 : _sim.BombsLanded;

    /// <summary>보스가 던지기를 아나 (설계 2026-09-30 조각2 §2.1). 위와 같이 디버그 전용 읽기다 — 알아챔 표시 "!" 장을 찍는다.</summary>
    public bool BossAlert => !_broken && !_over && _sim.BossAlert;

    /// <summary>보스가 끊고 멈칫하는 중인가 (§2.3). 위와 같이 디버그 전용 읽기다 — 멈칫 장을 찍는다.</summary>
    public bool BossHesitating => !_broken && !_over && _sim.BossHesitating;

    /// <summary>
    /// 파이터가 새 행동을 받나 — 칼질 · 대시 · 패리(행동 뒤 경직까지 · #82) 중이 아니고 굳어 있지도(탈진 · 붙들림 — <c>Fighter.Locked</c>) 않다.
    /// 위와 같이 디버그 전용 읽기다 — 스크린샷이 칼질을 다시 누를 때를 규칙에게 묻는다. 벽시계 간격(0.4초)으로 누르던 때, 칼질 뒤 경직이 들자
    /// 둘째 J 가 1타의 경직에 떨어져 2타가 됐다. 굳음은 탈진만 보던 것을 <c>Locked</c> 로 넓혔다(#96) — 붙들린 파이터도 선 자세(Idle)라, 탈진만
    /// 보면 흰 구에 잡힌 동안을 "받는다" 고 했다. 누른 것은 버려지고 스크린샷 대본은 그 누름이 먹기를 헛되이 기다린다.
    /// </summary>
    public bool FighterFree =>
        !_broken && !_over && _sim.Fighter.Action == FighterAction.Idle && !_sim.Fighter.Locked;

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
    /// 보스 중심과 파이터 중심 사이의 거리(px). 디버그 전용 읽기다 — 스크린샷이 "보스에게 붙었나" 를 시간이 아니라 거리로 잰다(보스는 쉬는 동안
    /// 제자리라 걸어와 주지 않는다 · 설계 2026-09-29 조각1 §5.1).
    /// </summary>
    public double BossGap => _broken ? 0 : Math.Abs(_sim.Boss.X - _sim.Fighter.X);

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
        _landingWave = GetNode<LandingWave>("%LandingWave");
        _hud = GetNode<BattleHud>("%Hud");
        _result = GetNode<BattleResult>("%Result");
        _world = GetNode<Node2D>("World");
        _worldHome = _world.Position;
        _feel = Balance.Data.Feel;
        _result.Bind(OnAgain, OnTitle);

        // 데이터 다섯은 데모와 같은 자리에서 읽는다(BattleTables). 판정 모양도 데이터다 (이슈 #59) — 규칙은 파일을 모른다.
        BattleTables data = BattleTables.Load();
        _dataSha256 = data.DataSha256;

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
        _hud.SetFormMarks(boss.Forms.Thresholds, boss.MaxHealth);

        // 시도 하나를 연다 (#72 · 설계 §4.4) — 번호가 오르고 시드가 새로 나와 재시도마다 순서가 대개 달라진다. 명부와 고르기는
        // data/stages.json 이 정하고, 고르기는 그때까지의 기록으로 한 번 세운다(데모와 같은 자리 — StageRoster.Setup). 대본 칸이 차
        // 있으면(GIF · 스크린샷 · #78) 이 전투만 그 대본으로 선다 — 가져가며 비우므로 다음 전투는 단계의 고르기로 돌아간다.
        RunHistory history = Game.Instance.History;
        _attempt = history.Open();
        if (StageRoster.Setup(
                data.Stages, _stage, _attempt.Seed, history.Records, data.Patterns, BattleSim.RestTicks(_bossConfig), Balance.Data.Picker,
                Game.Instance.TakeScript()) is not { } stage)
        {
            _broken = true; // [E] 는 StageRoster 가 남겼다
            return;
        }

        _setup = stage;
        _bossStartHealth = Game.Instance.TakeBossStartHealth();
        _instances = new InstanceTracker();
        Log.Info("run", $"attempt={_attempt.Number} stage={stage.Stage} seed={_attempt.Seed} picker={stage.PickerId} history={history.Records.Count}");

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
            BossStartHealth = _bossStartHealth,
        });

        _grabOrb = new GrabOrb();
        _world.AddChild(_grabOrb);
        _bombView = new BombView();
        _world.AddChild(_bombView);
        _alertMark = new AlertMark();
        _world.AddChild(_alertMark);

        // 바닥 충격파는 판정이 바닥 전체를 덮었는지를 아레나 폭으로 잰다(#83) — 판을 세운 바로 그 폭이다.
        _cues = new BattleCues(_sim, _fighterView, _bossView, _landingWave, _bombView, battle.ArenaWidth, ShakeFor, StartHitstop);

        // 칼질마다의 시트(시작하는 장 · 칼이 나가는 장 · 속도)를 건넨다 (이슈 #54 · #59). 가드 · 던지기가 멈춰 서는 장도 데이터다(#96 · 조각2 §5).
        BombDef bomb = _fighterConfig.Bomb;
        _fighterView.Load(
            _fighterConfig.Sprite,
            Swings(_fighterConfig),
            new SwingSheet(_fighterConfig.ParryAnim, _fighterConfig.ParryAnimFps, 0, 0),
            _fighterConfig.ParryAnimFrames,
            new StillFrame(_fighterConfig.GuardAnim, _fighterConfig.GuardFrame),
            new StillFrame(bomb.Anim, bomb.WindupFrame),
            new StillFrame(bomb.Anim, bomb.ReleaseFrame));
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

        // 규칙이 받은 그대로 모은다 — 히트스톱 동안 모은 엣지를 실은 **뒤**다(§4.3). 키에서 읽은 값을 모으면 되살린 판에서 넘긴 J 가 사라진다.
        _tape.Add(input);
        BattleOutcome? outcome = _sim.Tick(input);
        _instances.Observe(_sim.Boss.CurrentPattern, _sim.Events.Count);
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
        // 가드만 레벨이다(↓ 를 누르고 있는 동안 · 설계 §5.2). 패리는 누르는 것 한 번이다(0.333초 커밋 · 설계 §5.3). 폭탄도 엣지다(L · 조각2 §1.1).
        return new InputFrame(
            move,
            Input.IsActionJustPressed("jump"),
            Input.IsActionJustPressed("dash"),
            Input.IsActionJustPressed("parry"),
            Input.IsActionJustPressed("attack"),
            GuardHeld: Input.IsActionPressed("guard"),
            Bomb: Input.IsActionJustPressed("bomb"));
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
        //
        // 단계는 실제로 싸운 단계(잘라 쓴 값)다 — 요청한 값이 아니다(#59 3/6 · 설계 2026-09-28 §6.5). 디스크에도 한 줄을 덧붙인다 — 되살리기의
        // 재료다(AttemptFile).
        RunHistory history = Game.Instance.History;
        var record = new AttemptRecord(_attempt.Number, _setup.Stage, _attempt.Seed, outcome, [.. _sim.Events]);
        history.Record(record);
        Log.Debug("run", $"recorded attempt={_attempt.Number} outcome={outcome} events={_sim.Events.Count}");
        List<PatternInstance> instances = _instances.Finish(_sim.Events);
        var entry = new AttemptEntry(
            history.SessionSeed, history.Run, record, _setup.PickerId, _sim.PlanEntries, _sim.Ticks, instances, [.. _tape.Runs], _dataSha256,
            [.. _sim.BombRecords], [.. _sim.Forms.Shifts], _bossStartHealth);
        if (AttemptFile.Append(entry) is { } path)
        {
            Log.Debug("run", $"logged attempt={_attempt.Number} instances={instances.Count} plans={entry.Plans.Count} inputs={_tape.Runs.Count}"
                + $" bombs={_sim.BombRecords.Count} path={path}");
        }

        // 리포트 (#122 · 설계 2026-09-29 조각1 §4.5) — 모든 판의 결과 화면에 선다. 계획 수 · 캔슬 수가 머리이고 끊은 짝마다 한 줄이다.
        // 옛 망이 걷혀 확률 줄이 없다. 폭탄은 회피 다음 한 줄이다(설계 2026-09-30 조각2 §4).
        // 페이즈 줄(설계 2026-10-01 조각1 §3)은 머리 바로 뒤다 — 판이 어디까지 갔는지가 회피 · 폭탄보다 먼저 읽힌다.
        List<string> report = [.. PickReport.Lines(
            _setup.PickerId, _setup.PatternIds, _sim.Plans.Count, _sim.Cancels, _sim.Events, _sim.BombRecords, _sim.Drawn, instances)];
        string forms = FormReport.Line(_sim.Forms.Shifts, _sim.Ticks, _sim.Forms.Count);
        if (forms.Length > 0)
        {
            report.Insert(Math.Min(1, report.Count), forms);
        }

        _report = report;
        foreach (string line in _report)
        {
            Log.Debug("report", $"line=\"{line}\"");
        }
    }

    /// <summary>
    /// 결과 화면을 띄운다. 보스전이 하나라(설계 2026-09-29 조각1 §1) 이기면 곧 클리어다 — [처음부터] 가 기록을 비우고 새 런을 연다. 지면
    /// [다시] 가 같은 런의 다음 시도를 연다(새 시드 · 설계 §4.4).
    /// </summary>
    private void Reveal()
    {
        _resultShown = true;
        bool won = _outcome == BattleOutcome.Win;
        string headline = won ? "클리어" : "패배";
        string detail = won ? "보스를 쓰러뜨렸다" : $"보스 체력 {_sim.Boss.Health}/{_bossConfig.MaxHealth} 남음";
        string againLabel = won ? "처음부터" : "다시";

        _result.Reveal(won, headline, detail, againLabel, string.Join('\n', _report));
    }

    private void OnAgain()
    {
        // 클리어했으면 판을 처음으로 되돌린다 — 새 런이다.
        if (_outcome == BattleOutcome.Win)
        {
            Game.Instance.ResetRun();
        }

        Log.Info("scene", "battle action=again");
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
            // 붙들린 동안은 탈진이 겹쳐도 탈진 색을 안 칠한다 (#78 · 설계 §4.7) — 흰 구가 감싸고, 흰 구가 흩어진 뒤 남은 탈진이 탈진 색으로 넘어간다.
            _sim.Fighter.Exhausted && !_sim.Fighter.Held,
            // 남은 스태미나를 **비율로** 넘긴다 (이슈 #47) — 최대값의 사본을 뷰에 두면
            // fighters.json 이 움직이는 순간 가드 링이 거짓말을 한다.
            _fighterConfig.MaxStamina <= 0 ? 0 : _sim.Fighter.Stamina / _fighterConfig.MaxStamina,
            _sim.Fighter.Stiff));

        // 계획의 달리기는 동작 밖이라 단계가 없다 — 달리는 동안은 run 을 제 배속으로 돈다(설계 2026-09-29 조각1 §5.4). 폭탄에 반응한 멈칫도 동작
        // 밖이다 — idle 첫 장에 세운다(설계 2026-09-30 조각2 §5 · _hesitateAnim).
        bool running = _sim.BossRunning;
        bool hesitating = _sim.BossHesitating;
        _bossView.Show(new BossFrame(
            _sim.Boss.X,
            _sim.Boss.Y,
            _sim.Boss.Facing,
            Phase(),
            _sim.Boss.Exhausted,
            running ? _runAnim : hesitating ? _hesitateAnim : _sim.BossStep?.Anim,
            running ? null : hesitating ? 0 : _sim.BossStep?.Frame,
            // 돌진의 run 만 빠르다 (#78 · 설계 §4.6) — 단계가 단 움직임의 배속이다(_motionAnimSpeed). 규칙은 이 배속을 모른다.
            running ? _feel.RunAnimSpeed
                : _sim.BossStep?.Motion is { } motion && _motionAnimSpeed.TryGetValue(motion.Id, out Func<FeelBalance, double>? speed)
                    ? speed(_feel)
                    : 1.0,
            _sim.BossStep?.Mirror ?? false,
            _sim.Forms.Shifting ? _sim.Forms.ShiftLeft : 0));

        // 판이 끝나면 흰 구가 그릴 까닭이 없다 — 끝난 판은 틱을 안 밀어 규칙의 값(날 자리 · 붙들림 · 산 창)이 그 틱에 멈춰 남는다. 거르지 않으면
        // 흰 구가 나는 동안 이긴 판에서 흰 구가 두 몸 사이에 멈춘 채 결과 화면까지 떠 있다. 잡기에 죽은 판도 같다: 판은 그 잡기가 닿은 틱에 끝나고
        // (붙들림 60틱을 안 기다린다 · MoveBattleTests) 파이터는 규칙에서 붙들린 채라, 안 거르면 흰 구가 죽는 모션 위에 결과 화면까지 감싸 있다.
        // 셋(날기 · 붙듦 · 기다림)을 다 거른다 — 하나라도 남으면 그 상태로 멈춘다. 거르면 그 자리에서 흩어진다(GrabOrb 의 흩어짐 · 끝난 판도
        // 그리기는 계속 부른다).
        bool live = !_over;
        (HitBox Hit, double Progress)? ahead = live ? _sim.BossHitAhead : null;
        _grabOrb.Show(new OrbFrame(
            ahead is { Hit.GrabHoldSeconds: > 0 },
            ahead?.Progress ?? 0,
            live && _sim.Fighter.Held,
            live && _sim.GrabLive,
            _sim.Boss.X,
            _sim.Boss.Y,
            _bossConfig.Height,
            _sim.Fighter.X,
            _sim.Fighter.Y));

        // 폭탄 — 끝난 판은 손 위의 폭탄을 안 그린다(흰 구와 같은 까닭 · 죽는 모션 위에 폭탄이 떠 있다). 나는 폭탄은 규칙이 판 끝에 버렸다.
        _bombView.Show(new BombFrame(
            live && _sim.Fighter.Throwing,
            _sim.Fighter.X,
            _sim.Fighter.Y,
            _sim.Fighter.Facing,
            [.. _sim.BombsInFlight.Select(b => new BombArc(b.FromX, b.FromY, b.Progress))],
            _sim.Boss.X,
            _sim.Boss.Y,
            _bossConfig.Height));

        // 알아챔 — 끝난 판은 안 띄운다(손 위의 폭탄과 같은 까닭 · 끝난 판은 틱을 안 밀어 규칙의 값이 그 틱에 멈춰 남는다).
        _alertMark.Show(live && _sim.BossAlert, _sim.Boss.X, _sim.Boss.Y, _bossConfig.Height);

        _hud.Show(new HudFrame(
            _sim.Fighter.Health,
            _fighterConfig.MaxHealth,
            _sim.Fighter.Stamina,
            _fighterConfig.MaxStamina,
            _sim.Fighter.Exhausted,
            _sim.Boss.Health,
            _bossConfig.MaxHealth,
            _sim.Poise.Max <= 0 ? 0 : _sim.Poise.Value / _sim.Poise.Max,
            _sim.Boss.ExhaustLeft,
            _sim.Fighter.BombsLeft));

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

        // 붙들림과 탈진은 행동보다 먼저다(#71 · #78) — 둘 다 Idle 이지만 서 있는 것이 아니라 굳어 있다. 겹치면 붙들림이 먼저다(설계 §4.7).
        if (_sim.Fighter.Held)
        {
            return FighterPose.Held;
        }

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
            FighterAction.Throw => _sim.Fighter.Throwing ? FighterPose.ThrowWindup : FighterPose.ThrowRelease,
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
