using System;
using System.Globalization;
using System.IO;
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

    [Fact]
    public void 골든과_같은_판이_나온다()
    {
        string[] golden = File.ReadAllLines(Path.Combine("replay_golden.txt"));
        string line = Array.Find(golden, l => l.Length > 0 && !l.StartsWith('#'))
            ?? throw new InvalidOperationException("골든 파일에 값 줄이 없다");
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        parts.Length.ShouldBe(5, "골든 형식은 <시드> <틱수> <파이터HP> <보스HP> <관측수> 다");

        ulong seed = ulong.Parse(parts[0], CultureInfo.InvariantCulture);
        var sim = new BattleSim(new BattleSetup
        {
            Arena = new Arena(1920),
            Fighter = TestConfigs.Fighter(),
            Boss = new BossConfig
            {
                MaxHealth = 200,
                MoveSpeed = 160,
                HalfWidth = 120,
                PatternGap = 0.8,
                Sprite = "boss_test",
            },
            PatternIds = new[] { "횡베기", "지면쓸기", "대공찌르기", "연속베기", "내려찍기", "돌진" },
            Patterns = JsonData<PatternDef>.ParseTable(
                File.ReadAllText(Path.Combine("data", "patterns.json")), "patterns.json"),
            Seed = seed,
            MaxTicks = 60 * 180,
        });

        foreach (InputFrame input in Script())
        {
            if (sim.Tick(input) is not null)
            {
                break;
            }
        }

        var actual = new[] { sim.Ticks, sim.Fighter.Health, sim.Boss.Health, sim.Events.Count };
        var expected = new int[4];
        for (int i = 0; i < 4; i++)
        {
            expected[i] = int.Parse(parts[i + 1], CultureInfo.InvariantCulture);
        }

        actual.ShouldBe(expected,
            $"결정론이 깨졌습니다. 실측: {seed} {actual[0]} {actual[1]} {actual[2]} {actual[3]}\n"
            + "일부러 바꾼 것이면 tools/replay_golden.txt 를 이 값으로 고치고 PR 에 무엇을 왜 바꿨는지 적으십시오.");
    }
}
