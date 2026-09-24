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

    /// <summary>
    /// 모으고 있다 (이슈 #40). <c>attack</c> 시트의 <b>선딜 마지막 프레임</b>에 몸을 세운 자세다 —
    /// 칼을 끝까지 뒤로 뺀 그 그림이 곧 "모으는 중" 이라, 없는 애니메이션을 만들지 않는다.
    /// </summary>
    Charge,
    Dash,

    /// <summary>
    /// <b>방어 자세</b>다 (이슈 #47 · #53). 팩에 가드 그림이 <b>없어서</b> <c>idle</c> 을 빌려 쓰고,
    /// 갈라 보이게 하는 것은 <b>색과 멈춘 링</b>이다.
    ///
    /// <para>
    /// <b>패리 자세가 따로 없다</b> (이슈 #53). 전에는 <c>Parry</c> 가 있었고 몸 색도 링도 달랐는데,
    /// 패리와 가드가 한 행동이 된 이상 그림도 하나여야 한다 — 누른 사람은 자기가 창 안에 들었는지를
    /// 누르는 순간엔 알 수 없고(그건 판정이 서야 정해진다), 화면이 미리 갈라 말하면 거짓말이다.
    /// 받아쳤다는 것은 <b>그 뒤에</b> 한 번 터지는 작은 고리로만 말한다.
    /// </para>
    /// </summary>
    Guard,
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
/// <param name="Locked">가드가 깨져 굳었나. <c>guard_break_lock</c> 동안 아무것도 못 한다 —
/// <b>화면에 안 보이면 버그로 읽힌다</b>(키가 안 먹는 것처럼 보인다), 그래서 몸 색으로 말한다.
/// 부정확 패리의 고정이 없어져(이슈 #53) 이 색은 이제 한 가지 뜻뿐이다.</param>
/// <param name="ChargeProgress">차지를 얼마나 모았나(0~1). 링의 반지름이 이것이다 —
/// <c>ParryProgress</c> 와 같은 규약으로, 최대 시간(캐릭터마다 다르다)의 사본을 뷰에 두지 않는다.</param>
/// <param name="ChargeMaxed">차지가 <b>최대에 닿았나</b> (이슈 #40). 진행도만 넘기고 뷰가
/// <c>progress &gt;= 1</c> 로 판단하게 두지 않는다 — 단계는 구간이고 그 경계를 아는 곳은 규칙 하나다.
/// <b>최대인지 모르면 2초를 셀 수가 없다</b>, 그래서 이 한 칸이 색과 섬광을 가른다.</param>
/// <param name="GuardStamina">가드가 얼마나 버틸 수 있나 0~1 (이슈 #47) — 남은 스태미나를 최대로 나눈 값이다.
/// 가드 링의 굵기가 아니라 <b>밝기</b>가 이것이라, 바닥에 가까울수록 링이 꺼져 간다.
/// <b>뷰가 최대 스태미나를 따로 들지 않게</b> 비율로 넘긴다 — <c>ChargeProgress</c> 와 같은 규약이다.</param>
public readonly record struct FighterFrame(
    double X,
    double Y,
    int Facing,
    FighterPose Pose,
    bool Invulnerable,
    bool Locked,
    double ChargeProgress,
    bool ChargeMaxed,
    double GuardStamina);

