namespace Overfit.Battle.Rules;

/// <summary>
/// 보스방. <b>평평한 바닥 하나뿐이고 발판이 없다.</b>
///
/// <para>
/// 발판을 안 두는 것은 게임성이 아니라 <b>시뮬레이션 봇</b> 때문이다 — 발판이 생기면 봇이
/// "지금 어디 설까"를 풀어야 하고, 조금만 복잡해지면 경로 탐색이 된다. 학습 데이터로 수백만 판을
/// 돌려야 하는데 그 탐색이 매 틱 돌면 데이터 공장이 막힌다.
/// </para>
/// </summary>
public sealed class Arena
{
    /// <summary>중력. px/s². 데이터가 아니라 상수인 이유는 밸런스 노브가 아니라 좌표계의 일부이기 때문이다.</summary>
    public const double Gravity = 2400.0;

    public Arena(double width) => Width = width;

    /// <summary>아레나 폭. 보스 폭의 배수여야 대시로 빠질 곳이 남는다.</summary>
    public double Width { get; }
}
