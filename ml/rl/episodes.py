"""episodes.csv 를 읽어 판마다의 보상 합을 셈한다 — 결정을 안 적는 조종기(규칙 · 무작위)도 같은 잣대로 견준다(설계 2026-10-01 조각5 §3)."""

from __future__ import annotations

import csv
import json
from pathlib import Path

import numpy as np


def summary(out_dir: str | Path, reward: dict) -> dict:
    out = Path(out_dir)
    manifest = json.loads((out / "manifest.json").read_text(encoding="utf-8"))
    rows = list(csv.DictReader((out / "episodes.csv").open(encoding="utf-8")))
    fighter_lost = np.array([float(r["fighter_lost"]) for r in rows])
    boss_lost = np.array([float(r["boss_lost"]) for r in rows])
    ticks = np.array([float(r["ticks"]) for r in rows])
    won = np.array([r["boss_won"] == "1" for r in rows])
    ret = (reward["w_dealt"] * fighter_lost / manifest["fighter_max_health"]
           - reward["w_taken"] * boss_lost / manifest["boss_max_health"]
           - reward["w_time"] * ticks / 60.0
           + np.where(won, reward["w_win"], -reward["w_win"]))
    return {
        "episodes": len(rows),
        "boss_win": float(won.mean()),
        "return": float(ret.mean()),
        "return_se": float(ret.std(ddof=1) / np.sqrt(len(rows))) if len(rows) > 1 else 0.0,
        "ticks": float(ticks.mean()),
        "boss_lost": float(boss_lost.mean()),
        "fighter_lost": float(fighter_lost.mean()),
    }
