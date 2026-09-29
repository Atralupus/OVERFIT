#!/usr/bin/env bash
#
# 개발 루프와 빌드를 한 곳에서 돌린다.
#
#   tools/build.sh doctor              환경 점검 — 뭐가 없는지 알려준다
#   tools/build.sh check               포맷 검사 + 빌드 + 규칙 테스트 + .uid 짝 + 판정 모양. 커밋 전 게이트
#   tools/build.sh fix                 포맷 자동 수정
#   tools/build.sh build               C# 빌드만
#   tools/build.sh test [cover] [인자…] 규칙 테스트 (xUnit, tests/Overfit.Rules.Tests). Godot 을 안 띄운다 — 초 단위
#                                      발견 개수를 TRX 에서 읽어 인용한다. **0개면 실패다** — 러너가 빠지면
#                                      dotnet test 가 테스트 0개를 찾고 조용히 통과하기 때문이다
#                                      cover → 커버리지(cobertura) 까지. 나머지 인자는 dotnet test 로 그대로
#                                      (예: --filter FullyQualifiedName~Det)
#   tools/build.sh uids                .cs · .gdshader 마다 .uid 가 짝을 이루는지. check 가 부른다
#   tools/build.sh hitboxes            hitboxes.json 이 그림과 같은지 (extract_hitboxes.py --check). check 가 부른다
#                                      그림(PNG)이 안 깔린 체크아웃이면 경고하고 건너뛴다 — 실패가 아니다
#   tools/build.sh run [씬]            C# 빌드 후 게임 실행
#   tools/build.sh editor              에디터 실행
#   tools/build.sh import              에셋 임포트만 (헤드리스). 클론 직후 반드시 한 번
#   tools/build.sh smoke               헤드리스 부팅 + 씬 순회 (로그로 검증)
#   tools/build.sh demo [시드]         헤드리스로 전투 한 판 — 봇이 끝까지 돌린다 → [battle-demo][M]
#                                      시드는 시도 시드다(기본 51) — 게임 로그의 [run][I] attempt=… seed=X 를 그대로 넘기면
#                                      그 시도의 보스 순서가 되살아난다. 64비트 그대로 읽는다
#   tools/build.sh factory [인자…]     데이터 공장 — 봇 함대가 보스와 싸운 기록을 짓는다 → out/factory/<시드>-<from>-<to>/ (#108)
#                                      Godot 이 필요 없다(.NET 콘솔 · Release). 인자는 --help · 로그는 out/factory.log
#                                      예: tools/build.sh factory --fleet-seed=1 --from=0 --to=2000
#   tools/build.sh shots              창을 띄워 스크린샷 → out/shots/ · docs/shots/
#                                      엔진 안에서 뷰포트를 직접 찍는다 — 화면 기록 권한이 필요 없고 다른 창이 안 겹친다
#   tools/build.sh gifs [id…]          README 의 패턴별 GIF → docs/gifs/<id>.gif (기본: GifRunner.cs 의 대본 전부)
#                                      창과 Movie Maker 로 모든 프레임을 받고 대본이 남긴 구간만 ffmpeg 로 엮는다(480px · 15fps)
#                                      잡은 구간 4초 · 1MB 를 넘으면 실패다. ffmpeg 가 필요하다(brew install ffmpeg)
#   tools/build.sh export [프리셋]     플레이 가능한 빌드 → out/OVERFIT.app 과 out/OVERFIT-macos.zip (기본 프리셋 macOS)
#   LOG_LEVEL=trace tools/build.sh …   로그 레벨 지정 (trace|debug|info|warn|error)
#   HITBOXES=1 tools/build.sh run|shots  판정 보기 — Godot 의 Visible Collision Shapes 를 켠다(--debug-collisions).
#                                        규칙이 이 틱에 댄 판정 사각형이 그려진다. shots 는 그 사진을 docs/shots/ 로 안 넘긴다
#   tools/build.sh clean               빌드 산출물 삭제
#
# 로그 판정 — Godot 을 띄우는 서브커맨드 넷(smoke · demo · shots · gifs)은 판정 함수 하나(judge_headless)를 공유한다.
#   이름의 "headless" 는 처음 부른 곳(smoke)에서 왔다 — 창을 띄우는 shots · gifs 도 같은 로그 규약으로 판정한다.
#   ① 로그에 ^[tag][E] 가 있으면 실패 (CLAUDE.md: Error = 규칙 위반)
#   ①′ 엔진이 찍은 ERROR: 블록이 있으면 실패 — C# 예외는 여기로만 나온다. WARNING: 은 세기만 한다
#      에셋(res://assets/) 자원 로딩 실패만 면제하고(그림은 저장소에 없다) 면제 건수를 경고로 찍는다
#   ② 완료 표지([tag][M])가 없으면 실패 — 게임이 끝까지 못 갔다
#   ③ 표지 있고 종료 코드 0 → 통과      ④ 표지 있고 코드 != 0 → 실패
# 완료 표지는 core/Log.Marker 가 내므로 LOG_LEVEL 과 무관하다. 내용 검사(expect_log)만 그 줄이 안 찍히는 레벨에서 건너뛴다.
#
# 산출물은 전부 out/ 아래로 떨어진다 (gitignore 됨).

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/overfit"
SLN="$PROJECT/Overfit.sln"
OUT="$ROOT/out"

# 규칙 테스트. overfit/ **바깥**이다 — 안에 두면 Godot.NET.Sdk 의 기본 glob 이
# 테스트 소스를 게임 어셈블리에 컴파일해 넣는다. 규칙 파일은 옮기지 않고 csproj 가 링크만 한다.
TEST_DIR="$ROOT/tests/Overfit.Rules.Tests"
TEST_PROJ="$TEST_DIR/Overfit.Rules.Tests.csproj"

# 데이터 공장 (#108). 규칙 파일을 링크하는 .NET 콘솔이다 — 솔루션에 들어 있어 check 의 포맷 · 빌드가 같이 덮는다.
FACTORY_DIR="$ROOT/tools/factory"
FACTORY_PROJ="$FACTORY_DIR/Overfit.Factory.csproj"


GODOT="${GODOT_PATH:-${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}}"

