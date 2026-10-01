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
    /// ↓ 를 누르고 있는 동안의 가드다 (설계 §5.2). 팩에 가드 그림이 없어 칼을 사선으로 세운 <c>attack2</c> f1 에 <b>멈춰 선다</b>
    /// (#96 · 설계 §6 · <c>fighters.json</c> 의 <c>guard_anim</c> · <c>guard_frame</c>) — 색과 멈춘 링이 그 위에 붙는다.
    /// </summary>
    Guard,

    /// <summary>
    /// 폭탄의 선딜 (설계 2026-09-30 조각2 §5) — 팩에 던지는 모션이 없어 칼질 시트의 한 장(<c>bomb.anim</c> · <c>bomb.windup_frame</c>)에 멈춰 서고,
    /// 손 위에 폭탄을 그린다(<c>BombView</c>). 이 동안 맞으면 끊기고 폭탄을 잃는다.
    /// </summary>
    ThrowWindup,

    /// <summary>폭탄을 놓은 뒤의 경직 — 같은 시트의 <c>bomb.release_frame</c> 에 멈춰 선다. 폭탄은 이미 날아갔다.</summary>
    ThrowRelease,
    Hit,

    /// <summary>
    /// 탈진 (#71 · 설계 §5.5 · §6) — 스태미나를 다 썼거나 가드가 깨졌다. <c>hit</c> 를 제 속도로 한 번 돌고 마지막 장에 선다
    /// (반복하지 않는 애니메이션이다). 보스의 탈진과 같은 말이다: take-hit 에 선 채 굳어 있다.
    /// </summary>
    Exhausted,

    /// <summary>
    /// 붙들림 (#78 · 설계 §4.7 · §6 「잡힌 파이터」) — 잡기에 잡혔다. 탈진과 같은 <c>hit</c> 의 마지막 장이고 흰 구가 감싼다. 탈진 색은 안 칠한다 —
    /// 붙들림과 탈진이 겹치면 붙들림이 먼저 그려지고, 흰 구가 흩어진 뒤 남은 탈진이 탈진 색으로 넘어간다(같은 <c>hit</c> 라 처음부터 다시 안 돈다).
    /// <c>Battle</c> 이 그 동안 <see cref="FighterFrame.Exhausted"/> 를 거짓으로 싣는다.
    /// </summary>
    Held,
    Death,
}

/// <summary>
/// 보스가 지금 패턴의 어디쯤인가. <b>선딜과 판정이 서는 순간이 달라 보여야 한다</b> —
/// 붉은 틴트 하나로는 "무엇이 오는지" 가 안 보인다는 것이 플레이 피드백이었다.
/// </summary>
public enum BossPhase
{
    /// <summary>패턴이 안 돈다 — 제자리에서 쉬거나 계획의 달리기로 달리는 중이다(설계 2026-09-29 조각1 §5).</summary>
    Idle,

    /// <summary>선딜. 아직 올 판정이 남았다 — 얼마나 남았는지는 안 그린다(#78 · 미끼가 그림으로 새지 않게).</summary>
    Windup,

    /// <summary>후딜. 이 패턴에 더 올 판정이 없다.</summary>
    Recover,
}

/// <summary>
/// 한 렌더 프레임에 파이터를 그리는 데 필요한 전부. <b>순간(피격·가드 성공)은 여기 없다</b> —
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
/// <b>화면에 안 보이면 버그로 읽힌다</b>(키가 안 먹는 것처럼 보인다), 그래서 몸 색으로 말한다(자세는 <see cref="FighterPose.Exhausted"/>).
/// 붙들린 동안(#78)은 탈진이 겹쳐도 거짓이다 — 흰 구가 감싸는 동안 탈진 색을 안 칠한다(설계 §4.7).</param>
/// <param name="GuardStamina">가드가 얼마나 버틸 수 있나 0~1 (이슈 #47) — 남은 스태미나를 최대로 나눈 값이다.
/// 가드 링의 굵기가 아니라 <b>밝기</b>가 이것이라, 바닥에 가까울수록 링이 꺼져 간다.
/// <b>뷰가 최대 스태미나를 따로 들지 않게</b> 비율로 넘긴다.</param>
/// <param name="Stiff">행동 뒤 경직 중인가 (#82) — 칼질(<see cref="FighterPose.Attack"/>)이면 시트를 끝까지 흘린 뒤 <c>idle</c> 의 첫
/// 장에 멈춰 서고, 대시(<see cref="FighterPose.Dash"/>)면 대시의 마지막 자세를 붙든다. 자세는 그대로라(경직도 그 행동이다) 이것이 따로 온다.</param>
public readonly record struct FighterFrame(
    double X,
    double Y,
    int Facing,
    FighterPose Pose,
    bool Invulnerable,
    bool Exhausted,
    double GuardStamina,
    bool Stiff);

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
/// <param name="Bombs">남은 폭탄 (설계 2026-09-30 조각2 §5).</param>
public readonly record struct HudFrame(
    int Health,
    int MaxHealth,
    double Stamina,
    double MaxStamina,
    bool FighterExhausted,
    int BossHealth,
    int BossMaxHealth,
    double Poise,
    double BossExhaustLeft,
    int Bombs);

