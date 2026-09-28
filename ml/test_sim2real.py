"""sim2real.py 의 검사 (#114 · 설계 2026-09-28 §7.3) — 손으로 지은 기록 몇 줄 · 작은 망 · 작은 함대.

    ml/.venv/bin/python ml/test_sim2real.py

사람의 기록은 적고 귀하다 — 줄을 잘못 읽거나(옛 줄 · 1단계) 칸을 엉뚱한 예측에 이으면 "봇과 사람이 다르다" 를 틀리게 말한다.
"""

import json
import math
import os
import tempfile
import unittest

import numpy as np
import pandas as pd

import sim2real

NAMES = [f"f{i}" for i in range(19)]
HEADS = ["A", "B", "C", "D", "E"]
LN3 = math.log(3)

# 가중치 0 · 한 층 — 로짓이 곧 편향이다(B = ln 3 → 0.75 · D = −ln 3 → 0.25).
NET = {"features": NAMES, "heads": HEADS, "mean": [0.0] * 19, "std": [1.0] * 19,
       "layers": [{"w": [[0.0] * 19 for _ in HEADS], "b": [0.0, LN3, 0.0, -LN3, 0.0]}]}


def _line(stage=2, arm="network", features=True, instances=(("B", True), ("B", False), ("D", False)), sha="aaa"):
    entry = {"session_seed": 7, "run": 1, "attempt": 3, "stage": stage, "seed": 1, "picker": "network", "arm": arm, "drawn": [],
             "outcome": "lose", "ticks": 10, "events": [], "network_sha256": sha}
    if features:
        entry["features"] = [0.0] * 19
        entry["instances"] = [{"pattern_id": p, "hit": h} for p, h in instances]
    return entry


def _lines():
    return [_line(stage=1, arm=None), _line(features=False), _line(), _line(arm="uniform", instances=(("A", True),), sha="bbb")]


class Sim2RealTest(unittest.TestCase):
    def test_옛_줄과_1단계는_세고_뺀다(self):
        human = sim2real.Human(_lines())

        self.assertEqual((len(human.attempts), human.stage1, human.skipped_old), (2, 1, 1))
        self.assertEqual(human.networks, {"aaa", "bbb"})

    def test_깨진_줄은_파일과_줄을_말한다(self):
        folder = tempfile.mkdtemp()
        path = os.path.join(folder, "1.jsonl")
        with open(path, "w", encoding="utf-8") as f:
            f.write(json.dumps(_line()) + "\n\n{깨짐\n")

        with self.assertRaisesRegex(ValueError, "1.jsonl:3"):
            sim2real.read_lines([path])

    def test_사례는_그_시도의_그_칸_예측에_잇는다(self):
        frame = sim2real.cases(sim2real.Human(_lines()), NET, HEADS)

        self.assertEqual(list(frame["slot"]), [1, 1, 3, 0])
        self.assertEqual(list(frame["arm"]), ["network", "network", "network", "uniform"])
        self.assertEqual(list(frame["label"]), [1, 0, 0, 1])
        self.assertEqual(list(frame["logit"]), [LN3, LN3, -LN3, 0.0])

    def test_보정은_칸마다_예측_평균과_실제다(self):
        frame = sim2real.cases(sim2real.Human(_lines()), NET, HEADS)

        rows = {r["head"]: r for r in sim2real.calibration(frame, HEADS)}

        b = rows["B"]["all"]
        self.assertEqual(b["n"], 2)
        self.assertAlmostEqual(b["mean_p"], 0.75)
        self.assertEqual(b["rate"], 0.5)
        self.assertTrue(math.isnan(b["ece"]), "사례 열 건 아래는 ECE 를 안 잰다")
        self.assertEqual((rows["A"]["uniform"]["n"], rows["A"]["network"]["n"]), (1, 0))

    def test_갈래_비교는_갈래마다_맞는_비율과_차다(self):
        frame = sim2real.cases(sim2real.Human(_lines()), NET, HEADS)

        row = sim2real.arms(frame, draws=50)

        self.assertEqual((row["cases_network"], row["cases_uniform"]), (3, 1))
        self.assertAlmostEqual(row["hit_network"], 1 / 3)
        self.assertEqual(row["hit_uniform"], 1.0)
        self.assertAlmostEqual(row["diff"][0], 1 / 3 - 1)

    def test_분포_밖은_함대의_1_99_백분위_밖인_몫이다(self):
        fleet = np.zeros((100, 19))
        fleet[:, 0] = np.arange(100)
        human = np.zeros((2, 19))
        human[:, 0] = [-5, 50]

        rows = {r["feature"]: r for r in sim2real.out_of_distribution(human, fleet, NAMES)}

        self.assertAlmostEqual(rows["f0"]["low"], 0.99)
        self.assertAlmostEqual(rows["f0"]["high"], 98.01)
        self.assertEqual(rows["f0"]["outside"], 0.5)
        self.assertEqual(rows["f1"]["outside"], 0.0, "함대가 늘 0 인 칸에서 사람도 0 이면 안이다")

    def test_처음부터_끝까지_보고서를_쓴다(self):
        folder = tempfile.mkdtemp()
        records = os.path.join(folder, "7.jsonl")
        with open(records, "w", encoding="utf-8") as f:
            f.write("\n".join(json.dumps(line, ensure_ascii=False) for line in _lines()) + "\n")
        network = os.path.join(folder, "network.json")
        with open(network, "w", encoding="utf-8") as f:
            json.dump(NET, f)
        fleet = os.path.join(folder, "fleet")
        os.makedirs(fleet)
        rows = pd.DataFrame(np.zeros((4, 19)), columns=NAMES)
        rows.insert(0, "label", [0, 1, 0, 1])
        rows.insert(0, "slot", [0, 1, 2, 3])
        rows.insert(0, "attempt", [2, 2, 3, 3])
        rows.insert(0, "bot", [0, 0, 0, 1])
        rows.to_csv(os.path.join(fleet, "samples.csv"), index=False)
        out = os.path.join(folder, "report.md")

        code = sim2real.main([records, f"--fleet={fleet}", f"--network={network}", f"--out={out}", "--draws=20"])

        self.assertEqual(code, 0)
        with open(out, encoding="utf-8") as f:
            report = f.read()
        for heading in ("## 분포 밖인가", "## 보정", "## 갈래 비교"):
            self.assertIn(heading, report)
        self.assertIn("2단계 시도 2 (1단계 줄 1 · 입력이나 사례가 없는 옛 줄 1 은 뺐다)", report)

    def test_기록이_없으면_무엇을_할지_말하고_1이다(self):
        # 기본 폴더(ml/human)를 빈 임시 폴더로 바꿔 잰다 — 유저가 옮겨 둔 진짜 기록을 테스트가 읽지 않게.
        empty = tempfile.mkdtemp()
        original = sim2real.HUMAN_DIR
        sim2real.HUMAN_DIR = empty
        try:
            self.assertEqual(sim2real.main([f"--out={os.path.join(empty, 'r.md')}"]), 1)
        finally:
            sim2real.HUMAN_DIR = original


if __name__ == "__main__":
    unittest.main()
