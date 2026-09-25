using System.Collections.Generic;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 보스 수치와 판을 세우는 수치가 <b>데이터에</b> 있는가.
///
/// <para>
/// 전에는 <c>new BossConfig { … }</c> 가 C# 다섯 곳에 있었고 이미 갈려 있었다 —
/// 게임은 <c>boss_grym</c> 을, 데모와 테스트는 <c>boss_test</c> 를 그렸다.
/// 한 곳에서 체력을 고치면 게임·데모·골든이 서로 다른 전투를 말했다.
/// </para>
/// </summary>
public class BossDataTests
{
    [Fact]
    public void 실제_bosses_json_이_읽힌다()
    {
        TestConfigs.Bosses().ShouldNotBeEmpty();
    }

    [Fact]
    public void Balance_가_가리키는_보스가_bosses_json_에_있다()
    {
        // "기본 보스가 누구인가" 는 balance.json 이 정한다. 그 id 가 없는 이름이면
        // 부팅이 KeyNotFoundException 으로 죽는데, 그 스택엔 어느 파일이 어긋났는지가 안 적힌다.
        Dictionary<string, BossConfig> bosses = TestConfigs.Bosses();

        bosses.ShouldContainKey(TestConfigs.Balance().Battle.Boss);
    }

    [Fact]
    public void 아레나가_보스_몸보다_충분히_넓다()
    {
        // Arena 의 문서가 적어둔 계약이다 — "보스 폭의 배수여야 대시로 빠질 곳이 남는다".
        // 지금은 1920 / 240 = 8배다. 넷 아래로 내려가면 밖으로 빠지는 길이 사실상 없어져
        // dash_direction 축이 한쪽으로 쏠린다.
        BalanceData balance = TestConfigs.Balance();
        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            (balance.Battle.ArenaWidth / (boss.HalfWidth * 2)).ShouldBeGreaterThanOrEqualTo(4,
                $"{id}: 아레나가 보스 몸의 네 배도 안 된다 — 빠질 곳이 없다");
        }
    }

    [Fact]
    public void 한_판의_상한이_사람이_한_판_할_만큼은_된다()
    {
        // 한 판이 반드시 끝나게 하는 안전장치다. 너무 짧으면 정상 플레이가 시간 초과로 지고,
        // 그 패배가 학습 데이터에 "못 피해서 죽었다" 로 섞인다.
        TestConfigs.Balance().Battle.MaxTicks.ShouldBeGreaterThan(60 * 60);
    }

    [Fact]
    public void 경직_하나만으로_최대_차지가_들어간다()
    {
        // 상이 없으면 "가드 불가" 는 그냥 더 아픈 판정이다 (이슈 #47). 상은 **최대 차지 한 번**이다.
        //
        // ⚠ **패턴 간격을 더해서 재지 않는다** (이슈 #53). 전에는 경직 1.6 + 간격 0.8 = 2.4 로 쟀는데
        // 그 셈은 둘을 다 밟는다: 간격은 경직이 **풀린 뒤**의 시간이라 한 동작으로 안 이어지고,
        // 무엇보다 patterns.json 의 간격을 고치는 날 이 상이 말없이 사라진다.
        // 이제 경직 하나만으로 들어가야 한다.
        Dictionary<string, FighterConfig> fighters = TestConfigs.Fighters();
        fighters.ShouldNotBeEmpty("캐릭터가 하나도 없다 — 이 가드가 아무것도 안 본다");

        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            foreach ((string who, FighterConfig c) in fighters)
            {
                // 칼이 닿기까지 = 최대 차지 시간 + 판정. **붙든 시간이 곧 선딜**이라 선딜이 안 더해진다.
                // 지금 파이터는 칼질 한 칸뿐이다 — 그 칸(Combo[0])의 판정 시간을 쓴다.
                double lead = c.ChargeTiers[^1].Seconds + c.Combo[0].Active;
                boss.FinisherParryStagger.ShouldBeGreaterThanOrEqualTo(lead,
                    $"{id}: 경직 {boss.FinisherParryStagger} 초에"
                    + $" {who} 의 최대 차지({lead:0.000}초)가 안 들어간다");
            }
        }
    }

    [Fact]
    public void 경직에_받아친_것을_알아차릴_여유가_남는다()
    {
        // 딱 맞으면 **사람이 못 쓴다.** 받아친 것을 보고 손을 공격 키로 옮기는 시간이 있어야
        // "받아쳤다 → 제일 센 걸 꽂는다" 가 한 동작이 된다. 0.15초는 사람 반응의 아래쪽이다 —
        // 이 여유가 0 이 되면 경직 길이가 산수로만 맞고 손으로는 안 맞는다.
        Dictionary<string, FighterConfig> fighters = TestConfigs.Fighters();

        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            foreach ((string who, FighterConfig c) in fighters)
            {
                double lead = c.ChargeTiers[^1].Seconds + c.Combo[0].Active;
                (boss.FinisherParryStagger - lead).ShouldBeGreaterThanOrEqualTo(0.15,
                    $"{id}: {who} 의 최대 차지({lead:0.000}초)가 경직에 겨우 들어간다 — 반응할 틈이 없다");
            }
        }
    }

}