/// <summary>나는 폭탄 하나를 그리는 데 필요한 것 — 규칙의 <c>BombFlight</c> 를 <c>Battle</c> 이 옮겨 준다(뷰가 규칙 타입에 안 묶이게 · <see cref="SwingSheet"/> 과 같은 이유).</summary>
/// <param name="FromX">놓은 자리의 x — 파이터의 발 중심.</param>
/// <param name="FromY">놓은 자리의 발바닥 높이.</param>
/// <param name="Progress">난 몫 0 → 1.</param>
public readonly record struct BombArc(double FromX, double FromY, double Progress);

/// <summary>
/// 한 렌더 프레임에 폭탄을 그리는 데 필요한 전부 (설계 2026-09-30 조각2 §5) — 손 위의 폭탄(선딜 동안)과 나는 폭탄들. 나는 폭탄은 놓은 자리에서
/// 보스의 <b>지금</b> 자리로 간다 — 규칙이 보스를 따라가 떨어뜨리므로(§1.3) 그림도 매 프레임 보스를 다시 잰다.
/// </summary>
/// <param name="InHand">선딜 중인가 — 손 위에 폭탄을 그린다.</param>
/// <param name="FighterX">파이터의 발 중심 x.</param>
/// <param name="FighterY">파이터의 발바닥 높이.</param>
/// <param name="Facing">파이터가 보는 쪽 — 손이 몸 앞에 있다.</param>
/// <param name="Flying">나는 폭탄들.</param>
/// <param name="BossX">보스의 발 중심 x.</param>
/// <param name="BossY">보스의 발바닥 높이.</param>
/// <param name="BossBodyHeight">보스의 키 — 폭탄은 몸 가운데에 떨어진다.</param>
public readonly record struct BombFrame(
    bool InHand,
    double FighterX,
    double FighterY,
    int Facing,
    System.Collections.Generic.IReadOnlyList<BombArc> Flying,
    double BossX,
    double BossY,
    double BossBodyHeight);

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
/// 시트의 <b>한 장에 멈춰 선</b> 그림 — 가드가 칼을 사선으로 세운 자세(<c>attack2</c> f1 · #96 · 설계 §6)다. <see cref="SwingSheet"/> 을
/// 빌려 쓰지 않는다: 멈춘 그림에는 속도도 칼이 나가는 장도 없어, 빈 칸을 둔 채 넘기면 읽는 쪽이 그 칸이 뜻이 있는지 모른다.
/// <c>Battle</c> 이 데이터(<c>fighters.json</c> 의 <c>guard_anim</c> · <c>guard_frame</c>)에서 옮겨 준다.
/// </summary>
/// <param name="Anim"><c>.tres</c> 의 애니메이션 이름.</param>
/// <param name="Frame">멈춰 서는 장(0부터).</param>
public readonly record struct StillFrame(string Anim, int Frame);

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
/// <param name="Phase">패턴의 어디쯤인가 — 선딜 · 후딜 · 쉬는 중. <b>다음 판정까지 남은 시간은 싣지 않는다</b> (#78 · 설계 §6) — 그 값으로
/// 선딜 틴트를 무르익히던 때 2단계의 미끼(같은 그림 · 엇박)가 그림으로 샜다. 싣지 않으면 뷰가 다시 쓸 길도 없다.</param>
/// <param name="Exhausted">탈진했나 (#72 · 설계 §4.3). take-hit(<c>hit</c>)를 한 번 돌고 마지막 장에 선 채 푸른 톤이다 —
/// 경직 게이지로 무너진 그림이다(#71 — 패리로 무너지던 길은 #168 에서 걷었다). 맞으면 흰 플래시가 그 위에 얹힐 뿐 자세는 안 끊긴다.</param>
/// <param name="Anim">
/// 지금 든 타임라인 단계의 그림 — 보스 팩 <c>.tres</c> 의 애니메이션 이름(<c>patterns.json</c> 의 <c>anim</c> · 설계 §8.1). 계획의 달리기
/// 동안은 <c>run</c> 이다(설계 2026-09-29 조각1 §5.4 — 동작 밖이라 단계가 없다). 패턴이 안 돌고 안 달리면 null. <b>이것이 예고다</b> (#72 · 설계 §6): 옛 예고 표지(칼 · 끌기 · 危)를 걷었고, 3연격의 칼을 든 f0 ·
/// 점프 공격의 웅크린 <c>jump</c> f0 가 무엇이 오는지를 말한다.
/// </param>
/// <param name="Frame">
/// 그 단계가 붙드는 장(0부터). 뷰가 그 장에 세우고 멈춘다 — 장을 제 속도로 흘려 보내면 그림이 규칙의 창보다 먼저
/// 지나간다(파이터의 <c>HoldWindup</c> 과 같은 이유). null 이면 그 애니메이션을 제 속도로 돈다.
/// </param>
/// <param name="AnimSpeed">
/// 그 애니메이션을 도는 배속 — 보통 1 이고, 돌진(#78) 동안 <c>feel.rush_anim_speed</c> · 계획의 달리기 동안 <c>feel.run_anim_speed</c> 다. <c>Battle</c> 이
/// 단계의 움직임과 달리기를 보고 싣는다.
/// </param>
/// <param name="Mirror">
/// 그 단계의 그림을 보는 쪽의 반대로 그리나 — 단계의 <c>mirror</c>(설계 2026-09-29 조각1 §2.1). 올려베기가 attack2 를 뒤집어 그린다: 그 궤적의 높은
/// 부분이 보스 등 뒤라 그대로면 앞의 공중을 치는 그림이 안 된다. 판정은 모양(<c>rects</c>)이 정하고 규칙은 이 값을 안 읽는다.
/// </param>
/// <param name="FormShiftLeft">
/// 페이즈 전환의 남은 몫 1 → 0 (설계 2026-10-01 조각1 §4) — 0 이면 전환이 아니다. 전환 동안 보스는 idle 을 돌고 흰 플래시가 몇 번 뛴다.
/// </param>
public readonly record struct BossFrame(
    double X,
    double Y,
    int Facing,
    BossPhase Phase,
    bool Exhausted,
    string? Anim,
    int? Frame,
    double AnimSpeed,
    bool Mirror = false,
    double FormShiftLeft = 0);

