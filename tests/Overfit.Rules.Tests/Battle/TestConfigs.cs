using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 테스트가 같이 쓰는 설정. <b>수치는 여기 없다</b> — <c>overfit/data/*.json</c> 에서 읽는다.
/// csproj 가 그 파일들을 출력 폴더의 <c>data/</c> 로 복사한다.
///
/// <para>
/// 파이터만 리터럴이다. 이것은 특정 캐릭터가 아니라 <b>기준값</b>이라, fighters.json 의 캐릭터를
/// 그대로 쓰면 그 캐릭터의 밸런스를 고칠 때마다 무관한 테스트가 같이 빨개진다. 캐릭터가 하나가 된
/// 뒤에도(이슈 #38) 마찬가지다 — 공격 타이밍 하나를 그림에 맞추려고 패리 틱 수를 세는 테스트가
/// 같이 움직이면, 그 테스트들은 더 이상 자기가 말하는 것을 말하지 않는다.
/// 보스·아레나·상한은 반대다 — 실제 전투가 쓰는 바로 그 값이어야 게임·데모·골든이 같은 판을 말한다.
/// </para>
/// </summary>
public static class TestConfigs
{
    /// <summary>
    /// 기준 파이터. 경직 넷(<paramref name="firstStiff"/> · <paramref name="secondStiff"/> · <paramref name="dashRecover"/> ·
    /// <paramref name="parryStiff"/>)은 <b>경직의 길이를 규칙이 데이터에서 읽는지 보는 테스트</b>만 준다(#82 · FighterStiffTests) — 보스의
    /// <c>exhaustSeconds</c> 와 같은 자리다. 0 을 주면 경직이 없는 파이터다: 경직이 없던 때와 견줘 "정확히 그만큼 늦다" 를 재는 대조군이다.
    /// </summary>
    public static FighterConfig Fighter(
        double? firstStiff = null, double? secondStiff = null, double? dashRecover = null, double? parryStiff = null)
    {
        return new FighterConfig
        {
            MoveSpeed = 420,
            JumpVelocity = 940,
            MaxHealth = 100,
            MaxStamina = 100,
            HalfWidth = 30,
            Height = 120,
            DashSpeed = 2200,
            DashDuration = 0.18,
            DashIFrames = 0.14,
            DashCost = 25,
            // 경직 셋(대시 뒤 · 1타 뒤 · 2타 뒤)은 **실제 값 그대로**다 (#82) — 유저가 손맛으로 고른 값이고, 기준 파이터의 칼질(0.28 · 0.6초)이
            // 실제와 달라도 "한 번만 친 사람이 선다 · 2연격 뒤가 가장 길다 · 대시 뒤는 살짝" 의 모양이 이 값에서 나온다. 0.10 · 0.40 · 0.50 은
            // 6 · 24 · 30틱으로 딱 떨어져 테스트가 세는 틱이 반올림에 안 걸린다.
            DashRecover = dashRecover ?? 0.10,
            // 패리는 **실제 값 그대로**다 (설계 §5.3) — 가드 셋과 같이 캐릭터 성능이 아니라 조작의 정의다.
            ParryPreciseWindow = 0.133,
            ParryDuration = 0.3333,
            // 패리 뒤 경직도 실제 값 그대로다 (#82) — 0.25 = 15틱으로 딱 떨어진다. 헛친 패리 한 번이 커밋 20틱 + 경직 15틱이다.
            ParryStiff = parryStiff ?? 0.25,
            ParryCost = 15,
            ParryAnim = "attack2",
            ParryAnimFps = 12,
            ParryAnimFrames = 4,
            // 기준 파이터에는 그림이 없지만 칼질 한 칸의 관계는 진짜여야 한다 — 50fps · 14장이면 재생 0.28초로
            // 셋의 합과 같고, 0번에서 시작해 4번 장(0.08초)이 선딜의 끝이다. 거짓 값을 넣으면 이 픽스처가
            // "그림에서 거꾸로 정한다" 는 규칙의 반례가 된다.
            Combo = new List<ComboStepDef>
            {
                new()
                {
                    Anim = "attack", Fps = 50, Frames = 14, StartFrame = 0, BladeFrame = 4,
                    Windup = 0.08, Active = 0.06, Recover = 0.14, Stiff = firstStiff ?? 0.40,
                    Damage = 8, Hitbox = TestSwordId, Poise = 10,
                },
                // 2타 — 기준값이라 짧다(실제는 1.0초). 여기서 진짜여야 하는 것은 **모양**이다: 1타보다 선딜이 길고 더 아프다.
                // 20fps · 12장이면 재생 0.6초로 셋의 합과 같고, 0번에서 시작해 6번 장(0.3초)이 선딜의 끝이다.
                // 칼은 1타와 같은 기준 사각형이다 — 기준 파이터는 그림이 없다.
                // 경직도(10 · 45)는 **실제 값 그대로**다 (#71) — 보스의 게이지(실제 bosses.json)와 짝이라, 여기서 다르면 테스트가 말하는
                // "두 번째 2타에 무너진다" 가 게임의 것이 아니게 된다.
                new()
                {
                    Anim = "attack2", Fps = 20, Frames = 12, StartFrame = 0, BladeFrame = 6,
                    Windup = 0.3, Active = 0.1, Recover = 0.2, Stiff = secondStiff ?? 0.50,
                    Damage = 24, Hitbox = TestSwordId, Poise = 45,
                },
            },
            AttackCost = 12,
            // 가드 셋은 **실제 값 그대로**다 (이슈 #47). 패리 창과 같은 자리라 캐릭터 성능이 아니라
            // **조작의 정의**이고, 여기서 다른 값을 쓰면 테스트가 말하는 "가드" 가 게임의 가드가 아니게 된다.
            GuardChipRatio = 0.25,
            GuardStaminaPerDamage = 1.8,
            ExhaustSeconds = 1.1,
            // 가드의 그림은 규칙이 안 읽는다 — 패리의 그림 셋과 같이 실제 값을 둔다.
            GuardAnim = "attack2",
            GuardFrame = 1,
            StaminaRegen = 40,
            // 폭탄은 **실제 값 그대로**다 (설계 2026-09-30 조각2 §1.4) — 가드 · 패리와 같이 캐릭터 성능이 아니라 조작의 정의다. 보스가 끊어야 하는
            // 틱(누른 틱 + 88)이 선딜 90틱에서 나오므로, 여기서 다르면 테스트가 말하는 "끊긴다" 가 게임의 것이 아니게 된다. 90 · 15 · 30틱.
            Bomb = new BombDef
            {
                Count = 10,
                ThrowSeconds = 1.5,
                RecoverSeconds = 0.25,
                FlightSeconds = 0.5,
                Damage = 60,
                Anim = "attack",
                WindupFrame = 1,
                ReleaseFrame = 4,
            },
            Sprite = "test_unit",
        };
    }

    /// <summary>
    /// 칼질 한 칸을 베끼며 몇 값만 바꾼다. <see cref="ComboStepDef"/> 는 record 가 아니라 <c>with</c> 가 없다 — 테스트마다 열두 줄을 옮겨 적으면
    /// 칸에 키가 느는 날 베낀 곳마다 따라 고쳐야 하고, 하나를 빠뜨리면 그 테스트만 다른 칼질을 잰다. 그래서 베끼는 곳은 여기 하나다(#96 —
    /// 전에는 BossExhaustTests · BossMotionTests · Stage2BattleTests(지금의 MoveBattleTests)가 <c>Breaker</c> 를, BossPoiseTests · SwordTests 가 제 사본을 들고 있었다).
    /// </summary>
    public static ComboStepDef Step(ComboStepDef s, double? stiff = null, int? poise = null, string? hitbox = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ComboStepDef
        {
            Anim = s.Anim,
            Fps = s.Fps,
            Frames = s.Frames,
            StartFrame = s.StartFrame,
            BladeFrame = s.BladeFrame,
            Windup = s.Windup,
            Active = s.Active,
            Recover = s.Recover,
            Stiff = stiff ?? s.Stiff,
            Damage = s.Damage,
            Poise = poise ?? s.Poise,
            Hitbox = hitbox ?? s.Hitbox,
        };
    }

    /// <summary>
    /// 1타 한 대로 보스의 경직 게이지가 끝까지 차는 기준 파이터 — 무너지는 순간을 한 틱으로 만든다. 1타의 경직도만 실제 보스의 끝
    /// (<c>bosses.json</c> 의 <c>poise_max</c>)이고 나머지는 기준 파이터 그대로다.
    /// </summary>
    public static FighterConfig Breaker()
    {
        FighterConfig c = Fighter();
        c.Combo[0] = Step(c.Combo[0], poise: (int)Math.Ceiling(Boss().PoiseMax));
        return c;
    }

    /// <summary>기준 파이터의 칼 id. <c>hitboxes.json</c> 에는 없다 — <see cref="HitShapes"/> 가 더해 준다.</summary>
    public const string TestSwordId = "test/sword";

    /// <summary>
    /// 기준 파이터의 칼 — 옛 사거리 90 을 높이 300 으로 막은 사각형 하나(좌우 대칭). <b>그림에서 안 뽑는다</b>:
    /// 기준 파이터가 실제 그림의 모양을 쓰면 <c>hitboxes.json</c> 을 다시 뽑는 날(흰색 기준 하나만 바꿔도) 칼질을
    /// 세는 모든 테스트와 골든이 같이 움직인다 — 이 파일 머리의 "기준값" 과 같은 이유다.
    /// 보스 몸통(키 297) 앞에서 옛 판정(|dx| − 보스 반폭 ≤ 90)과 가로가 정확히 같아, 칼을 모양으로 옮긴 것이
    /// 기준 파이터의 판을 바꾸지 않는다.
    /// </summary>
    public static HitShape TestSword() => new(new[] { new HitRect(-90, 90, 0, 300) });

    /// <summary>실제 <c>hitboxes.json</c> 에 기준 파이터의 칼을 더한 표. 판을 세울 때 이것을 넘긴다.</summary>
    public static Dictionary<string, HitShape> HitShapes()
    {
        Dictionary<string, HitShape> shapes = HitShapeTable.Parse(
            File.ReadAllText(Path.Combine("data", "hitboxes.json")), "hitboxes.json");
        shapes[TestSwordId] = TestSword();
        return shapes;
    }

    /// <summary>
    /// 2연격의 마지막 칼이 닿기까지(초) — 앞 칼질 전부 + 마지막 칼질의 선딜 + 판정. 2타는 1타가 끝나는 틱에
    /// 이어진다(설계 §5.1). 받아친 뒤의 탈진이 이것을 담아야 "받아쳤다 → 2연격" 이 한 동작이 된다.
    /// </summary>
    public static double ComboLead(FighterConfig c)
    {
        double lead = 0;
        for (int i = 0; i < c.Combo.Count - 1; i++)
        {
            lead += c.Combo[i].Windup + c.Combo[i].Active + c.Combo[i].Recover;
        }

        return lead + c.Combo[^1].Windup + c.Combo[^1].Active;
    }

    /// <summary>
    /// 받아친 틱부터 되받아치기 2연격의 마지막 칼이 닿기까지(초). 받아친 패리의 커밋 안에서 누른 J 는 곧장 1타다
    /// (<c>Fighter.Begin</c> 의 되받아치기 · 판정 13) — 커밋이 끝나기를 안 기다린다. 가장 이른 J 는 받아친
    /// <b>다음 틱</b>이라 <see cref="BattleSim.Dt"/> 하나를 더한다: <c>BattleSim</c> 은 판정(과 탈진)을 파이터의 틱
    /// 뒤에 내므로 받아친 그 틱의 J 는 이미 지나갔다. <see cref="ComboLead"/> 만 쓰면 받아친 그 틱에 누른 J 를 세는
    /// 셈이고, 그 J 는 규칙이 못 받는다. 사람의 반응은 여기 안 넣는다 — 그 여유는 BossDataTests 가 따로 잰다.
    /// </summary>
    public static double CounterLead(FighterConfig c) => BattleSim.Dt + ComboLead(c);

    /// <summary>시험 패턴 <see cref="Sweep"/> 의 id.</summary>
    public const string SweepId = "쓸기";

    /// <summary>
    /// 판정 하나짜리 시험 패턴 — 0.5초에 <paramref name="maxDistance"/> 까지 · 높이 0~5000 을 친다
    /// (이슈 #59 · 판정 창). 대시 창을 넓게(1초) 두는 것은 무적의 길이를 <b>파이터 쪽</b>(0.14초)이
    /// 정하게 하기 위해서다. 패리는 안 된다 — 창을 재는 테스트가 패리 갈래로 새지 않게.
    /// </summary>
    public static PatternDef Sweep(double maxDistance, double activeSeconds, double endAt = 2.0) => new()
    {
        Tags = new PatternTags
        {
            DashWindow = 1.0,
            DashDirection = "either",
            Jumpable = false,
            AntiAir = false,
            Parryable = false,
            ParryWindow = 0,
            PunishGreed = false,
            Reach = "far",
            MultiHit = 1,
            Tracking = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0.0, Kind = "windup" },
            new()
            {
                T = 0.5, Kind = "active", Band = new[] { 0.0, maxDistance, 0.0, 5000.0 },
                Damage = 7, ActiveSeconds = activeSeconds,
            },
            new() { T = endAt, Kind = "end" },
        },
    };

    /// <summary>
    /// 보스가 서서 <see cref="Sweep"/> 만 휘두르는 판. 보스는 안 움직이고 안 죽는다. 간격 0.2초(12틱)라 첫 판정은 판의
    /// 12 + 30 = 42틱에 선다. <paramref name="maxTicks"/> 는 판을 창 한가운데서 끝내 보는 테스트만 준다.
    /// </summary>
    public static BattleSim SweepSim(double maxDistance, double activeSeconds, double endAt = 2.0, int maxTicks = 60 * 30) =>
        PatternSim(SweepId, Sweep(maxDistance, activeSeconds, endAt), maxTicks: maxTicks);

    /// <summary>
    /// 보스가 서서 패턴 하나(<paramref name="pattern"/>)만 되풀이하는 판 — 보스는 안 움직이고(쉬는 동안 제자리 · 달리지 않는다) 안 죽는다(체력 999_999) · 간격 0.2초(12틱)
    /// · 시드 1. 시험 패턴을 손으로 지어 규칙 하나를 재는 테스트들이 이 차림을 테스트마다 옮겨 적었다(#96). 파이터 · 모양 표 · 상한 · 움직임
    /// 등록표는 그것을 바꿔야 하는 테스트만 준다 — 없으면 기준 파이터 · 실제 모양 표 · 10초 · 실제 등록표다.
    /// </summary>
    public static BattleSim PatternSim(
        string id,
        PatternDef pattern,
        FighterConfig? fighter = null,
        IReadOnlyDictionary<string, HitShape>? shapes = null,
        int maxTicks = 60 * 10,
        Func<MotionDef, MotionBounds, IBossMotion?>? motions = null) =>
        new(new BattleSetup
        {
            Arena = Arena(),
            Fighter = fighter ?? Fighter(),
            HitShapes = shapes ?? HitShapes(),
            Boss = Boss(maxHealth: 999_999, rest: 0.2),
            PatternIds = new[] { id },
            Patterns = new Dictionary<string, PatternDef> { [id] = pattern },
            Seed = 1,
            MaxTicks = maxTicks,
            Motions = motions,
        });

    /// <summary>패턴이 설 때까지(선딜이 시작될 때까지) 민다 — <c>NextActiveIn</c> 이 null 이 아니게 되는 틱이다.</summary>
    public static void UntilWindup(BattleSim sim)
    {
        for (int i = 0; i < 600 && sim.NextActiveIn is null; i++)
        {
            sim.Tick(default);
        }

        sim.NextActiveIn.ShouldNotBeNull("600틱 안에 패턴이 안 섰다 — 시험 패턴이 안 돈다");
    }

    /// <summary>
    /// 판정이 두 틱 앞으로 다가올 때까지 민다 — 이 호출이 끝나면 두 번째 틱에 판정이 선다.
    ///
    /// <para>
    /// 러너가 틱을 세므로(설계 §3.6 ⑤) <c>NextActiveIn</c> 은 남은 틱 × 1/60 그대로다. 1/60 을 더해 가던 때는
    /// 30번 더한 값이 0.49999999999999994 라 0.5초 판정이 31번째 틱에 서서, 남은 시간으로 선 틱을 못 맞혔다.
    /// </para>
    /// </summary>
    public static void UntilNear(BattleSim sim)
    {
        UntilWindup(sim);
        for (int i = 0; i < 600 && sim.NextActiveIn is { } left && left > 2 * BattleSim.Dt; i++)
        {
            sim.Tick(default);
        }

        sim.NextActiveIn.ShouldNotBeNull("판정이 두 틱 앞으로 오기 전에 섰다");
        sim.NextActiveIn.Value.ShouldBeLessThanOrEqualTo(2 * BattleSim.Dt, "600틱 안에 판정이 두 틱 앞으로 안 왔다");
    }

    /// <summary>
    /// 판의 <paramref name="tick"/> 틱까지(그 틱 포함) <paramref name="input"/> 을 넣으며 민다. 상한을 두고, 끝나면 그 틱에 섰는지 단언한다 —
    /// 판은 결과가 난 뒤에도 틱을 받아서, 묶지 않거나 빠져나온 까닭을 안 보는 기다림은 규칙이 깨진 날 실패하지 않고 게이트를 멈춰 세우거나
    /// 엉뚱한 틱을 잰다(#71 계획의 결정 27 · #96).
    /// </summary>
    public static void UntilTick(BattleSim sim, int tick, InputFrame input = default)
    {
        ArgumentNullException.ThrowIfNull(sim);
        for (int i = 0; i < 60 * 60 && sim.Ticks < tick; i++)
        {
            sim.Tick(input);
        }

        sim.Ticks.ShouldBe(tick, $"{tick}틱까지 못 밀었다");
    }

    /// <summary>
    /// 판정이 <b>선 틱</b>까지 민다 — 이 호출이 끝난 순간 판정은 방금 섰다. 러너와 같은 술어로 잰다:
    /// 판정이 서면 그 단계는 더 이상 "다음 판정" 이 아니므로 <c>NextActiveIn</c> 이 null 이 된다
    /// (<see cref="Sweep"/> 은 판정이 하나뿐이다).
    /// </summary>
    public static void UntilFired(BattleSim sim)
    {
        UntilWindup(sim);
        for (int i = 0; i < 600 && sim.NextActiveIn is not null; i++)
        {
            sim.Tick(default);
        }

        sim.NextActiveIn.ShouldBeNull("600틱 안에 판정이 안 섰다");
    }

    /// <summary>실제 <c>fighters.json</c>. 캐릭터별 수치를 봐야 하는 가드가 쓴다.</summary>
    public static Dictionary<string, FighterConfig> Fighters() => Table<FighterConfig>("fighters.json");

    public static Dictionary<string, BossConfig> Bosses() => Table<BossConfig>("bosses.json");

    public static Dictionary<string, PatternDef> Patterns() => Table<PatternDef>("patterns.json");

    public static Dictionary<string, StageDef> Stages() => Table<StageDef>("stages.json");

    /// <summary>실제 <c>tools/factory/fleet.json</c> — 봇 함대의 성향 범위(#104). 게임 데이터가 아니라 실험의 조건이라 <c>data/</c> 밖에 있다.</summary>
    public static FleetConfig Fleet() => JsonData<FleetConfig>.ParseOne(File.ReadAllText("fleet.json"), "fleet.json");

    public static BalanceData Balance() =>
        JsonData<BalanceData>.ParseOne(File.ReadAllText(Path.Combine("data", "balance.json")), "balance.json");

    /// <summary>실제 아레나. 폭은 balance.json 이 정한다 — 테스트마다 1920 을 베껴 적지 않는다.</summary>
    public static Arena Arena() => new(Balance().Battle.ArenaWidth);

    /// <summary>한 판의 상한. 실제 전투가 쓰는 값이다.</summary>
    public static int MaxTicks() => Balance().Battle.MaxTicks;

    /// <summary>
    /// 실제 단계의 명부와 고르기 — 게임 · 데모와 같은 자리(<see cref="StageRoster.Setup"/>)에서 실제 동작 정의 · 실제 보스의 쉬는 길이 · 실제
    /// <c>picker</c> 수치로 세운다(설계 2026-09-29 조각1 §4.1). 테스트마다 넷을 베껴 넘기면 한 곳이 실제와 갈린다.
    /// </summary>
    public static StageSetup Stage(ulong seed, IReadOnlyList<ScriptPlan>? script = null, int stage = 1) =>
        StageRoster.Setup(
            Stages(), stage, seed, Array.Empty<AttemptRecord>(), Patterns(), BattleSim.RestTicks(Boss()), Balance().Picker, script)
        ?? throw new InvalidOperationException($"stages.json 의 {stage}단계가 안 선다");

    /// <summary>
    /// 실제 보스. <paramref name="maxHealth"/> · <paramref name="rest"/> · <paramref name="exhaustSeconds"/> · <paramref name="runSpeed"/> ·
    /// <paramref name="runStop"/> 는 <b>일부러 이상한 값을 넣어야 하는 테스트</b>만 준다 (체력 999_999 로 시간 초과를 만든다 · 쉬기를 하나로 굳혀
    /// 동작이 서는 틱을 센다 · 탈진 길이를 바꿔 그 길이를 규칙에게서 읽는지 본다 · 달리기를 늦추거나 멈춤을 줄여 상한과 경계를 밟는다). 반폭은
    /// 절대 안 받는다 — 몸 충돌 간격이 그 값을 쓰므로 여기서 갈리면 테스트가 실제 전투와 다른 자리를 재게 된다. 옛 <c>moveSpeed</c>(쉬는 동안 걷는
    /// 빠르기)는 걷기와 같이 걷었다 — 보스는 쉬는 동안 제자리다(설계 2026-09-29 조각1 §5.1).
    /// </summary>
    /// <param name="maxHealth">체력.</param>
    /// <param name="rest">
    /// 쉬는 길이 하나(초) — 주면 <c>rest_seconds</c> 가 이것 하나다(설계 2026-09-29 조각1 §3.4 — 실제는 셋 중 하나를 계획마다 고른다). 옛 이름
    /// <c>patternGap</c> 이 받던 자리다.
    /// </param>
    /// <param name="exhaustSeconds">탈진 길이.</param>
    /// <param name="runSpeed">달리기의 빠르기(px/s).</param>
    /// <param name="runStop">달리기가 멈추는 파이터 앞 거리(px).</param>
    /// <param name="reaction">폭탄 반응(설계 2026-09-30 조각2 §2.4) — 지연 · 멈칫 · 동작을 바꿔 그 값을 규칙이 데이터에서 읽는지 보거나, 반응의 동작을 긴 것으로
    /// 바꿔 버린 계획의 캔슬이 그 동작을 끊지 않는지 보거나, 지연을 한 판보다 길게 두어 던지기를 끝내 모르는 보스를 세운다(<c>BombTests</c>).</param>
    public static BossConfig Boss(
        int? maxHealth = null, double? rest = null, double? exhaustSeconds = null, double? runSpeed = null, double? runStop = null,
        BombReactionDef? reaction = null)
    {
        BalanceData balance = Balance();
        BossConfig data = Bosses()[balance.Battle.Boss];
        return new BossConfig
        {
            MaxHealth = maxHealth ?? data.MaxHealth,
            RunSpeed = runSpeed ?? data.RunSpeed,
            RunStop = runStop ?? data.RunStop,
            RunMaxSeconds = data.RunMaxSeconds,
            HalfWidth = data.HalfWidth,
            Height = data.Height,
            RestSeconds = rest is { } r ? new[] { r } : data.RestSeconds,
            ExhaustSeconds = exhaustSeconds ?? data.ExhaustSeconds,
            PoiseMax = data.PoiseMax,
            PoiseDecayDelay = data.PoiseDecayDelay,
            PoiseDecayPerSecond = data.PoiseDecayPerSecond,
            BombReaction = reaction ?? data.BombReaction,

            // 작은 체력(문턱 이하)을 쓰는 테스트는 한 형태로 둔다 — 문턱이 최대 체력 위면 판이 세울 때 거절한다(BossForms).
            Forms = maxHealth is { } m && data.Forms.Thresholds.Count > 0 && m <= data.Forms.Thresholds[0]
                ? new FormsDef { Thresholds = [], ShiftSeconds = data.Forms.ShiftSeconds }
                : data.Forms,
            Sprite = data.Sprite,
        };
    }

    /// <summary>
    /// 팩 <c>.tres</c> 의 애니메이션 → 장 수. 장은 <c>AtlasTexture</c> sub_resource 의 id(<c>이름_번호</c>)로 센다 —
    /// <c>tools/install_assets.py</c> 가 그렇게 짓는다. 이름은 애니메이션 목록의 <c>"name": &amp;"…"</c> 에서 온다.
    /// 데이터가 가리키는 그림(보스 단계의 <c>anim</c> · <c>frame</c> · 파이터 가드의 <c>guard_anim</c> · <c>guard_frame</c>)을 팩과 대 볼 때 쓴다 —
    /// <c>JsonData</c> 는 모르는 키를 조용히 버리므로 오타가 빌드를 그냥 지나간다.
    /// </summary>
    public static Dictionary<string, int> PackFrames(string sprite)
    {
        string text = File.ReadAllText(Path.Combine("spriteframes", $"{sprite}.tres"));
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, "\\[sub_resource type=\"AtlasTexture\" id=\"(.+)_(\\d+)\"\\]"))
        {
            string name = m.Groups[1].Value;
            int frame = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            counts[name] = Math.Max(counts.GetValueOrDefault(name), frame + 1);
        }

        return Regex.Matches(text, "\"name\": &\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToDictionary(name => name, name => counts.GetValueOrDefault(name), StringComparer.Ordinal);
    }

    private static Dictionary<string, T> Table<T>(string name) =>
        JsonData<T>.ParseTable(File.ReadAllText(Path.Combine("data", name)), name);
}
