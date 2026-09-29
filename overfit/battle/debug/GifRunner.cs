using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Overfit.Core;

namespace Overfit.Battle.Debug;

/// <summary>
/// README 의 패턴별 GIF 한 장을 찍는 대본을 돈다 (#78 · 설계 §6.2). <c>tools/build.sh gifs</c> 가 창을 띄우고 Movie Maker(<c>--write-movie</c>)로
/// 모든 프레임을 PNG 로 받게 한 뒤 <c>--gif=&lt;대본 id&gt;</c> 로 이것을 세운다. 여기는 <b>패턴을 고정하고</b>(대본 고르기 · 설계 §4.4) <b>그 사람처럼
/// 움직이는 파이터</b>를 둔 뒤, <b>잡을 구간</b>의 첫 장과 끝 장 번호를 로그로 남기고 끝낸다 — 장을 골라 GIF 로 엮는 것은 도구(ffmpeg)다.
///
/// <para>
/// <b>규칙의 틱을 보고 누른다 — 벽시계로 기다리지 않는다.</b> 틱은 대본이 겨냥한 패턴의 틱이다(설계 §4 의 표와 같은 자 · 첫 틱 1). 패턴이 선
/// 판의 틱이 B 면 판의 B + p 틱이 패턴의 p 틱이다. 패턴 앞의 입력(사거리 안으로 걸어 들어가기 · ↓ 붙들기)은 판의 틱으로 적는다 — "판이 설 때부터".
/// 물리 틱 신호는 그 틱의 <c>Battle._PhysicsProcess</c> <b>앞에</b> 오므로(<see cref="ShotRunner"/> 의 2연격 주석), 신호를 받은 자리에서 누른 것은 곧
/// 이어지는 틱이 읽는다 — 그래서 여기서는 "다음 틱"(지금까지 민 틱 + 1)의 입력을 넣는다. 히트스톱 동안 판은 틱을 안 밀고 누른 엣지는 끝난 첫 틱에
/// 넘어간다(<c>InputFrame.Carry</c>) — 대본은 그 동안 누르지 않는다.
/// </para>
///
/// <para>
/// 대본 id 는 ASCII 소문자다(<c>rush</c> · <c>grab</c> · <c>offbeat</c> · <c>jump3</c>) — 인자와 파일 이름(<c>docs/gifs/&lt;id&gt;.gif</c>)으로 쓰인다.
/// 대본은 이 안의 C# 표다 — 스크린샷 대본이 코드인 것과 같다.
/// </para>
/// </summary>
public partial class GifRunner : Node
{
    /// <summary>
    /// 한 대본의 상한(초) — 판이 서고 잡을 구간이 끝나기까지. 넘으면 <c>[E]</c> 로 멈춘다(GIF 가 안 찍힌 것은 도구의 실패다). 대본 넷은 길어야
    /// 10초 남짓이다(점프 ×3 칸 — 3연격을 돌고 점프 ×3 의 셋째 착지 + 30틱까지).
    /// </summary>
    private const double _timeout = 30.0;

    /// <summary>판의 틱으로 적는 입력의 시작 — "판이 설 때부터".</summary>
    private const int _battleStart = 1;

    /// <summary>끝이 없는 입력 — 대본이 끝날 때까지 붙든다.</summary>
    private const int _forever = int.MaxValue - 1;

    /// <summary>엣지인 액션 (설계 §5.4 · <c>InputFrame</c>) — 한 틱 앞에 누른다(<see cref="Drive"/>). 나머지(이동 · 가드)는 레벨이다.</summary>
    private static readonly HashSet<string> _edges = new(StringComparer.Ordinal) { "jump", "dash", "parry", "attack" };

