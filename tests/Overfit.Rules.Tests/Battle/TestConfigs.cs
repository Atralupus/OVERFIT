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
/// 파이터만 리터럴이다. 이것은 특정 캐릭터가 아니라 <b>기준값</b>이라 fighters.json 의 세 캐릭터 중
/// 어느 하나를 골라 쓰면 그 캐릭터의 밸런스를 고칠 때마다 무관한 테스트가 같이 빨개진다.
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
        ParryImpreciseWindow = 0.5,
        ParrySpamWindow = 0.1,
        ParryLock = 0.6,
        ParryInternalRatio = 0.5,
        ParryDuration = 0.30,
        ParryCost = 15,
        AttackWindup = 0.08,
        AttackActive = 0.06,
        AttackRecover = 0.14,
        AttackReach = 90,
        AttackDamage = 8,
        AttackCost = 12,
        StaminaRegen = 40,
        Sprite = "test_unit",
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
            StaggerSeconds = data.StaggerSeconds,
            Sprite = data.Sprite,
        };
    }

    private static Dictionary<string, T> Table<T>(string name) =>
        JsonData<T>.ParseTable(File.ReadAllText(Path.Combine("data", name)), name);
}
