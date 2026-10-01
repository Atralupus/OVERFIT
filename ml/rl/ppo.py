"""PPO — GAE · 손실과 로짓 기울기 · Adam (설계 2026-10-01 조각5 §1)."""

from __future__ import annotations

import numpy as np


def log_softmax_masked(logits: np.ndarray, mask: np.ndarray) -> np.ndarray:
    """가려진 칸은 −inf. C# NetController 와 같은 수식(열린 칸의 최댓값을 빼고)이다."""
    z = np.where(mask, logits, -np.inf)
    m = z.max(axis=1, keepdims=True)
    s = np.log(np.exp(z - m).sum(axis=1, keepdims=True))
    return z - m - s


def gae(reward: np.ndarray, value: np.ndarray, done: np.ndarray, gamma: float, lam: float) -> tuple[np.ndarray, np.ndarray]:
    """일반화 이점 추정 — 줄은 판 순서이고, 판의 끝(done) 다음 가치는 0 이다(판 경계를 안 넘는다)."""
    n = len(reward)
    adv = np.zeros(n)
    last = 0.0
    for t in range(n - 1, -1, -1):
        next_value = 0.0 if done[t] else value[t + 1]
        nonterminal = 0.0 if done[t] else 1.0
        delta = reward[t] + gamma * next_value - value[t]
        last = delta + gamma * lam * nonterminal * last
        adv[t] = last
    return adv, adv + value


def policy_loss(logits: np.ndarray, mask: np.ndarray, action: np.ndarray, old_logp: np.ndarray, adv: np.ndarray,
                clip: float, entropy_coef: float) -> tuple[float, np.ndarray, dict]:
    """clip 된 대리 목적의 음수 − 엔트로피 · 로짓에 대한 기울기 · 지표. 가려진 칸의 기울기는 0 이다."""
    n = len(action)
    logp_all = log_softmax_masked(logits, mask)
    p = np.where(mask, np.exp(logp_all), 0.0)
    logp = logp_all[np.arange(n), action]
    ratio = np.exp(logp - old_logp)
    clipped = np.clip(ratio, 1 - clip, 1 + clip)
    surr = np.minimum(ratio * adv, clipped * adv)
    safe = np.where(mask, logp_all, 0.0)
    entropy = -(p * safe).sum(axis=1)
    loss = -surr.mean() - entropy_coef * entropy.mean()

    # d(−surr)/dlogp — clip 이 묶은 쪽(이점 > 0 이고 비율 > 1+ε, 이점 < 0 이고 비율 < 1−ε)은 0.
    active = ~(((adv > 0) & (ratio > 1 + clip)) | ((adv < 0) & (ratio < 1 - clip)))
    g_logp = np.where(active, -adv * ratio, 0.0) / n
    onehot = np.zeros_like(logits)
    onehot[np.arange(n), action] = 1.0
    grad = g_logp[:, None] * (onehot - p)
    # dH/dz_j = −p_j (log p_j + H) — 엔트로피를 올리는 쪽이라 손실에는 −계수.
    d_entropy = -p * (safe + entropy[:, None])
    grad += -entropy_coef * d_entropy / n
    grad = np.where(mask, grad, 0.0)
    stats = {
        "entropy": float(entropy.mean()),
        "approx_kl": float((old_logp - logp).mean()),
        "clip_frac": float((np.abs(ratio - 1) > clip).mean()),
    }
    return float(loss), grad, stats


class Adam:
    def __init__(self, params: list[tuple[np.ndarray, np.ndarray]], lr: float, b1: float = 0.9, b2: float = 0.999, eps: float = 1e-8):
        self.lr, self.b1, self.b2, self.eps, self.t = lr, b1, b2, eps, 0
        self.m = [(np.zeros_like(w), np.zeros_like(b)) for w, b in params]
        self.v = [(np.zeros_like(w), np.zeros_like(b)) for w, b in params]

    def step(self, params: list[tuple[np.ndarray, np.ndarray]], grads: list[tuple[np.ndarray, np.ndarray]]) -> None:
        self.t += 1
        for i, ((w, b), (gw, gb)) in enumerate(zip(params, grads)):
            for j, (p, g) in enumerate(((w, gw), (b, gb))):
                m = self.m[i][j]
                v = self.v[i][j]
                m *= self.b1
                m += (1 - self.b1) * g
                v *= self.b2
                v += (1 - self.b2) * g * g
                mh = m / (1 - self.b1 ** self.t)
                vh = v / (1 - self.b2 ** self.t)
                p -= self.lr * mh / (np.sqrt(vh) + self.eps)


def clip_grads(grads: list[tuple[np.ndarray, np.ndarray]], max_norm: float) -> float:
    norm = float(np.sqrt(sum((gw * gw).sum() + (gb * gb).sum() for gw, gb in grads)))
    if norm > max_norm:
        scale = max_norm / (norm + 1e-12)
        for gw, gb in grads:
            gw *= scale
            gb *= scale
    return norm
