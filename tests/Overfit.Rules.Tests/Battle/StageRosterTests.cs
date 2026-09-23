using System.Collections.Generic;
using System.IO;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 단계 명부는 <b>데이터에 적혀 있어야</b> 한다. 전에는 <c>patterns.json</c> 의 키 순서에서
/// 앞 N 개를 잘라 썼는데, 그러면 패턴 하나를 파일 맨 위에 끼워 넣는 것만으로 1단계가
/// 다른 전투가 되고 그 전의 리플레이와 학습 데이터가 전부 조용히 달라진다.
/// </summary>
public class StageRosterTests
{
    private static Dictionary<string, StageDef> Stages() =>
        JsonData<StageDef>.ParseTable(File.ReadAllText(Path.Combine("data", "stages.json")), "stages.json");

    private static Dictionary<string, PatternDef> Patterns() =>
        JsonData<PatternDef>.ParseTable(File.ReadAllText(Path.Combine("data", "patterns.json")), "patterns.json");

    /// <summary>
    /// 정의된 마지막 단계. <b>숫자를 안 베낀다</b> — 단계 수는 설계가 바뀌면 같이 바뀐다
    /// (이슈 #48 이 다섯을 셋으로 줄였다). 베껴 두면 그때마다 무관한 테스트가 빨개지고,
    /// 더 나쁘게는 "5단계" 를 물어보는 테스트가 <b>잘린 3단계</b>를 보면서 통과한다.
    /// </summary>
    private static int LastStage()
    {
        int last = 0;
        foreach (string key in Stages().Keys)
        {
            if (int.TryParse(key, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n))
            {
                last = System.Math.Max(last, n);
            }
        }

        return last;
    }

    [Fact]
    public void 실제_stages_json_이_읽힌다()
    {
        Stages().ShouldNotBeEmpty();
    }

    [Fact]
    public void 명부가_적힌_그대로_순서까지_나온다()
    {
        // 순서가 계약이다 — Det 의 뽑기 좌표가 이 리스트의 인덱스다.
        StageRoster.For(Stages(), 1).ShouldBe(new[] { "내려찍기 I" });
    }

    [Fact]
    public void 명부의_모든_id_가_patterns_json_에_있다()
    {
        Dictionary<string, PatternDef> patterns = Patterns();
        foreach ((string stage, StageDef def) in Stages())
        {
            foreach (string id in def.Patterns)
            {
                patterns.ShouldContainKey(id, $"{stage}단계 명부의 {id} 가 patterns.json 에 없다");
            }
        }
    }

    [Fact]
    public void 한_단계_안에서_같은_패턴이_두_번_나오지_않는다()
    {
        // 중복이 있으면 그 패턴만 두 배로 뽑힌다 — 명부를 손으로 적는 대가다.
        foreach ((string stage, StageDef def) in Stages())
        {
            def.Patterns.Distinct().Count().ShouldBe(def.Patterns.Count, $"{stage}단계 명부에 중복이 있다");
        }
    }

    [Fact]
    public void 단계가_오를수록_명부가_줄지_않는다()
    {
        IReadOnlyList<string> before = StageRoster.For(Stages(), 1);
        for (int stage = 2; stage <= LastStage(); stage++)
        {
            IReadOnlyList<string> now = StageRoster.For(Stages(), stage);
            now.Count.ShouldBeGreaterThanOrEqualTo(before.Count, $"{stage}단계가 앞 단계보다 짧다");
            before = now;
        }
    }

    [Fact]
    public void 명부가_설계한_패턴_수를_넘지_않는다()
    {
        // want 는 설계가 정한 단계별 변종 수(1·3·5 · 이슈 #48)다. 모자란 것은 로그로 드러나지만
        // 넘치는 것은 아무 데도 안 남는다 — 새 패턴을 명부에 끼워 넣을 때 아직 자리가 없는
        // 낮은 단계에 얹으면 그 단계의 난이도 곡선이 조용히 달라진다.
        foreach ((string stage, StageDef def) in Stages())
        {
            def.Patterns.Count.ShouldBeLessThanOrEqualTo(def.Want, $"{stage}단계 명부가 want={def.Want} 보다 길다");
        }
    }

    [Fact]
    public void 안으로_파고들어야_안전한_패턴이_마지막_단계_명부에_있다()
    {
        // dash_direction_bias 축은 "안으로 파고들었나 밖으로 도망갔나" 를 잰다. 안쪽이 안전한
        // 패턴이 **명부에 없으면** 안으로 가는 것이 정답인 상황이 아예 없어 축이 상수가 된다 —
        // 죽은 입력은 망의 용량만 먹고 아무것도 가르치지 않는다.
        // patterns.json 에 있는 것만으로는 부족하다. 실제로 뽑히는 명부에 있어야 한다.
        Dictionary<string, PatternDef> patterns = Patterns();
        IReadOnlyList<string> roster = StageRoster.For(Stages(), LastStage());

        roster.ShouldContain(
            id => patterns[id].Timeline.Any(s => s.Kind == "active" && s.Distance![0] > 0),
            "마지막 단계 명부에 안쪽 안전지대를 가진 패턴이 없다 — dash_direction_bias 가 상수가 된다");
    }

