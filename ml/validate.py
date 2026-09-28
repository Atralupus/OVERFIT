"""§7.1 의 다섯 줄 — 같은 봇에게 망 보스와 무작위 보스 (#114 · 설계 2026-09-28 §7.1). tools/build.sh validate 가 부른다.

    ml/.venv/bin/python ml/validate.py [--data=out/evaluate/<시드>-<from>-<to>] [--draws=1000] [--seed=0]

평가 모드(tools/build.sh evaluate)가 지은 폴더를 읽어 다섯 줄을 재고 ml/validation.md 를 쓴다. 선을 못 넘은 줄이 있으면 종료 코드 1 이다 —
문턱(lift_min · max_targeted)을 조용히 고치지 않는다. 멈추고 까닭을 적는다(설계 §7.1).

| 무엇 | 선 |
|---|---|
| 의존 봉인 | 겨냥 표의 줄마다, 그 습관형 봇에게 (가) 그 패턴이 뽑히는 몫이 무작위의 1.5배 이상 · (나) 2단계 사례의 맞는 비율이 무작위보다 높다 — 구간이 0 을 안 넘는다 |
| 숨통 | 좁힌 시도는 전부 숨통 칸을 싣는다 · 숨통 칸의 맞는 비율이 겨냥 칸보다 낮다 — 구간이 0 을 안 넘는다 |
| 약점 저격이 아니다 | 고르게 약한 봇(혼합형 · 반응 0.30초 이상 · 흔들림 0.08초 이상)의 망 갈래 시도 중 80% 이상이 다섯 전부 |
| 죽음의 나선이 아니다 | 망 갈래의 2단계 승률이 무작위 갈래의 절반 이상 |
| 보정 | 뽑힌 사례(두 갈래)에서 칸마다 ECE ≤ 0.05 |

구간은 봇을 다시 뽑는 부트스트랩의 95%(2.5 · 97.5 백분위)다. 봇 하나의 두 갈래가 같이 뽑힌다 — 짝지은 비교라 봇마다의 차이가 지워진다.
선 · 구간을 어느 쪽에 거는지는 줄마다 GATES 에 적었다(점 추정인 선 셋 · 구간인 선 둘).
"""

import argparse
import hashlib
import json
import os
import sys

import numpy as np
import pandas as pd

import data
import evaluate

NETWORK = "network"
UNIFORM = "uniform"
ARMS = (NETWORK, UNIFORM)

# §7.1 의 선. 여기 말고 어디에도 안 둔다 — 보고서와 종료 코드가 이 수를 읽는다.
SHARE_RATIO_MIN = 1.5
FULL_SHARE_MIN = 0.80
WIN_RATIO_MIN = 0.5
ECE_MAX = evaluate.ECE_MAX

# 고르게 약한 봇 — 습관이 없고(혼합형) 느리고 흔들린다(§7.1).
WEAK_REACTION = 0.30
WEAK_JITTER = 0.08

DEFAULT_BOTS = 50_000  # tools/factory/EvaluateMode.cs 의 기본 수와 같다


class Evaluation:
    """평가 폴더 하나 — 매니페스트 · 세 CSV. 사례에 봇의 성향과 시도의 결정 · 로짓을 붙여 둔다."""

    def __init__(self, path, heads):
        self.path = path
        with open(os.path.join(path, "manifest.json"), encoding="utf-8") as f:
            self.manifest = json.load(f)
        self.heads = list(heads)
        # 열쇠의 형을 못박는다 — 빈 표(사례 없는 무리)는 형을 모르고 읽혀 잇기(merge)가 멈춘다. 빈칸만 NaN 이다(무작위 갈래의 결정 칸).
        read = lambda name, dtype: pd.read_csv(os.path.join(path, name), float_precision="round_trip", keep_default_na=False, na_values=[""],
                                               dtype=dtype)
        self.bots = read("bots.csv", {"bot": "int64", "habit": str, "reached_stage2": "int64", "network_won": "int64", "uniform_won": "int64"})
        self.attempts = read("attempts.csv", {"bot": "int64", "arm": str, "attempt": "int64", "mode": str, "reason": str, "narrowed": str,
                                              "breathing": "float64"})
        self.samples = read("samples.csv", {"bot": "int64", "arm": str, "attempt": "int64", "slot": "int64", "label": "int64"})
        self.bots["weak"] = weak(self.bots)
        self.attempts["narrowed_set"] = [parse_slots(v) for v in self.attempts["narrowed"]]
        keys = ["bot", "arm", "attempt"]
        logits = [f"logit_{h}" for h in range(len(self.heads))]
        joined = self.samples.merge(self.attempts[keys + ["mode", "breathing", "narrowed_set"] + logits], on=keys, how="left", validate="many_to_one")
        joined = joined.merge(self.bots[["bot", "habit", "rhythm", "weak"]], on="bot", how="left", validate="many_to_one")
        slot = joined["slot"].to_numpy()
        joined["logit"] = joined[logits].to_numpy()[np.arange(len(joined)), slot]
        self.cases = joined


