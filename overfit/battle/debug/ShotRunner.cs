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

        // **2단계로 간다.** 1단계 명부는 패턴이 둘뿐이라(stages.json) 셋째 예고가 영원히 안 오고,
        // 그러면 "패턴마다 예고가 다른가" 를 찍어서 증명할 수가 없다.
        Game.Instance.SetStage(2);
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

        // ── 방어 자세: 누른 직후 (이슈 #53) ───────────────────────────────
        // **Tap 이 아니라 Hold 다.** 자세는 누르는 그 틱에 서고 놓는 그 틱에 풀리므로,
        // 탭으로는 한 틱짜리 자세가 되어 셔터가 거의 언제나 빈 화면을 찍는다.
        // 여기서 증명할 것은 "패리와 가드가 **같은 그림**인가" 다 — 창 안인지 밖인지는
        // 판정이 서야 정해지고, 그 전에 화면이 갈라 말하면 거짓말이다.
        Hold("parry", true);
        await Until(() => _battle?.FighterGuarding == true, _pollTimeout);
        await Frames(3);
        await Screenshot.CaptureAsync(this, "battle-4-parry");
        Hold("parry", false);

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

        // ── 패리 불가 선딜(크림슨)은 **여기 없다** ────────────────────────
        // 이슈 #48 이 유일한 패리 불가 패턴(점프 강타)을 뺐다. 조건이 영영 참이 안 되는 기다림은
        // 16초를 버리고 경고 한 줄을 남긴 뒤 **아무 순간이나** 찍는다 — 그렇게 찍힌 장은
        // 파일 이름이 거짓말을 하므로, 기다림을 지운다. 그 붉은색은 이제 **가드 불가**의 것이고
        // (이슈 #53) 그 장은 battle-10c 가 찍는다 — 패리 불가가 돌아오면 다른 신호를 줘야 한다.

        // ── 피격: 체력이 줄어든 바로 다음 프레임 ──────────────────────────
        int before = _battle?.FighterHealth ?? 0;
        await Until(() => (_battle?.FighterHealth ?? 0) < before, _pollTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, "battle-7-hit");

        // ── 변종마다 다른 예고 (이슈 #28 · #48) ───────────────────────────
        // **맨 뒤다.** 서로 다른 변종 셋을 기다리는 것은 여러 주기가 걸리는데, 내려찍기 계열은
        // 전부 연속타라 그 사이에 파이터가 죽는다 — 앞에 두면 예고와 피격 순간이
        // 통째로 결과 화면으로 찍힌다(실제로 그렇게 찍혔다).
        // 계열이 하나가 된 뒤로 이 세 장이 **더** 중요해졌다: 2단계의 변종 셋은 같은 기술이라
        // 링도 모션도 같고, 갈리는 것은 표지 하나뿐이다(끌기의 띠 · 쇄도의 갈매기표 · 쐐기의 눈금).
        // 그 하나가 화면에서 실제로 갈리는지는 나란히 놓고 보는 수밖에 없고, 그래서 id 를 보고
        // 셔터를 누른다 — 같은 변종을 세 번 찍으면 세 장이 똑같고 그건 우연이지 증명이 아니다.
        var shot = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        for (int i = 1; i <= 3; i++)
        {
            await Until(
                () => _battle is { BossWindingUp: true } && _battle.BossPattern is string id && !shot.Contains(id),
                _tellTimeout);
            if (_battle?.BossPattern is string now)
            {
                shot.Add(now);
                Log.Info("shots", $"tell pattern={now} n={i}");
            }

            await Frames(8);
            await Screenshot.CaptureAsync(this, $"battle-6a-tell-{i}");
        }

        // ── 결과 화면: 아무것도 안 하고 맞아 죽는다 ───────────────────────
        await Until(() => _battle?.ResultVisible == true, _battleTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, "battle-8-result");

        await Charging();
        await Guarding();
        await Facing();

        Log.Marker("shots", "shots=done");
        GetTree().Quit();
    }

    /// <summary>
    /// 차지 세 장 (이슈 #40). <b>모으는 중 · 최대 · 최대로 휘두른 칼.</b>
    ///
    /// <para>
    /// 증명할 것은 <b>최대인지 아닌지가 화면에서 갈리는가</b> 하나다. 갈리지 않으면 플레이어는
    /// 2초를 셀 방법이 없고, 그러면 이 기술은 "언제 놓을지 모르는 기술" 이 된다.
    /// 그래서 두 장이 나란히 있어야 한다 — 한 장만으로는 "빛난다" 까지만 말한다.
    /// </para>
    ///
    /// <para>
    /// <b>새 판에서, 보스에게서 물러나 찍는다.</b> 최대 차지는 백장의 빈 시간 아홉 짝 중 여섯에
    /// 들어가지만(fighters.json 의 _note_charge_windup) 나머지 셋에서는 끊긴다 — 스크린샷이
    /// 그 주사위를 같이 굴릴 이유가 없다. <b>설계가 성립하는지는 테스트가 증명하고, 화면에
    /// 보이는지는 여기가 증명한다.</b> 사거리 밖으로 나가면 둘을 섞지 않고 그림만 볼 수 있다.
    /// </para>
    /// </summary>
    private async Task Charging()
    {
        Game.Instance.GoTo(Game.Scene.Battle);
        await Frames(4);
        _battle = GetTree().CurrentScene as Overfit.Battle.Battle;

        // 보스에게서 멀어진다. **벽까지 가지 않는다** — 벽에 붙으면 몸이 화면 왼쪽 끝에서 잘려
        // 링도 몸 색도 반만 보인다(실제로 그렇게 찍혔다). 0.8초면 336px 물러나 거리가 1296px 이고,
        // 보스는 160px/s 로 따라오므로 세 장을 다 찍는 동안 가장 먼 판정(760px)이 닿지 않는다.
        Hold("move_left", true);
        await Wait(0.8);
        Hold("move_left", false);

        Hold("attack", true);

        // **프레임을 세지 않는다.** 차지 시간은 데이터고(charge_tiers), 세어 두면 그 값을 고치는
        // 순간 "중간" 이 최대이거나 0 인 그림이 된다 — 이슈 #38 에서 공격 선딜로 밟은 실패다.
        await Until(() => _battle is { FighterCharging: true } && _battle.FighterChargeProgress >= 0.5, _pollTimeout);
        await Screenshot.CaptureAsync(this, "battle-5c-charge");

        await Until(() => _battle?.FighterChargeMaxed == true, _pollTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, "battle-5d-charge-max");

        // 놓는다 — 칼이 지나가는 그 프레임에 셔터를 누른다 (battle-5-attack 과 같은 규약).
        Hold("attack", false);
        await Until(() => _battle?.FighterAttackActive == true, _pollTimeout);
        await Screenshot.CaptureAsync(this, "battle-5e-charged-swing");
    }

    /// <summary>
    /// 방어 네 장 (이슈 #47 · #53). <b>危 예고(빨강) · 버티는 자세 · 깨지는 순간 · 받아친 순간.</b>
    ///
    /// <para>
    /// 증명할 것이 둘이다. ① <b>빨강이 호박과 확실히 갈리는가</b> — 1·2타는 막을 수 있고 3타는
    /// 못 막으므로, 그 차이가 선딜에서 안 읽히면 "버티면 된다" 를 그대로 믿다 무너진다.
    /// ② <b>받아친 연출이 정말 약한가</b> — 요청이 "가드와 같은 그림 + 약한 흔들림 + 작은 표시" 였고,
    /// 그게 지켜졌는지는 battle-10(자세) · battle-10d(받아침)를 나란히 놓아야만 보인다.
    /// </para>
    ///
    /// <para>
    /// <b>붙어서 찍는다.</b> 방어는 판정이 닿아야 일이 일어나고, 그 판정은 보스 사거리 안에서만 선다.
    /// 그리고 <b>누르고만 있는다</b> — 자세는 유지로 사는 유일한 기술이라 <c>Tap</c> 으로는
    /// 한 틱 만에 풀린다.
    /// </para>
    /// </summary>
    private async Task Guarding()
    {
        // **3단계로 간다** (이슈 #48). 가드 불가는 3단계 변종의 마무리에만 붙으므로 1·2단계에서는
        // 危 예고도 붕괴도 영원히 안 온다 — 그 명부에는 가드 불가 판정이 하나도 없다.
        // 여기만 3단계인 이유는 위의 예고 세 장이 2단계의 변종 셋을 찍기 때문이다(3단계는 다섯이라
        // 셋만 찍으면 어느 셋인지가 실행마다 달라진다).
        Game.Instance.SetStage(3);
        Game.Instance.GoTo(Game.Scene.Battle);
        await Frames(4);
        _battle = GetTree().CurrentScene as Overfit.Battle.Battle;

        // ── 危 예고: 가드 불가 판정을 가진 패턴의 선딜 (빨강 · 이슈 #53) ──
        // **맨 앞이다.** 아래 두 장은 맞아 가며 찍으므로 체력이 줄고, 뒤로 미루면 결과 화면이 찍힌다
        // (이 파일의 다른 주석들이 이미 밟은 실패다). 평소 예고(battle-6-windup)와 **나란히 놓고**
        // 봐야 이 연출이 일한다 — 한 장만으로는 "글자가 있다" 까지만 알 수 있다.
        await Until(() => _battle is { BossWindingUp: true, BossGuardBreak: true }, _tellTimeout);
        await Frames(6);
        await Screenshot.CaptureAsync(this, "battle-10c-guard-break-tell");

        // 보스 쪽으로 붙는다 — 닿지 않으면 가드가 할 일이 없다.
        Hold("move_right", true);
        await Wait(1.1);
        Hold("move_right", false);

        // ── 버티는 자세 ───────────────────────────────────────────────────
        // **프레임을 세지 않는다.** 자세가 서는 것은 이제 누른 그 틱이지만(이슈 #53) 규칙에게
        // 물어보는 규약은 그대로다 — 세어 두면 입력이 한 틱 밀리는 날 조용히 어긋난다.
        Hold("parry", true);
        await Until(() => _battle?.FighterGuarding == true, _pollTimeout);
        await Frames(2);
        await Screenshot.CaptureAsync(this, "battle-10-guard");

        // ── 붕괴: 가드 불가를 가드로 받은 그 순간 ─────────────────────────
        // 깨지는 것은 **사건**이라 상태로는 못 노린다. 그래서 횟수가 늘어난 것을 보고 셔터를
        // 누른다 — 피격 스크린샷과 같은 규약이다.
        int broke = _battle?.FighterGuardBreaks ?? 0;
        await Until(() => (_battle?.FighterGuardBreaks ?? 0) > broke, _tellTimeout);
        await Frames(3);
        await Screenshot.CaptureAsync(this, "battle-10b-guard-break");

        // ── 받아친 순간 (이슈 #53) ────────────────────────────────────────
        // **붙들고만 있어서는 못 받아친다.** 창은 누름에서 0.133초라, 오래 붙들면 그 누름은
        // 이미 낡아 전부 가드다 — 받아치려면 판정 <b>직전에</b> 다시 눌러야 한다.
        // 프레임을 세지 않고 남은 시간을 보고 누른다(봇이 쓰는 것과 같은 규칙이다):
        // 세어 두면 parry_precise_window 를 고치는 순간 이 장이 조용히 가드 사진이 된다.
        //
        // 붕괴(크게 터진다) 바로 다음 장인 것이 요점이다 — 두 장이 붙어 있어야
        // "받아친 연출이 약하다" 가 비교로 읽힌다.
        Hold("parry", false);
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

        // ── 지친 보스: 마무리를 받아쳐 굳은 동안 (이슈 #53) ──────────────
        // **Medieval King Pack 2 에는 지친 모션이 없다.** 시트는 열뿐이고(idle · run · jump · fall ·
        // attack1~3 · take-hit · take-hit-white · death) 그중 "숨이 차 서 있다" 인 것이 하나도 없다.
        // 없는 이름으로 Play 하면 뷰가 [W] 한 줄만 남기고 아무것도 안 바꿔, 칼을 든 공격 자세가
        // 2.3초 동안 그대로 선다 — 상을 받은 장면이 상을 안 받은 장면과 똑같아진다.
        // 그래서 지어내지 않고 **있는 것을 느리게** 돌린다: idle × stagger_anim_speed + 식은 몸 색.
        // 이 장은 그 둘이 실제로 "지쳤다" 로 읽히는지를 눈으로 확인하는 자리다.
        //
        // 위 루프가 받아친 것이 마무리가 아니었으면 굳지 않으므로, 굳을 때까지 계속 받아친다.
        for (int i = 0; i < 60 * 14 && _battle is { BossStaggered: false }; i++)
        {
            if (_battle is { BossWindingUp: true } && _battle.BossNextActiveIn is > 0 and <= 0.08)
            {
                Tap("parry");
            }

            await Frames(1);
        }

        if (_battle is { BossStaggered: false })
        {
            Log.Warn("shots", "stagger_not_seen");
        }

        await Frames(6);
        await Screenshot.CaptureAsync(this, "battle-10e-boss-exhausted");

        // ── 헛스윙: 칼은 지나갔는데 아무것도 안 나온 그 순간 (이슈 #48) ───
        // 3단계에만 있는 III-역린 의 박자다. **판정과 다른 그림이어야** 이 변종이 배울 수 있는
        // 함정이 된다 — 판정은 섬광 + 스파크 + 흔들림이고 헛스윙은 **빈 고리** 하나다.
        // 0.34초짜리 사건이라 벽시계로는 못 노린다: 개수가 는 것을 보고 셔터를 누른다.
        // 못 만나도 경고 한 줄이다 — 변종 다섯 중 하나라 여러 주기가 걸릴 수 있다.
        int feints = _battle?.BossFeints ?? 0;
        await Until(() => (_battle?.BossFeints ?? 0) > feints, _tellTimeout);
        await Frames(3);
        await Screenshot.CaptureAsync(this, "battle-11-feint");
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
        await Until(
            () => _battle is { BossWindingUp: true } && _battle.BossNextActiveIn >= 0.6,
            _tellTimeout);
        Hold("move_left", true);
        await Frames(2);   // 왼쪽을 보게 세운다 — 대시는 **바라보는 쪽으로만** 간다
        Tap("dash");
        await Frames(26);
        Hold("move_left", false);
        await Screenshot.CaptureAsync(this, "battle-9c-facing-locked");
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
