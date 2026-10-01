"""학습의 일꾼이 쓴 경험을 넘파이로 읽는다 (설계 2026-10-01 조각4 §6).

steps.bin 의 한 줄(작은 끝): obs float32[D] · mask uint8[A] · action int16 · logp float32 · value float32 · reward float32 · done uint8 · span int32 ·
episode int32. span 은 결정의 길이(틱) — 학습기가 시간으로 할인한다.
D · A 는 manifest.json 에 있다. 일꾼을 부르는 것(run)도 여기다 — 학습기(조각 5)가 바퀴마다 부른다.
"""

from __future__ import annotations

import json
import subprocess
from dataclasses import dataclass
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]


def step_dtype(obs: int, actions: int) -> np.dtype:
    """한 줄의 구조체 dtype — 일꾼(RolloutWriter.Write)의 순서와 같다."""
    return np.dtype([
        ("obs", "<f4", (obs,)),
        ("mask", "u1", (actions,)),
        ("action", "<i2"),
        ("logp", "<f4"),
        ("value", "<f4"),
        ("reward", "<f4"),
        ("done", "u1"),
        ("span", "<i4"),
        ("episode", "<i4"),
    ])


@dataclass
class Rollout:
    """경험 한 묶음 — 배열은 줄 순서(판 순서 · 판 안의 결정 순서)다."""

    manifest: dict
    obs: np.ndarray
    mask: np.ndarray
    action: np.ndarray
    logp: np.ndarray
    value: np.ndarray
    reward: np.ndarray
    done: np.ndarray
    span: np.ndarray
    episode: np.ndarray


def read(out_dir: str | Path) -> Rollout:
    out = Path(out_dir)
    manifest = json.loads((out / "manifest.json").read_text(encoding="utf-8"))
    rows = np.fromfile(out / "steps.bin", dtype=step_dtype(manifest["obs"], manifest["actions"]))
    if len(rows) != manifest["rows"]:
        raise ValueError(f"{out}: steps.bin 의 줄 {len(rows)} 이 매니페스트의 {manifest['rows']} 와 다르다")
    return Rollout(
        manifest=manifest,
        obs=rows["obs"].astype(np.float64),
        mask=rows["mask"].astype(bool),
        action=rows["action"].astype(np.int64),
        logp=rows["logp"].astype(np.float64),
        value=rows["value"].astype(np.float64),
        reward=rows["reward"].astype(np.float64),
        done=rows["done"].astype(bool),
        span=rows["span"].astype(np.int64),
        episode=rows["episode"].astype(np.int64),
    )


def run(seed: int, episodes: int, out_dir: str | Path, weights: str | Path | None = None, threads: int | None = None) -> Rollout:
    """일꾼을 부르고 읽는다 — tools/build.sh rollout 그대로(빌드 · [E] 판정 포함)."""
    args = [str(ROOT / "tools" / "build.sh"), "rollout", f"--seed={seed}", f"--episodes={episodes}", f"--out={out_dir}",
            f"--weights={weights if weights else 'none'}"]
    if threads:
        args.append(f"--threads={threads}")
    subprocess.run(args, check=True, cwd=ROOT, stdout=subprocess.DEVNULL)
    return read(out_dir)