    /// <summary>
    /// 대본 넷 (설계 §6.2 의 표). 틱은 패턴의 틱이고, 판의 틱으로 적은 것은 <see cref="GifInput.OnBattleClock"/> 이 참이다.
    /// 잡을 구간은 [<see cref="GifScript.From"/>, <see cref="GifScript.To"/>) 패턴 틱 — 60fps 로 240장(4초)을 넘지 않는다(도구가 막는다).
    /// 대본마다 같은 입력을 규칙 위에서 틱까지 못박은 테스트가 있다(<c>Stage2BattleTests</c>) — 숫자를 바꾸면 거기서 먼저 잰다.
    /// </summary>
    private static readonly GifScript[] _scripts =
    {
        // 멀리 서서 지켜보다 후딜에만 찔끔 친다 → 1타 → 돌진 → 3타 (설계 §4.6). 1타(51틱)가 멀리서 헛치자 후딜을 노려 걸어 들어가고(60 ~ 77틱),
        // 보스가 달리기 시작하는 78틱에 J 를 누른다 — 칼은 달려오는 보스에 닿지만(84틱) 1타 뒤 경직(0.40초 · #82)이 118틱까지 묶어, 1타가 끝난
        // 95틱에 대시로 빠지려 한 누름이 버려지고 도착(85틱) 15틱 뒤의 3타(100틱)를 맞는다. 3타 + 30 까지 잡는다. 입력은 짝 테스트
        // (Stage2BattleTests.일타_돌진은_달려오는_보스를_찌른_사람을_1타_뒤_경직에서_3타로_맞힌다)와 같다 — 그 테스트의 대조군(경직 0)은 같은
        // 95틱의 대시로 3타를 흘린다. 전에는 대본이 그 대시를 빼 두어 GIF 의 사람이 테스트의 사람과 달랐다(#96).
        new("rush", Patterns: new[] { "1타 돌진" }, Target: "1타 돌진",
            Inputs: new[] { new GifInput(60, 77, "move_right"), new GifInput(78, 78, "attack"), new GifInput(95, 95, "dash") },
            From: 1, To: 130),

        // 대시로만 피한다 → 1타 → 잡기 (설계 §4.7). 판이 서면 1타 사거리 안(보스와 356 떨어진 956)으로 걸어 들어가, 1타 창이 열리는 틱(51)에
        // 대시로 흘리고(보스를 뚫고 등 뒤로 빠진다 · 대시와 그 뒤 경직 0.10초가 68틱에 풀린다), 잡기 창(102)이 열리기 4틱 앞(98)에 또 대시해 **무적인
        // 채로** 흰 구에 붙들린다. 풀리는 틱(162) 뒤 흰 구가 흩어지는 0.3초(18틱 · feel.grab_orb_fade_seconds)까지 잡는다 — 그 값을 고치면 To 도 같이 고친다.
        new("grab", Patterns: new[] { "1타 잡기" }, Target: "1타 잡기",
            Inputs: new[]
            {
                new GifInput(_battleStart, 68, "move_right", OnBattleClock: true),
                new GifInput(51, 51, "dash"),
                new GifInput(98, 98, "dash"),
            },
            From: 1, To: 180),

        // 패리를 많이 한다 → 엇박 3연격 (설계 §4.9). 1타 사거리 안으로 걸어 들어가 3연격의 박자(1타 51틱의 2틱 앞 · 49틱)에 K 를 누른다 — 엇박의
        // 1타는 60틱이라 패리의 창(+6)을 지나 커밋(+18) 안에 떨어져 맨몸으로 맞는다. 헛친 한 번(커밋과 패리 뒤 경직 · 0.583초 · #82)이 84틱에 풀리면
        // 맞은 틱(60)에서 3연격의 간격(42틱)을 재어 2틱 앞(100)에 또 누른다 — 늦은 2타(111)에 또 맞는다. 둘째 + 30 까지 잡는다.
        new("offbeat", Patterns: new[] { "엇박 3연격" }, Target: "엇박 3연격",
            Inputs: new[]
            {
                new GifInput(_battleStart, 68, "move_right", OnBattleClock: true),
                new GifInput(49, 49, "parry"),
                new GifInput(100, 100, "parry"),
            },
            From: 1, To: 141),

        // 가드로 버틴다 → 점프 ×3 (설계 §4.8). 사거리 안으로 걸어 들어가 ↓ 를 놓지 않는다 — 3연격을 막고(54) 곧장 온 점프 ×3 의 셋째 착지에서
        // 붕괴한다(54 + 21.6 × 2 = 97.2 라 셋째 21.6 을 못 낸다). 3연격과 첫 도약은 잡지 않고 돌고(스태미나 바가 이미 줄어 있다) 둘째 도약의
        // 선딜(90)부터 셋째 착지(240) + 30 까지 잡는다 — 착지마다 흰 충격파가 선다(#83).
        new("jump3", Patterns: new[] { "3연격", "점프 3연속" }, Target: "점프 3연속",
            Inputs: new[]
            {
                new GifInput(_battleStart, 68, "move_right", OnBattleClock: true),
                new GifInput(69, _forever, "guard", OnBattleClock: true),
            },
            From: 90, To: 270),
    };

