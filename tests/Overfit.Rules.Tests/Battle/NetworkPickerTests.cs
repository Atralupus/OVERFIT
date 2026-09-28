using System;
using System.Collections.Generic;
using System.Linq;
using Overfit.Battle.Rules;
using Overfit.Core;
using Overfit.Rules.Tests.Support;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Battle;

/// <summary>
/// 망의 고르기 — 좁힌 명부 (#112 · 설계 2026-09-28 §6.2). 손으로 지은 작은 망(<see cref="TestNets"/> — 가중치 0 이라 로짓이 곧 출력 편향)으로
/// 들어 올림을 정해 본다. 학습한 망으로 테스트하면 학습할 때마다 테스트가 흔들린다.
/// </summary>
public class NetworkPickerTests
{
    private const ulong _seed = 51;

    private static NetworkPicker Picker(double[] logits, double[]? baseline = null, int events = 30, PickerBalance? knobs = null) =>
        new(new PickerInputs(TestNets.Heads, TestNets.History(events), _seed, 2, Network: new NetworkContext(
            TestNets.Constant(logits, baseline), knobs ?? TestNets.Knobs())));

    [Fact]
    public void 근거가_얇으면_다섯_전부다()
    {
        // 관측 12건 — min_samples(20)보다 적다. 도드라진 칸이 있어도 얇은 근거로 명부를 좁히지 않는다.
        NetworkPicker picker = Picker([0, 3, 0, 0, 0], events: 12);

        picker.Decision.Mode.ShouldBe(PickDecision.ModeFull);
        picker.Decision.Reason.ShouldBe(PickDecision.ReasonThin);
        picker.Decision.Samples.ShouldBe(12);
        picker.Decision.Narrowed.ShouldBe(new[] { 0, 1, 2, 3, 4 });
    }

    [Fact]
    public void 도드라진_칸이_없으면_다섯_전부다()
    {
        // 들어 올림이 전부 lift_min(0.405) 아래 — 고르게 약하거나 고르게 강한 사람이다. 약점 저격을 안 한다(설계 §6.2).
        NetworkPicker picker = Picker([0.1, 0.4, -2, 0.3, 0.0]);

        picker.Decision.Mode.ShouldBe(PickDecision.ModeFull);
        picker.Decision.Reason.ShouldBe(PickDecision.ReasonNoHabit);
        picker.Decision.Breathing.ShouldBeNull();
        picker.Decision.Lifts.ShouldNotBeNull().ShouldBe(new[] { 0.1, 0.4, -2, 0.3, 0.0 });
    }

    [Fact]
    public void 겨냥은_들어_올림이_큰_순서로_많아야_둘이고_숨통은_남은_칸_중_로짓이_가장_낮은_칸이다()
    {
        // 들어 올림 1.2(엇박) · 0.9(점프 3연속) · 0.5(잡기)가 문턱을 넘지만 겨냥은 둘 — 1.2 · 0.9. 숨통은 남은 셋(3연격 0.1 · 돌진 −0.3 · 잡기 0.5) 중
        // 로짓이 가장 낮은 돌진. 좁힌 명부는 명부 순서다.
        NetworkPicker picker = Picker([0.1, 0.9, -0.3, 0.5, 1.2]);

        picker.Decision.Mode.ShouldBe(PickDecision.ModeNarrowed);
        picker.Decision.Reason.ShouldBe(PickDecision.ReasonHabit);
        picker.Decision.Narrowed.ShouldBe(new[] { 1, 2, 4 });
        picker.Decision.Breathing.ShouldBe(2);
    }

    [Fact]
    public void 겨냥이_하나면_숨통과_둘이다()
    {
        NetworkPicker picker = Picker([0, 0, 0, 2, 0.1]);

        picker.Decision.Narrowed.ShouldBe(new[] { 0, 3 });
        picker.Decision.Breathing.ShouldBe(0, "로짓이 같으면 앞 칸이 숨통이다");
    }

    [Fact]
    public void 같은_들어_올림은_앞_칸이_먼저다()
    {
        // 0.6 이 셋 — 겨냥 둘은 앞 칸 둘(3연격 · 점프 3연속)이다. 숨통은 남은 셋 중 로짓이 가장 낮은 엇박.
        NetworkPicker picker = Picker([0.6, 0.6, 0.6, -1, -2]);

        picker.Decision.Narrowed.ShouldBe(new[] { 0, 1, 4 });
        picker.Decision.Breathing.ShouldBe(4);
    }

    [Fact]
    public void 들어_올림은_로짓에서_기저율의_로짓을_뺀_것이고_숨통은_로짓으로_고른다()
    {
        // 로짓이 다섯 모두 1.0 이어도 기저율이 다르면 들어 올림이 다르다 — 평균의 사람보다 이 사람에게 유독 먹히는 칸이 겨냥이다. 숨통은 들어
        // 올림이 아니라 로짓(이 사람이 가장 덜 맞을 패턴)으로 고른다: 넷이 같아 앞 칸.
        NetworkPicker picker = Picker([1, 1, 1, 1, 1], baseline: [0.8, 0.2, 1, 1, 1]);

        picker.Decision.Lifts.ShouldNotBeNull()[1].ShouldBe(0.8, 1e-12);
        picker.Decision.Narrowed.ShouldBe(new[] { 0, 1 });
        picker.Decision.Breathing.ShouldBe(0);
    }

