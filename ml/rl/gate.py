"""비교 관문 — "딥러닝이어야 했다" (설계 2026-10-01 조각8 §2). 실행: tools/build.sh gate --name=NAME

같은 시험 묶음(봇 함대 + 셀프 플레이의 파이터 저장본 넷 · evalboss 와 같다) · 같은 시드에서 보스 일곱을 재고, 3페이즈가 규칙 · Q 표를 margin 넘게 이기는지
판정한다. 결과는 ml/rl/gate/<이름>.json 에 싣는다(커밋한다 — README 의 표가 이것이다).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

import numpy as np

from ml.rl import episodes, worker

ROOT = worker.ROOT
NETS = ROOT / "overfit" / "data" / "boss_net"


def test_pool(fighters: list[Path]) -> list[str]:
    """시험 묶음 — 봇 함대 + 파이터 저장본 넷(라운드의 1/4 · 2/4 · 3/4 · 끝). evalboss 와 관문이 같은 묶음을 쓴다."""
    fighters = sorted(fighters)
    if not fighters:
        return ["fleet"]
    picks = sorted({fighters[i] for i in np.linspace(len(fighters) // 4, len(fighters) - 1, num=4).round().astype(int)})
    return ["fleet"] + [str(p) for p in picks]


def judge(rows: dict[str, float], margin: float) -> tuple[bool, str, float]:
    """3페이즈의 승률이 규칙 · Q 표 중 센 쪽보다 margin 이상 높나 — (통과, 센 대조군, 차이)."""
    best = max(("rule", "qtable"), key=lambda k: rows[k])
    gap = rows["form3"] - rows[best]
    return gap >= margin - 1e-12, best, gap


def bosses(qtable: Path) -> list[tuple[str, str, str | None]]:
    """(이름, 일꾼의 조종기, 가중치) — 게임의 보스(game)는 형태마다의 망을 바꾸는 조종기다."""
    forms = [NETS / f"form{i}.json" for i in (1, 2, 3)]
    return [
        ("random", "random", None),
        ("rule", "rule", None),
        ("qtable", "qtable", str(qtable)),
        ("form1", "net", str(forms[0])),
        ("form2", "net", str(forms[1])),
        ("form3", "net", str(forms[2])),
        ("game", "forms", ",".join(str(f) for f in forms)),
    ]


def sha(paths: list[str]) -> str:
    h = hashlib.sha256()
    for p in paths:
        h.update(Path(p).read_bytes())
    return h.hexdigest()


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", required=True)
    ap.add_argument("--qtable", default=None, help="기본 out/qtable/<이름>/qtable.json")
    ap.add_argument("--config", default=str(ROOT / "ml" / "rl" / "train.json"))
    args = ap.parse_args()
    config = json.loads(Path(args.config).read_text(encoding="utf-8"))
    cfg, gcfg = config["ppo"], config["gate"]
    qtable = Path(args.qtable) if args.qtable else ROOT / "out" / "qtable" / args.name / "qtable.json"
    if not qtable.exists():
        sys.exit(f"Q 표가 없다 — {qtable} (tools/build.sh qtrain --name={args.name})")
    opponents = test_pool(list((ROOT / "out" / "selfplay" / args.name).glob("fighter_r*.json")))
    print("opponents=" + ",".join(Path(o).name for o in opponents), flush=True)
    worker.build()
    rows = []
    for name, controller, weights in bosses(qtable):
        out = ROOT / "out" / "gate" / args.name / name
        worker.run(cfg["compare_seed"], gcfg["episodes"], out, weights=weights, controller=controller, opponents=opponents)
        s = episodes.summary(out, config["reward"])
        rows.append({"boss": name, "boss_win": round(s["boss_win"], 4), "fighter_lost": round(s["fighter_lost"], 1),
                     "boss_lost": round(s["boss_lost"], 1), "seconds": round(s["ticks"] / 60.0, 1), "return": round(s["return"], 4),
                     "weights_sha256": sha(weights.split(",")) if weights else None})
        print(f"{name:7s} boss_win={s['boss_win']:.3f} fighter_lost={s['fighter_lost']:.1f} boss_lost={s['boss_lost']:.1f}", flush=True)
    ok, best, gap = judge({r["boss"]: r["boss_win"] for r in rows}, gcfg["margin"])
    result = {
        "_comment": "비교 관문의 결과 (설계 2026-10-01 조각8 §2) — tools/build.sh gate 가 쓴다. 손으로 고치지 않는다.",
        "name": args.name, "episodes": gcfg["episodes"], "seed": cfg["compare_seed"], "margin": gcfg["margin"],
        "opponents": [{"name": Path(o).name, "sha256": sha([o]) if o != "fleet" else None} for o in opponents],
        "rows": rows, "pass": ok, "against": best, "gap": round(gap, 4),
    }
    dest = ROOT / "ml" / "rl" / "gate" / f"{args.name}.json"
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{'통과' if ok else '실패'} — 3페이즈 {rows[5]['boss_win']:.3f} vs {best} {dict((r['boss'], r['boss_win']) for r in rows)[best]:.3f}"
          f" (차이 {gap:+.3f} · 기준 {gcfg['margin']:+.3f}) → {dest}")
    if not ok:
        sys.exit(1)


if __name__ == "__main__":
    main()
