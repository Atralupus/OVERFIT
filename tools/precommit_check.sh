#!/usr/bin/env bash
#
# 커밋 전 게이트. Claude Code 의 PreToolUse(Bash) 훅으로 붙는다 (.claude/settings.json).
#
# stdin 으로 훅 입력 JSON 을 받는다. 실행하려는 명령이 git commit 이면
#   ⓪ 브랜치 게이트 — main 에 직접 커밋하지 않는다
#   ① TDD 게이트   — 테스트가 도는 코드를 고치는 커밋은 tests/ 변경을 동반해야 한다
#   ② 검사 게이트   — tools/build.sh check (포맷 · 빌드 · 규칙 테스트 · uid)
# 를 차례로 돌리고, 어긋나면 커밋을 막는다.
#
# ⚠ 어느 체크아웃을 볼 것인가 — 훅 입력의 cwd 다, 스크립트 위치가 아니다.
#   워크트리에서 커밋하면 스크립트는 주 체크아웃의 것이 돌 수 있다. 그때 스크립트 위치로 git 을
#   때리면 남의 인덱스와 남의 브랜치를 보게 된다 — 게이트가 통째로 헛돈다.
#
# 사람이 손으로 쓸 일은 없다. 검사만 돌려보려면 tools/build.sh check.

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# 게이트가 남긴 한마디(우회 · 건너뜀). 통과하든 차단하든 출력에 실려 나간다.
NOTE=""

# 통과 — 남길 말이 없으면 아무 말 없이 빠진다.
pass() {
  [[ -n "$NOTE" ]] && python3 -c '
import json, sys
print(json.dumps({"systemMessage": sys.argv[1]}, ensure_ascii=False))' "$NOTE"
  exit 0
}

# 한마디를 적어 둔다 — 여기서 끝내지 않는다. 우회해도 뒤 게이트는 그대로 돈다.
note() {
  NOTE="${NOTE:+$NOTE
}$1"
  printf '%s\n' "$1" >&2   # 훅 로그(transcript)에도 남긴다
}

# 차단 — Claude 에게 이유를 돌려준다.
deny() {
  python3 -c '
import json, sys
reason = sys.argv[1]
if sys.argv[2]:
    reason = sys.argv[2] + "\n\n" + reason
print(json.dumps({"hookSpecificOutput": {
    "hookEventName": "PreToolUse",
    "permissionDecision": "deny",
    "permissionDecisionReason": reason,
}}, ensure_ascii=False))' "$1" "$NOTE"
  exit 0
}

payload="$(cat)"

read_payload() {   # read_payload <키> — 훅 입력 JSON 에서 한 값을 꺼낸다
  python3 -c '
import json, sys
try:
    got = json.load(sys.stdin)
except Exception:
    raise SystemExit(print(""))
if sys.argv[1] == "command":
    print(got.get("tool_input", {}).get("command", ""))
else:
    print(got.get(sys.argv[1], ""))' "$1" <<< "$payload" 2>/dev/null
}

command="$(read_payload command)"

# 이 명령이 정말 커밋을 하려는가.
#
# ⚠ **부분 일치로 보면 안 된다.** 예전엔 `*"git commit"*` 였는데, 그러면 명령 **안에 실린 글**에
#   그 두 단어가 있기만 해도 걸린다 — 실제로 밟았다: 커밋 예시가 들어간 계획 문서를
#   heredoc 으로 쓰는 `cat > plan.md <<'EOF' … git commit -m … EOF` 가 통째로 차단됐다.
#   파일을 쓰는 명령이지 커밋하는 명령이 아닌데도.
#
# 그래서 **명령 위치**에 있을 때만 본다 — 문자열의 처음이거나, 구분자(`&&` `||` `;` `|` 개행,
# 또는 `(` `{`) 바로 뒤여야 한다. heredoc 본문의 글은 그 앞에 구분자가 아니라 텍스트가 있으므로 안 걸린다.
# 완벽하지는 않다(본문 줄이 마침 `git commit` 으로 시작하면 걸린다). 그건 받아들인다 —
# 거짓 통과보다 거짓 차단이 낫고, 거짓 차단은 검사가 한 번 더 도는 것으로 끝난다.
is_git_commit() {
  grep -qE '(^|[&|;(){}]|[[:space:]]&&|[[:space:]]\|\||^[[:space:]]*)[[:space:]]*git[[:space:]]+(-[^[:space:]]+[[:space:]]+)*commit([[:space:]]|$)' <<< "$1"
}

