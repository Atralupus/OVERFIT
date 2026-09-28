"""넘파이 MLP — 입력 19 → 32 → 32 → 5, ReLU, 칸을 가린 이진 교차 엔트로피, Adam (#110 · 설계 2026-09-28 §5.2 · §5.3).

torch 를 안 쓴다 — 모수 1,900 개의 망에 torch 가 주는 것이 없고, 이 환경에서는 CUDA 휠(수 GB)을 세션마다 받아야 한다
(docs/superpowers/plans/2026-09-28-학습.md). 역전파를 손으로 썼으므로 test_net.py 가 유한 차분으로 기울기를 검사한다.

가중치 w 는 **행이 출력 · 열이 입력**이다 — network.json 과 C# 의 PlayerNet 이 같은 모양이다. 학습은 행렬곱(BLAS)으로 하고, 게임과 골든은
출력마다 입력 순서대로 더한다 — 둘의 차이는 덧셈 순서뿐이라 export.py 가 1e-9 안인지 본다.
"""

import numpy as np

HIDDEN = (32, 32)


class Net:
    """층마다 (w, b). 마지막 층을 빼고 ReLU 다."""

    def __init__(self, weights, biases):
        self.w = [np.array(w, dtype=float) for w in weights]
        self.b = [np.array(b, dtype=float) for b in biases]

    @staticmethod
    def create(inputs, heads, seed, baseline, hidden=HIDDEN):
        """He 초기화(시드 고정). 출력 층은 작게(0.1 배) 두고 편향을 기저율의 로짓에서 시작한다 — 처음부터 기저율을 말하는 망이라 학습은 사람을 보고
        그 위로 얼마나 오르내리나만 배운다."""
        rng = np.random.default_rng(seed)
        sizes = [inputs, *hidden, heads]
        weights, biases = [], []
        for fan_in, fan_out in zip(sizes, sizes[1:]):
            weights.append(rng.normal(0.0, np.sqrt(2.0 / fan_in), size=(fan_out, fan_in)))
            biases.append(np.zeros(fan_out))
        weights[-1] *= 0.1
        biases[-1] = np.array(baseline, dtype=float)
        return Net(weights, biases)

    def params(self):
        """(이름, 배열) — 이름은 w0 · b0 · w1 · … 이다. 배열은 제자리에서 고쳐진다(Adam · 기울기 검사)."""
        for i, (w, b) in enumerate(zip(self.w, self.b)):
            yield f"w{i}", w
            yield f"b{i}", b

    def _forward(self, z):
        acts, pres = [z], []
        a = z
        last = len(self.w) - 1
        for i, (w, b) in enumerate(zip(self.w, self.b)):
            s = a @ w.T + b
            pres.append(s)
            a = s if i == last else np.where(s > 0, s, 0.0)
            acts.append(a)
        return a, acts, pres

    def logits(self, z):
        """표준화된 입력 (n, 19) → 로짓 (n, 5)."""
        return self._forward(np.asarray(z, dtype=float))[0]

    @staticmethod
    def _bce(s, y):
        # log(1 + e^s) - y·s 를 넘치지 않게: max(s, 0) - y·s + log(1 + e^-|s|).
        return np.maximum(s, 0) - s * y + np.log1p(np.exp(-np.abs(s)))

    def loss(self, z, slot, y):
        """칸을 가린 평균 BCE — 사례마다 제 칸의 로짓 하나만 본다."""
        out = self.logits(z)
        s = out[np.arange(len(y)), slot]
        return float(np.mean(self._bce(s, y)))

    def loss_and_grads(self, z, slot, y):
        out, acts, pres = self._forward(np.asarray(z, dtype=float))
        n = len(y)
        rows = np.arange(n)
        s = out[rows, slot]
        loss = float(np.mean(self._bce(s, y)))

        e = np.exp(-np.abs(s))
        p = np.where(s >= 0, 1 / (1 + e), e / (1 + e))
        delta = np.zeros_like(out)
        delta[rows, slot] = (p - y) / n

        grads = {}
        for i in reversed(range(len(self.w))):
            grads[f"w{i}"] = delta.T @ acts[i]
            grads[f"b{i}"] = delta.sum(axis=0)
            if i > 0:
                delta = (delta @ self.w[i]) * (pres[i - 1] > 0)
        return loss, grads


class Adam:
    """Adam(Kingma & Ba) — 모수를 제자리에서 고친다."""

    def __init__(self, net, lr=1e-3, beta1=0.9, beta2=0.999, eps=1e-8):
        self.net = net
        self.lr, self.beta1, self.beta2, self.eps = lr, beta1, beta2, eps
        self.m = {name: np.zeros_like(p) for name, p in net.params()}
        self.v = {name: np.zeros_like(p) for name, p in net.params()}
        self.t = 0

    def step(self, grads):
        self.t += 1
        correct1 = 1 - self.beta1 ** self.t
        correct2 = 1 - self.beta2 ** self.t
        for name, p in self.net.params():
            g = grads[name]
            self.m[name] = self.beta1 * self.m[name] + (1 - self.beta1) * g
            self.v[name] = self.beta2 * self.v[name] + (1 - self.beta2) * g * g
            p -= self.lr * (self.m[name] / correct1) / (np.sqrt(self.v[name] / correct2) + self.eps)
