#!/usr/bin/env python3
"""판정 모양을 그림에서 뽑아 overfit/data/hitboxes.json 에 쓴다 (이슈 #59 · 설계 §3).

    python3 tools/extract_hitboxes.py             뽑아서 쓴다
    python3 tools/extract_hitboxes.py --check     뽑은 것이 파일과 같은지만 본다 (다르면 1)
    python3 tools/extract_hitboxes.py --overlay   뽑은 사각형을 그림 위에 그려 out/hitbox_overlay/ 에 둔다
                                                  (--check 와 같이 쓰면 파일은 안 건드리고 그림만 그린다)

그림(PNG)이 안 깔린 체크아웃에서는 아무것도 안 하고 77 로 끝난다 — 실패(1)와 가른다. 게이트
(tools/build.sh check)가 77 만 경고로 넘긴다: 방금 클론한 사람은 볼 그림이 없을 뿐이지 무엇을 어긴 것이 아니다.

무엇을 뽑나 — hitboxes.json 의 **키가 곧 목록**이다. 키는 `팩/애니메이션/프레임` 이고
(예: `medieval_king/attack/2`), 새 판정 모양이 필요하면 키를 하나 더하고 이 도구를 돌린다.
애니메이션 이름은 **.tres 의 이름**이다 — 시트 파일 이름(attack1.png)이 아니라 뷰가 부르는 이름(attack).

어디를 기준으로 재나 — **.tres 의 region** 이다. install_assets.py 가 팩 전체의 불투명 범위로
세로를 잘라 두었고(그 아래끝이 곧 발바닥이다) 뷰는 그 region 을 그린다. 원본 프레임의 아래끝을
바닥으로 잡으면 보스는 33px, 파이터는 195px 어긋난 자리에 판정이 선다. 가로는 region 의 가운데가
몸 중심이다 (AnimatedSprite2D 는 가운데 정렬이다).

어떻게 뜨나 — region 을 cell_px 칸으로 나눠, 칸 안의 흰 픽셀(불투명도 > alpha_min, R·G·B 전부 > white_min)이
fill_min 이상이면 그 칸을 판정으로 친다. 같은 줄에서 이어진 칸은 가로로 합쳐 사각형 하나로 만든다.
네 값은 hitboxes.json 의 `_source` 에 있다 — 수치는 데이터에 둔다.

무엇이 궤적인가 — **순백(#FFFFFF)뿐이다.** white_min 254 는 "R·G·B 전부 255" 를 뜻한다. 그림에서 잰 값이다
(2026-09-25, 다섯 프레임 전부): 옛 기준(> 235)을 넘는 픽셀은 딱 두 색이고, 칼 궤적은 한 픽셀도 빠짐없이
#FFFFFF, 왕의 흰 털깃·수염은 한 픽셀도 빠짐없이 #F5EEEE(245, 238, 238) 다. 두 팩 다 궤적에 안티앨리어싱이 없다.
옛 기준은 둘을 같이 셌고, 그래서 attack3 f2 의 초승달 안쪽 — 공중에서 보스를 끌어안으면 안 맞아야 하는
자리 — 에 수염이 사각형 [44, 88, 126.5, 148.5] 을 세웠다(attack f2 의 두 줄이 한 칸씩 안으로 당겨진 것과
attack2 f2 의 [-352, -132] 끝 한 칸도 같은 털이다). 다른 거름은 재 보니 안 됐다:
  · 작은 덩어리 버리기(3칸 미만) — martial_hero attack f4 의 한 칸짜리 꼬리 [-10, 0, 12.5, 22.5] 까지 버린다.
  · 가장 큰 덩어리만 — medieval_king attack f2 에서 머리 뒤로 따로 도는 호를 버린다.
  · 배경(투명)에 닿는 덩어리만 — attack2 f2 의 털깃은 배경에 닿고, attack f2 의 털은 궤적에 붙어 한 덩어리다.
팩을 바꾸면 궤적 색부터 잰다 — 순백이 아닌 궤적은 이 기준으로는 하나도 안 잡힌다("흰 픽셀이 없다" 로 멈춘다).

배율은 balance.json 의 feel.boss_sprite_scale · fighter_sprite_scale 이다. 어느 팩이 보스이고
파이터인지는 bosses.json · fighters.json 의 sprite 가 말한다.

그림 원본(PNG)은 저장소에 없다 — install_assets.py 를 먼저 돌린다. 이 도구의 출력만 커밋한다.

눈으로 본다 — --overlay 는 키마다 region 을 확대해 그 위에 사각형 테두리를 그린다(하늘색 세로줄이 몸 중심).
사각형이 궤적 말고 다른 그림(몸 · 옷 · 털)을 덮고 있으면 거기서 보인다. 그리는 사각형은 파일에 쓰는 바로
그 값을 월드 px 에서 region 픽셀로 **되돌린** 것이라, 좌표 변환이 틀려도 그림에서 어긋나 보인다.
"""

