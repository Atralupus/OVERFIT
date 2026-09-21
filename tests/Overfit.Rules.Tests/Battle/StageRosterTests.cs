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

    [Fact]
    public void 실제_stages_json_이_읽힌다()
    {
        Stages().ShouldNotBeEmpty();
    }

    [Fact]
    public void 명부가_적힌_그대로_순서까지_나온다()
    {
        // 순서가 계약이다 — Det 의 뽑기 좌표가 이 리스트의 인덱스다.
        StageRoster.For(Stages(), 1).ShouldBe(new[] { "횡베기", "지면쓸기" });
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
        for (int stage = 2; stage <= 5; stage++)
        {
            IReadOnlyList<string> now = StageRoster.For(Stages(), stage);
            now.Count.ShouldBeGreaterThanOrEqualTo(before.Count, $"{stage}단계가 앞 단계보다 짧다");
            before = now;
        }
    }

    [Fact]
    public void 명부가_설계한_패턴_수를_넘지_않는다()
    {
        // want 는 설계가 정한 단계별 패턴 수(2·3·5·7·10)다. 모자란 것은 로그로 드러나지만
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
        IReadOnlyList<string> roster = StageRoster.For(Stages(), 5);

        roster.ShouldContain(
            id => patterns[id].Timeline.Any(s => s.Kind == "active" && s.Distance![0] > 0),
            "마지막 단계 명부에 안쪽 안전지대를 가진 패턴이 없다 — dash_direction_bias 가 상수가 된다");
    }

    [Fact]
    public void 설계보다_짧은_단계는_조용히_넘어가지_않는다()
    {
        // 프로토타입은 패턴이 여섯뿐이라 4·5단계가 설계(7·10)에 못 미친다. 줄여서 감추면
        // 나중에 "단계가 올라도 왜 안 어려워지지" 를 로그에서 찾을 수 없다.
        using var log = new LogCapture();

        StageRoster.For(Stages(), 5);

        log.Lines.ShouldContain(l => l.Contains("short stage=5", System.StringComparison.Ordinal));
    }

    [Fact]
    public void 정의된_범위_밖의_단계는_잘라_쓰고_경고한다()
    {
        // 여기서 [E] 를 내면 헤드리스 판정이 실패로 본다. 없는 단계를 달라고 한 것은
        // 데이터 손상이 아니라 호출자의 범위 문제라 경고가 맞다.
        using var log = new LogCapture();

        StageRoster.For(Stages(), 99).ShouldBe(StageRoster.For(Stages(), 5));

        log.Lines.ShouldContain(l => l.Contains("out_of_range asked=99 used=5", System.StringComparison.Ordinal));
        log.Lines.ShouldNotContain(l => l.StartsWith("[stage][E]", System.StringComparison.Ordinal));
    }
}
