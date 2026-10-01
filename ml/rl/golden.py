"""망의 골든 (설계 2026-10-01 조각7 §4). 실행: tools/build.sh golden

고른 세 망에 고정 관측 8 개를 넣어 **순수 파이썬**으로 로짓 · 가치를 셈한다 — 덧셈 순서가 C# PolicyNet 과 같다(출력마다 b 에서 시작해 입력 순서로 더한다 ·
파이썬 float 는 IEEE double · 곱셈과 덧셈을 따로). 결과를 16진 비트로 tests/Overfit.Rules.Tests/Battle/NetGolden.json 에 쓴다. 넘파이는 행렬곱의 덧셈 순서를
안 지키므로 쓰지 않는다.
"""

from __future__ import annotations

import hashlib
import json
import random
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
NETS = ROOT / "overfit" / "data" / "boss_net"
OUT = ROOT / "tests" / "Overfit.Rules.Tests" / "Battle" / "NetGolden.json"


def hexbits(x: float) -> str:
    return struct.pack(">d", x).hex()


def run(layers: list[dict], x: list[float]) -> list[float]:
    h = x
    for li, layer in enumerate(layers):
        out = []
        for row, b in zip(layer["w"], layer["b"]):
            s = b
            for w, v in zip(row, h):
                s = s + w * v
            out.append(max(0.0, s) if li < len(layers) - 1 else s)
        h = out
    return h


def main() -> None:
    rng = random.Random(7)
    doc = {"_comment": "tools/build.sh golden 이 지었다 — 손으로 고치지 않는다. 망을 다시 고르면 다시 짓는다.", "nets": []}
    for form in (1, 2, 3):
        path = NETS / f"form{form}.json"
        net = json.loads(path.read_text(encoding="utf-8"))
        cases = []
        for _ in range(8):
            x = [rng.uniform(-1.0, 1.0) for _ in range(net["obs"])]
            logits = run(net["policy"], x)
            value = run(net["value"], x)[0]
            cases.append({"obs": x, "logits": [hexbits(v) for v in logits], "value": hexbits(value)})
        doc["nets"].append({"file": f"boss_net/form{form}.json", "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "cases": cases})
    OUT.write_text(json.dumps(doc, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
