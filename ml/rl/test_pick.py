"""페이즈 고르기 (설계 2026-10-01 조각7 §1 · 이슈 #167). 실행: ml/.venv/bin/python -m ml.rl.test_pick"""

from __future__ import annotations

import numpy as np

from ml.rl import pick


def _rows(wins: list[float]) -> list[dict]:
    return [{"boss": f"boss_r{i:02d}", "boss_win": str(w)} for i, w in enumerate(wins)]


def test_every_phase_uses_every_move() -> None:
    # 이슈 #167 — 유저: "어떤 페이즈든 공격 자체는 전체 다 써야". 일곱 공격 중 하나라도 2% 밑인 저장본은 어느 페이즈도 못 된다.
    rows = _rows([0.40, 0.20, 0.30, 0.50, 0.60, 0.85])
    full = [1 / 7] * 7
    shares = {r["boss"]: full for r in rows}
    shares["boss_r01"] = [0.5, 0.49, 0.01, 0.0, 0.0, 0.0, 0.0]  # 가장 약하지만 두 공격만 쓴다
    shares["boss_r05"] = [0.3, 0.3, 0.3, 0.1, 0.0, 0.0, 0.0]  # 마지막이지만 공격을 다 안 쓴다
    got = [r["boss"] for r in pick.choose(rows, shares, min_share=0.02)]
    assert got == ["boss_r02", "boss_r03", "boss_r04"], got


def test_without_shares_keeps_old_rule() -> None:
    rows = _rows([0.40, 0.20, 0.30, 0.50, 0.60, 0.85])
    full = {r["boss"]: [1 / 7] * 7 for r in rows}
    assert [r["boss"] for r in pick.choose(rows, full, min_share=0.02)] == ["boss_r01", "boss_r03", "boss_r05"]


def test_move_shares_counts_only_move_starts() -> None:
    # 칸 [기다리기, 다가가기, 물러서기, 넘어 뛰기, 뒤로 뛰기, 계속하기, 동작 0..] — 동작 칸만 센다.
    action = np.array([0, 1, 6, 6, 7, 5, 8])
    assert pick.move_shares(action, actions=9, roster=3) == [0.5, 0.25, 0.25]


def main() -> None:
    test_every_phase_uses_every_move()
    test_without_shares_keeps_old_rule()
    test_move_shares_counts_only_move_starts()
    print("ok")


if __name__ == "__main__":
    main()
