"""망을 학습한다 (#110 · 설계 2026-09-28 §5) — tools/build.sh train 이 부른다.

    ml/.venv/bin/python ml/train.py [--data=out/factory/1-0-100000] [--seed=0] [--epochs=60] [--batch=1024] [--lr=0.001] [--patience=6] [--force]

원본(공장의 한 폴더) → 봇 번호로 80 · 10 · 10 → 학습 몫으로 표준화 → 칸을 가린 BCE · Adam · 조기 종료(검증 손실) → 시험 몫의 관문(§5.5).
관문을 넘으면 overfit/data/network.json 을 쓴다(--force 면 못 넘어도 쓴다 — 들여다볼 때만). ml/report.md 는 늘 쓴다.

결정론: 시드 고정 · BLAS 한 스레드(아래) — 같은 기기 · 같은 원본이면 같은 가중치다.
"""

import os

for _var in ("OPENBLAS_NUM_THREADS", "OMP_NUM_THREADS", "MKL_NUM_THREADS"):
    os.environ.setdefault(_var, "1")

import argparse  # noqa: E402
import sys  # noqa: E402
import time  # noqa: E402

import numpy as np  # noqa: E402

import data  # noqa: E402
import evaluate  # noqa: E402
import export  # noqa: E402
from net import Adam, Net  # noqa: E402


def parse(argv):
    p = argparse.ArgumentParser(description="패턴을 고르는 망을 학습한다 (#110)")
    p.add_argument("--data", default=os.path.join(data.ROOT, "out", "factory", "1-0-100000"), help="공장의 원본 폴더(설계 §4.8 의 규모)")
    p.add_argument("--seed", type=int, default=0)
    p.add_argument("--epochs", type=int, default=60)
    p.add_argument("--batch", type=int, default=1024)
    p.add_argument("--lr", type=float, default=1e-3)
    p.add_argument("--patience", type=int, default=6, help="검증 손실이 이만큼 안 줄면 멈춘다")
    p.add_argument("--force", action="store_true", help="관문을 못 넘어도 network.json 을 쓴다")
    return p.parse_args(argv)


def say(message):
    print(f"[train][I] {message}", flush=True)