/// <summary>
/// 한 렌더 프레임에 잡기의 흰 구(<c>GrabOrb</c> · #78 · 설계 §4.7)를 그리는 데 필요한 전부. 규칙은 흰 구를 모른다 — 날고 · 붙들고 · 기다리는 것은
/// 규칙의 단계와 잡힘에서 <c>Battle</c> 이 옮겨 싣는다. 좌표는 규칙 좌표(위가 +)다.
/// </summary>
/// <param name="Flying">잡기 창 바로 앞 단계인가 — 보스에게서 파이터에게 난다.</param>
/// <param name="Progress">그 단계가 지난 몫 0 ~ 1 — 창이 열리는 틱에 파이터 발밑의 바닥(땅에 선 파이터의 몸)에 닿는다.</param>
/// <param name="Held">파이터가 붙들렸나 — 파이터를 감싼다.</param>
/// <param name="GrabLive">잡기 창이 살아 있나 — 못 잡았으면 창이 닫힐 때까지 바닥에서 기다린다.</param>
/// <param name="BossX">보스 발 중심 x.</param>
/// <param name="BossY">보스 발바닥 높이.</param>
/// <param name="BossBodyHeight">보스 몸 키 — 흰 구가 몸 가운데에서 떠난다.</param>
/// <param name="FighterX">파이터 발 중심 x.</param>
/// <param name="FighterY">파이터 발바닥 높이 — 붙든 흰 구만 따른다. 날고 기다리는 흰 구는 바닥에 있다(잡기가 치는 곳).</param>
public readonly record struct OrbFrame(
    bool Flying,
    double Progress,
    bool Held,
    bool GrabLive,
    double BossX,
    double BossY,
    double BossBodyHeight,
    double FighterX,
    double FighterY);
