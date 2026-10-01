"""비교 — 학습에 안 쓴 판에서 규칙 · 무작위 · 망의 판 평균 보상 합 (설계 2026-10-01 조각5 §3). 실행: tools/build.sh compare --weights=FILE"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from ml.rl import episodes, worker

ROOT = worker.ROOT


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--weights", required=True)
    ap.add_argument("--config", default=str(ROOT / "ml" / "rl" / "train.json"))
    ap.add_argument("--out", default=str(ROOT / "out" / "compare"))
    args = ap.parse_args()
    config = json.loads(Path(args.config).read_text(encoding="utf-8"))
    cfg, reward = config["ppo"], config["reward"]
    worker.build()
    rows = []
    for name, controller, weights in (("random", "random", None), ("rule", "rule", None), ("net", "net", args.weights)):
        out = Path(args.out) / name
        worker.run(seed=cfg["compare_seed"], episodes=cfg["compare_episodes"], out_dir=out, weights=weights, controller=controller)
        s = episodes.summary(out, reward)
        rows.append((name, s))
        print(f"{name:7s} return={s['return']:+.4f}±{s['return_se']:.4f} boss_win={s['boss_win']:.3f} ticks={s['ticks']:.0f}"
              f" boss_lost={s['boss_lost']:.1f} fighter_lost={s['fighter_lost']:.1f}")
    best = max(rows, key=lambda x: x[1]["return"])[0]
    print(f"best={best}")


if __name__ == "__main__":
    main()
