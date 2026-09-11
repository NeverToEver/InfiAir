#!/usr/bin/env bash
# Main scene smoke (300 frames) + settings page build smoke + engine-error log scan.
# Usage: check_smoke.sh [log_path]   (default /tmp/smoke.log)
# Ported 1:1 from ci.yml fast-gate "Main scene smoke (300 frames)" + W-series
# error scan: exit 0 with "SCRIPT ERROR|Parse Error|Compile Error|Nonexistent
# function|Unhandled exception" in log also fails.
# 第二趟 --settings-probe：设置页各分组在「开页」时才构建，300 帧基线冒烟碰不到，
# 写错就是玩家点开即崩；冒烟改为「开局 300 帧 + 开设置页逐页切换 60 帧」两趟。
set -uo pipefail

GODOT="${GODOT:-godot}"
LOG="${1:-/tmp/smoke.log}"
PROBE_LOG="${LOG%.log}.settings.log"
ERR="SCRIPT ERROR\|Parse Error\|Compile Error\|Nonexistent function\|Unhandled exception"

if ! "$GODOT" --headless --path . --quit-after 300 > "$LOG" 2>&1; then
  echo "::error::main scene smoke failed"
  tail -30 "$LOG"
  exit 1
fi
if grep -q "$ERR" "$LOG"; then
  echo "::error::main scene smoke engine errors in log"
  grep -B1 "$ERR" "$LOG" | head -10
  exit 1
fi
tail -3 "$LOG"

if ! "$GODOT" --headless --path . --quit-after 60 -- --settings-probe > "$PROBE_LOG" 2>&1; then
  echo "::error::settings page smoke failed"
  tail -30 "$PROBE_LOG"
  exit 1
fi
if grep -q "$ERR" "$PROBE_LOG"; then
  echo "::error::settings page smoke engine errors in log"
  grep -B1 "$ERR" "$PROBE_LOG" | head -10
  exit 1
fi
echo "settings page smoke: ok"