def main(argv):
    args = parse(argv)
    started = time.time()
    raw = data.Raw(args.data)
    digest = data.data_digest()
    if raw.manifest["data_digest"] != digest:
        print(f"[train][E] data_digest 가 다르다 — 원본 {raw.manifest['data_digest'][:12]} · 지금 {digest[:12]}. 공장을 다시 돌려라"
              " (tools/build.sh factory --fleet-seed=1 --from=0 --to=100000)")
        return 1

    heads = data.stage2_heads()
    split = raw.split()
    x = raw.x()
    slot = raw.samples["slot"].to_numpy()
    y = raw.samples["label"].to_numpy(dtype=float)
    train, valid, test = (split[k][0] for k in ("train", "valid", "test"))
    say(f"data={args.data} samples={len(y):,} features={len(raw.features)} heads={len(heads)} "
        f"train={int(train.sum()):,} valid={int(valid.sum()):,} test={int(test.sum()):,} read={time.time() - started:.1f}s")

    mean, std = data.standardize(x[train])
    z = (x - mean) / std
    base_rates = [float(np.mean(y[train & (slot == h)])) for h in range(len(heads))]
    baseline = [evaluate.logit(p) for p in base_rates]
    say("base_rates " + " ".join(f"{h}={p:.3f}" for h, p in zip(heads, base_rates)))

    net = Net.create(len(raw.features), len(heads), args.seed, baseline)
    adam = Adam(net, lr=args.lr)
    rng = np.random.default_rng(args.seed)
    train_rows = np.flatnonzero(train)
    best_loss, best_params, best_epoch, waited = float("inf"), None, 0, 0
    for epoch in range(1, args.epochs + 1):
        order = rng.permutation(train_rows)
        for start in range(0, len(order), args.batch):
            rows = order[start:start + args.batch]
            _, grads = net.loss_and_grads(z[rows], slot[rows], y[rows])
            adam.step(grads)
        loss = net.loss(z[valid], slot[valid], y[valid])
        improved = loss < best_loss - 1e-6
        say(f"epoch={epoch} valid_loss={loss:.5f}{' best' if improved else ''}")
        if improved:
            best_loss, best_epoch, waited = loss, epoch, 0
            best_params = [p.copy() for _, p in net.params()]
        else:
            waited += 1
            if waited >= args.patience:
                break
    for (_, p), saved in zip(net.params(), best_params):
        p[...] = saved

    heads_rows = evaluate.heads_report(net.logits(z[test]), slot[test], y[test], heads, base_rates)
    targeting_rows = evaluate.targeting_report(raw, test, lambda rows: net.logits((rows - mean) / std), heads, baseline, data.fleet_targeting())
    ok = evaluate.passes(heads_rows, targeting_rows)
    for r in heads_rows:
        say(f"head={r['head']} n={r['n']} log_loss={r['log_loss']:.4f} baseline={r['baseline_log_loss']:.4f} ece={r['ece']:.4f} auc={r['auc']:.3f}"
            f"{'' if r['beats_baseline'] and r['calibrated'] else ' FAIL'}")
    for r in targeting_rows:
        say(f"targeting {r['habit']}->{r['pattern']} bots={r['bots']} attempts={r['attempts']} lift={r['lift']:+.3f}"
            f" targeted={r['targeted_share']:.1%}{'' if r['passes'] else ' FAIL'}")

    first, last = raw.manifest["bot_from"], raw.manifest["bot_to"]
    summary = {
        "원본": f"`{os.path.relpath(args.data, data.ROOT)}` · 함대 시드 {raw.manifest['fleet_seed']} · 봇 {first:,} ~ {last - 1:,} · 사례 {len(y):,}",
        "원본의 커밋": f"`{raw.manifest['commit']}`",
        "data_digest": f"`{digest}`",
        "나누기(봇 번호)": " · ".join(f"{k} {split[k][1][0]:,} ~ {split[k][1][1] - 1:,}" for k in ("train", "valid", "test")),
        "학습": f"시드 {args.seed} · 배치 {args.batch} · lr {args.lr} · 가장 좋은 에폭 {best_epoch} · 검증 손실 {best_loss:.5f}",
        "관문": "통과" if ok else "**못 넘음**",
    }
    export.write_report(heads_rows, targeting_rows, summary)
    say(f"report={os.path.relpath(export.REPORT, data.ROOT)} gate={'pass' if ok else 'FAIL'} seconds={time.time() - started:.1f}")
    if not ok and not args.force:
        print("[train][W] 관문을 못 넘었다 — network.json 을 안 쓴다(ml/report.md 를 보라)")
        return 1

    metrics = {
        "split": {k: list(split[k][1]) for k in ("train", "valid", "test")},
        "best_epoch": best_epoch,
        "valid_loss": round(best_loss, 6),
        "test": {r["head"]: {k: (round(r[k], 6) if isinstance(r[k], float) else r[k]) for k in ("n", "log_loss", "baseline_log_loss", "ece", "auc")}
                 for r in heads_rows},
        "targeting": [{k: (round(r[k], 6) if isinstance(r[k], float) else r[k]) for k in ("habit", "pattern", "bots", "attempts", "lift", "targeted_share")}
                      for r in targeting_rows],
        "lift_min": round(evaluate.LIFT_MIN, 6),
    }
    trained_on = {
        "commit": raw.manifest["commit"],
        "data_digest": raw.manifest["data_digest"],
        "fleet_seed": raw.manifest["fleet_seed"],
        "bots": last - first,
    }
    export.write_network(export.network_doc(net, raw.features, mean, std, heads, baseline, trained_on, metrics))
    probe = np.flatnonzero(test)[:500]
    worst = export.check_loop_order(export.NETWORK, x[probe], net.logits(z[probe]))
    say(f"network={os.path.relpath(export.NETWORK, data.ROOT)} loop_order_max_diff={worst:.3g}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
