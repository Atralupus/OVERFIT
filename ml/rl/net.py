"""넘파이 MLP — 정책망 · 가치망 (설계 2026-10-01 조각5 §1 · 조각4 §3).

C# PolicyNet 과 같은 수식이다: 은닉은 ReLU, 마지막 층은 선형, w 는 [출력][입력]. 가중치 JSON 의 모양도 같다 — 일꾼이 그대로 읽는다.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np


@dataclass
class Mlp:
    params: list[tuple[np.ndarray, np.ndarray]]
    _cache: list[np.ndarray] = field(default_factory=list, repr=False)

    @staticmethod
    def init(sizes: list[int], rng: np.random.Generator, last_scale: float) -> "Mlp":
        """He 초기화(ReLU) · 마지막 층은 last_scale 배 — 정책의 마지막 층을 작게 두면 처음 정책이 고른 확률에 가깝다(1페이즈 = 거의 안 배운 보스)."""
        params = []
        for i, (n_in, n_out) in enumerate(zip(sizes[:-1], sizes[1:])):
            w = rng.standard_normal((n_out, n_in)) * np.sqrt(2.0 / n_in)
            if i == len(sizes) - 2:
                w *= last_scale
            params.append((w, np.zeros(n_out)))
        return Mlp(params)

    def forward(self, x: np.ndarray) -> np.ndarray:
        self._cache = [x]
        h = x
        for i, (w, b) in enumerate(self.params):
            z = h @ w.T + b
            h = np.maximum(z, 0) if i < len(self.params) - 1 else z
            self._cache.append(h)
        return h

    def backward(self, upstream: np.ndarray) -> list[tuple[np.ndarray, np.ndarray]]:
        """마지막 forward 의 입력에 대한 기울기 — 층마다 (dw, db)."""
        grads: list[tuple[np.ndarray, np.ndarray]] = []
        g = upstream
        for i in range(len(self.params) - 1, -1, -1):
            w, _ = self.params[i]
            h_out = self._cache[i + 1]
            if i < len(self.params) - 1:
                g = g * (h_out > 0)
            h_in = self._cache[i]
            grads.append((g.T @ h_in, g.sum(axis=0)))
            g = g @ w
        return grads[::-1]


def _layers(m: Mlp) -> list[dict]:
    return [{"w": w.tolist(), "b": b.tolist()} for w, b in m.params]


def save(path: str | Path, obs: int, actions: int, roster: list[str], policy: Mlp, value: Mlp) -> None:
    doc = {"obs": obs, "actions": actions, "roster": roster, "policy": _layers(policy), "value": _layers(value)}
    Path(path).write_text(json.dumps(doc, ensure_ascii=False), encoding="utf-8")


def load(path: str | Path) -> tuple[Mlp, Mlp]:
    doc = json.loads(Path(path).read_text(encoding="utf-8"))

    def mlp(layers: list[dict]) -> Mlp:
        return Mlp([(np.array(l["w"], dtype=np.float64), np.array(l["b"], dtype=np.float64)) for l in layers])

    return mlp(doc["policy"]), mlp(doc["value"])
