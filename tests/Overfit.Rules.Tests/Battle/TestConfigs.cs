using System.Collections.Generic;
using System.IO;
using Overfit.Battle.Rules;
using Overfit.Core;

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
        AttackWindup = 0.08,
        AttackActive = 0.06,
        AttackRecover = 0.14,
        // 기준 파이터에는 그림이 없지만 셋의 관계는 진짜여야 한다 — 50fps · 14프레임이면
        // 재생 0.28초로 위 셋의 합과 같고, 4번 프레임(0.08초)이 선딜의 끝이다.
        // 거짓 값을 넣으면 이 픽스처가 "애니메이션에서 거꾸로 정한다"는 규칙의 반례가 된다.
        AttackAnimFps = 50,
        AttackAnimFrames = 14,
        AttackAnimBladeFrame = 4,
        AttackReach = 90,
        AttackDamage = 8,
        AttackCost = 12,
        // 가드 셋은 **실제 값 그대로**다 (이슈 #47). 패리 창과 같은 자리라 캐릭터 성능이 아니라
        // **조작의 정의**이고, 여기서 다른 값을 쓰면 테스트가 말하는 "가드" 가 게임의 가드가 아니게 된다.
        GuardChipRatio = 0.25,
        GuardStaminaPerDamage = 1.8,
        GuardBreakLock = 0.9,
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
            PatternGap = patternGap ?? data.PatternGap,
            FinisherParryStagger = data.FinisherParryStagger,
            Sprite = data.Sprite,
        };
    }

    private static Dictionary<string, T> Table<T>(string name) =>
        JsonData<T>.ParseTable(File.ReadAllText(Path.Combine("data", name)), name);
}