is_git_commit "$command" || pass

# ── 어느 체크아웃인가 ──────────────────────────────────────────────────────
# 훅 입력의 cwd 가 진짜 커밋이 일어나는 곳이다. 스크립트 위치($ROOT)는 워크트리에서 어긋난다.
REPO="$ROOT"
cwd="$(read_payload cwd)"
if [[ -n "$cwd" && -d "$cwd" ]]; then
  top="$(git -C "$cwd" rev-parse --show-toplevel 2>/dev/null)"
  if [[ -n "$top" && -f "$top/tools/precommit_check.sh" ]]; then
    REPO="$top"
  fi
fi

staged="$(git -C "$REPO" diff --cached --name-only 2>/dev/null)"
branch="$(git -C "$REPO" rev-parse --abbrev-ref HEAD 2>/dev/null)"

# ── 커밋 메시지 읽기 ──────────────────────────────────────────────────────
# 우회 표식(no-test: · on-main:)이 거기 있다. 못 읽는 형태(에디터로 여는 맨 git commit ·
# --fixup · 파싱 실패)가 있으므로 상태를 같이 돌려받는다.
msg_probe="$(python3 - "$command" "$REPO" <<'PY'
import re, shlex, subprocess, sys

command, root = sys.argv[1], sys.argv[2]


def revmsg(rev):
    try:
        done = subprocess.run(["git", "-C", root, "log", "-1", "--pretty=%B", rev],
                              capture_output=True, text=True, timeout=10)
        return done.stdout if done.returncode == 0 else None
    except Exception:
        return None


def readfile(path):
    try:
        with open(path if path.startswith("/") else f"{root}/{path}", encoding="utf-8") as f:
            return f.read()
    except Exception:
        return None


try:
    toks = shlex.split(command, posix=True)
except ValueError:
    print("unreadable")
    raise SystemExit

texts, readable, i = [], False, 0
while i < len(toks):
    t = toks[i]
    # -m / --message / -am / -m"msg" 같은 붙은 형태까지 본다.
    m = re.fullmatch(r"-([A-Za-z]*)([mF])(.*)", t)
    if t in ("--message", "--file") or t.startswith("--message=") or t.startswith("--file="):
        if "=" in t:
            val = t.split("=", 1)[1]
        else:
            val = toks[i + 1] if i + 1 < len(toks) else ""
            i += 1
        if t.startswith("--message"):
            texts.append(val)
            readable = True
        else:
            got = readfile(val)
            if got is None:
                readable = False
                break
            texts.append(got)
            readable = True
    elif m:
        kind, attached = m.group(2), m.group(3)
        val = attached
        if not val:
            val = toks[i + 1] if i + 1 < len(toks) else ""
            i += 1
        if kind == "m":
            texts.append(val)
            readable = True
        else:
            got = readfile(val)
            if got is None:
                readable = False
                break
            texts.append(got)
            readable = True
    elif t == "--amend":
        got = revmsg("HEAD")
        if got is not None:
            texts.append(got)
            readable = True
    elif t in ("-C", "-c", "--reuse-message", "--reedit-message") \
            or t.startswith("--reuse-message=") or t.startswith("--reedit-message="):
        if "=" in t:
            rev = t.split("=", 1)[1]
        else:
            rev = toks[i + 1] if i + 1 < len(toks) else "HEAD"
            i += 1
        got = revmsg(rev)
        if got is not None:
            texts.append(got)
            readable = True
    elif t.startswith("--fixup") or t.startswith("--squash") \
            or t in ("-t", "--template") or t.startswith("--template="):
        # 메시지를 git 이나 에디터가 만든다 — 우리가 읽을 수 없다.
        readable = False
        break
    i += 1

if not readable:
    print("unreadable")
    raise SystemExit

print("readable")
print("\n".join(texts))
PY
)"
msg_state="$(head -1 <<< "$msg_probe")"
msg_text="$(tail -n +2 <<< "$msg_probe")"

# 표식이 메시지에 있나 / 그 뒤 사유는 무엇인가. 사유 없는 우회는 우회가 아니다.
marker_present() { grep -qE "(^|[^A-Za-z0-9_-])$1:" <<< "$msg_text"; }
marker_reason() {
  sed -nE "s/.*$1:[[:space:]]*//p" <<< "$msg_text" | head -1 \
    | sed -E "s/[[:space:]]+$//; s/^[\"']//; s/[\"']$//; s/[[:space:]]+$//"
}