import argparse
import json
import math
import pathlib
import re
import struct
import sys
import zlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import install_assets  # noqa: E402 — PNG 판독기를 같이 쓴다 (Pillow 를 요구하지 않는다)

ROOT = pathlib.Path(__file__).resolve().parent.parent
DATA = ROOT / "overfit" / "data"
FRAMES = ROOT / "overfit" / "assets" / "spriteframes"
OUT = DATA / "hitboxes.json"

# 그림이 없어 볼 수 없다 — automake 테스트 관례의 '건너뜀' 번호다. 1(그림과 다르다 · 도구가 멈췄다)과 섞이면
# 게이트가 둘 중 하나를 잘못 다룬다: 없는 그림을 실패로 막거나, 진짜 어긋남을 경고로 넘긴다.
NO_ART = 77

# 겹쳐 그린 그림은 out/ 아래로만 떨어진다 (gitignore) — 그림 원본에서 유도한 것이라 커밋하지 않는다.
OVERLAY_DIR = ROOT / "out" / "hitbox_overlay"
OVERLAY_ZOOM = 4                  # 원본 1px → 4px. 지금 칸(cell_px 4)이면 한 칸이 16px 이라 눈에 들어온다
OVERLAY_BACK = (30, 30, 38)       # 투명 자리. 어둡게 깔아야 흰 궤적과 그 위의 테두리가 보인다
OVERLAY_RECT = (255, 48, 48)      # 사각형 테두리
OVERLAY_AXIS = (64, 200, 255)     # 몸 중심(가로 0) — 앞뒤를 읽는 기준

EXT_RE = re.compile(r'^\[ext_resource type="Texture2D" path="(?P<path>[^"]+)" id="(?P<id>[^"]+)"\]$')
SUB_RE = re.compile(r'^\[sub_resource type="AtlasTexture" id="(?P<id>[^"]+)"\]$')
ATLAS_RE = re.compile(r'^atlas = ExtResource\("(?P<id>[^"]+)"\)$')
REGION_RE = re.compile(r'^region = Rect2\((?P<x>[\d.]+), (?P<y>[\d.]+), (?P<w>[\d.]+), (?P<h>[\d.]+)\)$')


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def scales():
    """팩 이름 → 배율. 역할(보스 · 파이터)은 데이터의 sprite 가 정한다."""
    feel = read_json(DATA / "balance.json")["feel"]
    table = {}
    for key, boss in read_json(DATA / "bosses.json").items():
        if not key.startswith("_"):
            table[boss["sprite"]] = feel["boss_sprite_scale"]
    for key, fighter in read_json(DATA / "fighters.json").items():
        if not key.startswith("_"):
            table[fighter["sprite"]] = feel["fighter_sprite_scale"]
    return table


def atlas(pack):
    """.tres 를 읽어 `애니메이션_프레임` → {"sheet": 시트 경로, "region": (x, y, 폭, 높이)}."""
    ext, subs, current = {}, {}, None
    for line in (FRAMES / f"{pack}.tres").read_text(encoding="utf-8").splitlines():
        if m := EXT_RE.match(line):
            ext[m["id"]] = ROOT / "overfit" / m["path"].removeprefix("res://")
        elif m := SUB_RE.match(line):
            current = {}
            subs[m["id"]] = current
        elif current is not None and (m := ATLAS_RE.match(line)):
            current["sheet"] = ext[m["id"]]
        elif current is not None and (m := REGION_RE.match(line)):
            current["region"] = tuple(int(float(m[k])) for k in ("x", "y", "w", "h"))
    return subs


