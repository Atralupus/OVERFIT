using System.Collections.Generic;
using Overfit.Battle.Rules;
using Overfit.Battle.View;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 착지의 흰 충격파 (#83) — 뷰가 <b>규칙이 이 틱에 대 본 보스 판정 사각형</b>에서 모양을 읽는다(<see cref="FloorWave"/>).
/// 보는 것은 둘이다: 충격파가 <b>바닥 전체를 치는 판정에만</b> 서는가, 그 높이와 끝이 <b>판정의 것</b>인가 — "보이는 것이 곧 맞는 것".
/// </summary>
public class FloorWaveTests
{
    /// <summary>바닥의 폭 — 실제 아레나. 1920 을 베껴 적지 않는다.</summary>
    private static readonly double _floor = TestConfigs.Arena().Width;

    private static IReadOnlyList<HitRect> BandAt(double x, double inner, double outer, double low, double high, double y = 0) =>
        HitShape.Band(inner, outer, low, high).Place(new Placement(x, y, -1));

    [Fact]
    public void 바닥_전체에_걸친_띠는_발밑에서_시작해_판정의_끝까지_가는_충격파다()
    {
        FloorWave wave = FloorWave.Find(BandAt(595, 0, _floor, 0, 60), 595, _floor).ShouldNotBeNull();

        wave.Origin.ShouldBe(595, "충격파가 착지한 보스의 발밑에서 시작하지 않는다");
        wave.Left.ShouldBe(595 - _floor, "왼쪽 끝이 판정의 왼끝이 아니다");
        wave.Right.ShouldBe(595 + _floor, "오른쪽 끝이 판정의 오른끝이 아니다");
        wave.Height.ShouldBe(60, "띠의 높이가 판정의 높이가 아니다");
    }

    [Fact]
    public void 높이는_손으로_옮겨_적은_값이_아니라_판정의_높이다()
    {
        // 착지 띠는 지금 60 이다. 60 이 아닌 띠로 재야 "판정에서 읽는다" 와 "60 을 박았다" 가 갈린다.
        FloorWave.Find(BandAt(960, 0, _floor, 0, 45), 960, _floor).ShouldNotBeNull().Height.ShouldBe(45);
    }

    [Theory]
    [InlineData(60, 120)]
    [InlineData(120, 60)]
    public void 윗끝이_다른_두_장이_이어지면_낮은_쪽이_충격파의_높이다(double leftTop, double rightTop)
    {
        // 맞닿은 두 장이 함께 바닥을 다 덮는데 윗끝이 60 과 120 이다. 띠 안의 모든 자리가 실제로 맞으려면 낮은 쪽(60)을 그린다 — 높은
        // 쪽을 그리면 발이 60 ~ 120 에 뜬 사람이 한쪽 절반에서는 안 맞는데 띠 안에 그려진다. 목록은 일부러 오른쪽 장이 먼저다(정렬도 잰다).
        double mid = _floor / 2;
        var rects = new List<HitRect> { new(mid, _floor, 0, rightTop), new(0, mid, 0, leftTop) };

        FloorWave.Find(rects, mid, _floor).ShouldNotBeNull().Height.ShouldBe(60, "이어진 판정 중 높은 윗끝을 띠의 높이로 읽었다");
    }

    [Fact]
    public void 발이_이어진_판정_밖이면_출발점은_판정의_끝으로_당긴다()
    {
        // 바닥을 꼭 맞게 덮는 띠 [0, 폭] — 발 중심이 그 밖이면 충격파는 가까운 끝에서 출발한다. 판정 밖에서 출발하면 그 쪽 앞머리가
        // 바깥에서 안으로 거꾸로 달린다.
        IReadOnlyList<HitRect> exact = BandAt(_floor / 2, 0, _floor / 2, 0, 60);

        FloorWave.Find(exact, _floor + 300, _floor).ShouldNotBeNull().Origin.ShouldBe(_floor, "오른끝 밖의 발에서 출발한다");
        FloorWave.Find(exact, -300, _floor).ShouldNotBeNull().Origin.ShouldBe(0, "왼끝 밖의 발에서 출발한다");
    }

    [Fact]
    public void 바닥을_다_덮지_못하는_판정은_충격파가_아니다()
    {
        FloorWave.Find(BandAt(960, 0, 400, 0, 60), 960, _floor).ShouldBeNull("사거리 400 인 띠가 바닥 전체를 친다고 읽혔다");
    }

    [Fact]
    public void 가운데가_빈_띠는_이어진_바닥이_아니다()
    {
        // 안쪽 주머니가 있는 띠(안쪽 100) — 양끝만 보면 바닥을 다 덮지만, 보스 발밑 200px 이 비어 있다. 그 틈에 선 사람은 안 맞으므로
        // 발밑에서 퍼지는 충격파는 거짓말이다.
        FloorWave.Find(BandAt(960, 100, _floor + 200, 0, 60), 960, _floor).ShouldBeNull("가운데가 빈 띠를 하나로 이어 읽었다");
    }

    [Fact]
    public void 공중의_띠는_바닥을_치지_않는다()
    {
        // 발이 100 위인 보스가 댄 띠는 100 ~ 160 을 친다 — 바닥에 선 사람은 안 맞는다.
        FloorWave.Find(BandAt(960, 0, _floor, 0, 60, y: 100), 960, _floor).ShouldBeNull("공중의 띠를 바닥 충격파로 읽었다");
    }

    [Fact]
    public void 대_본_판정이_없으면_충격파도_없다()
    {
        FloorWave.Find(new List<HitRect>(), 960, _floor).ShouldBeNull();
    }

    [Fact]
    public void 그림에서_뽑은_칼은_어디서_휘둘러도_충격파가_아니다()
    {
        // hitboxes.json 의 모양 전부 — 3연격의 세 칼과 파이터 칼. 아레나 어디서든, 어느 쪽을 보든 바닥 전체를 안 덮는다.
        foreach ((string id, HitShape shape) in TestConfigs.HitShapes())
        {
            foreach (double x in new[] { 85.0, 960.0, _floor - 85 })
            {
                foreach (int facing in new[] { -1, 1 })
                {
                    FloorWave.Find(shape.Place(new Placement(x, 0, facing)), x, _floor)
                        .ShouldBeNull($"{id} 를 x={x} facing={facing} 에서 휘두른 것이 바닥 충격파로 읽혔다");
                }
            }
        }
    }

    [Fact]
    public void 앞머리는_발밑에서_출발해_다_퍼지면_판정의_끝에_닿는다()
    {
        var wave = new FloorWave(Origin: 595, Left: 595 - _floor, Right: 595 + _floor, Height: 60);

        wave.Fronts(0).ShouldBe((595.0, 595.0), "퍼지기 전의 앞머리가 발밑에 없다");
        wave.Fronts(1).ShouldBe((wave.Left, wave.Right), "다 퍼졌는데 판정의 끝에 안 닿았다");
        wave.Fronts(-1).ShouldBe((595.0, 595.0), "퍼지기 전 시각을 발밑 밖으로 읽었다");
        wave.Fronts(3).ShouldBe((wave.Left, wave.Right), "다 퍼진 뒤에 판정의 끝을 넘어간다");

        (double left, double right) = wave.Fronts(0.5);
        left.ShouldBeInRange(wave.Left + 1, 594, "반쯤 퍼진 왼쪽 앞머리가 발밑과 왼끝 사이에 없다");
        right.ShouldBeInRange(596, wave.Right - 1, "반쯤 퍼진 오른쪽 앞머리가 발밑과 오른끝 사이에 없다");
    }

    [Fact]
    public void 실제_패턴에서는_바닥_전체를_치는_띠의_창에만_서고_모양은_그_띠의_것이다()
    {
        // 판정 모양을 뷰에 따로 적지 않았다는 증명 — 실제 patterns.json 을 판에 올려 규칙이 대 본 사각형으로 묻는다. 기대값은
        // 타임라인의 band [안쪽, 바깥쪽, 아래, 위] 에서 따로 낸다: 안쪽이 0 이고 아래가 바닥이며 보스 발에서 양쪽으로 바닥을 다 덮는 띠다.
        Arena arena = TestConfigs.Arena();
        int waveTicks = 0, bladeTicks = 0;
        foreach (string id in TestConfigs.Patterns().Keys)
        {
            BattleSim sim = OnePattern(id, arena);
            bool began = false;
            for (int i = 0; i < 60 * 30 && !(began && sim.Boss.CurrentPattern is null); i++)
            {
                sim.Tick(default);
                began |= sim.Boss.CurrentPattern is not null;
                if (sim.BossTestedRects.Count == 0)
                {
                    continue;
                }

                FloorWave? wave = FloorWave.Find(sim.BossTestedRects, sim.Boss.X, arena.Width);
                if (sim.BossStep?.Band is { } b && b[0] <= 0 && b[2] <= 0 && sim.Boss.Y <= 0
                    && sim.Boss.X - b[1] <= 0 && sim.Boss.X + b[1] >= arena.Width)
                {
                    FloorWave got = wave.ShouldNotBeNull($"{id}: 틱 {sim.Ticks} 의 바닥 띠에 충격파가 안 선다");
                    got.Height.ShouldBe(b[3], $"{id}: 충격파의 높이가 band 의 위({b[3]})가 아니다");
                    got.Origin.ShouldBe(sim.Boss.X, $"{id}: 충격파가 보스 발밑에서 시작하지 않는다");
                    got.Left.ShouldBe(sim.Boss.X - b[1], $"{id}: 왼끝이 band 의 바깥쪽이 아니다");
                    got.Right.ShouldBe(sim.Boss.X + b[1], $"{id}: 오른끝이 band 의 바깥쪽이 아니다");
                    waveTicks++;
                }
                else
                {
                    wave.ShouldBeNull($"{id}: 틱 {sim.Ticks} 의 판정(바닥 전체가 아니다)에 충격파가 선다");
                    bladeTicks++;
                }
            }
        }

        waveTicks.ShouldBeGreaterThan(0, "바닥 띠를 한 번도 못 봤다 — 이 테스트가 충격파 갈래를 안 잰다");
        bladeTicks.ShouldBeGreaterThan(0, "칼 판정을 한 번도 못 봤다 — 이 테스트가 아닌 갈래를 안 잰다");
    }

    [Fact]
    public void 충격파는_판정_창이_닫히기_전에_판정의_끝까지_퍼진다()
    {
        // feel.landing_wave_spread_seconds 는 뷰의 수치지만 규칙의 창과 짝이다 — 창보다 느리면 판정이 끝났는데 앞머리가 아직 가고 있다.
        // 바닥을 치는 띠(안쪽 0 · 아래 바닥)의 창마다 잰다. 창의 길이는 러너와 같은 반올림이다(BattleSim.TicksFor).
        FeelBalance feel = TestConfigs.Balance().Feel;
        int bands = 0;
        foreach ((string id, PatternDef def) in TestConfigs.Patterns())
        {
            foreach (PatternStep step in def.Timeline)
            {
                if (step.Band is not { } b || b[0] > 0 || b[2] > 0)
                {
                    continue;
                }

                bands++;
                double window = BattleSim.TicksFor(step.ActiveSeconds) * BattleSim.Dt;
                feel.LandingWaveSpreadSeconds.ShouldBeLessThanOrEqualTo(
                    window, $"{id}: t={step.T} 의 창({window:0.000}초)보다 충격파가 느리게 퍼진다");
            }
        }

        bands.ShouldBeGreaterThan(0, "바닥을 치는 띠가 patterns.json 에 없다 — 이 테스트가 아무것도 안 잰다");
        feel.LandingWaveSpreadSeconds.ShouldBeGreaterThan(0, "퍼지는 시간이 0 이면 퍼지지 않고 한 번에 선다");
        feel.LandingWaveFadeSeconds.ShouldBeGreaterThan(0, "다 퍼진 띠가 옅어지지 않고 한 프레임에 사라진다");
        feel.LandingWaveAlpha.ShouldBeInRange(0.05, 0.95, "띠가 안 보이거나 뒤를 다 가린다 — 반투명이어야 한다");
        feel.LandingWaveEdgeAlpha.ShouldBeInRange(feel.LandingWaveAlpha, 1.0, "앞머리가 띠보다 밝아야 퍼지는 쪽이 읽힌다");
        feel.LandingWaveEdgeWidth.ShouldBeGreaterThan(0, "앞머리의 폭이 0 이면 앞머리가 안 그려진다");
    }

    /// <summary>
    /// 패턴 하나만 도는 실제 판 — 실제 보스 · 실제 판정 모양 · 실제 타임라인. 보스는 안 죽는다(체력 999_999). 파이터는 가만히 서 있어
    /// 닿는 판정은 창의 첫 틱에 끝나고, 안 닿는 판정은 창 내내 대 본다 — 둘 다 이 테스트가 보는 "대 본 틱" 이다.
    /// </summary>
    private static BattleSim OnePattern(string id, Arena arena) =>
        new(new BattleSetup
        {
            Arena = arena,
            Fighter = TestConfigs.Fighter(),
            HitShapes = TestConfigs.HitShapes(),
            Boss = TestConfigs.Boss(maxHealth: 999_999),
            PatternIds = new[] { id },
            Patterns = TestConfigs.Patterns(),
            Seed = 1,
            MaxTicks = 60 * 30,
        });
}
