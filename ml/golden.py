#!/usr/bin/env python3
"""망의 골든 — network.json 의 로짓을 C# 과 **같은 연산 순서**로 셈한다 (#110 · 설계 2026-09-28 §5.7). 표준 라이브러리만 쓴다.

    python3 ml/golden.py            고정 입력 스무 개의 로짓을 셈해 tests/Overfit.Rules.Tests/Battle/NetworkGolden.json 에 쓴다
    python3 ml/golden.py --verify   지금의 network.json 으로 다시 셈해 그 파일과 == 로 견준다 — 다르면 1, 망이 아직 없으면 77

연산 순서가 계약이다(C# 의 PlayerNet · 4번 PR 이 같은 파일을 == 로 본다):

  1. 표준화 z[i] = (x[i] - mean[i]) / std[i]
  2. 층마다 출력 j 에 대해 acc = b[j] 에서 시작해 입력 순서대로 acc += w[j][i] * z[i]
  3. 마지막 층을 빼고 ReLU = acc if acc > 0 else 0.0

넘파이의 dot 을 안 쓰는 이유: BLAS 가 더하는 순서를 바꾸고 FMA 를 쓸 수 있다. 파이썬의 float 는 IEEE 754 double 이고 + − × ÷ 는 올바르게 반올림된
결과가 하나뿐이다 — .NET 도 부동소수를 스스로 합치지 않으므로(FMA 없음) 같은 순서면 비트까지 같다. ReLU 를 max 로 안 쓰는 이유: −0 과 NaN 에서
파이썬의 max 와 C# 의 Math.Max 가 다른 값을 낸다.

check 가 --verify 를 매 커밋 돈다 — 파이썬 == 골든 파일 == C# 이 늘 선다. 넘파이가 없어도 돈다.
"""

import hashlib
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NETWORK = os.path.join(ROOT, "overfit", "data", "network.json")
GOLDEN = os.path.join(ROOT, "tests", "Overfit.Rules.Tests", "Battle", "NetworkGolden.json")
CASES = 20


def logits(net, x):
    """입력 한 줄(19) → 로짓(5). 위의 순서 그대로다."""
    z = [(x[i] - net["mean"][i]) / net["std"][i] for i in range(len(x))]
    layers = net["layers"]
    for k, layer in enumerate(layers):
        hidden = k < len(layers) - 1
        out = []
        for j, row in enumerate(layer["w"]):
            acc = layer["b"][j]
            for i in range(len(z)):
                acc += row[i] * z[i]
            out.append((acc if acc > 0 else 0.0) if hidden else acc)
        z = out
    return z


def inputs(net):
    """고정 입력 스무 개 — 0(첫 시도의 입력) · 평균 · 평균 ± 편차의 결정적인 조합. 망이 실제로 받는 범위를 덮는다."""
    mean, std = net["mean"], net["std"]
    cases = [[0.0] * len(mean), list(mean)]
    for k in range(CASES - 2):
        cases.append([mean[i] + std[i] * (((k * 7 + i * 3) % 9) - 4) / 2 for i in range(len(mean))])
    return cases


def check_shape(net):
    """모양 — 층의 크기가 이어지고 편차가 0 보다 크다. C# 은 어긋나면 [E] 다(설계 §5.6)."""
    n = len(net["features"])
    problems = []
    if len(net["mean"]) != n or len(net["std"]) != n:
        problems.append(f"features {n} · mean {len(net['mean'])} · std {len(net['std'])}")
    if any(not s > 0 for s in net["std"]):
        problems.append("std 에 0 이하가 있다")
    width = n
    for k, layer in enumerate(net["layers"]):
        if any(len(row) != width for row in layer["w"]) or len(layer["w"]) != len(layer["b"]):
            problems.append(f"층 {k} 의 모양이 입력 {width} 과 안 맞는다")
        width = len(layer["b"])
    if width != len(net["heads"]) or len(net["baseline"]) != len(net["heads"]):
        problems.append(f"출력 {width} · heads {len(net['heads'])} · baseline {len(net['baseline'])}")
    return problems


def _dumps(golden):
    """사례 하나를 한 줄에 — 입력 19칸과 로짓 5칸. 수는 json 의 기본(파이썬 repr · 가장 짧은 왕복 표기)이다."""
    lines = ["{"]
    lines.append(f' "_comment": {json.dumps(golden["_comment"], ensure_ascii=False)},')
    lines.append(f' "network_sha256": {json.dumps(golden["network_sha256"])},')
    lines.append(' "cases": [')
    cases = [f'  {{"input": {json.dumps(c["input"])}, "logits": {json.dumps(c["logits"])}}}' for c in golden["cases"]]
    lines.append(",\n".join(cases))
    lines.append(" ]")
    lines.append("}")
    return "\n".join(lines) + "\n"


def sha256(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def main(argv):
    verify = "--verify" in argv
    if not os.path.exists(NETWORK):
        print(f"망이 아직 없다 — {os.path.relpath(NETWORK, ROOT)} (tools/build.sh train 이 짓는다)")
        return 77

    with open(NETWORK, encoding="utf-8") as f:
        net = json.load(f)
    problems = check_shape(net)
    if problems:
        print("network.json 의 모양이 틀렸다 — " + " / ".join(problems))
        return 1

    if not verify:
        cases = [{"input": x, "logits": logits(net, x)} for x in inputs(net)]
        golden = {
            "_comment": "망의 골든 (#110 · 설계 2026-09-28 §5.7) — ml/golden.py 가 network.json 으로 셈했다. 파이썬 == 이 파일 == C#(PlayerNet). "
            "손으로 고치지 않는다: 망을 다시 학습하면 python3 ml/golden.py 로 다시 짓는다.",
            "network_sha256": sha256(NETWORK),
            "cases": cases,
        }
        with open(GOLDEN, "w", encoding="utf-8", newline="\n") as f:
            f.write(_dumps(golden))
        print(f"망 골든 — 입력 {len(cases)} 개의 로짓을 {os.path.relpath(GOLDEN, ROOT)} 에 썼다")
        return 0

    if not os.path.exists(GOLDEN):
        print(f"골든이 없다 — {os.path.relpath(GOLDEN, ROOT)}. python3 ml/golden.py 로 지으세요.")
        return 1
    with open(GOLDEN, encoding="utf-8") as f:
        golden = json.load(f)
    stale = golden.get("network_sha256") != sha256(NETWORK)
    wrong = []
    for k, case in enumerate(golden["cases"]):
        got = logits(net, case["input"])
        if got != case["logits"]:
            wrong.append(f"#{k}: 기대 {case['logits']} · 셈 {got}")
    if wrong or stale:
        why = "network.json 이 바뀌었는데 골든을 다시 안 지었다" if stale else "같은 망인데 로짓이 다르다 — 연산 순서가 바뀌었다"
        print(f"망 골든이 어긋났다 — {why}. 틀린 입력 {len(wrong)} 개")
        for line in wrong[:3]:
            print("  " + line)
        return 1

    print(f"망 골든 — 입력 {len(golden['cases'])} 개의 로짓이 network.json 과 비트까지 같다")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
