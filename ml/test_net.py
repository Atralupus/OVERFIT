"""net.py 의 검사 (#110) — 유한 차분 기울기 · 칸을 가린 손실 · 시드 · 출력 편향.

    ml/.venv/bin/python ml/test_net.py

넘파이가 필요해 check 에는 안 든다 — tools/build.sh train 이 학습 전에 먼저 돈다. 역전파를 손으로 썼으므로(torch 없음) 기울기가 곧
정확성의 전부다: 틀린 기울기로도 손실은 대개 줄어 학습이 "돌긴 도는" 채로 조용히 나쁜 망이 나온다.
"""

import unittest

import numpy as np

from net import Adam, Net

INPUTS = 19
HEADS = 5


def _batch(seed=7, n=8):
    rng = np.random.default_rng(seed)
    z = rng.normal(size=(n, INPUTS))
    slot = np.array([0, 1, 2, 3, 4, 1, 3, 0][:n])
    y = np.array([1, 0, 1, 1, 0, 0, 1, 0][:n], dtype=float)
    return z, slot, y


class NetTest(unittest.TestCase):
    def test_기울기는_유한_차분과_같다(self):
        net = Net.create(INPUTS, HEADS, seed=3, baseline=[0.2, -0.4, 1.0, 0.0, 0.5])
        z, slot, y = _batch()
        _, grads = net.loss_and_grads(z, slot, y)
        eps = 1e-6
        for name, param in net.params():
            flat = param.reshape(-1)
            for k in range(0, flat.size, max(1, flat.size // 17)):
                old = flat[k]
                flat[k] = old + eps
                plus = net.loss(z, slot, y)
                flat[k] = old - eps
                minus = net.loss(z, slot, y)
                flat[k] = old
                numeric = (plus - minus) / (2 * eps)
                self.assertAlmostEqual(grads[name].reshape(-1)[k], numeric, delta=1e-7 + 1e-5 * abs(numeric), msg=f"{name}[{k}]")

    def test_사례는_제_칸의_머리만_가르친다(self):
        # 사례 하나는 제 칸의 머리만 가르친다(설계 §5.3 — 칸을 가린 손실). 다른 칸의 출력 행과 편향에는 기울기가 없어야 한다.
        net = Net.create(INPUTS, HEADS, seed=3, baseline=np.zeros(HEADS))
        z, _, y = _batch()
        slot = np.full(len(y), 2)
        _, grads = net.loss_and_grads(z, slot, y)
        last = len(net.w) - 1
        for head in range(HEADS):
            touched = np.any(grads[f"w{last}"][head] != 0) or grads[f"b{last}"][head] != 0
            self.assertEqual(touched, head == 2, f"머리 {head}")

    def test_같은_시드는_같은_초기값이다(self):
        a = Net.create(INPUTS, HEADS, seed=11, baseline=np.zeros(HEADS))
        b = Net.create(INPUTS, HEADS, seed=11, baseline=np.zeros(HEADS))
        c = Net.create(INPUTS, HEADS, seed=12, baseline=np.zeros(HEADS))
        for (name, pa), (_, pb), (_, pc) in zip(a.params(), b.params(), c.params()):
            np.testing.assert_array_equal(pa, pb, err_msg=name)
            if name.startswith("w"):
                self.assertFalse(np.array_equal(pa, pc), name)

    def test_출력_편향은_기저율의_로짓에서_시작한다(self):
        # 처음부터 기저율을 말하는 망에서 시작한다 — 학습은 사람을 보고 그 위로 얼마나 오르내리나만 배운다.
        baseline = [0.3, -1.2, 0.7, 2.0, 0.0]
        net = Net.create(INPUTS, HEADS, seed=3, baseline=baseline)
        np.testing.assert_array_equal(net.b[-1], baseline)

    def test_adam_은_손실을_줄인다(self):
        # 입력의 한 열이 라벨을 정하는 작은 문제 — 몇백 걸음이면 손실이 크게 준다.
        rng = np.random.default_rng(5)
        z = rng.normal(size=(256, INPUTS))
        slot = rng.integers(0, HEADS, size=256)
        y = (z[:, 0] > 0).astype(float)
        net = Net.create(INPUTS, HEADS, seed=3, baseline=np.zeros(HEADS))
        adam = Adam(net, lr=1e-2)
        before = net.loss(z, slot, y)
        for _ in range(300):
            _, grads = net.loss_and_grads(z, slot, y)
            adam.step(grads)
        self.assertLess(net.loss(z, slot, y), before * 0.5)


if __name__ == "__main__":
    unittest.main()
