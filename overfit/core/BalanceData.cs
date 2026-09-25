using System.Text.Json.Serialization;

namespace Overfit.Core;

// data/balance.json 의 DTO. 키 이름은 snake_case 로 자동 변환된다 (BaseAttack → base_attack).
// `required` 가 붙은 키가 빠지면 부팅이 실패하고 빠진 키가 전부 나열된다 (RequiredKeys).
// Godot 을 모르는 순수 C# — 규칙 클래스가 그대로 쓴다.
//
// 콘텐츠 표(캐릭터 · 보스 · 패턴 · 단계)는 여기 없다. 그것들은 id 가 키인 자기 파일을 갖는다 —
// data/fighters.json · data/bosses.json · data/patterns.json · data/stages.json.
// 여기 있는 것은 **어느 콘텐츠의 것도 아닌** 수치다.

/// <summary>
/// 한 판을 <b>세우는</b> 수치. 보스의 것도 캐릭터의 것도 아니라 콘텐츠 표 어디에도 자리가 없다.
///
/// <para>
/// 전에는 <c>new Arena(1920)</c> 이 다섯 곳, <c>MaxTicks = 60 * 180</c> 이 네 곳에 리터럴로 있었다.
/// 흩어진 수치의 대가는 조용하다 — 한 곳만 고치면 게임과 데모와 골든이 서로 다른 전투를 말한다.
/// </para>
/// </summary>
public sealed class BattleBalance
{
    /// <summary>아레나 폭(px). 보스 몸 폭의 배수여야 대시로 빠질 곳이 남는다.</summary>
    public required double ArenaWidth { get; init; }

    /// <summary>한 판의 상한(틱). 고정 60틱이므로 10800 = 180초다. 한 판이 반드시 끝나게 하는 안전장치다.</summary>
    public required int MaxTicks { get; init; }

    /// <summary>
    /// 기본 보스의 id (<c>data/bosses.json</c> 의 키).
    /// 지금은 보스가 하나다 — 단계마다 달라지면 이 키를 <c>stages.json</c> 으로 옮긴다.
    /// </summary>
    public required string Boss { get; init; }

    /// <summary>
    /// 플레이어 캐릭터의 id (<c>data/fighters.json</c> 의 키).
    ///
    /// <para>
    /// 캐릭터 3택을 만들지 않는다 — 하나로 계속 간다. 그 "하나가 누구인가" 를 아는 곳이
    /// 여기 하나여야 게임과 데모가 다른 캐릭터로 돌지 않는다(보스가 그래서 갈렸던 적이 있다).
    /// 나머지 둘은 <c>fighters.json</c> 에 그대로 남는다.
    /// </para>
    /// </summary>
    public required string Fighter { get; init; }
}

/// <summary>
/// <b>뷰만 읽는</b> 연출 수치. 규칙 층은 이 블록을 안 본다 — 시뮬레이션이 여기에 반응하면
/// 잔상 개수를 바꾸는 것만으로 리플레이가 달라진다.
///
/// <para>
/// 그래도 데이터에 두는 이유는 이것들도 수치이기 때문이다. 잔상 간격 · 히트스톱 길이 ·
/// 흔들림 크기가 뷰 코드에 리터럴로 흩어지면 "전투가 어떤 느낌인가" 를 아는 곳이 없어진다.
/// </para>
///
/// <para>
/// 색은 여기 없다. 색은 "얼마나" 가 아니라 "무엇" 이라 뷰 쪽 이름 붙은 상수로 둔다 —
/// JSON 의 <c>[1.6, 1.6, 1.0, 1]</c> 은 사람이 읽고 고칠 수 있는 형태가 아니다.
/// </para>
/// </summary>
public sealed class FeelBalance
{
    /// <summary>
    /// 히트스톱의 길이(프레임). <b>틱을 건너뛴다</b> — <c>Battle._PhysicsProcess</c> 가 그 프레임에
    /// <c>BattleSim.Tick</c> 을 안 부른다. <c>BattleSim.Dt</c> 는 절대 안 건드린다: 한 틱의 길이가
    /// 달라지면 같은 입력이 다른 판을 내고 리플레이도 학습 데이터도 통째로 못 쓰게 된다.
    ///
    /// <para>
    /// <b>가드 불가를 받아친 3타에만 건다</b> (이슈 #53). 1·2타 패리에도 걸던 때는 3타까지의
    /// 간격이 패리 여부에 따라 0.90초와 1.52초를 오갔다 — 시간을 세우는 것은 "이건 특별하다" 는
    /// 말이라, 매번 일어나는 일에 걸면 그 말이 박자를 먹는다.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>이 값이 규칙 층이 아니라 뷰에 있는 것은 판단이다</b> (이슈 #53). 히트스톱은 틱의
    /// 길이를 늘이는 것이 아니라 <b>세우는</b> 것이라, 시뮬레이션이 지나가는 상태의 열이
    /// 걸든 안 걸든 한 칸도 안 다르다 — 사람과 헤드리스 봇이 <b>같은 판</b>을 산다는 뜻이고,
    /// 그래서 학습 데이터에 sim-to-real 간극이 안 생긴다. 규칙 층으로 옮기면 반대로 이 숫자가
    /// 리플레이의 일부가 되어, "얼마나 손맛 나는가" 를 눈으로 고칠 때마다 지금까지의 모든
    /// 리플레이가 못 쓰게 된다. 남는 차이는 사람이 벽시계로 0.117초를 더 쉰다는 것 하나인데,
    /// 그 0.117초는 2.3초짜리 경직 안에 떨어져 아무 판단도 밀지 않는다.
    /// </para>
    /// </summary>
    public required int HitstopFrames { get; init; }