def weak(bots):
    """고르게 약한 봇 — §7.1 의 정의."""
    return (bots["habit"] == "mixed") & (bots["reaction"] >= WEAK_REACTION) & (bots["jitter"] >= WEAK_JITTER)


def parse_slots(text):
    """좁힌 칸 — 공백으로 이은 칸 번호(EvalCsv). 무작위 갈래는 빈칸(NaN)이다."""
    if isinstance(text, str) and text.strip():
        return tuple(int(t) for t in text.split())
    return ()


def bootstrap(per_bot, stat, draws=1000, seed=0):
    """봇을 다시 뽑는 부트스트랩 — per_bot 은 (봇 × 칸) 의 합 표, stat 은 칸마다의 합 → 수. (점 추정, 2.5 백분위, 97.5 백분위).

    점 추정은 다시 뽑지 않은 합의 stat 이다. 다시 뽑은 합에서 stat 이 셈할 수 없으면(분모 0 — 작은 무리) 그 뽑기를 버린다.
    """
    per_bot = np.asarray(per_bot, dtype=float)
    point = stat(per_bot.sum(axis=0))
    if len(per_bot) == 0:
        return point, float("nan"), float("nan")
    rng = np.random.default_rng(seed)
    values = []
    for _ in range(draws):
        pick = rng.integers(0, len(per_bot), len(per_bot))
        with np.errstate(divide="ignore", invalid="ignore"):
            v = stat(per_bot[pick].sum(axis=0))
        if np.isfinite(v):
            values.append(v)
    if not values:
        return point, float("nan"), float("nan")
    lo, hi = np.percentile(values, [2.5, 97.5])
    return point, float(lo), float(hi)


def per_bot(frame, columns):
    """봇마다 칸의 합 — 봇 번호 순서. columns 는 {이름: 행마다의 0/1 · 수} 이다."""
    table = pd.DataFrame({"bot": frame["bot"].to_numpy(), **{k: np.asarray(v, dtype=float) for k, v in columns.items()}})
    return table.groupby("bot", sort=True)[list(columns)].sum().to_numpy()


def ratio(a, b):
    return a / b if b else float("nan")


def sealing(ev, habit, pattern, min_rhythm=0.0, draws=1000, seed=0):
    """의존 봉인 한 줄 — 그 습관형 봇의 2단계 사례에서 (가) 그 패턴이 뽑힌 몫의 비(망 / 무작위) · (나) 맞는 비율의 차(망 − 무작위)."""
    slot = ev.heads.index(pattern)
    c = ev.cases[(ev.cases["habit"] == habit) & (ev.cases["rhythm"] >= min_rhythm)]
    net, uni = (c["arm"] == NETWORK).to_numpy(), (c["arm"] == UNIFORM).to_numpy()
    picked, hit = (c["slot"] == slot).to_numpy(), c["label"].to_numpy() == 1
    table = per_bot(c, {"p_n": picked & net, "n_n": net, "p_u": picked & uni, "n_u": uni, "h_n": hit & net, "h_u": hit & uni})
    share = lambda s: ratio(ratio(s[0], s[1]), ratio(s[2], s[3]))
    diff = lambda s: ratio(s[4], s[1]) - ratio(s[5], s[3])
    sums = table.sum(axis=0)
    return {
        "habit": habit, "pattern": pattern, "bots": len(table),
        "share_network": ratio(sums[0], sums[1]), "share_uniform": ratio(sums[2], sums[3]),
        "share_ratio": bootstrap(table, share, draws, seed),
        "hit_network": ratio(sums[4], sums[1]), "hit_uniform": ratio(sums[5], sums[3]),
        "hit_diff": bootstrap(table, diff, draws, seed),
    }


