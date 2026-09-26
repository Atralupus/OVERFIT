using System.Collections.Generic;
using Godot;
using Overfit.Battle.Rules;
using Overfit.Core;

namespace Overfit.Battle.View;

/// <summary>
/// 규칙의 판정 사각형을 <see cref="CollisionShape2D"/> 로 옮겨, Godot 의 <b>Visible Collision Shapes</b>
/// (에디터 Debug 메뉴 · 실행 인자 <c>--debug-collisions</c>)가 그리게 한다 (이슈 #59 · 설계 §6.1).
///
/// <para>
/// <b>충돌 계산은 하나도 안 한다</b> — 모양을 들고만 있다(Area2D · monitoring 끔 · 레이어 0). 판정은 규칙 층이 하고,
/// 여기 놓이는 사각형은 규칙이 <b>이 틱에 실제로 대 본 것</b>이다(<see cref="BattleSim.BossTestedRects"/> 등).
/// 뷰가 hitboxes.json 을 따로 읽어 좌우를 뒤집고 발 위치를 더하면, 규칙 쪽 그 계산에 버그가 있어도 여기는
/// 멀쩡해 보인다 — 가장 필요한 순간에 거짓말하는 도구가 된다.
/// </para>
///
/// <para>
/// 판정을 Godot 물리로 옮기지 않는 이유: 학습 데이터는 봇이 <b>Godot 없이</b> 헤드리스로 수백만 판을 돌려
/// 만든다. 판정이 Area2D 에 있으면 판마다 엔진을 띄워야 하고, "같은 시드면 같은 결과" 도 물리 스텝에 묶인다.
/// </para>
/// </summary>
public partial class HitboxDebug : Node2D
{
    private static readonly Color _bossTested = new(1.0f, 0.2f, 0.2f, 0.45f);
    private static readonly Color _bossNext = new(1.0f, 0.7f, 0.1f, 0.15f);
    private static readonly Color _fighterTested = new(0.2f, 0.9f, 1.0f, 0.45f);
    private static readonly Color _bossBody = new(0.4f, 0.55f, 1.0f, 0.2f);
    private static readonly Color _fighterBody = new(0.3f, 1.0f, 0.4f, 0.25f);
    private static readonly Color _invulnerable = new(0.7f, 0.7f, 0.7f, 0.25f);
    private static readonly Color _parrying = new(1.0f, 0.95f, 0.2f, 0.35f);
    private static readonly Color _guarding = new(0.65f, 0.4f, 1.0f, 0.3f);

    private readonly List<CollisionShape2D> _pool = new();
    private Area2D _area = null!;
    private int _used;

    public override void _Ready()
    {
        // 스프라이트 위에 그린다 — 판정이 몸 뒤에 가려지면 보이는 것이 곧 맞는 것인지 볼 수 없다.
        ZIndex = 100;
        _area = new Area2D
        {
            Monitoring = false,
            Monitorable = false,
            CollisionLayer = 0,
            CollisionMask = 0,
        };
        AddChild(_area);
        Log.Debug("debug", "hitboxes=on");
    }

    /// <summary>
    /// 파이터 몸통의 색 — 규칙이 내놓은 <b>실효</b> 상태를 칠한다 (#72 · 설계 §6.1): 대시 무적(회색) · 패리 창(노랑) ·
    /// 가드(보라), 셋 다 아니면 초록이다. 이 틱에 대 본 보스 판정이 있으면 그 판정의 태그와 견준 값이다
    /// (<see cref="HitResolver.Effective"/>) — 패리를 못 받는 착지 띠 앞에서 누른 패리는 노랑이 아니라 초록이다.
    ///
    /// <para>
    /// 전에는 파이터 쪽 상태만 칠해서 <c>dash_window</c> 가 0 인 판정 앞에서도 몸통이 회색인데 규칙은 맞음을 냈다 —
    /// 판정 보기가 가장 필요한 순간에 거짓말을 했다. 뷰가 다시 계산하지 않고 규칙이 판정에 쓰는 바로 그 함수의 값을 받는다.
    /// </para>
    /// </summary>
    public static Color FighterColor(Defense defense) => defense switch
    {
        Defense.Invulnerable => _invulnerable,
        Defense.Parrying => _parrying,
        Defense.Guarding => _guarding,
        _ => _fighterBody,
    };

    /// <summary>이 프레임에 보일 사각형들을 놓는다. <b>규칙 좌표(위가 +)</b>로 받는다.</summary>
    public void Show(
        IReadOnlyList<HitRect> bossTested,
        IReadOnlyList<HitRect> bossNext,
        IReadOnlyList<HitRect> fighterTested,
        HitRect fighterBody,
        HitRect bossBody,
        Color fighterBodyColor)
    {
        _used = 0;
        Put(bossBody, _bossBody);
        Put(fighterBody, fighterBodyColor);
        foreach (HitRect r in bossNext)
        {
            Put(r, _bossNext);
        }

        foreach (HitRect r in bossTested)
        {
            Put(r, _bossTested);
        }

        foreach (HitRect r in fighterTested)
        {
            Put(r, _fighterTested);
        }

        for (int i = _used; i < _pool.Count; i++)
        {
            _pool[i].Visible = false;
        }
    }

    private void Put(HitRect r, Color color)
    {
        CollisionShape2D shape;
        if (_used < _pool.Count)
        {
            shape = _pool[_used];
        }
        else
        {
            shape = new CollisionShape2D { Shape = new RectangleShape2D() };
            _area.AddChild(shape);
            _pool.Add(shape);
        }

        _used++;

        ((RectangleShape2D)shape.Shape).Size = new Vector2((float)(r.X1 - r.X0), (float)(r.Y1 - r.Y0));
        // 규칙은 위가 + 이고 화면은 아래가 + 다 — FighterView 의 Position = (X, −Y) 와 같은 뒤집기다.
        shape.Position = new Vector2((float)((r.X0 + r.X1) / 2), (float)(-(r.Y0 + r.Y1) / 2));
        shape.DebugColor = color;
        shape.Visible = true;
    }
}
