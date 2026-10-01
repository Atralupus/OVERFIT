"""넘파이 망 · PPO · GAE (설계 2026-10-01 조각5 §5). 실행: ml/.venv/bin/python -m ml.rl.test_net"""

from __future__ import annotations

import tempfile
from pathlib import Path

import numpy as np

from ml.rl import net, ppo, rollout


def numeric_grad(f, x: np.ndarray, eps: float = 1e-6) -> np.ndarray:
    g = np.zeros_like(x)
    it = np.nditer(x, flags=["multi_index"], op_flags=["readwrite"])
    for _ in it:
        i = it.multi_index
        old = x[i]
        x[i] = old + eps
        hi = f()
        x[i] = old - eps
        lo = f()
        x[i] = old
        g[i] = (hi - lo) / (2 * eps)
    return g


def test_mlp_gradient() -> None:
    rng = np.random.default_rng(0)
    m = net.Mlp.init([5, 4, 3], rng, last_scale=1.0)
    x = rng.standard_normal((7, 5))
    upstream = rng.standard_normal((7, 3))

    def loss() -> float:
        return float((m.forward(x) * upstream).sum())

    m.forward(x)
    grads = m.backward(upstream)
    for (w, b), (gw, gb) in zip(m.params, grads):
        assert np.allclose(gw, numeric_grad(loss, w), atol=1e-5), "w 기울기가 수치 미분과 다르다"
        assert np.allclose(gb, numeric_grad(loss, b), atol=1e-5), "b 기울기가 수치 미분과 다르다"


def test_ppo_logit_gradient() -> None:
    rng = np.random.default_rng(1)
    n, a = 6, 5
    logits = rng.standard_normal((n, a))
    mask = rng.random((n, a)) < 0.7
    mask[:, 0] = True
    action = np.array([np.flatnonzero(mask[i])[rng.integers(mask[i].sum())] for i in range(n)])
    old_logp = ppo.log_softmax_masked(logits + 0.3 * rng.standard_normal((n, a)), mask)[np.arange(n), action]
    adv = rng.standard_normal(n)

    def loss() -> float:
        return ppo.policy_loss(logits, mask, action, old_logp, adv, clip=0.2, entropy_coef=0.05)[0]

    _, grad, _ = ppo.policy_loss(logits, mask, action, old_logp, adv, clip=0.2, entropy_coef=0.05)
    assert np.allclose(grad, numeric_grad(loss, logits), atol=1e-5), "PPO 로짓 기울기가 수치 미분과 다르다"
    assert np.all(grad[~mask] == 0), "가려진 칸에 기울기가 있다"


def test_gae_stops_at_episode_end() -> None:
    # 판 둘: [r=1, r=2(끝)] · [r=3(끝)]. γ=0.5 · λ=1 · 가치 0 이면 이점 = 할인 보상합이고 판 경계를 안 넘는다.
    reward = np.array([1.0, 2.0, 3.0])
    value = np.zeros(3)
    done = np.array([False, True, True])
    adv, ret = ppo.gae(reward, value, done, gamma=0.5, lam=1.0)
    assert np.allclose(adv, [1 + 0.5 * 2, 2, 3]), adv
    assert np.allclose(ret, adv)


def test_gae_discounts_by_time() -> None:
    # 결정의 길이로 할인한다(최종 리뷰) — γ^(span/12). 길이 24 의 결정 뒤 보상은 γ² 로 깎인다. 판의 끝은 끊긴다.
    reward = np.array([0.0, 1.0, 5.0])
    value = np.zeros(3)
    done = np.array([False, True, True])
    span = np.array([24, 12, 12])
    adv, _ = ppo.gae(reward, value, done, gamma=0.5, lam=1.0, span=span, unit=12)
    assert np.allclose(adv, [0.25 * 1.0, 1.0, 5.0]), adv


def test_json_round_trip_and_matches_csharp() -> None:
    # 넘파이 가중치를 C# 일꾼에 넘기고, 일꾼이 적은 logp 를 넘파이가 다시 셈한다(설계 §4).
    with tempfile.TemporaryDirectory() as tmp:
        probe = rollout.run(seed=21, episodes=2, out_dir=Path(tmp) / "probe")
        m = probe.manifest
        rng = np.random.default_rng(2)
        policy = net.Mlp.init([m["obs"], 32, 32, m["actions"]], rng, last_scale=1.0)
        value = net.Mlp.init([m["obs"], 32, 32, 1], rng, last_scale=1.0)
        path = Path(tmp) / "w.json"
        net.save(path, m["obs"], m["actions"], m["roster"], policy, value)
        p2, v2 = net.load(path)
        x = rng.standard_normal((3, m["obs"]))
        assert np.allclose(p2.forward(x), policy.forward(x)) and np.allclose(v2.forward(x), value.forward(x)), "JSON 왕복이 다르다"

        r = rollout.run(seed=22, episodes=4, out_dir=Path(tmp) / "net", weights=path)
        logp = ppo.log_softmax_masked(policy.forward(r.obs), r.mask)[np.arange(len(r.action)), r.action]
        assert np.max(np.abs(logp - r.logp)) < 1e-4, f"넘파이 logp 와 C# logp 가 다르다 — {np.max(np.abs(logp - r.logp))}"
        assert np.max(np.abs(value.forward(r.obs)[:, 0] - r.value)) < 1e-3, "넘파이 가치와 C# 가치가 다르다"


def main() -> None:
    test_mlp_gradient()
    test_ppo_logit_gradient()
    test_gae_stops_at_episode_end()
    test_gae_discounts_by_time()
    test_json_round_trip_and_matches_csharp()
    print("ok")


if __name__ == "__main__":
    main()
