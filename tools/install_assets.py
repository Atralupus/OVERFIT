#!/usr/bin/env python3
"""받아둔 itch 팩 zip 을 제자리에 풀고, 시트에서 SpriteFrames 를 **다시 만든다**.

전임자(`fetch_duelyst.py`)는 git 저장소를 얕게 clone 했다. itch.io 는 그렇게 못 받는다 —
내려받기에 브라우저 세션이 필요해서 스크립트가 자동으로 가져올 방법이 없다. 그래서 이 스크립트는
"받아오는" 일을 사람에게 넘기고 **받아둔 zip 을 제자리에 놓는** 일만 한다.

받는 곳은 `overfit/assets/LICENSES.md` 에 적혀 있다.

## 왜 푸는 것과 .tres 만드는 것이 한 명령인가

`.tres`(SpriteFrames)는 시트 PNG 에서 **유도되는** 값이다 — 프레임이 몇 장인지도, 발이 어디
닿는지도 시트를 재야 나온다. 두 명령으로 나누면 시트를 갈아끼우고 `.tres` 를 안 고치는 날이
오는데, 그 어긋남은 아무것도 빨갛게 하지 않고 **그림만 조용히 밀린다**. 한 명령이면 생길 수 없다.

## 프레임 개수를 손으로 적지 않는다

프레임 폭만 팩마다 선언하고(아래 `frame_width`), 개수는 **시트 폭을 나눠** 얻는다.
나누어떨어지지 않으면 거기서 멈춘다 — 그건 프레임 폭이 틀렸다는 뜻이고, 조용히 반올림하면
모든 프레임이 조금씩 밀려 어긋난 그림이 나온다.

## 발을 바닥에 붙이는 자리도 재서 정한다

두 팩 다 캐릭터가 프레임 **위쪽**에 떠 있다 (Martial Hero 는 200px 프레임 안에서 발이 y=122).
`Offset = -높이/2` 로 바닥을 맞추는 뷰 코드를 그대로 두면 캐릭터가 공중에 뜬다. 그래서 여기서
팩 전체의 불투명 픽셀 범위를 재어 `region` 의 세로를 그만큼으로 **자른다** — 잘린 region 의
아래가 곧 발바닥이라 뷰는 아무것도 몰라도 된다.

PNG 는 표준 라이브러리만으로 읽는다 (Pillow 를 요구하지 않는다). 두 팩 다 8bit RGBA ·
비인터레이스라 필터 해제만 하면 알파를 볼 수 있다.

**원본 PNG 는 커밋하지 않는다.** 라이선스 때문이 아니라 저장소 위생 때문이다 — 넷 다 CC0 라
재배포해도 되지만, 한 번 들어간 바이너리는 히스토리에서 빠지지 않는다. `.import` 와 `.tres`,
그리고 팩에 동봉된 라이선스 텍스트만 추적한다.
"""

import argparse
import dataclasses
import pathlib
import struct
import sys
import zipfile
import zlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "overfit" / "assets"
FRAMES_DIR = ASSETS / "spriteframes"

# res:// 기준 경로. .tres 안의 ext_resource 가 이 접두어로 시트를 가리킨다.
RES_PREFIX = "res://assets"


@dataclasses.dataclass(frozen=True)
class Anim:
    """SpriteFrames 의 애니메이션 하나. 이름은 **뷰가 부르는 이름**이다.

    `FighterView` · `BossView` 가 `idle · run · attack · hit · death` 를 재생한다.
    없는 이름으로 `Play` 하면 엔진이 `ERROR:` 를 찍고 헤드리스 판정이 실패하므로,
    그 다섯은 반드시 여기 있어야 한다 (`REQUIRED_ANIMS` 가 확인한다).
    """

    name: str
    sheet: str
    fps: float
    loop: bool


