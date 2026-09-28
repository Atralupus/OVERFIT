"""sim-to-real — 사람의 기록을 봇 함대에 견준다 (#114 · 설계 2026-09-28 §7.3).

    ml/.venv/bin/python ml/sim2real.py [기록.jsonl …] [--fleet=out/factory/1-0-100000] [--out=ml/human/report.md]

기록은 게임이 판마다 user://attempts/<세션 시드>.jsonl 에 남긴 줄이다. 사람이 ml/human/ 으로 옮긴다(자동 업로드 없음 · gitignore) — 인자가 없으면
그 폴더의 *.jsonl 전부다. 2단계 시도만 잰다(망이 고르는 자리).

- **분포 밖인가** — 사람의 입력 19칸이 함대(원본의 시도마다의 입력)의 1 ~ 99 백분위 밖인 몫, 칸마다. 봇이 흉내 못 낸 사람이다.
- **보정** — 사람의 사례마다 그 시도를 시작할 때의 예측(지금의 network.json 으로 입력을 순전파 · 게임과 같은 연산 순서)과 라벨. 칸마다 · 갈래마다.
- **갈래 비교** — 망 갈래와 무작위 갈래의 맞는 비율과 그 차. 사람은 적으므로 구간을 같이 적는다(시도를 다시 뽑는 부트스트랩).

입력과 사례는 게임이 한 번 지은 것을 읽는다(PlayerFeatures · InstanceTracker — 공장과 같은 코드). #113 의 게임이 남긴 줄에는 둘이 없어 건너뛰고
몇 줄인지 적는다. 결과는 기록이 쌓인 뒤의 PR 이 README 에 붙인다(§7.3).
"""

import argparse
import glob
import hashlib
import json
import os
import sys

import numpy as np
import pandas as pd

import data
import evaluate
import golden
import validate

HUMAN_DIR = os.path.join(data.ROOT, "ml", "human")
FLEET_DIR = os.path.join(data.ROOT, "out", "factory", "1-0-100000")
LOW, HIGH = 1, 99


class Human:
    """사람의 2단계 시도들 — 입력 · 사례 · 갈래. 옛 줄(입력이나 사례가 없다)과 1단계 줄은 센 뒤 버린다."""

    def __init__(self, lines):
        self.attempts = []
        self.skipped_old = 0
        self.stage1 = 0
        self.networks = set()
        for entry in lines:
            if entry["stage"] != 2:
                self.stage1 += 1
                continue
            if entry.get("features") is None or entry.get("instances") is None:
                self.skipped_old += 1
                continue
            self.attempts.append(entry)
            self.networks.add(entry.get("network_sha256"))


def read_lines(paths):
    """JSONL 을 읽는다 — 빈 줄은 건너뛰고, 깨진 줄은 어느 파일의 몇째 줄인지 말하고 멈춘다."""
    out = []
    for path in paths:
        with open(path, encoding="utf-8") as f:
            for number, line in enumerate(f, 1):
                if line.strip():
                    try:
                        out.append(json.loads(line))
                    except json.JSONDecodeError as e:
                        raise ValueError(f"{path}:{number} 이 깨졌다 — {e}") from e
    return out


def fleet_features(fleet_dir, names):
    """함대의 입력 — 원본의 사례에서 시도마다 한 줄(같은 시도의 사례는 입력이 같다)."""
    frame = pd.read_csv(os.path.join(fleet_dir, "samples.csv"), float_precision="round_trip", usecols=["bot", "attempt", *names])
    return frame.drop_duplicates(["bot", "attempt"])[list(names)].to_numpy(dtype=float)


def out_of_distribution(human_x, fleet_x, names):
    """칸마다 — 함대의 1 · 99 백분위와 사람의 입력이 그 밖인 몫."""
    low = np.percentile(fleet_x, LOW, axis=0)
    high = np.percentile(fleet_x, HIGH, axis=0)
    rows = []
    for i, name in enumerate(names):
        column = human_x[:, i] if len(human_x) else np.array([])
        outside = (column < low[i]) | (column > high[i])
        rows.append({"feature": name, "low": float(low[i]), "high": float(high[i]),
                     "outside": float(outside.mean()) if len(column) else float("nan"),
                     "human_median": float(np.median(column)) if len(column) else float("nan")})
    return rows