# LOG_LEVEL=trace|debug|info|warn|error 를 게임의 --log-level 유저 인자로 넘긴다. 비어 있으면 게임 기본값(디버그 빌드 debug).
LOG_ARG=""
[[ -n "${LOG_LEVEL:-}" ]] && LOG_ARG="--log-level=${LOG_LEVEL}"

# 판정 보기 (이슈 #59 · 설계 §6.1). 실행 중에는 못 켜므로(SceneTree.debug_collisions_hint) 띄울 때 정한다.
HITBOX_ARG=""
[[ "${HITBOXES:-}" == "1" ]] && HITBOX_ARG="--debug-collisions"

# 세션 시드 (#72 · 설계 §4.4). 게임은 켤 때마다 벽시계로 세션 시드를 뽑는다. Game 을 타는 두 명령(smoke · shots)은
# 실행마다 같은 판이어야 로그와 사진이 같은 것을 가리키므로 51 로 고정한다 — 시도마다 시드는 여전히 다르다.
SESSION_ARG="--session-seed=51"

# 버전은 Godot 이 알려주는 값에서 뽑는다. 하드코딩하면 업그레이드 때 조용히 어긋난다.
godot_version() { "$GODOT" --version 2>/dev/null | tail -1 | tr -d '\r'; }

say()  { printf '\033[1m%s\033[0m\n' "$*"; }
ok()   { printf '  \033[32m✓\033[0m %s\n' "$*"; }
bad()  { printf '  \033[31m✗\033[0m %s\n' "$*"; }
warn() { printf '  \033[33m!\033[0m %s\n' "$*"; }
die()  { printf '\033[31m%s\033[0m\n' "$*" >&2; exit 1; }

need_godot() {
  [[ -x "$GODOT" ]] || die "Godot 을 못 찾았습니다: $GODOT
GODOT_PATH 환경변수로 경로를 알려주세요."
}

# ---------------------------------------------------------------- 헤드리스 판정
#
# 엔진 진단(ERROR:/WARNING:) 블록을 한 줄씩 뱉는다. 머리줄 + 뒤따르는 들여쓴 줄을 ` ⏎ ` 로 이어 붙인다.
# 줄이 아니라 **블록**으로 보는 이유: 예외 종류와 첫 스택 프레임이 같은 줄에 있어야
# "이 메서드에서 난 이 예외"를 단위로 면제하거나 막을 수 있다.
#   engine_diag_blocks <로그파일>
engine_diag_blocks() {
  awk '
    function flush() { if (open) { print buf }; open = 0; buf = "" }
    /^(ERROR|WARNING|SCRIPT ERROR|USER ERROR|USER WARNING|USER SCRIPT ERROR):/ { flush(); buf = $0; open = 1; next }
    open && /^[[:space:]]/ { line = $0; sub(/^[[:space:]]+/, "", line); buf = buf " ⏎ " line; next }
    { flush() }
    END { flush() }
  ' "$1"
}

# 에셋이 없는 체크아웃 면제 목록.
#
# 그림 파일(PNG)은 저장소에 없다 — tools/install_assets.py 가 받아둔 zip 을 푼다. 풀기 전 체크아웃에서는
# .tres 는 읽히는데 그것이 가리키는 텍스처가 없어 엔진이 자원 로딩 ERROR 를 쏟는다.
# 그건 우리 코드의 버그가 아니라 **환경**이라, 방금 클론한 사람이 smoke 부터 막히지 않게 면제한다.
#
# ⚠ **좁게 유지한다.** 에셋 경로(res://assets/)와 그 임포트 캐시의 자원 로딩 실패뿐이다.
#   C# 예외 패턴을 여기 넣지 마라 — 엔진이 파일을 못 읽는 것은 환경이지만,
#   그 결과로 생긴 null 을 우리 코드가 건드리는 것은 우리 버그다. 그 둘은 같이 묻히면 안 된다.
#
# 세 가지를 동시에 못박는다. 전에는 경로 문자열 하나만 봤는데, 판정이 **블록** 단위라
# (머리줄 + 들여쓴 줄을 ⏎ 로 이어 붙인다) 그 경로를 어딘가에서 **언급하기만 해도** 면제됐다:
#   ① ^ERROR: — 엔진 자신의 진단만이다. SCRIPT ERROR: 는 관리 코드 예외가 나오는 자리라
#     절대 면제될 수 없어야 한다. 앵커가 없으면 이어 붙인 블록 어디에 ERROR 가 있어도 걸렸다.
#   ② 로더 문구 — 엔진이 "자원을 못 열었다" 고 말한 것만. 우리 버그로 난 진단이 같은 경로를
#     스치기만 한 경우(예: 타입이 어긋난 .tres 를 GD.Load<SpriteFrames> 한 결과)는 안 걸린다.
#   ③ [^⏎]* — 경로가 **머리줄 안에** 있어야 한다. 뒤에 이어 붙은 스택 프레임의 경로로는 못 빠진다.
_JUDGE_ASSET_ABSENT_ALLOW='^ERROR: (Failed loading resource|Unable to open file|Cannot open file|No loader found for resource|Error loading resource)[^⏎]*res://(assets/|\.godot/imported/)'
# 엔진은 같은 사실을 두 층에서 말한다. 로더가 "못 읽었다" 고 찍기 전에, 텍스트 자원 **파서**가
# ".tres 6번째 줄의 ext_resource 가 없는 파일을 가리킨다" 고 먼저 찍는다 — 그 줄은 로더 문구로
# 시작하지 않고 경로로 시작한다. **가리켜지는 쪽은 반드시 res://assets/** 이고, 가리키는 쪽은
# 그림을 직접 다는 두 자리(SpriteFrames .tres · 배경 씬 ui/Backdrop.tscn)로 못박는다 —
# 아무나 가리켜도 되게 두면 우리 씬의 진짜 깨진 참조가 같이 초록이 된다.
_JUDGE_ASSET_ABSENT_ALLOW+='|^ERROR: res://(assets/|ui/Backdrop\.tscn)[^⏎]*Parse Error: \[ext_resource\] referenced non-existent resource at: res://assets/'

