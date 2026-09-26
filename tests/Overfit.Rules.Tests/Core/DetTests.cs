using System;
using System.Collections.Generic;
using Overfit.Core;
using Shouldly;
using Xunit;

namespace Overfit.Rules.Tests.Core;

/// <summary>
/// 결정론 커널의 계약. <b>여기 박힌 값은 바꾸는 것이 아니라 지키는 것이다.</b>
///
/// <para>
/// 골든 벡터는 C# 구현을 찍어서 만든 것이 아니라 <b>파이썬으로 알고리즘을 따로 구현해</b> 얻었다 —
/// 같은 코드를 두 번 읽으면 오타도 같이 통과하기 때문이다. 두 구현이 같은 값을 내므로
/// 이 숫자들은 "지금 코드가 내는 값"이 아니라 "명세가 정한 값"이다.
/// </para>
///
/// 이 값이 달라지면 같은 시드의 과거 결과가 전부 달라진다. 빨개지면 <b>Det.cs 를 되돌린다.</b>
/// </summary>
public class DetTests
{
    [Fact]
    public void Mix64_는_0_을_0_으로_보낸다()
    {
        // splitmix64 파이널라이저의 성질. 그래서 시드 0 을 그냥 넣으면 죽은 상태가 되고,
        // Hash64 가 Golden 과 xor 한 뒤 섞는 이유가 이것이다.
        Det.Mix64(0).ShouldBe(0UL);
        Det.Mix64(1).ShouldBe(0x5692161D100B05E5UL);
    }

    [Fact]
    public void Hash64_는_명세가_정한_값을_낸다()
    {
        Det.Hash64(0, 0).ShouldBe(0x4671749834FBAC8EUL);
        Det.Hash64(1, 1, 2, 3).ShouldBe(0xBAC6970D416DBD8FUL);
        Det.Hash64(1, 1, 3, 2, 1).ShouldBe(0xE40224189B9142D7UL);
    }

    [Fact]
    public void 시드_0_도_죽은_상태가_아니다()
    {
        Det.Hash64(0, 0).ShouldNotBe(0UL);
    }

    [Fact]
    public void 같은_좌표는_몇_번을_불러도_같다()
    {
        ulong first = Det.Hash64(42, 1, 7, 8, 9);
        for (int i = 0; i < 100; i++)
        {
            Det.Hash64(42, 1, 7, 8, 9).ShouldBe(first);
        }
    }

    [Fact]
    public void 키의_순서가_값을_가른다()
    {
        // 자리마다 회전량이 달라서 (2,3,0) 과 (3,2,0) 은 다른 좌표다.
        Det.Hash64(1, 1, 2, 3).ShouldNotBe(Det.Hash64(1, 1, 3, 2));
    }

    [Fact]
    public void 음수_키는_절댓값으로_접히지_않는다()
    {
        Det.Hash64(1, 1, -1).ShouldBe(0x46A6149A047A87CBUL);
        Det.Hash64(1, 1, -1).ShouldNotBe(Det.Hash64(1, 1, 1));
    }

    [Fact]
    public void 도메인이_스트림을_가른다()
    {
        Det.Hash64(5, 1, 3).ShouldNotBe(Det.Hash64(5, 2, 3));
    }

    [Fact]
    public void Unit_은_1_0_을_내지_않는다()
    {
        // 상위 53비트만 쓰므로 최댓값이 (2^53-1)/2^53 이다. chance=1.0 을 항상 참으로 만드는 성질이다.
        Det.Unit(ulong.MaxValue).ShouldBeLessThan(1.0);
        Det.Unit(0).ShouldBe(0.0);
        Det.Roll01(1, 1, 2, 3).ShouldBe(0.7295927436220414, 1e-15);
    }

    [Fact]
    public void Roll01_은_0_과_1_사이에_고르게_퍼진다()
    {
        const int samples = 100_000;
        double sum = 0;
        for (int i = 0; i < samples; i++)
        {
            double v = Det.Roll01(7, 3, i);
            v.ShouldBeGreaterThanOrEqualTo(0.0);
            v.ShouldBeLessThan(1.0);
            sum += v;
        }

        (sum / samples).ShouldBe(0.5, 0.01);
    }

    [Fact]
    public void RollInt_은_칸에_고르게_떨어진다()
    {
        const int samples = 100_000;
        const int bins = 6;
        var counts = new int[bins];
        for (int i = 0; i < samples; i++)
        {
            counts[Det.RollInt(9, 4, bins, i)]++;
        }

        foreach (int count in counts)
        {
            // 균등이면 한 칸에 16,667. ±5% 안이면 편향이 없다고 본다.
            count.ShouldBeInRange((int)(samples / bins * 0.95), (int)(samples / bins * 1.05));
        }
    }

    [Fact]
    public void RollInt_은_칸이_하나면_항상_0()
    {
        for (int i = 0; i < 50; i++)
        {
            Det.RollInt(3, 1, 1, i).ShouldBe(0);
        }
    }

    [Fact]
    public void RollInt_은_칸이_0_이하면_거절한다()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Det.RollInt(1, 1, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => Det.Index(123, -1));
    }

    [Fact]
    public void SeedFromLong_은_음수를_양수로_접지_않는다()
    {
        Det.SeedFromLong(-1).ShouldBe(ulong.MaxValue);
        Det.SeedFromLong(-1).ShouldNotBe(Det.SeedFromLong(1));
    }

    [Fact]
    public void SeedFromString_은_명세가_정한_값을_낸다()
    {
        Det.SeedFromString(string.Empty).ShouldBe(0xF52A15E9A9B5E89BUL);
        Det.SeedFromString("overfit").ShouldBe(0xDD4CE0CD21878B05UL);
        // 한글도 같은 규칙(UTF-16 코드 유닛을 하위→상위 바이트 순)으로 먹는다. 문화권에 안 흔들린다.
        Det.SeedFromString("신경망").ShouldBe(0xAA623FA5AD56D938UL);
    }

    [Fact]
    public void SeedFromString_은_null_을_거절한다()
    {
        Should.Throw<ArgumentNullException>(() => Det.SeedFromString(null!));
    }

    [Fact]
    public void 서로_다른_좌표는_서로_다른_값을_낸다()
    {
        // 전단사의 성질. 작은 좌표들이 충돌하면 그건 섞기가 깨진 것이다.
        var seen = new HashSet<ulong>();
        for (long k1 = 0; k1 < 40; k1++)
        {
            for (long k2 = 0; k2 < 40; k2++)
            {
                seen.Add(Det.Hash64(1, 1, k1, k2)).ShouldBeTrue($"충돌: k1={k1} k2={k2}");
            }
        }
    }

    [Fact]
    public void 시도_도메인은_6번_attempt_다()
    {
        // 시도 시드 = Hash64(세션 시드, Attempt, k1: 시도 번호) (#72 · 설계 §4.4). 번호는 뒤에 더할 뿐이고(CLAUDE.md §4)
        // 로그의 이름도 계약이다 — 바꾸면 그 도메인의 과거 시드가 전부 달라진다.
        Det.Domain.Attempt.ShouldBe(6u);
        Det.Domain.Name(Det.Domain.Attempt).ShouldBe("attempt");
    }

    [Fact]
    public void 도메인_이름은_모르는_번호를_숫자_그대로_돌려준다()
    {
        Det.Domain.Name(0).ShouldBe("0");
        Det.Domain.Name(7).ShouldBe("7");
    }
}
