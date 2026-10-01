"""페이즈 고르기 (설계 2026-10-01 조각7 §1). 실행: tools/build.sh pick --name=sp-1

셀프 플레이의 eval.csv 에서 — 형태 1: r00 을 뺀 저장본 중 승률이 가장 낮은 것("무작위까지는 아니고 거의 안 된") · 형태 3: 마지막 저장본("최종형태") ·
형태 2: 1 과 3 의 승률 한가운데에 가장 가까운 것. 고른 가중치를 overfit/data/boss_net/form{1,2,3}.json 으로 복사하고 picks.json 에 출처를 남긴다.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / "overfit" / "data" / "boss_net"


def choose(rows: list[dict]) -> list[dict]:
    """eval.csv 의 줄(저장본 순) → 형태 1 · 2 · 3 의 줄."""
    trained = [r for r in rows if r["boss"] != "boss_r00"]
    first = min(trained, key=lambda r: (float(r["boss_win"]), r["boss"]))
    last = rows[-1]
    mid = (float(first["boss_win"]) + float(last["boss_win"])) / 2
    second = min((r for r in trained if r not in (first, last)), key=lambda r: (abs(float(r["boss_win"]) - mid), r["boss"]))
    return [first, second, last]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", required=True)
    args = ap.parse_args()
    src = ROOT / "out" / "selfplay" / args.name
    rows = list(csv.DictReader((src / "eval.csv").open(encoding="utf-8")))
    picks = choose(rows)
    DEST.mkdir(parents=True, exist_ok=True)
    record = {"_comment": "형태(페이즈)마다의 보스 망 — tools/build.sh pick 이 셀프 플레이의 eval.csv 에서 골랐다(설계 2026-10-01 조각7 §1). 손으로 고치지 말고 다시 고른다.",
              "selfplay": args.name, "forms": []}
    for form, row in enumerate(picks, start=1):
        dst = DEST / f"form{form}.json"
        shutil.copyfile(src / f"{row['boss']}.json", dst)
        record["forms"].append({"form": form, "from": row["boss"], "boss_win": float(row["boss_win"]), "return": float(row["return"]),
                                "sha256": hashlib.sha256(dst.read_bytes()).hexdigest()})
        print(f"form{form} ← {row['boss']} boss_win={row['boss_win']}")
    (DEST / "picks.json").write_text(json.dumps(record, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
