using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 박자가 곧 정체성인 변종 넷이 <b>1단계의 박자에 맞물려 있는가</b> (이슈 #54).
///
/// <para>
/// 아홉 변종은 한 뼈대(선딜 · 간격 · 간격)를 쓴다. 그중 넷은 그 뼈대에 <b>기대어</b> 뜻을 갖는다 —
/// 끌기는 마무리를 뼈대보다 <b>늦게</b> 오게 하고, 쐐기는 판정 넷을 뼈대의 마지막 간격으로 끝내고,
/// 역린은 마무리 앞에 헛스윙을 끼우고, 역습은 2타 뒤 간격(미끼)에 판정을 끼운다.
/// 이슈 #54 가 뼈대를 0.55 · 0.90 → 0.70 · 1.10 으로 넓혔는데, 뼈대만 옮기고 넷을 두면 그 넷은
/// <b>조용히 다른 기술</b>이 된다: 끌기의 "밀린" 마무리가 뼈대의 마무리보다 먼저 오고, 역습의 미끼가
/// 이미 끝난 칼질 뒤에 와 욕심 대신 서 있는 몸을 친다. 데이터 테스트는 그걸 못 본다 — 타임라인은 여전히 시간순이다.
/// </para>
///
/// <para>
/// 그래서 넷을 뼈대(<c>내려찍기 I</c>)의 <b>실제 데이터</b>와 맞대어 본다. 시각을 여기 손으로 적지 않는다 —
/// 적으면 뼈대를 다시 고치는 날 이 테스트가 옛 뼈대를 재면서 초록이다.
/// </para>
/// </summary>
public class VariantRhythmTests
{
    private const string _base = "내려찍기 I";

    /// <summary>붙었다고 보는 거리(px). 보스가 서는 자리(반폭 85 + 30 = 115)에 여유를 둔다.</summary>
    private const double _close = 130;

    /// <summary><paramref name="pattern"/> 의 판정들이 서는 러너 틱(패턴이 선 뒤 몇 번째 틱인가).</summary>
    private static List<int> ActiveTicks(string pattern)
    {
        var runner = new PatternRunner(TestConfigs.Patterns()[pattern]);
        var ticks = new List<int>();
        for (int k = 1; !runner.Finished; k++)
        {
            ticks.AddRange(runner.Tick(BattleSim.Dt).Select(_ => k));
        }

        return ticks;
    }

    /// <summary>패턴 하나만 도는 판. 보스는 안 죽는다 — 여기서 재는 것은 박자이지 승패가 아니다.</summary>
    private static BattleSim OnePattern(FighterConfig fighter, string pattern) => new(new BattleSetup
    {
        Arena = TestConfigs.Arena(),
        Fighter = fighter,
        HitShapes = TestConfigs.HitShapes(),
        Boss = TestConfigs.Boss(maxHealth: 999_999),
        PatternIds = new[] { pattern },
        Patterns = TestConfigs.Patterns(),
        Seed = 51,
        MaxTicks = 60 * 30,
    });

    // ── 역습: 미끼는 2타 직후의 칼질을 친다 ──────────────────────────────────

    [Fact]
    public void 역습의_미끼는_2타_직후에_지른_칼이_휘두르는_도중에_친다()
    {
        // **이 변종의 문장 그대로다** (patterns.json 의 `III-역습` _note): 2타 뒤 간격이 계열의 미끼이고,
        // 이 변종만 거기에 판정을 끼워 **2타 직후에 지른 칼을 휘두른 자세 그대로** 친다.
        //
        // 선딜이 0.3333 이던 때는 2타(1.40) 다음 틱에 누른 칼이 1.8166 에 닿고 1.90 에 끝나서 미끼 1.85 가
        // 그 사이였다. 선딜이 0.0833 이 되자(이슈 #54) 같은 칼이 2타 뒤 16틱에 **이미 끝나** 있어, 미끼가
        // 칼질을 마치고 서 있는 몸을 쳤다 — 욕심을 벌하는 변종이 그냥 서 있는 것을 벌했다.
        // 이 테스트가 그 자리에서 빨갛게 섰다(GreedWindow 가 거짓).
        //
        // 누르는 자리는 **2타가 선 다음 틱**이다 — "2타를 받았다, 이제 내 차례" 의 가장 이른 자리다.
        // 칼질이 몇 틱인지를 여기 적지 않는다: 실제 캐릭터(fighters.json)가 규칙을 돌며 정한다.
        foreach ((string id, FighterConfig fighter) in TestConfigs.Fighters())
        {
            DodgeEvent bait = TapAfterSecondHit(fighter, "내려찍기 III-역습");

            bait.Finisher.ShouldBeFalse($"{id}: 2타 다음에 온 것이 마무리다 — 미끼 판정이 없다");
            bait.GreedWindow.ShouldBeTrue(
                $"{id}: 미끼가 칼질이 끝난 뒤에 왔다 — 2타 직후에 지른 칼을 휘두른 자세 그대로 못 친다");
            bait.Verdict.ShouldBe(HitVerdict.Hit, $"{id}: 칼질 중인 몸에 미끼가 안 닿았다");
        }
    }

    /// <summary>
    /// 붙은 채로 선 패턴에서 2타가 선 <b>다음 틱</b>에 탭을 누르고, 그 뒤에 서는 판정(셋째 관측)을 돌려준다.
    /// 앞의 두 대는 그대로 맞는다 — 방어하지 않는 것이 이 자리의 전제다(욕심만 잰다).
    /// </summary>
    private static DodgeEvent TapAfterSecondHit(FighterConfig fighter, string pattern)
    {
        BattleSim sim = OnePattern(fighter, pattern);
        int round = -1;
        string? last = null;
        bool tapped = false;

        for (int t = 0; t < 60 * 30; t++)
        {
            double gap = Math.Abs(sim.Fighter.X - sim.Boss.X);
            sbyte toward = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);
            bool fresh = sim.Boss.CurrentPattern is not null && last is null;
            last = sim.Boss.CurrentPattern;

            // 개막의 첫 패턴은 멀리서 선다 — **붙은 뒤에 새로 선** 패턴의 한 바퀴만 본다.
            if (round < 0 && fresh && gap <= _close)
            {
                round = sim.Events.Count;
            }

            bool tap = round >= 0 && !tapped && sim.Events.Count == round + 2;
            tapped |= tap;
            sbyte move = round < 0 && gap > _close ? toward : (sbyte)0;
            sim.Tick(new InputFrame(move, false, false, false, Attack: tap));

            if (round >= 0 && sim.Events.Count > round + 2)
            {
                tapped.ShouldBeTrue("2타 뒤에 누르기도 전에 셋째가 왔다 — 이 도우미가 아무것도 안 잰다");
                return sim.Events[round + 2];
            }
        }

        throw new Xunit.Sdk.XunitException($"{pattern}: 붙은 채로 선 패턴의 셋째 판정을 못 봤다");
    }

    // ── 끌기: 밀린 마무리는 뼈대의 박자로 뛴 대시의 착지를 친다 ─────────────────

    [Theory]
    [InlineData("내려찍기 II-끌기")]
    [InlineData("내려찍기 III-끌기")]
    public void 끌기의_마무리는_1단계_박자에_맞춰_뛴_대시의_착지를_친다(string pattern)
    {
        // 대시 의존자는 1단계에서 박자를 외운다 — **1단계의 마무리가 오는 그 틱에** 밖으로 뛴다.
        // 끌기는 마무리를 밀어 그 대시가 **착지한 자리**(서는 자리 115 + 대시 367 = 482)를 친다.
        // 뼈대를 넓히고 끌기를 그대로 두면 "밀린" 마무리가 뼈대의 마무리보다 **먼저** 와서 대시하기
        // 전의 몸을 품 안(290 미만)에서 헛친다 — 봉인이 통째로 사라진다. 그래서 대시 시각을 손으로 안 적고
        // 뼈대의 데이터에서 읽는다.
        int beat = ActiveTicks(_base)[^1];

        foreach ((string id, FighterConfig fighter) in TestConfigs.Fighters())
        {
            DodgeEvent finisher = DashOnBaseBeat(fighter, pattern, beat);

            finisher.Verdict.ShouldBe(HitVerdict.Hit,
                $"{id}: 1단계 박자로 밖으로 뛴 대시가 {pattern} 의 마무리를 {finisher.Verdict} 로 넘겼다 — 봉인이 없다");
            finisher.Distance.ShouldBeGreaterThan(290, $"{id}: 착지점이 아니라 품 안에서 맞았다");
            finisher.Distance.ShouldBeLessThan(620, $"{id}: 착지점을 지나쳐서 맞았다");
        }
    }

    /// <summary>
    /// 붙은 채로 선 패턴에서, 패턴이 선 뒤 <paramref name="beat"/> 번째 틱에 <b>보스를 등지고</b> 대시한다.
    /// 대시는 바라보는 쪽으로만 가므로(Fighter.Move) 한 틱 먼저 등을 돌린다. 마무리의 관측을 돌려준다.
    /// </summary>
    private static DodgeEvent DashOnBaseBeat(FighterConfig fighter, string pattern, int beat)
    {
        BattleSim sim = OnePattern(fighter, pattern);
        int since = -1;
        string? last = null;

        for (int t = 0; t < 60 * 30; t++)
        {
            double gap = Math.Abs(sim.Fighter.X - sim.Boss.X);
            sbyte toward = (sbyte)(sim.Fighter.X < sim.Boss.X ? 1 : -1);
            bool fresh = sim.Boss.CurrentPattern is not null && last is null;
            last = sim.Boss.CurrentPattern;

            if (since < 0 && fresh && gap <= _close)
            {
                since = 0;
            }

            int before = sim.Events.Count;
            if (since < 0)
            {
                sim.Tick(new InputFrame(gap > _close ? toward : (sbyte)0, false, false, false, false));
                continue;
            }

            since++;
            sbyte away = (sbyte)-toward;
            sim.Tick(new InputFrame(
                since == beat - 1 ? away : (sbyte)0, false, Dash: since == beat, false, false));

            for (int i = before; i < sim.Events.Count; i++)
            {
                if (sim.Events[i].Finisher)
                {
                    return sim.Events[i];
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"{pattern}: 붙은 채로 선 패턴의 마무리를 못 봤다");
    }

    // ── 쐐기: 판정 넷을 뼈대의 마지막 간격으로 끝낸다 ──────────────────────────

    [Theory]
    [InlineData("내려찍기 II-쐐기")]
    [InlineData("내려찍기 III-쐐기")]
    public void 쐐기는_앞이_촘촘하고_마무리_앞_간격은_1단계와_같다(string pattern)
    {
        // 쐐기의 박자는 **뼈대의 변주**다: 앞의 셋을 뼈대의 첫 간격보다 촘촘하게 몰고(대시하는 사람에게
        // 회복을 덜 준다 — 그 변종의 _note), 빨간 마무리 앞은 뼈대와 **같은** 간격을 둔다. 뼈대만 넓히고
        // 쐐기를 두면 마무리 앞이 뼈대보다 짧아져 쐐기만 박자가 급해진다 — 유저가 "간격이 짧다" 고 한
        // 바로 그것을 쐐기에만 남기게 된다. 시각은 데이터의 초로 견준다(반 틱 여유 — FighterDataTests 와 같다).
        PatternDef baseDef = TestConfigs.Patterns()[_base];
        PatternDef wedge = TestConfigs.Patterns()[pattern];
        List<double> b = baseDef.Timeline.Where(s => s.Kind == "active").Select(s => s.T).ToList();
        List<double> w = wedge.Timeline.Where(s => s.Kind == "active").Select(s => s.T).ToList();

        (w[^1] - w[^2]).ShouldBe(b[^1] - b[^2], BattleSim.Dt / 2,
            $"{pattern}: 빨간 마무리 앞 간격이 1단계({b[^1] - b[^2]:0.00})와 다르다");

        for (int i = 1; i < w.Count - 1; i++)
        {
            (w[i] - w[i - 1]).ShouldBeLessThan(b[1] - b[0],
                $"{pattern}: {i}번째 간격이 1단계의 첫 간격({b[1] - b[0]:0.00})보다 안 촘촘하다");
        }
    }

    [Fact]
    public void 쐐기가_깬_가드는_빨간_마무리가_올_때까지_굳어_있다()
    {
        // `III-쐐기` 봉인의 **뒤쪽 절반**이다 (WedgeSealTests 가 실제 판으로 본다). 셋째 대가 붙든 가드를
        // 스태미나로 깨면 guard_break_lock 동안 굳는데, 그 고정이 빨간 마무리까지 **덮어야** "무너진 채로
        // 맞는다 — 받아칠 기회조차 없다" 가 선다. 고정이 먼저 풀리면 깨진 사람이 다시 누를 틈이 생기고,
        // 그 누름이 마무리 앞 8틱 안이면 **빨강을 받아쳐** 2.3초 경직을 얻는다 — 봉인이 상으로 뒤집힌다.
        //
        // **이슈 #54 에서 실제로 뒤집혔다.** 고정 0.9(54틱)는 옛 미끼 간격 0.90 에서 온 값이었고
        // (설계 §1.2: "붕괴하면 남은 타격을 그대로 맞는 길이"), 간격을 1.10(66틱)으로 넓히자 고정이
        // 마무리 12틱 앞에 풀려 붙든 가드가 다시 섰다. 그래서 고정도 간격을 따라 1.1 이 됐다.
        // 반 틱 여유는 데이터의 초가 틱에 닿는 누산 오차다(FighterDataTests 와 같다).
        PatternDef def = TestConfigs.Patterns()["내려찍기 III-쐐기"];
        List<PatternStep> hits = def.Timeline.Where(s => s.Kind == "active").ToList();
        int heavy = hits.FindIndex(s => s.Damage == hits.Take(hits.Count - 1).Max(h => h.Damage));
        heavy.ShouldBe(hits.Count - 2, "무거운 대가 마무리 바로 앞이 아니다 — 이 산수가 다른 대를 잰다");
        double gap = hits[^1].T - hits[heavy].T;

        foreach ((string id, FighterConfig c) in TestConfigs.Fighters())
        {
            (c.GuardBreakLock + (BattleSim.Dt / 2)).ShouldBeGreaterThanOrEqualTo(gap,
                $"{id}: 붕괴 고정 {c.GuardBreakLock}초가 셋째 → 빨간 마무리 {gap:0.00}초보다 짧다"
                + " — 깨진 사람이 빨강 앞에서 다시 선다");
        }
    }

    // ── 역린: 헛스윙은 마무리 앞, 기억 창 안 · 패리 창 밖에 선다 ─────────────────

    [Fact]
    public void 역린의_헛스윙은_마무리_앞_기억_창_안이고_패리_창_밖이다()
    {
        // 역린이 패리 의존을 봉인하는 산수는 두 창 사이에 서 있다 (patterns.json 의 `III-역린` _note):
        //   · 헛스윙에 지른 누름과 마무리에 지른 누름이 **기억 창 안**이어야 연타 사슬이 자라
        //     마무리의 패리 창이 깎인다(0.133 → 0.1).
        //   · 헛스윙에 누르고 버틴 누름이 마무리에서 **정확 창 밖**이어야 그 누름이 낡아 가드가 되고,
        //     빨간 마무리가 그 가드를 깬다.
        // 이슈 #54 가 뼈대를 넓히며 헛스윙 → 마무리가 0.30 → 0.37 이 됐다. 뼈대를 더 넓히다 이 간격이
        // 기억 창(0.5)을 넘으면 헛스윙은 아무것도 안 깎는 빈 시간이 된다 — 여기서 빨개진다.
        PatternDef def = TestConfigs.Patterns()["내려찍기 III-역린"];
        double feint = def.Timeline.Single(s => s.Kind == "feint").T;
        double finisher = def.Timeline.Last(s => s.Kind == "active").T;

        foreach ((string id, FighterConfig c) in TestConfigs.Fighters())
        {
            (finisher - feint).ShouldBeLessThan(c.ParryMemoryWindow,
                $"{id}: 헛스윙 → 마무리 {finisher - feint:0.00}초가 기억 창 밖이다 — 헛스윙에 지른 패리가 아무것도 안 깎는다");
            (finisher - feint).ShouldBeGreaterThan(c.ParryPreciseWindow,
                $"{id}: 헛스윙 → 마무리 {finisher - feint:0.00}초가 패리 창 안이다 — 헛스윙에 누른 것이 마무리를 그대로 받아친다");
        }
    }
}
