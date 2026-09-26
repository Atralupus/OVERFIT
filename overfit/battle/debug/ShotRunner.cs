using System;
using System.Threading.Tasks;
using Godot;
using Overfit.Core;

namespace Overfit.Battle.Debug;

/// <summary>
/// 창을 띄운 채 씬을 돌며 스크린샷을 찍는다. <c>tools/build.sh shots</c> 가 띄운다.
///
/// <para>
/// 전에는 벽시계로만 기다렸다 — 정해진 초에 셔터를 눌렀다. 그러면 <b>무엇이 찍힐지 아무도 모른다</b>:
/// 패턴 주기(간격 0.8초 + 패턴 1.65~1.90초)와 어긋나 실행할 때마다 다른 순간이 나오고,
/// 플레이어는 아무것도 안 하므로 대시·패리·공격은 한 번도 안 찍힌다.
/// </para>
///
/// <para>
/// 그래서 둘을 한다. ① <b>입력을 넣는다</b> — 사람이 누르는 것과 같은 액션을 합성해
/// <c>Battle</c> 의 <c>Read()</c> 가 그대로 읽게 한다. ② <b>상태를 보고 셔터를 누른다</b> —
/// 보스가 선딜에 들어갔을 때, 체력이 줄었을 때, 결과가 떴을 때.
/// 그래야 스크린샷이 "이 피드백이 보이는가" 를 실제로 증명한다.
/// </para>
/// </summary>
public partial class ShotRunner : Node
{
    /// <summary>한 판이 끝나기를 기다리는 상한(초). 넘으면 결과 화면 없이 넘어간다.</summary>
    private const double _battleTimeout = 90.0;

    /// <summary>상태를 기다릴 때의 기본 상한(초).</summary>
    private const double _pollTimeout = 8.0;

    /// <summary>
    /// <b>아직 안 찍은</b> 패턴의 선딜을 기다리는 상한(초). 기본 상한보다 길다 — 패턴은 무작위로
    /// 뽑히므로 특정 하나를 기다리는 것은 한 주기가 아니라 여러 주기다(주기 ≈ 간격 0.8 + 패턴 1.7~1.9초).
    /// 넘기면 경고만 남기고 그냥 찍는다 — 스크린샷이 못 찍힌 것은 게임의 규칙 위반이 아니다.
    /// </summary>
    private const double _tellTimeout = 16.0;

    /// <summary>
    /// 두 패턴 중 <b>정해진 하나</b>를 기다리는 상한(초) (#72). 1단계는 uniform 이라 한 주기(간격 0.8 + 3.25 또는 1.5초)마다
    /// 반반이다 — 30초면 예닐곱 번 뽑아 못 볼 확률이 1% 밑이다. 넘기면 위와 같이 경고만 남기고 그냥 찍는다.
    /// </summary>
    private const double _patternTimeout = 30.0;

    private Overfit.Battle.Battle? _battle;

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        Log.Info("shots", "start");

        await Wait(0.6);
        await Screenshot.CaptureAsync(this, "title");

        // 크레딧도 찍는다. 이 화면은 data/credits.json 을 읽어 **자기가 짓는** 화면이라
        // 항목이 늘면 줄이 늘고 링크가 길면 잘린다 — 그 종류의 실패는 로그에 안 남는다.
        // 이 저장소는 화면의 실패를 여러 번 스크린샷에서 처음 봤다.
        Game.Instance.GoTo(Game.Scene.Credits);
        await Frames(6);
        await Screenshot.CaptureAsync(this, "credits");

        // 1단계에서 찍는다 — 유저가 처음 만나는 판이다(#72 · 두 단계의 명부는 5번 PR 까지 같다).
        Game.Instance.SetStage(1);
        Game.Instance.GoTo(Game.Scene.Battle);
        await Frames(4);
        _battle = GetTree().CurrentScene as Overfit.Battle.Battle;
        if (_battle is null)
        {
            // [E] 가 아니라 [W] 다 — 스크린샷이 못 찍힌 것은 게임의 규칙 위반이 아니다.
            // 대신 shots 는 PNG 개수를 세어 0장이면 실패시킨다.
            Log.Warn("shots", "battle_scene_missing");
        }

        await Shoot("battle-1-approach", 1.0);

