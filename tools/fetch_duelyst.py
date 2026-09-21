#!/usr/bin/env python3
"""Duelyst 애니메이션 스프라이트를 받는다 (CC0 1.0).

원본은 Open Duelyst 이고 Counterplay Games 가 2023-01-10 에 소스와 에셋 전부를
CC0 1.0 으로 공개했다 — 저작자 표시 의무도 없다. 여기서 받는 것은 그것을
Godot 4 SpriteFrames 로 재포장한 저장소다.

**원본 PNG 는 커밋하지 않는다.** 라이선스 때문이 아니라 저장소 위생 때문이다 —
한 번 들어간 바이너리는 히스토리에서 빠지지 않는다. `.import` · `.tres` 와
라이선스 파일만 추적한다.
"""

import argparse
import pathlib
import shutil
import subprocess
import sys
import tempfile

REPO = "https://github.com/Jordyfel/duelyst-animated-sprites-godot.git"
DEST = pathlib.Path(__file__).resolve().parent.parent / "overfit" / "addons" / "duelyst_animated_sprites"
# 원본 저장소는 spriteframes·spritesheets 를 저장소 루트가 아니라
# addons/duelyst_animated_sprites/ 아래에 둔다 — 그 접두어는 DEST 에 이미 있으니 벗겨낸다.
ADDON_PREFIX = "addons/duelyst_animated_sprites/"
WANT = [
    ADDON_PREFIX + "spriteframes/units",
    ADDON_PREFIX + "spritesheets/units",
    "LICENSE",
    "README.md",
]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dry-run", action="store_true", help="무엇이 복사될지만 보여준다")
    args = parser.parse_args()

    if shutil.which("git") is None:
        print("git 이 없습니다.", file=sys.stderr)
        return 1

    with tempfile.TemporaryDirectory() as tmp:
        print(f"얕은 clone: {REPO}")
        done = subprocess.run(
            ["git", "clone", "--depth", "1", REPO, tmp],
            capture_output=True, text=True, check=False)
        if done.returncode != 0:
            print(done.stderr, file=sys.stderr)
            return 1

        total = 0
        for rel in WANT:
            src = pathlib.Path(tmp) / rel
            if not src.exists():
                print(f"  ! 없음: {rel}")
                continue

            dst_rel = rel.removeprefix(ADDON_PREFIX)
            dst = DEST / ("duelyst-LICENSE" if rel == "LICENSE"
                          else "duelyst-README.md" if rel == "README.md" else dst_rel)
            if src.is_dir():
                count = sum(1 for _ in src.rglob("*") if _.is_file())
                print(f"  {rel}: {count}개 → {dst}")
                total += count
                if not args.dry_run:
                    shutil.copytree(src, dst, dirs_exist_ok=True)
            else:
                print(f"  {rel} → {dst}")
                total += 1
                if not args.dry_run:
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(src, dst)

        if total == 0:
            print("아무것도 못 받았습니다 — 업스트림 구조가 바뀌었을 수 있습니다.", file=sys.stderr)
            print(f"기대한 경로: {', '.join(WANT)}", file=sys.stderr)
            return 1

        verb = "복사(예정)" if args.dry_run else "복사"
        print(f"\n{total}개 {verb}. 다음: tools/build.sh import")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
