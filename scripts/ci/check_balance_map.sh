#!/usr/bin/env bash
# Balance map generator zero-diff gate (M8).
# Usage: check_balance_map.sh
# docs/BALANCE_MAP.md is regenerated from cfg() call sites (with line numbers);
# code changes not followed by a regen accumulate line-number drift. Regen + zero
# diff blocks stale docs at the gate (runs in full-regression only).
set -uo pipefail

# OLD inline relied on GH Actions default `bash -e` (a failing generator aborted the
# step). We run under `set -uo pipefail` (no -e), so guard explicitly: a generator
# crash must fail the gate, not fall through to a zero-diff against the unchanged
# doc and silently pass.
python3 scripts/tools/gen_balance_map.py || { echo "::error::balance map generator failed"; exit 1; }
if ! git diff --exit-code -- docs/BALANCE_MAP.md; then
  echo "::error::docs/BALANCE_MAP.md stale - after changing cfg() calls run python3 scripts/tools/gen_balance_map.py and commit the artifact"
  git diff -- docs/BALANCE_MAP.md | head -40
  exit 1
fi