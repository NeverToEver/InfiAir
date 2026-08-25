#!/usr/bin/env bash
# Compile probe every test/*.tscn with --quit-after 2 (catches Parse/Compile/
# SCRIPT ERROR / Nonexistent function / Unhandled exception that --import misses,
# e.g. screenshot tools). timeout 60 per scene; flake retry once for the Godot
# C# finalize low-frequency segfault (exit 134 / leaked unsafe reference).
# Usage: check_compile_probe.sh [log_dir]   (default /tmp)
# Ported 1:1 from ci.yml fast-gate "Compile probe all test scenes (gate blind spot)".
set -uo pipefail

GODOT="${GODOT:-godot}"
LOG_DIR="${1:-/tmp}"
ERR="SCRIPT ERROR\|Parse Error\|Compile Error\|Nonexistent function\|Unhandled exception"

probe_scene() {
  local tscn="$1" tag="$2" name log code
  name="$(basename "$tscn" .tscn)"
  log="$LOG_DIR/probe-$name-$tag.log"
  if timeout 60 "$GODOT" --headless --path . "res://$tscn" --quit-after 2 >"$log" 2>&1; then
    :
  else
    code=$?
    if [ "$code" -eq 124 ]; then
      echo "::error::$name compile probe timed out (hang)"
    else
      echo "::error::$name compile probe failed (exit $code)"
    fi
    tail -20 "$log"
    return 1
  fi
  if grep -q "$ERR" "$log"; then
    echo "::error::$name compile probe engine error"
    grep -B2 "$ERR" "$log" | head -20
    return 1
  fi
}

bad=0
for tscn in test/*.tscn; do
  name="$(basename "$tscn" .tscn)"
  if probe_scene "$tscn" first; then
    :
  else
    echo "::warning::$name compile probe failed once, retrying once for possible exit flake"
    if ! probe_scene "$tscn" retry; then
      bad=1
    fi
  fi
done
if [ "$bad" -ne 0 ]; then
  exit 1
fi
echo "compile probe: $(ls test/*.tscn | wc -l) scenes clean"