"""network.json 과 report.md 를 쓴다 (#110 · 설계 2026-09-28 §5.6).

수는 파이썬 repr — 가장 짧은 왕복 표기라 C# 의 double.Parse 가 같은 double 을 얻는다. 가중치 w 는 행이 출력 · 열이 입력이고, 행 하나를 한 줄에
쓴다(사람이 훑을 수 있게). 게임은 이 파일을 필수 데이터로 읽는다(4번 PR) — 키가 빠지면 부팅이 빠진 키를 나열하고 멈춘다.
"""

import json
import math
import os

import numpy as np

import golden

ROOT = golden.ROOT
NETWORK = golden.NETWORK
REPORT = os.path.join(ROOT, "ml", "report.md")

# 넘파이(행렬곱 · BLAS 의 덧셈 순서)와 골든(입력 순서의 덧셈)의 로짓 차이의 상한 — 행렬을 뒤집어 쓴 것 같은 실수는 이보다 몇 자리 크다.
LOOP_ORDER_TOLERANCE = 1e-9


def _number(v):
    if isinstance(v, (bool, np.bool_)):
        return "true" if v else "false"
    if isinstance(v, (int, np.integer)):
        return str(int(v))
    f = float(v)
    if not math.isfinite(f):
        return "null"
    return repr(f)


def _is_number(v):
    return isinstance(v, (int, float, np.integer, np.floating)) and not isinstance(v, (bool, np.bool_))


def dumps(v, level=0):
    """들여 쓴 JSON — 수의 목록은 한 줄에, 그 밖은 한 층씩."""
    pad = " " * level
    if isinstance(v, dict):
        items = [f"{pad} {json.dumps(k, ensure_ascii=False)}: {dumps(x, level + 1)}" for k, x in v.items()]
        return "{\n" + ",\n".join(items) + "\n" + pad + "}"
    if isinstance(v, (list, tuple, np.ndarray)):
        v = list(v)
        if all(_is_number(x) for x in v):
            return "[" + ", ".join(_number(x) for x in v) + "]"
        return "[\n" + ",\n".join(pad + " " + dumps(x, level + 1) for x in v) + "\n" + pad + "]"
    if _is_number(v) or isinstance(v, (bool, np.bool_)):
        return _number(v)
    return json.dumps(v, ensure_ascii=False)


def network_doc(net, features, mean, std, heads, baseline, trained_on, metrics):
    return {
        "_comment": "패턴을 고르는 망 (#110 · 설계 2026-09-28 §5.6) — tools/build.sh train 이 공장의 원본으로 학습해 썼다. 손으로 고치지 않는다. "
        "로짓 = 표준화 (x - mean) / std → 층마다 acc = b[j] 에서 입력 순서대로 acc += w[j][i] * z[i] → 마지막 층을 빼고 ReLU. "
        "w 는 행이 출력 · 열이 입력. baseline 은 칸마다 학습 몫 기저율의 로짓 — 들어 올림 = 로짓 - baseline(설계 §6.2).",
        "version": 1,
        "features": list(features),
        "mean": [float(m) for m in mean],
        "std": [float(s) for s in std],
        "heads": list(heads),
        "baseline": [float(b) for b in baseline],
        "layers": [{"w": w.tolist(), "b": b.tolist()} for w, b in zip(net.w, net.b)],
        "trained_on": trained_on,
        "metrics": metrics,
    }


def write_network(doc, path=NETWORK):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(dumps(doc))
        f.write("\n")


def check_loop_order(path, x, numpy_logits):
    """쓴 파일을 다시 읽어 골든의 순서로 셈한 로짓이 넘파이의 로짓과 1e-9 안인지 — 행렬을 뒤집어 쓰거나 표준화를 빼먹은 실수를 잡는다."""
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
    worst = 0.0
    for row, expected in zip(x, numpy_logits):
        got = golden.logits(doc, [float(v) for v in row])
        worst = max(worst, max(abs(g - float(e)) for g, e in zip(got, expected)))
    if worst > LOOP_ORDER_TOLERANCE:
        raise AssertionError(f"넘파이와 입력 순서의 로짓이 {worst:.3g} 만큼 갈린다 — 상한 {LOOP_ORDER_TOLERANCE}")
    return worst


def write_report(heads_rows, targeting_rows, summary, path=REPORT):
    """ml/report.md — 사람이 읽는 학습 결과. network.json 의 metrics 와 같은 수다."""
    lines = [
        "# 망의 학습 결과",
        "",
        "`tools/build.sh train` 이 쓴다(#110 · 설계 2026-09-28 §5.5). 손으로 고치지 않는다.",
        "",
    ]
    lines += [f"- {k}: {v}" for k, v in summary.items()]
    lines += [
        "",
        "## 칸마다 (시험 몫)",
        "",
        "| 칸 | 사례 | 로그 손실 | 기저율의 로그 손실 | ECE (≤ 0.05) | AUC | 관문 |",
        "|---|---|---|---|---|---|---|",
    ]
    for r in heads_rows:
        mark = "통과" if r["beats_baseline"] and r["calibrated"] else "**못 넘음**"
        lines.append(f"| {r['head']} | {r['n']:,} | {r['log_loss']:.4f} | {r['baseline_log_loss']:.4f} | {r['ece']:.4f} | {r['auc']:.3f} | {mark} |")
    lines += [
        "",
        "## 겨냥 표 (시험 몫 · 시도마다의 들어 올림 평균 ≥ lift_min 0.405)",
        "",
        "| 습관형 → 패턴 | 봇 | 시도 | 들어 올림 평균 | 겨냥으로 선 시도 | 관문 |",
        "|---|---|---|---|---|---|",
    ]
    for r in targeting_rows:
        mark = "통과" if r["passes"] else "**못 넘음**"
        lines.append(f"| {r['habit']} → {r['pattern']} | {r['bots']:,} | {r['attempts']:,} | {r['lift']:+.3f} | {r['targeted_share']:.1%} | {mark} |")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