def sealing_passes(row):
    """(가) 점 추정이 선 이상 · (나) 구간의 아래 끝이 0 보다 크다."""
    return row["share_ratio"][0] >= SHARE_RATIO_MIN and row["hit_diff"][1] > 0


def breathing(ev, draws=1000, seed=0):
    """숨통 — 좁힌 시도가 숨통 칸을 싣나(구조) · 좁힌 시도의 사례에서 숨통 칸과 겨냥 칸의 맞는 비율."""
    a = ev.attempts[(ev.attempts["arm"] == NETWORK) & (ev.attempts["mode"] == "narrowed")]
    carried = all(int(b) in s for b, s in zip(a["breathing"], a["narrowed_set"]))
    c = ev.cases[(ev.cases["arm"] == NETWORK) & (ev.cases["mode"] == "narrowed")]
    inside = all(int(s) in n for s, n in zip(c["slot"], c["narrowed_set"]))
    is_b = (c["slot"] == c["breathing"]).to_numpy()
    hit = c["label"].to_numpy() == 1
    table = per_bot(c, {"h_b": hit & is_b, "n_b": is_b, "h_t": hit & ~is_b, "n_t": ~is_b})
    sums = table.sum(axis=0)
    diff = lambda s: ratio(s[0], s[1]) - ratio(s[2], s[3])
    return {
        "attempts": len(a), "carried": carried, "inside": inside, "cases": len(c),
        "hit_breathing": ratio(sums[0], sums[1]), "hit_targeted": ratio(sums[2], sums[3]),
        "diff": bootstrap(table, diff, draws, seed),
    }


def breathing_passes(row):
    """구조(숨통을 싣고 좁힌 칸 밖을 안 낸다) · 차의 구간 위 끝이 0 보다 작다."""
    return row["carried"] and row["inside"] and row["diff"][2] < 0


def weakness(ev, draws=1000, seed=0):
    """약점 저격이 아니다 — 고르게 약한 봇의 망 갈래 시도 중 다섯 전부(thin · no_habit)로 선 몫."""
    a = ev.attempts[ev.attempts["arm"] == NETWORK].merge(ev.bots[["bot", "weak"]], on="bot", how="left")
    a = a[a["weak"]]
    full = (a["mode"] == "full").to_numpy()
    thin, no_habit = (a["reason"] == "thin").to_numpy(), (a["reason"] == "no_habit").to_numpy()
    table = per_bot(a, {"full": full, "n": np.ones(len(a)), "thin": thin, "no_habit": no_habit})
    sums = table.sum(axis=0)
    return {
        "bots": len(table), "attempts": len(a),
        "full": bootstrap(table, lambda s: ratio(s[0], s[1]), draws, seed),
        "thin": ratio(sums[2], sums[1]), "no_habit": ratio(sums[3], sums[1]),
    }


def weakness_passes(row):
    return row["full"][0] >= FULL_SHARE_MIN


def spiral(ev, group=None, draws=1000, seed=0):
    """죽음의 나선이 아니다 — 2단계에 간 봇의 갈래마다 승률과 그 비(망 / 무작위)."""
    b = ev.bots[ev.bots["reached_stage2"] == 1]
    if group is not None:
        b = b[group(b)]
    table = per_bot(b, {"w_n": b["network_won"], "w_u": b["uniform_won"], "n": np.ones(len(b))})
    sums = table.sum(axis=0)
    return {
        "bots": len(b), "win_network": ratio(sums[0], sums[2]), "win_uniform": ratio(sums[1], sums[2]),
        "ratio": bootstrap(table, lambda s: ratio(s[0], s[1]), draws, seed),
    }


def spiral_passes(row):
    return row["ratio"][0] >= WIN_RATIO_MIN


def calibration(ev):
    """보정 — 뽑힌 사례마다 그 시도의 그 칸 예측(시그모이드(로짓))과 라벨. 칸마다 · 두 갈래를 합쳐서와 갈래마다."""
    rows = []
    for h, head in enumerate(ev.heads):
        row = {"head": head}
        for name, mask in (("all", np.ones(len(ev.cases), dtype=bool)), (NETWORK, (ev.cases["arm"] == NETWORK).to_numpy()),
                           (UNIFORM, (ev.cases["arm"] == UNIFORM).to_numpy())):
            c = ev.cases[mask & (ev.cases["slot"] == h).to_numpy()]
            p = evaluate.sigmoid(c["logit"].to_numpy())
            y = c["label"].to_numpy(dtype=float)
            row[name] = {"n": len(c), "mean_p": float(p.mean()) if len(c) else float("nan"),
                         "rate": float(y.mean()) if len(c) else float("nan"), "ece": evaluate.ece(p, y) if len(c) else float("nan")}
        rows.append(row)
    return rows


