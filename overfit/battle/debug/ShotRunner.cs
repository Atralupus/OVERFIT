using System;
using System.Threading.Tasks;
using Godot;
using Overfit.Battle.Rules;
using Overfit.Core;
using static Overfit.Battle.Debug.SceneDriver;

namespace Overfit.Battle.Debug;

/// <summary>
/// 창을 띄운 채 씬을 돌며 스크린샷을 찍는다. <c>tools/build.sh shots</c> 가 띄운다.
///
/// <para>
/// 전에는 벽시계로만 기다렸다 — 정해진 초에 셔터를 눌렀다. 그러면 <b>무엇이 찍힐지 아무도 모른다</b>:
/// 패턴 주기(간격 0.8초 + 3연격 3.25초 또는 점프 공격 1.5초)와 어긋나 실행할 때마다 다른 순간이 나오고,
/// 플레이어는 아무것도 안 하므로 대시·패리·공격은 한 번도 안 찍힌다.
/// </para>
///
/// <para>
/// 그래서 둘을 한다. ① <b>입력을 넣는다</b> — 사람이 누르는 것과 같은 액션을 합성해
/// <c>Battle</c> 의 <c>Read()</c> 가 그대로 읽게 한다. ② <b>상태를 보고 셔터를 누른다</b> —
/// 보스가 선딜에 들어갔을 때, 체력이 줄었을 때, 결과가 떴을 때.
/// 그래야 스크린샷이 "이 피드백이 보이는가" 를 실제로 증명한다.
/// </para>
///
/// <para>
/// 여기는 <b>대본</b>(어느 장면을 언제 찍나)만 든다. 손(새 판 · 누르기 · 프레임과 상태를 기다리기)은 <see cref="SceneDriver"/> 다 (#78).
/// </para>
/// </summary>
public partial class ShotRunner : Node
{
    /// <summary>한 판이 끝나기를 기다리는 상한(초). 넘으면 결과 화면 없이 넘어간다.</summary>
    private const double _battleTimeout = 90.0;

    /// <summary>상태를 기다릴 때의 기본 상한(초).</summary>
    private const double _pollTimeout = 8.0;

    /// <summary>
    /// 붙든 가드가 <b>깨지기를</b> 기다리는 상한(초) — <c>battle-10b-guard-break</c> 하나가 쓴다. 기본 상한보다 길다 — 가드는
    /// 3연격 두 바퀴에 걸쳐 깨지므로 한 주기가 아니라 여러 주기다(주기 = 간격 0.8 + 3연격 3.25초).
    /// 넘기면 경고만 남기고 그냥 찍는다 — 스크린샷이 못 찍힌 것은 게임의 규칙 위반이 아니다.
    /// 정해진 패턴 하나를 기다리는 것은 이것이 아니라 <see cref="_patternTimeout"/> 이다.
    /// </summary>
    private const double _guardBreakTimeout = 16.0;

    /// <summary>
    /// <b>정해진 패턴</b>의 한 순간을 기다리는 상한(초) (#72). 판은 전부 대본 판이다(<see cref="NewBattle"/> 에 패턴을 준 판 · #78) — 기다리는 패턴이
    /// 첫 뽑기(판의 48틱)부터 오거나 대본의 다음 칸으로 한 주기 뒤에 오므로, 이 상한은 울타리일 뿐이다(규칙이 깨져 그 순간이 영영 안 올 때 셔터가
    /// 30초 뒤에 그냥 누른다).
    ///
    /// <para>
    /// <b>대본 없는 판을 걷은 길.</b> 옛 1단계 판(보스 피격 · 방향 잠금)은 uniform 이라 한 주기(간격 0.8 + 3.25 또는 1.5초)마다 반반이었다. 30초면
    /// 예닐곱 번 뽑아 못 볼 확률이 1% 밑이지만, 세션 시드가 51 로 고정이라 그 1% 에 든 판은 <b>실행마다</b> 빗나간다 — 실제로 밟았다(공중 사진의 판은
    /// 3연격만 여덟 번 뽑아 첫 점프 공격이 1992틱이었다). 그래서 특정 패턴이 꼭 와야 하는 장을 대본 판으로 옮겼고(#78 — 그때 쓰던 "첫 패턴이 원하는
    /// 것인 판을 여덟 번까지 새로 세우는" 되풀이도 걷었다), 남은 판도 명부가 일곱이 된 뒤 대본(<see cref="_classic"/>)으로 옮겼다(설계 2026-09-29
    /// 조각1 §2) — 거기서도 밟았다(<see cref="_classic"/> 의 주석).
    /// </para>
    /// </summary>
    private const double _patternTimeout = 30.0;

    /// <summary>
    /// 붙은 자리 — 보스 중심에서 파이터 중심까지(px). 3연격의 세 칼이 다 닿고(3타는 80 ~ 404) 파이터의 칼도 보스 몸에 닿는다. 옛 판은 쉬는 동안
    /// 보스가 파이터 앞 115 까지 걸어와 이 자리를 만들어 주었다 — 쉬는 동안 제자리가 된 뒤로는(설계 2026-09-29 조각1 §5.1) 파이터가 걸어간다.
    /// </summary>
    private const double _near = 150;

    /// <summary>
    /// 대본 없이 찍던 판들의 대본 — 옛 1단계의 두 동작을 번갈아 돈다(설계 2026-09-29 조각1 §2). 이 파일의 장면들(보스 피격 · 선딜 · 결과 · 2연격 ·
    /// 경직 게이지 · 방향 잠금)은 그 명부에 맞춰 짰다: 3연격의 선딜에 칼을 넣고, 3연격은 보스가 선 자리에서 427px 까지만 친다. 명부가 일곱이 되자
    /// 무작위 판에서 3연격은 한 뽑기에 1/7 이라 — 실제로 4/8 의 명부에서 첫 판(시도 1)은 3연격을 한 번도 안 뽑은 채 파이터가 30초에 죽었고(잡기 ·
    /// 올려베기 · 빠른 3연격 …) 보스 피격 · 선딜 · 피격 세 장이 기다리다 빈손으로 넘어갔다. 대본이면 명부와 시드가 바뀌어도 같은 장이 찍힌다.
    /// </summary>
    private static readonly string[] _classic = { "3연격", "점프 공격" };

    /// <summary>손과 기다림 — 새 판 · 누르기 · 프레임과 상태 (<see cref="SceneDriver"/>).</summary>
    private readonly SceneDriver _drive;

    private Overfit.Battle.Battle? _battle;

    public ShotRunner() => _drive = new SceneDriver(this, "shots");

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        Log.Info("shots", "start");

        await _drive.Wait(0.6);
        await Screenshot.CaptureAsync(this, "title");

        // 크레딧도 찍는다. 이 화면은 data/credits.json 을 읽어 **자기가 짓는** 화면이라
        // 항목이 늘면 줄이 늘고 링크가 길면 잘린다 — 그 종류의 실패는 로그에 안 남는다.
        // 이 저장소는 화면의 실패를 여러 번 스크린샷에서 처음 봤다.
        Game.Instance.GoTo(Game.Scene.Credits);
        await _drive.Frames(6);
        await Screenshot.CaptureAsync(this, "credits");

        // 옛 1단계의 두 동작으로 찍는다(_classic) — 아래 장면들이 그 명부에 맞춰 짜였다.
        await NewBattle(_classic);

        await Shoot("battle-1-approach", 1.0);

        // 보스 쪽으로 붙는다. 붙어 있어야 판정에 걸리고, 그래야 피격·패리가 찍힌다.
        await WalkIn();

