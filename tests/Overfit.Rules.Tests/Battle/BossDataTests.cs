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
    public void 폭탄_반응의_시간_둘은_틱에_딱_떨어지고_반응의_동작은_데이터에_있다()
    {
        // 설계 2026-09-30 조각2 §2.4 — 반응 지연 0.3초(18틱) · 멈칫 0.25초(15틱). 반 틱이면 반올림이 "누른 틱 + 88 안에 닿나" 를 정한다. 반응의 동작은
        // patterns.json 의 id 다 — 명부에 없어도 되지만 데이터에는 있어야 한다(없으면 판이 끊을 자리마다 [E] 를 남기고 안 끊는다).
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            BombReactionDef r = boss.BombReaction;
            foreach ((string key, double seconds) in new[] { ("delay_seconds", r.DelaySeconds), ("hesitate_seconds", r.HesitateSeconds) })
            {
                seconds.ShouldBeGreaterThan(0, $"{id}: bomb_reaction.{key} 가 0 이하다");
                (seconds * 60).ShouldBe(System.Math.Round(seconds * 60), 1e-9, $"{id}: bomb_reaction.{key} {seconds} 가 정수 틱이 아니다");
            }

            patterns.ShouldContainKey(r.Move, $"{id}: 반응의 동작 {r.Move} 이 patterns.json 에 없다");
        }
    }

    [Fact]
    public void 쉬는_길이는_정수_틱이고_0_보다_크다()
    {
        // 설계 2026-09-29 조각1 §3.4 — 0.4 · 0.8 · 1.2초(24 · 48 · 72틱). 옛 pattern_gap 0.8 을 가운데에 두고 반씩 흔든다. 반 틱이면 반올림이 쉬기를
        // 한 틱 밀어 "빠른 3연격이 2연격을 잡는 여유 6틱" 같은 잰 값이 조용히 어긋난다. 0 이하면 한 틱으로 읽힌다(TicksFor) — 틀린 데이터가 말없이 돈다.
        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            boss.RestSeconds.ShouldNotBeEmpty($"{id}: 쉬는 길이가 없다");
            foreach (double rest in boss.RestSeconds)
            {
                rest.ShouldBeGreaterThan(0, $"{id}: 쉬는 길이 {rest} 가 0 이하다");
                (rest * 60).ShouldBe(System.Math.Round(rest * 60), 1e-9, $"{id}: 쉬는 길이 {rest} 가 정수 틱이 아니다");
            }
        }

        BattleSim.RestTicks(TestConfigs.Boss()).ShouldBe(new[] { 24, 48, 72 });
    }

    [Fact]
    public void 달리기의_멈춤은_3연격_세_판정과_올려베기가_선_파이터에게_닿는_거리다()
    {
        // 설계 2026-09-29 조각1 §5.3 — 달리기는 파이터 앞 run_stop 에서 멈추고 첫 동작을 세운다. 그 자리에 선 파이터에게 3연격의 세 칼이 다 닿아야
        // "달려와서 3연격" 이 헛치지 않는다(파이터 중심의 앞쪽 거리로 1타 −74 ~ 404 · 2타 −338 ~ 426 · 3타 80 ~ 404). 올려베기(−30 ~ 426)도 닿는다.
        // 보스는 x 960 에서 +1 을 본다 — 판정 모양은 hitboxes.json · 손으로 적은 rects 그대로다.
        Dictionary<string, PatternDef> patterns = TestConfigs.Patterns();
        Dictionary<string, HitShape> shapes = TestConfigs.HitShapes();
        var at = new Placement(960, 0, 1);
        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            boss.RunStop.ShouldBeGreaterThan(0, $"{id}: 달리기가 파이터 몸 안까지 파고든다");
            foreach ((string who, FighterConfig c) in TestConfigs.Fighters())
            {
                HitRect body = new Fighter(c, TestConfigs.Arena(), 960 + boss.RunStop).Body;
                foreach (string move in new[] { "3연격", "올려베기" })
                {
                    HitBox?[] hits = BossHits.Of(patterns[move], shapes);
                    int strikes = 0;
                    foreach (HitBox? hit in hits)
                    {
                        if (hit is not { } h)
                        {
                            continue;
                        }

                        strikes++;
                        ShapeHit.Overlaps(h.Shape, at, body).ShouldBeTrue($"{id} · {who}: 앞 {boss.RunStop} 에 선 파이터에게 {move} 의 {strikes}번째 칼이 안 닿는다");
                    }

                    strikes.ShouldBeGreaterThan(0, $"{move} 에 칼이 없다 — 이 가드가 아무것도 안 본다");
                }
            }
        }
    }

    [Fact]
    public void 달리기_상한은_아레나_끝에서_끝보다_길다()
    {
        // 설계 2026-09-29 조각1 §5.3 — run_max_seconds 는 안전장치다. 가장 먼 달리기(보스가 한쪽 벽 · 파이터가 반대쪽 벽 · 그 앞 run_stop 까지)보다
        // 짧으면 서 있는 파이터에게 달려가다가 [W] run_timeout 이 난다 — 정상 플레이가 경고를 낸다. 실제 수치로 (1920 − 85 − 30 − 280) / 840 = 1.82초다.
        double arena = TestConfigs.Balance().Battle.ArenaWidth;
        double thinnest = double.MaxValue;
        foreach (FighterConfig c in TestConfigs.Fighters().Values)
        {
            thinnest = System.Math.Min(thinnest, c.HalfWidth);
        }

        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            boss.RunSpeed.ShouldBeGreaterThan(0, $"{id}: 달리기가 안 간다");
            double longest = (arena - boss.HalfWidth - thinnest - boss.RunStop) / boss.RunSpeed;
            longest.ShouldBeLessThan(boss.RunMaxSeconds, $"{id}: 가장 먼 달리기 {longest:0.00}초가 상한 {boss.RunMaxSeconds}초를 넘는다");
        }
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

    [Fact]
    public void 형태의_문턱은_내려가고_전환은_정수_틱이다()
    {
        // 설계 2026-10-01 조각1 §1 — 체력 1200 · 문턱 900 · 400 · 전환 1.5초(90틱). 판이 세울 때 같은 조건으로 거절한다(BossForms).
        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            _ = new BossForms(boss.Forms.Thresholds, boss.MaxHealth, boss.MaxHealth, BattleSim.TicksFor(boss.Forms.ShiftSeconds));
            (boss.Forms.ShiftSeconds * 60).ShouldBe(System.Math.Round(boss.Forms.ShiftSeconds * 60), 1e-9, $"{id}: shift_seconds 가 정수 틱이 아니다");
        }

        BossConfig real = TestConfigs.Boss();
        real.MaxHealth.ShouldBe(1200);
        real.Forms.Thresholds.ShouldBe(new[] { 900, 400 });
        BattleSim.TicksFor(real.Forms.ShiftSeconds).ShouldBe(90);
    }

    [Fact]
    public void 쉬는_길이는_결정_간격의_배수이고_늦춤은_정수_틱이다()
    {
        // 설계 2026-10-01 조각2 §1 — 규칙 조종기가 0.11 의 쉬기를 결정 간격(12틱)마다의 기다리기로 낸다. 배수가 아니면 동작이 한 칸 늦게 선다.
        foreach ((string id, BossConfig boss) in TestConfigs.Bosses())
        {
            int decide = BattleSim.TicksFor(boss.DecideSeconds);
            foreach (int rest in BattleSim.RestTicks(boss))
            {
                (rest % decide).ShouldBe(0, $"{id}: 쉬기 {rest}틱이 결정 간격 {decide}틱의 배수가 아니다");
            }

            (boss.SightDelaySeconds * 60).ShouldBe(System.Math.Round(boss.SightDelaySeconds * 60), 1e-9, $"{id}: sight_delay_seconds 가 정수 틱이 아니다");
        }

        BattleSim.TicksFor(TestConfigs.Boss().DecideSeconds).ShouldBe(12);
        BattleSim.TicksFor(TestConfigs.Boss().SightDelaySeconds).ShouldBe(18);
    }
}
