using Godot;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 점프 공격의 착지 — 착지한 보스의 발밑에서 바닥을 따라 양옆으로 퍼지는 <b>흰 충격파</b> (#83). 유저: "점프공격때 하단영역에 데미지를
/// 준다는 연출이 있어야겠네요 흰색영역이 퍼지면서 충격파를 주는듯한 연출을 추가해주세요." (2026-09-26)
///
/// <para>
/// 착지 판정은 그림의 칼 궤적이 아니라 바닥 띠(<c>band [0, 1920, 0, 60]</c>)라, 보스 공격의 링을 걷은 뒤(#81)에는 "낮은 곳이 맞는다" 가
/// 화면 어디에도 없었다. 그래서 띠 자체를 그린다 — <b>모양은 규칙이 그 틱에 대 본 판정 사각형에서 읽는다</b>(<see cref="FloorWave"/>).
/// 높이는 판정의 높이이고 앞머리는 판정의 가로 끝(아레나로 자른 것)까지 간다. 보이는 것이 곧 맞는 것이다.
/// </para>
///
/// <para>
/// <b>셋을 겹쳐 그린다</b> (리뷰 m4). ① <b>밑깔개</b> — 판정 전체(아레나로 자른 것)를 선 첫 프레임부터 옅게 깐다. 규칙은 창의 첫 틱에
/// 바닥 전체를 치므로 앞머리가 아직 발밑에 있어도 "낮은 곳이 다 지금 맞는다" 가 화면에 있어야 한다. ② <b>띠</b> — 두 앞머리 사이를
/// 밑깔개보다 밝게. ③ <b>앞머리</b> — 양쪽으로 창 내내 고르게 달려 나가는 밝은 끝. 앞머리는 충격파가 퍼지는 것을, 밑깔개는 맞는 자리
/// 전체를 말한다. 전에는 밑깔개 없이 앞머리가 화면 밖(±1920)을 향해 처음이 빠르게 가서, 퍼지는 것이 두세 프레임만 보여 번쩍임으로 읽혔다.
/// </para>
///
/// <para>
/// <b>동그라미가 아니다</b> — 반투명한 흰 띠와, 양쪽으로 달려 나가는 밝은 앞머리다. 보스 공격에 동그라미를 안 그린다는 #81 과 부딪히지 않는다.
/// </para>
///
/// <para>
/// 보스 밑에 달지 않고 <c>World</c> 에 따로 둔다 — 보스는 판정 창이 닫힌 뒤 걸어가는데 띠는 착지한 자리에 남아 옅어져야 한다.
/// 두 몸 <b>위</b>에 그린다(씬에서 <c>FighterView</c> 뒤) — 띠가 몸 뒤에 가려지면 발이 그 높이 안에 있는지가 안 보인다. 반투명이라 발은 비친다.
/// </para>
///
/// <para>
/// 시계는 그림의 것(<c>_Process</c>)이다. 판정 창(틱)을 따라 퍼지게 하지 않는 것은 닿은 판정이 창의 첫 틱에 끝나기 때문이다 — 규칙이 대 본
/// 사각형은 그 한 틱뿐이라, 창을 따라가면 띠가 한 프레임에 사라진다. 퍼지는 시간은 창 안에 들게 데이터가 잡는다(<c>FloorWaveTests</c>).
/// </para>
/// </summary>
public partial class LandingWave : Node2D
{
    /// <summary>충격파의 색. 색은 "얼마나" 가 아니라 "무엇" 이라 이름 붙은 상수로 둔다(<c>FeelBalance</c> 머리) — 밝기는 <c>feel</c> 이다.</summary>
    private static readonly Color _white = Colors.White;

    private FeelBalance _feel = null!;

    /// <summary>지금 그리는 충격파. 없으면 null — 다 옅어지면 걷는다.</summary>
    private FloorWave? _wave;

    /// <summary>충격파가 선 뒤 지난 시간(초).</summary>
    private double _age;

    public override void _Ready() => _feel = Balance.Data.Feel;

    /// <summary>
    /// 충격파를 세운다 — 바닥 전체를 치는 판정이 서는 틱에 <c>BattleCues</c> 가 부른다. 이미 퍼지던 것이 있으면 새것으로 갈아 끼운다.
    /// 지금 데이터의 바닥 전체 판정은 점프 공격의 착지 하나라 겹칠 일이 없고, 설계의 점프 ×3(§4.8 · 5번 PR)도 착지가 1.5초 간격이다 —
    /// 충격파는 0.4초 남짓 산다(퍼짐 0.125 + 옅어짐 0.3). 갈아 끼우는 것은 그 간격이 줄어드는 날을 위한 것이다: 앞의 띠를 이어 그리면
    /// 새 착지의 발밑이 안 보인다.
    /// </summary>
    public void Start(FloorWave wave)
    {
        _wave = wave;
        _age = 0;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_wave is null)
        {
            return;
        }

        _age += delta;
        if (_age >= _feel.LandingWaveSpreadSeconds + _feel.LandingWaveFadeSeconds)
        {
            _wave = null;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_wave is not { } wave)
        {
            return;
        }

        double spread = _feel.LandingWaveSpreadSeconds;
        (double left, double right) = wave.Fronts(spread <= 0 ? 1 : _age / spread);

        // 다 퍼지기 전에는 안 옅어진다 — 판정이 사는 동안 흐려지면 "끝났다" 로 읽힌다. 그 뒤는 제곱으로 옅어진다: 선형이면 끝까지 흐릿하게
        // 남아 꼬리가 길다(Afterimage 와 같은 곡선).
        double fadeSeconds = _feel.LandingWaveFadeSeconds;
        float left01 = fadeSeconds <= 0 ? 1 : (float)Mathf.Clamp(1 - ((_age - spread) / fadeSeconds), 0, 1);
        float strength = left01 * left01;

        // 규칙은 위가 + 이고 화면은 아래가 + 다 — 바닥(0)에서 위로 판정의 높이만큼이 화면에서는 -높이 ~ 0 이다.
        float top = -(float)wave.Height;
        float x0 = (float)left;
        float x1 = (float)right;
        var underlay = new Color(_white, (float)_feel.LandingWaveUnderlayAlpha * strength);
        var fill = new Color(_white, (float)_feel.LandingWaveAlpha * strength);
        var edge = new Color(_white, (float)_feel.LandingWaveEdgeAlpha * strength);

        // 밑깔개 — 판정 전체(아레나로 자른 것)를 첫 프레임부터. 앞머리가 발밑에 있는 첫 틱에 규칙은 이미 이 전부를 친다. 띠와 앞머리는 그
        // 위에 겹친다: 앞머리가 지나간 곳이 더 밝아 달리는 것이 읽힌다.
        // 밑깔개는 아레나 끝보다 흔들림만큼 더 그린다 — 맞는 판정은 그 너머까지 가지만(아레나를 넘는 칸은 아무도 못 선다) 착지가 닿는
        // 순간 화면이 흔들려 World 가 shake_pixels 만큼 밀리면, 딱 아레나에서 자른 띠는 화면 끝에서 그만큼 모자라 보인다(판정 보기 사진
        // hitbox-2-landing 의 9px 틈 · 리뷰 n1). 바닥 슬래브(Battle.tscn 의 Ground)가 끝을 밖으로 흘리는 것과 같은 이유다.
        float bleed = (float)_feel.ShakePixels;
        DrawRect(new Rect2((float)wave.Left - bleed, top, (float)(wave.Right - wave.Left) + (2 * bleed), -top), underlay);

        // 앞머리 — 앞끝이 가장 밝고 안쪽으로 띠의 밝기까지 내려온다. 발밑보다 안쪽으로는 안 넘어간다(막 섰을 때 두 앞머리가 겹치지 않게).
        // 평평한 띠는 두 앞머리 **사이**에만 칠한다 — 앞머리 밑까지 칠하면 앞머리의 안쪽 끝에서 알파가 겹쳐 턱이 진다.
        float width = (float)_feel.LandingWaveEdgeWidth;
        float origin = (float)wave.Origin;
        float innerLeft = Mathf.Min(x0 + width, origin);
        float innerRight = Mathf.Max(x1 - width, origin);
        if (innerRight > innerLeft)
        {
            DrawRect(new Rect2(innerLeft, top, innerRight - innerLeft, -top), fill);
        }

        DrawFront(innerRight, x1, top, fill, edge);
        DrawFront(innerLeft, x0, top, fill, edge);
    }

    /// <summary>앞머리 하나 — <paramref name="inner"/> 에서 띠의 밝기, <paramref name="outer"/>(앞끝)에서 앞머리의 밝기인 가로 그라디언트.</summary>
    private void DrawFront(float inner, float outer, float top, Color fill, Color edge)
    {
        if (Mathf.IsEqualApprox(inner, outer))
        {
            return;
        }

        DrawPolygon(
            [new Vector2(inner, top), new Vector2(outer, top), new Vector2(outer, 0), new Vector2(inner, 0)],
            [fill, edge, edge, fill]);
    }
}
