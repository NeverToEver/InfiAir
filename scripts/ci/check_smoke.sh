#!/usr/bin/env bash
# Main scene smoke (300 frames) + engine-error log scan.
# Usage: check_smoke.sh [log_path]   (default /tmp/smoke.log)
# Ported 1:1 from ci.yml fast-gate "Main scene smoke (300 frames)" + W-series
# error scan: exit 0 with "SCRIPT ERROR|Parse Error|Compile Error|Nonexistent
# function|Unhandled exception" in log also fails.
set -uo pipefail

GODOT="${GODOT:-godot}"
LOG="${1:-/tmp/smoke.log}"
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