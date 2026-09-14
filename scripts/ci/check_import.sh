#!/usr/bin/env bash
# 资源导入门禁：导入退出码 + 日志引擎错误。
# Usage: check_import.sh [log_path]   (default /tmp/import.log)
# 判定：导入退出码非 0 → 红；否则日志出现 `ERROR` → 红（附命中行）；干净则回显尾几行。
# 为什么抓日志 ERROR：资源导入静默坏（缺依赖/路径错/资源损坏）不改变退出码，只在日志留 ERROR；
# 旧的 `Warning treated as error` 判据在零 GDScript 后永不出现，是取不到判据的死判据。
set -uo pipefail

GODOT="${GODOT:-godot}"
LOG="${1:-/tmp/import.log}"

if ! "$GODOT" --headless --import --path . > "$LOG" 2>&1; then
  echo "::error::import failed"
  tail -30 "$LOG"
  exit 1
fi
if grep -q "ERROR" "$LOG"; then
  echo "::error::import log contains ERROR:"
  grep -n "ERROR" "$LOG" | head -30
  exit 1
fi
tail -3 "$LOG"
