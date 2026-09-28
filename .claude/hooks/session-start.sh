#!/bin/bash
# Claude Code on the web 세션이 뜰 때 dotnet · Godot 을 준비한다.
#
# 로컬(사람의 기기)에는 이미 있으니 클라우드 세션에서만 돈다.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

export DEBIAN_FRONTEND=noninteractive

# ── .NET SDK ──────────────────────────────────────────────────────────────
# Overfit.csproj · Overfit.Rules.Tests.csproj 를 빌드하는 데 필요하다(Godot 4.7 의 .NET 모듈과 같은 버전 계열).
if ! command -v dotnet >/dev/null 2>&1; then
  apt-get update -qq
  apt-get install -y -qq dotnet-sdk-8.0
fi

# ── Godot 4.7 (.NET/Mono) ─────────────────────────────────────────────────
# project.godot 의 config/features 가 "4.7" · "C#" 을 박아 둔 그 버전이다.
#
# 공식 배포처(godotengine.org · downloads.tuxfamily.org)는 이 환경의 네트워크 정책이 CONNECT 단계에서
# 막는다(확인함 — connect_rejected). GitHub 릴리스 미러(godotengine/godot-builds)는 뚫려 있으므로 거기서 받는다.
GODOT_DIR="/opt/godot/Godot_v4.7-stable_mono_linux_x86_64"
GODOT_BIN="$GODOT_DIR/Godot_v4.7-stable_mono_linux.x86_64"

if [ ! -x "$GODOT_BIN" ]; then
  tmp="$(mktemp -d)"
  curl -fsSL --max-time 120 -o "$tmp/godot.zip" \
    "https://github.com/godotengine/godot-builds/releases/download/4.7-stable/Godot_v4.7-stable_mono_linux_x86_64.zip"
  mkdir -p /opt/godot
  unzip -q "$tmp/godot.zip" -d /opt/godot
  rm -rf "$tmp"
  chmod +x "$GODOT_BIN"
fi

# tools/build.sh 는 godot 을 PATH 가 아니라 GODOT_PATH(없으면 GODOT)로 찾는다 — 세션 전체에 심어 둔다.
ln -sf "$GODOT_BIN" /usr/local/bin/godot
ln -sf "$GODOT_BIN" /usr/bin/godot   # godot MCP 서버의 기본 조회 경로
echo "export GODOT_PATH=$GODOT_BIN" >> "$CLAUDE_ENV_FILE"
