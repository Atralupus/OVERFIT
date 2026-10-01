using System;
using System.Globalization;
using System.Numerics;

namespace Overfit.Core;

/// <summary>
/// 결정론 커널. <b>상태가 없는 것이 존재 이유다</b> — 같은 인자면 언제 어디서 몇 번을 불러도 같은 값이다.
///
/// <para>
/// 왜 <c>System.Random</c> 이 아닌가: MS 는 <c>Random</c> 의 시드 수열이 런타임 버전 간에 유지된다는 것을
/// <b>명시적으로 부인한다.</b> .NET 을 올리면 같은 시드가 다른 수열을 뱉고 리플레이가 전부 깨진다.
/// 그래서 명세를 우리가 소유한다 — 아래 상수와 연산 순서가 곧 계약이다.
/// </para>
///
/// <para>
/// 쓰는 법: 난수를 "뽑는" 대신 <b>좌표로 조회한다.</b> 시드 · 도메인 · 키(무엇의 몇 번째인지)를 주면 값이 나온다.
/// 호출 순서에 값이 끌려다니지 않으므로, 무엇을 언제 굴렸는지와 무관하게 같은 입력은 같은 결과다.
/// </para>
///
/// <para>
/// ⚠ <b>여기 있는 상수와 연산 순서는 바꾸지 않는다.</b> 하나만 건드려도 모든 리플레이가 달라진다.
/// 테스트 벡터(<c>tests/Overfit.Rules.Tests/Core/DetTests.cs</c>)가 값을 못 바꾸게 박아뒀다.
/// </para>
///
/// <para>
/// 로그는 여기서 찍지 않는다 — 틱마다 수천 번 불릴 자리다. 왜 이 값이 나왔는지는
/// <b>호출자가</b> 자기 태그로 남긴다.
/// </para>
/// </summary>
public static class Det
{
    /// <summary>황금비 상수 2^64/φ. 시드 0 이 죽은 상태가 되지 않게 섞는 자리에 쓴다.</summary>
    public const ulong Golden = 0x9E3779B97F4A7C15UL;

    /// <summary>splitmix64 파이널라이저의 곱 상수 ①.</summary>
    private const ulong _mul1 = 0xBF58476D1CE4E5B9UL;

    /// <summary>splitmix64 파이널라이저의 곱 상수 ②.</summary>
    private const ulong _mul2 = 0x94D049BB133111EBUL;

    /// <summary>도메인을 64비트로 펴는 홀수 상수. 도메인 번호가 1·2·3 처럼 작아도 스트림이 멀리 떨어진다.</summary>
    private const ulong _domainMul = 0xD1B54A32D192ED03UL;

    /// <summary>FNV-1a 64 오프셋 베이시스. <see cref="SeedFromString"/> 전용.</summary>
    private const ulong _fnvOffset = 14695981039346656037UL;

    /// <summary>FNV-1a 64 소수. <see cref="SeedFromString"/> 전용.</summary>
    private const ulong _fnvPrime = 1099511628211UL;

    /// <summary>2^53. <see cref="Unit"/> 의 나눗셈 분모 — double 이 정확히 세는 마지막 정수다.</summary>
    private const double _twoPow53 = 9007199254740992.0;

    /// <summary>
    /// 난수 스트림의 이름. <b>문자열 리터럴을 흩뿌리지 않는다</b> — 오타 하나가 조용히 다른 스트림을 만들고,
    /// 그건 "가끔 리플레이가 안 맞는다"로만 보인다.
    ///
    /// <para>
    /// ⚠ <b>값은 계약이다.</b> 이미 있는 번호를 바꾸거나 재사용하지 않는다 — 바꾸면 그 도메인의 과거 결과가 전부 달라진다.
    /// 새 도메인은 뒤에 <b>추가만</b> 한다. 0 은 "도메인 없음"으로 비워 둔다 (실수로 초기화된 uint 를 잡기 위해).
    /// 더할 때는 여기 한 줄과 <see cref="Name"/> 의 짝을 같이 넣는다.
    /// </para>
    /// </summary>
    public static class Domain
    {
        /// <summary>
        /// 보스가 다음에 어떤 패턴을 돌릴지 — <c>uniform</c> 고르기의 스트림이다(#72 · 설계 §4.4). 망은 이 스트림을 갈아끼우지 않고
        /// 고르기 등록표(<c>PatternPickers</c>)에 구현을 하나 더한다 — 그때도 uniform 은 대조군으로 이 스트림을 그대로 쓴다.
        /// </summary>
        public const uint PatternPick = 1;

        /// <summary>최소 봇이 회피 수단을 고를 때. 학습 데이터용 봇 함대는 이 스트림을 쓰지 않는다 — 함대는 <see cref="FleetAct"/> 다.</summary>
        public const uint BotChoice = 2;

