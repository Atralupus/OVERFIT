using Godot;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 잡기의 <b>흰 구</b> (#78 · 설계 §4.7 · §6 「잡기」). 유저: "잡기 연출은 idle애니메이션 상태에서 그냥 흰색 구가 캐릭터를 잡도록 합니다."
/// 규칙은 흰 구를 모른다 — 날고 · 붙들고 · 흩어지는 시각은 규칙의 단계와 잡힘이 정하고(<see cref="OrbFrame"/>), 여기는 그것을 그리기만 한다.
///
/// <list type="bullet">
/// <item><b>난다</b> — 잡기 창 바로 앞 단계(1.30초의 idle) 동안 보스 몸 가운데에서 <b>파이터 발밑 바닥의</b> 몸 가운데로 그 단계가 지난
/// 몫만큼 간다 — 창이 열리는 틱에 땅에 선 파이터에 닿는다. 뛴 파이터를 따라 뜨지 않는다: 잡기가 치는 곳은 바닥이다.</item>
/// <item><b>붙든다</b> — 파이터가 붙들린 동안(<c>Fighter.Held</c>) 파이터를 감싼다.</item>
/// <item><b>기다린다</b> — 못 잡았으면(뛰었으면) 창이 산 동안 파이터 발밑의 바닥에 선다.</item>
/// <item><b>흩어진다</b> — 붙들림이 풀리거나 창이 닫히거나 패턴이 끊기거나(탈진) 판이 끝나면(<c>Battle</c> 이 거른다) 그 자리에서
/// <c>feel.grab_orb_fade_seconds</c> 동안 부풀며 사라진다.</item>
/// </list>
///
/// <para>
/// <b>보스 공격의 링이 아니다</b> (#81 과의 경계). #81 은 "적 공격에 동그라미 연출" 을 걷었다 — 예고 링과 판정 충격파는 공격이 온다고 말하는
/// 동그라미였다. 흰 구는 잡는 <b>물체</b>이고, 유저가 그것을 잡기의 그림으로 정했다. 그래서 남긴다(설계 §12 「흰 구」). 잡기의 띠는 착지와 같은
/// 바닥 전체 모양이지만 착지의 흰 충격파는 안 선다 — 잡기의 그림은 이것 하나다(<c>BattleCues</c> · <c>BattleSim.BossTestedGrab</c>).
/// </para>
///
/// <para>
/// 크기는 <c>feel.grab_orb_radius</c>(90 — 파이터 키 120 의 3/4, 몸을 감쌀 만큼)이고 색은 이름 붙은 상수(<see cref="_fill"/>)다 — 수치는
/// 데이터, 색은 "얼마나" 가 아니라 "무엇" 이라 뷰의 이름 붙은 상수라는 규약(<c>FeelBalance</c> 머리) 그대로다. 착지의 흰 충격파(<c>LandingWave</c>)와
/// 같은 자리다. 몸 가운데는 발에서 이 반지름만큼 위로 잡는다: 파이터 몸(120)의 가운데(60)를 감싸려면 구가 땅에 닿아야 붙든 그림이 된다.
/// </para>
/// </summary>
public partial class GrabOrb : Node2D
{
    /// <summary>흰 구의 색 — 흰색 반투명(설계 §6 「잡기」). 알파 0.55 는 감싼 파이터가 비쳐 보일 만큼이다 — 붙들린 자세(<c>hit</c>)가 보여야 한다.</summary>
    private static readonly Color _fill = new(1, 1, 1, 0.55f);

    private FeelBalance _feel = null!;

    /// <summary>지난 프레임에 무엇을 그렸나 — <c>fly</c> · <c>hold</c> · <c>wait</c>, 안 그렸으면 null. 그렸다가 null 이 된 프레임이 흩어지기의 첫 프레임이다.</summary>
    private string? _state;

    /// <summary>남은 흩어짐(초). 0 이면 흩어지는 중이 아니다.</summary>
    private double _fadeLeft;

    public override void _Ready()
    {
        _feel = Balance.Data.Feel;

        // 두 몸 위에 그린다 — 감싼 구가 파이터 뒤에 가려지면 "잡혔다" 가 안 보인다.
        ZIndex = 10;
        Visible = false;
    }

    /// <summary>한 프레임. <paramref name="frame"/> 의 모든 값은 규칙이 정한 것이다.</summary>
    public void Show(OrbFrame frame)
    {
        double radius = _feel.GrabOrbRadius;
        Vector2 fighter = new((float)frame.FighterX, (float)-(frame.FighterY + radius));

        // 날아갈 곳과 기다릴 곳은 파이터 발밑 **바닥**의 몸 가운데다 — 떠 있는 몸이 아니다. 잡기의 띠는 바닥(높이 60)을 치므로 흰 구가 창이
        // 열리기 전부터 "맞는 자리는 바닥" 이라고 말해야 한다(설계 §12 「잡기의 충격파」 · 보이는 것이 곧 맞는 것). 처음 것은 떠 있는 몸 가운데로
        // 날았다 — 보고 뛴 사람(90틱)에게 흰 구가 공중에서 닿았다가(101틱 · 발 192 · 흰 구 276) 창이 열리는 틱(102)에 한 프레임 만에 바닥(90)으로
        // 떨어졌다. 제대로 뛴 사람이 "공중에서 잡혔다" 로 읽는다. 땅에 선 사람에게는 둘이 같다(발 0).
        Vector2 floor = new((float)frame.FighterX, (float)-radius);
        Vector2 boss = new((float)frame.BossX, (float)-(frame.BossY + frame.BossBodyHeight / 2));

        (string? state, Vector2 at) = frame.Held ? ("hold", fighter)
            : frame.Flying ? ("fly", boss.Lerp(floor, (float)frame.Progress))
            : frame.GrabLive ? ("wait", floor)
            : ((string?)null, Position);

        if (state is not null)
        {
            if (state != _state)
            {
                Log.Debug("view", $"grab_orb state={state} x={at.X:0} y={-at.Y:0}");
            }

            _state = state;
            Position = at;
            _fadeLeft = 0;
            Paint(1);
            return;
        }

        // 그리다가 그릴 까닭이 없어졌다 — 그 자리에서 흩어진다(풀림 · 창이 닫힘 · 끊김).
        if (_state is not null)
        {
            _state = null;
            _fadeLeft = _feel.GrabOrbFadeSeconds;
            Log.Debug("view", $"grab_orb state=fade x={Position.X:0} y={-Position.Y:0}");
        }

        if (_fadeLeft <= 0)
        {
            Visible = false;
            return;
        }

        _fadeLeft = System.Math.Max(0, _fadeLeft - GetProcessDeltaTime());
        Paint(_feel.GrabOrbFadeSeconds <= 0 ? 0 : _fadeLeft / _feel.GrabOrbFadeSeconds);
    }

    /// <summary>남은 몫 <paramref name="left"/>(1 = 온전하다 → 0 = 다 흩어졌다)만큼 그린다 — 흩어지는 동안 반지름이 1.5배까지 부푼다.</summary>
    private void Paint(double left)
    {
        Visible = left > 0;
        Scale = Vector2.One * (float)(1 + (0.5 * (1 - left)));
        Modulate = new Color(1, 1, 1, (float)left);
    }

    public override void _Draw() => DrawCircle(Vector2.Zero, (float)_feel.GrabOrbRadius, _fill);
}