    /// <summary>
    /// 보스가 굳어 있는 동안 <b>idle 을 얼마나 느리게</b> 돌릴까 (이슈 #53).
    ///
    /// <para>
    /// Medieval King Pack 2 에는 지친 모션이 <b>없다</b> — 시트가 열이고
    /// (idle · run · jump · fall · attack1~3 · take-hit · take-hit-white · death) 그중 어느 것도
    /// "숨이 차 서 있다" 가 아니다. 없는 이름으로 <c>Play</c> 하면 <c>PlaySafe</c> 가 조용히
    /// 삼키므로 지어내지 않고, 대신 <b>있는 것을 느리게</b> 돌린다: 0.35배 idle + 식은 몸 색이
    /// 그 자리에서 읽히는 유일한 "지쳤다" 다.
    /// </para>
    /// </summary>
    public required double StaggerAnimSpeed { get; init; }

    /// <summary>
    /// 플레이어 스프라이트의 배율. 팩의 그림은 픽셀아트라 원본 전신이 52px 뿐이라
    /// 그대로 두면 1920x1080 화면에서 안 읽힌다.
    ///
    /// <para>
    /// <b>히트박스와 짝이다.</b> 2.5배면 키 130px 로 <c>fighters.json</c> 의 <c>height</c> 120 과 맞는다 —
    /// 한쪽만 고치면 그림과 규칙이 다른 말을 한다.
    /// </para>
    /// </summary>
    public required double FighterSpriteScale { get; init; }

    /// <summary>
    /// 보스 스프라이트의 배율. 5.5배면 키 297px · 몸통 폭 171px 라
    /// <c>bosses.json</c> 의 <c>half_width</c> 85 와 맞고, 플레이어의 2.28배로 선다.
    ///
    /// <para>
    /// Duelyst 시절엔 4배가 <c>BossView</c> 의 <c>const</c> 였고 <c>half_width</c> 120 이 그 값을
    /// 전제했다. 팩을 갈면서 둘을 같이 옮기지 않으면 보스의 <b>사거리가 몸과 어긋난다</b> —
    /// 그래서 배율도 수치로 끌어내려 히트박스와 같은 층에 둔다.
    /// </para>
    /// </summary>
    public required double BossSpriteScale { get; init; }

    /// <summary>잔상을 남기는 간격(초). 무적 창(0.14초)을 이 값으로 나눈 만큼 잔상이 생긴다.</summary>
    public required double DashGhostInterval { get; init; }

    /// <summary>잔상 하나가 사라지는 데 걸리는 시간(초).</summary>
    public required double DashGhostFade { get; init; }

    /// <summary>공격 판정 · 패리 성공 섬광의 길이(초).</summary>
    public required double FlashSeconds { get; init; }

    /// <summary>피격 붉은 플래시의 길이(초).</summary>
    public required double HitFlashSeconds { get; init; }

    /// <summary>화면 흔들림의 길이(초).</summary>
    public required double ShakeSeconds { get; init; }

    /// <summary>화면 흔들림의 최대 진폭(px).</summary>
    public required double ShakePixels { get; init; }

