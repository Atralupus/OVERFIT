using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

public class HitResolverTests
{
    private const double _dt = 1.0 / 60.0;
    private const double _bossX = 960;

    private static Fighter Spawn(double x) => new(TestConfigs.Fighter(), TestConfigs.Arena(), x);

    /// <summary>
    /// 기본 태그. 대시 창은 파이터의 것보다 <b>넓게</b> 둔다 (0.18 &gt; 0.14) — 그래야 창을 따로 주지 않은 테스트는 전부 파이터 쪽이 결정한다.
    /// </summary>
    private static PatternTags Tags(double? dashWindow = null) => new()
    {
        DashWindow = dashWindow ?? 0.18,
        DashDirection = "out",
        Jumpable = false,
        AntiAir = false,
        PunishGreed = false,
        Reach = "mid",
        MultiHit = 1,
        Tracking = false,
    };

    /// <summary>
    /// <paramref name="ticks"/> 틱째의 파이터. 첫 틱에 <paramref name="start"/> 를 넣고
    /// 그 뒤로는 <b>아무것도 안 누른다</b> — 대시는 커밋이라 그래도 무적과 대시가 흐른다.
    /// </summary>
    private static Fighter Acting(InputFrame start, int ticks)
    {
        Fighter f = Spawn(_bossX + 100);
        f.Tick(start, _dt);
        for (int i = 1; i < ticks; i++)
        {
            f.Tick(default, _dt);
        }

        return f;
    }

    /// <summary>보스 발밑에서 오른쪽을 본다 — 파이터는 보스 오른쪽에 선다(Spawn(_bossX + …)).</summary>
    private static readonly Placement _at = new(_bossX, 0, 1);

    /// <summary>바닥에서 200 까지, 보스로부터 260 안쪽.</summary>
    private static HitBox Mid() => new(HitShape.Band(0, 260, 0, 200), 18);

    /// <summary>바닥에서 70 까지 — 점프로 넘는다.</summary>
    private static HitBox Low() => new(HitShape.Band(0, 520, 0, 70), 14);

    /// <summary>바닥에서 140 위 — 선 키(120)를 넘으므로 지상이 안전한 대공.</summary>
    private static HitBox High() => new(HitShape.Band(0, 300, 140, 420), 20);

    /// <summary>
    /// 안쪽 190px 이 비어 있는 판정 (옛 끌기의 마무리가 이 모양이었다 — 실제 데이터에서는 3타의 초승달 안쪽이 이런 빈 곳이다).
    /// <b>이 박스가 있어야 "너무 가까워서 안 맞았다" 를 물어볼 수 있다</b> — 안쪽이 0 이면
    /// 그 갈래는 값으로 도달할 수 없는 자리라 테스트가 못 선다.
    /// </summary>
    private static HitBox Pocket() => new(HitShape.Band(190, 760, 0, 330), 14);

    private static Fighter Airborne(double x)
    {
        Fighter f = Spawn(x);
        f.Tick(new InputFrame(0, true, false, false), _dt);
        for (int i = 0; i < 20; i++)
        {
            f.Tick(default, _dt);
        }

        return f;
    }

    [Fact]
    public void 거리_밖이면_안_맞는다()
    {
        HitResolver.Resolve(Spawn(_bossX + 400), _at, Mid(), Tags()).ShouldBe(HitVerdict.MissedTooFar);
    }

    [Fact]
    public void 거리_안이면_맞는다()
    {
        HitResolver.Resolve(Spawn(_bossX + 100), _at, Mid(), Tags()).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 좌우_어느_쪽이든_같은_거리면_같다()
    {
        HitResolver.Resolve(Spawn(_bossX - 100), _at, Mid(), Tags())
            .ShouldBe(HitResolver.Resolve(Spawn(_bossX + 100), _at, Mid(), Tags()));
    }

    [Fact]
    public void 대시_무적이면_피한다()
    {
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, true, false), _dt);
        f.Invulnerable.ShouldBeTrue();