#   judge_headless <무엇을 돌렸나> <로그파일> <완료 표지> <종료 코드> [의도된 에러 정규식]
#
# ⚠ **여기에 C# 예외를 면제로 넣지 마라.** 예외는 어디서 나든 우리 코드의 버그다.
#   한 번 면제를 만들면 그 자리의 진짜 버그도 같이 초록이 된다.
judge_headless() {
  local what="$1" log="$2" marker="$3" code="$4" allow="${5:-}"

  [[ -f "$log" ]] || die "$what: 로그 파일이 없습니다 — $log"

  # ① 에러 로그. 앵커(^)가 중요하다 — 인용문 안의 [E] 를 에러로 오인하지 않는다.
  local errs
  errs="$(grep -E '^\[[a-z]+\]\[E\]' "$log" || true)"
  [[ -n "$allow" ]] && errs="$(grep -vE "$allow" <<< "$errs" || true)"
  if [[ -n "$errs" ]]; then
    bad "$what: 에러 로그 $(grep -c . <<< "$errs")줄 — [E] 는 규칙 위반이다. 전체 로그: $log"
    head -5 <<< "$errs" | sed 's/^/      /'
    exit 1
  fi

  # ①′ 엔진 진단. C# 예외는 여기로만 나온다 — 우리 로그 형식(^[tag][E])으로는 안 찍힌다.
  local blocks engine warns
  blocks="$(engine_diag_blocks "$log")"
  engine="$(grep -E '^(ERROR|SCRIPT ERROR|USER ERROR|USER SCRIPT ERROR):' <<< "$blocks" || true)"
  warns="$(grep -cE '^(WARNING|USER WARNING):' <<< "$blocks" || true)"
  [[ -n "$allow" ]] && engine="$(grep -vE "$allow" <<< "$engine" || true)"

  # 에셋 없는 체크아웃 면제. **몇 건을 봐줬는지 반드시 찍는다** — 안 찍으면 그림이 없는 실행과
  # 멀쩡한 실행이 똑같이 초록으로 보이고, "왜 아무것도 안 보이지" 를 로그에서 찾을 수 없다.
  local exempted=0
  if [[ -n "$engine" ]]; then
    exempted="$(grep -cE "$_JUDGE_ASSET_ABSENT_ALLOW" <<< "$engine" || true)"
    engine="$(grep -vE "$_JUDGE_ASSET_ABSENT_ALLOW" <<< "$engine" || true)"
  fi
  [[ "$exempted" -gt 0 ]] && warn "$what: 에셋 없는 체크아웃으로 보아 엔진 ERROR ${exempted}건 면제 (tools/install_assets.py 를 안 돌린 상태다)"

  if [[ -n "$engine" ]]; then
    bad "$what: 엔진 ERROR $(grep -c . <<< "$engine")건 — C# 예외거나 엔진이 규칙 위반을 본 것이다. 전체 로그: $log"
    head -5 <<< "$engine" | cut -c1-400 | sed 's/^/      /'
    exit 1
  fi

  # WARNING 은 막지 않는다. 우리 자신의 [W] 도 판정을 막지 않는데 엔진 경고만 막으면 기준이 어긋난다.
  [[ "$warns" -gt 0 ]] && warn "$what: 엔진 WARNING ${warns}건 (막지 않는다)"

  # ② 완료 표지
  grep -qF "$marker" "$log" || die "$what: 완료 표지가 없습니다 — \"$marker\" (끝까지 못 갔다). 전체 로그: $log"

  # ③④ 종료 코드
  [[ "$code" -eq 0 ]] || die "$what: 완료 표지는 있지만 Godot 종료 코드가 $code 입니다. 전체 로그: $log"
}

# LOG_LEVEL 을 숫자로. 비어 있으면 게임 기본값(디버그 빌드 = debug)을 따른다.
log_level_num() {
  case "${1:-}" in
    trace) echo 0 ;;
    debug|"") echo 1 ;;
    info) echo 2 ;;
    warn) echo 3 ;;
    error) echo 4 ;;
    off) echo 5 ;;
    *) echo 1 ;;
  esac
}

# 로그 **내용** 검사. judge_headless 가 "끝까지 갔나"를 보는 것과 달리 이건 "무엇이 일어났나"를 본다.
#
#   expect_log <로그파일> <그 줄의 레벨> <ERE> <실패 메시지>
#
# 완료 표지는 [M] 이라 레벨과 무관하지만 내용 줄은 아니다 — LOG_LEVEL 이 그 줄의 레벨보다 높으면
# 줄이 아예 안 찍히므로 검사를 건너뛴다(경고만 남긴다). 안 그러면 `LOG_LEVEL=warn` 이 거짓 실패를 낸다.
expect_log() {
  local log="$1" level="$2" re="$3" msg="$4"
  if [[ "$(log_level_num "${LOG_LEVEL:-}")" -gt "$(log_level_num "$level")" ]]; then
    warn "건너뜀 (LOG_LEVEL=${LOG_LEVEL:-} > $level): $msg"
    return 0
  fi
  grep -qE "$re" "$log" || die "$msg 전체 로그: $log"
}

# ---------------------------------------------------------------- doctor

cmd_doctor() {
  say "환경 점검"
  local missing=0

  if [[ -x "$GODOT" ]]; then ok "Godot $(godot_version)"
  else bad "Godot 없음 — $GODOT"; missing=1; fi

  if command -v dotnet >/dev/null; then ok ".NET SDK $(dotnet --version)"
  else bad ".NET SDK 없음 — https://dotnet.microsoft.com/download"; missing=1; fi

  if command -v python3 >/dev/null; then ok "Python $(python3 --version 2>&1 | cut -d' ' -f2) (커밋 훅이 쓴다)"
  else warn "python3 없음 — tools/precommit_check.sh 가 안 돈다"; fi

  echo
  if [[ $missing -eq 0 ]]; then say "개발 루프에 필요한 것은 다 있습니다."
  else say "위 ✗ 를 채우세요."; fi
}

# ---------------------------------------------------------------- 개발 루프

cmd_build() {
  say "C# 빌드"
  dotnet build "$SLN" -v minimal
}

cmd_fix() {
  say "포맷 수정"
  dotnet format "$SLN"
  ok "정리했습니다."
}