        // 보스 쪽으로 붙는다. 붙어 있어야 판정에 걸리고, 그래야 피격·패리가 찍힌다.
        Hold("move_right", true);
        await Wait(1.1);
        Hold("move_right", false);

        // ── 대시: 무적 창 한가운데를 잡는다 ────────────────────────────────
        // 무적은 0.14초(≈8프레임)이고 대시는 0.18초다. 4프레임째면 잔상이 서너 장 깔린 채
        // 아직 무적이다 — 정확히 그 차이를 보여주려고 이 순간을 고른다.
        Tap("dash");
        await Frames(4);
        await Screenshot.CaptureAsync(this, "battle-2-dash");

        // 같은 대시의 9프레임째 — 무적은 끝났고(8.4프레임) 대시는 아직 돈다(10.8프레임까지).
        // **이 두 장이 나란히 있어야** 그 0.04초가 증명된다: 잔상이 멈추고 몸이 어두워진다.
        // 설계상 여기서 맞는 것이 맞는데, 플레이어에게는 지금까지 보이지 않던 구간이다.
        await Frames(5);
        await Screenshot.CaptureAsync(this, "battle-3-dash-tail");

        await Wait(0.5);

        // ── 패리: 칼을 사선으로 세우는 0.33초 커밋의 한가운데 (설계 §5.3) ─────────
        // 패리는 이제 누르는 것 한 번이다 — Tap. 가드(↓)와 그림이 갈리는지가 이 장의 증명이다: 패리는 칼을 세우며
        // 움직이고 가드는 서 있다(battle-10-guard 와 나란히 본다). 20틱 커밋의 10틱째가 한가운데다.
        Tap("parry");
        await Until(() => _battle?.FighterParrying == true, _pollTimeout);
        await Frames(10);
        await Screenshot.CaptureAsync(this, "battle-4-parry");

        await Wait(0.5);

        // ── 공격: 칼이 지나가는 그 프레임 ─────────────────────────────────
        // **프레임 수를 세지 않는다.** 전에는 "선딜 0.09초 ≈ 6프레임" 이라 적고 여섯을 셌는데,
        // 그 숫자는 fighters.json 의 선딜과 손으로 맞춘 사본이라 값이 바뀌면 조용히 어긋난다.
        // 그리고 실제로 어긋나 있었다(이슈 #38): 공격 액션이 애니메이션보다 짧아 칼이 나가는
        // 프레임까지 가지도 못했고, 이 스크린샷은 내내 **칼을 뒤로 뺀 자세**를 찍고 있었다.
        // 판정이 서는 틱이 곧 그 프레임이므로 규칙에게 물어보고 셔터를 누른다.
        Tap("attack");
        await Until(() => _battle?.FighterAttackActive == true, _pollTimeout);
        await Screenshot.CaptureAsync(this, "battle-5-attack");

        // ── 보스 피격: 작가가 그린 흰 실루엣 (이슈 #28) ───────────────────
        // 0.2초(12프레임)뿐이라 벽시계로 노리면 거의 놓친다. **체력이 준 것을 보고** 셔터를 누른다 —
        // 때린 것이 닿았는지가 안 보이면 공격에 값이 안 붙는다는 것이 이 연출의 이유이고,
        // 그 증명은 "흰가" 가 아니라 "맞은 그 프레임에 흰가" 다.
        await Wait(0.4);
        int bossBefore = _battle?.BossHealth ?? 0;
        Tap("attack");
        // **7프레임 뒤다(≈0.117초).** 팩의 take-hit-white 는 4프레임 10fps 이고 흰 프레임은
        // 그중 두 번째라, 맞은 그 프레임을 찍으면 흰색이 아니라 평범한 피격 자세가 나온다 —
        // 실제로 그렇게 찍혔고 "흰 플래시가 없다" 로 잘못 읽힐 뻔했다.
        await Until(() => (_battle?.BossHealth ?? 0) < bossBefore, _pollTimeout);
        await Frames(7);
        await Screenshot.CaptureAsync(this, "battle-5b-boss-hit");

        // ── 보스 선딜: 예고 링이 조여 드는 중 ─────────────────────────────
        await Until(() => _battle?.BossWindingUp == true, _pollTimeout);
        await Frames(12);
        await Screenshot.CaptureAsync(this, "battle-6-windup");