@dataclasses.dataclass(frozen=True)
class Pack:
    """팩 하나. zip 이름 · 어디에 풀지 · 프레임 폭 · 애니메이션 목록."""

    zip_name: str
    dest: str
    # zip 멤버 경로의 **끝**으로 찾는다 → 내려받은 파일이 한 겹 더 감싸여 있어도 걸린다.
    members: dict
    frame_width: int = 0
    anims: tuple = ()
    # SpriteFrames 를 만들 팩만 id 를 갖는다 (배경은 안 만든다).
    sprite_id: str = ""


# 뷰가 실제로 `Play` 하는 이름. 캐릭터 팩에는 이 다섯이 반드시 있어야 한다.
REQUIRED_ANIMS = ("idle", "run", "attack", "hit", "death")

SPRITES = "Sprites/"

PACKS = (
    Pack(
        # 플레이어. `Take Hit - white silhouette` 이 **피격 흰 실루엣**이다 —
        # 셰이더나 modulate 로 흉내 내지 않고 작가가 그린 것을 그대로 쓴다.
        zip_name="Martial Hero.zip",
        dest="martial_hero",
        sprite_id="martial_hero",
        frame_width=200,
        members={
            "License.txt": "License.txt",
            SPRITES + "Idle.png": "idle.png",
            SPRITES + "Run.png": "run.png",
            SPRITES + "Jump.png": "jump.png",
            SPRITES + "Fall.png": "fall.png",
            SPRITES + "Attack1.png": "attack1.png",
            SPRITES + "Attack2.png": "attack2.png",
            SPRITES + "Take Hit.png": "take-hit.png",
            SPRITES + "Take Hit - white silhouette.png": "take-hit-white.png",
            SPRITES + "Death.png": "death.png",
        },
        anims=(
            Anim("idle", "idle.png", 8, loop=True),
            Anim("run", "run.png", 12, loop=True),
            Anim("jump", "jump.png", 8, loop=False),
            Anim("fall", "fall.png", 8, loop=False),
            Anim("attack", "attack1.png", 12, loop=False),
            Anim("attack2", "attack2.png", 12, loop=False),
            Anim("hit", "take-hit.png", 10, loop=False),
            Anim("hit_white", "take-hit-white.png", 10, loop=False),
            Anim("death", "death.png", 8, loop=False),
        ),
    ),
    Pack(
        # 보스. **공격 모션이 셋이라** 골랐다 — 백장의 세 패턴(#28)에 하나씩 배정된다.
        # Medieval Warrior Pack 은 둘뿐이고 zip 안에 라이선스 파일도 없었다.
        zip_name="Medieval King Pack 2.zip",
        dest="medieval_king",
        sprite_id="medieval_king",
        frame_width=160,
        members={
            "License.txt": "License.txt",
            SPRITES + "Idle.png": "idle.png",
            SPRITES + "Run.png": "run.png",
            SPRITES + "Jump.png": "jump.png",
            SPRITES + "Fall.png": "fall.png",
            SPRITES + "Attack1.png": "attack1.png",
            SPRITES + "Attack2.png": "attack2.png",
            SPRITES + "Attack3.png": "attack3.png",
            SPRITES + "Take Hit.png": "take-hit.png",
            SPRITES + "Take Hit - white silhouette.png": "take-hit-white.png",
            SPRITES + "Death.png": "death.png",
        },
        anims=(
            Anim("idle", "idle.png", 8, loop=True),
            Anim("run", "run.png", 10, loop=True),
            Anim("jump", "jump.png", 8, loop=False),
            Anim("fall", "fall.png", 8, loop=False),
            Anim("attack", "attack1.png", 8, loop=False),
            Anim("attack2", "attack2.png", 8, loop=False),
            Anim("attack3", "attack3.png", 8, loop=False),
            Anim("hit", "take-hit.png", 10, loop=False),
            Anim("hit_white", "take-hit-white.png", 10, loop=False),
            Anim("death", "death.png", 8, loop=False),
        ),
    ),
    Pack(
        # 배경. 패럴랙스 세 겹만 쓴다 — 타일셋·소품·플레이어 스프라이트는 안 가져온다.
        zip_name="warped city files.zip",
        dest="warped_city",
        members={
            "ENVIRONMENT/background/skyline-a.png": "skyline-a.png",
            "ENVIRONMENT/background/buildings-bg.png": "buildings-bg.png",
            "ENVIRONMENT/background/near-buildings-bg.png": "near-buildings-bg.png",
        },
    ),
)


