"""보스 저장본마다 시험 상대 묶음과의 승률 (설계 2026-10-01 조각6 §4). 실행: tools/build.sh evalboss [--name=NAME]

시험 묶음 = 봇 함대 + 파이터 저장본 넷(라운드의 1/4 · 2/4 · 3/4 · 끝) — 모든 보스 저장본이 같은 묶음과 싸운다. → out/selfplay/<이름>/eval.csv.
조각 7 의 페이즈 고르기가 읽는다.
"""

from __future__ import annotations

import argparse
import csv
import json
from pathlib import Path

from ml.rl import episodes, gate, worker

ROOT = worker.ROOT


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", required=True)
    ap.add_argument("--config", default=str(ROOT / "ml" / "rl" / "train.json"))
    args = ap.parse_args()
    config = json.loads(Path(args.config).read_text(encoding="utf-8"))
    cfg, sp = config["ppo"], config["selfplay"]
    out = ROOT / "out" / "selfplay" / args.name
    bosses = sorted(out.glob("boss_r*.json"))
    opponents = gate.test_pool(list(out.glob("fighter_r*.json")))
    print("opponents=" + ",".join(Path(o).name for o in opponents))
    worker.build()
    with (out / "eval.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["boss", "episodes", "boss_win", "return", "ticks", "boss_lost", "fighter_lost"])
        for b in bosses:
            r_dir = out / "eval" / b.stem
            worker.run(cfg["compare_seed"], sp["eval_episodes"], r_dir, weights=b, learner="boss", opponents=opponents)
            s = episodes.summary(r_dir, config["reward"])
            w.writerow([b.stem, s["episodes"], round(s["boss_win"], 4), round(s["return"], 4), round(s["ticks"], 1),
                        round(s["boss_lost"], 1), round(s["fighter_lost"], 1)])
            f.flush()
            print(f"{b.stem} boss_win={s['boss_win']:.3f} return={s['return']:+.4f} ticks={s['ticks']:.0f}", flush=True)


if __name__ == "__main__":
    main()
