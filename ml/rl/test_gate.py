"""비교 관문의 판정 · 시험 묶음 (설계 2026-10-01 조각8 §2). 실행: ml/.venv/bin/python -m ml.rl.test_gate"""

from __future__ import annotations

from pathlib import Path

from ml.rl import gate


def test_passes_only_when_phase3_beats_rule_and_qtable_by_margin() -> None:
    rows = {"random": 0.05, "rule": 0.40, "qtable": 0.52, "form1": 0.2, "form2": 0.5, "form3": 0.62, "game": 0.6}
    ok, best, gap = gate.judge(rows, margin=0.10)
    assert (ok, best) == (True, "qtable") and abs(gap - 0.10) < 1e-12, (ok, best, gap)
    assert gate.judge({**rows, "form3": 0.6199}, margin=0.10)[0] is False
    # 규칙이 더 세면 규칙과 견준다.
    assert gate.judge({**rows, "rule": 0.58}, margin=0.10)[:2] == (False, "rule")


def test_test_pool_is_fleet_and_four_spread_fighters() -> None:
    # evalboss 와 같은 묶음이어야 관문의 승률이 eval.csv 와 같은 자다(조각 6 §4).
    fighters = [Path(f"fighter_r{i:02d}.json") for i in range(21)]
    got = gate.test_pool(fighters)
    assert got[0] == "fleet" and [Path(p).name for p in got[1:]] == [f"fighter_r{i:02d}.json" for i in (5, 10, 15, 20)], got


def main() -> None:
    test_passes_only_when_phase3_beats_rule_and_qtable_by_margin()
    test_test_pool_is_fleet_and_four_spread_fighters()
    print("ok")


if __name__ == "__main__":
    main()