        // ── 대시: 무적 창 한가운데를 잡는다 ────────────────────────────────
        // 무적은 0.14초(≈8프레임)이고 대시는 0.18초다. 4프레임째면 잔상이 서너 장 깔린 채
        // 아직 무적이다 — 정확히 그 차이를 보여주려고 이 순간을 고른다.
        Tap("dash");
        await _drive.Frames(4);
        await Screenshot.CaptureAsync(this, "battle-2-dash");

        // 같은 대시의 9프레임째 — 무적은 끝났고(8.4프레임) 대시는 아직 돈다(10.8프레임까지).
        // **이 두 장이 나란히 있어야** 그 0.04초가 증명된다: 잔상이 멈추고 몸이 어두워진다.
        // 설계상 여기서 맞는 것이 맞는데, 플레이어에게는 지금까지 보이지 않던 구간이다.
        await _drive.Frames(5);
        await Screenshot.CaptureAsync(this, "battle-3-dash-tail");

        await _drive.Wait(0.5);

        // ── 패리: 칼을 사선으로 세우는 0.33초 커밋의 한가운데 (설계 §5.3) ─────────
        // 패리는 이제 누르는 것 한 번이다 — Tap. 가드(↓)와 그림이 갈리는지가 이 장의 증명이다: 패리는 칼을 세우며
        // 움직이고 가드는 서 있다(battle-10-guard 와 나란히 본다). 20틱 커밋의 10틱째가 한가운데다.
        Tap("parry");
        await _drive.Until(() => _battle?.FighterParrying == true, _pollTimeout);
        await _drive.Frames(10);
        await Screenshot.CaptureAsync(this, "battle-4-parry");

        await _drive.Wait(0.5);

        // ── 공격: 칼이 지나가는 그 프레임 ─────────────────────────────────
        // **프레임 수를 세지 않는다.** 전에는 "선딜 0.09초 ≈ 6프레임" 이라 적고 여섯을 셌는데,
        // 그 숫자는 fighters.json 의 선딜과 손으로 맞춘 사본이라 값이 바뀌면 조용히 어긋난다.
        // 그리고 실제로 어긋나 있었다(이슈 #38): 공격 액션이 애니메이션보다 짧아 칼이 나가는
        // 프레임까지 가지도 못했고, 이 스크린샷은 내내 **칼을 뒤로 뺀 자세**를 찍고 있었다.
        // 판정이 서는 틱이 곧 그 프레임이므로 규칙에게 물어보고 셔터를 누른다.
        // **그 틱에 멈춰 찍는다**(CaptureOn · #72). 이 칼은 걸어 들어오는 보스와 겹친 채 창의 첫 틱에 닿고(실제로 gap=-82 ·
        // tick 213), 닿은 칼질은 그 틱에 끝난다 — 판정이 선 것을 보고 나서 찍으면 두 프레임 늦어, 판정 보기(HITBOXES)에
        // 채운 사각형 없이 흰 궤적만 찍혔다.
        Tap("attack");
        await CaptureOn("battle-5-attack", () => _battle is { FighterSwingTested: true }, _pollTimeout);

        // ── 보스 피격: 선딜 도중에 맞아도 공격 자세 그대로 희게 번쩍인다 (#71 · 설계 §6 · §9) ────────
        // 셰이더 흰 플래시는 0.12초(7프레임)뿐이라 벽시계로 노리면 거의 놓친다. **체력이 준 것을 보고** 셔터를 누른다.
        // 증명할 것은 "흰가" 가 아니라 "공격 자세 그대로 흰가" 다 — 그래서 3연격의 선딜(칼을 든 f0 를 0.725초 붙든다)에 칼을 넣는다.
        // 판정까지 0.3초 넘게 남은 선딜만 고른다: 1타는 누른 뒤 5틱에 서므로 칼이 f0 에 선 보스에 닿는다. 번쩍인 장은
        // [view][D] boss_flash anim=… frame=… flash=… 로도 남는다(맞은 뒤 첫 BossView.Show — 플래시 아래 그린 장이다).
        //
        // **닿을 때까지, 칼질이 끝나 설 때마다 다시 누른다** (#72 · #82) — 보스와 겹친 채 서 있어도 첫 칼이 빗나갈 수 있다. 칼질이 경직까지
        // 끝나기를 규칙에게 묻는다(FighterFree): 전에는 0.4초마다 눌렀는데, 칼질 뒤 경직(0.40초)이 들자 둘째 J 가 1타의 경직에 떨어져
        // 2타가 됐다 — 1타의 경직 중 J 는 곧장 2타다.
        await _drive.Until(
            () => _battle is { BossPattern: "3연격", BossWindingUp: true } && _battle.BossNextActiveIn > 0.3,
            _patternTimeout);
        int bossBefore = _battle?.BossHealth ?? 0;
        bool BossHit() => (_battle?.BossHealth ?? 0) < bossBefore;
        for (int f = 0; f < _pollTimeout * Engine.PhysicsTicksPerSecond; f++)
        {
            await PressWhenFree("attack");
            if (BossHit() && _drive.Pause())
            {
                break;
            }
        }

        if (!BossHit())
        {
            Log.Warn("shots", "boss_hit_not_seen");
        }

        // **맞은 틱 바로 뒤에 멈춰 찍는다** (CaptureOn 과 같은 자리). 플래시는 맞은 틱에 1 이고 0.12초(7프레임)에 0 이 된다 —
        // 멈추면 그림(Battle._Process 의 BossView.Show)도 멈춰 맞은 뒤 첫 프레임의 세기(≈ 0.86)가 찍힌다. 멈추지 않고 두 프레임 뒤를 찍었더니
        // 셔터가 그리기를 두 번 더 기다려 절반쯤 꺼진 흰색이 찍혔다. 옛 흰 실루엣은 4장 중 둘째가 흰 장이라 7프레임 뒤를 찍었다.
        await Screenshot.CaptureAsync(this, "battle-5b-boss-hit");
        _drive.Resume();

        // ── 보스 선딜: 예고 자세에 한 색의 선딜 틴트로 선다 (무르익음은 #78 · 링은 #81 로 걷었다) ──
        await _drive.Until(() => _battle?.BossWindingUp == true, _pollTimeout);
        await _drive.Frames(12);
        await Screenshot.CaptureAsync(this, "battle-6-windup");

        // ── 피격: 체력이 줄어든 바로 다음 프레임 ──────────────────────────
        int before = _battle?.FighterHealth ?? 0;
        await _drive.Until(() => (_battle?.FighterHealth ?? 0) < before, _pollTimeout);
        await _drive.Frames(2);
        await Screenshot.CaptureAsync(this, "battle-7-hit");

        // ── 결과 화면: 아무것도 안 하고 맞아 죽는다 ───────────────────────
        await _drive.Until(() => _battle?.ResultVisible == true, _battleTimeout);
        await _drive.Frames(2);
        await Screenshot.CaptureAsync(this, "battle-8-result");

        await Combo();
        await Guarding();
        await Poise();
        await Exhaustion();
        await Facing();
        await Leap();

        // 판정이 그려진 사진은 판정 보기로 띄웠을 때만 뜻이 있다 — 안 켰으면 흰 궤적 위에 아무것도 없다(설계 §6.1 · §9).
        if (GetTree().DebugCollisionsHint)
        {
            await Hitboxes();
        }

        // 동작마다 한 장 (#78 · 설계 2026-09-29 조각1 §2) · 달리기 한 장(§5.4). 다섯 다 대본 판이라 패턴 순서가 시도 시드에 안 달린다(대본 고르기 ·
        // 설계 §4.4) — 어디에 두어도 같은 장이 찍힌다. 판정 보기의 판들 뒤에 둔 것은 들인 순서일 뿐이다. 그 판들도 대본이라 이 다섯이 앞에 끼어 시도
        // 번호가 밀려도 안 바뀐다.
        await Rush();
        await Grab();
        await Uppercut();
        await Offbeat();
        await Run();
        await Bombs();

