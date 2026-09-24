using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 차지 한 단계. <c>data/fighters.json</c> 의 <c>charge_tiers</c> 한 칸이고,
/// <b>목록의 순서가 곧 단계 번호</b>다 (0 = 안 모은 것).
///
/// <para>
/// 왜 구간인가 — 연속 배수가 더 단순한데도 구간을 고른 이유는 <b>최대에 닿았는지를 화면이
/// 말해야 하기 때문</b>이다 (이슈 #40). 2초를 세라고 하면서 "거의 최대" 와 "최대" 를 같은
/// 그림으로 두면 아무도 못 센다. 게다가 피해는 정수라 연속 배수는 반올림에서 이웃한 값들이
/// 같은 수로 뭉개진다 — 눈에도 안 보이고 숫자로도 안 갈리는 차이는 없는 차이다.
/// 구간이면 단계 번호 하나가 계측에 그대로 실려(<see cref="DodgeEvent.ChargeTier"/>) 학습
/// 데이터에도 남는다.
/// </para>
/// </summary>
public sealed class ChargeTierDef
{
    /// <summary>이 단계에 들어가는 <b>하한</b> 시간(초). 첫 칸은 0 이어야 한다 — 그냥 누른 것이 0단계다.</summary>
    public required double Seconds { get; init; }

    /// <summary><see cref="FighterConfig.AttackDamage"/> 에 곱할 배수.</summary>
    public required double DamageMultiplier { get; init; }
}

/// <summary>
/// 캐릭터 한 종의 수치. <c>data/fighters.json</c> 의 모양이고 키는 snake_case 로 변환된다.
/// <b>여기 없는 수치를 C# 에 상수로 두지 않는다.</b>
/// </summary>
public sealed class FighterConfig
{
    public required double MoveSpeed { get; init; }

    public required double JumpVelocity { get; init; }

    public required int MaxHealth { get; init; }

    public required double MaxStamina { get; init; }

    /// <summary>몸 절반 폭. 벽 제한이 이것을 쓴다.</summary>
    public required double HalfWidth { get; init; }

    /// <summary>키. 세로 판정(점프로 넘는가 · 대공에 걸리는가)이 이것을 쓴다.</summary>
    public required double Height { get; init; }

    public required double DashSpeed { get; init; }

    public required double DashDuration { get; init; }

    /// <summary>무적 창. <see cref="DashDuration"/> 보다 <b>짧아야</b> 대시 타이밍이 의미를 갖는다 — 끝자락엔 맞는다.</summary>
    public required double DashIFrames { get; init; }

    public required double DashCost { get; init; }

    /// <summary>
    /// <b>패리</b>의 창 (이슈 #53). 적중이 누름에서 이 시간 안에 서면 피해 0 이고,
    /// 그 밖이면 붙들고 있는 한 <b>가드</b>다 — 이 한 숫자가 방어 하나를 둘로 가른다.
    /// 그래서 이것은 캐릭터 성능이 아니라 <b>조작의 정의</b>다.
    /// </summary>
    public required double ParryPreciseWindow { get; init; }

    /// <summary>
    /// 한 번의 누름이 <b>아직 그 사람의 것</b>인 시간(초) — 이슈 #53. 두 곳이 쓴다:
    /// 연타 사슬(이 안에서 또 누르면 사슬이 자란다)과 계측의 공 돌리기(<c>BattleSim</c> 이
    /// 이 안의 누름까지만 그 판정의 시도로 센다).
    ///
    /// <para>
    /// 전에는 <c>parry_imprecise_window</c> 였다 — "늦게 눌렀지만 절반은 받아낸다" 는 중간 단계의 창.
    /// 그 단계를 가드가 대신하면서 창의 <b>뜻</b>만 남았다. 없는 기능을 가리키는 이름을 남겨 두면
    /// 다음 사람이 그 기능을 찾으러 간다.
    /// </para>
    /// </summary>
    public required double ParryMemoryWindow { get; init; }

    /// <summary>
    /// 연타 징벌로 좁아진 패리 창. 앞 누름의 기억 창 안에서 또 누르면 두 번째 누름이 이 창을 쓰고,
    /// 세 번째부터는 패리 창이 아예 없다 — 붙들고 있으면 가드로는 여전히 막는다.
    /// </summary>
    public required double ParrySpamWindow { get; init; }

    /// <summary>
    /// 방어 자세를 <b>누를 때</b> 한 번 드는 스태미나. 버티는 값은 시간이 아니라 막아낸 피해에
    /// 비례해 나간다(<see cref="GuardStaminaPerDamage"/>) — 그래서 이것은 "손을 댄 값" 이다.
    /// </summary>
    public required double ParryCost { get; init; }

    public required double AttackWindup { get; init; }

    public required double AttackActive { get; init; }