        /// <summary>
        /// 최소 봇이 공격을 <b>얼마나 모을지</b> 고를 때 (이슈 #40).
        /// 회피 선택과 <b>같은 스트림을 쓰지 않는다</b> — 한 스트림을 나눠 쓰면 봇이 회피를 한 번 더
        /// 고른 것만으로 그 뒤의 모든 차지가 달라져, 두 판단이 서로를 흔든다.
        /// <para>⚠ <b>은퇴했다</b> (이슈 #59 — 차지가 없어졌다). 번호는 비워 둔다: 재사용하면 옛 리플레이의 스트림과 섞인다.</para>
        /// </summary>
        public const uint BotCharge = 3;

        /// <summary>
        /// 최소 봇이 이번 패턴을 <b>가드로 받을지</b> 고를 때 (이슈 #47).
        /// 회피 선택(<see cref="BotChoice"/>)과 <b>같은 스트림을 안 쓴다</b> — 나눠 쓰면 가드 주사위
        /// 하나가 그 뒤의 모든 회피 선택을 밀어, 이미 박아둔 골든과 데모가 가드와 무관하게 통째로
        /// 달라진다. 번호는 <b>뒤에 더할 뿐</b>이고 1~3 은 손대지 않는다.
        /// </summary>
        public const uint BotGuard = 4;

        /// <summary>
        /// 최소 봇이 1타를 누를 때 <b>2타를 이을지</b> 고를 때 (이슈 #59 · 설계 §5.1). 회피 선택 · 가드 선택과
        /// 같은 스트림을 안 쓴다 — 나눠 쓰면 칼질 하나가 그 뒤의 모든 회피 선택을 민다. 번호는 뒤에 더할 뿐이다.
        /// </summary>
        public const uint BotCombo = 5;

        /// <summary>
        /// 시도(전투 한 번)의 시드 (#72 · 설계 §4.4) — <c>Hash64(세션 시드, Attempt, k1: 시도 번호)</c>. 재시도마다 보스가
        /// 달라지게 하는 자리다(유저: "재시도 할때마다 달라지게"). 뽑기는 그 시드 위에서 지금처럼 <see cref="PatternPick"/> 를 쓴다.
        /// 번호는 뒤에 더할 뿐이다.
        /// </summary>
        public const uint Attempt = 6;

        /// <summary>
        /// 봇 함대의 <b>봇 한 대</b> (#104 · 설계 2026-09-28 §3.3) — <c>Hash64(함대 시드, FleetBot, k1: 봇 번호, k2: 0)</c> 이 그 봇의 세션 시드이고,
        /// <c>k2: 1 + j</c> 가 성향 j 의 좌표다. 봇 번호 하나로 그 봇이 되살아난다. 번호는 뒤에 더할 뿐이다.
        /// </summary>
        public const uint FleetBot = 7;

        /// <summary>
        /// 함대 봇의 <b>행동 선택</b> (#104) — 이번 패턴을 가드로 받나 · 욕심 · 수단 · 리듬 · 대시 방향 · 2연격. 시도 시드 위에서 굴린다.
        /// 타이밍 잡음(<see cref="FleetTiming"/>)과 같은 스트림을 안 쓴다 — 나눠 쓰면 잡음을 한 번 더 뽑은 것만으로 그 뒤의 모든 선택이 밀린다.
        /// </summary>
        public const uint FleetAct = 8;

        /// <summary>함대 봇의 <b>타이밍 잡음</b> (#104) — 판정마다 균등 난수 넷(합이 잡음이다 · 설계 2026-09-28 §3.1). 번호는 뒤에 더할 뿐이다.</summary>
        public const uint FleetTiming = 9;

        /// <summary>
        /// 2단계 시도의 <b>동전</b> — 망 · 무작위 갈래 (#112 · 설계 2026-09-28 §6.3). <c>RollInt(시도 시드, PickerArm, 100)</c> 을 정수 퍼센트와 견줬다.
        /// <para>⚠ <b>은퇴했다</b> (설계 2026-09-29 조각1 §6 — 옛 망과 동전을 걷었다). 번호는 비워 둔다: 재사용하면 옛 리플레이의 스트림과 섞인다.</para>
        /// </summary>
        public const uint PickerArm = 10;

