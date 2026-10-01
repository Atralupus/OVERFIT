"""페이즈 고르기 (설계 2026-10-01 조각7 §1 · 이슈 #167). 실행: ml/.venv/bin/python -m ml.rl.test_pick"""

from __future__ import annotations

import numpy as np

from ml.rl import pick


def _rows(wins: list[float]) -> list[dict]:
    return [{"boss": f"boss_r{i:02d}", "boss_win": str(w)} for i, w in enumerate(wins)]


def test_phases_sit_between_untrained_and_final() -> None:
    # 이슈 #167 — 유저: "1페이즈도 적당히 학습된 보스여야합니다. 너무 안된 보스말고요". 안 배운 r00 의 승률에서 마지막 저장본의 승률까지를 자로 삼아
    # 1페이즈는 그 0.5, 2페이즈는 0.75 에 가장 가까운 저장본이다(같으면 이른 것). 3페이즈는 마지막.
    rows = _rows([0.40, 0.42, 0.55, 0.66, 0.70, 0.82, 0.90])  # 자: 0.40 → 0.90 · 과녁 0.65 · 0.775
    full = {r["boss"]: [1 / 7] * 7 for r in rows}
    got = [r["boss"] for r in pick.choose(rows, full, min_share=0.01, targets=[0.5, 0.75])]
    assert got == ["boss_r03", "boss_r05", "boss_r06"], got


def test_every_phase_uses_every_move() -> None:
    # 이슈 #167 — 유저: "어떤 페이즈든 공격 자체는 전체 다 써야". 일곱 공격 중 하나라도 min_share 밑인 저장본은 어느 페이즈도 못 된다.
    rows = _rows([0.40, 0.42, 0.55, 0.66, 0.70, 0.82, 0.90])
    full = [1 / 7] * 7
    shares = {r["boss"]: full for r in rows}
    shares["boss_r03"] = [0.5, 0.49, 0.01, 0.0, 0.0, 0.0, 0.0]
    shares["boss_r06"] = [0.3, 0.3, 0.3, 0.1, 0.0, 0.0, 0.0]
    got = [r["boss"] for r in pick.choose(rows, shares, min_share=0.01, targets=[0.5, 0.75])]
    # 3페이즈는 쓸 수 있는 마지막(r05 · 0.82)이고 자는 0.40 → 0.82 · 과녁 0.61 · 0.715 다.
    assert got == ["boss_r02", "boss_r04", "boss_r05"], got


def test_move_shares_counts_only_move_starts() -> None:
    # 칸 [기다리기, 다가가기, 물러서기, 넘어 뛰기, 뒤로 뛰기, 계속하기, 동작 0..] — 동작 칸만 센다.
    action = np.array([0, 1, 6, 6, 7, 5, 8])
    assert pick.move_shares(action, actions=9, roster=3) == [0.5, 0.25, 0.25]


def main() -> None:
    test_phases_sit_between_untrained_and_final()
    test_every_phase_uses_every_move()
    test_move_shares_counts_only_move_starts()
    print("ok")


if __name__ == "__main__":
    main()
