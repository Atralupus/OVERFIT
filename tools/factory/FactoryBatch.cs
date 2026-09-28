using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Overfit.Factory;

/// <summary>
/// 봇 여럿을 묶음으로 나눠 병렬로 돌리고 <b>봇 번호 순서로</b> 낸다 (#108 · 설계 2026-09-28 §4.5). 봇은 서로 독립이라(제 판 · 제 봇 · 제 기록) 어느
/// 스레드가 먼저 끝나도 결과가 같다 — 순서만 번호로 되돌리면 파일이 스레드 수와 무관하게 바이트까지 같다.
///
/// <para>
/// 묶음(기본 1,000 대)마다 끝을 기다려 내는 것은 메모리 때문이다 — 봇 10만 대의 표본을 한꺼번에 들면 GB 단위다. 부르는 쪽이 받은 묶음을 파일에 쓰고
/// 놓으면 한 번에 드는 것은 묶음 하나다. 묶음의 끝에서 가장 느린 봇을 기다리는 동안 코어가 조금 논다 — 1,000 대면 그 꼬리가 작다.
/// </para>
///
/// <para>
/// 봇 한 대가 무엇을 하는지는 부르는 쪽이 <c>run</c> 으로 준다 — 학습 데이터(<see cref="BotRun"/>)와 평가(<see cref="EvalRun"/> · #114)가 같은
/// 묶음 · 같은 순서의 약속을 쓴다.
/// </para>
/// </summary>
public static class FactoryBatch
{
    /// <param name="from">첫 봇 번호.</param>
    /// <param name="to">끝 봇 번호 — <b>빼고</b> 센다(봇 <paramref name="from"/> ~ <paramref name="to"/> − 1).</param>
    /// <param name="threads">동시에 도는 봇 수의 상한.</param>
    /// <param name="run">봇 번호 → 그 봇의 결과. 봇끼리 아무것도 나누지 않아야 한다 — 표는 읽기만 한다.</param>
    /// <param name="emit">묶음 하나가 끝날 때마다 — 봇 번호 순서의 결과.</param>
    /// <param name="chunk">묶음의 봇 수.</param>
    public static void Run<T>(int from, int to, int threads, Func<int, T> run, Action<IReadOnlyList<T>> emit, int chunk = 1000)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(emit);
        ArgumentOutOfRangeException.ThrowIfLessThan(threads, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunk, 1);
        var options = new ParallelOptions { MaxDegreeOfParallelism = threads };
        for (int start = from; start < to; start += chunk)
        {
            int first = start;
            var results = new T[Math.Min(chunk, to - first)];
            Parallel.For(0, results.Length, options, i => results[i] = run(first + i));
            emit(results);
        }
    }
}