# ---------------------------------------------------------------- PNG 읽기
#
# 표준 라이브러리만 쓴다. Pillow 를 요구하면 클론 직후 절차가 pip 하나만큼 길어지는데,
# 여기서 필요한 것은 "알파가 있는 행이 어디부터 어디까지인가" 하나뿐이다.


def png_rgba(path):
    """(폭, 높이, 행 목록). 행은 필터를 푼 RGBA 바이트다.

    판독기가 하나인 이유: 이 파일(발 높이를 재 region 을 자른다)과 extract_hitboxes.py(판정 모양을 뽑는다)가
    같은 픽셀을 봐야 판정이 그려지는 자리와 맞는다. 판독기가 둘이면 한쪽만 고쳐지는 날이 온다.
    """
    data = path.read_bytes()
    if data[:8] != b"\x89PNG\r\n\x1a\x0a":
        raise ValueError(f"PNG 가 아닙니다: {path}")

    width = height = depth = color = interlace = 0
    pixels = bytearray()
    pos = 8
    while pos + 8 <= len(data):
        length = int.from_bytes(data[pos:pos + 4], "big")
        kind = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + length]
        if kind == b"IHDR":
            width, height, depth, color, _, _, interlace = struct.unpack(">IIBBBBB", body)
        elif kind == b"IDAT":
            pixels += body
        elif kind == b"IEND":
            break
        pos += 12 + length

    if (depth, color, interlace) != (8, 6, 0):
        raise ValueError(
            f"{path}: 8bit RGBA · 비인터레이스만 읽습니다 "
            f"(depth={depth} color={color} interlace={interlace}). "
            "팩이 바뀌었다면 이 판독기를 늘리세요 — 조용히 건너뛰면 발 높이가 틀어집니다.")

    raw = zlib.decompress(bytes(pixels))
    bpp, stride = 4, width * 4
    prev = bytearray(stride)
    rows = []
    at = 0
    for _ in range(height):
        kind = raw[at]
        at += 1
        line = bytearray(raw[at:at + stride])
        at += stride
        _unfilter(kind, line, prev, bpp, stride)
        rows.append(line)
        prev = line

    return width, height, rows


def png_alpha_rows(path):
    """(폭, 높이, 알파가 있는 첫 행, 마지막 행). 전부 투명하면 두 행은 None 이다."""
    width, height, rows = png_rgba(path)
    first = last = None
    for y, line in enumerate(rows):
        if any(line[3::4]):
            first = y if first is None else first
            last = y
    return width, height, first, last


def _unfilter(kind, line, prev, bpp, stride):
    """PNG 행 필터 해제 (RFC 2083 §6). 자리에서 고친다."""
    if kind == 0:
        return
    if kind == 1:
        for i in range(bpp, stride):
            line[i] = (line[i] + line[i - bpp]) & 0xFF
    elif kind == 2:
        for i in range(stride):
            line[i] = (line[i] + prev[i]) & 0xFF
    elif kind == 3:
        for i in range(stride):
            left = line[i - bpp] if i >= bpp else 0
            line[i] = (line[i] + ((left + prev[i]) >> 1)) & 0xFF
    elif kind == 4:
        for i in range(stride):
            left = line[i - bpp] if i >= bpp else 0
            up = prev[i]
            upleft = prev[i - bpp] if i >= bpp else 0
            guess = left + up - upleft
            da, db, dc = abs(guess - left), abs(guess - up), abs(guess - upleft)
            pick = left if da <= db and da <= dc else (up if db <= dc else upleft)
            line[i] = (line[i] + pick) & 0xFF
    else:
        raise ValueError(f"모르는 PNG 행 필터: {kind}")


