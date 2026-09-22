# 에셋 출처

## 목록은 여기 없다 — [`../data/credits.json`](../data/credits.json) 에 있다

팩 이름 · 만든 사람 · 링크 · 라이선스 · 그 라이선스를 **어디서 읽었는지**가 전부 그 파일 하나에 있고,
게임의 크레딧 화면(`credits/Credits.tscn`)이 같은 파일을 읽어 자기를 짓는다.

**왜 여기 표를 두지 않는가.** 같은 목록을 문서와 화면에 각각 적으면 팩이 바뀌는 날 한쪽만 고쳐진다.
그 어긋남은 아무 테스트도 빨갛게 하지 않고 라이선스 주장만 조용히 거짓이 된다 —
이 저장소는 같은 종류의 어긋남에 이미 여러 번 물렸다(태그와 기하 · 없는 애니메이션 이름 · 49 와 50).

생성기를 두지 않고 **가리키기만** 하는 이유도 같다. 생성기는 도구 하나와 게이트 한 줄을 더 요구하는데
그렇게 해서 얻는 것은 표 하나이고, 가리키면 중복이 **애초에 생길 수 없다.**

> 규칙: `license` 는 **받은 파일 안의 라이선스를 읽고** 적는다. 스토어 페이지 문구는 근거로 치지 않는다 —
> 페이지는 조용히 고쳐지지만 받은 zip 안의 한 줄은 그대로 남는다. 읽은 자리는 각 항목의 `note` 에 적혀 있다.

이 문서에 남는 것은 **목록이 아닌 것**뿐이다: 어떻게 받는지 · 무엇이 없는지 · 무엇을 왜 안 쓰는지 ·
원본을 왜 커밋하지 않는지.

## 받는 법 — 손으로 받고, 스크립트가 제자리에 놓는다

itch.io 는 내려받기에 브라우저 세션이 필요해서 **스크립트가 자동으로 못 가져온다.**
전임자(`fetch_duelyst.py`)가 git 저장소를 얕게 clone 할 수 있었던 것은 그쪽이 GitHub 였기 때문이다.

세 개를 받는다. 셋 다 무료이고 zip 안에 라이선스 파일이 동봉돼 있다.

| 받을 것 | 어디서 | 파일 이름 |
|---|---|---|
| 플레이어 | https://luizmelo.itch.io/martial-hero | `Martial Hero.zip` |
| 보스 | https://luizmelo.itch.io/medieval-king-pack-2 | `Medieval King Pack 2.zip` |
| 배경 | https://ansimuz.itch.io/warped-city | `warped city files.zip` |

받아둔 채로:

```bash
python3 tools/install_assets.py             # ~/Downloads 아래를 뒤진다
python3 tools/install_assets.py ~/어디든     # 다른 곳에 받았으면
python3 tools/install_assets.py --dry-run   # 무엇이 어디로 갈지만 본다
tools/build.sh import                       # ⚠ 반드시 한 번
```

스크립트는 푸는 것으로 끝나지 않고 **SpriteFrames(`.tres`)를 시트에서 다시 만든다.**
프레임 개수는 시트 폭을 나눠 얻고(나누어떨어지지 않으면 거기서 멈춘다), `region` 의 세로는
팩 전체의 불투명 범위로 잘라 **잘린 아래끝이 곧 발바닥**이 되게 한다 — 두 팩 다 캐릭터가
프레임 위쪽에 떠 있어서(Martial Hero 는 200px 프레임 안에서 발이 y=122) 이 처리를 안 하면
캐릭터가 공중에 뜬다. 뷰 코드는 그 사실을 몰라도 된다.

## 보스가 Medieval King Pack 2 인 이유