        // ── 피격: 체력이 줄어든 바로 다음 프레임 ──────────────────────────
        int before = _battle?.FighterHealth ?? 0;
        await Until(() => (_battle?.FighterHealth ?? 0) < before, _pollTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, "battle-7-hit");

        // ── 결과 화면: 아무것도 안 하고 맞아 죽는다 ───────────────────────
        await Until(() => _battle?.ResultVisible == true, _battleTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, "battle-8-result");

        await Combo();
        await Guarding();
        await Facing();
        await Leap();
        await StageTwo();

        // 판정이 그려진 사진은 판정 보기로 띄웠을 때만 뜻이 있다 — 안 켰으면 흰 궤적 위에 아무것도 없다(설계 §6.1 · §9).
        if (GetTree().DebugCollisionsHint)
        {
            await Hitboxes();
        }

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
        Game.Instance.GoTo(Game.Scene.Battle);
        await Frames(4);
        _battle = GetTree().CurrentScene as Overfit.Battle.Battle;

        // 보스에게서 멀어진다. **벽까지 가지 않는다** — 벽에 붙으면 몸이 화면 왼쪽 끝에서 잘린다(실제로 그렇게 찍혔다).
        Hold("move_left", true);
        await Wait(0.8);
        Hold("move_left", false);

        // 보스 쪽으로 **돌아선다.** 왼쪽을 본 채 휘두르면 칼이 몸 앞(왼쪽)으로 나가 2타의 긴 칼이 화면 왼쪽 끝 밖으로
        // 잘린다. 한 틱만 오른쪽을 눌러 방향만 바꾼다 — 7px 움직일 뿐이라 보스에게서는 여전히 멀다.
        // ⚠ **두 프레임이다.** physics_frame 신호는 그 틱의 _PhysicsProcess **앞에** 오므로, 한 프레임만 기다리고
        // 놓으면 Battle 이 읽기 전에 손을 뗀다 — 처음에 Frames(1) 로 찍었더니 칼이 여전히 왼쪽 밖으로 나갔다.
        Hold("move_right", true);
        await Frames(2);
        Hold("move_right", false);

        // 1타를 누르고 1타 도중에 한 번 더 — 2타는 1타가 끝나는 틱에 이어진다.
        Tap("attack");
        await Frames(2);
        Tap("attack");

        // 2타가 선 것은 규칙에게 묻는다. 선딜의 한가운데는 **20틱 뒤**다 — 2타 선딜(0.6667초 = 40틱)의 절반을 옮겨 적은
        // 숫자라, 선딜을 20틱 밑으로 줄이면 이 장이 칼 장을 찍어 아래 장과 같아진다(나란히 두면 바로 보인다).
        await Until(() => _battle?.FighterComboStep == 1, _pollTimeout);
        await Frames(20);
        await Screenshot.CaptureAsync(this, "battle-5c-combo-windup");

        // 칼이 지나가는 그 프레임 (battle-5-attack 과 같은 규약).
        await Until(() => _battle is { FighterAttackActive: true, FighterComboStep: 1 }, _pollTimeout);
        await Screenshot.CaptureAsync(this, "battle-5d-combo-blade");
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
        Game.Instance.SetStage(1);
        Game.Instance.GoTo(Game.Scene.Battle);
        await Frames(4);
        _battle = GetTree().CurrentScene as Overfit.Battle.Battle;

        // 보스 쪽으로 붙는다 — 닿지 않으면 가드가 할 일이 없다.
        Hold("move_right", true);
        await Wait(1.1);
        Hold("move_right", false);

        // ── 버티는 자세 ───────────────────────────────────────────────────
        // **프레임을 세지 않는다.** 가드가 서는 것은 누른 그 틱이지만 규칙에게 물어보는 규약은
        // 그대로다 — 세어 두면 입력이 한 틱 밀리는 날 조용히 어긋난다.
        Hold("guard", true);
        await Until(() => _battle?.FighterGuarding == true, _pollTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, "battle-10-guard");

