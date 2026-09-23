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
    /// <b>정확</b> 패리의 창. 적중 이 시간 안에 눌렀으면 피해 0 · 보스 경직 · 기 +1 이다.
    /// <see cref="ParryDuration"/> 보다 짧다 — 실패가 비싸야 패리 의존도가 축이 된다.
    /// </summary>
    public required double ParryPreciseWindow { get; init; }

    /// <summary>
    /// <b>부정확</b> 패리의 창. 정확 창을 놓쳤어도 이 안이면 피해의 <see cref="ParryInternalRatio"/> 만
    /// 내상으로 받는다. 이 창이 있는 이유는 게임 감각만이 아니다 — "늦게 눌렀다" 와 "아무것도 안 했다" 가
    /// 지금까지 같은 점(<c>Verb=None · TimingError=0</c>)이었고, 이 중간 단계가 그 둘을 갈라 준다.
    /// <b>패리 행동(<see cref="ParryDuration"/>)보다 길다</b> — 그래서 창은 행동이 아니라 누름에 붙는다.
    /// </summary>
    public required double ParryImpreciseWindow { get; init; }

    /// <summary>
    /// 연타 징벌로 좁아진 정확 창. 앞 누름의 부정확 창 안에서 또 누르면 두 번째 누름이 이 창을 쓰고,
    /// 세 번째부터는 정확 창이 아예 없다(부정확만 가능).
    /// </summary>
    public required double ParrySpamWindow { get; init; }

    /// <summary>부정확 패리로 받아냈을 때 지상에서 굳는 시간(초). 공중에서는 안 굳는다.</summary>
    public required double ParryLock { get; init; }

    /// <summary>부정확 패리가 내상으로 받는 피해 비율.</summary>
    public required double ParryInternalRatio { get; init; }

    public required double ParryDuration { get; init; }

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

    // ── 가드 (이슈 #47) ─────────────────────────────────────────────────────────
    //
    // 가드는 **패리와 같은 키**다. 누름은 언제나 패리 시도이고, 패리 동작(parry_duration)이
    // 끝났는데 아직 누르고 있으면 가드가 선다. 가드의 값은 피해가 아니라 **스태미나**로 내고,
    // 그래서 세 수치가 전부 "얼마나 흘리나 · 얼마나 드나 · 깨지면 얼마나 아픈가" 다.

    /// <summary>
    /// 가드가 <b>흘려보내는</b> 피해의 비율. <see cref="ParryInternalRatio"/> 보다 작아야
    /// 가드를 고를 이유가 있다 — 그 관계를 <c>FighterDataTests</c> 가 지킨다.
    /// </summary>
    public required double GuardChipRatio { get; init; }

    /// <summary>
    /// 막아낸 피해 1 당 드는 스태미나. <b>정액이 아니라 비례인 것이 이 기술의 레버다</b> —
    /// 무거운 마무리 한 방이 가드를 깨고, 그 붕괴가 곧 가드 퍼니쉬다. 정액이면 연타든 마무리든
    /// 같은 값이라 "무엇을 가드할까" 라는 판단이 통째로 사라진다.
    /// </summary>
    public required double GuardStaminaPerDamage { get; init; }

    /// <summary>
    /// 가드가 깨졌을 때 굳는 시간(초). <see cref="ParryLock"/> 보다 길다 —
    /// 붕괴가 부정확 패리와 같은 값이면 버티는 것에 위험이 없다.
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