    /// <summary>링을 그리는 높이(px, 발밑 기준). 몸 한가운데여야 캐릭터를 감싸 보인다.</summary>
    public required double RingOffsetY { get; init; }

    /// <summary>링 선의 두께(px). 이 크기에서 가늘면 아예 안 보인다.</summary>
    public required double RingWidth { get; init; }

    /// <summary>패리 창이 열릴 때 링의 시작 반지름(px).</summary>
    public required double ParryRingFrom { get; init; }

    /// <summary>패리 창이 닫힐 때 링의 끝 반지름(px).</summary>
    public required double ParryRingTo { get; init; }

    /// <summary>성공 섬광(링 + 스파크)이 퍼지는 시간(초).</summary>
    public required double BurstSeconds { get; init; }

    /// <summary>패리 성공 스파크 개수.</summary>
    public required int SparkCount { get; init; }

    /// <summary>스파크 한 가닥의 길이(px).</summary>
    public required double SparkLength { get; init; }

    /// <summary>
    /// 공격 섬광을 몸에서 앞으로 얼마나 밀어 낼지(px). <b>몸 위에 겹치면 안 된다</b> —
    /// 캐릭터가 130px 뿐이라 몸에 겹친 섬광은 "칼이 어디까지 닿나" 를 못 말한다.
    /// </summary>
    public required double AttackRingX { get; init; }

    /// <summary>공격 섬광의 시작 반지름(px).</summary>
    public required double AttackRingFrom { get; init; }

    /// <summary>
    /// 공격 섬광의 끝 반지름(px). ⚠ 칼이 그림의 모양이 되면서(이슈 #59) 사거리와 같은 눈금이라는
    /// 뜻은 없어졌다 — 링은 4번 PR(연출)이 걷는다.
    /// </summary>
    public required double AttackRingTo { get; init; }

    /// <summary>
    /// 차지 링의 <b>시작</b> 반지름(px). 패리 링과 반대로 여기서 <see cref="ChargeRingTo"/> 쪽으로
    /// <b>조여 든다</b> — 모이는 것은 퍼지는 것이 아니고, 보스 선딜 예고가 판정을 향해 줄어드는 것과
    /// 같은 문법이다 (이슈 #40).
    /// </summary>
    public required double ChargeRingFrom { get; init; }

    /// <summary>
    /// 차지 링이 <b>최대에 닿았을 때</b>의 반지름(px). 몸보다 작게 잡아 링이 몸에 붙어 멈춘다 —
    /// 멈춘 링이 "더 모을 것이 없다" 는 말이고, 그 말이 없으면 2초를 셀 방법이 없다.
    /// </summary>
    public required double ChargeRingTo { get; init; }

    /// <summary>
    /// 예고 링이 나타나는 시점 — 판정까지 <b>이만큼 남았을 때</b>부터 보인다(초).
    /// 선딜 길이는 패턴마다 다르므로(0.40~0.80) 뷰가 그 값을 알 필요가 없게 고정 리드로 잡는다.
    /// </summary>
    public required double TellLeadSeconds { get; init; }

    /// <summary>보스 선딜 예고 링의 시작 반지름(px). 판정 순간을 향해 <b>줄어든다</b>.</summary>
    public required double TellRingFrom { get; init; }

    /// <summary>보스 선딜 예고 링이 판정 순간에 닿는 반지름(px).</summary>
    public required double TellRingTo { get; init; }

    /// <summary>보스 링을 그리는 높이(px, 발밑 기준). 보스는 297px 라 파이터와 같은 높이면 발치에 깔린다.</summary>
    public required double BossRingOffsetY { get; init; }

    /// <summary>보스 판정이 서는 순간 퍼지는 충격파의 끝 반지름(px). 보스 몸(반폭 85)보다 훨씬 커야 "퍼진다" 로 읽힌다.</summary>
    public required double BossRingTo { get; init; }

    /// <summary>사망 애니메이션을 보여주고 결과 화면을 띄우기까지의 시간(초).</summary>
    public required double DeathHoldSeconds { get; init; }
}

/// <summary>모든 수치의 진실 원천. <c>data/balance.json</c> 하나가 이 모양이다.</summary>
public sealed class BalanceData
{
    [JsonPropertyName("_version")]
    public int Version { get; init; }

    public required BattleBalance Battle { get; init; }

    /// <summary>연출 수치. 규칙이 아니라 <b>뷰</b>가 읽는다.</summary>
    public required FeelBalance Feel { get; init; }
}