        // ── 붕괴: 스태미나가 바닥난 그 대 ─────────────────────────────────
        // 붙든 가드는 3연격 한 바퀴에 54(8 · 8 · 14 의 1.8배)를, 점프 공격의 착지에 21.6 을 문다 — 두세 패턴이면 깨진다.
        // 깨지는 것은 **사건**이라 상태로는 못 노린다. 그래서 횟수가 늘어난 것을 보고 셔터를 누른다.
        int broke = _battle?.FighterGuardBreaks ?? 0;
        await Until(() => (_battle?.FighterGuardBreaks ?? 0) > broke, _tellTimeout);
        await Frames(3);
        await Screenshot.CaptureAsync(this, "battle-10b-guard-break");

        // ── 받아친 순간 (이슈 #53) ────────────────────────────────────────
        // **↓ 로는 못 받아친다.** 받아치는 것은 K 다 — 판정 <b>직전에</b> 눌러야 창(0.133초) 안에 선다.
        // 프레임을 세지 않고 남은 시간을 보고 누른다(봇이 쓰는 것과 같은 규칙이다): 세어 두면
        // parry_precise_window 를 고치는 순간 이 장이 조용히 맞는 사진이 된다. 점프 공격의 착지는 패리를 못 받으므로
        // 거기 누른 것은 맞고 지나간다 — 받아칠 때까지 누른다.
        Hold("guard", false);
        int parried = _battle?.FighterParries ?? 0;
        for (int i = 0; i < 60 * 12 && (_battle?.FighterParries ?? 0) == parried; i++)
        {
            if (_battle is { BossWindingUp: true } && _battle.BossNextActiveIn is > 0 and <= 0.08)
            {
                Tap("parry");
            }

            await Frames(1);
        }

        if ((_battle?.FighterParries ?? 0) == parried)
        {
            Log.Warn("shots", "parry_not_seen");
        }

        await Frames(3);
        await Screenshot.CaptureAsync(this, "battle-10d-parry");

        // ── 받아쳐 무너진 보스 (#72 · 설계 §4.3) ──────────────────────────
        // 어느 타를 받아쳐도 무너진다. take-hit(10fps · 4장 = 24프레임)를 다 돈 뒤라야 마지막 장에 선 자세가 찍힌다 —
        // 받아친 뒤 30프레임이다(위의 3 + 27). 탈진은 1.5초(90틱)라 아직 한참 남았다.
        if (_battle is { BossExhausted: false })
        {
            Log.Warn("shots", "exhaust_not_seen");
        }

        await Frames(27);
        await Screenshot.CaptureAsync(this, "battle-10e-boss-exhausted");
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
        Game.Instance.GoTo(Game.Scene.Battle);
        await Frames(4);
        _battle = GetTree().CurrentScene as Overfit.Battle.Battle;

        // 파이터는 아레나의 25% · 보스는 75% 에 선다 — 보스는 처음부터 왼쪽을 본다.
        await Wait(0.8);
        await FacingShot("battle-9-face-left");

        // 오른쪽으로 달려 보스를 지나간다.
        Hold("move_right", true);
        await Wait(2.6);
        Hold("move_right", false);
        await FacingShot("battle-9b-face-right");