    [Fact]
    public void 다섯_전부는_uniform_과_비트까지_같다()
    {
        // 망 갈래의 thin · no_habit 과 무작위 갈래는 뽑기가 같아야 한다(설계 §6.2 · §6.3) — 둘을 가르는 것은 갈래 이름뿐이다.
        foreach (NetworkPicker full in new[] { Picker([0, 3, 0, 0, 0], events: 1), Picker([0, 0, 0, 0, 0]) })
        {
            var uniform = new UniformPicker(_seed, 5);
            Enumerable.Range(0, 300).Select(full.Pick).ShouldBe(Enumerable.Range(0, 300).Select(uniform.Pick));
        }
    }

    [Fact]
    public void 좁힌_명부_밖의_칸을_안_낸다()
    {
        // 명부 밖 칸은 BattleSim 이 [E] 로 거절하는 자리다(#59 3/6) — 좁힌 명부는 명부의 인덱스만 싣는다. 뽑기는 좁힌 수 위의 RollInt 다.
        NetworkPicker picker = Picker([0.1, 0.9, -0.3, 0.5, 1.2]);
        int[] picks = Enumerable.Range(0, 600).Select(picker.Pick).ToArray();

        picks.Distinct().OrderBy(i => i).ShouldBe(new[] { 1, 2, 4 });
        for (int draw = 0; draw < 50; draw++)
        {
            picks[draw].ShouldBe(new[] { 1, 2, 4 }[Det.RollInt(_seed, Det.Domain.PatternPick, 3, k1: draw)]);
        }
    }

    [Fact]
    public void 망의_머리가_명부와_다르면_세울_때_거절한다()
    {
        // 머리의 순서가 곧 칸이다 — 학습 뒤에 명부를 바꾸면 망의 칸이 다른 패턴을 가리킨다. 세울 때 멈춰야 한다(Setup 이 [E] 로 바꾼다).
        string[] roster = ["3연격", "점프 3연속", "1타 돌진", "엇박 3연격", "1타 잡기"];
        var inputs = new PickerInputs(roster, TestNets.History(30), _seed, 2, Network: new NetworkContext(TestNets.Constant([0, 0, 0, 0, 0]), TestNets.Knobs()));

        Should.Throw<ArgumentException>(() => new NetworkPicker(inputs)).Message.ShouldContain("명부");
    }

    [Fact]
    public void 망이_없으면_세울_때_거절한다()
    {
        Should.Throw<ArgumentException>(() => new NetworkPicker(new PickerInputs(TestNets.Heads, TestNets.History(30), _seed, 2)));
    }

    [Fact]
    public void 결정을_한_줄로_남긴다()
    {
        using var log = new LogCapture();

        Picker([0.1, 0.9, -0.3, 0.5, 1.2]);
        Picker([0, 3, 0, 0, 0], events: 12);

        log.Lines.ShouldContain(l => l.StartsWith("[pick][D] mode=narrowed reason=habit samples=30 narrowed=점프 3연속,1타 돌진,엇박 3연격 breathing=1타 돌진 logits=0.100,0.900,-0.300,0.500,1.200 lifts=", StringComparison.Ordinal));
        log.Lines.ShouldContain("[pick][D] mode=full reason=thin samples=12");
    }

    [Fact]
    public void 등록표에_network_가_있다()
    {
        PatternPickers.Ids.ShouldContain(NetworkPicker.Id);
    }

    private static Dictionary<string, StageDef> NetworkStages() => new()
    {
        ["1"] = new() { Want = 2, Patterns = new[] { "3연격", "점프 공격" }, Picker = "uniform" },
        ["2"] = new() { Want = 5, Patterns = TestNets.Heads, Picker = NetworkPicker.Id },
    };

    private static StageSetup Setup(ulong seed, int share) =>
        StageRoster.Setup(NetworkStages(), 2, seed, TestNets.History(30), network: new NetworkContext(TestNets.Constant([0, 2, 0, 0, 0]), TestNets.Knobs(share: share)))
            .ShouldNotBeNull();

