#!/usr/bin/env bash
# Run all assertion scenes: test/*_test.tscn minus the long-running autoplay probe.
# Per scene 300s timeout; flake retry once (HUD timing flakes under CI load);
# engine-error log scan (exit 0 but errors in log fails - dead/weak assertions);
# scene-count hard check (ran == discovered-1); exit code = failed count.
# Usage: run_assertion_scenes.sh [log_dir]   (default /tmp)
# Ported 1:1 from ci.yml full-regression "Run assertion scenes" (V/W series).
set -uo pipefail

GODOT="${GODOT:-godot}"
LOG_DIR="${1:-/tmp}"
ERR="SCRIPT ERROR\|Parse Error\|Compile Error\|Nonexistent function\|Unhandled exception"

passed=0
failed=0
flakes=0
run=0
total=$(ls test/*_test.tscn | wc -l)

for tscn in test/*_test.tscn; do
  name="$(basename "$tscn" .tscn)"
  case "$name" in
    autoplay_test) echo "skip $name (long-running probe, run locally)"; continue ;;
  esac
  run=$((run + 1))
  echo "== $name =="
  log="$LOG_DIR/$name.log"
  oklog="$log"
  if timeout 300 "$GODOT" --headless --path . "res://$tscn" >"$log" 2>&1; then
    passed=$((passed + 1))
  else
    code=$?
    echo "::warning::$name failed once (exit $code), retrying once for possible flake"
    if timeout 300 "$GODOT" --headless --path . "res://$tscn" >"$log.retry" 2>&1; then
      echo "::warning::$name passed on retry (flaky)"
      passed=$((passed + 1))
      flakes=$((flakes + 1))
      oklog="$log.retry"
    else
      code=$?
      echo "::error::$name FAILED twice (exit $code)"
      tail -25 "$log.retry"
      failed=$((failed + 1))
      continue
    fi
  fi
  if grep -q "$ERR" "$oklog"; then
    echo "::error::$name passed exit 0 but engine errors in log"
    grep -B1 "$ERR" "$oklog" | head -10
    failed=$((failed + 1))
  fi
done

if [ "$run" -ne "$((total - 1))" ]; then
  echo "::error::assertion scene count mismatch - ran $run, discovered $((total - 1)) (autoplay_test skipped)"
  failed=$((failed + 1))
fi
echo "CI result: $passed passed, $failed failed, $flakes flaky (of $((total - 1)) scenes)"
exit "$failed"