        /// <summary>
        /// 계획의 <b>끊나</b> (설계 2026-09-29 조각1 §3.5) — 첫 동작에 캔슬 지점이 있으면 <c>RollInt(시도 시드, PlanCancel, 100, k1: 계획 번호)</c> 를
        /// <c>balance.json</c> 의 <c>picker.cancel_percent</c> 와 견준다. 계획의 결정마다 스트림을 따로 둔다 — 한 결정의 칸 수가 바뀌어도(명부에 동작을
        /// 더해도) 다른 결정의 좌표가 안 밀린다. 첫 동작은 옛 uniform 의 <see cref="PatternPick"/> 를 그대로 쓴다. 번호는 뒤에 더할 뿐이다.
        /// </summary>
        public const uint PlanCancel = 11;

        /// <summary>계획의 <b>어디서</b> 끊나 — 그 동작의 캔슬 지점에서 고르게(<c>RollInt(시도 시드, PlanPoint, 지점 수, k1: 계획 번호)</c>).</summary>
        public const uint PlanPoint = 12;

        /// <summary>계획의 <b>무엇으로</b> 잇나 — 첫 동작을 뺀 명부에서 고르게(<c>RollInt(시도 시드, PlanNext, 명부 수 − 1, k1: 계획 번호)</c>).</summary>
        public const uint PlanNext = 13;

        /// <summary>계획의 <b>쉬는 길이</b> — <c>bosses.json</c> 의 <c>rest_seconds</c> 에서 고르게(<c>RollInt(시도 시드, PlanRest, 쉬기 수, k1: 계획 번호)</c>).</summary>
        public const uint PlanRest = 14;

        /// <summary>
        /// 계획의 <b>달리나</b> (설계 2026-09-29 조각1 §5.2) — <c>RollInt(시도 시드, PlanRun, 100, k1: 계획 번호)</c> 를 <c>balance.json</c> 의
        /// <c>picker.run_percent</c> 와 견준다. 늘 굴린다 — 다른 결정과 스트림이 달라 무엇을 골랐든 좌표가 안 밀린다.
        /// </summary>
        public const uint PlanRun = 15;

        /// <summary>
        /// 데모 봇이 <b>폭탄을 던지나</b> (설계 2026-09-30 조각2 §6) — 동작이 설 때 <c>RollInt(시드, BotBomb, 3, k1: 본 동작 수)</c>, 보스가 무너질 때
        /// <c>RollInt(시드, BotBomb, 2, k1: 본 탈진 수, k2: 1)</c> 가 0 이면 던진다. 가드(4)와 같은 키를 쓰지만 도메인이 달라 따로 굴린다.
        /// </summary>
        public const uint BotBomb = 16;

        /// <summary>무작위 조종기 (설계 2026-10-01 조각2 §3.2) — 결정 번호가 k1 이다. 열린 칸 중 하나를 고른다.</summary>
        public const uint BossControl = 17;

        /// <summary>학습의 일꾼 (설계 2026-10-01 조각4 §6) — 판 번호가 k1. 판의 시드(k2 0)와 망 조종기의 뽑기 시드(k2 1)를 가른다.</summary>
        public const uint Rollout = 18;

