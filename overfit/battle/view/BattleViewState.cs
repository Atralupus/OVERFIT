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

    /// <summary>
    /// 패리 — <c>attack2</c> 의 f0~f3(칼을 사선으로 세운 자세)을 12fps 로 도는 0.333초 커밋 (설계 §5.3).
    /// 가드와 그림이 갈린다: 패리는 칼을 세우며 <b>움직이고</b> 가드는 서 있다.
    /// </summary>
    Parry,

    /// <summary>
    /// ↓ 를 누르고 있는 동안의 가드다 (설계 §5.2). 팩에 가드 그림이 없어 <c>idle</c> 을 빌리고, 갈라 보이게 하는 것은
    /// <b>색과 멈춘 링</b>이다 — <c>attack2</c> 의 f1 자세로 바꾸는 것은 6번 PR(연출)이다.
    /// </summary>
    Guard,
    Hit,

    /// <summary>
    /// 탈진 (#71 · 설계 §5.5 · §6) — 스태미나를 다 썼거나 가드가 깨졌다. <c>hit</c> 를 제 속도로 한 번 돌고 마지막 장에 선다
    /// (반복하지 않는 애니메이션이다). 보스의 탈진과 같은 말이다: take-hit 에 선 채 굳어 있다.
    /// </summary>
    Exhausted,
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
/// <param name="Exhausted">탈진했나 (#71 · 설계 §5.5) — 가드 붕괴든 스태미나 0 이든. <c>exhaust_seconds</c> 동안 아무것도 못 한다 —
/// <b>화면에 안 보이면 버그로 읽힌다</b>(키가 안 먹는 것처럼 보인다), 그래서 몸 색으로 말한다(자세는 <see cref="FighterPose.Exhausted"/>).</param>
/// <param name="GuardStamina">가드가 얼마나 버틸 수 있나 0~1 (이슈 #47) — 남은 스태미나를 최대로 나눈 값이다.
/// 가드 링의 굵기가 아니라 <b>밝기</b>가 이것이라, 바닥에 가까울수록 링이 꺼져 간다.
/// <b>뷰가 최대 스태미나를 따로 들지 않게</b> 비율로 넘긴다.</param>
public readonly record struct FighterFrame(
    double X,
    double Y,
    int Facing,
    FighterPose Pose,
    bool Invulnerable,
    bool Exhausted,
    double GuardStamina);

/// <summary>
/// 한 렌더 프레임에 HUD 를 그리는 데 필요한 전부 — <see cref="FighterFrame"/> 과 같은 규약이다. 비율과 몫은 <c>Battle</c> 이 규칙의
/// 값에서 내어 싣는다: 뷰가 최대값이나 탈진 길이의 사본을 들면 데이터를 고치는 날 바가 거짓말한다.
/// </summary>
/// <param name="Health">파이터 체력.</param>
/// <param name="MaxHealth">파이터 최대 체력.</param>
/// <param name="Stamina">파이터 스태미나.</param>
/// <param name="MaxStamina">파이터 최대 스태미나.</param>
/// <param name="FighterExhausted">파이터가 탈진했나 (#71 · 설계 §6) — 스태미나 바가 탈진 모양(보스 게이지의 탈진과 같은 파랑)이 된다.</param>
/// <param name="BossHealth">보스 체력.</param>
/// <param name="BossMaxHealth">보스 최대 체력.</param>
/// <param name="Poise">보스의 경직 게이지 0~1 (#71 · 설계 §4.5).</param>
/// <param name="BossExhaustLeft">보스의 남은 탈진 0~1 — 0 이면 탈진이 아니다. 탈진 동안 게이지 자리가 이것을 푸르게 그린다.</param>
public readonly record struct HudFrame(
    int Health,
    int MaxHealth,
    double Stamina,
    double MaxStamina,
    bool FighterExhausted,
    int BossHealth,
    int BossMaxHealth,
    double Poise,
    double BossExhaustLeft);