    [Fact]
    public void 설계보다_짧은_단계는_조용히_넘어가지_않는다()
    {
        // 모자람을 조용히 삼키면 나중에 "단계가 올라도 왜 안 어려워지지" 를 로그에서 찾을 수 없다.
        //
        // ⚠ **손으로 세운 명부로 본다.** 전에는 진짜 stages.json 의 5단계를 물어봤다 — 백장의 패턴이
        // 셋뿐이라 3~5단계가 설계(5·7·10)에 늘 못 미쳤기 때문이다. 이슈 #48 이 단계를 셋으로 줄이면서
        // **세 단계 모두 want 를 정확히 채운다**(1·3·5). 그래서 진짜 데이터로는 이 경고를 볼 수 없고,
        // 그렇다고 이 가드를 지우면 다음에 모자란 단계가 생겼을 때 아무 데도 안 남는다.
        // 단언은 그대로 두고 **보는 대상만** 옮긴다.
        using var log = new LogCapture();
        var shortened = new Dictionary<string, StageDef>
        {
            ["1"] = new() { Want = 5, Patterns = new[] { "내려찍기 I" } },
        };

        StageRoster.For(shortened, 1);

        log.Lines.ShouldContain(l => l.Contains("short stage=1 want=5 have=1", System.StringComparison.Ordinal));
    }

    [Fact]
    public void 지금_명부는_설계한_수를_정확히_채운다()
    {
        // 위 가드가 진짜 데이터에서 떠난 이유를 **데이터로** 못박는다 (이슈 #48). 세 단계가
        // want 를 정확히 채우는 것이 지금의 사실이고, 그게 깨지면 위 가드가 아니라 여기가 빨개진다 —
        // "모자란 단계가 생겼다" 를 아무도 안 보는 채로 두지 않기 위해서다.
        foreach ((string stage, StageDef def) in Stages())
        {
            def.Patterns.Count.ShouldBe(def.Want, $"{stage}단계 명부가 want={def.Want} 와 다르다");
        }
    }

    [Fact]
    public void 명부에_구멍이_있으면_예외가_아니라_에러_로그를_남긴다()
    {
        // Math.Clamp 로 범위만 맞춘 뒤 바로 색인하던 때는, stages.json 의 키가 연속이라는
        // **적어둔 적 없는 가정**이 깨지는 순간 KeyNotFoundException 이었다. 예외는 우리 로그
        // 형식으로 안 찍혀 [E] 게이트에 안 걸리고, 엔진 ERROR 블록으로만 나온다.
        using var log = new LogCapture();
        var holed = new Dictionary<string, StageDef>
        {
            ["1"] = new() { Want = 1, Patterns = new[] { "내려찍기 I" } },
            ["3"] = new() { Want = 5, Patterns = new[] { "내려찍기 III-역린" } },
        };

        StageRoster.For(holed, 2).ShouldBeEmpty();

        log.Lines.ShouldContain(l => l.StartsWith("[stage][E] stage_missing", System.StringComparison.Ordinal));
    }

    [Fact]
    public void 패턴이_하나도_없는_명부는_경고가_아니라_거절이다()
    {
        // 전에는 short 경고만 내고 빈 목록을 그대로 돌려줬다. 그 목록은 BattleSim.Begin 에서
        // Det.RollInt(n: 0) 이 되어 ArgumentOutOfRangeException 으로 터졌다 —
        // 데이터가 깨진 것을 **쓰는 자리**에서 알게 되면 원인이 로그에 안 남는다.
        using var log = new LogCapture();
        var empty = new Dictionary<string, StageDef>
        {
            ["1"] = new() { Want = 2, Patterns = System.Array.Empty<string>() },
        };

        StageRoster.For(empty, 1).ShouldBeEmpty();

        log.Lines.ShouldContain(l => l.StartsWith("[stage][E] empty_roster", System.StringComparison.Ordinal));
    }

    [Fact]
    public void 정의된_범위_밖의_단계는_잘라_쓰고_경고한다()
    {
        // 여기서 [E] 를 내면 헤드리스 판정이 실패로 본다. 없는 단계를 달라고 한 것은
        // 데이터 손상이 아니라 호출자의 범위 문제라 경고가 맞다.
        using var log = new LogCapture();

        StageRoster.For(Stages(), 99).ShouldBe(StageRoster.For(Stages(), LastStage()));

        log.Lines.ShouldContain(l => l.Contains($"out_of_range asked=99 used={LastStage()}", System.StringComparison.Ordinal));
        log.Lines.ShouldNotContain(l => l.StartsWith("[stage][E]", System.StringComparison.Ordinal));
    }
}
