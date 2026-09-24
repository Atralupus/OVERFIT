using System.IO;
using Overfit.Battle.Rules;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 판정 데이터 (이슈 #59 · 설계 §3.3). <c>hitboxes.json</c> 은 도구(<c>tools/extract_hitboxes.py</c>)가 쓰고
/// 사람은 안 고친다 — 여기서 보는 것은 <b>도구를 돌렸나</b>와 <b>그림과 같은 기준으로 돌렸나</b>다.
/// </summary>
public class HitShapeTableTests
{
    private static readonly string[] _ids =
    {
        "medieval_king/attack/2",
        "medieval_king/attack2/2",
        "medieval_king/attack3/2",
        "martial_hero/attack/4",
        "martial_hero/attack2/4",
    };

    private static string Json() => File.ReadAllText(Path.Combine("data", "hitboxes.json"));

    [Fact]
    public void 실제_hitboxes_json_을_읽는다()
    {
        var shapes = HitShapeTable.Parse(Json(), "hitboxes.json");

        foreach (string id in _ids)
        {
            shapes.ShouldContainKey(id);
            shapes[id].Local.Count.ShouldBeGreaterThan(0, $"{id} 가 비었다 — 도구를 돌렸나");
        }
    }

    [Fact]
    public void 배율은_balance_json_과_같다()
    {
        // 판정 크기와 그려지는 크기가 따로 놀면 "보이는 것이 곧 맞는 것" 이 무너진다.
        // 어느 팩이 보스이고 파이터인지는 bosses.json · fighters.json 의 sprite 가 말한다.
        BalanceData balance = TestConfigs.Balance();
        string bossSprite = TestConfigs.Bosses()[balance.Battle.Boss].Sprite;
        string fighterSprite = TestConfigs.Fighters()[balance.Battle.Fighter].Sprite;
        var defs = JsonData<HitShapeDef>.ParseTable(Json(), "hitboxes.json");

        foreach ((string id, HitShapeDef def) in defs)
        {
            string pack = id.Split('/')[0];
            double expected = pack == bossSprite ? balance.Feel.BossSpriteScale
                : pack == fighterSprite ? balance.Feel.FighterSpriteScale
                : double.NaN;
            expected.ShouldNotBe(double.NaN, $"{id}: {pack} 는 보스도 파이터도 아니다 — 배율을 모른다");
            def.Scale.ShouldBe(expected, $"{id}");
        }
    }

    /// <summary>
    /// 기준점이 그림과 맞나. 아래 값은 도구가 아니라 <b>그림에서 따로 잰</b> 흰 궤적의 끝이다
    /// (region 기준 · 발바닥 = region 아래끝 · 몸 중심 = region 가로 가운데 — 2026-09-24 PIL 로 잼).
    /// 도구의 칸(4px)만큼은 어긋날 수 있으므로 한 칸 안이면 맞은 것이다.
    ///
    /// <para>
    /// 이 테스트가 잡는 것: 원본 프레임의 아래끝을 바닥으로 잡으면 보스는 33px, 플레이어는 195px 어긋난다.
    /// 가로 가운데를 빼먹으면 수백 px 이다. 테스트는 PNG 없이 돌므로 이렇게 따로 잰 값과 견주는 것 말고는
    /// 기준점을 볼 방법이 없다.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("medieval_king/attack/2", -110.0, 390.5, 49.5, 236.5, 22.5)]
    [InlineData("martial_hero/attack/4", -45.0, 222.5, 10.0, 172.5, 10.5)]
    public void 기준점이_그림과_맞는다(string id, double x0, double x1, double y0, double y1, double oneCell)
    {
        HitRect bounds = HitShapeTable.Parse(Json(), "hitboxes.json")[id].Bounds;

        bounds.X0.ShouldBe(x0, oneCell, $"{id} 앞뒤가 어긋났다 — 몸 중심을 region 가운데로 잡았나");
        bounds.X1.ShouldBe(x1, oneCell, $"{id} 앞뒤가 어긋났다 — 몸 중심을 region 가운데로 잡았나");
        bounds.Y0.ShouldBe(y0, oneCell, $"{id} 높이가 어긋났다 — 발바닥을 region 아래끝으로 잡았나");
        bounds.Y1.ShouldBe(y1, oneCell, $"{id} 높이가 어긋났다 — 발바닥을 region 아래끝으로 잡았나");
    }

    [Fact]
    public void 사각형이_없는_칸은_거절한다()
    {
        const string json = """{ "a/b/1": { "scale": 1, "region": [0, 0, 1, 1], "rects": [] } }""";

        Should.Throw<DataException>(() => HitShapeTable.Parse(json, "t.json")).Message.ShouldContain("a/b/1");
    }

    [Fact]
    public void 사각형은_네_숫자여야_한다()
    {
        const string json = """{ "a/b/1": { "scale": 1, "region": [0, 0, 1, 1], "rects": [[0, 1, 2]] } }""";

        Should.Throw<DataException>(() => HitShapeTable.Parse(json, "t.json")).Message.ShouldContain("a/b/1");
    }

    [Fact]
    public void 뒤집힌_사각형은_거절한다()
    {
        const string json = """{ "a/b/1": { "scale": 1, "region": [0, 0, 1, 1], "rects": [[5, 1, 0, 1]] } }""";

        Should.Throw<DataException>(() => HitShapeTable.Parse(json, "t.json")).Message.ShouldContain("a/b/1");
    }
}
