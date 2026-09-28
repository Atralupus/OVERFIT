namespace Overfit.Rules.Tests.Factory;

/// <summary>
/// 공장 · 평가 테스트가 쓰는 함대 봇 (#126). 함대 시드 51 에서 1단계를 넘는 봇들이다 — 2단계의 사례 · 결정을 보는 테스트는 이 봇들로 돈다.
///
/// <para>
/// 보스 체력이 200 → 400 이 되고(#126) 1단계를 넘는 봇이 62% 에서 2% 로 줄었다. 전에는 봇 0 ~ 7 만 돌려도 2단계 사례가 명부를 다 덮었는데,
/// 이제 앞 64 대 안에 넘는 봇이 하나(26)뿐이다. 넓게 훑으면 테스트가 초 단위를 넘으므로 넘는 봇을 골라 둔다. 수치(체력 · 함대 설정)가 바뀌면
/// 다시 고른다: <c>tools/build.sh factory --fleet-seed=51 --from=0 --to=3000</c> 의 <c>bots.csv</c> 에서 <c>reached_stage2</c> 가 1 인 봇을 앞에서부터.
/// </para>
/// </summary>
internal static class FleetBots
{
    /// <summary>함대 시드 51 · 1단계 시도 상한 5 에서 2단계에 가는 봇, 앞에서부터 열여섯.</summary>
    public static readonly int[] ReachStage2 = [26, 99, 193, 196, 334, 336, 528, 534, 556, 558, 595, 644, 669, 687, 755, 775];
}
