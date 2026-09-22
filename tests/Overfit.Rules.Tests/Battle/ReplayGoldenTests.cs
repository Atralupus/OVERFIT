using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 같은 시드 · 같은 입력이 <b>커밋을 넘어서도</b> 같은 판을 내는가.
/// 프로세스 안의 두 판을 견주는 <c>BattleSimTests</c> 와 다르다 — 그쪽은 오늘의 코드끼리 견주고,
/// 이쪽은 오늘의 코드를 <b>어제 박아둔 값</b>과 견준다.
///
/// <para>
/// 이게 깨지면 나중에 만든 학습 데이터를 재현할 수 없다. 물리 상수 하나, 연산 순서 하나면 갈린다.
/// </para>
///
/// <para>
/// 틱수·체력·관측수만 박아두던 때는 <b>계측이 통째로 골든 밖</b>이었다. 부호를 뒤집든, 기준 시각을
/// 한 틱 옮기든, 분기 순서를 바꾸든 이 네 숫자는 그대로인데 망이 먹을 특징은 전부 달라졌다.
/// 그래서 회피 관측 스트림 전체의 다이제스트를 같이 박는다 — 10축은 이 스트림의 집계이므로
/// 스트림을 덮으면 축도 덮인다.
/// </para>
/// </summary>
public class ReplayGoldenTests
{
    /// <summary>골든을 만든 입력 시퀀스. <b>이 함수를 바꾸면 골든도 바꿔야 한다.</b></summary>
    private static InputFrame[] Script()
    {
        var inputs = new InputFrame[60 * 60];
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i] = new InputFrame(
                (sbyte)(i % 11 < 5 ? 1 : -1),
                Jump: i % 37 == 0,
                Dash: i % 23 == 0,
                Parry: i % 29 == 0,
                Attack: i % 17 == 0);
        }

        return inputs;
    }

    /// <summary>
    /// 회피 관측 스트림의 다이제스트. <b>모든 필드를 다 넣는다</b> — 하나라도 빼면 그 필드는
    /// 다시 골든 밖이 된다.
    ///
    /// <para>
    /// FNV-1a 를 손으로 짠다. <c>string.GetHashCode()</c> 는 .NET 에서 <b>프로세스마다 다르다</b> —
    /// 그걸로 박아두면 골든이 실행할 때마다 깨진다. 실수를 못 하게 여기 이유를 적어 둔다.
    /// </para>
    /// </summary>
    private static string Digest(IReadOnlyList<DodgeEvent> events)
    {
        var text = new StringBuilder();
        foreach (DodgeEvent e in events)
        {
            text.Append(CultureInfo.InvariantCulture, $"{e.PatternId}|{e.Verb}|{e.Verdict}|");
            text.Append(CultureInfo.InvariantCulture, $"{e.TimingError:0.0000}|{e.Direction}|");
            text.Append(CultureInfo.InvariantCulture, $"{e.Airborne}|{e.Distance:0.000}|{e.GreedWindow}|");
            // 차지 단계도 넣는다 (이슈 #40). 이 스크립트는 한 번도 안 모으므로 값은 전부 0 이지만,
            // 빼 두면 "모으고 맞았다" 가 골든 밖이 되어 배수 규칙을 통째로 바꿔도 초록이다.
            text.Append(CultureInfo.InvariantCulture, $"{e.ChargeTier}|");
            // 가능했던 수단도 넣는다. 패턴 id 에서 따라 나오는 값처럼 보이지만, patterns.json 의
            // 태그를 고치면 id 는 그대로인 채 의존도 축의 분모가 통째로 달라진다 —
            // 다이제스트에서 빼면 그 변화가 골든 밖이 된다.
            text.Append(CultureInfo.InvariantCulture, $"{e.DashAvailable}|{e.JumpAvailable}|{e.ParryAvailable}\n");
        }

        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(text.ToString()))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    [Fact]
    public void 골든과_같은_판이_나온다()
    {
        string[] golden = File.ReadAllLines(Path.Combine("replay_golden.txt"));
        string line = Array.Find(golden, l => l.Length > 0 && !l.StartsWith('#'))
            ?? throw new InvalidOperationException("골든 파일에 값 줄이 없다");
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        parts.Length.ShouldBe(6, "골든 형식은 <시드> <틱수> <파이터HP> <보스HP> <관측수> <관측다이제스트> 다");

        ulong seed = ulong.Parse(parts[0], CultureInfo.InvariantCulture);
        Dictionary<string, StageDef> stages = JsonData<StageDef>.ParseTable(
            File.ReadAllText(Path.Combine("data", "stages.json")), "stages.json");

        var sim = new BattleSim(new BattleSetup
        {
            Arena = TestConfigs.Arena(),
            Fighter = TestConfigs.Fighter(),
            Boss = TestConfigs.Boss(),
            // 패턴 id 를 여기 베껴 적지 않는다 — 베끼면 stages.json 이 바뀌어도 골든이 초록이라
            // "실제로 도는 전투" 와 "골든이 도는 전투" 가 조용히 갈린다.
            PatternIds = StageRoster.For(stages, 5),
            Patterns = JsonData<PatternDef>.ParseTable(
                File.ReadAllText(Path.Combine("data", "patterns.json")), "patterns.json"),
            Seed = seed,
            MaxTicks = TestConfigs.MaxTicks(),
        });

        foreach (InputFrame input in Script())
        {
            if (sim.Tick(input) is not null)
            {
                break;
            }
        }

        string measured = string.Join(' ',
            seed.ToString(CultureInfo.InvariantCulture),
            sim.Ticks.ToString(CultureInfo.InvariantCulture),
            sim.Fighter.Health.ToString(CultureInfo.InvariantCulture),
            sim.Boss.Health.ToString(CultureInfo.InvariantCulture),
            sim.Events.Count.ToString(CultureInfo.InvariantCulture),
            Digest(sim.Events));

        measured.ShouldBe(string.Join(' ', parts),
            $"결정론이 깨졌습니다. 실측: {measured}\n"
            + "다이제스트만 다르면 판의 겉모습은 같고 **계측 값이 달라진 것**입니다 — 회피 verb·판정·타이밍·거리 중 하나입니다.\n"
            + "일부러 바꾼 것이면 tools/replay_golden.txt 를 이 값으로 고치고 PR 에 무엇을 왜 바꿨는지 적으십시오.");
    }
}
