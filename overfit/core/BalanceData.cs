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
    /// <b>보스가 탈진에 드는 틱에 건다</b> (#72 · 설계 §4.3) — 뷰가 앞 틱의 탈진 여부와 견줘 잡는다. 받아친 관측에 걸지 않는
    /// 것은 경직 게이지로 무너진 탈진(#71)에 받아친 관측이 없어서다: 원인이 무엇이든 같은 탈진에 같이 걸린다. 그동안 누른 키는
    /// 버리지 않고 끝난 첫 틱에 넘긴다(<c>InputFrame.Carry</c> · #71).
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>이 값이 규칙 층이 아니라 뷰에 있는 것은 판단이다</b> (이슈 #53). 히트스톱은 틱의
    /// 길이를 늘이는 것이 아니라 <b>세우는</b> 것이라, 시뮬레이션이 지나가는 상태의 열이
    /// 걸든 안 걸든 한 칸도 안 다르다 — 사람과 헤드리스 봇이 <b>같은 판</b>을 산다는 뜻이고,
    /// 그래서 학습 데이터에 sim-to-real 간극이 안 생긴다. 규칙 층으로 옮기면 반대로 이 숫자가
    /// 리플레이의 일부가 되어, "얼마나 손맛 나는가" 를 눈으로 고칠 때마다 지금까지의 모든
    /// 리플레이가 못 쓰게 된다. 남는 차이는 사람이 벽시계로 0.117초를 더 쉰다는 것 하나인데,
    /// 그 0.117초는 1.5초짜리 탈진 안에 떨어져 아무 판단도 밀지 않는다.
    /// </para>
    /// </summary>
    public required int HitstopFrames { get; init; }

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

    /// <summary>파이터가 맞았을 때 붉은 플래시와 <c>hit</c> 자세의 길이(초).</summary>
    public required double HitFlashSeconds { get; init; }

    /// <summary>
    /// 보스가 맞았을 때 흰 플래시의 길이(초) (#71 · 설계 §6). 셰이더(<c>hit_flash.gdshader</c>)가 이 동안 1 에서 0 으로 희게 민다 —
    /// 애니메이션은 안 바꾼다. 0.12초는 히트스톱 7프레임(0.117초)과 거의 같아, 보스가 무너지는 틱의 한 대는 희게 멈춘 한 장면이 된다.
    /// 파이터의 붉은 플래시(<see cref="HitFlashSeconds"/>)와 따로 두는 이유: 그쪽은 <c>hit</c> 자세를 같이 세우는 길이라 짧게 못 줄인다.
    /// </summary>
    public required double BossHitFlashSeconds { get; init; }

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
    /// 뜻은 없어졌다 — 링은 6번 PR(연출)이 걷는다.
    /// </summary>
    public required double AttackRingTo { get; init; }

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