    public required double AttackRecover { get; init; }

    public required double AttackReach { get; init; }

    public required int AttackDamage { get; init; }

    public required double AttackCost { get; init; }

    /// <summary>
    /// 차지 단계표. <b>시간 오름차순</b>이고 첫 칸은 0초 · 배수 1 이다(그냥 누른 것).
    /// <b>마지막 칸의 <see cref="ChargeTierDef.Seconds"/> 가 곧 최대 차지 시간</b>이라
    /// 따로 키를 두지 않는다 — 두 곳에 적으면 갈린다.
    /// 순서와 첫 칸의 규약은 <c>FighterDataTests</c> 가 지킨다.
    /// </summary>
    public required List<ChargeTierDef> ChargeTiers { get; init; }

    // ── 공격 애니메이션의 사실 셋 ────────────────────────────────────────────────
    //
    // **규칙은 이 셋을 안 읽는다.** 여기 있는 이유는 위의 세 시간(선딜·판정·후딜)이
    // 이 셋에서 **거꾸로 정해지기 때문**이다 — 그리고 그 관계를 지키는 것이 테스트의 일이다.
    // 값을 뷰나 .tres 에만 두면 테스트가 못 읽어, 액션이 그림보다 짧아도 아무도 안 빨개진다.
    // 실제로 그랬다(이슈 #38): 0.30초짜리 공격이 0.50초짜리 6프레임을 돌려 칼이 나가기 전에
    // idle 로 돌아갔고, 그래서 **칼 휘두르는 그림이 한 번도 화면에 안 나왔다.**

    /// <summary>공격 애니메이션의 재생 속도(fps). <c>.tres</c> 의 <c>speed</c> 와 같은 값이다.</summary>
    public required double AttackAnimFps { get; init; }

    /// <summary>공격 애니메이션의 프레임 수. 재생 시간은 <c>frames / fps</c> 다.</summary>
    public required int AttackAnimFrames { get; init; }

    /// <summary>
    /// 칼이 실제로 지나가는 프레임의 번호(0부터). <b>시트를 열어서 정한다</b> —
    /// 번호로 짐작하지 않는다. 선딜은 여기까지고, 판정은 여기서부터 선다.
    /// </summary>
    public required int AttackAnimBladeFrame { get; init; }

    // ── 가드 (이슈 #47 · #53) ───────────────────────────────────────────────────
    //
    // 가드는 패리와 **같은 행동**이다 (이슈 #53). 누르면 그 틱부터 방어 자세이고, 판정이
    // parry_precise_window 안에 서면 패리 · 그 밖이면 가드다. 가드의 값은 피해가 아니라
    // **스태미나**로 내고, 그래서 세 수치가 "얼마나 흘리나 · 얼마나 드나 · 깨지면 얼마나 아픈가" 다.

    /// <summary>
    /// 가드가 <b>흘려보내는</b> 피해의 비율. 1 보다 작아야 막는 것에 뜻이 있고, 0 보다 커야
    /// 버티는 것이 공짜가 아니다 — 그 관계를 <c>FighterDataTests</c> 가 지킨다.
    /// </summary>
    public required double GuardChipRatio { get; init; }

    /// <summary>
    /// 막아낸 피해 1 당 드는 스태미나. <b>정액이 아니라 비례인 것이 이 기술의 레버다</b> —
    /// 무거운 마무리 한 방이 가드를 깨고, 그 붕괴가 곧 가드 퍼니쉬다. 정액이면 연타든 마무리든
    /// 같은 값이라 "무엇을 가드할까" 라는 판단이 통째로 사라진다.
    /// </summary>
    public required double GuardStaminaPerDamage { get; init; }

    /// <summary>
    /// 가드가 깨졌을 때 굳는 시간(초). <b>이 게임에 남은 유일한 고정</b>이다 (이슈 #53) —
    /// 부정확 패리가 사라지면서 "굳는다" 는 결과가 붕괴 하나에만 붙는다.
    /// 그 길이가 "남은 타격을 그대로 맞는" 값이고, 그게 버티기를 고른 값이다.
    /// </summary>
    public required double GuardBreakLock { get; init; }

    /// <summary>초당 회복량. 행동 중에는 회복하지 않는다 — <b>가드 중에도 안 찬다.</b></summary>
    public required double StaminaRegen { get; init; }

    /// <summary>
    /// <c>assets/spriteframes/&lt;id&gt;.tres</c> 의 id. <b>규칙은 이 값을 안 쓴다</b> — 뷰가 읽어 그릴 뿐이다.
    /// 여기 두는 이유는 "이 캐릭터가 무엇인가"의 진실이 data/fighters.json 한 곳이어야 하기 때문이다.
    /// </summary>
    public required string Sprite { get; init; }
}