def rects_of(sheet, region, scale, source):
    """region 안의 흰 픽셀을 칸으로 떠서 줄마다 합친 사각형들 — 공격자 기준 월드 px."""
    cell = source["cell_px"]
    _, _, rows = install_assets.png_rgba(sheet)
    rx, ry, rw, rh = region
    cols = math.ceil(rw / cell)
    lines = math.ceil(rh / cell)

    def white(x, y):
        o = x * 4
        r, g, b, a = rows[y][o:o + 4]
        return a > source["alpha_min"] and min(r, g, b) > source["white_min"]

    out = []
    for cy in range(lines):
        y_top, y_bot = cy * cell, min((cy + 1) * cell, rh)
        run = None
        for cx in range(cols + 1):   # 한 칸 더 — 줄 끝에서 달리던 것을 닫는다
            filled = False
            if cx < cols:
                x_left, x_right = cx * cell, min((cx + 1) * cell, rw)
                hits = sum(
                    white(rx + x, ry + y)
                    for y in range(y_top, y_bot) for x in range(x_left, x_right))
                filled = hits >= source["fill_min"] * (x_right - x_left) * (y_bot - y_top)
            if filled and run is None:
                run = cx
            elif not filled and run is not None:
                x0 = (run * cell - rw / 2) * scale
                x1 = (min(cx * cell, rw) - rw / 2) * scale
                y0 = (rh - y_bot) * scale
                y1 = (rh - y_top) * scale
                out.append([round(x0, 2), round(x1, 2), round(y0, 2), round(y1, 2)])
                run = None
    return out


def targets(existing):
    """지금 파일의 키마다 (.tres 의 칸, 배율). 그림을 읽기 전에 이름과 역할부터 맞춰 본다."""
    table = scales()
    found = {}
    for key in sorted(k for k in existing if not k.startswith("_")):
        pack, anim, frame = key.split("/")
        sub = atlas(pack).get(f"{anim}_{frame}")
        if sub is None:
            raise SystemExit(f"{key}: {pack}.tres 에 {anim}_{frame} 가 없다 — 애니메이션 이름은 .tres 의 이름이다")
        if pack not in table:
            raise SystemExit(f"{key}: {pack} 는 bosses.json · fighters.json 어느 sprite 도 아니다 — 배율을 모른다")
        found[key] = (sub, table[pack])
    return found


def extract(existing, found):
    """키마다 다시 뽑는다. 메타 키(_로 시작)는 그대로 둔다."""
    source = existing["_source"]
    out = {k: v for k, v in existing.items() if k.startswith("_")}
    for key, (sub, scale) in found.items():
        rects = rects_of(sub["sheet"], sub["region"], scale, source)
        if not rects:
            raise SystemExit(f"{key}: 흰 픽셀이 없다 — 판정 프레임이 맞나")
        out[key] = {"scale": scale, "region": list(sub["region"]), "rects": rects}
    return out