**공격 모션이 셋**이라서다 (`Attack1 · Attack2 · Attack3`). 백장의 세 패턴([#28](https://github.com/Atralupus/OVERFIT/issues/28))에
하나씩 배정된다. 후보였던 Medieval Warrior Pack 은 공격이 둘뿐이고, zip 안에 라이선스 파일도 없었다
(itch 페이지만 CC0 라고 적고 있는데, 그건 위의 규칙상 근거가 아니다).

## 피격 흰 섬광은 에셋에 이미 있다

두 팩 다 `Take Hit - white silhouette.png` 를 준다 — [#28](https://github.com/Atralupus/OVERFIT/issues/28) 이 요청한
"보스가 피격 시 흰색으로 빛나는" 연출 그 자체다. 셰이더나 `modulate` 로 흉내 내지 않는다:
작가가 그린 실루엣이라 몸 모양이 정확히 맞는다. `.tres` 에 `hit_white` 라는 이름으로 들어 있다.

## 없는 것

**대시 · 패리 애니메이션이 없다.** 게임의 핵심 동사 둘에 전용 그림이 없다.
이펙트로 대신한다 — 대시는 `run` + 잔상, 패리는 `hit` 프레임 정지 + 히트스톱.
2D 액션에서 이 둘의 피드백은 애니메이션이 아니라 이펙트·히트스톱이 결정한다.

**캐릭터가 하나뿐이다.** `fighters.json` 의 셋(단검 · 중검 · 대검)이 전부 같은 `martial_hero` 를 가리킨다.
Martial Hero 팩에 캐릭터가 하나라서다. 지금 화면에 서는 것은 `balance.json` 이 가리키는 하나뿐이라
당장 문제가 되지 않지만, 3택이 돌아오면 팩이 더 필요하다.

**없는 이름으로 `Play` 하면 엔진이 `ERROR:` 를 찍고 그건 헤드리스 판정을 실패시킨다.**
뷰는 이름을 그대로 믿지 말고 `SpriteFrames.HasAnimation` 으로 확인하고 없으면 로그만 남기고 넘어가야 한다.
만드는 쪽에서도 막는다 — `install_assets.py` 의 `REQUIRED_ANIMS` 가 뷰가 부르는 다섯
(`idle · run · attack · hit · death`)이 없으면 `.tres` 를 안 만들고 멈춘다.

## 배경은 이제 에셋이다

아레나 배경은 `ui/Backdrop.tscn` 이 **그라디언트와 사각형으로 그리던** 것이었다.
이슈 #26 에서 Warped City 의 패럴랙스 세 겹이 그 자리를 가져갔다.

**세 겹 다 `modulate` 로 눌러 뒀다** (하늘 0.42 · 먼 건물 0.30 · 가까운 건물 0.24).
원본은 네온 간판이 박힌 밝은 그림이라 그대로 두면 싸우는 둘을 삼킨다 — 손으로 그리던 배경이
0.02~0.06 밝기였던 것과 같은 이유다. 바닥(`battle/Battle.tscn` 의 `World/Ground`)은 그대로 그린다:
그쪽은 World 안이라 싸우는 둘과 **같이 흔들려야** 한다.

## 안 쓰는 것과 그 이유

- **FREE_Samurai 2D Pixel Art** — zip 안 `License.txt` 가 CC0 가 **아니다**(에셋으로 재배포 금지).
  우리는 원본을 커밋하지 않으니 그 조항과 충돌하지는 않지만, 무료판은 애니메이션이 넷뿐이라
  보스로 못 쓴다. 라이선스 종류를 CC0 하나로 유지하는 값이 더 크다.
- **Medieval Warrior Pack (· Version 1.2)** — 공격이 둘뿐이고 zip 안에 라이선스 파일이 없다.
- **CraftPix** — 탑다운 4방향이라 가로 시점에 안 맞는다. 라이선스도 항목마다 다시 확인해야 한다.
- **Tiny Swords** — 상업적 사용은 가능하나 재배포 금지 조건이 있고, 탑다운 지형이다.
- **Kenney (CC0) · OFL 폰트** — 깨끗하다. UI·글꼴이 필요해지는 시점에 추가한다.

## 원본은 커밋하지 않는다

`.import` · `.tres` 와 팩에 동봉된 라이선스 텍스트만 추적한다. **라이선스 때문이 아니라
저장소 위생 때문이다** — 셋 다 CC0 라 재배포해도 되지만, 한 번 들어간 바이너리는 히스토리에서 빠지지 않는다.

`.gitignore` 가 `overfit/assets/**/*.png` 로 막고, 커밋 전에 이것이 **0** 인지 본다:

```bash
git ls-files overfit | grep -c '\.png$'
```