# ---------------------------------------------------------------- SpriteFrames 짓기


def measure(pack, dest):
    """팩의 시트를 전부 재서 (애니메이션별 프레임 수, 팩 전체의 세로 잘라낼 자리)."""
    counts = {}
    tops, bottoms = [], []
    for anim in pack.anims:
        sheet = dest / anim.sheet
        width, height, first, last = png_alpha_rows(sheet)
        if width % pack.frame_width:
            raise ValueError(
                f"{sheet.name}: 폭 {width} 가 프레임 폭 {pack.frame_width} 로 나누어떨어지지 않습니다. "
                "프레임 폭 선언이 틀렸습니다 — 반올림하면 모든 프레임이 밀립니다.")
        if first is None:
            raise ValueError(f"{sheet.name}: 전부 투명합니다.")
        counts[anim.name] = width // pack.frame_width
        tops.append(first)
        bottoms.append(last)
        del height

    # 팩 **전체**의 범위로 자른다. 애니메이션마다 따로 자르면 바닥선이 애니메이션마다 달라져
    # idle→run 에서 캐릭터가 위아래로 튄다.
    return counts, min(tops), max(bottoms) + 1


def write_spriteframes(pack, dest, out, dry_run):
    """`.tres` 하나를 짓는다. 내용이 시트에서 유도되므로 같은 시트면 같은 파일이다."""
    for name in REQUIRED_ANIMS:
        if name not in {a.name for a in pack.anims}:
            raise ValueError(
                f"{pack.sprite_id}: 뷰가 부르는 애니메이션 '{name}' 이 없습니다. "
                "없는 이름으로 Play 하면 엔진 ERROR: 가 나고 헤드리스 판정이 실패합니다.")

    counts, top, bottom = measure(pack, dest)
    tall = bottom - top

    sheets = []
    for anim in pack.anims:
        if anim.sheet not in sheets:
            sheets.append(anim.sheet)

    lines = []
    steps = len(sheets) + sum(counts.values())
    lines.append(f'[gd_resource type="SpriteFrames" load_steps={steps + 1} format=3]')
    lines.append("")
    lines.append("; tools/install_assets.py 가 시트에서 만든다. 손으로 고치지 마세요 —")
    lines.append("; region 의 세로는 팩 전체의 불투명 범위이고, 그 아래가 곧 발바닥입니다.")
    lines.append(f"; 프레임 {pack.frame_width}px · 잘라낸 세로 {top}..{bottom} ({tall}px)")
    lines.append("")
    for sheet in sheets:
        lines.append(
            f'[ext_resource type="Texture2D" '
            f'path="{RES_PREFIX}/{pack.dest}/{sheet}" id="{_sheet_id(sheet)}"]')
    lines.append("")

    for anim in pack.anims:
        for i in range(counts[anim.name]):
            lines.append(f'[sub_resource type="AtlasTexture" id="{anim.name}_{i}"]')
            lines.append(f'atlas = ExtResource("{_sheet_id(anim.sheet)}")')
            lines.append(
                f"region = Rect2({i * pack.frame_width}, {top}, {pack.frame_width}, {tall})")
            lines.append("")

    lines.append("[resource]")
    blocks = []
    for anim in pack.anims:
        frames = ", ".join(
            '{\n"duration": 1.0,\n"texture": SubResource("%s_%d")\n}' % (anim.name, i)
            for i in range(counts[anim.name]))
        blocks.append(
            '{\n"frames": [%s],\n"loop": %s,\n"name": &"%s",\n"speed": %s\n}'
            % (frames, "true" if anim.loop else "false", anim.name, float(anim.fps)))
    lines.append("animations = [" + ", ".join(blocks) + "]")

    text = "\n".join(lines) + "\n"
    total = sum(counts.values())
    print(f"  {out.relative_to(ROOT)} — 애니메이션 {len(pack.anims)}개 · 프레임 {total}장"
          f" · region 세로 {tall}px")
    for anim in pack.anims:
        print(f"      {anim.name:<10} {counts[anim.name]}장 @ {anim.fps:g}fps"
              f"{' (반복)' if anim.loop else ''}")
    if not dry_run:
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(text, encoding="utf-8")