        HitResolver.Resolve(f, _at, Mid(), Tags()).ShouldBe(HitVerdict.Dodged);
    }

    [Fact]
    public void 낮은_판정은_점프로_넘는다()
    {
        Fighter f = Airborne(_bossX + 100);

        f.Y.ShouldBeGreaterThan(70);
        HitResolver.Resolve(f, _at, Low(), Tags()).ShouldBe(HitVerdict.MissedByHeight);
    }

    [Fact]
    public void 대공은_지상이_안전하고_공중이_위험하다()
    {
        HitResolver.Resolve(Spawn(_bossX + 100), _at, High(), Tags()).ShouldBe(HitVerdict.MissedByHeight);
        HitResolver.Resolve(Airborne(_bossX + 100), _at, High(), Tags()).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 안_맞은_이유를_거리와_높이로_나눠_말한다()
    {
        // 여기서 이유를 버리면 BattleSim 은 "그 순간 무슨 행동 중이었나" 로 추측할 수밖에 없다.
        // 그 추측이 실제로 틀렸다 — 점프로 넘긴 판정이 같이 눌러둔 패리(#168 에서 걷었다)의 공으로 기록됐다.
        HitResolver.Resolve(Spawn(_bossX + 600), _at, Low(), Tags()).ShouldBe(HitVerdict.MissedTooFar);
        HitResolver.Resolve(Airborne(_bossX + 100), _at, Low(), Tags()).ShouldBe(HitVerdict.MissedByHeight);
    }

    [Fact]
    public void 거리로_빗나간_것이_안인지_밖인지까지_말한다()
    {
        // 한 갈래(MissedByRange)였을 때는 **파고들어 피한 것과 도망쳐 피한 것이 같은 한 점**이었다.
        // 그 둘은 겨냥할 것이 정반대라(안쪽을 덮는 패턴 · 도주로를 덮는 패턴),
        // 계측이 못 가르면 2단계가 정반대 패턴을 뽑는다.
        HitResolver.Resolve(Spawn(_bossX + 100), _at, Pocket(), Tags())
            .ShouldBe(HitVerdict.MissedByGap);
        HitResolver.Resolve(Spawn(_bossX + 900), _at, Pocket(), Tags())
            .ShouldBe(HitVerdict.MissedTooFar);

        // 주머니와 사거리 사이는 그냥 맞는다 — 위 둘이 "거리면 무조건 빗나간다" 가 아니라는 증거다.
        HitResolver.Resolve(Spawn(_bossX + 400), _at, Pocket(), Tags()).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 거리가_먼저_걸리면_높이는_안_본다()
    {
        // 둘 다 어긋났을 때 무엇이라 말하는가. 거리를 먼저 보므로 거리로 답한다 —
        // 순서를 박아두지 않으면 같은 상황이 판마다 다른 라벨을 내 학습 데이터가 흔들린다.
        HitResolver.Resolve(Airborne(_bossX + 600), _at, Low(), Tags()).ShouldBe(HitVerdict.MissedTooFar);
    }

    // ── 패턴의 창이 실제로 문다 (이슈 #16) ─────────────────────────────────────
    //
    // 전에는 HitResolver 가 파이터의 창만 봤다. 패턴이 더 좁은 창(그때의 parry_window 0.10)을 선언해도
    // 파이터의 창이 그대로 이겨서 **그 숫자는 아무것도 안 했다** — 그런데 망은 그 숫자를 배운다.
    // 거짓말하는 숫자는 없는 숫자보다 나쁘다. 이제 유효 창은 **둘 중 좁은 쪽**이다. 패리의 창은 패리와 같이 걷었다(#168) — 대시가 남는다.

    [Fact]
    public void 패턴_창이_더_넓으면_파이터_창이_이긴다()
    {
        // 좁은 쪽이 이긴다는 것은 양방향이다. 패턴이 넉넉해도 파이터의 무적이 끝났으면
        // 무적이 아니다 — 안 그러면 패턴 태그가 캐릭터 차이를 지워 버린다.
        // 대시가 9틱 동안 330px 를 가므로 사거리가 넉넉한 띠로 댄다 — Mid() 면 거리로 빗나가 이 테스트가 창을 안 본다.
        InputFrame dash = new(0, false, true, false);
        Fighter late = Acting(dash, 9);   // 0.15 > 기준 파이터의 무적 0.14
        var wide = new HitBox(HitShape.Band(0, 2000, 0, 200), 18);

        late.Action.ShouldBe(FighterAction.Dash, "대시가 벌써 끝났다 — 이 테스트가 대시 안을 안 본다");
        late.Invulnerable.ShouldBeFalse();
        HitResolver.Resolve(late, _at, wide, Tags(dashWindow: 0.30)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 대시_무적도_패턴과_파이터_중_좁은_창을_따른다()
    {
        InputFrame dash = new(0, false, true, false);

        HitResolver.Resolve(Acting(dash, 1), _at, Mid(), Tags(dashWindow: 0.05))
            .ShouldBe(HitVerdict.Dodged);

        Fighter late = Acting(dash, 4);
        late.Invulnerable.ShouldBeTrue("파이터 무적은 아직 돌아야 이 테스트가 좁은 쪽을 본다");
        HitResolver.Resolve(late, _at, Mid(), Tags(dashWindow: 0.05)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void Dash_window_0_은_길이가_0_인_창이_아니라_대시_불가다()
    {
        // 두 해석이 값으로는 같은 곳에 떨어지지만 뜻이 다르다. DodgeEvent.DashAvailable 이
        // dash_window > 0 && 판정의 답(HitBox.Dashable · #78)으로 "대시가 가능했나" 를 싣고, 의존도 축의 분모가 그것이다 —
        // 0 을 "아주 짧은 창" 으로 읽으면 그 분모가 거짓이 된다.
        Fighter f = Acting(new InputFrame(0, false, true, false), 1);

        f.Invulnerable.ShouldBeTrue();
        HitResolver.Resolve(f, _at, Mid(), Tags(dashWindow: 0)).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 판정기는_상태를_안_바꾼다()
    {
        Fighter f = Spawn(_bossX + 100);
        int before = f.Health;

        HitResolver.Resolve(f, _at, Mid(), Tags());
        HitResolver.Resolve(f, _at, Mid(), Tags());

        f.Health.ShouldBe(before);
    }

    // ── 가드 (이슈 #47 · 설계 §5.2) ─────────────────────────────────────────────

    /// <summary>↓ 를 누르고 선 파이터 (설계 §5.2).</summary>
    private static Fighter Guarding()
    {
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, false, false, false, GuardHeld: true), _dt);

        f.Guarding.ShouldBeTrue("가드가 안 섰다 — 아래 테스트들이 전부 다른 갈래를 본다");
        return f;
    }

    [Fact]
    public void 막고_있으면_깎여서_막는다()
    {
        HitResolver.Resolve(Guarding(), _at, Mid(), Tags()).ShouldBe(HitVerdict.Guarded);
    }

    [Fact]
    public void 스태미나가_모자라면_방어가_깨진다()
    {
        Fighter f = Guarding();
        f.Spend(f.Stamina - 1);   // 1 남는다. Mid() 는 18피해라 32.4 가 든다

        HitResolver.Resolve(f, _at, Mid(), Tags()).ShouldBe(HitVerdict.GuardBroken);
    }

    [Fact]
    public void 방어는_거리와_높이보다_뒤다()
    {
        // 순서를 박아둔다. 안 닿은 판정까지 "막았다" 로 적으면 가드 개수가 실제로 막은 것보다
        // 부풀고, 그 개수가 곧 계측이다.
        var box = new HitBox(HitShape.Band(0, 10, 0, 200), 18);
        HitResolver.Resolve(Guarding(), _at, box, Tags()).ShouldBe(HitVerdict.MissedTooFar);
    }

    [Fact]
    public void 왼쪽을_보는_보스의_모양은_왼쪽을_친다()
    {
        // 앞으로만 치는 모양. 옛 띠는 좌우 대칭이라 "보는 쪽" 이 판정에 실리는지를 한 번도 드러낸 적이 없다.
        var box = new HitBox(new HitShape(new[] { new HitRect(50, 300, 0, 200) }), 18);
        var facingLeft = new Placement(_bossX, 0, -1);

        HitResolver.Resolve(Spawn(_bossX - 150), facingLeft, box, Tags()).ShouldBe(HitVerdict.Hit);
        HitResolver.Resolve(Spawn(_bossX + 150), facingLeft, box, Tags())
            .ShouldBe(HitVerdict.MissedTooFar, "등 뒤를 쳤다 — 보는 쪽이 판정에 안 실렸다");
    }

    [Fact]
    public void 몸통이_닿으면_중심이_밖이어도_맞는다()
    {
        // 띠 [0, 260] · 파이터 중심 280 — 옛 판정(몸을 점으로 봤다)은 빗나감이었다. 몸 왼끝이 250 이라 닿는다.
        HitResolver.Resolve(Spawn(_bossX + 280), _at, Mid(), Tags()).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 모양_안의_구멍에_뜬_몸은_높이가_아니라_틈으로_빗나간다()
    {
        // 설계 §3.6 ② — 가로 → 세로 → 틈 순서이고, 세로가 틈보다 먼저인 것은 몸이 모양 **전체**의 위나 아래일 때뿐이다.
        // 초승달 구멍 안에 뜬 몸은 틈이다. 두 장 사이(높이 50 ~ 200)가 빈 모양이고, 네 틱 뛴 몸(56 ~ 176)이 그 사이에 든다.
        var hollow = new HitBox(new HitShape(new[] { new HitRect(0, 300, 0, 50), new HitRect(0, 300, 200, 400) }), 18);
        Fighter f = Spawn(_bossX + 100);
        f.Tick(new InputFrame(0, true, false, false), _dt);
        for (int i = 0; i < 3; i++)
        {
            f.Tick(default, _dt);
        }

        f.Y.ShouldBeGreaterThan(50, "몸이 아래 장에 닿는다 — 이 테스트가 구멍을 안 본다");
        (f.Y + f.BodyHeight).ShouldBeLessThan(200, "몸이 위 장에 닿는다 — 이 테스트가 구멍을 안 본다");
        HitResolver.Resolve(f, _at, hollow, Tags()).ShouldBe(HitVerdict.MissedByGap);
        HitResolver.Resolve(Spawn(_bossX + 100), _at, hollow, Tags()).ShouldBe(HitVerdict.Hit, "땅의 몸이 아래 장에 안 닿았다");
    }

    [Fact]
    public void 실효_방어는_판정의_태그와_파이터의_창을_같이_본다()
    {
        // 설계 §6.1 — 판정 보기가 칠하는 색이 곧 판정이 고르는 갈래다(한 함수). 태그가 없으면(산 판정이 없으면) 파이터 쪽 그대로다.
        Fighter dashing = Acting(new InputFrame(0, false, true, false), 1);
        HitResolver.Effective(dashing, null).ShouldBe(Defense.Invulnerable);
        HitResolver.Effective(dashing, Tags(dashWindow: 0)).ShouldBe(Defense.None, "대시 불가 판정 앞의 무적을 칠한다");

        HitResolver.Effective(Guarding(), null).ShouldBe(Defense.Guarding);
        HitResolver.Effective(Guarding(), Tags(dashWindow: 0)).ShouldBe(Defense.Guarding, "대시를 못 받는 판정도 가드로는 막는다");
    }

    [Fact]
    public void 대시를_안_받는_판정은_무적_창_안이어도_맞는다()
    {
        // 설계 §7.3 · §4.7 — 판정의 답(dash: false)은 패턴 태그를 좁힌다. 태그로는 대시 창이 넓게(0.18) 열려 있고 파이터는 무적 창
        // 한가운데지만 이 판정만은 맨몸이다 — 잡기가 "대시중에도 잡히는 공격" 인 자리다. 같은 몸에 답이 없는 판정은 무적이 먹는다.
        Fighter f = Acting(new InputFrame(0, false, true, false), 3);
        f.Invulnerable.ShouldBeTrue("무적 창 안이 아니다 — 이 테스트가 답을 안 본다");

        HitResolver.Resolve(f, _at, Mid(), Tags()).ShouldBe(HitVerdict.Dodged);
        HitResolver.Resolve(f, _at, Mid() with { Dashable = false }, Tags()).ShouldBe(HitVerdict.Hit);
    }

    [Fact]
    public void 가드를_안_받는_판정은_가드_중에도_맨몸이다()
    {
        // 설계 §5.2 · §7.3 — 가드로 못 막는 판정은 가드를 깨는 것이 아니라 가드를 **안 본다**: 붕괴(전액 + 탈진)가 아니라 맨몸에 떨어진다.
        // 스태미나가 모자라 깨질 몸이어도 맨몸이다 — 옛 guard_break(빨간 마무리)가 흉내 내던 것과 다른 결과다(설계 §8 「지우는 것」).
        HitResolver.Resolve(Guarding(), _at, Mid() with { Guardable = false }, Tags()).ShouldBe(HitVerdict.Hit);

        Fighter spent = Guarding();
        spent.Spend(spent.Stamina - 1);
        HitResolver.Resolve(spent, _at, Mid() with { Guardable = false }, Tags())
            .ShouldBe(HitVerdict.Hit, "가드를 안 받는 판정이 모자란 스태미나로 붕괴를 냈다 — 가드를 봤다");
    }

    [Fact]
    public void 붙드는_판정이_맨몸에_닿으면_잡힘이다()
    {
        // 설계 §4.7 — 붙드는 판정(grab_hold_seconds > 0)이 닿은 결과는 맞음이 아니라 잡힘이다: 뷰가 흰 구를 붙이고 로그와 계측이 "잡혔다" 를
        // 따로 센다. 가르는 것은 판정의 깃발과 결과다 — 패턴 이름이 아니다(CLAUDE.md §2). 안 닿으면 전처럼 빗나감이고, 답이 받는 수단
        // (여기서는 대시)이면 전처럼 그 수단이 먹는다.
        HitBox grab = Mid() with { GrabHoldSeconds = 1.0 };
        HitResolver.Resolve(Spawn(_bossX + 100), _at, grab, Tags()).ShouldBe(HitVerdict.Grabbed);
        HitResolver.Resolve(Spawn(_bossX + 400), _at, grab, Tags()).ShouldBe(HitVerdict.MissedTooFar);
        HitResolver.Resolve(Acting(new InputFrame(0, false, true, false), 2), _at, grab, Tags())
            .ShouldBe(HitVerdict.Dodged, "대시를 받는 붙드는 판정에 무적이 안 먹었다");
    }

    [Fact]
    public void 실효_방어는_판정의_답도_본다()
    {
        // 설계 §6.1 — 5번 PR 부터 몸통 색은 판정 단위의 답으로 칠한다. 잡기 앞의 무적 · 가드가 "무적" · "가드" 색이면 그 색이 거짓말한다.
        // 답을 모르는 자리(판정 없이 태그만)는 전처럼 태그와 파이터의 창만 본다.
        Fighter dashing = Acting(new InputFrame(0, false, true, false), 1);
        HitResolver.Effective(dashing, Tags(), Mid()).ShouldBe(Defense.Invulnerable);
        HitResolver.Effective(dashing, Tags(), Mid() with { Dashable = false }).ShouldBe(Defense.None, "대시를 안 받는 판정 앞의 무적을 칠한다");

        HitResolver.Effective(Guarding(), Tags(), Mid() with { Guardable = false })
            .ShouldBe(Defense.None, "가드를 안 받는 판정 앞의 가드를 칠한다");
        HitResolver.Effective(Guarding(), Tags(), Mid()).ShouldBe(Defense.Guarding);
    }

    [Fact]
    public void 실제_3타는_초승달_안쪽만_틈이고_땅의_등_뒤와_앞끝_너머는_거리다()
    {
        // 설계 §2 — attack3 f2 의 궤적은 땅에서 앞만 친다: 보스 중심 +80 안(초승달 안쪽)과 등 뒤는 땅에 선 사람에게 안전하다
        // (등 뒤 궤적은 높이 412.5 위에만 있다). 셋 다 모양의 외곽 상자 안이지만 이유가 다르다(#72 · ShapeHit.Test):
        //   · +40 — 보스 중심과 앞 궤적 사이, 품 안이다. 틈이다(설계 §3.6 ② · §7.1).
        //   · −150 — 몸 높이에 등 뒤를 치는 궤적이 없고 몸이 보스 중심 뒤다. 보스를 돌아 나간 것이라 거리다.
        //   · +420 — 몸 높이(0 ~ 120)의 궤적은 +374 에서 끝나고 그 위의 궤적만 +418 까지 뻗는다. 몸 왼끝(+390)이 그 너머라 거리다.
        // 외곽 상자만 보면 셋 다 틈이고, 거리 축이 −150 · +420 을 "보스에 붙었다" 로 읽는다. 궤적 한가운데는 맞는다.
        var third = new HitBox(TestConfigs.HitShapes()["medieval_king/attack3/2"], 14);

        HitResolver.Resolve(Spawn(_bossX + 40), _at, third, Tags()).ShouldBe(HitVerdict.MissedByGap, "초승달 안쪽이 틈이 아니다");
        HitResolver.Resolve(Spawn(_bossX - 150), _at, third, Tags()).ShouldBe(HitVerdict.MissedTooFar, "땅의 등 뒤가 거리가 아니다");
        HitResolver.Resolve(Spawn(_bossX + 420), _at, third, Tags()).ShouldBe(HitVerdict.MissedTooFar, "앞끝 너머가 거리가 아니다");
        HitResolver.Resolve(Spawn(_bossX + 200), _at, third, Tags()).ShouldBe(HitVerdict.Hit, "궤적 한가운데가 안 맞는다");
    }
}
