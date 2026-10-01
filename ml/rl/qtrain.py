"""Q 표 보스의 학습을 관문의 시험 묶음으로 부른다 (설계 2026-10-01 조각8 §1.3). 실행: tools/build.sh qtrain --name=NAME

학습은 C#(공장 콘솔의 --qtrain)이다 — 칸을 자르는 함수가 한 언어에만 있어야 고르기와 배우기가 같은 칸을 본다. 여기는 묶음을 세고 부르기만 한다.
"""

from __future__ import annotations

import argparse
import subprocess
import sys

from ml.rl import gate, worker

ROOT = worker.ROOT


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", required=True)
    ap.add_argument("--iterations", type=int, default=None)
    args = ap.parse_args()
    opponents = gate.test_pool(list((ROOT / "out" / "selfplay" / args.name).glob("fighter_r*.json")))
    worker.build()
    cmd = ["dotnet", str(worker.DLL), "--qtrain", "--opponents=" + ",".join(opponents), f"--out={ROOT / 'out' / 'qtable' / args.name}",
           "--log-level=info"]
    if args.iterations:
        cmd.append(f"--iterations={args.iterations}")
    lines = []
    with subprocess.Popen(cmd, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True) as p:
        for line in p.stdout:
            print(line, end="", flush=True)
            lines.append(line)
    out = "".join(lines)
    if p.returncode != 0 or "qtrain=done" not in out or "][E]" in out:
        sys.exit(f"Q 표 학습이 멈췄다 (코드 {p.returncode})")


if __name__ == "__main__":
    main()
