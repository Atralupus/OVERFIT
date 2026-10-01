"""일꾼의 출력을 읽는다 (설계 2026-10-01 조각4 §7). 실행: ml/.venv/bin/python -m ml.rl.test_rollout"""

from __future__ import annotations

import tempfile

import numpy as np

from ml.rl import rollout


def main() -> None:
    with tempfile.TemporaryDirectory() as tmp:
        r = rollout.run(seed=4, episodes=8, out_dir=tmp)
        m = r.manifest
        n = m["rows"]
        assert n > 0, "줄이 없다"
        assert r.obs.shape == (n, m["obs"]) and r.mask.shape == (n, m["actions"]), "모양이 매니페스트와 다르다"
        assert r.mask[np.arange(n), r.action].all(), "가려진 칸을 뽑았다"
        assert (r.mask.sum(axis=1) >= 2).all(), "열린 칸이 하나뿐인 결정이 적혔다"
        # 가중치 없는 일꾼은 열린 칸에 같은 확률 — logp = −log(열린 칸 수).
        assert np.allclose(r.logp, -np.log(r.mask.sum(axis=1)), atol=1e-5), "같은 확률의 logp 가 아니다"
        assert r.done.sum() == m["episodes"], "판마다 끝이 하나가 아니다"
        assert np.all(np.diff(r.episode) >= 0), "판 순서가 아니다"
        last = np.flatnonzero(r.done)
        assert np.array_equal(r.episode[last], np.arange(m["episodes"])), "끝 줄의 판 번호가 다르다"
        print(f"ok rows={n} obs={m['obs']} actions={m['actions']} boss_wins={m['boss_wins']}")


if __name__ == "__main__":
    main()