def calibration_passes(rows):
    return all(r["all"]["ece"] <= ECE_MAX for r in rows)


GROUPS = (
    ("대시", lambda c: c["habit"] == "dash"),
    ("가드", lambda c: c["habit"] == "guard"),
    ("패리", lambda c: c["habit"] == "parry"),
    ("점프", lambda c: c["habit"] == "jump"),
    ("간격", lambda c: c["habit"] == "spacing"),
    ("혼합형", lambda c: c["habit"] == "mixed"),
    ("고르게 약한", lambda c: c["weak"]),
)


def shares(ev):
    """무리마다 · 갈래마다 뽑힌 칸의 몫 — README 의 "망이 이 사람에게 이 패턴을 고르는 비율"(§7.2)."""
    out = []
    for name, pick in GROUPS:
        c = ev.cases[pick(ev.cases)]
        row = {"group": name, "bots": int(c["bot"].nunique())}
        for arm in ARMS:
            a = c[c["arm"] == arm]
            counts = np.bincount(a["slot"].to_numpy(), minlength=len(ev.heads)).astype(float)
            row[arm] = counts / counts.sum() if counts.sum() else counts
        out.append(row)
    return out


def pct(x):
    """퍼센트 한 자리 — 셈할 수 없는 몫(분모 0)은 줄표다."""
    return "—" if x != x else f"{100 * x:.1f}%"


def interval(t, fmt):
    point, lo, hi = t
    return f"{fmt(point)} [{fmt(lo)}, {fmt(hi)}]"


def mark(ok):
    return "통과" if ok else "**못 넘음**"


