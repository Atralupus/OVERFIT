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
    public void 탈진_하나만으로_되받아치기_2연격이_들어간다()
    {
        // 받아치면 어느 타든 보스가 탈진한다 (#72 · 설계 §4.3). 그 상은 **2연격 한 번**이다 — 받아쳤다 → 제일 센 걸 꽂는다.
        //
        // ⚠ **패턴 간격을 더해서 재지 않는다** (이슈 #53). 간격은 탈진이 **풀린 뒤**의 시간이라 한 동작으로 안 이어지고,
        // patterns.json 의 간격을 고치는 날 이 상이 말없이 사라진다. 탈진 하나만으로 들어가야 한다.
        Dictionary<string, FighterConfig> fighters = TestConfigs.Fighters();
        fighters.ShouldNotBeEmpty("캐릭터가 하나도 없다 — 이 가드가 아무것도 안 본다");

        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            foreach ((string who, FighterConfig c) in fighters)
            {
                // 칼이 닿기까지 = 한 틱 + 1타 전체 + 2타의 선딜 + 판정의 끝 (TestConfigs.CounterLead) — 2타가 창의 **끝 틱**에
                // 닿는 가장 나쁜 경우다(설계 §4.3). J 는 패리 커밋이 끝나기를 안 기다린다(되받아치기 · 판정 13).
                double lead = TestConfigs.CounterLead(c);
                boss.ExhaustSeconds.ShouldBeGreaterThanOrEqualTo(lead,
                    $"{id}: 탈진 {boss.ExhaustSeconds} 초에 {who} 의 되받아치기 2연격({lead:0.000}초)가 안 들어간다");
            }
        }
    }

    [Fact]
    public void 탈진에_받아친_것을_알아차릴_여유가_남는다()
    {
        // 딱 맞으면 **사람이 못 쓴다.** 받아친 것을 보고 손을 공격 키로 옮기는 시간이 있어야
        // "받아쳤다 → 제일 센 걸 꽂는다" 가 한 동작이 된다. 0.15초는 사람 반응의 아래쪽이다 —
        // 이 여유가 0 이 되면 탈진 길이가 산수로만 맞고 손으로는 안 맞는다. 지금 값: 1.5 − 1.1001 = 0.3999.
        Dictionary<string, FighterConfig> fighters = TestConfigs.Fighters();

        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            foreach ((string who, FighterConfig c) in fighters)
            {
                // 여유는 **받아친 다음 틱부터** 센다 — 커밋 안이어도 알아차린 순간 J 가 나간다(위 테스트의 주석).
                double lead = TestConfigs.CounterLead(c);
                (boss.ExhaustSeconds - lead).ShouldBeGreaterThanOrEqualTo(0.15,
                    $"{id}: {who} 의 되받아치기 2연격({lead:0.000}초)가 탈진에 겨우 들어간다 — 반응할 틈이 없다");
            }
        }
    }

    [Fact]
    public void 연격_한_번으로는_안_무너지고_연달아_두_번이면_두_번째_2타에_무너진다()
    {
        // 설계 §4.5 — 게이지 100 · 1타 10 · 2타 45: 한 번은 55 라 안 무너지고, 연달아 두 번이면 10 → 55 → 65 → 110 에서 두 번째
        // 2타에 무너진다. 셋째 칼(65)에 무너지면 "2타가 더 큰 경직도" 가 무너뜨리는 칼이 아니다.
        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            foreach ((string who, FighterConfig c) in TestConfigs.Fighters())
            {
                double once = c.Combo[0].Poise + c.Combo[1].Poise;
                once.ShouldBeLessThan(boss.PoiseMax, $"{id}: {who} 의 2연격 한 번({once})에 무너진다");
                (once + c.Combo[0].Poise).ShouldBeLessThan(boss.PoiseMax, $"{id}: {who} 의 두 번째 1타에 무너진다");
                (2 * once).ShouldBeGreaterThanOrEqualTo(boss.PoiseMax, $"{id}: {who} 의 2연격 두 번({2 * once})으로도 안 무너진다");
            }
        }
    }

    [Fact]
    public void 경직_유예가_한_연격을_0_15초_넘게_남기고_덮는다()
    {
        // 설계 §4.5 — 1타가 판정 창의 첫 틱에 닿고 2타가 창의 끝 틱에 닿는 가장 긴 경우가 1타의 판정 + 후딜 + 2타의 선딜 + 판정 =
        // 1.0001초다. 유예(1.2초)가 그보다 짧으면 2타가 닿기 전에 게이지가 줄기 시작해 이어 친 칼이 앞 칼의 몫을 잃는다.
        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            foreach ((string who, FighterConfig c) in TestConfigs.Fighters())
            {
                double longest = c.Combo[0].Active + c.Combo[0].Recover + c.Combo[1].Windup + c.Combo[1].Active;
                (boss.PoiseDecayDelay - longest).ShouldBeGreaterThanOrEqualTo(0.15,
                    $"{id}: 유예 {boss.PoiseDecayDelay}초가 {who} 의 2연격({longest:0.0000}초)을 겨우 덮는다");
            }
        }
    }

    [Fact]
    public void 게이지를_깬_2타_뒤에도_탈진_안에_1타_하나는_반응_여유를_남기고_닿는다()
    {
        // 설계 §4.3 — 게이지를 깬 것이 2타면 그 2타를 끝까지 휘두르고 **2타 뒤 경직까지** 서야 다음 칼이 선다(2연격의 끝이다 · #82).
        // 가장 나쁜 경우는 2타가 창의 첫 틱에 닿아 무너뜨린 것이다: 남은 판정 + 후딜 0.3333 + 2타 뒤 경직 0.5 + 다음 틱의 J 0.0167 +
        // 1타가 창의 끝 틱에 닿기까지(선딜 + 판정) 0.1666 = 1.0166초 — 1.5 에 0.4834 가 남는다. 경직은 규칙처럼 틱으로 센다.
        //
        // **전에는 반격 2연격이 여유 없이 들어갔다**(1.4334초 — 0.0666 이 남았다). 2타 뒤 경직 0.5 가 들며 그 2연격(1.9334초)은 이제 안
        // 들어간다 — 게이지 쪽 반격은 1타 하나다. 패리 쪽 반격(위 둘 — 되받아치기 2연격)은 그대로라, 받아친 쪽이 때려서 연 쪽보다 확실히
        // 크다(설계 §11 「게이지가 패리 중심 고리를 약하게 할 수 있다」를 누그러뜨린다). 여유는 패리 쪽과 같은 0.15 다 — 이제 1타 하나라
        // 보고 누를 틈이 있다.
        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            foreach ((string who, FighterConfig c) in TestConfigs.Fighters())
            {
                ComboStepDef first = c.Combo[0], last = c.Combo[^1];
                double stiff = BattleSim.TicksFor(last.Stiff) * BattleSim.Dt;
                double lead = last.Active + last.Recover + stiff + BattleSim.Dt + first.Windup + first.Active;
                (boss.ExhaustSeconds - lead).ShouldBeGreaterThanOrEqualTo(0.15,
                    $"{id}: 탈진 {boss.ExhaustSeconds}초에 게이지를 깬 {who} 의 반격 1타({lead:0.0000}초)가 겨우 들어간다 — 반응할 틈이 없다");
            }
        }
    }
}