        // ── 잠금: 선딜 도중에 반대편으로 지나간다 ─────────────────────────
        // 방금 자리(보스의 오른쪽)에서 기다렸다가 **선딜 도중에** 왼쪽으로 빠져나간다.
        // 보스는 그 선딜이 끝날 때까지 오른쪽을 본 채여야 한다 — 예고(칼)도 오른쪽에 그대로 있다.
        //
        // **판정까지 0.6초 넘게 남은 선딜만 고른다.** 그냥 "선딜인가" 만 보면 끝자락에 걸리고,
        // 그때 지나가면 잠긴 몸 대신 판정 충격파가 찍힌다 — 실제로 그렇게 찍혔다.
        //
        // **걸음이 아니라 대시로 넘는다.** 보스 몸이 반폭 85 라 걸음(420px/s)으로는 선딜 하나 안에
        // 몸 밖으로 확실히 못 나간다 — 겹친 채 찍히면 어느 쪽에 섰는지가 그림에서 안 읽힌다.
        // 대시는 0.18초에 396px 이라 한 번에 넘기고, 남은 프레임은 이어지는 걸음이 더 벌린다.
        // **3연격만 고른다** — 점프 공격은 도약하는 틱(0.40초)에 착지 자리 쪽으로 돌아선다(설계 §4.2 · 잠금의 유일한 예외).
        await Until(
            () => _battle is { BossWindingUp: true, BossPattern: "3연격" } && _battle.BossNextActiveIn >= 0.6,
            _tellTimeout);
        Hold("move_left", true);
        await Frames(2);   // 왼쪽을 보게 세운다 — 대시는 **바라보는 쪽으로만** 간다
        Tap("dash");
        await Frames(26);
        Hold("move_left", false);
        await Screenshot.CaptureAsync(this, "battle-9c-facing-locked");
    }

    /// <summary>
    /// 공중의 점프 공격 한 장 (#72 · 설계 §4.2 · §9). <b>정점 근처</b>에서 찍는다 — 궤적은 4H·s(1−s)(H = 280)라 발이 250 위인 것은
    /// 36틱 중 가운데 11틱 남짓이다. 규칙의 Y 를 그린 몸이 땅에서 떠 있어야 한다(설계 §6 「보스 높이」).
    /// <b>새 판에서</b> 점프 공격을 기다린다 — 패턴은 무작위라 몇 주기일 수 있다.
    /// </summary>
    private async Task Leap()
    {
        await NewBattle(1);
        await Until(() => _battle is { BossPattern: "점프 공격" } && _battle.BossY >= 250, _patternTimeout);
        await Screenshot.CaptureAsync(this, "battle-6a-leap");
    }

    /// <summary>
    /// 2단계 전투 한 장 (#72 · 설계 §9). 두 단계의 명부가 5번 PR 까지 같아 싸우는 그림은 1단계와 같다 — 그래서 그 판의
    /// <b>결과 화면</b>을 찍는다: "2단계 · 보스 체력 …" 이 그 판이 2단계였다는 것을 글로 말한다. 아무것도 안 하고 맞아 죽는다
    /// (<c>battle-8-result</c> 와 같은 길).
    /// </summary>
    private async Task StageTwo()
    {
        await NewBattle(2);
        await Until(() => _battle?.ResultVisible == true, _battleTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, "battle-11-stage-2");
    }

    /// <summary>
    /// 판정 보기 다섯 장 (설계 §9) — <c>HITBOXES=1 tools/build.sh shots</c> 에서만 찍고 <c>out/shots</c> 에만 남는다.
    ///
    /// <para>
    /// ① <b>3연격의 세 장</b> — 판정마다 규칙이 대 본 틱에 한 장. 채운 사각형(규칙이 대 본 모양)이 그림의 흰 궤적과 겹쳐야 한다.
    /// ② <b>착지 띠</b> — 바닥 전체 · 높이 0 ~ 60. ③ <b>착지 앞의 패리</b> — 착지 창이 열리기 직전에 K 를 눌러 패리 창 안에서
    /// 착지를 맞는다. 파이터 몸통이 "패리 창" 색이 아니어야 한다: 착지는 패리를 안 받아 실효 방어가 없다(설계 §6.1).
    /// 가만히 선 파이터는 셋 다 창의 첫 틱에 맞고 그 판정은 그 틱에 끝난다 — 그래서 대 본 그 틱에 판을 세우고 찍는다
    /// (<see cref="CaptureTested"/>).
    /// </para>
    ///
    /// <para>
    /// ③ 은 <b>새 판의 첫 점프 공격</b>에서 찍는다. 같은 판에서 두 번째를 기다리면 그새 착지 자리(파이터 115 앞)에 선 보스의
    /// 3연격을 서너 번 맞아 죽을 수 있다 — 새 판은 보스가 960 떨어져 선다.
    /// </para>
    /// </summary>
    private async Task Hitboxes()
    {
        await NewBattle(1);

        // ① 3연격의 선딜을 기다려 셋을 차례로 — 대 본 틱을 찍고, 대 보기가 그친 것을 보고 다음 판정을 기다린다.
        await Until(() => _battle is { BossPattern: "3연격", BossWindingUp: true }, _patternTimeout);
        for (int hit = 1; hit <= 3; hit++)
        {
            await CaptureTested($"hitbox-1-triple-{hit}", _pollTimeout);
            await Until(() => _battle is { BossSwingTested: false }, _pollTimeout);
        }

        // ②
        await Until(() => _battle is { BossPattern: "점프 공격", BossWindingUp: true }, _patternTimeout);
        await CaptureTested("hitbox-2-landing", _pollTimeout);

        // ③ 판정까지 5틱(0.08초) 안이면 누른다 — 패리 창(0.133초 = 8틱)이 착지 창의 첫 틱을 덮는다(battle-10d 와 같은 규칙).
        await NewBattle(1);
        await Until(
            () => _battle is { BossPattern: "점프 공격", BossWindingUp: true } && _battle.BossNextActiveIn is > 0 and <= 0.08,
            _patternTimeout);
        Tap("parry");
        await CaptureTested("hitbox-3-landing-parry", _pollTimeout);
    }

    /// <summary>
    /// 보스 판정을 대 본 <b>그 틱</b>의 화면을 찍는다 (설계 §6.1 · §9). 셔터가 한 틱만 늦어도 첫 틱에 닿아 끝난 판정의 사각형이 없고
    /// 몸통 색도 파이터 쪽 상태로 돌아간 그림이 찍힌다. 그래서 조건이 참인 그 자리에서 트리를 멈춘다 — <see cref="Until"/> 의 조건은
    /// 물리 틱 신호 안에서, <c>Battle</c> 이 다음 틱을 밀기 <b>전에</b> 돈다. 멈춘 동안 화면은 방금 그린 그 틱이고, 찍은 뒤 푼다.
    /// </summary>
    private async Task CaptureTested(string name, double timeout)
    {
        await Until(() => _battle is { BossSwingTested: true } && Pause(), timeout);
        await Screenshot.CaptureAsync(this, name);
        GetTree().Paused = false;
    }

    /// <summary>트리를 멈춘다. <see cref="CaptureTested"/> 의 조건 안에서 부르려고 참을 돌려준다.</summary>
    private bool Pause()
    {
        GetTree().Paused = true;
        return true;
    }

    /// <summary>그 단계의 새 판을 세우고 씬이 설 때까지 기다린다.</summary>
    private async Task NewBattle(int stage)
    {
        Game.Instance.SetStage(stage);
        Game.Instance.GoTo(Game.Scene.Battle);
        await Frames(4);
        _battle = GetTree().CurrentScene as Overfit.Battle.Battle;
    }

    /// <summary>
    /// 방향이 <b>가라앉은 뒤</b> 한 장. 패턴이 도는 동안에는 방향이 잠겨 있으므로
    /// (<c>Boss.Face</c>) 패턴 중에 찍으면 "아직 안 돌았다" 가 찍힌다 — 그건 잠금이 일하는
    /// 그림이지 이 두 장이 증명할 것이 아니다(그건 <c>battle-9c</c> 가 맡는다).
    /// </summary>
    private async Task FacingShot(string name)
    {
        await Until(() => _battle is { BossPattern: null }, _pollTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, name);
    }

    /// <summary>
    /// 액션 하나를 <b>이번 프레임에만</b> 누른다. 엔진의 입력 큐로 넣으므로
    /// <c>Battle</c> 은 사람이 누른 것과 구별할 수 없다 — 합성 경로를 따로 만들지 않는 이유다.
    /// </summary>
    private static void Tap(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
    }

    /// <summary>이동처럼 누르고 있어야 하는 액션. <c>IsActionPressed</c>(레벨)가 읽는다.</summary>
    private static void Hold(string action, bool pressed)
    {
        if (pressed)
        {
            Input.ActionPress(action);
        }
        else
        {
            Input.ActionRelease(action);
        }
    }

    private async Task Shoot(string name, double after)
    {
        await Wait(after);
        await Screenshot.CaptureAsync(this, name);
    }

    private async Task Wait(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>
    /// 조건이 참이 될 때까지 프레임 단위로 기다린다. <b>상한이 있다</b> —
    /// 안 오는 상태를 영원히 기다리면 완료 표지가 안 찍혀 <c>shots</c> 가 "끝까지 못 갔다" 로 죽는데,
    /// 진짜 이유(그 상태가 안 왔다)는 로그에 안 남는다.
    /// </summary>
    private async Task Until(Func<bool> ready, double timeout)
    {
        double waited = 0;
        while (!ready() && waited < timeout)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            waited += 1.0 / Engine.PhysicsTicksPerSecond;
        }

        if (!ready())
        {
            Log.Warn("shots", $"timeout waited={waited:0.0}s — 그 순간이 안 와서 그냥 찍는다");
        }
    }
}
