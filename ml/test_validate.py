"""validate.py 의 검사 (#114 · 설계 2026-09-28 §9 의 5번 줄 — 지표 계산 · 작은 손 입력).

    ml/.venv/bin/python ml/test_validate.py

평가 폴더를 손으로 지어(임시 폴더 · 봇 몇 대) 다섯 줄의 수를 손셈과 견준다. 판정이 수 하나를 잘못 읽으면 "망이 일한다" 가 거짓말을 한다 —
몫의 분모 · 갈래 · 무리를 가르는 자리가 전부 여기서 잡힌다.
"""

import json
import math
import os
import tempfile
import unittest

import numpy as np
import pandas as pd

import validate

HEADS = ["A", "B", "C", "D", "E"]

# 빈 표도 머리는 쓴다 — 평가 모드는 사례가 없어도 머리를 쓴다(CsvFile).
BOT_COLUMNS = ["bot", "habit", "reaction", "jitter", "rhythm", "reached_stage2", "network_won", "uniform_won"]
ATTEMPT_COLUMNS = ["bot", "arm", "attempt", "mode", "reason", "narrowed", "breathing"] + [f"logit_{h}" for h in range(len(HEADS))]
SAMPLE_COLUMNS = ["bot", "arm", "attempt", "slot", "label"]


def _bot(bot, habit="dash", reaction=0.2, jitter=0.05, rhythm=0.0, reached=1, net_won=0, uni_won=0):
    return {"bot": bot, "habit": habit, "reaction": reaction, "jitter": jitter, "rhythm": rhythm, "reached_stage2": reached,
            "network_won": net_won, "uniform_won": uni_won}


def _attempt(bot, arm, attempt, mode="", reason="", narrowed="", breathing="", logits=(0, 0, 0, 0, 0)):
    row = {"bot": bot, "arm": arm, "attempt": attempt, "mode": mode, "reason": reason, "narrowed": narrowed, "breathing": breathing}
    row.update({f"logit_{h}": v for h, v in enumerate(logits)})
    return row


def _cases(bot, arm, attempt, slots, labels):
    return [{"bot": bot, "arm": arm, "attempt": attempt, "slot": s, "label": y} for s, y in zip(slots, labels)]