# ── ⓪ 브랜치 게이트 ───────────────────────────────────────────────────────
# CLAUDE.md 의 첫 규칙 — "main 에 직접 커밋하지 않는다. 모든 변경은 브랜치 → PR".
# 강제하는 것이 없으면 실수로 main 에 쌓인다. 여기서 막는다.
# 우회는 남긴다: 훅 자신이 깨졌을 때 · 되돌릴 수 없는 긴급 상황에 출구가 없으면 안 된다.
if [[ "$branch" == "main" ]]; then
  branch_hint="브랜치를 따고 다시 커밋하세요:

  git switch -c <브랜치명>

정말 main 에 직접 커밋해야 하면 커밋 메시지 본문에 사유를 적으세요:

  on-main: <왜 브랜치를 못 쓰는가>

(우회는 훅 출력과 커밋 메시지에 영구히 남습니다.)"

  if [[ "$msg_state" != "readable" ]]; then
    deny "main 에 직접 커밋하려 합니다 (체크아웃: $REPO).

커밋 메시지를 읽을 수 없는 형태(에디터로 여는 맨 git commit · --fixup 등)라
우회 표식도 확인할 수 없습니다. -m 으로 메시지를 주세요.

$branch_hint"
  elif marker_present "on-main"; then
    on_main_reason="$(marker_reason "on-main")"
    if [[ -z "$on_main_reason" ]]; then
      deny "main 직접 커밋 우회에 사유가 없습니다. on-main: 뒤에 왜 브랜치를 못 쓰는지 적으세요."
    fi
    note "[branch] 우회 — on-main: $on_main_reason
main 에 직접 커밋합니다. 사유는 커밋 메시지에 그대로 남습니다."
  else
    deny "main 에 직접 커밋하려 합니다 (체크아웃: $REPO).

CLAUDE.md: \"main 에 직접 커밋하지 않는다. 모든 변경은 브랜치 → PR 을 거친다.\"

$branch_hint"
  fi
fi

# ── ① TDD 게이트 ──────────────────────────────────────────────────────────
# 범위는 손으로 적지 않는다 — tests/ csproj 가 **링크하는 파일**이 곧 범위다.
# 링크된 파일은 Godot 없이 tests/ 에서 통째로 돌릴 수 있다는 뜻이고, 그러니 테스트를 못 쓸 핑계가 없다.
# csproj 가 진실이라 새 파일이 링크에 들어오면 게이트도 자동으로 따라온다 (목록 이중관리 없음).
# 반대(tests/ 만 고침)는 막지 않는다 — 실패하는 테스트를 먼저 커밋하는 red 단계가 TDD 의 첫걸음이다.
gated_re="$(python3 - "$REPO/tests/Overfit.Rules.Tests/Overfit.Rules.Tests.csproj" 2>/dev/null <<'PY'
import sys
import xml.etree.ElementTree as ET

# 링크돼 있어도 게이트에서 빼는 것. 늘릴 때는 왜인지 여기에 적는다.
EXCLUDE = {
    # balance.json 의 모양 그 자체인 노브 DTO. 수치 하나 늘리는 커밋은 데이터 검증이 보고,
    # 그 수치를 실제로 쓰는 규칙 파일은 어차피 이 게이트에 걸린다.
    "overfit/core/BalanceData.cs",
}

SPECIAL = set(".[]{}()*+?^$|\\")


def to_regex(path):
    out, i = [], 0
    while i < len(path):
        if path.startswith("**/", i):
            out.append("([^/]+/)*")
            i += 3
        elif path[i] == "*":
            out.append("[^/]*")
            i += 1
        else:
            out.append("\\" + path[i] if path[i] in SPECIAL else path[i])
            i += 1
    return "".join(out)


try:
    root = ET.parse(sys.argv[1]).getroot()
except Exception:
    raise SystemExit(1)

pats = []
for node in root.iter("Compile"):
    inc = (node.get("Include") or "").replace("\\", "/")
    while inc.startswith("../"):
        inc = inc[3:]
    if not inc.startswith("overfit/") or inc in EXCLUDE:
        continue
    pats.append(to_regex(inc))

if not pats:
    raise SystemExit(1)
print("^(" + "|".join(sorted(set(pats))) + ")$")
PY
)"

# csproj 를 못 읽으면 게이트를 건너뛴다 — 다만 건너뛴 사실을 남긴다(조용한 무력화 방지).
if [[ -z "$gated_re" ]]; then
  note "[tdd] csproj 를 못 읽어 TDD 범위를 정하지 못했습니다 — 이 커밋은 TDD 검사를 건너뜁니다."
