using System.Collections.Generic;
using System.IO;
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
    public static FighterConfig Fighter() => new()
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
        ParryPreciseWindow = 0.133,
        ParryMemoryWindow = 0.5,
        ParrySpamWindow = 0.1,
        ParryCost = 15,
        // 기준 파이터에는 그림이 없지만 칼질 한 칸의 관계는 진짜여야 한다 — 50fps · 14장이면 재생 0.28초로
        // 셋의 합과 같고, 0번에서 시작해 4번 장(0.08초)이 선딜의 끝이다. 거짓 값을 넣으면 이 픽스처가
        // "그림에서 거꾸로 정한다" 는 규칙의 반례가 된다.
        Combo = new List<ComboStepDef>
        {
            new()
            {
                Anim = "attack", Fps = 50, Frames = 14, StartFrame = 0, BladeFrame = 4,
                Windup = 0.08, Active = 0.06, Recover = 0.14, Damage = 8, Hitbox = TestSwordId,
            },
        },
        AttackCost = 12,
        // 가드 셋은 **실제 값 그대로**다 (이슈 #47). 패리 창과 같은 자리라 캐릭터 성능이 아니라
        // **조작의 정의**이고, 여기서 다른 값을 쓰면 테스트가 말하는 "가드" 가 게임의 가드가 아니게 된다.
        GuardChipRatio = 0.25,
        GuardStaminaPerDamage = 1.8,
        GuardBreakLock = 1.1,
        // 기준값이라 **짧다.** 실제 캐릭터는 0 / 0.8 / 2.0 초인데(fighters.json) 그 값을 베끼면
        // 차지 한 번을 재는 테스트가 120틱을 돌고, 무엇보다 캐릭터의 차지 시간을 고칠 때마다
        // 무관한 테스트가 같이 빨개진다. 여기서 진짜여야 하는 것은 수치가 아니라 **모양**이다:
        // 시간 오름차순 · 첫 칸은 0초 ×1.
        ChargeTiers = new List<ChargeTierDef>
        {
            new() { Seconds = 0.0, DamageMultiplier = 1.0 },
            new() { Seconds = 0.5, DamageMultiplier = 2.0 },
            new() { Seconds = 1.0, DamageMultiplier = 3.0 },
        },
        StaminaRegen = 40,
        Sprite = "test_unit",
    };

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
    /// 손으로 세우는 패턴의 예고. <b>내용은 아무 뜻이 없다</b> — 규칙 층은 이 값을 읽지 않고,
    /// 그리는 것은 뷰다. 여기 있는 이유는 <c>PatternDef.Tell</c> 이 required 이기 때문뿐이다.
    /// </summary>
    public static PatternTell Tell() => new()
    {
        Id = "test_mark",
        Anim = "attack",
        X = 0,
        Y = 0,
        Length = 100,
    };

    /// <summary>시험 패턴 <see cref="Sweep"/> 의 id.</summary>
    public const string SweepId = "쓸기";

    /// <summary>
    /// 판정 하나짜리 시험 패턴 — 0.5초에 <paramref name="maxDistance"/> 까지 · 높이 0~5000 을 친다
    /// (이슈 #59 · 판정 창). 대시 창을 넓게(1초) 두는 것은 무적의 길이를 <b>파이터 쪽</b>(0.14초)이
    /// 정하게 하기 위해서다. 패리는 안 된다 — 창을 재는 테스트가 패리 갈래로 새지 않게.
    /// </summary>
    public static PatternDef Sweep(double maxDistance, double activeSeconds, double endAt = 2.0) => new()
    {
        Tell = Tell(),
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
            Feint = false,
            MultiHit = 1,
            Tracking = false,
            HasGuardBreak = false,
        },
        Timeline = new List<PatternStep>
        {
            new() { T = 0.0, Kind = "windup" },
            new()
            {
                T = 0.5, Kind = "active", Distance = new[] { 0.0, maxDistance }, Height = new[] { 0.0, 5000.0 },
                Damage = 7, ActiveSeconds = activeSeconds,
            },
            new() { T = endAt, Kind = "end" },
        },
    };

    /// <summary>보스가 서서 <see cref="Sweep"/> 만 휘두르는 판. 보스는 안 움직이고 안 죽는다.</summary>
    public static BattleSim SweepSim(double maxDistance, double activeSeconds, double endAt = 2.0) => new(new BattleSetup
    {
        Arena = Arena(),
        Fighter = Fighter(),
        HitShapes = HitShapes(),
        Boss = Boss(maxHealth: 999_999, moveSpeed: 0, patternGap: 0.2),
        PatternIds = new[] { SweepId },
        Patterns = new Dictionary<string, PatternDef> { [SweepId] = Sweep(maxDistance, activeSeconds, endAt) },
        Seed = 1,
        MaxTicks = 60 * 30,
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
    /// 판정이 두 틱 안으로 다가올 때까지 민다 — 판정은 다음 틱이나 그다음 틱에 선다.
    ///
    /// <para>
    /// "바로 전 틱" 을 <c>NextActiveIn</c> 으로 맞히려 하지 않는다. 러너의 시계는 틱마다 1/60 을 더해 가므로
    /// 30번 더한 값이 0.49999999999999994 라 0.5초 판정은 31번째 틱에 선다 — "남은 시간이 한 틱 이하면 다음
    /// 틱에 선다" 고 가정하면 한 틱 어긋난다 (이슈 #59 계획을 실행하기 전에 실제로 재 봤다).
    /// </para>
    /// </summary>
    public static void UntilNear(BattleSim sim)
    {
        UntilWindup(sim);
        while (sim.NextActiveIn is { } left && left > 2 * BattleSim.Dt)
        {
            sim.Tick(default);
        }
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

    public static BalanceData Balance() =>
        JsonData<BalanceData>.ParseOne(File.ReadAllText(Path.Combine("data", "balance.json")), "balance.json");

    /// <summary>실제 아레나. 폭은 balance.json 이 정한다 — 테스트마다 1920 을 베껴 적지 않는다.</summary>
    public static Arena Arena() => new(Balance().Battle.ArenaWidth);

    /// <summary>한 판의 상한. 실제 전투가 쓰는 값이다.</summary>
    public static int MaxTicks() => Balance().Battle.MaxTicks;

    /// <summary>
    /// 실제 보스. <paramref name="maxHealth"/> · <paramref name="moveSpeed"/> · <paramref name="patternGap"/> 은
    /// <b>일부러 이상한 값을 넣어야 하는 테스트</b>만 준다 (체력 999_999 로 시간 초과를 만든다 ·
    /// 속도 0 으로 보스를 세운다). 반폭은 절대 안 받는다 — 몸 충돌 간격이 그 값을 쓰므로
    /// 여기서 갈리면 테스트가 실제 전투와 다른 자리를 재게 된다.
    /// </summary>
    public static BossConfig Boss(int? maxHealth = null, double? moveSpeed = null, double? patternGap = null)
    {
        BalanceData balance = Balance();
        BossConfig data = Bosses()[balance.Battle.Boss];
        return new BossConfig
        {
            MaxHealth = maxHealth ?? data.MaxHealth,
            MoveSpeed = moveSpeed ?? data.MoveSpeed,
            HalfWidth = data.HalfWidth,
            Height = data.Height,
            PatternGap = patternGap ?? data.PatternGap,
            FinisherParryStagger = data.FinisherParryStagger,
            Sprite = data.Sprite,
        };
    }

    private static Dictionary<string, T> Table<T>(string name) =>
        JsonData<T>.ParseTable(File.ReadAllText(Path.Combine("data", name)), name);
}