def render(ev, result):
    """ml/validation.md — 손으로 고치지 않는다."""
    m = ev.manifest
    sign = lambda x: f"{x:+.3f}"
    lines = [
        "# 망은 정말 일하나 — §7.1 의 다섯 줄", "",
        "`tools/build.sh validate` 가 쓴다(#114 · 설계 2026-09-28 §7.1). 손으로 고치지 않는다.", "",
        f"- 평가: `{os.path.relpath(ev.path, data.ROOT)}` · 함대 시드 {m['fleet_seed']} · 봇 {m['bot_from']:,} ~ {m['bot_to'] - 1:,}"
        f"(학습에 안 쓴 봇 — 망은 봇 0 ~ {m['trained_on']['bots'] - 1:,} 을 배웠다) · 2단계에 간 봇 {m['reached_stage2']:,}",
        f"- 평가의 커밋: `{m['commit']}` · network.json `{m['network_sha256'][:12]}…` (trained_on `{m['trained_on']['commit'][:12]}…`)",
        f"- 고르기: min_samples {m['picker']['min_samples']} · lift_min {m['picker']['lift_min']} · max_targeted {m['picker']['max_targeted']}",
        f"- 사례: 망 갈래 {m['arms'][NETWORK]['samples']:,} · 무작위 갈래 {m['arms'][UNIFORM]['samples']:,} · 구간은 봇을 다시 뽑는 부트스트랩 "
        f"{result['draws']:,}번의 95%", "",
        "## 판정", "",
        "| 무엇 | 선 | 잰 값 | 판정 |", "|---|---|---|---|",
    ]
    for row in result["sealing"]:
        lines.append(f"| 의존 봉인 — {row['habit']} → {row['pattern']} | 몫의 비 ≥ {SHARE_RATIO_MIN} · 맞는 비율의 차 > 0(구간) | "
                     f"{interval(row['share_ratio'], lambda x: f'{x:.2f}')} · {interval(row['hit_diff'], sign)} | {mark(sealing_passes(row))} |")
    b = result["breathing"]
    lines.append(f"| 숨통 | 전부 싣는다 · 숨통 − 겨냥 < 0(구간) | {'싣는다' if b['carried'] and b['inside'] else '**안 싣는다**'} · "
                 f"{interval(b['diff'], sign)} | {mark(breathing_passes(b))} |")
    w = result["weakness"]
    lines.append(f"| 약점 저격이 아니다 | 다섯 전부 ≥ {pct(FULL_SHARE_MIN)} | {interval(w['full'], pct)} | {mark(weakness_passes(w))} |")
    s = result["spiral"]
    lines.append(f"| 죽음의 나선이 아니다 | 승률의 비 ≥ {WIN_RATIO_MIN} | {interval(s['ratio'], lambda x: f'{x:.2f}')} | {mark(spiral_passes(s))} |")
    worst = max(r["all"]["ece"] for r in result["calibration"])
    lines.append(f"| 보정 | 칸마다 ECE ≤ {ECE_MAX} | 가장 큰 칸 {worst:.4f} | {mark(calibration_passes(result['calibration']))} |")

    lines += ["", "## 의존 봉인", "",
              "그 습관형 봇의 2단계 사례 — 망 갈래와 무작위 갈래에서 그 패턴이 뽑힌 몫과 맞는 비율(사례 전부).", "",
              "| 습관 → 패턴 | 봇 | 뽑힌 몫 (망 · 무작위) | 몫의 비 | 맞는 비율 (망 · 무작위) | 차 |", "|---|---|---|---|---|---|"]
    for row in result["sealing"]:
        lines.append(f"| {row['habit']} → {row['pattern']} | {row['bots']:,} | {pct(row['share_network'])} · {pct(row['share_uniform'])} | "
                     f"{interval(row['share_ratio'], lambda x: f'{x:.2f}')} | {pct(row['hit_network'])} · {pct(row['hit_uniform'])} | "
                     f"{interval(row['hit_diff'], sign)} |")

    lines += ["", "## 숨통", "",
              f"망 갈래의 좁힌 시도 {b['attempts']:,} — 숨통 칸을 {'전부 싣는다' if b['carried'] else '**안 싣는 시도가 있다**'} · 사례가 좁힌 칸 "
              f"{'안에만 있다' if b['inside'] else '**밖에도 있다**'}. 좁힌 시도의 사례 {b['cases']:,} 에서 맞는 비율 — 숨통 칸 {pct(b['hit_breathing'])} · "
              f"겨냥 칸 {pct(b['hit_targeted'])} · 차 {interval(b['diff'], sign)}."]

    lines += ["", "## 약점 저격이 아니다", "",
              f"고르게 약한 봇(혼합형 · 반응 {WEAK_REACTION:.2f}초 이상 · 흔들림 {WEAK_JITTER:.2f}초 이상) {w['bots']:,} 대의 망 갈래 시도 {w['attempts']:,} — "
              f"다섯 전부 {interval(w['full'], pct)} (근거가 얇다 {pct(w['thin'])} · 도드라진 칸이 없다 {pct(w['no_habit'])})."]

    lines += ["", "## 죽음의 나선이 아니다", "", "2단계에 간 봇의 갈래마다 2단계 승률(시도 상한 안에 이겼나).", "",
              "| 무리 | 봇 | 망 갈래 | 무작위 갈래 | 비 |", "|---|---|---|---|---|"]
    for name, row in (("전부", s), ("고르게 약한", result["spiral_weak"])):
        lines.append(f"| {name} | {row['bots']:,} | {pct(row['win_network'])} | {pct(row['win_uniform'])} | "
                     f"{interval(row['ratio'], lambda x: f'{x:.2f}')} |")

    lines += ["", "## 보정", "", "뽑힌 사례마다 그 시도를 시작할 때의 예측(그 칸의 로짓 → 확률)과 실제. ECE 는 같은 도수 10 구간.", "",
              "| 칸 | 사례 | 예측 평균 | 실제 | ECE (두 갈래) | ECE (망) | ECE (무작위) |", "|---|---|---|---|---|---|---|"]
    for r in result["calibration"]:
        a = r["all"]
        lines.append(f"| {r['head']} | {a['n']:,} | {pct(a['mean_p'])} | {pct(a['rate'])} | {a['ece']:.4f} | "
                     f"{r[NETWORK]['ece']:.4f} | {r[UNIFORM]['ece']:.4f} |")

    lines += ["", "## 망이 이 사람에게 이 패턴을 고르는 비율", "",
              "무리마다 2단계에서 뽑힌 패턴의 몫 — 망 갈래 (무작위 갈래). 무작위는 칸마다 20% 안팎이다.", "",
              "| 무리 | 봇 | " + " | ".join(ev.heads) + " |", "|---|---|" + "---|" * len(ev.heads)]
    for row in result["shares"]:
        cells = " | ".join(f"{pct(row[NETWORK][h])} ({pct(row[UNIFORM][h])})" for h in range(len(ev.heads)))
        lines.append(f"| {row['group']} | {row['bots']:,} | {cells} |")
    return "\n".join(lines) + "\n"


