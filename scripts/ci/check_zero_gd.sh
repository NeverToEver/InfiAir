#!/usr/bin/env bash
# Zero-GDScript gate (M7d): any .gd file (outside .godot/ and builds/) fails.
# Ported 1:1 from ci.yml fast-gate "Zero-GDScript gate". Single source of truth:
# CI calls this; local runs `bash scripts/ci/check_zero_gd.sh`.
set -uo pipefail

leftover=$(find . -name "*.gd" -not -path "./.godot/*" -not -path "./builds/*" | wc -l)
echo "remaining .gd files: $leftover"
if [ "$leftover" -ne 0 ]; then
  echo "::error::zero-GDScript gate failed - .gd files present (forbidden since full C# migration M7):"
  find . -name "*.gd" -not -path "./.godot/*" -not -path "./builds/*"
  exit 1
fi
exit 0