def overlay(key, entry, sheet):
    """region 을 확대해 깔고 그 위에 사각형 테두리를 그린다 → out/hitbox_overlay/<키의 / 를 _ 로>.png.

    사각형은 월드 px 에서 region 픽셀로 **되돌려** 그린다 — 뽑을 때(rects_of)의 역이다. 뽑은 칸을 그대로
    칠하면 좌표 변환(몸 중심 · 발바닥 · 배율)이 틀려도 그림에서는 멀쩡해 보인다.
    """
    _, _, rows = install_assets.png_rgba(sheet)
    rx, ry, rw, rh = entry["region"]
    scale, zoom = entry["scale"], OVERLAY_ZOOM
    width, height = rw * zoom, rh * zoom

    canvas = []
    for y in range(rh):
        line = bytearray()
        src = rows[ry + y]
        for x in range(rw):
            r, g, b, a = src[(rx + x) * 4:(rx + x) * 4 + 4]
            # 알파를 바닥 위에 섞는다. 지금 두 팩은 알파가 0 · 255 뿐이지만, 섞어 두면 반투명 팩이 와도 안 틀린다
            line += bytes((c * a + k * (255 - a)) // 255 for c, k in zip((r, g, b), OVERLAY_BACK)) * zoom
        canvas.extend(bytearray(line) for _ in range(zoom))

    def paint(x, y, color):
        if 0 <= x < width and 0 <= y < height:
            canvas[y][x * 3:x * 3 + 3] = bytes(color)

    center = round(rw / 2 * zoom)
    for y in range(height):
        paint(center, y, OVERLAY_AXIS)

    for x0, x1, y0, y1 in entry["rects"]:
        left = round((x0 / scale + rw / 2) * zoom)
        right = round((x1 / scale + rw / 2) * zoom) - 1
        top = round((rh - y1 / scale) * zoom)
        bottom = round((rh - y0 / scale) * zoom) - 1
        for x in range(left, right + 1):
            paint(x, top, OVERLAY_RECT)
            paint(x, bottom, OVERLAY_RECT)
        for y in range(top, bottom + 1):
            paint(left, y, OVERLAY_RECT)
            paint(right, y, OVERLAY_RECT)

    path = OVERLAY_DIR / f"{key.replace('/', '_')}.png"
    write_png(path, width, height, canvas)
    return path


def write_png(path, width, height, rows):
    """8bit RGB PNG 를 쓴다. 판독기(install_assets.png_rgba)와 같은 이유로 표준 라이브러리만 쓴다 — Pillow 를 요구하지 않는다."""
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + bytes(row) for row in rows)   # 행마다 필터 0(없음)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b"\x89PNG\r\n\x1a\n"
                     + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(raw, 9))
                     + chunk(b"IEND", b""))


def dump(data):
    """사람이 읽는 모양 — 사각형 한 줄에 하나. 같은 입력이면 같은 글자다."""
    lines = ["{"]
    items = list(data.items())
    for i, (key, value) in enumerate(items):
        comma = "," if i < len(items) - 1 else ""
        if key.startswith("_"):
            lines.append(f"  {json.dumps(key)}: {json.dumps(value, ensure_ascii=False)}{comma}")
            continue
        lines.append(f"  {json.dumps(key)}: {{")
        lines.append(f'    "scale": {value["scale"]}, "region": {json.dumps(value["region"])},')
        lines.append('    "rects": [')
        rects = value["rects"]
        for j, r in enumerate(rects):
            lines.append(f"      {json.dumps(r)}{',' if j < len(rects) - 1 else ''}")
        lines.append("    ]")
        lines.append(f"  }}{comma}")
    lines.append("}")
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="뽑은 것이 파일과 같은지만 본다")
    parser.add_argument("--overlay", action="store_true",
                        help="뽑은 사각형을 그림 위에 그려 out/hitbox_overlay/ 에 둔다")
    args = parser.parse_args()

    existing = read_json(OUT)
    found = targets(existing)
    missing = sorted({sub["sheet"] for sub, _ in found.values() if not sub["sheet"].is_file()})
    if missing:
        print("그림(PNG)이 없어 판정 모양을 볼 수 없다 — python3 tools/install_assets.py 를 먼저 돌려라")
        for sheet in missing:
            print(f"  없음: {sheet.relative_to(ROOT)}")
        return NO_ART

    data = extract(existing, found)
    text = dump(data)

    # 검사보다 먼저 그린다 — 파일과 그림이 갈렸을 때가 바로 그림을 봐야 할 때다.
    if args.overlay:
        for key, (sub, _) in found.items():
            print(f"  {overlay(key, data[key], sub['sheet']).relative_to(ROOT)}")

    if args.check:
        if OUT.read_text(encoding="utf-8") != text:
            print("hitboxes.json 이 그림과 다르다 — python3 tools/extract_hitboxes.py 를 돌려라")
            return 1
        print("hitboxes.json 이 그림과 같다")
        return 0

    OUT.write_text(text, encoding="utf-8")
    for key, value in data.items():
        if not key.startswith("_"):
            print(f"  {key:<26} 사각형 {len(value['rects']):3d}장 · 배율 {value['scale']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