else
  rules_staged="$(grep -E "$gated_re" <<< "$staged")"
  tests_staged="$(grep -E '^tests/' <<< "$staged")"

  # "git add tests/... && git commit" 처럼 한 줄로 이어붙인 경우, 훅은 add 이전 상태를 본다.
  # 명령 안에서 tests/ 를 스테이징하려는 게 보이면 그것도 테스트 변경으로 친다 (거짓 차단 방지).
  if [[ -z "$tests_staged" ]] \
    && grep -qE '\bgit[[:space:]]+(add|stage)\b' <<< "$command" \
    && grep -qE '(^|[^A-Za-z0-9_/.-])tests/' <<< "$command"; then
    tests_staged="(명령에 git add tests/… 가 있음)"
  fi

  if [[ -n "$rules_staged" && -z "$tests_staged" ]]; then
    rules_list="$(sed 's/^/  - /' <<< "$rules_staged")"

    # 못 읽는 메시지면 검사를 건너뛴다 — 거짓 차단이 거짓 통과보다 나쁘다.
    if [[ "$msg_state" != "readable" ]]; then
      note "[tdd] 건너뜀 — 커밋 메시지를 읽을 수 없는 형태라 TDD 검사를 하지 않았습니다.
스테이징된 파일:
$rules_list"
    elif marker_present "no-test"; then
      bypass_reason="$(marker_reason "no-test")"
      if [[ -z "$bypass_reason" ]]; then
        deny "TDD 우회에 사유가 없습니다. no-test: 뒤에 왜 테스트가 필요 없는지 적으세요.

  예) git commit -m \"refactor: 필드 이름을 바꾼다

  no-test: 순수 리네임, 동작 변화 없음\""
      fi
      note "[tdd] 우회 — no-test: $bypass_reason
테스트 없이 다음을 커밋합니다:
$rules_list
사유는 커밋 메시지에 그대로 남습니다."
    else
      deny "TDD: 테스트가 도는 코드를 고치면서 tests/ 변경이 없습니다.

스테이징된 파일:
$rules_list

이 파일들은 tests/Overfit.Rules.Tests 의 csproj 가 링크하는 것들입니다 —
Godot 없이 초 단위로 전부 돌릴 수 있다는 뜻이고, 그래서 게이트가 여기까지 봅니다.
실패하는 테스트를 먼저 쓰고(red) → 통과할 만큼만 고치고(green) → 정리(refactor)하세요.

셋 중 하나를 하세요:
  1. tests/Overfit.Rules.Tests 에 테스트를 더하고 함께 스테이징한다
     (테스트만 먼저 커밋하는 red 상태는 막지 않습니다)
  2. 그 변경을 이 커밋에서 뺀다
  3. 정말 테스트가 필요 없으면 커밋 메시지 본문에 사유를 적는다:
     no-test: 순수 리네임, 동작 변화 없음
     (우회한 커밋은 훅 출력과 커밋 메시지에 남습니다)

⚠️ 'git add … && git commit …' 처럼 한 줄로 붙이면 훅이 add 이전 상태를 봅니다.
   스테이징과 커밋은 나눠서 실행하세요."
    fi
  fi
fi

# ── ② 검사 게이트 (포맷 · 빌드 · 규칙 테스트 · uid) ────────────────────────
command -v dotnet >/dev/null || pass   # 툴체인이 없으면 검사할 방법이 없다

# 스테이징된 것 중 검사 대상이 없으면 돌릴 이유가 없다.
# C# 뿐 아니라 overfit/data/*.json 도 대상이다 — 수치는 코드가 아니라 그 파일에만 있다.
if [[ -n "$staged" ]] \
  && ! grep -qE '\.(cs|csproj|sln)$|(^|/)\.(editorconfig|runsettings)$|^overfit/data/.*\.json$|^overfit/.*\.(tscn|tres|godot)$' <<< "$staged"; then
  pass
fi

# 커밋되는 그 체크아웃에서 돌린다 — 워크트리면 워크트리의 build.sh 다.
output="$(cd "$REPO" && ./tools/build.sh check 2>&1)"
if [[ $? -ne 0 ]]; then
  deny "커밋 전 검사(tools/build.sh check)가 실패했습니다. 고치고 다시 커밋하세요. (체크아웃: $REPO)

$(sed 's/\x1b\[[0-9;]*m//g' <<< "$output" | tail -30)

포맷 문제면 tools/build.sh fix 로 고칠 수 있습니다."
fi
pass
