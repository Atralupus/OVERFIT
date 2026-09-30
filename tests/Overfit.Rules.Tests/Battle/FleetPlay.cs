using System;
using System.Collections.Generic;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 함대 봇으로 실제 판을 끝까지 미는 차림 (#104) — 실제 캐릭터 · 실제 보스 · <see cref="StageRoster.Setup"/> 의 명부와 고르기 · 판과 봇에 같은 시드.
/// 데모의 차림(<c>BotPolicyTests.PlayDemo</c>)과 같다. 데이터는 한 번만 읽는다 — 관문이 백 판 넘게 돈다.
/// </summary>
internal static class FleetPlay
{
    private static readonly Lazy<BalanceData> _balance = new(TestConfigs.Balance);
    private static readonly Lazy<Dictionary<string, PatternDef>> _patterns = new(TestConfigs.Patterns);
    private static readonly Lazy<Dictionary<string, StageDef>> _stages = new(TestConfigs.Stages);
    private static readonly Lazy<Dictionary<string, HitShape>> _shapes = new(TestConfigs.HitShapes);
    private static readonly Lazy<FighterConfig> _fighter = new(() => TestConfigs.Fighters()[_balance.Value.Battle.Fighter]);
    private static readonly Lazy<BossConfig> _boss = new(() => TestConfigs.Bosses()[_balance.Value.Battle.Boss]);
    private static readonly Lazy<BeatTable> _beats = new(() => new BeatTable(_patterns.Value, TestConfigs.Fleet().RhythmReferences));

    /// <summary>
    /// 관문의 기준 성향 — 혼합형 · 수단 넷 고르게 · 반응 0.25 · 흔들림 0.05 · 편향 −0.02 · 리듬 없음 · 대시 방향 반반 · 붙어서 친다 ·
    /// 욕심 조금 · 2연격 반반. 한 성향만 바꿔 짝지은 축이 움직이는지 본다.
    /// </summary>
    public static readonly BotTraits Mid = new(
        BotHabit.Mixed, 0.25, 0.25, 0.25, 0.25, ReactionSeconds: 0.25, JitterSeconds: 0.05, BiasSeconds: -0.02, Rhythm: 0,
        DashInward: 0.5, RestGap: 0, Greed: 0.1, Chain: 0.5, JumpLead: 0.5);

    public static FighterConfig Fighter => _fighter.Value;

    public static BeatTable Beats => _beats.Value;

    /// <summary>
    /// <paramref name="stage"/> 단계의 판을 세울 차림 — 대본을 주면 그 전투만 대본으로 선다(스크린샷 · GIF 와 같은 길). 되살리기(<c>ReplayTests</c>)가
    /// 같은 차림에 저장한 입력만 다시 넣는다.
    /// </summary>
    public static BattleSetup Setup(int stage, ulong seed, IReadOnlyList<ScriptPlan>? script = null)
    {
        StageSetup setup = StageRoster.Setup(
                _stages.Value, stage, seed, Array.Empty<AttemptRecord>(), _patterns.Value, BattleSim.RestTicks(_boss.Value), _balance.Value.Picker, script)
            ?? throw new InvalidOperationException($"stages.json 에 {stage}단계가 안 선다");
        return new BattleSetup
        {
            Arena = new Arena(_balance.Value.Battle.ArenaWidth),
            Fighter = _fighter.Value,
            HitShapes = _shapes.Value,
            Boss = _boss.Value,
            PatternIds = setup.PatternIds,
            Patterns = _patterns.Value,
            Seed = seed,
            Picker = setup.Picker,
            MaxTicks = _balance.Value.Battle.MaxTicks,
        };
    }

    /// <summary><paramref name="stage"/> 단계의 판 — <see cref="Setup"/> 으로 선다.</summary>
    public static BattleSim Sim(int stage, ulong seed, IReadOnlyList<ScriptPlan>? script = null) => new(Setup(stage, seed, script));

    /// <summary>실제 동작 정의 — 되살리기가 대본의 캔슬 지점(초)을 칸으로 되찾을 때 읽는다.</summary>
    public static IReadOnlyDictionary<string, PatternDef> Patterns => _patterns.Value;

    /// <summary><paramref name="stage"/> 단계의 명부.</summary>
    public static IReadOnlyList<string> Roster(int stage) => StageRoster.For(_stages.Value, stage);

    public static FleetBot Bot(BotTraits traits, ulong seed) => new(traits, seed, Beats, Fighter);

    /// <summary>한 판을 끝까지 민다.</summary>
    public static (BattleOutcome Outcome, BattleSim Sim) Play(BotTraits traits, ulong seed, int stage = 1, IReadOnlyList<ScriptPlan>? script = null)
    {
        BattleSim sim = Sim(stage, seed, script);
        FleetBot bot = Bot(traits, seed);
        BattleOutcome? outcome = null;
        while (outcome is null)
        {
            outcome = sim.Tick(bot.Next(sim));
        }

        return (outcome.Value, sim);
    }
}
