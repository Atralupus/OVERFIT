# 시작하기

## 1. 필요한 것

| | 왜 |
|---|---|
| [Godot 4.7.2 **mono**](https://godotengine.org/download) | C# 빌드에 mono 판이 필요하다. 일반 판으로는 안 된다 |
| [.NET SDK 8 이상](https://dotnet.microsoft.com/download) | 확인된 조합은 SDK 9.0.x 로 `net8.0` 을 빌드하는 것이다 |
| Python 3 | 커밋 훅(`tools/precommit_check.sh`)이 쓴다 |

Godot 을 기본 위치(`/Applications/Godot_mono.app`)가 아닌 곳에 뒀으면 `GODOT_PATH` 로 알려준다.

```bash
export GODOT_PATH=/path/to/Godot
```

## 2. 클론 직후

```bash
git clone git@github.com:Atralupus/Overfit.git
cd Overfit
tools/build.sh doctor             # 무엇이 없는지 알려준다
python3 tools/install_assets.py   # 받아둔 에셋 zip 을 제자리에 푼다 (셋 다 CC0)
tools/build.sh import             # ⚠ 반드시 한 번. .godot/ 캐시와 .uid 를 만든다
tools/build.sh run                # 게임 실행
```

**에셋 zip 은 먼저 손으로 받아야 한다.** itch.io 는 내려받기에 브라우저 세션이 필요해서
스크립트가 자동으로 못 가져온다 — 셋을 어디서 받는지는
[`overfit/assets/LICENSES.md`](overfit/assets/LICENSES.md) 에 표로 있다.
받아둔 곳이 `~/Downloads` 가 아니면 경로를 인자로 준다
(`python3 tools/install_assets.py ~/어디든`).

`import` 를 건너뛰면 `.uid` 가 없어 `check` 의 uid 단계가 빨개진다.
**생긴 `.uid` 는 커밋한다** — 없으면 체크아웃마다 UID 가 갈리고 엔진은 WARNING 으로만 알린다.

**에셋 원본(PNG)은 저장소에 없다** — 추적하는 것은 `.import` 와 `.tres` 뿐이다.
에셋을 건드린 커밋에서는 `git ls-files overfit | grep -c '\.png$'` 가 **0** 인지 본다.

## 3. 개발 루프

인자 없이 `tools/build.sh` 를 부르면 서브커맨드 전체 목록이 나온다 — **이 문서보다 그쪽이 최신이다.**

| 명령 | 무엇 | 걸리는 시간 |
|---|---|---|
| `tools/build.sh test` | 규칙 테스트. Godot 을 안 띄운다 | 초 |
| `tools/build.sh check` | 포맷 · 빌드 · 규칙 테스트 · uid. **커밋 게이트** | 초 |
| `tools/build.sh fix` | 포맷 자동 수정 | 초 |
| `tools/build.sh smoke` | 헤드리스 부팅 + 씬 순회. 엔진이 있어야만 보이는 것 | 십수 초 |
| `tools/build.sh run` | 창을 띄워 실행 | |
| `tools/build.sh editor` | Godot 에디터 | |

로그를 더 보려면 `LOG_LEVEL=trace tools/build.sh smoke`. 산출물은 전부 `out/` 으로 떨어진다(gitignore).

### 무엇을 언제 돌리나

- 규칙만 고쳤다 → `test`
- 커밋하기 전 → `check` (훅이 자동으로 부른다)
- 씬 · Autoload · Godot 쪽 코드를 고쳤다 → `smoke`. **`check` 는 엔진을 안 띄우므로 이걸 못 본다**

## 4. 작업 방식

`CLAUDE.md` 에 코드 규칙 · 로그 규약 · 검증 절차가 있다. 사람도 읽는 문서다.

요약하면 넷이다.

1. **main 에 직접 커밋하지 않는다.** 브랜치 → PR.
2. **규칙 코드는 TDD 로 고친다.** `tests/Overfit.Rules.Tests` 의 csproj 가 링크하는 파일이 그 범위다.
3. **규칙은 Godot 을 모른다.** 순수 C# 파일에 `using Godot;` 를 넣으면 테스트 어셈블리가 `CS0246` 으로 막는다.
4. **디버깅은 전부 로그로.** `[tag][레벨] key=value`, 에러는 `[E]`.

커밋 훅이 1·2 를 강제하고, 컴파일러가 3 을, 헤드리스 판정이 4 를 지킨다.
우회가 필요하면 커밋 메시지에 사유를 적는다 (`on-main:` · `no-test:`) — 사유 없는 우회는 막힌다.

## 5. Godot MCP

`.mcp.json` 에 [godot-mcp](https://github.com/Coding-Solo/godot-mcp) 가 등록돼 있다.
Claude Code 가 씬을 띄우고 디버그 출력을 읽는 데 쓴다. `GODOT_PATH` 가 거기에도 박혀 있으니
Godot 위치가 다르면 그 파일도 같이 고친다.

## 6. 저장소 구조

```
overfit/           Godot 프로젝트
  core/            Autoload 와 공용 뼈대. 순수 C# 과 Godot 경계층이 여기서 갈린다
  data/            모든 수치. 코드에 매직 넘버를 두지 않는다
  title/ play/     씬. 규칙을 담지 않고 그리기만 한다
  ui/theme/        전역 Theme 하나. 색·폰트·여백은 여기서만 정의한다
tests/Overfit.Rules.Tests/   규칙 테스트. overfit/ **바깥**이다 (csproj 주석 참조)
tools/             개발 루프(build.sh)와 커밋 훅
docs/              기획
out/               산출물. gitignore
```
