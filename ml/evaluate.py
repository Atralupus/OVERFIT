"""지표와 관문 (#110 · 설계 2026-09-28 §5.4 · §5.5).

| 무엇 | 선 |
|---|---|
| 시험 몫의 로그 손실 | 다섯 칸 모두 기저율보다 낮다 |
| 보정 | 칸마다 ECE ≤ 0.05 (같은 도수 10 구간) |
| 겨냥 | 겨냥 표의 줄마다, 그 습관형 봇(시험 몫)의 시도에서 그 패턴의 들어 올림 평균이 lift_min 이상 |
| AUC | 칸마다 적는다(선은 없다) |

로그 · 시그모이드는 여기(파이썬)에만 있다 — 게임의 결정 경로는 사칙연산과 비교뿐이다(설계 §8).
"""

import math

import numpy as np

# 설계 §6.4 의 picker.lift_min(ln 1.5 — 평균의 사람보다 오즈가 1.5 배). 4번 PR 이 balance.json 의 picker 로 옮긴다 — 그때 여기가 그 값을 읽는다.
LIFT_MIN = math.log(1.5)
ECE_MAX = 0.05
BINS = 10


def logit(p):
    return math.log(p / (1 - p))


def sigmoid(s):
    e = np.exp(-np.abs(s))
    return np.where(s >= 0, 1 / (1 + e), e / (1 + e))


def log_loss(p, y):
    p = np.clip(p, 1e-15, 1 - 1e-15)
    return float(np.mean(-(y * np.log(p) + (1 - y) * np.log(1 - p))))


def ece(p, y, bins=BINS):
    """같은 도수 구간의 보정 오차 — p 순으로 줄 세워 bins 덩이로 나누고, 덩이마다 |평균 p − 맞은 몫| 을 크기로 가중해 더한다."""
    order = np.argsort(p, kind="stable")
    total = 0.0
    for chunk in np.array_split(order, bins):
        if len(chunk):
            total += len(chunk) / len(p) * abs(float(np.mean(p[chunk])) - float(np.mean(y[chunk])))
    return total


def auc(p, y):
    """맨–휘트니 — 같은 점수는 평균 순위."""
    order = np.argsort(p, kind="stable")
    ranks = np.empty(len(p))
    sorted_p = p[order]
    i = 0
    while i < len(p):
        j = i
        while j + 1 < len(p) and sorted_p[j + 1] == sorted_p[i]:
            j += 1
        ranks[order[i:j + 1]] = (i + j) / 2 + 1
        i = j + 1
    positives = y == 1
    n1, n0 = int(positives.sum()), int((~positives).sum())
    if n1 == 0 or n0 == 0:
        return float("nan")
    return float((ranks[positives].sum() - n1 * (n1 + 1) / 2) / (n1 * n0))


def heads_report(logits, slot, y, heads, base_rates):
    """칸마다 — 사례 수 · 망의 로그 손실 · 기저율의 로그 손실 · ECE · AUC · 두 선을 넘었나."""
    rows = []
    for h, head in enumerate(heads):
        mask = slot == h
        p = sigmoid(logits[mask, h])
        t = y[mask]
        loss = log_loss(p, t)
        base = log_loss(np.full(len(t), base_rates[h]), t)
        e = ece(p, t)
        rows.append({
            "head": head,
            "n": int(mask.sum()),
            "log_loss": loss,
            "baseline_log_loss": base,
            "ece": e,
            "auc": auc(p, t),
            "beats_baseline": loss < base,
            "calibrated": e <= ECE_MAX,
        })
    return rows


def targeting_report(raw, rows_mask, logits_of, heads, baseline_logits, targeting):
    """겨냥 표의 줄마다 — 시험 몫의 그 습관형 봇(리듬 문턱 이상)의 **시도마다** 입력으로 로짓을 내어 그 패턴의 들어 올림을 평균한다.
    시도가 고르기가 서는 단위다(설계 §6.2 — 시도를 시작할 때 한 번)."""
    samples = raw.samples[rows_mask]
    attempts = samples.drop_duplicates(subset=["bot", "attempt"])
    bots = raw.bots.set_index("bot")
    out = []
    for row in targeting:
        head = heads.index(row["pattern"])
        min_rhythm = row.get("min_rhythm", 0)
        chosen = bots[(bots["habit"] == row["habit"]) & (bots["rhythm"] >= min_rhythm)].index
        mine = attempts[attempts["bot"].isin(chosen)]
        x = mine[raw.features].to_numpy(dtype=float)
        lifts = logits_of(x)[:, head] - baseline_logits[head] if len(x) else np.array([])
        lift = float(np.mean(lifts)) if len(lifts) else float("nan")
        out.append({
            "habit": row["habit"],
            "pattern": row["pattern"],
            "min_rhythm": min_rhythm,
            "bots": int(mine["bot"].nunique()),
            "attempts": int(len(mine)),
            "lift": lift,
            "targeted_share": float(np.mean(lifts >= LIFT_MIN)) if len(lifts) else float("nan"),
            "passes": bool(len(lifts)) and lift >= LIFT_MIN,
        })
    return out


def passes(heads_rows, targeting_rows):
    return all(r["beats_baseline"] and r["calibrated"] for r in heads_rows) and all(r["passes"] for r in targeting_rows)
