using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Overfit.Battle.Rules;
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
/// 대본 id 는 ASCII 소문자다(<c>offbeat</c>) — 인자와 파일 이름(<c>docs/gifs/&lt;id&gt;.gif</c>)으로 쓰인다. 대본은 이 안의 C# 표다 — 스크린샷
/// 대본이 코드인 것과 같다. 조각 1 에서 없어진 동작을 쓰던 대본 셋(<c>rush</c> · <c>grab</c> · <c>jump3</c>)은 걷었다 — 새 대본은 GIF 를 다시 찍을
/// 때 짠다(설계 2026-09-29 조각1 §4.2). README 의 그 세 그림(<c>docs/gifs/</c>)은 그대로 둔다.
/// </para>
/// </summary>
public partial class GifRunner : Node
{
    /// <summary>
    /// 한 대본의 상한(초) — 판이 서고 잡을 구간이 끝나기까지. 넘으면 <c>[E]</c> 로 멈춘다(GIF 가 안 찍힌 것은 도구의 실패다). 대본은 길어야
    /// 몇 초다(<c>offbeat</c> — 판이 서고 엇박 3연격의 둘째 판정 + 30틱까지 3초 남짓).
    /// </summary>
    private const double _timeout = 30.0;

    /// <summary>판의 틱으로 적는 입력의 시작 — "판이 설 때부터".</summary>
    private const int _battleStart = 1;

    /// <summary>엣지인 액션 (설계 §5.4 · <c>InputFrame</c>) — 한 틱 앞에 누른다(<see cref="Drive"/>). 나머지(이동 · 가드)는 레벨이다.</summary>
    private static readonly HashSet<string> _edges = new(StringComparer.Ordinal) { "jump", "dash", "parry", "attack" };

    /// <summary>
    /// 대본 (설계 §6.2 의 표 — 조각 1 뒤에는 하나다). 틱은 패턴의 틱이고, 판의 틱으로 적은 것은 <see cref="GifInput.OnBattleClock"/> 이 참이다.
    /// 잡을 구간은 [<see cref="GifScript.From"/>, <see cref="GifScript.To"/>) 패턴 틱 — 60fps 로 240장(4초)을 넘지 않는다(도구가 막는다).
    /// 대본마다 같은 입력을 규칙 위에서 틱까지 못박은 테스트가 있다(<c>MoveBattleTests</c>) — 숫자를 바꾸면 거기서 먼저 잰다.
    /// </summary>
    private static readonly GifScript[] _scripts =
    {
        // 패리를 많이 한다 → 엇박 3연격 (설계 §4.9). 1타 사거리 안으로 걸어 들어가 3연격의 박자(1타 51틱의 2틱 앞 · 49틱)에 K 를 누른다 — 엇박의
        // 1타는 60틱이라 패리의 창(+6)을 지나 커밋(+18) 안에 떨어져 맨몸으로 맞는다. 헛친 한 번(커밋과 패리 뒤 경직 · 0.583초 · #82)이 84틱에 풀리면
        // 맞은 틱(60)에서 3연격의 간격(42틱)을 재어 2틱 앞(100)에 또 누른다 — 늦은 2타(111)에 또 맞는다. 둘째 + 30 까지 잡는다.
        new("offbeat", Plans: SceneDriver.Moves("엇박 3연격"), Target: "엇박 3연격",
            Inputs: new[]
            {
                new GifInput(_battleStart, 68, "move_right", OnBattleClock: true),
                new GifInput(49, 49, "parry"),
                new GifInput(100, 100, "parry"),
            },
            From: 1, To: 141),
    };

    private readonly SceneDriver _drive;

    public GifRunner() => _drive = new SceneDriver(this, "gif");

    /// <summary>돌릴 대본의 id — <c>Game</c> 이 <c>--gif=</c> 에서 읽어 세운다.</summary>
    public string ScriptId { get; set; } = "";

    /// <summary>
    /// 등록된 대본 id 들 — 모르는 id 를 받았을 때 로그에 싣는다. 대본 목록은 위의 표(<see cref="_scripts"/>) 하나다: <c>tools/build.sh gifs</c> 는
    /// 인자 없이 돌면 이 파일에서 표의 줄(<c>new("&lt;id&gt;", Plans: …</c>)을 읽어 다 찍는다(<c>gif_ids</c>). 전에는 build.sh 가 제 목록
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
        Log.Info("gif", $"start id={script.Id} plans={Game.ScriptText(script.Plans)} target={script.Target}");

        // "판이 설 때부터" 의 누름은 판을 세우기 **전에** 누른다 — 판은 씬이 선 첫 물리 프레임부터 틱을 밀고, 아래 고리는 판이 몇 틱 돈 뒤에
        // 돈다(재 보니 5틱째부터 걸었다). 레벨이라 씬이 바뀌는 동안에도 눌린 채 남는다.
        var held = new Dictionary<string, bool>(StringComparer.Ordinal);
        Drive(script, _battleStart, int.MinValue, held);
        Overfit.Battle.Battle? battle = await _drive.NewBattle(script.Plans);
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
        string Id, ScriptPlan[] Plans, string Target, GifInput[] Inputs, int From, int To);
}