/// <summary>
/// 한 프레임에 보스의 <b>예고 표지</b>를 그리는 데 필요한 전부.
///
/// <para>
/// <c>PatternTell</c>(규칙 층의 DTO)을 그대로 안 넘긴다. 뷰가 규칙 타입에 묶이면 그 타입을 쪼개는
/// 리팩터가 그림까지 끌고 다니고, 무엇보다 <c>X</c> 의 뜻이 여기서 <b>달라진다</b>:
/// 데이터의 <c>x</c> 는 "보스 앞/뒤" 이고 화면의 X 는 "왼/오른쪽" 이다. 그 뒤집기는 보스가
/// 어디를 보는지를 아는 <c>Battle</c> 만 할 수 있다 — 뷰에 맡기면 뷰가 다시 규칙을 읽게 된다.
/// </para>
/// </summary>
/// <param name="ShapeId">예고 모양 id. <see cref="BossTellShapes"/> 가 이것으로 구현을 찾는다.</param>
/// <param name="X">보스 발밑 기준 가로 오프셋(px). <b>이미 보스가 보는 쪽으로 뒤집혀 있다.</b></param>
/// <param name="Y">바닥에서의 높이(px, 위가 +). <b>이 숫자 하나가 "칼이 땅에 있나" 를 말한다.</b></param>
/// <param name="Length">모양의 주된 크기(px). 칼이면 날 길이, 고리면 반지름이다.</param>
/// <param name="Finisher">지금 오는 판정이 이 패턴의 <b>마무리</b>인가 (이슈 #53).
/// 참이면 예고가 <b>빨강</b>이다.
///
/// <para>
/// <b>빨강이 비어 있던 자리를 여기가 가져갔다.</b> 원래 뜻은 "패리 불가 · 대시해라" 였는데
/// 그 주인(점프 강타)이 이슈 #48 에서 사라졌다. 이제 빨강은 <b>"이 한 대가 받아칠 값이 있는 대"</b>
/// 다 — 받아치면 보스가 <c>finisher_parry_stagger</c> 만큼 굳고 거기 최대 차지가 들어간다.
/// </para>
///
/// <para>
/// ⚠ <b>패턴이 아니라 판정 단위다.</b> 패턴 단위로 칠하면 선딜 내내 참이라 막아도 되는
/// 앞의 연타까지 빨개진다 — 실제로 그렇게 떴고 스크린샷에서 보고 고쳤다.
/// </para></param>
/// <param name="GuardBreak">지금 오는 판정이 <b>가드 불가</b>인가 (이슈 #47 · #53).
/// 참이면 빨강 위에 <c>危</c> 가 같이 뜬다.
///
/// <para>
/// <b><see cref="Finisher"/> 와 둘로 두는 것이 이 이슈의 결정이다.</b> 색이 "받아칠 값이 있다"
/// 까지 말하고, 글자가 "게다가 막을 수조차 없다" 를 말한다. 마무리는 아홉 변종 전부에 있고
/// 가드 불가는 3단계 다섯에만 있으므로, 하나로 묶으면 둘 중 하나는 반드시 거짓말이 된다 —
/// 묶어서 빨강을 가드 불가에 주면 1단계에 빨강이 영영 안 뜨고, 반대로 하면 막을 수 있는 대에
/// 危 가 뜬다. 기호를 색과 같이 두는 이유이기도 하다: 소울라이크를 해 본 사람의 기본값은
/// 붉은 것을 <b>피하는</b> 것이라, 무엇을 하라는 말은 글자가 져야 한다.
/// </para></param>
public readonly record struct BossTell(
    string ShapeId,
    double X,
    double Y,
    double Length,
    bool Finisher,
    bool GuardBreak);

/// <summary>
/// 한 렌더 프레임에 보스를 그리는 데 필요한 전부. <see cref="FighterFrame"/> 과 같은 규약이다 —
/// <b>순간(판정이 섰다 · 맞았다 · 죽었다)은 여기 없다.</b> 그건 상태가 아니라 사건이라
/// <c>BossView</c> 의 메서드 호출로 들어온다.
/// </summary>
/// <param name="X">보스의 규칙 좌표 x.</param>
/// <param name="Facing">-1 왼쪽 · +1 오른쪽. <b>규칙이 정한 값을 그대로 싣는다</b> —
/// 뷰가 보스와 파이터의 x 를 보고 스스로 정하면 "같은 시드면 같은 결과" 가 그림까지 덮지 못하고,
/// 무엇보다 <b>패턴 중 잠금</b>(<c>Boss.Face</c>)이 뷰에서 풀려 예고가 스윙 도중에 뒤집힌다.</param>
/// <param name="Phase">패턴의 어디쯤인가 — 선딜 · 후딜 · 쉬는 중.</param>
/// <param name="NextActiveIn">다음 판정까지 남은 시간(초). 더 올 판정이 없으면 null.</param>
/// <param name="Staggered">가드 불가를 받아쳐 굳어 있나 (이슈 #53). 이 동안은 예고를 그리지 않고
/// idle 을 <c>feel.stagger_anim_speed</c> 로 느리게 돌린다 — 팩에 지친 모션이 없어서 고른 방법이다.</param>
/// <param name="Anim">선딜에 재생할 모션 이름. 패턴마다 다르다(<c>patterns.json</c> 의 <c>tell.anim</c>).
/// 패턴이 안 돌면 null.</param>
/// <param name="Tell">이 패턴의 예고 표지. 패턴이 안 돌거나 후딜이면 null.</param>
public readonly record struct BossFrame(
    double X,
    int Facing,
    BossPhase Phase,
    double? NextActiveIn,
    bool Staggered,
    string? Anim,
    BossTell? Tell);