cmd_test() {
  local build=1 cover=0
  local extra=()
  while [[ $# -gt 0 ]]; do
    case "$1" in
      cover|--cover) cover=1; shift ;;
      --no-build)    build=0; shift ;;
      *)             extra+=("$1"); shift ;;
    esac
  done

  command -v dotnet >/dev/null || die "dotnet 이 없습니다. tools/build.sh doctor 를 보세요."
  [[ -f "$TEST_PROJ" ]] || die "테스트 프로젝트가 없습니다: $TEST_PROJ"

  if [[ $build -eq 1 ]]; then
    say "테스트 빌드"
    dotnet build "$TEST_PROJ" -v minimal || die "테스트 빌드 실패."
  fi

  say "규칙 테스트 (dotnet test$([[ $cover -eq 1 ]] && echo ' + 커버리지'))"
  local results="$OUT/TestResults"
  rm -rf "$results"
  mkdir -p "$results"

  local args=(test "$TEST_PROJ" --no-build -v minimal
              --logger "trx;LogFileName=results.trx" --results-directory "$results")
  [[ $cover -eq 1 ]] && args+=(--collect "XPlat Code Coverage")
  [[ ${#extra[@]} -gt 0 ]] && args+=("${extra[@]}")

  local code=0
  dotnet "${args[@]}" || code=$?

  # 몇 개가 실제로 발견됐나. xunit.runner.visualstudio 가 빠지면 dotnet test 는 테스트 0개를 찾고
  # **조용히 통과한다** — 종료 코드만 보면 초록불인데 아무것도 안 돈 상태다. TRX 에서 직접 센다.
  local trx="$results/results.trx" total=""
  if [[ -f "$trx" ]]; then
    total="$(sed -n 's/.*<Counters[^>]*total="\([0-9]*\)".*/\1/p' "$trx" | head -1)"
  fi

  [[ $code -eq 0 ]] || die "규칙 테스트 실패 (발견 ${total:-?}건). 결과: $trx"
  [[ -n "$total" ]] || die "테스트 결과(TRX)를 읽지 못했습니다 — $trx"
  [[ "$total" -gt 0 ]] || die "테스트가 0건 발견됐습니다. 러너(xunit.runner.visualstudio)가 빠졌는지 보세요 — 0건은 조용한 통과라 실패로 칩니다."

  ok "규칙 테스트 통과 — ${total}건"
  [[ $cover -eq 1 ]] && ok "커버리지: $(find "$results" -name 'coverage.cobertura.xml' | head -1)"
  return 0
}

# .cs · .gdshader 마다 .uid 가 짝을 이루는지. 빠지면 체크아웃마다 UID 가 갈리고 엔진은 WARNING 으로만 알린다 —
# .tscn 이 uid:// 로 스크립트를 가리키는 순간 그게 조용한 깨짐이 된다.
# .uid 는 Godot 이 임포트할 때 만든다. 없으면 tools/build.sh import 를 한 번 돌린다.
# Godot 4.7 은 제 안에 uid 를 적을 자리가 없는 텍스트 자원에 옆 파일(.uid)을 만든다 — 스크립트와 셰이더다. 이 저장소의 첫
# 셰이더(battle/view/hit_flash.gdshader · #71)를 임포트했더니 hit_flash.gdshader.uid 가 생겼다. .tres · .tscn 은 uid 를 제 머리에 적는다.
cmd_uids() {
  local missing=0 f
  while IFS= read -r f; do
    [[ -f "$f.uid" ]] && continue
    bad "uid 없음 — ${f#"$ROOT"/}"
    missing=1
  done < <(find "$PROJECT" \( -name '*.cs' -o -name '*.gdshader' \) \
    -not -path "*/.godot/*" -not -path "*/obj/*" -not -path "*/bin/*" -not -path "*/addons/*" | sort)

  if [[ $missing -eq 1 ]]; then
    die "위 파일에 .uid 짝이 없습니다. tools/build.sh import 를 돌리고 생긴 .uid 를 같이 커밋하세요."
  fi
  ok ".uid 짝 — 전부 있음"
}

# hitboxes.json 이 그림과 같은지 (이슈 #59). 판정 모양은 도구가 그림에서 뽑아 쓰는 값이라, 그림이나 뽑는 기준
# (_source)이 바뀌었는데 도구를 안 돌리면 판정과 그림이 **조용히** 갈린다 — 아무 테스트도 PNG 를 못 보기 때문이다.
# CLAUDE.md §3 의 자리다: 데이터의 불변식은 cmd_check 에 한 줄로 붙인다.
#
# 그림은 저장소에 없다. 방금 클론한 체크아웃은 **볼 그림이 없을 뿐** 무엇을 어긴 것이 아니므로 경고로 건너뛴다 —
# 도구가 그 경우만 77 로 따로 알린다. 그 밖의 실패(그림과 다르다 · 도구가 멈췄다)는 그대로 막는다.
cmd_hitboxes() {
  command -v python3 >/dev/null || { warn "python3 없음 — 판정 모양 검사를 건너뜁니다"; return 0; }
  local out code=0
  out="$(python3 "$ROOT/tools/extract_hitboxes.py" --check 2>&1)" || code=$?
  case "$code" in
    0)  ok "$out" ;;
    77) warn "판정 모양 — 그림(PNG)이 없는 체크아웃이라 건너뜁니다"
        sed 's/^/      /' <<< "$out" ;;
    *)  sed 's/^/      /' <<< "$out"
        die "판정 모양이 그림과 다르거나 도구가 멈췄습니다 (위 출력). 다르면 python3 tools/extract_hitboxes.py --overlay 로 다시 뽑고 겹친 그림을 보세요." ;;
  esac
}

