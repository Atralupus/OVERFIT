using System;
using Overfit.Core;

namespace Overfit.Battle.Rules;

/// <summary>보스의 움직임 — 무엇을 하는 중인가 (설계 2026-10-01 조각3 §0).</summary>
public enum TravelKind
{
    None,
    Retreat,
    LeapOver,
    LeapBack,
}

/// <summary>
/// 물러서기 · 점프 이동의 상태 (설계 2026-10-01 조각3) — 조종기가 고르면 서고(<see cref="Start"/>) 틱마다 한 걸음(<see cref="Step"/>)이다. 물러서기는
/// 아레나 끝에 닿으면, 점프는 내리면 끝난다. 다가가기의 달리기(<see cref="PlanRun"/>)와 따로 두는 것은 그쪽이 0.11 의 보스 그대로여야 해서다(골든).
/// 점프는 웅크림(예고) 뒤 도약(<see cref="LeapMotion"/> 의 <c>far</c> · <c>away</c>)이다 — 공격이 아니라 판정도 착지 충격파도 없다.
/// </summary>
public sealed class BossTravel
{
    private readonly MovementDef _def;
    private readonly int _crouchTicks;
    private readonly int _airTicks;
    private int _crouchLeft;

    public BossTravel(MovementDef def)
    {
        ArgumentNullException.ThrowIfNull(def);
        _def = def;
        _crouchTicks = BattleSim.TicksFor(def.LeapCrouchSeconds);
        _airTicks = BattleSim.TicksFor(def.LeapAirSeconds);
    }

    public TravelKind Kind { get; private set; }

    public bool Active => Kind != TravelKind.None;

    /// <summary>뛰는 중인가(웅크림 포함) — 반응이 이때는 안 끊는다(공중에서 돌진을 세우면 떠서 달린다).</summary>
    public bool Leaping => Kind is TravelKind.LeapOver or TravelKind.LeapBack;

    /// <summary>웅크리는 중인가 — 뷰가 jump f0 에 세운다.</summary>
    public bool Crouching => Leaping && _crouchLeft > 0;

    /// <summary>내려오는 중인가 — 뜬 시간의 뒤 절반. 뷰가 fall 을 그린다.</summary>
    public bool Falling => Leaping && _crouchLeft <= 0 && MotionTick * 2 > _airTicks;

    /// <summary>도는 움직임 — 공중에서 탈진 · 전환이 끼면 판이 이것을 넘겨받아 높이만 따라 내린다.</summary>
    public IBossMotion? Motion { get; private set; }

    /// <summary>움직임의 틱 — 넘겨받는 쪽이 이어 센다.</summary>
    public int MotionTick { get; private set; }

    /// <summary>움직임을 세운다. 등록표에 없으면 <c>[E]</c> 를 남기고 거짓이다.</summary>
    public bool Start(TravelKind kind, MotionBounds bounds, int tick)
    {
        MotionDef def = kind switch
        {
            TravelKind.Retreat => new MotionDef { Id = "retreat", Speed = _def.RetreatSpeed },
            TravelKind.LeapOver => new MotionDef { Id = "leap", Height = _def.LeapHeight, Air = _def.LeapAirSeconds, Land = "far" },
            _ => new MotionDef { Id = "leap", Height = _def.LeapHeight, Air = _def.LeapAirSeconds, Land = "away", Distance = _def.LeapBackDistance },
        };
        Motion = BossMotions.Create(def, bounds);
        MotionTick = 0;
        _crouchLeft = kind == TravelKind.Retreat ? 0 : _crouchTicks;
        if (Motion is null)
        {
            Log.Error("boss", $"motion_missing id={def.Id} tick={tick}");
            Kind = TravelKind.None;
            return false;
        }

        Kind = kind;
        return true;
    }

    /// <summary>한 걸음 — 끝났으면(물러서기가 끝에 닿음 · 점프가 내림) 참이고 움직임이 걷힌다.</summary>
    public bool Step(Boss boss, double fighterX)
    {
        ArgumentNullException.ThrowIfNull(boss);
        if (Motion is null)
        {
            return true;
        }

        if (_crouchLeft > 0)
        {
            _crouchLeft--;
            return false;
        }

        MotionStep step = Motion.Tick(new MotionContext(boss.X, boss.Y, boss.Facing, fighterX, MotionTick++));
        boss.Move(step.X, step.Y, step.Facing);
        if (!step.Finished)
        {
            return false;
        }

        Clear();
        return true;
    }

    public void Clear()
    {
        Kind = TravelKind.None;
        Motion = null;
        _crouchLeft = 0;
    }
}
