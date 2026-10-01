"""페이즈 고르기 (설계 2026-10-01 조각7 §1). 실행: tools/build.sh pick --name=sp-1

셀프 플레이의 eval.csv 에서 — 형태 1: r00 을 뺀 저장본 중 승률이 가장 낮은 것 · 형태 3: 마지막 저장본 · 형태 2: 1 과 3 의 승률 한가운데에 가장 가까운 것.
**평가에서 일곱 공격을 모두 쓴 저장본만 고른다**(이슈 #167 — 유저: "어떤 페이즈든 공격 자체는 전체 다 써야"): 공격마다 동작 시작의 min_share 이상.
sp-1 의 1페이즈(r02)는 점프 공격 · 잡기 둘에 몰려 있었다. 고른 가중치를 overfit/data/boss_net/form{1,2,3}.json 으로 복사하고 picks.json 에 출처를 남긴다.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import shutil
from pathlib import Path

import numpy as np

from ml.rl import rollout

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "overfit" / "data" / "boss_net"


def move_shares(action: np.ndarray, actions: int, roster: int) -> list[float]:
    """고른 칸들 → 동작(명부)마다 동작 시작 중의 몫. 칸 배치는 [움직임 · 계속하기 …, 동작 0 … 동작 N−1] 이라 끝의 roster 칸이 동작이다."""
    moves = action[action >= actions - roster] - (actions - roster)
    counts = np.bincount(moves, minlength=roster)
    total = counts.sum()
    return [float(c / total) if total else 0.0 for c in counts]


def choose(rows: list[dict], shares: dict[str, list[float]], min_share: float) -> list[dict]:
    """eval.csv 의 줄(저장본 순) → 형태 1 · 2 · 3 의 줄. 일곱 공격 중 하나라도 min_share 밑인 저장본은 빼고 고른다."""
    eligible = [r for r in rows if min(shares[r["boss"]]) >= min_share]
    trained = [r for r in eligible if r["boss"] != "boss_r00"]
    if len(trained) < 3:
        raise SystemExit(f"공격을 모두 쓰는 저장본이 {len(trained)} 개뿐이다 — 페이즈 셋을 못 고른다")
    first = min(trained, key=lambda r: (float(r["boss_win"]), r["boss"]))
    last = trained[-1]
    mid = (float(first["boss_win"]) + float(last["boss_win"])) / 2
    second = min((r for r in trained if r not in (first, last)), key=lambda r: (abs(float(r["boss_win"]) - mid), r["boss"]))
    return [first, second, last]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", required=True)
    args = ap.parse_args()
    src = ROOT / "out" / "selfplay" / args.name
    rows = list(csv.DictReader((src / "eval.csv").open(encoding="utf-8")))
    min_share = json.loads((ROOT / "ml" / "rl" / "train.json").read_text(encoding="utf-8"))["pick"]["min_move_share"]
    shares = {}
    for r in rows:
        ro = rollout.read(src / "eval" / r["boss"])
        shares[r["boss"]] = move_shares(ro.action, ro.manifest["actions"], len(ro.manifest["roster"]))
        print(f"{r['boss']} boss_win={r['boss_win']} moves=" + " ".join(f"{x:.2f}" for x in shares[r["boss"]]))
    picks = choose(rows, shares, min_share)
    DEST.mkdir(parents=True, exist_ok=True)
    record = {"_comment": "형태(페이즈)마다의 보스 망 — tools/build.sh pick 이 셀프 플레이의 eval.csv 에서 골랐다(설계 2026-10-01 조각7 §1). 손으로 고치지 말고 다시 고른다.",
              "selfplay": args.name, "forms": []}
    for form, row in enumerate(picks, start=1):
        dst = DEST / f"form{form}.json"
        shutil.copyfile(src / f"{row['boss']}.json", dst)
        record["forms"].append({"form": form, "from": row["boss"], "boss_win": float(row["boss_win"]), "return": float(row["return"]),
                                "move_shares": [round(x, 4) for x in shares[row["boss"]]],
                                "sha256": hashlib.sha256(dst.read_bytes()).hexdigest()})
        print(f"form{form} ← {row['boss']} boss_win={row['boss_win']}")
    (DEST / "picks.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