def sha256(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def default_data():
    """평가 모드의 기본 폴더 — 망이 배운 봇 다음부터 DEFAULT_BOTS 대(EvaluateMode 와 같은 셈)."""
    with open(os.path.join(data.DATA_DIR, "network.json"), encoding="utf-8") as f:
        t = json.load(f)["trained_on"]
    first = t["bots"]
    return os.path.join(data.ROOT, "out", "evaluate", f"{t['fleet_seed']}-{first}-{first + DEFAULT_BOTS}")


def run(ev, draws=1000, seed=0):
    """다섯 줄을 잰다 — 판정은 *_passes 가 한다."""
    result = {"draws": draws}
    result["sealing"] = [sealing(ev, r["habit"], r["pattern"], r.get("min_rhythm", 0.0), draws, seed) for r in data.fleet_targeting()]
    result["breathing"] = breathing(ev, draws, seed)
    result["weakness"] = weakness(ev, draws, seed)
    result["spiral"] = spiral(ev, None, draws, seed)
    result["spiral_weak"] = spiral(ev, weak, draws, seed)
    result["calibration"] = calibration(ev)
    result["shares"] = shares(ev)
    result["passed"] = (all(sealing_passes(r) for r in result["sealing"]) and breathing_passes(result["breathing"])
                        and weakness_passes(result["weakness"]) and spiral_passes(result["spiral"])
                        and calibration_passes(result["calibration"]))
    return result


def main(argv):
    p = argparse.ArgumentParser(description="§7.1 의 다섯 줄 — 같은 봇에게 망 보스와 무작위 보스 (#114)")
    p.add_argument("--data", default=None, help="평가 폴더 (기본: 망이 배운 봇 다음부터 5만 대)")
    p.add_argument("--draws", type=int, default=1000, help="부트스트랩 횟수")
    p.add_argument("--seed", type=int, default=0)
    p.add_argument("--out", default=os.path.join(data.ROOT, "ml", "validation.md"))
    args = p.parse_args(argv)
    path = args.data or default_data()
    if not os.path.exists(os.path.join(path, "manifest.json")):
        print(f"[validate][E] 평가 폴더가 없다 — {path}. tools/build.sh evaluate 를 먼저 돌려라")
        return 1
    ev = Evaluation(path, data.stage2_heads())
    now = sha256(os.path.join(data.DATA_DIR, "network.json"))
    if ev.manifest["network_sha256"] != now:
        print(f"[validate][E] 평가가 다른 망으로 지어졌다 — 평가 {ev.manifest['network_sha256'][:12]} · 지금 {now[:12]}. tools/build.sh evaluate 를 다시 돌려라")
        return 1
    result = run(ev, args.draws, args.seed)
    with open(args.out, "w", encoding="utf-8") as f:
        f.write(render(ev, result))
    print(f"[validate][I] report={os.path.relpath(args.out, data.ROOT)} passed={result['passed']}")
    for row in result["sealing"]:
        print(f"[validate][I] sealing {row['habit']}->{row['pattern']} share_ratio={row['share_ratio'][0]:.2f} "
              f"hit_diff={row['hit_diff'][0]:+.3f} [{row['hit_diff'][1]:+.3f}, {row['hit_diff'][2]:+.3f}] pass={sealing_passes(row)}")
    b, w, s = result["breathing"], result["weakness"], result["spiral"]
    print(f"[validate][I] breathing carried={b['carried']} inside={b['inside']} diff={b['diff'][0]:+.3f} [{b['diff'][1]:+.3f}, {b['diff'][2]:+.3f}] "
          f"pass={breathing_passes(b)}")
    print(f"[validate][I] weakness full={w['full'][0]:.3f} [{w['full'][1]:.3f}, {w['full'][2]:.3f}] pass={weakness_passes(w)}")
    print(f"[validate][I] spiral ratio={s['ratio'][0]:.2f} win_network={s['win_network']:.3f} win_uniform={s['win_uniform']:.3f} pass={spiral_passes(s)}")
    for r in result["calibration"]:
        print(f"[validate][I] calibration head={r['head']} n={r['all']['n']} ece={r['all']['ece']:.4f}")
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
