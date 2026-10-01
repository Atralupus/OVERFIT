"""학습기의 출력 폴더 · 체크포인트 일정 (설계 2026-10-01 조각5 §0 · 최종 리뷰). 실행: ml/.venv/bin/python -m ml.rl.test_train"""

from __future__ import annotations

import tempfile
from pathlib import Path

from ml.rl import train


def test_refuses_folder_with_old_checkpoints() -> None:
    # 같은 이름으로 다시 돌리면 옛 판의 체크포인트가 새 것 옆에 남아, 페이즈 고르기가 다른 판의 것을 고른다(최종 리뷰).
    with tempfile.TemporaryDirectory() as tmp:
        out = Path(tmp) / "boss"
        train.prepare_out(out, force=False)
        (out / "ckpt_0010.json").write_text("{}", encoding="utf-8")
        try:
            train.prepare_out(out, force=False)
        except FileExistsError:
            pass
        else:
            raise AssertionError("옛 체크포인트가 있는 폴더를 받았다")
        train.prepare_out(out, force=True)
        assert not list(out.glob("ckpt_*.json")), "force 가 옛 체크포인트를 안 지웠다"


def test_checkpoints_dense_early_then_sparse() -> None:
    # 초반에 정책이 빨리 바뀌므로(엔트로피가 20 바퀴 안에 무너졌다) 처음 dense_until 바퀴는 바퀴마다 남긴다.
    saved = [it for it in range(1, 61) if train.is_checkpoint(it, every=10, dense_until=20)]
    assert saved == list(range(1, 21)) + [30, 40, 50, 60], saved


def main() -> None:
    test_refuses_folder_with_old_checkpoints()
    test_checkpoints_dense_early_then_sparse()
    print("ok")


if __name__ == "__main__":
    main()