    [Fact]
    public void 동전은_몫이_0이면_늘_무작위_100이면_늘_망이다()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            Setup(seed, 0).Arm.ShouldBe("uniform");
            Setup(seed, 100).Arm.ShouldBe("network");
        }
    }

    [Fact]
    public void 동전은_반쯤_망이다()
    {
        int network = Enumerable.Range(0, 1000).Count(seed => Setup((ulong)seed, 50).Arm == "network");

        network.ShouldBeInRange(450, 550);
    }

    private static StageSetup? Forced(ulong seed, string arm) =>
        StageRoster.Setup(NetworkStages(), 2, seed, TestNets.History(30), network: new NetworkContext(TestNets.Constant([0, 2, 0, 0, 0]), TestNets.Knobs(share: 50)), arm: arm);

    [Fact]
    public void 갈래를_정해_넘기면_동전을_안_던진다()
    {
        // 평가(#114 · 설계 §7.1)가 같은 시도를 두 갈래로 한 번씩 돈다 — 동전이 무엇을 내든 넘긴 갈래로 선다. 몫 50 에 시드 마흔이면 동전은 양쪽을 다 낸다.
        for (ulong seed = 0; seed < 40; seed++)
        {
            StageSetup network = Forced(seed, NetworkPicker.Id).ShouldNotBeNull();
            network.Arm.ShouldBe(NetworkPicker.Id);
            network.Decision.ShouldNotBeNull().Mode.ShouldBe(PickDecision.ModeNarrowed);

            StageSetup uniform = Forced(seed, "uniform").ShouldNotBeNull();
            uniform.Arm.ShouldBe("uniform");
            uniform.Decision.ShouldBeNull();
            var reference = new UniformPicker(seed, 5);
            Enumerable.Range(0, 100).Select(uniform.Picker.Pick).ShouldBe(Enumerable.Range(0, 100).Select(reference.Pick));
        }
    }

    [Fact]
    public void 모르는_갈래는_E_를_남기고_판을_안_세운다()
    {
        using var log = new LogCapture();

        Forced(51, "script").ShouldBeNull();

        log.Lines.ShouldContain("[stage][E] arm_unknown arm=script stage=2");
    }

    [Fact]
    public void 고르기가_망이_아닌_단계는_갈래를_안_쓴다()
    {
        // 갈래는 망 단계의 동전 자리다 — 1단계(uniform)에 넘겨도 판은 그 단계의 고르기로 서고 갈래가 없다.
        StageRoster.Setup(NetworkStages(), 1, 51, TestNets.History(30), arm: NetworkPicker.Id).ShouldNotBeNull().Arm.ShouldBeNull();
    }

    [Fact]
    public void 무작위_갈래는_uniform_과_같은_판이고_망_갈래는_좁힌다()
    {
        // 동전은 PickerArm 스트림이라 뽑기(PatternPick)를 안 민다 — 무작위 갈래의 순서가 UniformPicker(시드) 그대로다.
        for (ulong seed = 0; seed < 40; seed++)
        {
            StageSetup setup = Setup(seed, 50);
            setup.PickerId.ShouldBe(NetworkPicker.Id);
            if (setup.Arm == "uniform")
            {
                setup.Decision.ShouldBeNull();
                var uniform = new UniformPicker(seed, 5);
                Enumerable.Range(0, 100).Select(setup.Picker.Pick).ShouldBe(Enumerable.Range(0, 100).Select(uniform.Pick));
            }
            else
            {
                setup.Decision.ShouldNotBeNull().Mode.ShouldBe(PickDecision.ModeNarrowed);
            }
        }
    }

    [Fact]
    public void 망이_없는데_network_를_부르면_E_를_남기고_판을_안_세운다()
    {
        using var log = new LogCapture();

        StageRoster.Setup(NetworkStages(), 2, 51, TestNets.History(30)).ShouldBeNull();

        log.Lines.ShouldContain("[stage][E] network_missing stage=2");
    }

    [Fact]
    public void 망의_머리가_명부와_다르면_E_를_남기고_판을_안_세운다()
    {
        using var log = new LogCapture();
        Dictionary<string, StageDef> stages = NetworkStages();
        stages["2"] = new StageDef { Want = 5, Patterns = new[] { "3연격", "점프 3연속", "1타 돌진", "엇박 3연격", "1타 잡기" }, Picker = NetworkPicker.Id };
        var network = new NetworkContext(TestNets.Constant([0, 0, 0, 0, 0]), TestNets.Knobs(share: 100));

        StageRoster.Setup(stages, 2, 51, TestNets.History(30), network: network).ShouldBeNull();

        log.Lines.ShouldContain(l => l.StartsWith("[stage][E] picker_rejected id=network stage=2 reason=", StringComparison.Ordinal));
    }

    [Fact]
    public void 잘라_쓴_단계를_싣는다()
    {
        // 기록의 단계는 실제로 싸운 단계다(설계 §6.5 · #59 3/6) — 없는 단계를 물으면 가장 가까운 단계로 잘라 쓰고, 그 값을 싣는다.
        StageSetup setup = StageRoster.Setup(NetworkStages(), 9, 51, TestNets.History(30), network: new NetworkContext(TestNets.Constant([0, 0, 0, 0, 0]), TestNets.Knobs()))
            .ShouldNotBeNull();

        setup.Stage.ShouldBe(2);
        StageRoster.Setup(NetworkStages(), 1, 51, TestNets.History(30)).ShouldNotBeNull().Stage.ShouldBe(1);
        StageRoster.Setup(NetworkStages(), 1, 51, TestNets.History(30)).ShouldNotBeNull().Arm.ShouldBeNull("1단계는 uniform 이라 갈래가 없다");
    }
}