def _sheet_id(sheet):
    return "s_" + sheet.removesuffix(".png").replace("-", "_")


# ---------------------------------------------------------------- 풀기


def find_zip(source, name):
    """받아둔 zip 을 찾는다. 하위 폴더까지 본다 — 브라우저가 어디에 떨궜는지는 사람마다 다르다."""
    direct = source / name
    if direct.is_file():
        return direct
    found = sorted(source.rglob(name))
    return found[0] if found else None


def unpack(pack, source, dry_run):
    """zip 하나를 푼다. 넣은 파일 수를 돌려준다."""
    archive = find_zip(source, pack.zip_name)
    if archive is None:
        print(f"  ! 못 찾음: {pack.zip_name} (찾은 곳: {source})")
        return 0

    dest = ASSETS / pack.dest
    print(f"  {archive} → {dest.relative_to(ROOT)}")

    taken = 0
    with zipfile.ZipFile(archive) as zf:
        names = [n for n in zf.namelist()
                 if not n.startswith("__MACOSX/") and not n.endswith("/")]
        for want, as_name in pack.members.items():
            hits = [n for n in names if n.endswith(want)]
            if not hits:
                print(f"    ! zip 안에 없음: {want}")
                continue
            body = zf.read(hits[0])
            print(f"    {as_name} ({len(body):,} 바이트)")
            taken += 1
            if not dry_run:
                dest.mkdir(parents=True, exist_ok=True)
                (dest / as_name).write_bytes(body)

    return taken


def main():
    parser = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "source", nargs="?", default=str(pathlib.Path.home() / "Downloads"),
        help="받아둔 zip 이 있는 폴더 (하위 폴더까지 찾는다). 기본값 ~/Downloads")
    parser.add_argument("--dry-run", action="store_true", help="무엇이 어디로 갈지만 보여준다")
    args = parser.parse_args()

    source = pathlib.Path(args.source).expanduser()
    if not source.is_dir():
        print(f"그런 폴더가 없습니다: {source}", file=sys.stderr)
        return 1

    print(f"받아둔 zip 을 찾는 곳: {source}")
    total = 0
    unpacked = []
    for pack in PACKS:
        taken = unpack(pack, source, args.dry_run)
        total += taken
        if taken:
            unpacked.append(pack)

    if total == 0:
        print("\n아무것도 설치하지 못했습니다.", file=sys.stderr)
        print("기대한 zip: " + " · ".join(p.zip_name for p in PACKS), file=sys.stderr)
        print("받는 곳은 overfit/assets/LICENSES.md 에 적혀 있습니다.", file=sys.stderr)
        return 1

    print("\nSpriteFrames")
    for pack in unpacked:
        if not pack.sprite_id:
            continue
        if args.dry_run and not (ASSETS / pack.dest / pack.anims[0].sheet).is_file():
            print(f"  {pack.sprite_id}.tres — 시트가 아직 없어 건너뜁니다 (--dry-run)")
            continue
        write_spriteframes(
            pack, ASSETS / pack.dest, FRAMES_DIR / f"{pack.sprite_id}.tres", args.dry_run)

    verb = "설치(예정)" if args.dry_run else "설치"
    print(f"\n{total}개 파일 {verb}. 다음: tools/build.sh import")
    print("⚠ PNG 는 커밋하지 않습니다 — git ls-files overfit | grep -c '\\.png$' 가 0 이어야 합니다.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