cmd_check() {
  say "포맷 검사"
  if ! dotnet format "$SLN" --verify-no-changes --no-restore; then
    echo
    die "포맷이 어긋났습니다. tools/build.sh fix 로 고치세요."
  fi
  ok "포맷 통과"

  say "빌드"
  dotnet build "$SLN" -v minimal --no-restore || die "빌드 실패."
  ok "빌드 통과"

  # 규칙 테스트. 위의 `dotnet build "$SLN"` 이 테스트 프로젝트까지 이미 빌드했으므로 --no-build 로 재사용한다.
  # Godot 을 안 띄우므로 게이트에 붙일 만큼 싸다.
  # 느린 테스트가 생기면 여기에 --filter "speed!=slow" 를 붙인다. 그때는 **전체를 도는 자리**를
  # 따로 두어야 한다 — 필터를 준 실행은 부분집합이라 "다 돌았다"를 단언할 수 없다.
  cmd_test --no-build

  say ".uid 짝"
  cmd_uids

  say "판정 모양 (hitboxes.json ↔ 그림)"
  cmd_hitboxes
}

cmd_run() {
  need_godot
  # CLI 실행은 C# 을 자동으로 빌드하지 않는다. 안 하면 옛 어셈블리로 돈다.
  cmd_build
  say "실행"
  if [[ $# -gt 0 ]]; then "$GODOT" --path "$PROJECT" $HITBOX_ARG "$1" -- $LOG_ARG
  else "$GODOT" --path "$PROJECT" $HITBOX_ARG -- $LOG_ARG; fi
}

cmd_editor() { need_godot; "$GODOT" --path "$PROJECT" --editor; }

cmd_import() {
  need_godot
  say "임포트"
  local start; start="$(date +%s)"
  "$GODOT" --headless --path "$PROJECT" --import 2>&1 | grep -vE "^\[ *[0-9]+% \]" || true
  ok "임포트 완료 ($(( $(date +%s) - start ))초)"
}

# 부팅 → 씬 순회 → 종료. 창 없이 라우팅과 부팅 로그를 확인한다.
# 엔진이 있어야만 보이는 것을 본다: 씬이 뜨는지 · 스크립트가 붙는지 · C# 예외가 나는지.
cmd_smoke() {
  need_godot
  cmd_build
  say "스모크 (헤드리스)"
  # 헤드리스는 실시간 동기화가 없어 프레임이 폭주한다. --fixed-fps 로 1프레임=1/60초를 고정해야 타이머가 프레임 수와 맞는다.
  mkdir -p "$OUT"
  local log="$OUT/smoke.log" code=0
  "$GODOT" --headless --fixed-fps 60 --path "$PROJECT" --quit-after 3600 -- --tour $SESSION_ARG $LOG_ARG > "$log" 2>&1 || code=$?
  grep -E "^\[(boot|data|scene|tour)\]" "$log" || true
  judge_headless "스모크" "$log" "tour=done" "$code"
  # 순회가 정말 씬을 갈아끼웠나. 표지만 보면 "돌지 않고 끝난" 경우를 못 본다.
  expect_log "$log" info '^\[data\]\[I\] loaded ' "balance.json 을 읽은 흔적이 없습니다."
  expect_log "$log" info '^\[scene\]\[I\] goto=Play$' "Play 씬으로 간 흔적이 없습니다."
  expect_log "$log" info '^\[scene\]\[I\] play ready$' "Play 씬의 스크립트가 안 붙었습니다."
  expect_log "$log" info '^\[scene\]\[I\] goto=Battle$' "Battle 씬으로 간 흔적이 없습니다."
  # 단계까지 본다. "battle ready" 만 보면 단계 진행이 통째로 빠져도 초록이다 —
  # 그 값은 Autoload 가 들고 있어서 씬만 떠서는 증명되지 않는다.
  expect_log "$log" info '^\[scene\]\[I\] battle ready stage=[0-9]+ fighter=' "Battle 씬의 스크립트가 안 붙었거나 단계를 못 읽었습니다."
  # 시도마다 시드 (#72 · 설계 §4.4). 세션 시드 51 에서 순회가 세운 두 전투가 시도 1 · 2 이고, 시드는
  # Hash64(51, attempt, k1: 번호) 다 — RunHistoryTests 가 첫 값을 박아 뒀다. 시도 2 의 줄이 "전투가 설 때마다 시드가 바뀐다" 를 본다.
  expect_log "$log" info '^\[run\]\[I\] session_seed=51$' "세션 시드 51 이 안 넘어갔습니다 — 스모크가 실행마다 다른 판을 돕니다."
  # 대본 칸 (#96 · 설계 §4.4). 순회가 첫 전투에만 대본을 넣는다(Game._tourScript) — 첫 줄의 picker=script 가 칸이 전투에 닿은 것이고,
  # 둘째 줄의 picker=uniform 이 Battle 이 칸을 **가져가며 비운** 것이다(Game.TakeScript). 칸이 남으면 둘째 전투도 script 로 선다.
  expect_log "$log" info '^\[run\]\[I\] attempt=1 stage=1 seed=16800346292054821908 picker=script history=0$' "첫 전투가 시도 1 의 시드와 순회의 대본으로 안 섰습니다."
  # 둘째 전투 — 보스전이 하나라 같은 단계(1)이고 새 시도 · 새 시드다(설계 2026-09-29 조각1 §1). 첫 전투는 끝까지 안 가 기록이 없다(history=0).
  expect_log "$log" info '^\[run\]\[I\] attempt=2 stage=1 seed=9131751153949564229 picker=uniform history=0$' "둘째 전투가 새 시도를 안 열었거나 첫 전투의 대본이 남았습니다(TakeScript 가 칸을 안 비웠다)."
  # 크레딧 화면은 data/credits.json 을 읽어 스스로를 짓는다. 화면이 떴는지만 보면 목록이 통째로
  # 비어도 초록이므로, 몇 줄을 세웠는지까지 본다 — 라이선스 표시가 사라지는 것은 조용한 실패다.
  expect_log "$log" info '^\[scene\]\[I\] credits ready$' "크레딧 씬의 스크립트가 안 붙었습니다."
  expect_log "$log" debug '^\[credits\]\[D\] entries=[1-9][0-9]* ' "크레딧 화면이 항목을 하나도 못 세웠습니다."
  expect_log "$log" info '^\[scene\]\[I\] goto=Title$' "Title 로 돌아온 흔적이 없습니다."
  ok "스모크 통과 ($log)"
}

# 전투 한 판을 창 없이. 규칙 층만 돌린다 — 씬이 뜨는지는 smoke 가 본다.
cmd_demo() {
  need_godot
  cmd_build
  say "전투 데모 (헤드리스)"
  mkdir -p "$OUT"
  local seed="${1:-51}" log="$OUT/demo.log" code=0
  "$GODOT" --headless --fixed-fps 60 --path "$PROJECT" --quit-after 3600 \
    -- --battle-demo "--seed=$seed" $LOG_ARG ${EXTRA:-} > "$log" 2>&1 || code=$?
  grep -E "^\[(battle-demo|result|axes)\]" "$log" || true
  judge_headless "전투 데모" "$log" "battle-demo=done" "$code"
  # 판이 정말 돌았나. 표지만 보면 "아무것도 안 하고 끝난" 경우를 못 본다.
  expect_log "$log" info '^\[result\]\[I\] (win|lose) ' "승패 판정이 안 났습니다."
  expect_log "$log" info '^\[axes\]\[I\] samples=[1-9]' "회피 관측이 0건입니다 — 계측이 안 돌았습니다."
  ok "전투 데모 통과 ($log)"
}

# 데이터 공장 (#108 · 설계 2026-09-28 §4). 봇 함대가 보스전을 게임과 같은 순서로 치고(이길 때까지 · 많아야 --tries 번), 판의 사례(패턴이 선 것
# 하나)마다 라벨(맞았나)을 samples.csv 에 싣는다. 봇 · 매니페스트는 bots.csv · manifest.json. Release 로 빌드해 돈다 — 디버그의 규칙은
# 몇 배 느리다. 판정은 헤드리스와 같은 함수다(judge_headless): [E] 가 하나라도 있으면(규칙 위반) · 표지가 없으면(끝까지 못 갔다) ·
# 종료 코드가 0 이 아니면 실패다. 커밋을 매니페스트에 적는다 — 고친 채 돌렸으면 -dirty 가 붙는다.
cmd_factory() {
  run_factory "데이터 공장" "공장" "$OUT/factory.log" "factory=done" "$@"
  ok "공장 통과 ($OUT/factory.log)"
}

# 공장 콘솔을 Release 로 빌드해 저장소 뿌리에서 돌린다. 기본 경로(overfit/data · tools/factory/fleet.json ·
# out/…)가 뿌리에서 선다. 커밋을 매니페스트에 적는다 — 고친 채 돌렸으면 -dirty 가 붙는다.
run_factory() {
  local title="$1" name="$2" log="$3" marker="$4"
  shift 4
  command -v dotnet >/dev/null || die "dotnet 이 없습니다. tools/build.sh doctor 를 보세요."
  say "공장 빌드 (Release)"
  dotnet build "$FACTORY_PROJ" -c Release -v minimal || die "공장 빌드 실패."
  say "$title"
  mkdir -p "$OUT"
  local code=0 commit
  commit="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || echo unknown)"
  [[ -z "$(git -C "$ROOT" status --porcelain 2>/dev/null)" ]] || commit="$commit-dirty"
  (cd "$ROOT" && dotnet "$FACTORY_DIR/bin/Release/net8.0/Overfit.Factory.dll" "--commit=$commit" "$@") 2>&1 | tee "$log" || code=$?
  judge_headless "$name" "$log" "$marker" "$code"
}

# 익스포트 템플릿이 있는 폴더. 버전 문자열은 Godot 에게 물어본다 —
# 박아두면 엔진을 올릴 때 조용히 어긋나고, 그 어긋남은 "템플릿이 없다" 가 아니라
# "옛 템플릿으로 빌드됐다" 로 나타난다.
export_template_dir() {
  local version
  version="$(godot_version | sed 's/\.official\..*$//')"
  echo "$HOME/Library/Application Support/Godot/export_templates/$version"
}

# 플레이 가능한 빌드를 만든다.
#
#   tools/build.sh export [프리셋]     기본 macOS
#
# 템플릿이 없으면 **먼저 멈춘다.** Godot 은 템플릿 없이도 끝까지 가다가 실행되지 않는
# 껍데기를 남기는데, 그건 실패보다 나쁘다 — 산출물이 생겼으니 성공한 것처럼 보인다.
# 창을 띄워 스크린샷. 헤드리스로는 못 한다 — 뷰포트에 그려진 것이 없다.
cmd_shots() {
  need_godot
  cmd_build
  say "스크린샷"
  local out="$OUT/shots" log="$OUT/shots.log" code=0
  rm -rf "$out"
  mkdir -p "$out"
  "$GODOT" --path "$PROJECT" $HITBOX_ARG -- --shots "--shot-dir=$out" $SESSION_ARG $LOG_ARG > "$log" 2>&1 || code=$?
  grep -E "^\[(shots|shot)\]" "$log" || true
  judge_headless "스크린샷" "$log" "shots=done" "$code"

  local n
  n="$(find "$out" -name '*.png' | wc -l | tr -d ' ')"
  [[ "$n" -gt 0 ]] || die "스크린샷이 0장입니다 — 창이 안 떴거나 뷰포트가 비었습니다. 전체 로그: $log"

  # 판정이 그려진 사진은 디버그용이다 — README 가 쓰는 docs/shots/ 로 넘기지 않는다.
  if [[ -n "$HITBOX_ARG" ]]; then
    ok "스크린샷 ${n}장 — $out (판정 보기라 docs/shots/ 는 그대로 둔다)"
    return
  fi

  # 문서용 축소본. 원본은 창 크기(1280x720 — project.godot 의 window_*_override)라 README 에 그대로 넣으면 무겁다.
  mkdir -p "$ROOT/docs/shots"
  local f
  for f in "$out"/*.png; do
    sips -Z 960 "$f" --out "$ROOT/docs/shots/$(basename "$f")" >/dev/null 2>&1 || warn "축소 실패: $(basename "$f")"
  done

  ok "스크린샷 ${n}장 — $out (축소본 docs/shots/)"
}

# README 의 패턴별 GIF (#78 · 설계 §6.2). 대본(battle/debug/GifRunner.cs)마다 창을 띄워 Movie Maker(--write-movie)로 모든 프레임을 PNG 로
# 받는다 — 엔진이 모든 프레임을 고정 간격으로 쓰므로 빠지는 장이 없고, 같은 대본이면 장의 틱 · 로그 · 관측이 같다(화면 흔들림만 뷰의 난수
# GD.Randf 라 장마다 흔들린다 — 그래서 다시 찍으면 GIF 의 바이트가 조금 다르다). 러너가 로그로 남긴 잡을 구간
# ([gif][I] capture_from frame=… · capture_to frame=…)의 장만 ffmpeg 에 넘긴다: 판의 앞머리(걸어 들어오기)와 앞 패턴은 돌되 안 잡는다.
# 60fps → 15fps 로 솎고 · 가로 480px(Movie Maker 는 창 크기 1280×720 으로 받는다 — 그 3/8) · 팔레트를 떠서(바뀐 사각형만 싣는다)
# docs/gifs/<id>.gif 로 쓴다.
#
# 예산은 잡은 구간에 건다 — 4초(60fps 로 240장) · 1MB. 넘으면 실패다: README 가 무거워지는 것을 조용히 두지 않는다. 판정 보기와 섞지
# 않는다(HITBOXES=1 이면 거절한다) — 채운 사각형이 README 에 실린다. 세션 시드는 51 이다(smoke · shots 와 같은 이유 — 대본은 패턴을
# 고정하지만 스크린샷처럼 실행마다 같은 판이어야 한다).
#
# 대본 목록은 GifRunner.cs 의 표(_scripts) 하나다 — 여기 따로 적어 두면 대본을 더하는 날 한쪽만 는다(#96 · 전에는 GIF_IDS 가 따로 있었다).
# 인자 없이 돌면 그 표의 줄 `new("<id>", Patterns: …` 에서 id 를 읽는다. 줄의 꼴을 바꾸면 여기도 같이 고친다(GifRunner.Ids 의 주석).
gif_ids() {
  sed -n 's/^[[:space:]]*new("\([a-z0-9]*\)", Patterns:.*/\1/p' "$PROJECT/battle/debug/GifRunner.cs"
}
GIF_MAX_FRAMES=240
GIF_MAX_BYTES=1048576
FFMPEG="${FFMPEG:-/opt/homebrew/bin/ffmpeg}"

cmd_gifs() {
  need_godot
  [[ -z "$HITBOX_ARG" ]] || die "HITBOXES=1 로는 GIF 를 안 찍습니다 — 판정 사각형이 README 에 실린다. 판정 보기는 shots 로 보세요."
  [[ -x "$FFMPEG" ]] || die "ffmpeg 이 없습니다 — $FFMPEG
brew install ffmpeg 로 깔거나 FFMPEG 환경변수로 경로를 알려주세요."
  cmd_build
  say "GIF"
  local ids=("$@")
  # 낱말로 갈라 담는다 — id 는 ASCII 소문자 · 숫자라 안전하다(macOS 의 bash 3.2 에는 mapfile 이 없다).
  [[ ${#ids[@]} -gt 0 ]] || ids=($(gif_ids))
  [[ ${#ids[@]} -gt 0 ]] || die "GifRunner.cs 에서 대본 id 를 못 읽었습니다 — 표의 줄 꼴(new(\"<id>\", Patterns: …)이 바뀌었나요? gif_ids 를 고치세요."
  mkdir -p "$ROOT/docs/gifs" "$OUT/gifs"

  local id
  for id in "${ids[@]}"; do
    local dir="$OUT/gifs/$id" log="$OUT/gifs/$id.log" code=0
    rm -rf "$dir"
    mkdir -p "$dir"
    # --write-movie 의 경로는 절대 경로여야 한다 — 상대 경로는 --path 의 프로젝트 폴더 기준이라 폴더를 못 찾고 한 장도 안 쓴다(재 보니 그랬다).
    # --quit-after 는 마지막 울타리다 — 러너가 30초(1800장)에 스스로 멈추고, 그 전에 끝난 판도 [E] 로 멈춘다(GifRunner).
    "$GODOT" --path "$PROJECT" --write-movie "$dir/f.png" --fixed-fps 60 --quit-after 3600 -- "--gif=$id" $SESSION_ARG $LOG_ARG > "$log" 2>&1 || code=$?
    judge_headless "GIF $id" "$log" "gif=done id=$id" "$code"

    local from to ticks
    from="$(sed -n 's/^\[gif\]\[I\] capture_from frame=\([0-9]*\) .*/\1/p' "$log" | head -1)"
    to="$(sed -n 's/^\[gif\]\[I\] capture_to frame=\([0-9]*\) .*/\1/p' "$log" | head -1)"
    [[ -n "$from" && -n "$to" ]] || die "GIF $id: 잡을 구간이 로그에 없습니다 — [gif][I] capture_from · capture_to. 전체 로그: $log"
    local n=$(( to - from ))
    (( n > 0 )) || die "GIF $id: 잡을 구간이 비었습니다 (${from} → ${to}). 전체 로그: $log"
    (( n <= GIF_MAX_FRAMES )) || die "GIF $id: 잡은 구간이 ${n}장 — 4초(${GIF_MAX_FRAMES}장)를 넘습니다. 대본의 구간을 줄이세요."
    # 장 수는 틱 수보다 적을 수 없다 — 틱마다 한 장을 그리고(--fixed-fps 60 · 물리 60Hz), 히트스톱은 틱 없이 장만 더한다. 적으면 엔진이 그리기를
    # 건너뛴 것이다(가려진 창이 그랬다 — 재 보니 129틱이 45장에 담겨 GIF 가 세 배 빠르게 돌았다). 그런 GIF 는 박자가 거짓말이라 실패로 친다.
    ticks=$(( $(sed -n 's/^\[gif\]\[I\] capture_to frame=[0-9]* tick=\([0-9]*\).*/\1/p' "$log" | head -1) \
            - $(sed -n 's/^\[gif\]\[I\] capture_from frame=[0-9]* tick=\([0-9]*\).*/\1/p' "$log" | head -1) ))
    (( n >= ticks )) || die "GIF $id: ${ticks}틱이 ${n}장에 담겼습니다 — 엔진이 그리기를 건너뛰었습니다(창이 가려졌나요?). 창을 앞에 두고 다시 도세요."

    # 입력을 잡을 구간 [from, to) 의 n 장으로 자른다 — trim 은 fps 로 솎기 **전**의 입력 장을 센다. 전에는 -frames:v n 을 걸었는데, 그것은
    # 출력(15fps 로 솎은 뒤)의 장 수라 입력을 안 묶었다: n 장이 n/4 장으로 솎이므로 상한에 닿지 않고, 입력은 러너가 끝난 뒤의 장(to 너머)까지
    # 흘러들었다(#96 · #93 리뷰 T11-M1 — grab 은 한 장이었다).
    local gif="$ROOT/docs/gifs/$id.gif"
    "$FFMPEG" -y -loglevel error -framerate 60 -start_number "$from" -i "$dir/f%08d.png" \
      -vf "trim=end_frame=$n,fps=15,scale=480:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=diff_mode=rectangle" \
      "$gif" || die "GIF $id: ffmpeg 이 멈췄습니다. 프레임: $dir"

    local size
    size="$(stat -f%z "$gif")"
    (( size <= GIF_MAX_BYTES )) || die "GIF $id: $(( size / 1024 ))KB — 1MB 를 넘습니다. 구간을 줄이거나 대본을 고치세요."
    ok "$id.gif — ${n}장($(awk "BEGIN { printf \"%.2f\", $n / 60 }")초) · $(( size / 1024 ))KB"
    rm -rf "$dir"
  done
}

cmd_export() {
  need_godot
  local preset="${1:-macOS}"

  [[ -f "$PROJECT/export_presets.cfg" ]] \
    || die "익스포트 프리셋이 없습니다 — $PROJECT/export_presets.cfg (이 파일은 커밋되어 있어야 합니다)"
  grep -q "name=\"$preset\"" "$PROJECT/export_presets.cfg" \
    || die "프리셋 \"$preset\" 가 export_presets.cfg 에 없습니다."

  local templates; templates="$(export_template_dir)"
  [[ -d "$templates" ]] || die "익스포트 템플릿이 없습니다 — $templates
에디터의 [에디터] → [익스포트 템플릿 관리] 에서 받거나, 같은 버전의 템플릿 tpz 를 그 경로에 푸세요."
  [[ -f "$templates/macos.zip" ]] || die "macOS 템플릿이 없습니다 — $templates/macos.zip"

  # C# 을 먼저 빌드한다. 익스포트가 옛 어셈블리를 싣는 것은 실패보다 나쁘다 —
  # 돌긴 도는데 어제의 규칙으로 돈다.
  cmd_build

  say "익스포트 ($preset)"
  mkdir -p "$OUT"
  local app="$OUT/OVERFIT.app" log="$OUT/export.log" code=0
  rm -rf "$app"
  "$GODOT" --headless --path "$PROJECT" --export-release "$preset" "$app" > "$log" 2>&1 || code=$?

  # 엔진 진단은 로그로만 나온다. 종료 코드가 0 이어도 ERROR 가 있으면 반쯤 만들어진 번들이다.
  local errs
  errs="$(engine_diag_blocks "$log" | grep -E "^(ERROR|SCRIPT ERROR|USER ERROR):" || true)"
  if [[ -n "$errs" ]]; then
    bad "익스포트: 엔진 ERROR $(grep -c . <<< "$errs")건. 전체 로그: $log"
    head -5 <<< "$errs" | cut -c1-400 | sed 's/^/      /'
    exit 1
  fi

  [[ $code -eq 0 ]] || die "익스포트 실패 (종료 코드 $code). 전체 로그: $log"
  [[ -d "$app" ]] || die "번들이 안 만들어졌습니다 — $app. 전체 로그: $log"
  [[ -x "$app/Contents/MacOS/OVERFIT" ]] || die "번들 안에 실행 파일이 없습니다 — $app/Contents/MacOS/OVERFIT"

  # 번들은 **폴더**다. 올리거나 보내려면 파일 하나여야 하고, 그냥 zip 하면 심볼릭 링크와
  # 서명 블록이 깨진다 — ditto 가 애플이 그 용도로 주는 도구다.
  local zip="$OUT/OVERFIT-macos.zip"
  rm -f "$zip"
  ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"

  ok "$app ($(du -sh "$app" | cut -f1))"
  ok "$zip ($(du -sh "$zip" | cut -f1))"
  ok "로그: $log"
}

cmd_clean() {
  rm -rf "$OUT" "$PROJECT/.godot/mono/temp" "$PROJECT/obj" "$PROJECT/bin" "$TEST_DIR/obj" "$TEST_DIR/bin" "$FACTORY_DIR/obj" "$FACTORY_DIR/bin"
  ok "산출물을 지웠습니다."
}

# ----------------------------------------------------------------

# 파일 맨 위 주석 블록이 곧 사용법이다. 줄 번호를 박아두면 파일을 고칠 때 어긋난다.
usage() {
  awk 'NR<3 {next} /^#/ {sub(/^# ?/,""); print; next} {exit}' "${BASH_SOURCE[0]}"
}

case "${1:-}" in
  doctor)    shift; cmd_doctor "$@" ;;
  check)     shift; cmd_check "$@" ;;
  fix)       shift; cmd_fix "$@" ;;
  build)     shift; cmd_build "$@" ;;
  test)      shift; cmd_test "$@" ;;
  uids)      shift; cmd_uids "$@" ;;
  hitboxes)  shift; cmd_hitboxes "$@" ;;
  run)       shift; cmd_run "$@" ;;
  editor)    shift; cmd_editor "$@" ;;
  import)    shift; cmd_import "$@" ;;
  smoke)     shift; cmd_smoke "$@" ;;
  demo)      shift; cmd_demo "$@" ;;
  factory)   shift; cmd_factory "$@" ;;
  shots)     shift; cmd_shots "$@" ;;
  gifs)      shift; cmd_gifs "$@" ;;
  export)    shift; cmd_export "$@" ;;
  clean)     shift; cmd_clean "$@" ;;
  ""|-h|--help|help) usage ;;
  *) echo "모르는 명령: $1" >&2; echo >&2; usage >&2; exit 1 ;;
esac
