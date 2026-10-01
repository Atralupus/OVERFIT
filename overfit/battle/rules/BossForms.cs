using System;
using System.Collections.Generic;
using System.Linq;

namespace Overfit.Battle.Rules;

/// <summary>
/// 보스의 형태 — 세 페이즈 (설계 2026-10-01 조각1 §2). 지금 형태 · 바닥(다음 문턱 · 마지막 형태면 0) · 전환 시계 · 전환을 시작한 틱들을 든다. 피해를
/// 바닥에서 멈추고 전환을 세우는 것은 판(<see cref="BattleSim"/>)이다 — 여기는 "지금 어디인가" 만 안다. 이름이 phase 가 아닌 것은 <c>BossPhase</c>
/// (선딜 · 판정 · 후딜)와 겹치지 않게다. 유저에게는 "페이즈" 다.
/// </summary>
public sealed class BossForms
{
    private readonly IReadOnlyList<int> _thresholds;
    private readonly int _shiftTicks;
    private readonly List<int> _shifts = new();
    private int _shiftLeft;

    /// <param name="thresholds">남은 체력의 문턱들 — 내려가고, 0 보다 크고 최대 체력보다 작다.</param>
    /// <param name="maxHealth">최대 체력.</param>
    /// <param name="startHealth">시작 체력 — 대본 · 망 GIF 전용(§2.5 · 조각8 §3). 든 칸이 시작 형태이고, 문턱과 같으면 어느 형태인지 몰라 거절한다.</param>
    /// <param name="shiftTicks">전환의 길이(틱) — 1 이상.</param>
    public BossForms(IReadOnlyList<int> thresholds, int maxHealth, int startHealth, int shiftTicks)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        for (int i = 0; i < thresholds.Count; i++)
        {
            int t = thresholds[i];
            if (t <= 0 || t >= maxHealth || (i > 0 && t >= thresholds[i - 1]))
            {
                throw new ArgumentException(
                    $"forms.thresholds 가 틀렸다 — [{string.Join(',', thresholds)}] · 최대 체력 {maxHealth}: 내려가고 0 과 최대 사이여야 한다", nameof(thresholds));
            }
        }

        if (thresholds.Contains(startHealth))
        {
            throw new ArgumentException($"시작 체력 {startHealth} 이 문턱과 같다 — 어느 형태에서 서는지 모른다", nameof(startHealth));
        }

        if (shiftTicks < 1)
        {
            throw new ArgumentException($"forms.shift_seconds 가 한 틱 아래다 ({shiftTicks}틱)", nameof(shiftTicks));
        }

        _thresholds = thresholds;
        _shiftTicks = shiftTicks;
        Form = 1;
        while (Form <= thresholds.Count && thresholds[Form - 1] > startHealth)
        {
            Form++;
        }
    }

    /// <summary>지금 형태 — 1 부터.</summary>
    public int Form { get; private set; }

    /// <summary>형태 수 — 문턱 수 + 1.</summary>
    public int Count => _thresholds.Count + 1;

    /// <summary>피해가 멈추는 바닥 — 다음 문턱, 마지막 형태면 0.</summary>
    public int Floor => Form <= _thresholds.Count ? _thresholds[Form - 1] : 0;

    /// <summary>전환 중인가 — 무적이다.</summary>
    public bool Shifting => _shiftLeft > 0;

    /// <summary>남은 전환의 몫 — 시작한 틱에 1, 끝난 틱에 0. 뷰가 흰 플래시를 나눈다.</summary>
    public double ShiftLeft => (double)_shiftLeft / _shiftTicks;

    /// <summary>전환을 시작한 판의 틱들 — 시도 기록의 <c>form_shifts</c>.</summary>
    public IReadOnlyList<int> Shifts => _shifts;

    /// <summary>이 체력이면 전환을 시작하나 — 전환 중이 아니고 바닥이 0 보다 크고 바닥에 닿았다.</summary>
    public bool Reached(int health) => !Shifting && Floor > 0 && health <= Floor;

    /// <summary>전환을 시작한다.</summary>
    public void Begin(int tick)
    {
        _shiftLeft = _shiftTicks;
        _shifts.Add(tick);
    }

    /// <summary>전환 시계를 한 틱 민다 — 끝나는 틱에 참이고 그때 형태가 오른다.</summary>
    public bool Tick()
    {
        if (_shiftLeft <= 0 || --_shiftLeft > 0)
        {
            return false;
        }

        Form++;
        return true;
    }
}