    private readonly SceneDriver _drive;

    public GifRunner() => _drive = new SceneDriver(this, "gif");

    /// <summary>돌릴 대본의 id — <c>Game</c> 이 <c>--gif=</c> 에서 읽어 세운다.</summary>
    public string ScriptId { get; set; } = "";

    /// <summary>
    /// 등록된 대본 id 들 — 모르는 id 를 받았을 때 로그에 싣는다. 대본 목록은 위의 표(<see cref="_scripts"/>) 하나다: <c>tools/build.sh gifs</c> 는
    /// 인자 없이 돌면 이 파일에서 표의 줄(<c>new("&lt;id&gt;", Patterns: …</c>)을 읽어 다 찍는다(<c>gif_ids</c>). 전에는 build.sh 가 제 목록
    /// (<c>GIF_IDS</c>)을 따로 들어 대본을 더하는 날 한쪽만 늘 수 있었다(#96). 줄의 꼴을 바꾸면 <c>gif_ids</c> 도 같이 고친다.
    /// </summary>
    public static IEnumerable<string> Ids
    {
        get
        {
            foreach (GifScript s in _scripts)
            {
                yield return s.Id;
            }
        }
    }

    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        GifScript? script = Array.Find(_scripts, s => s.Id == ScriptId);
        if (script is null)
        {
            Log.Error("gif", $"script_missing id={ScriptId} known={string.Join(',', Ids)}");
            GetTree().Quit(1);
            return;
        }

        // 한 프레임 기다린 뒤 판을 세운다 — 이 노드는 Game._Ready 가 트리에 붙이는 도중에 서므로 곧장 씬을 바꾸면 엔진이 "자식을 붙이는 중"
        // 이라며 ERROR 를 찍는다(재 보니 그랬다 — 헤드리스 판정이 그 한 줄로 떨어진다).
        await _drive.Frames(1);
        Log.Info("gif", $"start id={script.Id} patterns={string.Join(',', script.Patterns)} target={script.Target}");

        // "판이 설 때부터" 의 누름은 판을 세우기 **전에** 누른다 — 판은 씬이 선 첫 물리 프레임부터 틱을 밀고, 아래 고리는 판이 몇 틱 돈 뒤에
        // 돈다(재 보니 5틱째부터 걸었다). 레벨이라 씬이 바뀌는 동안에도 눌린 채 남는다.
        var held = new Dictionary<string, bool>(StringComparer.Ordinal);
        Drive(script, _battleStart, int.MinValue, held);
        Overfit.Battle.Battle? battle = await _drive.NewBattle(script.Patterns);
        if (battle is null)
        {
            Log.Error("gif", $"battle_missing id={script.Id}");
            GetTree().Quit(1);
            return;
        }

        int begun = -1;
        bool started = false;
        double waited = 0;
        while (true)
        {
            await _drive.Frames(1);

            // 겨냥한 패턴이 선 판의 틱 — 그 패턴이 처음 보인 신호 자리의 "지금까지 민 틱" 이다(한 물리 프레임에 판은 많아야 한 틱을 민다).
            if (begun < 0 && battle.BossPattern == script.Target)
            {
                begun = battle.SimTicks;
                Log.Info("gif", $"target_begun pattern={script.Target} tick={begun}");
            }

            int next = battle.SimTicks + 1;
            int pattern = begun < 0 ? int.MinValue : next - begun;
            Drive(script, next, pattern, held);

            // 잡을 구간 — 이 프레임은 다음 틱(pattern)을 그린다. Movie Maker 는 그려진 장마다 한 장을 쓰고, 그 번호가 지금까지 그린 장 수다.
            if (!started && pattern == script.From)
            {
                started = true;
                Log.Info("gif", $"capture_from frame={Engine.GetFramesDrawn()} tick={pattern}");
            }

            if (started && pattern == script.To)
            {
                Log.Info("gif", $"capture_to frame={Engine.GetFramesDrawn()} tick={pattern}");
                break;
            }

            // 잡을 구간이 끝나기 전에 판이 끝나면(누가 죽었다) 구간은 영영 안 온다 — 끝난 판은 틱을 안 민다(Battle.SimTicks). 기다림의 상한도
            // 겨냥한 패턴이 선 뒤까지 건다: 안 걸면 창이 뜬 채 도구가 안 돌아온다(gifs 는 --quit-after 도 걸지만 그것은 마지막 울타리다).
            waited += 1.0 / Engine.PhysicsTicksPerSecond;
            if (battle.Over || waited > _timeout)
            {
                Log.Error("gif", $"target_not_captured id={script.Id} pattern={script.Target} begun={begun >= 0} over={battle.Over} waited={waited:0.0}s");
                GetTree().Quit(1);
                return;
            }
        }

