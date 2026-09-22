# 에셋 출처

**쓰는 것은 하나다.**

| 에셋 | 라이선스 | 폴더 | 받는 곳 |
|---|---|---|---|
| Duelyst animated sprites (Godot 4) | **CC0 1.0** | `../addons/duelyst_animated_sprites/` | https://github.com/Jordyfel/duelyst-animated-sprites-godot |

원본은 [Open Duelyst](https://github.com/open-duelyst/duelyst) 이고, Counterplay Games 가
2023-01-10 에 소스와 **에셋 전부**를 CC0 1.0 으로 공개했다 — "no strings attached".
**저작자 표시 의무도 없다.** 애드온 저장소의 `duelyst-LICENSE` 가 CC0 1.0 전문이다.

유닛 696종(보스 전용 50종), 애니메이션은 `idle · breathing · run · attack · hit · death`.

## 쓰는 유닛

| 역할 | id |
|---|---|
| 캐릭터 · 단검 | `neutral_mercdaggerkiri` |
| 캐릭터 · 중검 | `f1_warblade` |
| 캐릭터 · 대검 | `f4_underworldbrute` |
| 보스 | `boss_*` 중 하나 (눈으로 보고 정한다) |

## 없는 것

**대시 · 패리 애니메이션이 없다.** 게임의 핵심 동사 둘에 전용 그림이 없다.
이펙트로 대신한다 — 대시는 `run` + 잔상, 패리는 `hit` 프레임 정지 + 히트스톱.
2D 액션에서 이 둘의 피드백은 애니메이션이 아니라 이펙트·히트스톱이 결정한다.

696종이 전부 같은 세트를 갖지는 않는다 — 실측 `idle 695 · attack 693 · death 691 · breathing 690 · hit 682 · run 668`.
**없는 이름으로 `Play` 하면 엔진이 `ERROR:` 를 찍고 그건 헤드리스 판정을 실패시킨다.**
뷰는 이름을 그대로 믿지 말고 `SpriteFrames.HasAnimation` 으로 확인하고 없으면 로그만 남기고 넘어가야 한다.

## 배경은 에셋이 아니다

아레나 배경(하늘 · 기둥 · 안개 · 바닥)은 `ui/Backdrop.tscn` 과 `battle/Battle.tscn` 이
**그라디언트와 사각형으로 그린다.** 그림을 하나 더 들이면 이 파일이 그만큼 길어지는데,
이 파일이 저장소의 라이선스 주장 전체가 서 있는 자리다. 단색을 벗어나는 데는 그릴 수 있는 것으로 충분했다.

## 안 쓰는 것과 그 이유

- **CraftPix** — 탑다운 4방향이라 가로 시점에 안 맞는다. 라이선스도 항목마다 다시 확인해야 한다.
- **Tiny Swords** — 상업적 사용은 가능하나 재배포 금지 조건이 있고, 탑다운 지형이다.
- **Kenney (CC0) · OFL 폰트** — 깨끗하다. UI·글꼴이 필요해지는 시점에 추가한다.

## 원본은 커밋하지 않는다

`.import` · `.tres` 와 라이선스 파일만 추적한다. **라이선스 때문이 아니라 저장소 위생 때문이다** —
CC0 라 재배포해도 되지만, 한 번 들어간 바이너리는 히스토리에서 빠지지 않는다.
받는 법은 `python3 tools/fetch_duelyst.py`.
