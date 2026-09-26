using Overfit.Battle.Rules;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 경직 게이지의 산수 (#71 · 설계 §4.5) — 판 없이 잰다. 언제 채우고 비우는지(결정타 · 탈진)는 <c>BossPoiseTests</c> 가 판 위에서 본다.
/// </summary>
public class PoiseGaugeTests
{
    /// <summary>실제 보스의 게이지 — bosses.json 의 세 키(100 · 1.2초 · 초당 10)로 선다.</summary>
    private static PoiseGauge Real() => PoiseGauge.For(TestConfigs.Boss());

    [Fact]
    public void 채우면_실제로_찬_양을_돌려주고_끝에서_멈춘다()
    {
        // 설계 §4.5 — 칼질 하나에 그 경직도만큼 차고, 끝에서 멈추고 넘치지 않는다. 로그의 poise= 는 **실제로 찬 양**이라
        // 넘친 몫까지 적으면 "왜 무너졌나" 를 틀리게 말한다.
        PoiseGauge gauge = Real();

        gauge.Fill(10).ShouldBe(10);
        gauge.Fill(45).ShouldBe(45);
        gauge.Fill(10).ShouldBe(10);
        gauge.Full.ShouldBeFalse("65 에 찼다");
        gauge.Fill(45).ShouldBe(35, "넘친 10 까지 찼다고 한다");
        gauge.Value.ShouldBe(100);
        gauge.Full.ShouldBeTrue();
    }

    [Fact]
    public void 맞은_뒤_72틱은_그대로이고_그_뒤로_틱마다_초당_10의_몫이_빠진다()
    {
        // 1.2초 = 72틱 (반올림은 BattleSim.TicksFor 한 곳). 초당 10 이면 한 틱에 1/6 이다 — 가득 찬 게이지가 10초에 빠진다.
        PoiseGauge gauge = Real();
        gauge.Fill(10);

        for (int t = 1; t <= 72; t++)
        {
            gauge.Tick();
            gauge.Value.ShouldBe(10, $"맞은 뒤 {t}틱째에 벌써 빠졌다");
        }

        gauge.Tick();
        gauge.Value.ShouldBe(10 - (10 * BattleSim.Dt), 1e-9, "유예가 끝난 틱에 초당 10 의 한 틱 몫이 안 빠졌다");

        for (int t = 0; t < 60; t++)
        {
            gauge.Tick();
        }

        gauge.Value.ShouldBe(0, "0 아래로 내려갔거나 10초 비율로 안 빠졌다");
    }

    [Fact]
    public void 새로_맞으면_유예가_처음부터_다시_선다()
    {
        // 꾸준히 찌르는 동안에는 게이지가 안 줄어든다(설계 §4.5 의 산수 — 1타만 스태미나가 버티는 빠르기로 찌르면 열 번째에 무너진다).
        // 그 빠르기가 약 1.0초마다다: 칼질 0.27 + 1타 뒤 경직 0.40(#82) + 한 번 값(14)을 채우는 Idle 0.35. 유예(1.2초) 안이다.
        PoiseGauge gauge = Real();
        gauge.Fill(10);
        for (int t = 0; t < 70; t++)
        {
            gauge.Tick();
        }

        gauge.Fill(10);
        for (int t = 0; t < 72; t++)
        {
            gauge.Tick();
        }

        gauge.Value.ShouldBe(20, "두 번째 칼이 유예를 다시 안 세웠다");
    }

    [Fact]
    public void 비우면_0_이다()
    {
        // 탈진할 때 비운다(설계 §4.3). 남은 유예는 볼 길이가 없다 — 0 에서는 빠질 것이 없고, 다음 칼이 유예를 다시 세운다.
        PoiseGauge gauge = Real();
        gauge.Fill(55);

        gauge.Empty();
        gauge.Value.ShouldBe(0);
        gauge.Full.ShouldBeFalse();

        gauge.Tick();
        gauge.Value.ShouldBe(0, "빈 게이지가 0 아래로 갔다");
    }
}