def cases(human, net, heads):
    """사람의 사례 한 줄씩 — 시도 · 갈래 · 칸 · 라벨 · 그 시도의 그 칸 로짓(지금 망 · 게임과 같은 순서)."""
    rows = []
    for index, entry in enumerate(human.attempts):
        logits = golden.logits(net, entry["features"])
        for instance in entry["instances"]:
            slot = heads.index(instance["pattern_id"])
            rows.append({"attempt": index, "arm": entry.get("arm") or "", "slot": slot, "label": 1 if instance["hit"] else 0, "logit": logits[slot]})
    return pd.DataFrame(rows, columns=["attempt", "arm", "slot", "label", "logit"])


def calibration(frame, heads):
    """칸마다 — 사례 수 · 예측 평균 · 실제 · ECE(사례가 열 건 이상일 때만 — 구간이 비면 수가 거짓말을 한다). 두 갈래를 합쳐서와 갈래마다."""
    rows = []
    for h, head in enumerate(heads):
        row = {"head": head}
        for arm in ("all", validate.NETWORK, validate.UNIFORM):
            c = frame[(frame["slot"] == h) & ((frame["arm"] == arm) if arm != "all" else True)]
            p = evaluate.sigmoid(c["logit"].to_numpy(dtype=float))
            y = c["label"].to_numpy(dtype=float)
            row[arm] = {"n": len(c), "mean_p": float(p.mean()) if len(c) else float("nan"), "rate": float(y.mean()) if len(c) else float("nan"),
                        "ece": evaluate.ece(p, y) if len(c) >= evaluate.BINS else float("nan")}
        rows.append(row)
    return rows


def arms(frame, draws=1000, seed=0):
    """갈래 비교 — 맞는 비율(망 · 무작위)과 차(망 − 무작위)의 구간. 시도를 다시 뽑는다 — 한 시도의 사례들은 같이 뽑힌다."""
    net = (frame["arm"] == validate.NETWORK).to_numpy()
    uni = (frame["arm"] == validate.UNIFORM).to_numpy()
    hit = frame["label"].to_numpy() == 1
    table = pd.DataFrame({"attempt": frame["attempt"], "h_n": hit & net, "n_n": net, "h_u": hit & uni, "n_u": uni})
    table = table.groupby("attempt", sort=True)[["h_n", "n_n", "h_u", "n_u"]].sum().to_numpy(dtype=float)
    sums = table.sum(axis=0) if len(table) else np.zeros(4)
    diff = lambda s: validate.ratio(s[0], s[1]) - validate.ratio(s[2], s[3])
    return {"cases_network": int(sums[1]), "cases_uniform": int(sums[3]),
            "hit_network": validate.ratio(sums[0], sums[1]), "hit_uniform": validate.ratio(sums[2], sums[3]),
            "diff": validate.bootstrap(table, diff, draws, seed)}