def _evaluation(bots, attempts, samples):
    """임시 폴더에 세 CSV 와 매니페스트를 쓰고 읽는다 — 읽기 · 잇기까지 같이 잰다."""
    folder = tempfile.mkdtemp()
    pd.DataFrame(bots, columns=BOT_COLUMNS).to_csv(os.path.join(folder, "bots.csv"), index=False)
    pd.DataFrame(attempts, columns=ATTEMPT_COLUMNS).to_csv(os.path.join(folder, "attempts.csv"), index=False)
    pd.DataFrame(samples, columns=SAMPLE_COLUMNS).to_csv(os.path.join(folder, "samples.csv"), index=False)
    with open(os.path.join(folder, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump({"network_sha256": "시험"}, f)
    return validate.Evaluation(folder, HEADS)


class BootstrapTest(unittest.TestCase):
    def test_점_추정은_전체_합이고_시드가_같으면_구간이_같다(self):
        table = [[1, 2], [3, 4], [0, 1], [2, 2]]
        stat = lambda s: s[0] / s[1]

        a = validate.bootstrap(table, stat, draws=200, seed=3)
        b = validate.bootstrap(table, stat, draws=200, seed=3)

        self.assertEqual(a, b)
        self.assertAlmostEqual(a[0], 6 / 9)
        self.assertLessEqual(a[1], a[0])
        self.assertGreaterEqual(a[2], a[0])

    def test_봇이_모두_같으면_구간이_점이다(self):
        point, lo, hi = validate.bootstrap([[1, 4]] * 5, lambda s: s[0] / s[1], draws=50)

        self.assertEqual((point, lo, hi), (0.25, 0.25, 0.25))


class MetricsTest(unittest.TestCase):
    def test_의존_봉인은_그_습관형의_몫의_비와_맞는_비율의_차다(self):
        # 대시형 둘은 같다 — 망 갈래에서 D 가 넷 중 셋(맞음 셋) · 무작위에서 넷 중 하나(맞음 둘). 가드형은 무리에 안 든다.
        bots = [_bot(1), _bot(2), _bot(3, habit="guard")]
        samples = []
        attempts = []
        for bot in (1, 2, 3):
            attempts += [_attempt(bot, "network", 2, "narrowed", "habit", "0 3", 0), _attempt(bot, "uniform", 2)]
            samples += _cases(bot, "network", 2, [3, 3, 0, 3], [1, 1, 0, 1]) + _cases(bot, "uniform", 2, [0, 1, 2, 3], [0, 1, 0, 1])
        ev = _evaluation(bots, attempts, samples)

        row = validate.sealing(ev, "dash", "D", draws=50)

        self.assertEqual(row["bots"], 2)
        self.assertEqual((row["share_network"], row["share_uniform"]), (0.75, 0.25))
        self.assertEqual(row["share_ratio"], (3.0, 3.0, 3.0))
        self.assertEqual((row["hit_network"], row["hit_uniform"]), (0.75, 0.5))
        self.assertEqual(row["hit_diff"], (0.25, 0.25, 0.25))
        self.assertTrue(validate.sealing_passes(row))

    def test_숨통은_좁힌_시도의_숨통_칸과_겨냥_칸을_가른다(self):
        bots = [_bot(1), _bot(2)]
        attempts = [_attempt(1, "network", 2, "narrowed", "habit", "1 3 4", 1), _attempt(2, "network", 2, "narrowed", "habit", "1 3 4", 1),
                    _attempt(1, "network", 3, "full", "no_habit", "0 1 2 3 4")]
        samples = (_cases(1, "network", 2, [1, 3, 4, 1], [0, 1, 1, 0]) + _cases(2, "network", 2, [1, 3, 4, 1], [0, 1, 1, 0])
                   + _cases(1, "network", 3, [0, 2], [1, 1]))
        ev = _evaluation(bots, attempts, samples)

        row = validate.breathing(ev, draws=50)

        self.assertEqual((row["attempts"], row["cases"]), (2, 8), "다섯 전부로 선 시도는 숨통을 안 잰다")
        self.assertTrue(row["carried"] and row["inside"])
        self.assertEqual((row["hit_breathing"], row["hit_targeted"]), (0.0, 1.0))
        self.assertEqual(row["diff"], (-1.0, -1.0, -1.0))
        self.assertTrue(validate.breathing_passes(row))

    def test_숨통을_안_실은_시도가_있으면_못_넘는다(self):
        attempts = [_attempt(1, "network", 2, "narrowed", "habit", "1 3", 4)]
        ev = _evaluation([_bot(1)], attempts, _cases(1, "network", 2, [1, 3], [0, 1]))

        self.assertFalse(validate.breathing(ev, draws=20)["carried"])

    def test_약점_저격은_고르게_약한_봇의_망_갈래만_센다(self):
        # 약한 혼합형의 망 갈래 넷 중 다섯 전부가 셋 — 75% 로 선(80%) 아래다. 대시형 · 무작위 갈래는 안 센다.
        bots = [_bot(1, habit="mixed", reaction=0.35, jitter=0.10), _bot(2), _bot(3, habit="mixed", reaction=0.20, jitter=0.10)]
        attempts = [_attempt(1, "network", 2, "full", "thin", "0 1 2 3 4"), _attempt(1, "network", 3, "full", "no_habit", "0 1 2 3 4"),
                    _attempt(1, "network", 4, "narrowed", "habit", "0 2", 2), _attempt(1, "network", 5, "full", "no_habit", "0 1 2 3 4"),
                    _attempt(1, "uniform", 2), _attempt(2, "network", 2, "narrowed", "habit", "0 3", 0),
                    _attempt(3, "network", 2, "narrowed", "habit", "0 3", 0)]
        ev = _evaluation(bots, attempts, [])

        row = validate.weakness(ev, draws=20)

        self.assertEqual((row["bots"], row["attempts"]), (1, 4))
        self.assertEqual(row["full"][0], 0.75)
        self.assertEqual((row["thin"], row["no_habit"]), (0.25, 0.5))
        self.assertFalse(validate.weakness_passes(row))

    def test_죽음의_나선은_2단계에_간_봇의_승률의_비다(self):
        bots = [_bot(1, net_won=1, uni_won=1), _bot(2, uni_won=1), _bot(3), _bot(4), _bot(5, reached=0, net_won=1, uni_won=1)]
        ev = _evaluation(bots, [], [])

        row = validate.spiral(ev, draws=20)

        self.assertEqual((row["bots"], row["win_network"], row["win_uniform"]), (4, 0.25, 0.5))
        self.assertEqual(row["ratio"][0], 0.5)
        self.assertTrue(validate.spiral_passes(row), "절반이면 선 위다")

    def test_보정은_뽑힌_칸의_그_시도의_로짓을_읽는다(self):
        # C 칸(2)의 로짓만 0(확률 0.5)이고 나머지는 극단 — 이음이 칸을 잘못 읽으면 예측 평균이 0.5 에서 멀어진다. 스무 건이라 같은 도수 열 구간이
        # 둘씩(맞음 · 안 맞음) 담겨 ECE 가 0 이다 — 넷이면 구간마다 한 건이라 확률 0.5 의 예측도 ECE 0.5 다(정의가 그렇다).
        logits = (9, -9, 0, 9, -9)
        attempts = [_attempt(1, "uniform", 2, logits=logits), _attempt(1, "network", 2, "full", "thin", "0 1 2 3 4", logits=logits)]
        samples = _cases(1, "uniform", 2, [2] * 10, [1, 0] * 5) + _cases(1, "network", 2, [2] * 10, [1, 0] * 5)
        ev = _evaluation([_bot(1)], attempts, samples)

        rows = {r["head"]: r for r in validate.calibration(ev)}

        c = rows["C"]["all"]
        self.assertEqual(c["n"], 20)
        self.assertAlmostEqual(c["mean_p"], 0.5)
        self.assertAlmostEqual(c["rate"], 0.5)
        self.assertAlmostEqual(c["ece"], 0.0)
        self.assertEqual(rows["C"]["network"]["n"], 10)
        self.assertTrue(math.isnan(rows["A"]["all"]["ece"]), "뽑힌 적 없는 칸은 모른다")

    def test_좁힌_칸은_공백으로_이은_번호다(self):
        self.assertEqual(validate.parse_slots("0 1 3"), (0, 1, 3))
        self.assertEqual(validate.parse_slots(float("nan")), ())
        self.assertEqual(validate.parse_slots(np.nan), ())


if __name__ == "__main__":
    unittest.main()
