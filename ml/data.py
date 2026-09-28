"""공장의 원본을 읽고 나눈다 (#110 · 설계 2026-09-28 §4.4 · §5.3).

원본은 tools/build.sh factory 가 짓는 폴더 하나다 — samples.csv(사례 한 줄 · 입력 19칸) · bots.csv(봇 한 줄 · 성향) · manifest.json.
"""

import hashlib
import json
import os

import numpy as np
import pandas as pd

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA_DIR = os.path.join(ROOT, "overfit", "data")

# DataDigest.Files 와 같은 목록 · 같은 순서다(overfit/core/DataDigest.cs). 둘이 갈리면 "배운 게임이 지금의 게임인가" 가 거짓말을 한다.
DIGEST_FILES = ("bosses.json", "fighters.json", "hitboxes.json", "patterns.json", "stages.json")

# samples.csv 의 앞 네 칸 — 그 뒤가 입력이다(SampleCsv.SamplesHeader).
KEYS = ("bot", "attempt", "slot", "label")


def data_digest(data_dir=DATA_DIR):
    """DataDigest.Of 와 같은 정의 — 파일마다 이름 \\0 길이 \\0 바이트를 이은 sha256(소문자 16진)."""
    h = hashlib.sha256()
    for name in DIGEST_FILES:
        with open(os.path.join(data_dir, name), "rb") as f:
            body = f.read()
        h.update(name.encode("utf-8") + b"\0" + str(len(body)).encode("ascii") + b"\0" + body)
    return h.hexdigest()


def stage2_heads(data_dir=DATA_DIR):
    """망의 머리 = stages.json 2단계의 명부 순서 — 공장의 칸(slot)이 이 인덱스다."""
    with open(os.path.join(data_dir, "stages.json"), encoding="utf-8") as f:
        return list(json.load(f)["2"]["patterns"])


def fleet_targeting(fleet_path=os.path.join(ROOT, "tools", "factory", "fleet.json")):
    """겨냥 표 — fleet.json 의 targeting(#109). 관문이 줄마다 들어 올림을 잰다."""
    with open(fleet_path, encoding="utf-8") as f:
        return json.load(f)["targeting"]


class Raw:
    """원본 한 폴더. 수는 왕복 서식으로 읽는다(float_precision="round_trip") — 공장이 쓴 double 을 비트까지 그대로 얻는다."""

    def __init__(self, path):
        self.path = path
        with open(os.path.join(path, "manifest.json"), encoding="utf-8") as f:
            self.manifest = json.load(f)
        self.samples = pd.read_csv(os.path.join(path, "samples.csv"), float_precision="round_trip")
        self.bots = pd.read_csv(os.path.join(path, "bots.csv"), float_precision="round_trip")
        columns = list(self.samples.columns)
        if tuple(columns[: len(KEYS)]) != KEYS:
            raise ValueError(f"samples.csv 의 머리가 {KEYS} 로 시작하지 않는다: {columns[:4]}")
        self.features = columns[len(KEYS):]

    def x(self, rows=None):
        frame = self.samples if rows is None else self.samples[rows]
        return frame[self.features].to_numpy(dtype=float)

    def split(self):
        """봇 번호로 학습 80 · 검증 10 · 시험 10 — 범위를 앞에서부터 자른다. 같은 봇의 사례가 양쪽에 새면 외운 것을 일반화로 착각한다."""
        first, last = self.manifest["bot_from"], self.manifest["bot_to"]
        n = last - first
        cut1, cut2 = first + n * 8 // 10, first + n * 9 // 10
        bot = self.samples["bot"].to_numpy()
        return {
            "train": (bot < cut1, (first, cut1)),
            "valid": ((bot >= cut1) & (bot < cut2), (cut1, cut2)),
            "test": (bot >= cut2, (cut2, last)),
        }


def standardize(x):
    """학습 몫의 평균 · 모집단 편차. 편차가 0 인 열은 1 — 그 열은 아무것도 안 가르친다(설계 §5.3)."""
    mean = x.mean(axis=0)
    std = x.std(axis=0)
    std = np.where(std > 0, std, 1.0)
    return mean, std
