"""셀프 플레이 — 보스 망과 파이터 망을 번갈아 학습한다 (설계 2026-10-01 조각6 §3). 실행: tools/build.sh selfplay [--rounds=R] [--name=NAME]

출력은 out/selfplay/<이름>/ — boss_r##.json · fighter_r##.json(라운드마다의 저장본) · metrics.csv. 파이터 망은 학습용 상대라 게임에는 안 들어간다.
"""

from __future__ import annotations

import argparse
import csv
import json
import time
from pathlib import Path

import numpy as np

from ml.rl import episodes, net, ppo, train, worker

ROOT = worker.ROOT


def pool(snapshots: list[Path], latest: int, spread: int) -> list[Path]:
    """상대 묶음 — 최신 latest 개 + 그 앞에서 고르게 spread 개. 최신 상대 하나에만 강해지는 편향을 막는다(우산 §5.3)."""
    if not snapshots:
        return []
    chosen = snapshots[-latest:]
    older = snapshots[:-latest]
    if older and spread > 0:
        idx = np.linspace(0, len(older) - 1, num=min(spread, len(older))).round().astype(int)
        chosen = [older[i] for i in sorted(set(idx.tolist()))] + chosen
    return chosen


class Side:
    """배우는 쪽 하나 — 망 둘(정책 · 가치) · Adam · 최신 가중치 파일."""

    def __init__(self, name: str, manifest: dict, cfg: dict, rng: np.random.Generator, out: Path):
        self.name, self.cfg, self.out = name, cfg, out
        self.dims = (manifest["obs"], manifest["actions"], manifest["roster"])
        self.policy = net.Mlp.init([manifest["obs"], *cfg["hidden"], manifest["actions"]], rng, last_scale=cfg["policy_last_scale"])
        self.value = net.Mlp.init([manifest["obs"], *cfg["hidden"], 1], rng, last_scale=1.0)
        self.opt_p, self.opt_v = ppo.Adam(self.policy.params, cfg["lr"]), ppo.Adam(self.value.params, cfg["lr"])
        self.latest = out / f"{name}_latest.json"
        self.save(self.latest)

    def save(self, path: Path) -> None:
        net.save(path, *self.dims, self.policy, self.value)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--rounds", type=int)
    ap.add_argument("--iterations", type=int, help="라운드마다 바퀴 수(보스 · 파이터 같게) — 시험용")
    ap.add_argument("--name")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--config", default=str(ROOT / "ml" / "rl" / "train.json"))
    args = ap.parse_args()
    config = json.loads(Path(args.config).read_text(encoding="utf-8"))
    cfg, sp = config["ppo"], config["selfplay"]
    rounds = args.rounds or sp["rounds"]
    iters = {"boss": args.iterations or sp["boss_iterations"], "fighter": args.iterations or sp["fighter_iterations"]}
    out = ROOT / "out" / "selfplay" / (args.name or f"sp-{cfg['seed']}")
    train.prepare_out(out, args.force, pattern="*_r*.json")
    rng = np.random.default_rng(cfg["seed"])

    worker.build()
    boss = Side("boss", worker.run(0, 1, out / "probe_boss").manifest, cfg, rng, out)
    fighter = Side("fighter", worker.run(0, 1, out / "probe_fighter", learner="fighter").manifest, cfg, rng, out)
    snaps = {"boss": [out / "boss_r00.json"], "fighter": [out / "fighter_r00.json"]}
    boss.save(snaps["boss"][0])
    fighter.save(snaps["fighter"][0])

    fields = ["round", "side", "iteration", "rows", "episodes", "boss_win", "return", "ticks", "boss_lost", "fighter_lost",
              "policy_loss", "value_loss", "entropy", "approx_kl", "clip_frac", "seconds"]
    with (out / "metrics.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        for rnd in range(1, rounds + 1):
            for side, other, base, opponents_fixed, eps in (
                (boss, "fighter", 0, ["fleet"], sp["boss_episodes"]),
                (fighter, "boss", 500, ["rule"], sp["fighter_episodes"]),
            ):
                opponents = opponents_fixed + [str(p) for p in pool(snaps[other], sp["pool_latest"], sp["pool_spread"])]
                for it in range(1, iters[side.name] + 1):
                    t0 = time.time()
                    seed = cfg["seed"] * 1_000_000 + rnd * 1000 + base + it
                    r = worker.run(seed, eps, out / f"rollout_{side.name}", weights=side.latest, learner=side.name, opponents=opponents)
                    adv, ret = ppo.gae(r.reward, r.value, r.done, cfg["gamma"], cfg["lam"], span=r.span,
                                       unit=cfg["decide_ticks"] if side.name == "boss" else 6)
                    s = train.update(side.policy, side.value, side.opt_p, side.opt_v, r, adv, ret, cfg, rng)
                    side.save(side.latest)
                    e = episodes.summary(out / f"rollout_{side.name}", config["reward"])
                    ep_return = float(np.bincount(r.episode, weights=r.reward).mean()) if len(r.reward) else 0.0
                    row = {"round": rnd, "side": side.name, "iteration": it, "rows": len(r.action), "episodes": e["episodes"],
                           "boss_win": round(e["boss_win"], 4), "return": round(ep_return, 4), "ticks": round(e["ticks"], 1),
                           "boss_lost": round(e["boss_lost"], 1), "fighter_lost": round(e["fighter_lost"], 1),
                           **{k: round(v, 5) for k, v in s.items()}, "seconds": round(time.time() - t0, 2)}
                    w.writerow(row)
                    f.flush()
                    print(" ".join(f"{k}={v}" for k, v in row.items()), flush=True)
                snap = out / f"{side.name}_r{rnd:02d}.json"
                side.save(snap)
                snaps[side.name].append(snap)
    print(f"done out={out}")


if __name__ == "__main__":
    main()
