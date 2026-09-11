#!/usr/bin/env bash
# Main scene smoke (300 frames) + settings page build smoke + encounter event smoke + engine-error scan.
# Usage: check_smoke.sh [log_path]   (default /tmp/smoke.log)
# Ported 1:1 from ci.yml fast-gate "Main scene smoke (300 frames)" + W-series
# error scan: exit 0 with "SCRIPT ERROR|Parse Error|Compile Error|Nonexistent
# function|Unhandled exception" in log also fails.
# 三趟覆盖三个「跑不到就发现不了」的面：
#   1) 300 帧基线：开机全链路；
#   2) --settings-probe：设置页各分组在「开页」时才构建，写错＝玩家点开即崩；
#   3) --event-probe：遭遇事件要过分数门槛 + 掷签，无头跑不到，而编排/投弹/清场是高密度出错区。
set -uo pipefail

GODOT="${GODOT:-godot}"
LOG="${1:-/tmp/smoke.log}"
ERR="SCRIPT ERROR\|Parse Error\|Compile Error\|Nonexistent function\|Unhandled exception"

run_case() {
  local label="$1" frames="$2" log="$3"
  shift 3
  if ! "$GODOT" --headless --path . --quit-after "$frames" -- "$@" > "$log" 2>&1; then
    echo "::error::$label failed"
    tail -30 "$log"
    exit 1
  fi
  if grep -q "$ERR" "$log"; then
    echo "::error::$label engine errors in log"
    grep -B1 "$ERR" "$log" | head -10
    exit 1
  fi
  echo "$label: ok"
}

run_case "main scene smoke(300)" 300 "$LOG"
run_case "settings page smoke" 60 "${LOG%.log}.settings.log" --settings-probe
# 编队事件全周期（入场 1s + 转弯 1.2s + 投弹 ≈3s + 离场 1.5s）取 400 帧留余量
run_case "formation strike smoke" 400 "${LOG%.log}.formation.log" --event-probe=formation_strike
