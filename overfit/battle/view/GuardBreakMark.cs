using Godot;

namespace Overfit.Battle.View;

/// <summary>
/// 보스 선딜에 뜨는 <b>가드 불가 표지</b> — 한자 <c>危</c> 한 글자다 (이슈 #47).
///
/// <para>
/// <b>빨강과 같은 말을 한다 — 그래도 둔다</b> (이슈 #53). 마무리가 아홉 변종 전부 가드 불가가 되면서
/// 예고의 빨강은 한 가지 뜻, "가드로 못 막는다 = 받아쳐라" 가 됐고 이 글자도 같은 대에 같은 뜻으로
/// 뜬다. 뜻으로는 겹친다. 남겨 두는 이유는 <b>통로가 다르기</b> 때문이다: 빨강과 호박은 적록 색각
/// 이상에서 가장 먼저 무너지는 짝이다. 완전형 제2색각으로 흉내 내면(Machado 2009) 링의 두 색이
/// <b>같은 겨자색</b>이 되고(색상각 95.9° 대 95.6°) 밝기만 조금 다르다(ΔE2000 12) — 링은 한 번에
/// 하나만 뜨므로 "밝은 쪽인가 어두운 쪽인가" 를 절대 판단으로 맞혀야 하는데, 싸우는 중에 믿을
/// 신호가 아니다. 제1색각에서는 빨강이 오히려 <b>더 어둡고 덜 눈에 띄는</b> 쪽이 된다.
/// 그 사람들에게 이 대가 다르다고 말하는 것은 이 글자의 <b>모양</b>뿐이다.
/// </para>
///
/// <para>
/// ⚠ 한때 이 문서는 "색은 얼마나 위험한가를, 기호는 무엇을 하라를 말한다" 고 적었다. 그 나눔은
/// 거둔다 — <c>危</c> 는 "위험" 이지 "받아쳐라" 가 아니고(세키로에서도 막을 수 없다는 표시다),
/// 이제 색도 같은 것을 말한다. 무엇을 하라는 것은 이 게임이 가르친다: 빨간 대를 받아치면 보스가
/// 굳고 거기 최대 차지가 들어간다.
/// </para>
///
/// <para>
/// <see cref="IBossTellShape"/> 가 <b>아니다.</b> 그쪽은 <c>patterns.json</c> 의 <c>tell.id</c> 로
/// 고르는 <b>패턴마다 다른</b> 모양이고, 이것은 판정 하나의 깃발(<c>guard_break</c>)에 붙는
/// <b>하나뿐인</b> 표지다 — 등록표에 넣으면 "어느 패턴이 이 모양을 쓰는가" 라는, 답이 이미 정해진
/// 질문을 데이터에 다시 묻게 된다.
/// </para>
///
/// <para>
/// 폰트는 엔진 기본(<see cref="ThemeDB"/>)이다. 저장소에 폰트 파일이 없고 한글이 이미 이 경로로
/// 그려지므로(타이틀 · HUD) 한자도 같은 대체 폰트를 탄다. 글리프가 없으면 두부가 나오는데,
/// 그건 <c>[E]</c> 가 아니라 눈으로 잡는 종류의 실패라 <c>tools/build.sh shots</c> 가 본다.
/// </para>
/// </summary>
public static class GuardBreakMark
{
    /// <summary>표지의 글자. 한 글자여야 한다 — 두 글자면 이 크기에서 읽는 시간이 예고보다 길어진다.</summary>
    private const string _glyph = "危";

    /// <summary>
    /// 글자 뒤에 까는 판의 반지름 배수. 보스 몸(호박색 예고 링과 흰 실루엣) 위에 그냥 얹으면
    /// 획이 배경에 먹혀서 <b>무슨 글자인지</b>가 안 읽힌다 — 실제로 그렇다.
    /// </summary>
    private const float _plateScale = 0.62f;

    /// <summary>글자 뒤 판의 색. 거의 검정이다 — 판이 밝으면 글자가 묻히고, 빨간 링 위에 빨간 판을
    /// 깔면 글자만 사라진다.</summary>
    private static readonly Color _plate = new(0.10f, 0.06f, 0.02f, 0.82f);

    /// <summary>
    /// <paramref name="into"/> 의 로컬 좌표에 그린다. 원점은 <b>보스 발밑</b>이고
    /// <b>화면 좌표라 y 는 아래가 +</b> 다 — <see cref="IBossTellShape"/> 와 같은 규약이다.
    /// </summary>
    /// <param name="into">그릴 캔버스. <c>_Draw</c> 안에서만 부른다.</param>
    /// <param name="at">글자의 중심. 부르는 쪽이 보스 키를 알고 정한다.</param>
    /// <param name="size">글자 크기(px).</param>
    /// <param name="color">글자 색. 무르익음(선딜 진행도)이 이미 알파에 섞여 있다.</param>
    public static void Draw(CanvasItem into, Vector2 at, float size, Color color)
    {
        if (into is null || size <= 0)
        {
            return;
        }

        Font font = ThemeDB.Singleton.FallbackFont;
        if (font is null)
        {
            // 폰트가 없으면 글자 대신 아무것도 안 그린다. 링은 그대로 뜨므로 예고가 사라지지는 않는다.
            return;
        }

        into.DrawCircle(at, size * _plateScale, _plate);

        // DrawString 의 기준점은 **베이스라인 왼쪽**이다. 중심에 두려면 실제로 재서 옮겨야 한다 —
        // 눈대중으로 빼면 폰트가 바뀌는 순간 글자가 링 밖으로 새어 나간다.
        Vector2 extent = font.GetStringSize(_glyph, HorizontalAlignment.Left, -1, (int)size);
        into.DrawString(
            font,
            at + new Vector2(-extent.X / 2.0f, extent.Y * 0.36f),
            _glyph,
            HorizontalAlignment.Left,
            -1,
            (int)size,
            color);
    }
}