def render(human, ood, calib, comparison, network_sha, sources):
    pct = validate.pct
    lines = [
        "# sim-to-real — 사람과 봇 함대", "",
        "`ml/sim2real.py` 가 쓴다(#114 · 설계 2026-09-28 §7.3).", "",
        f"- 기록: {', '.join(os.path.relpath(s, data.ROOT) for s in sources)}",
        f"- 2단계 시도 {len(human.attempts):,} (1단계 줄 {human.stage1:,} · 입력이나 사례가 없는 옛 줄 {human.skipped_old:,} 은 뺐다)",
        f"- 예측은 지금의 network.json(`{network_sha[:12]}…`)으로 셈했다 — 기록의 망: "
        + ", ".join(f"`{(s or '없음')[:12]}`" for s in sorted(human.networks, key=lambda v: v or "")), "",
        "## 분포 밖인가", "",
        f"사람의 입력이 함대의 {LOW} ~ {HIGH} 백분위 밖인 몫 — 높은 칸은 봇이 흉내 못 낸 사람이다.", "",
        "| 칸 | 함대 1% | 함대 99% | 사람의 가운데 값 | 밖인 몫 |", "|---|---|---|---|---|",
    ]
    for r in sorted(ood, key=lambda r: -np.nan_to_num(r["outside"])):
        lines.append(f"| {r['feature']} | {r['low']:.3f} | {r['high']:.3f} | {r['human_median']:.3f} | {pct(r['outside'])} |")
    lines += ["", "## 보정", "", "사례마다 그 시도를 시작할 때의 예측과 실제. ECE 는 사례가 열 건 이상인 칸만.", "",
              "| 칸 | 사례 | 예측 평균 | 실제 | ECE | 망 갈래 (사례 · 실제) | 무작위 갈래 (사례 · 실제) |", "|---|---|---|---|---|---|---|"]
    for r in calib:
        a, n, u = r["all"], r[validate.NETWORK], r[validate.UNIFORM]
        ece = "—" if np.isnan(a["ece"]) else f"{a['ece']:.4f}"
        lines.append(f"| {r['head']} | {a['n']:,} | {pct(a['mean_p'])} | {pct(a['rate'])} | {ece} | {n['n']:,} · {pct(n['rate'])} | {u['n']:,} · {pct(u['rate'])} |")
    d = comparison["diff"]
    lines += ["", "## 갈래 비교", "",
              f"맞는 비율 — 망 갈래 {pct(comparison['hit_network'])}(사례 {comparison['cases_network']:,}) · 무작위 갈래 {pct(comparison['hit_uniform'])}"
              f"(사례 {comparison['cases_uniform']:,}) · 차 {d[0]:+.3f} [{d[1]:+.3f}, {d[2]:+.3f}] (시도를 다시 뽑는 부트스트랩의 95%)."]
    return "\n".join(lines) + "\n"


def main(argv):
    p = argparse.ArgumentParser(description="sim-to-real — 사람의 기록을 봇 함대에 견준다 (#114)")
    p.add_argument("paths", nargs="*", help="기록(.jsonl) — 없으면 ml/human/*.jsonl")
    p.add_argument("--fleet", default=FLEET_DIR, help="함대의 원본(공장의 폴더) — 분포 밖을 가늠하는 백분위")
    p.add_argument("--network", default=os.path.join(data.DATA_DIR, "network.json"))
    p.add_argument("--out", default=os.path.join(HUMAN_DIR, "report.md"))
    p.add_argument("--draws", type=int, default=1000)
    args = p.parse_args(argv)
    sources = args.paths or sorted(glob.glob(os.path.join(HUMAN_DIR, "*.jsonl")))
    if not sources:
        print(f"[sim2real][E] 기록이 없다 — 게임의 user://attempts/*.jsonl 을 {os.path.relpath(HUMAN_DIR, data.ROOT)}/ 로 옮겨 오라")
        return 1
    with open(args.network, "rb") as f:
        raw = f.read()
    net = json.loads(raw)
    human = Human(read_lines(sources))
    if not human.attempts:
        print(f"[sim2real][E] 잴 2단계 시도가 없다 — 1단계 줄 {human.stage1} · 옛 줄 {human.skipped_old}")
        return 1
    names = net["features"]
    heads = net["heads"]
    human_x = np.array([a["features"] for a in human.attempts], dtype=float)
    ood = out_of_distribution(human_x, fleet_features(args.fleet, names), names)
    frame = cases(human, net, heads)
    report = render(human, ood, calibration(frame, heads), arms(frame, args.draws), hashlib.sha256(raw).hexdigest(), sources)
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        f.write(report)
    print(report)
    print(f"[sim2real][I] report={os.path.relpath(args.out, data.ROOT)} attempts={len(human.attempts)} cases={len(frame)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
