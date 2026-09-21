# 에셋 출처

**쓰는 것은 하나다.**

| 에셋 | 라이선스 | 폴더 | 받는 곳 |
|---|---|---|---|
| Duelyst animated sprites (Godot 4) | **CC0 1.0** | `../addons/duelyst_animated_sprites/` | https://github.com/Jordyfel/duelyst-animated-sprites-godot |

원본은 [Open Duelyst](https://github.com/open-duelyst/duelyst) 이고, Counterplay Games 가
2023-01-10 에 소스와 **에셋 전부**를 CC0 1.0 으로 공개했다 — "no strings attached".
**저작자 표시 의무도 없다.** 애드온 저장소의 `duelyst-LICENSE` 가 CC0 1.0 전문이다.

유닛 696종(보스 전용 50종), 애니메이션은 `idle · breathing · walk · attack · hit · dead`.

## 쓰는 유닛

| 역할 | id |
|---|---|
| 캐릭터 · 단검 | `neutral_mercdaggerkiri` |
| 캐릭터 · 중검 | `f1_warblade` |
| 캐릭터 · 대검 | `f4_underworldbrute` |
| 보스 | `boss_*` 중 하나 (눈으로 보고 정한다) |

## 없는 것

**대시 · 패리 애니메이션이 없다.** 게임의 핵심 동사 둘에 전용 그림이 없다.
이펙트로 대신한다 — 대시는 `walk` + 잔상, 패리는 `hit` 프레임 정지 + 히트스톱.
2D 액션에서 이 둘의 피드백은 애니메이션이 아니라 이펙트·히트스톱이 결정한다.

## 안 쓰는 것과 그 이유

- **CraftPix** — 탑다운 4방향이라 가로 시점에 안 맞는다. 라이선스도 항목마다 다시 확인해야 한다.
- **Tiny Swords** — 상업적 사용은 가능하나 재배포 금지 조건이 있고, 탑다운 지형이다.
- **Kenney (CC0) · OFL 폰트** — 깨끗하다. UI·글꼴이 필요해지는 시점에 추가한다.

## 원본은 커밋하지 않는다

`.import` 와 `.tres` 만 추적한다. **라이선스 때문이 아니라 저장소 위생 때문이다** —
CC0 라 재배포해도 되지만, 한 번 들어간 바이너리는 히스토리에서 빠지지 않는다.
받는 법은 `python3 tools/fetch_duelyst.py`.
