#!/usr/bin/env bash
# Import project resources + engine-warning gate.
# Usage: check_import.sh [log_path]   (default /tmp/import.log)
# Ported 1:1 from ci.yml fast-gate "Import project resources (warning gate)".
# Fails on: non-zero import, or "Warning treated as error" (GDScript warnings
# configured as error-level in project.godot = zero tolerance).
set -uo pipefail

GODOT="${GODOT:-godot}"
LOG="${1:-/tmp/import.log}"

if ! "$GODOT" --headless --import --path . > "$LOG" 2>&1; then
  echo "::error::import failed"
  tail -30 "$LOG"
  exit 1
fi
if grep -q "Warning treated as error" "$LOG"; then
  echo "::error::GDScript warnings (treated as errors) found:"
  grep -B1 "Warning treated as error" "$LOG" | head -30
  exit 1
fi
tail -3 "$LOG"