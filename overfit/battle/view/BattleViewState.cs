namespace Overfit.Battle.View;

/// <summary>
/// 뷰가 그릴 자세. <b>규칙의 <c>FighterAction</c> 이 아니다</b> — 그걸 그대로 쓰면 뷰가
/// 규칙 열거형에 묶여, 행동 하나를 쪼개는 리팩터가 그림까지 끌고 다니게 된다.
/// 여기 있는 것은 "무슨 그림인가" 뿐이고, 규칙 → 자세 변환은 둘 다 아는 <c>Battle</c> 이 한다.
/// </summary>
public enum FighterPose
{
    Idle,
    Run,
    Attack,
    Dash,
    Parry,
    Hit,
    Death,
}

/// <summary>
/// 보스가 지금 패턴의 어디쯤인가. <b>선딜과 판정이 서는 순간이 달라 보여야 한다</b> —
/// 붉은 틴트 하나로는 "무엇이 오는지" 가 안 보인다는 것이 플레이 피드백이었다.
/// </summary>
public enum BossPhase
{
    /// <summary>패턴이 안 돈다. 다가오는 중이다.</summary>
    Idle,

    /// <summary>선딜. 아직 올 판정이 남았다 — 남은 시간이 예고 링의 반지름이 된다.</summary>
    Windup,

    /// <summary>후딜. 이 패턴에 더 올 판정이 없다.</summary>
    Recover,
}

/// <summary>
/// 한 렌더 프레임에 파이터를 그리는 데 필요한 전부. <b>순간(피격·패리 성공)은 여기 없다</b> —
/// 그건 상태가 아니라 사건이라 <c>FighterView</c> 의 메서드 호출로 들어온다.
/// 상태와 사건을 한 구조체에 섞으면 프레임이 두 번 그려질 때 사건이 두 번 난다.
/// </summary>
/// <param name="X">규칙 좌표 x.</param>
/// <param name="Y">규칙 좌표 y (위가 +, 바닥 0). 화면에서는 뒤집는다.</param>
/// <param name="Facing">-1 왼쪽 · +1 오른쪽.</param>
/// <param name="Pose">그릴 자세.</param>
/// <param name="Invulnerable">대시 <b>무적 창</b> 안인가. 잔상이 이것에 묶인다 —
/// 대시(0.18초)보다 무적(0.14초)이 짧은 것은 일부러고, 그 차이가 보여야 대시 타이밍이 의미를 갖는다.</param>
/// <param name="Parrying">패리 창 안인가. 링이 이것에 묶인다.</param>
/// <param name="ParryProgress">패리 창을 얼마나 지났나(0~1). 링의 반지름이 이것이다 —
/// 뷰가 자기 시계로 재게 하면 창 길이(캐릭터마다 다르다)를 뷰가 알아야 하고,
/// 그 사본은 fighters.json 이 움직이는 순간 조용히 어긋난다.</param>
/// <param name="Locked">부정확 패리에 굳었나(이슈 #27). 0.6초 동안 아무것도 못 한다 —
/// <b>화면에 안 보이면 버그로 읽힌다</b>(키가 안 먹는 것처럼 보인다), 그래서 몸 색으로 말한다.</param>
public readonly record struct FighterFrame(
    double X,
    double Y,
    int Facing,
    FighterPose Pose,
    bool Invulnerable,
    bool Parrying,
    double ParryProgress,
    bool Locked);
