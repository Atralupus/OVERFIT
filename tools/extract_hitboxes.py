#!/usr/bin/env python3
"""판정 모양을 그림에서 뽑아 overfit/data/hitboxes.json 에 쓴다 (이슈 #59 · 설계 §3).

    python3 tools/extract_hitboxes.py           뽑아서 쓴다
    python3 tools/extract_hitboxes.py --check   뽑은 것이 파일과 같은지만 본다 (다르면 1)

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

배율은 balance.json 의 feel.boss_sprite_scale · fighter_sprite_scale 이다. 어느 팩이 보스이고
파이터인지는 bosses.json · fighters.json 의 sprite 가 말한다.

그림 원본(PNG)은 저장소에 없다 — install_assets.py 를 먼저 돌린다. 이 도구의 출력만 커밋한다.
"""

import argparse
import json
import math
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import install_assets  # noqa: E402 — PNG 판독기를 같이 쓴다 (Pillow 를 요구하지 않는다)

ROOT = pathlib.Path(__file__).resolve().parent.parent
DATA = ROOT / "overfit" / "data"
FRAMES = ROOT / "overfit" / "assets" / "spriteframes"
OUT = DATA / "hitboxes.json"

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


def extract(existing):
    """지금 파일의 키마다 다시 뽑는다. 메타 키(_로 시작)는 그대로 둔다."""
    source = existing["_source"]
    table = scales()
    out = {k: v for k, v in existing.items() if k.startswith("_")}
    for key in sorted(k for k in existing if not k.startswith("_")):
        pack, anim, frame = key.split("/")
        sub = atlas(pack).get(f"{anim}_{frame}")
        if sub is None:
            raise SystemExit(f"{key}: {pack}.tres 에 {anim}_{frame} 가 없다 — 애니메이션 이름은 .tres 의 이름이다")
        if pack not in table:
            raise SystemExit(f"{key}: {pack} 는 bosses.json · fighters.json 어느 sprite 도 아니다 — 배율을 모른다")
        rects = rects_of(sub["sheet"], sub["region"], table[pack], source)
        if not rects:
            raise SystemExit(f"{key}: 흰 픽셀이 없다 — 판정 프레임이 맞나")
        out[key] = {"scale": table[pack], "region": list(sub["region"]), "rects": rects}
    return out


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
    args = parser.parse_args()

    data = extract(read_json(OUT))
    text = dump(data)
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