        Log.Marker("shots", "shots=done");
        GetTree().Quit();
    }

    /// <summary>
    /// 2연격 두 장 (설계 §5.1). <b>2타의 선딜 · 2타의 칼.</b> 2타는 같은 attack2 시트를 반속으로 돌아 칼을 크게
    /// 세운다 — 그것이 보여야 "크게 한 방" 이 읽힌다. 선딜 한가운데와 칼이 나가는 장을 나란히 둔다.
    ///
    /// <para>
    /// <b>새 판에서, 보스에게서 물러나 찍는다</b> — 판정과 섞지 않고 그림만 보려고. 맞으면 피격 자세가 이긴다.
    /// </para>
    /// </summary>
    private async Task Combo()
    {
        await NewBattle(_classic);

        // 보스에게서 멀어진다. **벽까지 가지 않는다** — 벽에 붙으면 몸이 화면 왼쪽 끝에서 잘린다(실제로 그렇게 찍혔다).
        Hold("move_left", true);
        await _drive.Wait(0.8);
        Hold("move_left", false);

        // 보스 쪽으로 **돌아선다.** 왼쪽을 본 채 휘두르면 칼이 몸 앞(왼쪽)으로 나가 2타의 긴 칼이 화면 왼쪽 끝 밖으로
        // 잘린다. 한 틱만 오른쪽을 눌러 방향만 바꾼다 — 7px 움직일 뿐이라 보스에게서는 여전히 멀다.
        // ⚠ **두 프레임이다.** physics_frame 신호는 그 틱의 _PhysicsProcess **앞에** 오므로, 한 프레임만 기다리고
        // 놓으면 Battle 이 읽기 전에 손을 뗀다 — 처음에 Frames(1) 로 찍었더니 칼이 여전히 왼쪽 밖으로 나갔다.
        Hold("move_right", true);
        await _drive.Frames(2);
        Hold("move_right", false);

        // 1타를 누르고 1타 도중에 한 번 더 — 2타는 1타가 끝나는 틱에 이어진다.
        Tap("attack");
        await _drive.Frames(2);
        Tap("attack");

        // 2타가 선 것은 규칙에게 묻는다. 선딜의 한가운데는 **20틱 뒤**다 — 2타 선딜(0.6667초 = 40틱)의 절반을 옮겨 적은
        // 숫자라, 선딜을 20틱 밑으로 줄이면 이 장이 칼 장을 찍어 아래 장과 같아진다(나란히 두면 바로 보인다).
        await _drive.Until(() => _battle?.FighterComboStep == 1, _pollTimeout);
        await _drive.Frames(20);
        await Screenshot.CaptureAsync(this, "battle-5c-combo-windup");

        // 칼이 지나가는 그 프레임 (battle-5-attack 과 같은 규약 — 칼을 대 본 그 틱에 멈춰 찍는다).
        await CaptureOn("battle-5d-combo-blade", () => _battle is { FighterSwingTested: true, FighterComboStep: 1 }, _pollTimeout);
    }

    /// <summary>
    /// 방어 네 장 (이슈 #47 · #53 · #72). <b>버티는 자세 · 스태미나로 깨지는 순간 · 받아친 순간 · 받아쳐 무너진 보스.</b>
    ///
    /// <para>
    /// 증명할 것이 둘이다. ① <b>가드가 깨지는 길은 스태미나 하나다</b>(설계 §5.2) — ↓ 를 붙든 채 맞기만 하면 가드는 Idle 이
    /// 아니라 스태미나가 안 차고, 바닥나는 대에서 깨진다. 옛 빨간 가드 불가 마무리는 걷었다. ② <b>받아친 연출은 약하고, 대신 보스가
    /// 무너진다</b>(설계 §4.3) — 받아친 고리는 가드가 받아낸 고리와 크기가 같고 색만 따뜻하다. 무너진 보스는 take-hit 를 한 번 돌고
    /// 마지막 장에 선 채 푸른 톤이다. battle-10(가드) · battle-10d(받아침)를 나란히 놓으면 고리는 같은 크기 · 몸은 다른 그림이어야 한다.
    /// </para>
    ///
    /// <para>
    /// <b>붙어서 찍는다.</b> 방어는 판정이 닿아야 일이 일어나고, 그 판정은 보스 사거리 안에서만 선다.
    /// 그리고 <b>↓ 를 누르고만 있는다</b> — 가드는 누르고 있는 동안이다(설계 §5.2).
    /// </para>
    /// </summary>
    private async Task Guarding()
    {
        await NewBattle("3연격");

        // 보스 쪽으로 붙는다 — 닿지 않으면 가드가 할 일이 없다.
        await WalkIn();

        // ── 버티는 자세 ───────────────────────────────────────────────────
        // **프레임을 세지 않는다.** 가드가 서는 것은 누른 그 틱이지만 규칙에게 물어보는 규약은
        // 그대로다 — 세어 두면 입력이 한 틱 밀리는 날 조용히 어긋난다.
        Hold("guard", true);
        await _drive.Until(() => _battle?.FighterGuarding == true, _pollTimeout);
        await _drive.Frames(2);
        await Screenshot.CaptureAsync(this, "battle-10-guard");

        // ── 붕괴: 스태미나가 바닥난 그 대 ─────────────────────────────────
        // 붙든 가드는 3연격 한 바퀴에 54(8 · 8 · 14 의 1.8배)를 문다 — 둘째 바퀴의 3타에서 깨진다. 3연격만 도는 판(대본)인 까닭: 점프 공격의
        // 착지와 잡기는 가드를 안 받아(설계 2026-09-29 조각1 §2.3 · §2.4) 스태미나를 안 쓰고 맨몸에 맞는다 — 그런 판이 끼면 깨지기 전에 체력이 준다.
        // 깨지는 것은 **사건**이라 상태로는 못 노린다. 그래서 횟수가 늘어난 것을 보고 셔터를 누른다.
        int broke = _battle?.FighterGuardBreaks ?? 0;
        await _drive.Until(() => (_battle?.FighterGuardBreaks ?? 0) > broke, _guardBreakTimeout);
        await _drive.Frames(3);
        await Screenshot.CaptureAsync(this, "battle-10b-guard-break");

        // ── 받아친 순간 (이슈 #53) ────────────────────────────────────────
        // **↓ 로는 못 받아친다.** 받아치는 것은 K 다 — 판정 <b>직전에</b> 눌러야 창(0.133초) 안에 선다.
        // 프레임을 세지 않고 남은 시간을 보고 누른다(봇이 쓰는 것과 같은 규칙이다): 세어 두면
        // parry_precise_window 를 고치는 순간 이 장이 조용히 맞는 사진이 된다. 누른 것이 빗나가면(타이밍) 맞고 지나간다 — 받아칠 때까지
        // 누른다(3연격만 도는 판이라 판정마다 받아칠 수 있다).
        Hold("guard", false);
        int parried = _battle?.FighterParries ?? 0;
        for (int i = 0; i < 60 * 12 && (_battle?.FighterParries ?? 0) == parried; i++)
        {
            if (_battle is { BossWindingUp: true } && _battle.BossNextActiveIn is > 0 and <= 0.08)
            {
                Tap("parry");
            }

            await _drive.Frames(1);
        }

        if ((_battle?.FighterParries ?? 0) == parried)
        {
            Log.Warn("shots", "parry_not_seen");
        }

        // **히트스톱 안에서 J 를 누른다** (#71 · 설계 §1). 받아친 틱에 보스가 무너져 7프레임 히트스톱이 걸렸다 — 그동안 누른 키는
        // 버려지지 않고 끝난 첫 틱에 넘어가 되받아치기 1타가 선다(로그 [battle][D] hitstop_carry … attack=True). 사람이 받아친 것을
        // 보고 곧장 누르는 자리가 여기다.
        Tap("attack");
        await _drive.Frames(3);
        await Screenshot.CaptureAsync(this, "battle-10d-parry");

        // ── 받아쳐 무너진 보스 (#72 · 설계 §4.3) ──────────────────────────
        // 어느 타를 받아쳐도 무너진다. take-hit(10fps · 4장 = 24프레임)를 다 돈 뒤라야 마지막 장에 선 자세가 찍힌다 —
        // 받아친 뒤 30프레임이다(위의 3 + 27). 탈진은 1.5초(90틱)라 아직 한참 남았다.
        if (_battle is { BossExhausted: false })
        {
            Log.Warn("shots", "exhaust_not_seen");
        }

        await _drive.Frames(27);
        await Screenshot.CaptureAsync(this, "battle-10e-boss-exhausted");
    }

    /// <summary>
    /// 경직 게이지 두 장 (#71 · 설계 §4.5 · §9). <b>반쯤 찬 게이지 · 패리 없이 게이지로 무너진 보스.</b> 붙어서 2연격(J 두 번)을 두 번
    /// 넣는다 — 한 번은 55 로 안 무너지고, 연달아 두 번이면 두 번째 2타에 무너진다. 보스의 칼은 막지도 피하지도 않고 맞는다 —
    /// 칼질은 맞아도 안 끊기고(끝까지 커밋) 보스는 맞아도 하던 것을 안 멈춘다(흰 플래시뿐이다).
    ///
    /// <para>
    /// 셔터는 규칙에게 묻는다(<c>BossPoise</c> · <c>BossExhausted</c>) — 칼이 몇 번 닿았는지를 세면 경직도 데이터를 고치는 날 다른 장이
    /// 찍힌다. 무너진 장은 take-hit(24프레임)를 다 돈 뒤다(<c>battle-10e</c> 와 같은 30프레임) — 보스는 마지막 장에 선 채 푸르고,
    /// 게이지 자리는 파랗게 바뀌어 남은 탈진을 그린다.
    /// </para>
    /// </summary>
    private async Task Poise()
    {
        await NewBattle(_classic);
        await WalkIn();

        bool half = false;
        for (int round = 0; round < 12 && _battle is { BossExhausted: false }; round++)
        {
            // 1타를 누르고 1타 도중에 한 번 더 — 2타는 1타가 끝나는 틱에 이어진다. 2연격이 2타 뒤 경직(0.50초 · #82)까지 끝나 서면 다시
            // 누른다(FighterFree). 전에는 80프레임(2연격 한 바퀴 0.25 + 1.0초) 뒤에 눌렀는데, 경직이 들자 그 J 둘이 2타의 경직에 버려져
            // 한 판 걸러 한 번만 쳤다 — 두 연격 사이가 벌어져 게이지가 줄면 두 번째 2타에 안 무너진다.
            Tap("attack");
            await _drive.Frames(2);
            Tap("attack");
            await _drive.Frames(2);
            for (int f = 0; f < 150 && _battle is { BossExhausted: false, FighterFree: false }; f++)
            {
                await _drive.Frames(1);
            }

            if (!half && _battle is { BossExhausted: false, BossPoise: > 0.3 and < 0.9 })
            {
                half = true;
                await Screenshot.CaptureAsync(this, "battle-12-poise");
            }
        }

        if (_battle is { BossExhausted: false })
        {
            Log.Warn("shots", "poise_break_not_seen");
        }

        await _drive.Frames(30);
        await Screenshot.CaptureAsync(this, "battle-12b-poise-break");
    }

    /// <summary>
    /// 스태미나를 다 써 탈진한 파이터 한 장 (#71 · 설계 §5.5 · §9). <b>대시를 좌우로 네 번</b> 해 스태미나(25 씩)를 거의 다 쓰고, 남은 몇(대시
    /// 사이 Idle 틱에 찬 것)을 <b>패리</b> 한 번으로 0 까지 쓴다 — 모자란 마지막 한 번도 나간다. 그 패리가 경직까지 끝나는 틱에 탈진한다.
    /// take-hit(10fps · 4장 = 24프레임)를 다 돈 뒤라야 마지막 장에 선 자세가 찍힌다 — 30프레임 뒤다. 몸은 탈진 색이고 스태미나 바는
    /// 파랗다(보스 게이지의 탈진과 같은 파랑). 가드 붕괴로 든 탈진과 같은 그림이다(<c>battle-10b</c> 는 붕괴의 순간 · 큰 고리).
    ///
    /// <para>
    /// <b>3연격만 도는 판(대본 · #78)에서</b> 찍는다. 3연격은 보스가 선 자리(1440 · 쉬는 동안 제자리)에서 3.25초 동안 427px 까지만
    /// 쳐 파이터가 선 자리(480)에 안 닿고, 대시 넷(대시 11틱 + 대시 뒤 경직 6틱 + 돌아서는 틱)과 패리 하나(커밋 20틱 + 패리 뒤 경직 15틱 ·
    /// 모두 ≈ 1.9초)와 셔터가 그 안에 든다. 점프 공격이면 도약이 파이터 앞에 내려 맞는 자세가 섞인다.
    /// </para>
    ///
    /// <para>
    /// <b>무엇으로 바닥내나를 두 번 옮겼다</b> (#82). 전에는 1타를 16프레임마다 눌렀다(여덟 번 ≈ 2.1초) — 칼질 뒤 경직(1타 0.40초)이 들자
    /// 여덟 번이 5초를 넘어 3연격 밖으로 나가고, 16프레임마다의 J 는 1타의 경직에 떨어져 2타가 됐다. 그다음 패리만 일곱 번 눌렀는데, 패리 뒤
    /// 경직(0.25초)이 들자 한 번이 35틱이라 일곱 번이 4.1초로 다시 3연격 밖이다. 대시는 25 를 17틱에 써 가장 빠르다. <b>한쪽으로만 네 번</b>
    /// 뛰면 벽에 붙어 몸이 화면 왼쪽 끝에서 잘렸다 — 그래서 왼쪽 · 오른쪽을 번갈아 뛰어 선 자리(480)로 돌아와 보스 쪽을 본 채 찍는다.
    /// 대시는 바라보는 쪽으로만 가서, 뛰기 전에 두 프레임 걸어 돌아선다(<see cref="Combo"/> 와 같은 이유로 두 프레임이다).
    /// </para>
    /// </summary>
    private async Task Exhaustion()
    {
        await NewBattle("3연격");
        foreach (string way in new[] { "move_left", "move_right", "move_left", "move_right" })
        {
            await _drive.Until(() => _battle is { FighterFree: true } or { FighterExhausted: true }, _pollTimeout);
            if (_battle is { FighterExhausted: true })
            {
                break;
            }

            Hold(way, true);
            await _drive.Frames(2);
            Hold(way, false);
            await PressWhenFree("dash");
        }

        for (int f = 0; f < 3 * Engine.PhysicsTicksPerSecond && _battle is { FighterExhausted: false }; f++)
        {
            await PressWhenFree("parry");
        }

        if (_battle is { FighterExhausted: false })
        {
            Log.Warn("shots", "fighter_exhaust_not_seen");
        }

        await _drive.Frames(30);
        await Screenshot.CaptureAsync(this, "battle-10f-fighter-exhausted");
    }

    /// <summary>
    /// 보스의 방향 전환 세 장 (이슈 #36). <b>새 판에서 찍는다</b> — 앞 시퀀스는 파이터가 맞아 죽는 것으로
    /// 끝나므로 이어 붙일 수 없고, 앞에 끼워 넣으면 남은 체력을 몇 초어치 더 써서 뒤의 장면들이
    /// 통째로 결과 화면으로 찍힌다(이 파일의 다른 주석들이 이미 밟은 실패다).
    ///
    /// <para>
    /// 증명해야 하는 것이 둘이다. ① <b>양쪽</b> — 파이터가 왼쪽에 있을 때와 오른쪽에 있을 때
    /// 보스가 각각 그쪽을 보는가(#27 로 몸 충돌이 없어져 반대편으로 돌아갈 수 있게 됐고,
    /// 그때부터 보스는 등 뒤를 향해 칼을 휘두르는 그림이었다). ② <b>잠금</b> — 스윙 도중에
    /// 지나가도 <b>안</b> 돌아보는가. 둘째 장이 없으면 첫째 장은 "따라 도는 것" 만 말하고,
    /// 그것만 맞추려다 예고를 거짓말로 만드는 것이 이 기능의 유일한 함정이다.
    /// </para>
    /// </summary>
    private async Task Facing()
    {
        await NewBattle(_classic);

        // 파이터는 아레나의 25% · 보스는 75% 에 선다 — 보스는 처음부터 왼쪽을 본다.
        await _drive.Wait(0.8);
        await FacingShot("battle-9-face-left");

        // 오른쪽으로 달려 보스를 지나간다.
        Hold("move_right", true);
        await _drive.Wait(2.6);
        Hold("move_right", false);
        await FacingShot("battle-9b-face-right");

        // ── 잠금: 선딜 도중에 반대편으로 지나간다 ─────────────────────────
        // 방금 자리(보스의 오른쪽)에서 기다렸다가 **선딜 도중에** 왼쪽으로 빠져나간다.
        // 보스는 그 선딜이 끝날 때까지 오른쪽을 본 채여야 한다 — 단계가 붙든 3연격의 선딜 자세(칼을 치켜든 attack 의 앞 장)도
        // 오른쪽을 향한 채 그려진다.
        //
        // **판정까지 0.6초 넘게 남은 선딜만 고른다.** 그냥 "선딜인가" 만 보면 끝자락에 걸리고,
        // 그때 지나가면 잠긴 몸 대신 판정이 선 순간이 찍힌다 — 실제로 그렇게 찍혔다(그때는 판정 충격파가 덮었다 · 링은 #81 로 걷었다).
        //
        // **걸음이 아니라 대시로 넘는다.** 보스 몸이 반폭 85 라 걸음(420px/s)으로는 선딜 하나 안에
        // 몸 밖으로 확실히 못 나간다 — 겹친 채 찍히면 어느 쪽에 섰는지가 그림에서 안 읽힌다.
        // 대시는 0.18초에 396px 이라 한 번에 넘기고, 대시 뒤 경직(0.1초 · #82)이 지나면 이어지는 걸음이 남은 프레임만큼 더 벌린다.
        // **3연격만 고른다** — 점프 공격은 도약하는 틱(0.40초)에 착지 자리 쪽으로 돌아선다(설계 §4.2 · 잠금의 유일한 예외).
        await _drive.Until(
            () => _battle is { BossWindingUp: true, BossPattern: "3연격" } && _battle.BossNextActiveIn >= 0.6,
            _patternTimeout);
        Hold("move_left", true);
        await _drive.Frames(2);   // 왼쪽을 보게 세운다 — 대시는 **바라보는 쪽으로만** 간다
        Tap("dash");
        await _drive.Frames(26);
        Hold("move_left", false);
        await Screenshot.CaptureAsync(this, "battle-9c-facing-locked");
    }

    /// <summary>
    /// 점프 공격 두 장 — 공중과 착지 (#72 · #83 · 설계 §4.2 · §6 · §9). 점프 공격만 도는 판(대본 · #78)에서 찍는다.
    ///
    /// <para>
    /// ① <c>battle-6a-leap</c> — <b>정점</b>에서 찍는다(<see cref="Apex"/>). 규칙의 Y 를 그린 몸이 땅에서 가장 높이 떠 있어야 한다(설계 §6
    /// 「보스 높이」).
    /// </para>
    ///
    /// <para>
    /// ② <c>battle-6b-landing-wave</c> (#83) — 착지 창 8틱 중 <b>넷째 틱</b>에 멈춰 찍는다(<see cref="CaptureTested"/>). 흰 충격파가
    /// <b>반쯤 퍼진</b> 장이다: 밑깔개가 아레나 전체(판정을 아레나로 자른 것)에 옅게 깔려 있고, 밝은 앞머리 둘이 보스 발밑과 아레나 끝의
    /// 가운데쯤(0.067초 / 0.125초 ≈ 절반)을 달리고 있으며, 띠의 높이는 판정의 높이(60)다. 전에는 창의 첫 틱에 찍었다 — 앞머리가 화면 밖으로
    /// 곧장 나가던 때라 그 틱밖에 없었다(리뷰 m4). 지금 첫 틱에 찍으면 앞머리가 아직 발밑에 붙어 "달린다" 가 안 보인다.
    /// 파이터는 그 전에 <b>뛰어</b> 띠 위에 떠 있다 — 띠 위와 띠 안이 한 장에서 갈려야 "낮은 곳이 맞는다" 가 읽힌다.
    /// <b>누르는 자리는 0.3초 문턱이 아니라 정점이 정한다.</b> 정점(<see cref="Apex"/>)은 도약 19틱째(패턴 43틱)에 멈추는데, 그때 창(60틱)까지
    /// 17틱(0.283초)이 남아 이미 0.3초 밑이다 — 그래서 아래 기다림은 정점 사진을 풀자마자 참이고, 점프는 창보다 17틱쯤(≈0.28초) 앞에 든다.
    /// 그래도 넘는다: 점프는 누른 틱 + 3 ~ + 55 동안 발이 60 위라(patterns.json 의 점프 공격 _note) 창 8틱을 다 덮는다.
    /// <b>둘은 묶여 있다</b> — 정점을 잡는 자리나 도약의 뜬 시간(<c>motion.air</c>)을 고치면 이 누름이 같이 옮겨 간다. 정점이 창보다 18틱 넘게
    /// 앞서 풀리면 그때부터는 0.3초 문턱이 누름을 정하고, 누름이 창에 너무 붙으면(같은 _note: 창보다 3 ~ 48틱 앞이라야 넘는다) 못 넘어
    /// 아래의 <c>landing_wave_band_gone</c> 이 남는다.
    /// 몸에 안 닿은 띠는 창 내내 대 보므로 넷째 틱에도 <c>BossSwingTested</c> 가 참이다.
    /// </para>
    /// </summary>
    private async Task Leap()
    {
        await NewBattle("점프 공격");
        await Apex("battle-6a-leap", "점프 공격", leap: 1);

        await _drive.Until(
            () => _battle is { BossPattern: "점프 공격", BossWindingUp: true } && _battle.BossNextActiveIn <= 0.3,
            _pollTimeout);
        Tap("jump");

        // 창의 첫 틱을 보고 세 틱 더 민다 — 넷째 틱의 신호 안에서 멈춘다(CaptureOn 과 같은 자리). 그 장의 충격파는 네 프레임 퍼졌다.
        await _drive.Until(() => _battle is { BossSwingTested: true }, _pollTimeout);
        int hp = _battle?.FighterHealth ?? 0;
        await _drive.Frames(3);
        // 띠가 넷째 틱까지 살아 있어야 이 사진이 착지다. 뛴 파이터가 띠를 못 넘었으면 띠는 첫 틱에 닿아 끝나고, 아래의 CaptureTested 는
        // 다음 보스 칼(대본이 점프 공격만 돌리니 다음 점프 공격의 착지 · 2.3초 뒤)을 이 이름으로 말없이 찍는다 — 그 경우를 남긴다(리뷰 n4).
        if (_battle is not { BossSwingTested: true, BossPattern: "점프 공격" } || (_battle?.FighterHealth ?? 0) < hp)
        {
            Log.Warn("shots", $"landing_wave_band_gone pattern={_battle?.BossPattern ?? "-"} hp={_battle?.FighterHealth ?? 0} was={hp}");
        }

        await CaptureTested("battle-6b-landing-wave", _pollTimeout);
    }

    /// <summary>
    /// 도약 <paramref name="leap"/> 번째의 <b>정점</b>에서 판을 세우고 찍는다 (#78 · #59 의 3/6 넘김). 정점은 오르기를 그친 첫 틱으로 잡는다 —
    /// 높이가 앞 틱보다 안 높아진 틱이다(식 4H·s(1−s) 에서 정점 다음 틱 — 도약 19틱째라 280 보다 0.86px 낮다). 전에는 "발이 250 위" 를
    /// 기다렸다: 정점 높이(280)를 손으로 옮긴 수라 <c>motion.height</c> 를 250 밑으로 고치는 날 이 장이 30초를 기다리다 땅을 찍는다.
    /// 이 멈춤 자리를 옮기면 <see cref="Leap"/> 의 점프 누름도 같이 옮겨 간다 — 거기 기다림은 정점을 풀자마자 참이다.
    ///
    /// <para>
    /// 멈추고 찍고 푸는 것은 <see cref="CaptureOn"/> 그대로다(#96 — 전에는 그 세 줄을 여기 따로 들었다). 조건(<c>Top</c>)은 앞 프레임의 높이와
    /// 도약 수를 쥐는데, 기다림(<see cref="SceneDriver.Until"/>)이 프레임마다 한 번만 물어 그 상태가 한 프레임에 두 번 밀리지 않는다.
    /// 상한이 지나도록 정점이 안 오면 <c>[shots][W] apex_not_seen</c> 을 남긴다 — 그 장은 정점이 아닌 채 찍혔다.
    /// </para>
    /// </summary>
    private async Task Apex(string name, string pattern, int leap)
    {
        double last = 0;
        int leaps = 0;
        bool Top()
        {
            double y = _battle is { } b && b.BossPattern == pattern ? b.BossY : 0;
            leaps += last <= 0 && y > 0 ? 1 : 0;
            bool top = leaps == leap && y > 0 && y <= last;
            last = y;
            return top;
        }

        if (!await CaptureOn(name, Top, _patternTimeout))
        {
            Log.Warn("shots", $"apex_not_seen name={name} pattern={pattern} leap={leap} leaps={leaps} y={last:0}");
        }
    }

    /// <summary>
    /// 돌진 중 한 장 (#78 · 설계 §4.6 · §9 · 설계 2026-09-29 조각1 §2.4). 돌진만 도는 판(대본)에서 가만히 선 파이터(480)에게 보스가 달려오는
    /// 한가운데다 — 앞쪽 거리 960(보스는 쉬는 동안 제자리다)에서 멈출 자리(760)까지 12틱을 달리므로 <c>run</c> 에 든 뒤 5틱이다. 그림은 <c>run</c> 을
    /// 3배속(30fps)으로 돈다.
    /// </summary>
    private async Task Rush()
    {
        await NewBattle("돌진");
        await _drive.Until(() => _battle is { BossStepAnim: "run" }, _patternTimeout);
        await _drive.Frames(5);
        await Screenshot.CaptureAsync(this, "battle-13-rush");
    }

    /// <summary>
    /// 흰 구가 붙든 파이터 한 장 (#78 · 설계 §4.7 · §6 · §9). 잡기만 도는 판에서 가만히 선 파이터는 0.60초의 창에 잡힌다(띠가 바닥 전체다).
    /// 붙들린 파이터는 take-hit(10fps · 4장 = 24프레임)를 다 돌고 마지막 장에 선다 — 붙들린 뒤 30프레임이다(붙드는 60틱의 한가운데).
    /// </summary>
    private async Task Grab()
    {
        await NewBattle("잡기");
        await _drive.Until(() => _battle is { FighterHeld: true }, _patternTimeout);
        if (_battle is { FighterHeld: false })
        {
            Log.Warn("shots", "grab_not_seen");
        }

        await _drive.Frames(30);
        await Screenshot.CaptureAsync(this, "battle-13b-grab");
    }

    /// <summary>
    /// 올려베기의 칼 한 장 (설계 2026-09-29 조각1 §2.1). 올려베기만 도는 판에서 판정이 서는 틱(51)이다 — 그림은 attack2 의 셋째 장(f2)을
    /// <b>좌우로 뒤집어</b> 그린다(<c>mirror</c> · 뷰만 읽는다). 뒤집지 않으면 attack2 의 높은 궤적이 보스 등 뒤에 선다 — 이 장에서 흰 궤적이 보스
    /// <b>앞</b>(가만히 선 파이터 쪽)의 공중을 긋고 있어야 한다. 파이터는 멀리(480) 서 있어 안 맞는다 — 칼 그림만 본다.
    /// </summary>
    private async Task Uppercut()
    {
        await NewBattle("올려베기");
        await CaptureTested("battle-13c-uppercut", _patternTimeout);
    }

    /// <summary>
    /// 달리기 중 한 장 (설계 2026-09-29 조각1 §5.4). 쉬기(0.8초) 뒤 달리기를 고른 3연격만 도는 판에서 가만히 선 파이터(480)에게 보스가 달려오는
    /// 한가운데다 — 판이 선 거리 960 에서 파이터 앞 280 까지 49틱을 달리므로 달리기에 든 뒤 24틱이다. 그림은 <c>run</c> 을
    /// <c>feel.run_anim_speed</c>(1 · 10fps)로 돈다 — 돌진(3배)보다 느린 달리기다. 몸 색은 쉬는 색(선딜 틴트가 없다)이다 — 달리는 몸에는 판정이 없다.
    /// </summary>
    private async Task Run()
    {
        _battle = await _drive.NewBattle(new ScriptPlan(0.8, "3연격", Run: true));
        if (_battle is null)
        {
            Log.Warn("shots", "battle_scene_missing script=run");
            return;
        }

        await _drive.Until(() => _battle is { BossRunning: true }, _patternTimeout);
        if (_battle is { BossRunning: false })
        {
            Log.Warn("shots", "run_not_seen");
        }

        await _drive.Frames(24);
        await Screenshot.CaptureAsync(this, "battle-13e-run");
    }

    /// <summary>
    /// 폭탄 여섯 장 (설계 2026-09-30 조각2 §5) — 두 판이다. 보스가 던지기를 보고 끊으려 하므로(§2) 떨어지는 장과 끊기는 장은 다른 판에서 찍는다.
    ///
    /// <para>
    /// ① <b>떨어지는 판</b> — 쉬기 0.8초 뒤 3연격만 도는 판(대본)에서 가만히 선 파이터(480)가 3연격이 서자마자 던진다. 보스는 18틱 뒤 알지만(머리 위
    /// "!") 3연격의 첫 캔슬 지점(78)까지 못 끊는다 — 거기서 끊고 멈칫한 뒤 돌진하는 사이 파이터가 놓고, 폭탄은 달려온 보스를 따라가 떨어진다(§2.5 ·
    /// "3연격이 시작되면 던진다" 를 이 조각의 보스는 못 끊는다). 선딜의 한가운데(손 위의 폭탄 · 3연격의 선딜 · "!"), 놓은 뒤 15틱(나는 폭탄 · 달려오는
    /// 보스), 떨어진 뒤 3프레임(보스 몸의 주황 불꽃)이다. 3연격의 칼은 960 떨어진 파이터에 안 닿는다.
    /// </para>
    ///
    /// <para>
    /// ② <b>끊기는 판</b> — 보스가 2.5초 쉬는 판에서 곧장 던진다. 쉬는 보스는 아는 그 틱(던진 틱 + 18)에 끊는다. 멈칫 15틱의 한가운데(idle 첫 장에
    /// 굳어 선 보스 · "!"), 반응의 돌진이 달리는 중(<c>run</c>), 던지기가 끊긴 뒤 2프레임(손에서 흩어지는 회색 연기 · 돌진의 3타)이다.
    /// </para>
    /// </summary>
    private async Task Bombs()
    {
        _battle = await _drive.NewBattle(new ScriptPlan(0.8, "3연격"));
        if (_battle is null)
        {
            Log.Warn("shots", "battle_scene_missing script=bombs");
            return;
        }

        await _drive.Until(() => _battle is { BossPattern: "3연격" }, _patternTimeout);
        Tap("bomb");
        await _drive.Until(() => _battle is { FighterThrowing: true }, _pollTimeout);
        await _drive.Until(() => _battle is { BossAlert: true }, _pollTimeout);
        if (_battle is { BossAlert: false })
        {
            Log.Warn("shots", "bomb_alert_not_seen");
        }

        await _drive.Frames(24);
        await Screenshot.CaptureAsync(this, "battle-14-bomb-windup");

        await _drive.Until(() => _battle is { BombsInFlight: > 0 }, _patternTimeout);
        await _drive.Frames(15);
        await Screenshot.CaptureAsync(this, "battle-14b-bomb-flight");

        await _drive.Until(() => _battle is { BombsLanded: > 0 }, _patternTimeout);
        if (_battle is { BombsLanded: 0 })
        {
            Log.Warn("shots", "bomb_land_not_seen");
        }

        await _drive.Frames(3);
        await Screenshot.CaptureAsync(this, "battle-14c-bomb-boom");

        await BombCut();
    }

    /// <summary>폭탄의 ② 끊기는 판 — <see cref="Bombs"/> 의 둘째 문단.</summary>
    private async Task BombCut()
    {
        _battle = await _drive.NewBattle(new ScriptPlan(2.5, "3연격"));
        if (_battle is null)
        {
            Log.Warn("shots", "battle_scene_missing script=bomb_cut");
            return;
        }

        await _drive.Until(() => _battle is { FighterFree: true }, _pollTimeout);
        Tap("bomb");
        await _drive.Until(() => _battle is { BossHesitating: true }, _patternTimeout);
        if (_battle is { BossHesitating: false })
        {
            Log.Warn("shots", "bomb_hesitate_not_seen");
        }

        await _drive.Frames(7);
        await Screenshot.CaptureAsync(this, "battle-14d-bomb-hesitate");

        await _drive.Until(() => _battle is { BossStepAnim: "run" }, _patternTimeout);
        await _drive.Frames(4);
        await Screenshot.CaptureAsync(this, "battle-14e-bomb-rush");

        await _drive.Until(() => _battle is { FighterThrowing: false }, _patternTimeout);
        if (_battle is { BombsInFlight: > 0 })
        {
            Log.Warn("shots", "bomb_cut_not_seen");
        }

        await _drive.Frames(2);
        await Screenshot.CaptureAsync(this, "battle-14f-bomb-cut");
    }

    /// <summary>
    /// 엇박의 붙든 f0 한 장 (#78 · 설계 §4.9 · §9). 엇박 3연격만 도는 판에서 패턴의 48틱이다 — 3연격이면 44틱에 칼이 올라(f1) 판정 7틱 앞인데,
    /// 엇박은 칼을 든 f0 에 그대로 서 있다(판정 12틱 앞 · 다음 판정까지 0.20초). 그 틱에 판을 세우고 찍는다. "칼이 안 오른다" 가 보고 누르는
    /// 사람의 단서다 — <c>battle-6-windup</c>(3연격의 f0)과 같은 자세 · 같은 한 색의 선딜 틴트인 것이 이 장의 증명이다(#78 — 무르익음을 걷었다).
    /// </summary>
    private async Task Offbeat()
    {
        await NewBattle("엇박 3연격");
        await CaptureOn(
            "battle-13d-offbeat",
            () => _battle is { BossPattern: "엇박 3연격", BossWindingUp: true } && _battle.BossNextActiveIn is > 0 and <= 0.2 + 1e-9,
            _patternTimeout);
    }

    /// <summary>
    /// 판정 보기 여덟 장 (설계 §9) — <c>HITBOXES=1 tools/build.sh shots</c> 에서만 찍고 <c>out/shots</c> 에만 남는다.
    ///
    /// <para>
    /// ① <b>3연격의 세 장</b> — 판정마다 규칙이 대 본 틱에 한 장. 채운 사각형(규칙이 대 본 모양)이 그림의 흰 궤적과 겹쳐야 한다.
    /// ② <b>착지 띠</b> — 바닥 전체 · 높이 0 ~ 60. ③ <b>착지 앞의 패리</b> — 착지 창이 열리기 직전에 K 를 눌러 패리 창 안에서
    /// 착지를 맞는다. 파이터 몸통이 "패리 창" 색이 아니어야 한다: 착지는 패리를 안 받아 실효 방어가 없다(설계 §6.1).
    /// ② · ③ 의 착지 띠는 땅에 선 몸에 창의 첫 틱에 닿고 그 판정은 그 틱에 끝난다 — 그래서 대 본 그 틱에 판을 세우고 찍는다
    /// (<see cref="CaptureTested"/>). ① 은 빗나간다: 대본의 첫 패턴이라 보스가 선 자리(1440 · 쉬는 동안 제자리라 파이터와 960 떨어져)에서 서고,
    /// 가만히 선 파이터에게 셋 다 사거리(427) 밖(<c>MissedTooFar</c>)이라 창 8틱을 다 산다. 그래도 같은 길로 찍는다 — 닿든 안 닿든 대 본 첫 틱이다.
    /// </para>
    ///
    /// <para>
    /// (#78 · 설계 §9 의 5번 PR) ④ <b>잡기 띠</b> — 0.60초의 바닥 전체 띠. 가만히 선 몸에 첫 틱에 닿아 잡힌다 — 몸통은 맨몸의 색이다: 잡기는
    /// 대시 · 가드 · 패리를 안 받는다. 착지 띠와 같은 모양이지만 흰 충격파가 없고 흰 구가 파이터를 감싼다. ⑤ <b>돌진 뒤 3타</b> — 달려와 멈춘
    /// 자리(파이터 앞 280)에서 3타의 궤적이 파이터에 닿는다. ⑥ <b>올려베기</b>(설계 2026-09-29 조각1 §2.1) — 손으로 적은 사각형 [0, 396, 0, 360] 이
    /// 뒤집어 그린 attack2 의 흰 궤적을 덮는다. 사각형이 그림보다 크다(유저 확인 — 그림과 달라도 크게). 멀리 선 몸이라 창 8틱을 다 산다.
    /// </para>
    ///
    /// <para>
    /// 판마다 대본으로 패턴을 고정한다(<see cref="NewBattle"/> · #78). 전에는 첫 패턴이 원하는 것인 판이 설 때까지 새 판을 세웠다(여덟
    /// 판까지) — 시도마다 시드가 달라 첫 점프 공격이 다섯째 뽑기일 수도 있었다. ③ 은 따로 새 판이다: 같은 판이면 대본이 ② 뒤에 3연격을 한 번 더
    /// 돌린 뒤에야 점프 공격이 다시 오고, 파이터는 그새 착지 자리(파이터 앞 115)에 선 보스의 세 타를 맞는다.
    /// </para>
    /// </summary>
    private async Task Hitboxes()
    {
        await NewBattle("3연격", "점프 공격");

        // ① 3연격의 선딜을 기다려 셋을 차례로 — 대 본 틱을 찍고, 대 보기가 그친 것을 보고 다음 판정을 기다린다.
        await _drive.Until(() => _battle is { BossPattern: "3연격", BossWindingUp: true }, _patternTimeout);
        for (int hit = 1; hit <= 3; hit++)
        {
            await CaptureTested($"hitbox-1-triple-{hit}", _pollTimeout);
            await _drive.Until(() => _battle is { BossSwingTested: false }, _pollTimeout);
        }

        // ②
        await _drive.Until(() => _battle is { BossPattern: "점프 공격", BossWindingUp: true }, _patternTimeout);
        await CaptureTested("hitbox-2-landing", _pollTimeout);

        // ③ 판정까지 5틱(0.08초) 안이면 누른다 — 패리 창(0.133초 = 8틱)이 착지 창의 첫 틱을 덮는다(battle-10d 와 같은 규칙).
        await NewBattle("점프 공격");
        await _drive.Until(
            () => _battle is { BossPattern: "점프 공격", BossWindingUp: true } && _battle.BossNextActiveIn is > 0 and <= 0.08,
            _patternTimeout);
        Tap("parry");
        await CaptureTested("hitbox-3-landing-parry", _pollTimeout);

        // ④ 잡기의 띠 — 동작의 판정이 이것 하나다.
        await NewBattle("잡기");
        await CaptureTested("hitbox-4-grab", _patternTimeout);

        // ⑤ 달려와 멈춘 뒤의 3타(attack3) — 동작의 판정이 이것 하나다.
        await NewBattle("돌진");
        await CaptureTested("hitbox-5-rush-strike", _patternTimeout);

        // ⑥ 올려베기의 손으로 적은 사각형.
        await NewBattle("올려베기");
        await CaptureTested("hitbox-6-uppercut", _patternTimeout);
    }

    /// <summary>
    /// 규칙이 판정을 대 본 <b>그 틱</b>의 화면을 찍는다 (설계 §6.1 · §9). 셔터가 한 틱만 늦어도 첫 틱에 닿아 끝난 판정의 사각형이 없고
    /// 몸통 색도 파이터 쪽 상태로 돌아간 그림이 찍힌다. 그래서 조건이 참인 그 자리에서 트리를 멈춘다 — <see cref="SceneDriver.Until"/> 의 조건은
    /// 물리 틱 신호 안에서, <c>Battle</c> 이 다음 틱을 밀기 <b>전에</b> 돈다. 멈춘 동안 화면은 방금 그린 그 틱이고, 찍은 뒤 푼다.
    /// <paramref name="tested"/> 가 "대 봤나" 다 — 보스 칼은 <see cref="CaptureTested"/>, 파이터 칼은 <c>FighterSwingTested</c>.
    /// 판정 보기가 아닐 때 멈춰도 해가 없다: 찍히는 것은 같은 틱의 그림이다. 그 틱이 왔으면 참이다 — 상한이 지나 그냥 찍었으면 거짓이다.
    /// </summary>
    private async Task<bool> CaptureOn(string name, Func<bool> tested, double timeout)
    {
        bool seen = await _drive.Until(() => tested() && _drive.Pause(), timeout);
        await Screenshot.CaptureAsync(this, name);
        _drive.Resume();
        return seen;
    }

    /// <summary>보스 판정을 대 본 그 틱에 찍는다 — <see cref="CaptureOn"/> 의 보스 쪽.</summary>
    private Task<bool> CaptureTested(string name, double timeout) =>
        CaptureOn(name, () => _battle is { BossSwingTested: true }, timeout);

    /// <summary>
    /// 보스 앞 <see cref="_near"/> 까지 걸어 붙는다 — 시간이 아니라 거리로 멈춘다. 옛 장면들은 1.1초를 걸었는데, 그것으로 붙은 것은 쉬는 동안 보스가
    /// 걸어와 준 덕이었다(설계 2026-09-29 조각1 §5.1 이 걷었다 — 그대로 두니 보스 앞 500 에 서서 가드 · 패리 장면이 판정을 못 만났다).
    /// </summary>
    private async Task WalkIn()
    {
        Hold("move_right", true);
        await _drive.Until(() => _battle is { BossGap: <= _near }, _pollTimeout);
        Hold("move_right", false);
    }

    /// <summary>
    /// 새 판을 세워 <see cref="_battle"/> 에 둔다 — <paramref name="script"/> 를 주면 그 판 하나를 대본으로 세운다(<see cref="SceneDriver.NewBattle"/>).
    /// 판이 안 섰으면 <c>[W] battle_scene_missing</c> 이다 — 스크린샷이 못 찍힌 것은 게임의 규칙 위반이 아니다(<c>shots</c> 는 PNG 개수를 세어
    /// 0장이면 실패시킨다). 같은 사건을 GIF 러너는 <c>[E]</c> 로 본다 — 무게는 부르는 쪽이 정한다(<see cref="SceneDriver"/> 의 주석 · #96).
    /// </summary>
    private async Task NewBattle(params string[] script)
    {
        _battle = await _drive.NewBattle(Moves(script));
        if (_battle is null)
        {
            Log.Warn("shots", $"battle_scene_missing script={string.Join(',', script)}");
        }
    }

    /// <summary>
    /// 파이터가 서 있으면(<c>FighterFree</c>) 한 번 누르고 <b>그 누름이 먹을 때까지</b> 기다린다(상한 10프레임) — 안 서 있으면 한 프레임만
    /// 민다. 누른 뒤 한 프레임만 기다리고 다시 물으면, 누름이 아직 안 먹은 틱의 "서 있다" 를 한 번 더 읽어 둘째 누름이 1타 도중에 떨어진다 —
    /// 그러면 2타가 이어진다(#82 · 실제로 밟았다: 보스 피격 장 뒤에 이을 생각 없던 2타가 닿아 결과 화면의 보스 체력이 160 이었다).
    /// </summary>
    private async Task PressWhenFree(string action)
    {
        if (_battle is not { FighterFree: true })
        {
            await _drive.Frames(1);
            return;
        }

        Tap(action);
        await _drive.Frames(1);
        for (int i = 0; i < 10 && _battle is { FighterFree: true }; i++)
        {
            await _drive.Frames(1);
        }
    }

    /// <summary>
    /// 방향이 <b>가라앉은 뒤</b> 한 장. 패턴이 도는 동안에는 방향이 잠겨 있으므로
    /// (<c>Boss.Face</c>) 패턴 중에 찍으면 "아직 안 돌았다" 가 찍힌다 — 그건 잠금이 일하는
    /// 그림이지 이 두 장이 증명할 것이 아니다(그건 <c>battle-9c</c> 가 맡는다).
    /// </summary>
    private async Task FacingShot(string name)
    {
        await _drive.Until(() => _battle is { BossPattern: null }, _pollTimeout);
        await _drive.Frames(2);
        await Screenshot.CaptureAsync(this, name);
    }

    private async Task Shoot(string name, double after)
    {
        await _drive.Wait(after);
        await Screenshot.CaptureAsync(this, name);
    }
}
