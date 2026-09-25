using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 쐐기 두 변종이 <b>가드 의존만</b> 봉인하는가 (이슈 #53).
///
/// <para>
/// 마무리가 아홉 변종 전부 가드 불가가 되면서(유저 결정) <c>III-쐐기</c> 는 <c>II-쐐기</c> 와 갈리던
/// 유일한 것 — 마무리의 guard_break 깃발 — 을 잃었다. 그대로 두면 두 변종은 글자 하나 안 다른
/// 같은 패턴이다. 그래서 3단계의 쐐기는 <b>빨간 마무리가 오기 전에</b> 붙든 가드를 깬다.
/// </para>
///
/// <para>
/// 여기서 증명하는 것은 둘이다. ① 붙들고 버티는 사람에게는 3단계가 정말로 더 나쁘다
/// (그리고 어떻게 나쁜지가 2단계와 <b>종류가</b> 다르다). ② 붙들지 않는 사람 — 받아치거나 피하는
/// 사람 — 에게는 <b>한 틱도 안 다르다</b>. 봉인이지 벌이 아니다(설계 §2.3).
/// </para>
///
/// <para>
/// 파이터는 <b>실제 캐릭터</b>(<c>fighters.json</c>)로 돈다. 이 봉인은 그 캐릭터의 스태미나 산수
/// (최대 100 · 누름 15 · 피해당 1.8) 위에 서 있어서, 기준값(<c>TestConfigs.Fighter</c>)으로 재면
/// 캐릭터를 고친 날 봉인이 조용히 풀려도 초록이다.
/// </para>
/// </summary>
public class WedgeSealTests
{
    private const string _two = "내려찍기 II-쐐기";
    private const string _three = "내려찍기 III-쐐기";

    /// <summary>
    /// 받아치거나 피하는 스크립트가 누르는 시각 — 판정까지 이만큼 남았을 때(초).
    /// 봇의 반응 창(0.10)보다 좁게 잡는다: 사거리 밖에서 헛누른 뒤의 연타 사슬이 패리 창을
    /// 0.1 로 깎아도 그 안에 들어가게 — 여기서 재려는 것은 봉인이지 누르는 솜씨가 아니다.
    /// </summary>
    private const double _press = 0.05;

    /// <summary>붙었다고 보는 거리(px). 보스가 서는 자리(반폭 85 + 30 = 115)에 여유를 둔다.</summary>
    private const double _close = 130;

    private enum Answer
    {
        /// <summary>판정마다 창 안에서 한 번 누른다.</summary>
        Parry,

        /// <summary>판정마다 무적 창 안에서 한 번 대시한다.</summary>
        Dash,

        /// <summary>붙은 뒤 새 패턴이 서면 누르고, 그 패턴 내내 놓지 않는다 — 가드 의존의 모양이다.</summary>
        Hold,
    }

    /// <summary>
    /// <paramref name="pattern"/> 하나만 도는 판을 <paramref name="answer"/> 로 <paramref name="ticks"/> 만큼 돌린다.
    /// </summary>
    private static BattleSim Play(FighterConfig fighter, string pattern, Answer answer, int ticks)
    {
        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = fighter,
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = new[] { pattern },
            Patterns = TestConfigs.Patterns(),
            Seed = 51,
            MaxTicks = ticks + 1,
        });

        bool actedForThisHit = false;
        bool holding = false;
        string? lastPattern = null;
        for (int t = 1; t <= ticks; t++)
        {
            double gap = System.Math.Abs(sim.Fighter.X - sim.Boss.X);
            sbyte toward = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);
            bool fresh = sim.Boss.CurrentPattern is not null && lastPattern is null;
            lastPattern = sim.Boss.CurrentPattern;

            InputFrame input;
            if (answer == Answer.Hold)
            {
                // 붙은 뒤 **새로 선** 패턴의 첫 틱에 누르고 그 패턴 내내 놓지 않는다.
                // 붙기 전에 누르면 걷지 못하고(자세 중에는 못 움직인다), 패턴 도중에 누르면
                // 앞의 대를 놓친다 — 둘 다 "가드에 기댄다" 가 아니다.
                holding |= fresh && gap <= _close;
                holding &= sim.Boss.CurrentPattern is not null;
                input = holding
                    ? new InputFrame(0, false, false, Parry: sim.Fighter.Action == FighterAction.Idle, false, ParryHeld: true)
                    : new InputFrame(gap > _close ? toward : (sbyte)0, false, false, false, false);
            }
            else if (!actedForThisHit && sim.NextActiveIn is double left && left <= _press)
            {
                actedForThisHit = true;
                input = answer == Answer.Parry
                    ? new InputFrame(0, false, false, Parry: true, false)
                    : new InputFrame(0, false, Dash: true, false, false);
            }
            else
            {
                input = new InputFrame(gap > _close ? toward : (sbyte)0, false, false, false, false);
            }

            int before = sim.Events.Count;
            sim.Tick(input);
            if (sim.Events.Count > before)
            {
                actedForThisHit = false;
            }
        }

        return sim;
    }

    /// <summary>붙든 가드가 받은 <b>첫 패턴 한 바퀴</b>의 관측 — 누르기 시작한 뒤의 판정 넷.</summary>
    private static List<DodgeEvent> HeldRound(FighterConfig fighter, string pattern)
    {
        BattleSim sim = Play(fighter, pattern, Answer.Hold, 60 * 20);
        int start = sim.Events.ToList().FindIndex(e => e.Verb == DodgeVerb.Guard);
        start.ShouldBeGreaterThanOrEqualTo(0, $"{pattern}: 가드가 한 번도 판정을 안 받았다 — 이 테스트가 아무것도 안 본다");

        // 가드로 받은 첫 대부터 한 패턴(판정 넷)이다. 쐐기는 둘 다 판정이 넷이다.
        sim.Events.Count.ShouldBeGreaterThanOrEqualTo(start + 4, $"{pattern}: 붙든 한 바퀴가 끝나지 않았다");
        return sim.Events.Skip(start).Take(4).ToList();
    }

    [Fact]
    public void II_쐐기는_붙든_가드가_빨간_마무리까지_버틴다()
    {
        // 2단계의 쐐기는 **무게**로 봉인한다: 가드 불가인 마무리가 26 이라 다른 변종(14)의 두 배 가까이다.
        // 앞의 셋은 붙들고 버틸 수 있다 — 누름 15 + 8 × 1.8 × 3 = 58.2 로 100 안이다.
        foreach ((string id, FighterConfig fighter) in TestConfigs.Fighters())
        {
            List<DodgeEvent> round = HeldRound(fighter, _two);

            round.Take(3).Select(e => e.Verdict).ShouldAllBe(v => v == HitVerdict.Guarded,
                $"{id}: 2단계 쐐기의 앞 세 대를 붙든 가드가 못 버텼다 — 봉인이 한 단계 이르다");
            round[3].Verdict.ShouldBe(HitVerdict.GuardBroken, $"{id}: 빨간 마무리를 가드가 버텼다");
            round[3].Finisher.ShouldBeTrue();

            // **무엇이** 깼는지까지 본다. 이슈 #47 때 이 대는 스태미나 고갈로 깨졌다(누름 15 + 58.2 +
            // 26 × 1.8 = 46.8 → 105 > 100). 지금은 빨강이라 **깃발이** 깬다 — 스태미나가 남아
            // 있어도 깨진다. 빨강의 뜻("가드로 못 막는다")이 스태미나 산수에 기대면 안 된다.
            round[3].GuardAvailable.ShouldBeFalse($"{id}: 빨간 마무리가 가드 불가가 아니다 — 스태미나로만 깨졌다");
        }
    }

    [Fact]
    public void III_쐐기는_붙든_가드를_빨간_마무리_전에_깬다()
    {
        // **3단계의 쐐기는 종류가 다른 봉인이다.** 셋째 대가 무거워(32) 붙든 가드의 스태미나가
        // **빨간 마무리가 오기 전에** 바닥난다:
        //
        //   누름 15 → 85 · 첫째 8 × 1.8 = 14.4 → 70.6 · 둘째 14.4 → 56.2 · 셋째 32 × 1.8 = 57.6 > 56.2 → 깨짐
        //
        // 깨지면 전액을 맞고 guard_break_lock(1.1초) 동안 굳고, 자세는 **새로 눌러야** 선다.
        // 그래서 붙들고만 있던 사람은 빨간 마무리를 **이미 무너진 채로** 맞는다 — 받아칠 기회조차
        // 없다. 2단계의 교훈이 "빨간 것은 받아쳐라" 였다면 3단계의 교훈은 "애초에 붙들지 마라" 다.
        //
        // ⚠ **그 "받아칠 기회조차 없다" 는 고정이 셋째 → 마무리를 덮어야 선다** (이슈 #54 에서 밟았다).
        // 간격을 1.10(66틱)으로 넓히자 옛 고정 0.9(54틱)가 마무리 12틱 앞에 풀려, 이 스크립트가 다시
        // 누른 가드에 빨강이 떨어졌다(Hit 가 아니라 GuardBroken — 아래 마지막 단언이 빨갛게 섰다).
        // 8틱 안에 눌렀다면 **받아쳐** 경직까지 얻었을 자리다. 고정도 간격을 따라 1.1 이 됐다 —
        // 산수는 VariantRhythmTests.쐐기가_깬_가드는_빨간_마무리가_올_때까지_굳어_있다 에 있다.
        // 앞 절반(스태미나)은 간격과 무관하다: 붙든 가드는 Idle 이 아니라 안 차서 여유는 그대로 1.4 다.
        foreach ((string id, FighterConfig fighter) in TestConfigs.Fighters())
        {
            List<DodgeEvent> round = HeldRound(fighter, _three);

            round[0].Verdict.ShouldBe(HitVerdict.Guarded);
            round[1].Verdict.ShouldBe(HitVerdict.Guarded);

            // 셋째가 **스태미나로** 깨졌는지를 못박는다 — 가드 불가 깃발로 깨졌다면 그건
            // 마무리를 셋째로 옮긴 것이지 스태미나 봉인이 아니다.
            round[2].Verdict.ShouldBe(HitVerdict.GuardBroken, $"{id}: 셋째 대가 붙든 가드를 못 깼다 — 봉인이 없다");
            round[2].GuardAvailable.ShouldBeTrue($"{id}: 셋째 대가 가드 불가로 깨졌다 — 스태미나 봉인이 아니다");
            round[2].Finisher.ShouldBeFalse();

            // 그리고 빨간 마무리는 가드가 **아니라** 맨몸에 떨어진다.
            round[3].Finisher.ShouldBeTrue();
            round[3].Verdict.ShouldBe(HitVerdict.Hit, $"{id}: 빨간 마무리가 무너진 가드가 아닌 무언가에 먹혔다");
        }
    }

    [Fact]
    public void 붙든_가드에게는_셋째_단계가_둘째_단계보다_아프다()
    {
        // 봉인의 **값**을 못박는다. 무게로 봉인하는 2단계: 칩 2 × 3 + 마무리 전액 26 = 32.
        // 먼저 깨는 3단계: 칩 2 × 2 + 셋째 전액 32 + 마무리 전액 14 = 50.
        // 둘이 같거나 뒤집히면 3단계의 쐐기는 이름만 봉인이다.
        foreach ((string id, FighterConfig fighter) in TestConfigs.Fighters())
        {
            int two = HeldDamage(fighter, _two);
            int three = HeldDamage(fighter, _three);

            three.ShouldBeGreaterThan(two, $"{id}: 붙든 가드가 3단계 쐐기({three})에서 2단계({two})보다 안 아프다");
        }
    }

    /// <summary>붙든 가드가 한 바퀴 동안 받은 피해 — 그 바퀴 앞뒤의 체력 차.</summary>
    private static int HeldDamage(FighterConfig fighter, string pattern)
    {
        // 한 바퀴를 따로 돌려 체력을 잰다. 개막에 사거리 밖에서 헛돈 대는 피해가 0 이라 안 섞인다.
        BattleSim sim = Play(fighter, pattern, Answer.Hold, 60 * 20);
        int start = sim.Events.ToList().FindIndex(e => e.Verb == DodgeVerb.Guard);
        start.ShouldBeGreaterThanOrEqualTo(0);

        // 관측에는 피해가 안 실리므로 판정과 패턴 수치에서 거꾸로 셈한다.
        List<PatternStep> hits = TestConfigs.Patterns()[pattern].Timeline.Where(s => s.Kind == "active").ToList();
        int damage = 0;
        for (int i = 0; i < 4; i++)
        {
            int full = hits[i].Damage;
            damage += sim.Events[start + i].Verdict switch
            {
                HitVerdict.Guarded => (int)System.Math.Round(full * fighter.GuardChipRatio, System.MidpointRounding.AwayFromZero),
                HitVerdict.GuardBroken or HitVerdict.Hit => full,
                _ => 0,
            };
        }

        return damage;
    }

    [Theory]
    [InlineData(nameof(Answer.Parry))]
    [InlineData(nameof(Answer.Dash))]
    public void 붙들지_않는_사람에게는_둘째_단계와_한_틱도_안_다르다(string answerName)
    {
        // **설계 §2.3 의 원칙: 저격 대상이 아닌 사람에겐 같은 난이도여야 한다.** 3단계 쐐기가
        // 바꾼 것은 **피해 숫자뿐**이고(PatternDataTests 가 그것만 바뀌었는지를 본다), 피해는
        // 맞았거나 가드로 받았을 때만 판에 들어간다. 받아치거나 피하는 사람의 판에는 그 숫자가
        // 한 번도 안 들어가므로 체력 · 스태미나 · 관측이 **비트 하나 안 다르게** 같아야 한다.
        //
        // 전제도 같이 못박는다 — 이 스크립트가 한 번이라도 맞았다면 피해가 판에 섞여 두 판이
        // 당연히 갈리고, 그건 "봉인이 샌다" 가 아니라 "스크립트가 실수했다" 다.
        var answer = System.Enum.Parse<Answer>(answerName);
        foreach ((string id, FighterConfig fighter) in TestConfigs.Fighters())
        {
            BattleSim two = Play(fighter, _two, answer, 60 * 15);
            BattleSim three = Play(fighter, _three, answer, 60 * 15);

            foreach (BattleSim sim in new[] { two, three })
            {
                sim.Events.ShouldNotContain(e => e.Verdict == HitVerdict.Hit || e.Verdict == HitVerdict.GuardBroken,
                    $"{id} · {answer}: 스크립트가 맞았다 — 두 판을 견주는 전제가 무너진다");
                sim.Events.Count(e => e.Verdict is HitVerdict.Parried or HitVerdict.Dodged).ShouldBeGreaterThanOrEqualTo(4,
                    $"{id} · {answer}: 받아치거나 피한 대가 한 바퀴도 안 된다 — 이 테스트가 아무것도 안 본다");
            }

            three.Fighter.Health.ShouldBe(two.Fighter.Health, $"{id} · {answer}: 3단계 쐐기에서 체력이 다르다");
            three.Fighter.Stamina.ShouldBe(two.Fighter.Stamina, $"{id} · {answer}: 3단계 쐐기에서 스태미나가 다르다");
            three.Events.Select(e => e with { PatternId = "" })
                .ShouldBe(two.Events.Select(e => e with { PatternId = "" }),
                    $"{id} · {answer}: 3단계 쐐기의 관측이 2단계와 다르다 — 피해 말고 무언가가 같이 바뀌었다");
        }
    }
}