/// <summary>
/// 칼질 한 칸을 <b>그리는 데</b> 필요한 것 — 어느 시트를 몇 fps 로, 몇 번 장부터 돌리고 몇 번 장에서 칼이 나가나.
/// 규칙의 <c>ComboStepDef</c> 를 그대로 안 넘긴다: 뷰가 규칙 DTO 에 묶이면 그 타입을 고치는 날 그림까지 끌려온다
/// (<see cref="FighterFrame"/> 과 같은 이유다). <c>Battle</c> 이 데이터에서 옮겨 준다.
/// </summary>
/// <param name="Anim"><c>.tres</c> 의 애니메이션 이름.</param>
/// <param name="Fps">이 칼질의 재생 속도. 시트의 속도와 다르면 그 비율로 돌린다(2타는 반속).</param>
/// <param name="StartFrame">칼질이 시작하는 장.</param>
/// <param name="BladeFrame">칼이 지나가는 장 — 판정이 서는 틱에 여기로 맞춰 세운다.</param>
public readonly record struct SwingSheet(string Anim, double Fps, int StartFrame, int BladeFrame);

/// <summary>
/// 한 렌더 프레임에 보스를 그리는 데 필요한 전부. <see cref="FighterFrame"/> 과 같은 규약이다 —
/// <b>순간(판정이 섰다 · 맞았다 · 죽었다)은 여기 없다.</b> 그건 상태가 아니라 사건이라
/// <c>BossView</c> 의 메서드 호출로 들어온다.
/// </summary>
/// <param name="X">보스의 규칙 좌표 x.</param>
/// <param name="Y">보스의 발바닥 높이 (규칙 좌표 · 위가 +). 도약(설계 §4.2)이 움직인다 — 뷰가 y = 0 을 박지 않는다.</param>
/// <param name="Facing">-1 왼쪽 · +1 오른쪽. <b>규칙이 정한 값을 그대로 싣는다</b> —
/// 뷰가 보스와 파이터의 x 를 보고 스스로 정하면 "같은 시드면 같은 결과" 가 그림까지 덮지 못하고,
/// 무엇보다 <b>패턴 중 잠금</b>(<c>Boss.Face</c>)이 뷰에서 풀려 예고가 스윙 도중에 뒤집힌다.</param>
/// <param name="Phase">패턴의 어디쯤인가 — 선딜 · 후딜 · 쉬는 중.</param>
/// <param name="NextActiveIn">다음 판정까지 남은 시간(초). 더 올 판정이 없으면 null.</param>
/// <param name="Exhausted">탈진했나 (#72 · 설계 §4.3). take-hit(<c>hit</c>)를 한 번 돌고 마지막 장에 선 채 푸른 톤이다 —
/// 패리로든 경직 게이지로든(#71) 같은 그림이다. 맞으면 흰 플래시가 그 위에 얹힐 뿐 자세는 안 끊긴다.</param>
/// <param name="Anim">
/// 지금 든 타임라인 단계의 그림 — 보스 팩 <c>.tres</c> 의 애니메이션 이름(<c>patterns.json</c> 의 <c>anim</c> · 설계 §8.1).
/// 패턴이 안 돌면 null. <b>이것이 예고다</b> (#72 · 설계 §6): 옛 예고 표지(칼 · 끌기 · 危)를 걷었고, 3연격의 칼을 든 f0 ·
/// 점프 공격의 웅크린 <c>jump</c> f0 가 무엇이 오는지를 말한다.
/// </param>
/// <param name="Frame">
/// 그 단계가 붙드는 장(0부터). 뷰가 그 장에 세우고 멈춘다 — 장을 제 속도로 흘려 보내면 그림이 규칙의 창보다 먼저
/// 지나간다(파이터의 <c>HoldWindup</c> 과 같은 이유). null 이면 그 애니메이션을 제 속도로 돈다.
/// </param>
public readonly record struct BossFrame(
    double X,
    double Y,
    int Facing,
    BossPhase Phase,
    double? NextActiveIn,
    bool Exhausted,
    string? Anim,
    int? Frame);
