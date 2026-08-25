#!/usr/bin/env bash
# Balance map generator zero-diff gate (M8).
# Usage: check_balance_map.sh
# docs/BALANCE_MAP.md is regenerated from cfg() call sites (with line numbers);
# code changes not followed by a regen accumulate line-number drift. Regen + zero
# diff blocks stale docs at the gate (runs in full-regression only).
set -uo pipefail

python3 scripts/tools/gen_balance_map.py
if ! git diff --exit-code -- docs/BALANCE_MAP.md; then
  echo "::error::docs/BALANCE_MAP.md stale - after changing cfg() calls run python3 scripts/tools/gen_balance_map.py and commit the artifact"
  git diff -- docs/BALANCE_MAP.md | head -40
  exit 1
fi