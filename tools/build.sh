#!/usr/bin/env bash
#
# 개발 루프와 빌드를 한 곳에서 돌린다.
#
#   tools/build.sh doctor              환경 점검 — 뭐가 없는지 알려준다
#   tools/build.sh check               포맷 검사 + 빌드 + 규칙 테스트 + .uid 짝. 커밋 전 게이트
#   tools/build.sh fix                 포맷 자동 수정
#   tools/build.sh build               C# 빌드만
#   tools/build.sh test [cover] [인자…] 규칙 테스트 (xUnit, tests/PreReLU.Rules.Tests). Godot 을 안 띄운다 — 초 단위
#                                      발견 개수를 TRX 에서 읽어 인용한다. **0개면 실패다** — 러너가 빠지면
#                                      dotnet test 가 테스트 0개를 찾고 조용히 통과하기 때문이다
#                                      cover → 커버리지(cobertura) 까지. 나머지 인자는 dotnet test 로 그대로
#                                      (예: --filter FullyQualifiedName~Det)
#   tools/build.sh uids                .cs 마다 .uid 가 짝을 이루는지. check 가 부른다
#   tools/build.sh run [씬]            C# 빌드 후 게임 실행
#   tools/build.sh editor              에디터 실행
#   tools/build.sh import              에셋 임포트만 (헤드리스). 클론 직후 반드시 한 번
#   tools/build.sh smoke               헤드리스 부팅 + 씬 순회 (로그로 검증)
#   LOG_LEVEL=trace tools/build.sh …   로그 레벨 지정 (trace|debug|info|warn|error)
#   tools/build.sh clean               빌드 산출물 삭제
#
# 헤드리스 판정 — 창 없이 도는 서브커맨드(지금은 smoke 하나)는 판정 함수 하나(judge_headless)를 공유한다.
#   ① 로그에 ^[tag][E] 가 있으면 실패 (CLAUDE.md: Error = 규칙 위반)
#   ①′ 엔진이 찍은 ERROR: 블록이 있으면 실패 — C# 예외는 여기로만 나온다. WARNING: 은 세기만 한다
#   ② 완료 표지([tag][M])가 없으면 실패 — 게임이 끝까지 못 갔다
#   ③ 표지 있고 종료 코드 0 → 통과      ④ 표지 있고 코드 != 0 → 실패
# 완료 표지는 core/Log.Marker 가 내므로 LOG_LEVEL 과 무관하다. 내용 검사(expect_log)만 그 줄이 안 찍히는 레벨에서 건너뛴다.
#
# 산출물은 전부 out/ 아래로 떨어진다 (gitignore 됨).

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/prerelu"
SLN="$PROJECT/PreReLU.sln"
OUT="$ROOT/out"

# 규칙 테스트. prerelu/ **바깥**이다 — 안에 두면 Godot.NET.Sdk 의 기본 glob 이
# 테스트 소스를 게임 어셈블리에 컴파일해 넣는다. 규칙 파일은 옮기지 않고 csproj 가 링크만 한다.
TEST_DIR="$ROOT/tests/PreReLU.Rules.Tests"
TEST_PROJ="$TEST_DIR/PreReLU.Rules.Tests.csproj"

GODOT="${GODOT_PATH:-${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}}"

# LOG_LEVEL=trace|debug|info|warn|error 를 게임의 --log-level 유저 인자로 넘긴다. 비어 있으면 게임 기본값(디버그 빌드 debug).
LOG_ARG=""
[[ -n "${LOG_LEVEL:-}" ]] && LOG_ARG="--log-level=${LOG_LEVEL}"

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

# .cs 마다 .uid 가 짝을 이루는지. 빠지면 체크아웃마다 UID 가 갈리고 엔진은 WARNING 으로만 알린다 —
# .tscn 이 uid:// 로 스크립트를 가리키는 순간 그게 조용한 깨짐이 된다.
# .uid 는 Godot 이 임포트할 때 만든다. 없으면 tools/build.sh import 를 한 번 돌린다.
cmd_uids() {
  local missing=0 f
  while IFS= read -r f; do
    [[ -f "$f.uid" ]] && continue
    bad "uid 없음 — ${f#"$ROOT"/}"
    missing=1
  done < <(find "$PROJECT" -name '*.cs' \
    -not -path "*/.godot/*" -not -path "*/obj/*" -not -path "*/bin/*" -not -path "*/addons/*" | sort)

  if [[ $missing -eq 1 ]]; then
    die "위 .cs 에 .uid 짝이 없습니다. tools/build.sh import 를 돌리고 생긴 .uid 를 같이 커밋하세요."
  fi
  ok ".uid 짝 — 전부 있음"
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
}

cmd_run() {
  need_godot
  # CLI 실행은 C# 을 자동으로 빌드하지 않는다. 안 하면 옛 어셈블리로 돈다.
  cmd_build
  say "실행"
  if [[ $# -gt 0 ]]; then "$GODOT" --path "$PROJECT" "$1" -- $LOG_ARG
  else "$GODOT" --path "$PROJECT" -- $LOG_ARG; fi
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
  "$GODOT" --headless --fixed-fps 60 --path "$PROJECT" --quit-after 3600 -- --tour $LOG_ARG > "$log" 2>&1 || code=$?
  grep -E "^\[(boot|data|scene|tour)\]" "$log" || true
  judge_headless "스모크" "$log" "tour=done" "$code"
  # 순회가 정말 씬을 갈아끼웠나. 표지만 보면 "돌지 않고 끝난" 경우를 못 본다.
  expect_log "$log" info '^\[data\]\[I\] loaded ' "balance.json 을 읽은 흔적이 없습니다."
  expect_log "$log" info '^\[scene\]\[I\] goto=Play$' "Play 씬으로 간 흔적이 없습니다."
  expect_log "$log" info '^\[scene\]\[I\] play ready$' "Play 씬의 스크립트가 안 붙었습니다."
  expect_log "$log" info '^\[scene\]\[I\] goto=Title$' "Title 로 돌아온 흔적이 없습니다."
  ok "스모크 통과 ($log)"
}

cmd_clean() {
  rm -rf "$OUT" "$PROJECT/.godot/mono/temp" "$PROJECT/obj" "$PROJECT/bin" "$TEST_DIR/obj" "$TEST_DIR/bin"
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
  run)       shift; cmd_run "$@" ;;
  editor)    shift; cmd_editor "$@" ;;
  import)    shift; cmd_import "$@" ;;
  smoke)     shift; cmd_smoke "$@" ;;
  clean)     shift; cmd_clean "$@" ;;
  ""|-h|--help|help) usage ;;
  *) echo "모르는 명령: $1" >&2; echo >&2; usage >&2; exit 1 ;;
esac
