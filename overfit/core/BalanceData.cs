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
    /// 뜻은 없어졌다. 그래도 캐릭터의 링이라 남긴다 — 유저: "전체적으로 캐릭터는 남겨놔도 됩니다" (2026-09-26 · #81).
    /// </summary>
    public required double AttackRingTo { get; init; }

    /// <summary>
    /// 선딜 틴트가 무르익기 시작하는 시점 — 판정까지 <b>이만큼 남았을 때</b>부터 보스의 몸 색이 선딜 틴트 쪽으로 간다(초).
    /// 선딜 길이는 패턴마다 다르므로(0.40~0.80) 뷰가 그 값을 알 필요가 없게 고정 리드로 잡는다.
    ///
    /// <para>
    /// 같은 리드로 <b>조여 들던 예고 링</b>과 판정에 퍼지던 충격파는 걷었다 (#81 — 유저: "적 공격에 동그라미 연출은
    /// 제거해주세요"). 그 둘만 읽던 키 넷(<c>tell_ring_from</c> · <c>tell_ring_to</c> · <c>boss_ring_offset_y</c> ·
    /// <c>boss_ring_to</c>)도 같이 지웠다. 틴트의 무르익음은 동그라미가 아니라 남았고, 5번 PR 이 걷는다(설계 §6).
    /// </para>
    /// </summary>
    public required double TellLeadSeconds { get; init; }

    /// <summary>
    /// 착지의 흰 충격파가 발밑에서 판정의 양끝까지 퍼지는 시간(초) (#83). 유저: "점프공격때 하단영역에 데미지를 준다는 연출이
    /// 있어야겠네요 흰색영역이 퍼지면서 충격파를 주는듯한 연출을 추가해주세요." (2026-09-26)
    ///
    /// <para>
    /// <b>판정 창 안에 끝나야 한다</b> — 착지 띠의 <c>active_seconds</c>(0.125)와 같은 값이다. 러너는 그 창을 8틱(0.133초)으로 세므로
    /// 앞머리는 창이 닫히기 반 틱 전에 판정의 끝에 닿는다. 창보다 느리면 판정이 끝났는데 앞머리가 아직 가고 있어 "지금 퍼지는 것에
    /// 맞는다" 로 읽힌다. <c>FloorWaveTests</c> 가 바닥을 치는 띠의 창마다 잰다.
    /// </para>
    ///
    /// <para>
    /// <b>창 내내 고르게 퍼진다</b> (리뷰 m4) — 창보다 한참 짧으면 앞머리가 창의 앞 몇 틱에 끝까지 가 버려 퍼지는 것이 안 보인다. 앞머리는
    /// 발밑에서 아레나로 자른 판정의 끝까지 지난 몫만큼 간다(<c>FloorWave.Fronts</c>): 발 595 에서 착지하면 오른쪽은 한 프레임에 177px ·
    /// 왼쪽은 79px 남짓이다. 전에는 처음이 빠른 곡선으로 ±1920(화면 밖)을 향해 가서 두세 프레임 만에 화면을 벗어나 번쩍임으로 읽혔다.
    /// </para>
    ///
    /// <para>
    /// ⚠ 규칙의 띠는 창의 <b>첫 틱부터</b> 바닥 전체를 친다. 퍼지는 것은 그림이라 멀리 선 사람은 앞머리가 닿기 몇 틱 전에 맞는다 —
    /// 그것은 밑깔개(<see cref="LandingWaveUnderlayAlpha"/>)가 말한다: 첫 프레임부터 판정 전체가 옅게 깔린다.
    /// </para>
    /// </summary>
    public required double LandingWaveSpreadSeconds { get; init; }

    /// <summary>
    /// 다 퍼진 충격파가 옅어져 사라지는 시간(초) (#83). 퍼지는 동안은 옅어지지 않는다 — 판정이 사는 동안 띠가 흐려지면 "끝났다" 로
    /// 읽힌다. 0.3초는 화면 흔들림(0.2초)보다 조금 길다: 흔들림이 가라앉은 뒤에도 띠의 높이를 눈으로 잴 틈이 남는다.
    /// </summary>
    public required double LandingWaveFadeSeconds { get; init; }

    /// <summary>
    /// 충격파 띠의 불투명도 0~1 (#83). <b>반투명</b>이어야 한다 — 띠 안에 선 두 몸의 발이 보여야 "누가 그 높이에 있나" 가 읽힌다.
    /// 색(흰색)은 여기 없다 — 색은 "얼마나" 가 아니라 "무엇" 이라 뷰의 이름 붙은 상수다(이 클래스 머리).
    /// </summary>
    public required double LandingWaveAlpha { get; init; }

    /// <summary>충격파 앞머리의 불투명도 0~1 (#83). 띠(<see cref="LandingWaveAlpha"/>)보다 밝아야 퍼지는 쪽이 읽힌다.</summary>
    public required double LandingWaveEdgeAlpha { get; init; }

    /// <summary>
    /// 충격파 <b>밑깔개</b>의 불투명도 0~1 (#83 · 리뷰 m4) — 판정 전체(아레나로 자른 것)를 충격파가 선 첫 프레임부터 옅게 깐다. 규칙은
    /// 창의 첫 틱에 바닥 전체를 치므로, 앞머리가 아직 발밑에 있을 때도 "낮은 곳이 다 지금 맞는다" 가 화면에 있어야 한다. 앞머리는 퍼지는
    /// 것을, 밑깔개는 맞는 자리 전체를 말한다. 띠(<see cref="LandingWaveAlpha"/>)보다 옅어야 앞머리가 지나간 곳이 갈린다.
    /// </summary>
    public required double LandingWaveUnderlayAlpha { get; init; }

    /// <summary>
    /// 충격파 앞머리의 폭(px) (#83) — 앞끝에서 안쪽으로 이만큼이 띠의 밝기에서 앞머리의 밝기로 올라간다. 앞머리는 한 프레임에 백 px 안팎(80 ~ 250)을
    /// 가므로 얇은 선은 한 장면에서 선으로만 보이고 "달려간다" 가 안 읽힌다.
    /// </summary>
    public required double LandingWaveEdgeWidth { get; init; }

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
