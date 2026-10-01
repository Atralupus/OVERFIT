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


def test_pool_takes_latest_and_spread_older() -> None:
    # 셀프 플레이의 상대 묶음(설계 2026-10-01 조각6 §3) — 최신 셋 + 그 앞에서 고르게 둘.
    from ml.rl import selfplay
    snaps = [Path(f"s{i:02d}") for i in range(10)]
    got = [p.name for p in selfplay.pool(snaps, latest=3, spread=2)]
    assert got == ["s00", "s06", "s07", "s08", "s09"], got
    assert [p.name for p in selfplay.pool(snaps[:2], latest=3, spread=2)] == ["s00", "s01"]
    assert selfplay.pool([], latest=3, spread=2) == []


def test_selfplay_one_round_runs() -> None:
    # 스펙 §5 — 셀프 플레이 한 라운드가 끝까지 돈다(최종 리뷰가 빠진 것을 짚었다). 판 수를 줄인 설정으로 바퀴 하나씩.
    import json
    import sys

    from ml.rl import selfplay
    config = json.loads((train.ROOT / "ml" / "rl" / "train.json").read_text(encoding="utf-8"))
    config["selfplay"].update({"boss_episodes": 4, "fighter_episodes": 2})
    with tempfile.TemporaryDirectory() as tmp:
        cfg_path = Path(tmp) / "train.json"
        cfg_path.write_text(json.dumps(config), encoding="utf-8")
        name = f"test-{Path(tmp).name}"
        argv = sys.argv
        sys.argv = ["selfplay", "--rounds=1", "--iterations=1", f"--name={name}", f"--config={cfg_path}"]
        try:
            selfplay.main()
        finally:
            sys.argv = argv
        out = train.ROOT / "out" / "selfplay" / name
        try:
            assert (out / "boss_r01.json").exists() and (out / "fighter_r01.json").exists(), "라운드 저장본이 없다"
            rows = (out / "metrics.csv").read_text(encoding="utf-8").strip().splitlines()
            assert len(rows) == 3, rows
        finally:
            import shutil
            shutil.rmtree(out, ignore_errors=True)


def main() -> None:
    test_refuses_folder_with_old_checkpoints()
    test_checkpoints_dense_early_then_sparse()
    test_pool_takes_latest_and_spread_older()
    test_selfplay_one_round_runs()
    print("ok")


if __name__ == "__main__":
    main()