        Log.Marker("gif", $"gif=done id={script.Id}");
        GetTree().Quit();
    }

    /// <summary>
    /// 다음 틱의 입력을 넣는다 — 그 틱에 눌려 있어야 할 액션은 누르고 아닌 액션은 뗀다(바뀔 때만 · <c>Input.ActionPress</c>). 판의 틱으로 적은
    /// 입력은 <paramref name="battleTick"/>, 패턴의 틱으로 적은 입력은 <paramref name="patternTick"/>(패턴 앞이면 <c>int.MinValue</c>)로 잰다.
    ///
    /// <para>
    /// <b>엣지(대시 · 패리 · 공격 · 점프)는 한 틱 앞의 신호 자리에서 누르고 다음 신호 자리에서 뗀다.</b> 엔진은 누름을 그다음 물리 프레임의
    /// "막 눌렀다"(<c>IsActionJustPressed</c>)로 센다 — 재 보니 99틱의 신호 자리에서 누른 대시가 100틱에 섰고, 1타 창이 열리는 틱(99)에 서야 할
    /// 대시가 1타에 맞았다. 레벨(이동 · 가드 — <c>IsActionPressed</c>)은 누른 그 틱에 읽힌다. 스크린샷 대본은 몇 프레임 뒤를 찍어 이 한 틱이
    /// 안 보이지만 이 대본은 틱을 박자로 쓴다. 한 판의 입력 규약(엣지 넷 · 레벨 둘)은 설계 §5.4 다.
    /// </para>
    /// </summary>
    private static void Drive(GifScript script, int battleTick, int patternTick, Dictionary<string, bool> held)
    {
        foreach (GifInput input in script.Inputs)
        {
            held.TryAdd(input.Action, false);
        }

        foreach (string action in new List<string>(held.Keys))
        {
            int lead = _edges.Contains(action) ? 1 : 0;
            bool want = false;
            foreach (GifInput input in script.Inputs)
            {
                int now = (input.OnBattleClock ? battleTick : patternTick) + lead;
                want |= input.Action == action && now >= input.From && now <= input.To;
            }

            if (want != held[action])
            {
                held[action] = want;
                SceneDriver.Hold(action, want);
                Log.Debug("gif", $"hold action={action} pressed={want} battle_tick={battleTick} pattern_tick={patternTick}");
            }
        }
    }

    /// <summary>입력 하나 — [<paramref name="From"/>, <paramref name="To"/>] 틱(끝 포함) 동안 <paramref name="Action"/> 을 누르고 있는다.</summary>
    /// <param name="From">첫 틱.</param>
    /// <param name="To">끝 틱(포함). 엣지(대시 · 패리)는 From 과 같게 적는다 — 한 틱 누른다.</param>
    /// <param name="Action">InputMap 의 액션 — 사람이 누르는 것과 같은 길이다.</param>
    /// <param name="OnBattleClock">판의 틱으로 적었나 — 패턴 앞의 입력("판이 설 때부터")이다. 거짓이면 겨냥한 패턴의 틱이다.</param>
    private readonly record struct GifInput(int From, int To, string Action, bool OnBattleClock = false);

    /// <summary>대본 하나 — 단계 · 패턴 순서(대본 고르기) · 겨냥한 패턴 · 입력 · 잡을 구간 [From, To)(패턴 틱).</summary>
    private sealed record GifScript(
        string Id, string[] Patterns, string Target, GifInput[] Inputs, int From, int To);
}
