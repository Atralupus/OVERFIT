"""PPO 학습기 (설계 2026-10-01 조각5). 실행: tools/build.sh train [--iterations=N] [--name=NAME]

바퀴마다 일꾼을 지금 가중치로 부르고, GAE 로 이점을 지어 PPO 로 정책망 · 가치망을 고친다. 출력은 out/train/<이름>/ —
metrics.csv · ckpt_####.json · latest.json. 체크포인트는 조각 6 · 7 의 페이즈 고르기가 고를 후보다.
"""

from __future__ import annotations

import argparse
import csv
import json
import time
from pathlib import Path

import numpy as np

from ml.rl import episodes, net, ppo, worker

ROOT = worker.ROOT


def prepare_out(out: Path, force: bool, pattern: str = "ckpt_*.json") -> None:
    """출력 폴더 — 옛 체크포인트가 있으면 멈춘다(force 면 지운다). 같은 이름으로 다시 돌리면 옛 판의 체크포인트가 새 것 옆에 남아 페이즈 고르기(조각 7)가
    다른 판의 것을 고른다(최종 리뷰가 밟았다)."""
    out.mkdir(parents=True, exist_ok=True)
    old = sorted(out.glob(pattern))
    if old and not force:
        raise FileExistsError(f"{out} 에 옛 체크포인트 {len(old)} 개가 있다 — 다른 --name 을 쓰거나 --force")
    for p in old:
        p.unlink()


def is_checkpoint(iteration: int, every: int, dense_until: int) -> bool:
    """초반(dense_until 바퀴까지)은 바퀴마다, 그 뒤는 every 바퀴마다 — 정책이 초반에 빨리 바뀐다(첫 학습에서 엔트로피가 20 바퀴 안에 무너졌다)."""
    return iteration <= dense_until or iteration % every == 0


def update(policy: net.Mlp, value: net.Mlp, opt_p: ppo.Adam, opt_v: ppo.Adam, r, adv: np.ndarray, ret: np.ndarray, cfg: dict,
           rng: np.random.Generator) -> dict:
    n = len(adv)
    stats = {"policy_loss": 0.0, "value_loss": 0.0, "entropy": 0.0, "approx_kl": 0.0, "clip_frac": 0.0}
    batches = 0
    for _ in range(cfg["epochs"]):
        order = rng.permutation(n)
        for start in range(0, n, cfg["minibatch"]):
            idx = order[start:start + cfg["minibatch"]]
            a = adv[idx]
            a = (a - a.mean()) / (a.std() + 1e-8)
            logits = policy.forward(r.obs[idx])
            loss, g_logits, s = ppo.policy_loss(logits, r.mask[idx], r.action[idx], r.logp[idx], a, cfg["clip"], cfg["entropy_coef"])
            grads = policy.backward(g_logits)
            ppo.clip_grads(grads, cfg["max_grad_norm"])
            opt_p.step(policy.params, grads)

            v = value.forward(r.obs[idx])[:, 0]
            diff = v - ret[idx]
            v_loss = 0.5 * float((diff * diff).mean())
            g_v = (cfg["value_coef"] * diff / len(idx))[:, None]
            grads_v = value.backward(g_v)
            ppo.clip_grads(grads_v, cfg["max_grad_norm"])
            opt_v.step(value.params, grads_v)

            stats["policy_loss"] += loss
            stats["value_loss"] += v_loss
            for k in ("entropy", "approx_kl", "clip_frac"):
                stats[k] += s[k]
            batches += 1
    return {k: v / max(batches, 1) for k, v in stats.items()}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--iterations", type=int)
    ap.add_argument("--name")
    ap.add_argument("--config", default=str(ROOT / "ml" / "rl" / "train.json"))
    ap.add_argument("--force", action="store_true", help="옛 체크포인트가 있는 폴더를 비우고 돈다")
    args = ap.parse_args()
    config = json.loads(Path(args.config).read_text(encoding="utf-8"))
    cfg, reward = config["ppo"], config["reward"]
    iterations = args.iterations or cfg["iterations"]
    out = ROOT / "out" / "train" / (args.name or f"boss-{cfg['seed']}")
    prepare_out(out, args.force)
    rng = np.random.default_rng(cfg["seed"])

    worker.build()
    probe = worker.run(seed=0, episodes=1, out_dir=out / "probe")
    m = probe.manifest
    policy = net.Mlp.init([m["obs"], *cfg["hidden"], m["actions"]], rng, last_scale=cfg["policy_last_scale"])
    value = net.Mlp.init([m["obs"], *cfg["hidden"], 1], rng, last_scale=1.0)
    opt_p, opt_v = ppo.Adam(policy.params, cfg["lr"]), ppo.Adam(value.params, cfg["lr"])
    latest = out / "latest.json"
    net.save(latest, m["obs"], m["actions"], m["roster"], policy, value)
    net.save(out / "ckpt_0000.json", m["obs"], m["actions"], m["roster"], policy, value)

    fields = ["iteration", "rows", "episodes", "boss_win", "return", "ticks", "boss_lost", "fighter_lost",
              "policy_loss", "value_loss", "entropy", "approx_kl", "clip_frac", "seconds"]
    with (out / "metrics.csv").open("w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        for it in range(1, iterations + 1):
            t0 = time.time()
            r = worker.run(seed=cfg["seed"] * 1_000_000 + it, episodes=cfg["episodes"], out_dir=out / "rollout", weights=latest)
            adv, ret = ppo.gae(r.reward, r.value, r.done, cfg["gamma"], cfg["lam"], span=r.span, unit=r.manifest["decide_ticks"])
            s = update(policy, value, opt_p, opt_v, r, adv, ret, cfg, rng)
            net.save(latest, m["obs"], m["actions"], m["roster"], policy, value)
            if is_checkpoint(it, cfg["checkpoint_every"], cfg["checkpoint_dense_until"]):
                net.save(out / f"ckpt_{it:04d}.json", m["obs"], m["actions"], m["roster"], policy, value)
            e = episodes.summary(out / "rollout", reward)
            row = {"iteration": it, "rows": len(r.action), "episodes": e["episodes"], "boss_win": round(e["boss_win"], 4),
                   "return": round(e["return"], 4), "ticks": round(e["ticks"], 1), "boss_lost": round(e["boss_lost"], 1),
                   "fighter_lost": round(e["fighter_lost"], 1), **{k: round(v, 5) for k, v in s.items()},
                   "seconds": round(time.time() - t0, 2)}
            w.writerow(row)
            f.flush()
            print(" ".join(f"{k}={v}" for k, v in row.items()), flush=True)
    print(f"done out={out}")


if __name__ == "__main__":
    main()
