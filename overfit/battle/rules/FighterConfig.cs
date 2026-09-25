using System.Collections.Generic;

namespace Overfit.Battle.Rules;

/// <summary>
/// 칼질 한 단계 (이슈 #59 · 설계 §5.1). <c>data/fighters.json</c> 의 <c>combo</c> 한 칸이다. 그림의 사실(어느 시트의
/// 몇 번 장을 몇 fps 로), 규칙의 시간(선딜 · 판정 · 후딜), 칼의 모양(<c>hitboxes.json</c> 의 id)을 <b>한 칸에</b> 둔다 —
/// 셋이 따로 적혀 있으면 한쪽만 고치는 날 칼과 그림이 갈린다.
///
/// <para>
/// 시간은 그림에서 <b>거꾸로</b> 정한다 (이슈 #38 · #54). 규칙은 시간과 피해와 모양만 읽고, 그림의 사실 넷
/// (<see cref="Fps"/> · <see cref="Frames"/> · <see cref="StartFrame"/> · <see cref="BladeFrame"/>)은 뷰가 그리고
/// <c>FighterDataTests</c> 가 시간과 맞대어 본다. 그 넷을 뷰나 .tres 에만 두면 테스트가 못 읽어, 액션이 그림보다
/// 짧아도 아무도 안 빨개진다 — 실제로 그랬다(이슈 #38): 칼 휘두르는 그림이 한 번도 화면에 안 나왔다.
/// </para>
/// </summary>
public sealed class ComboStepDef
{
    /// <summary><c>.tres</c> 의 애니메이션 이름 — 시트 파일 이름이 아니다 (1타는 <c>attack</c>, 팩의 attack1).</summary>
    public required string Anim { get; init; }

    /// <summary>이 칼질의 재생 속도(fps). 시트의 속도와 다를 수 있다 — 같은 시트를 반속으로 돌리는 칼질이 있다.</summary>
    public required double Fps { get; init; }

    /// <summary>시트의 장 수. 칼질은 <see cref="StartFrame"/> 부터 끝까지 돈다.</summary>
    public required int Frames { get; init; }

    /// <summary>
    /// 칼질이 시작하는 장(0부터). 선딜은 <b>손</b>이 정하고 시트는 <b>작가</b>가 정해서, 둘이 어긋나면 그림을
    /// 빨리 돌리지 않고 앞 장을 건너뛴다 — 빨리 돌리면 칼이 나가는 장까지 같이 빨라져 판정 위에 그림이 못 선다(이슈 #38).
    /// </summary>
    public required int StartFrame { get; init; }

    /// <summary>칼이 실제로 지나가는 장(0부터). <b>시트를 열어서 정한다</b> — 선딜은 여기까지, 판정은 여기서부터.</summary>
    public required int BladeFrame { get; init; }

    public required double Windup { get; init; }

    public required double Active { get; init; }

    public required double Recover { get; init; }

    public required int Damage { get; init; }

    /// <summary>
    /// 칼의 판정 모양 — <c>hitboxes.json</c> 의 id(<c>팩/애니메이션/장</c>). <see cref="BladeFrame"/> 의 흰 궤적에서
    /// 뽑은 것이다. <c>BattleSim</c> 이 판을 세울 때 모양을 찾고, 없는 id 면 그 자리에서 거절한다.
    /// </summary>
    public required string Hitbox { get; init; }
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

    /// <summary>패리의 창 (설계 §5.3) — 누른 순간부터 이 안에 선 판정을 받아친다. 그 밖이면 그냥 맞는다. 조작의 정의라 캐릭터 성능이 아니다.</summary>
    public required double ParryPreciseWindow { get; init; }

    /// <summary>패리를 누를 때 드는 스태미나 (설계 §5.3: 15). 가드를 드는 값은 없다 — _note_guard.</summary>
    public required double ParryCost { get; init; }

    /// <summary>
    /// 패리의 커밋(초) — 누르면 이 동안 아무것도 못 한다 (설계 §5.3). 앞쪽 <see cref="ParryPreciseWindow"/> 만
    /// 받아치므로 나머지는 무방비다: 그것이 난사의 벌이라 연타 징벌이 따로 없다.
    /// </summary>
    public required double ParryDuration { get; init; }

    /// <summary>패리가 도는 시트(<c>.tres</c> 의 이름). <b>규칙은 안 읽는다</b> — 뷰가 그리고 테스트가 커밋과 맞대어 본다.</summary>
    public required string ParryAnim { get; init; }

    /// <summary>패리 시트의 재생 속도(fps).</summary>
    public required double ParryAnimFps { get; init; }

    /// <summary>패리가 도는 장 수 — 0번부터(설계 §5.3: f0~f3 이면 4).</summary>
    public required int ParryAnimFrames { get; init; }

    /// <summary>
    /// 칼질 목록 (설계 §5.1). <b>목록의 순서가 곧 몇 번째 칼질인가</b>다 — 1타 · 2타. 첫 칸이 J 를 눌렀을 때 나가는
    /// 칼이고, 칼질 도중 J 를 또 누르면 그 칼질이 끝나는 틱에 다음 칸이 이어진다.
    /// </summary>
    public required List<ComboStepDef> Combo { get; init; }

    /// <summary>칼질마다 드는 스태미나 — 1타는 누를 때, 2타는 이을 때(설계 §5.1: 타마다 14).</summary>
    public required double AttackCost { get; init; }

    // ── 가드 (이슈 #47 · 설계 §5.2) ─────────────────────────────────────────────
    //
    // 가드는 ↓ (또는 S) 를 **누르고 있는 동안**이다. 드는 값은 없고, 값은 막아낸 피해에 비례하는
    // **스태미나**로 낸다 — 그래서 세 수치가 "얼마나 흘리나 · 얼마나 드나 · 깨지면 얼마나 아픈가" 다.

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