        /// <summary>로그용 이름. 모르는 번호는 숫자 그대로 — 값을 감추는 것보다 낫다.</summary>
        public static string Name(uint domain) => domain switch
        {
            PatternPick => "pattern_pick",
            BotChoice => "bot_choice",
            BotCharge => "bot_charge",
            BotGuard => "bot_guard",
            BotCombo => "bot_combo",
            Attempt => "attempt",
            FleetBot => "fleet_bot",
            FleetAct => "fleet_act",
            FleetTiming => "fleet_timing",
            PickerArm => "picker_arm",
            PlanCancel => "plan_cancel",
            PlanPoint => "plan_point",
            PlanNext => "plan_next",
            PlanRest => "plan_rest",
            PlanRun => "plan_run",
            BotBomb => "bot_bomb",
            _ => domain.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// splitmix64 파이널라이저. 전단사(bijection)라 서로 다른 입력은 서로 다른 출력이고,
    /// 한 비트만 바뀌어도 출력의 절반쯤이 뒤집힌다(아발란치). <see cref="Hash64"/> 의 유일한 섞기 원소다.
    /// </summary>
    public static ulong Mix64(ulong z)
    {
        unchecked
        {
            z ^= z >> 30;
            z *= _mul1;
            z ^= z >> 27;
            z *= _mul2;
            z ^= z >> 31;
            return z;
        }
    }

    /// <summary>
    /// 좌표 하나의 해시. 시드 · 도메인 · 키 셋을 순서대로 접어 넣는다 — 매 단계가 전단사라
    /// <b>어느 인자든 한 비트만 달라지면 출력이 완전히 달라진다.</b>
    ///
    /// <para>
    /// 키는 <c>long</c> 이고 음수는 2의 보수 그대로 들어간다 — <b>절댓값으로 접지 않는다.</b>
    /// <c>k1=-1</c> 과 <c>k1=1</c> 은 다른 좌표다. 자리마다 회전량이 달라서 <c>(1,2,3)</c> 과 <c>(3,2,1)</c> 도 다르다.
    /// </para>
    /// </summary>
    /// <param name="seed">세션 · 판 시드. 정수는 <see cref="SeedFromLong"/>, 문자열은 <see cref="SeedFromString"/> 로 만든다.</param>
    /// <param name="domain"><see cref="Domain"/> 의 상수. 스트림을 가른다.</param>
    /// <param name="k1">첫째 키. 보통 "무엇"(개체 id · 슬롯 번호).</param>
    /// <param name="k2">둘째 키. 보통 "몇 번째"(횟수 · 세대).</param>
    /// <param name="k3">셋째 키. 남는 축.</param>
    public static ulong Hash64(ulong seed, uint domain, long k1 = 0, long k2 = 0, long k3 = 0)
    {
        unchecked
        {
            ulong h = Mix64(seed ^ Golden);
            h = Mix64(h ^ (domain * _domainMul));
            h = Mix64(BitOperations.RotateRight(h, 23) ^ (ulong)k1);
            h = Mix64(BitOperations.RotateRight(h, 31) ^ (ulong)k2);
            h = Mix64(BitOperations.RotateRight(h, 47) ^ (ulong)k3);
            return h;
        }
    }

    /// <summary>
    /// 해시를 <c>[0,1)</c> double 로. 상위 53비트만 쓴다 — double 이 정확히 세는 폭이고,
    /// 최댓값이 (2^53-1)/2^53 이라 <b>1.0 은 나올 수 없다.</b>
    /// </summary>
    public static double Unit(ulong hash) => (hash >> 11) * (1.0 / _twoPow53);

    /// <summary>
    /// 해시를 <c>[0,n)</c> 칸으로. Lemire 곱-시프트 — 나머지 연산의 낮은 비트 편향이 없고,
    /// 남는 편향은 n/2^64 라 100k 표본에서 보이지 않는다. <paramref name="n"/> 이 1 이면 항상 0.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">칸 수가 1 미만.</exception>
    public static int Index(ulong hash, int n)
    {
        if (n < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(n), n, "칸 수는 1 이상이어야 한다");
        }

        return (int)Math.BigMul(hash, (ulong)n, out _);
    }

    /// <summary>
    /// 좌표 하나의 <c>[0,1)</c> 값. <c>Unit(Hash64(...))</c> 와 같다.
    /// 확률 판정은 <c>Roll01(...) &lt; chance</c> 로 쓴다 — 1.0 이 안 나오므로 chance=1.0 은 항상 참이다.
    /// </summary>
    public static double Roll01(ulong seed, uint domain, long k1 = 0, long k2 = 0, long k3 = 0)
        => Unit(Hash64(seed, domain, k1, k2, k3));

    /// <summary>
    /// 좌표 하나의 <c>[0,n)</c> 칸. <c>Index(Hash64(...), n)</c> 와 같다.
    ///
    /// <para>
    /// ⚠ <paramref name="n"/> 이 키보다 <b>앞에</b> 있다 — 키는 선택 인자(기본 0)라 뒤에만 설 수 있기 때문이다.
    /// 헷갈리면 이름 있는 인자로 쓴다: <c>Det.RollInt(seed, domain, n: pool.Count, k1: draw)</c>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">칸 수가 1 미만.</exception>
    public static int RollInt(ulong seed, uint domain, int n, long k1 = 0, long k2 = 0, long k3 = 0)
        => Index(Hash64(seed, domain, k1, k2, k3), n);

    /// <summary>
    /// 정수를 시드로. 2의 보수 그대로 넓힌다 — <b>음수가 양수로 접히지 않는다.</b>
    /// <c>-1</c> 과 <c>1</c> 은 다른 시드다.
    /// </summary>
    public static ulong SeedFromLong(long value) => unchecked((ulong)value);

    /// <summary>
    /// 문자열을 시드로. FNV-1a 64 로 접고 <see cref="Mix64"/> 로 편다.
    /// UTF-16 코드 유닛을 하위 바이트 → 상위 바이트 순으로 먹는다 — 서수(ordinal) 비교라
    /// <b>문화권 · 플랫폼에 흔들리지 않는다.</b> 빈 문자열도 유효한 시드다.
    /// </summary>
    /// <exception cref="ArgumentNullException">null.</exception>
    public static ulong SeedFromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        unchecked
        {
            ulong h = _fnvOffset;
            foreach (char c in text)
            {
                h = (h ^ (byte)(c & 0xFF)) * _fnvPrime;
                h = (h ^ (byte)(c >> 8)) * _fnvPrime;
            }

            return Mix64(h);
        }
    }
}
