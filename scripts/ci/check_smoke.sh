#!/usr/bin/env bash
# 无头冒烟四趟（固定步长、机器速度）+ 事件全周期完成标记断言。
# Usage: check_smoke.sh [log_path]   (default /tmp/smoke.log)
# 四趟覆盖四个「跑不到就发现不了」的面：
#   1) 300 帧基线：开机全链路；
#   2) --settings-probe：设置页各分组在「开页」时才构建，写错＝玩家点开即崩；
#   3) --event-probe=formation_strike / elite_turret：遭遇要过分数门槛 + 掷签，常规冒烟跑不到，
#      而编排/投弹/落点圈/反射弹/结算分支是高密度出错区。
# 判定三件事，缺一不可：
#   a) 退出码为 0；b) 日志无引擎错误；c) 事件那两趟必须出现全周期完成标记
#   ——帧数只是上限，事件中途停摆同样是「零错误退出」，没有标记就是没跑到。
# --fixed-fps 60：固定步长让帧数＝模拟时长，且不等真实时间（编队 700 帧 ≈1s，精英 2500 帧 ≈2s）。
set -uo pipefail

GODOT="${GODOT:-godot}"
LOG="${1:-/tmp/smoke.log}"
ERR="SCRIPT ERROR\|Parse Error\|Compile Error\|Nonexistent function\|Unhandled exception"

run_case() {
  local label="$1" frames="$2" log="$3"
  shift 3
  if ! "$GODOT" --headless --path . --fixed-fps 60 --quit-after "$frames" -- "$@" > "$log" 2>&1; then
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

expect_marker() {
  local label="$1" log="$2" marker="$3"
  if ! grep -qF "$marker" "$log"; then
    echo "::error::$label：日志无完成标记「$marker」——事件没跑完全周期"
    tail -30 "$log"
    exit 1
  fi
  echo "$label: ok"
}

run_case "main scene smoke(300)" 300 "$LOG"
run_case "settings page smoke" 60 "${LOG%.log}.settings.log" --settings-probe
# 编队全周期（入场 1.5s + 转弯 1.2s + 投弹最长 ≈4.95s + 离场 1.5s ≈ 9.1s）取 700 帧
run_case "formation strike smoke" 700 "${LOG%.log}.formation.log" --event-probe=formation_strike
expect_marker "formation strike 全周期" "${LOG%.log}.formation.log" "[event-probe] formation_strike 全周期完成"
# 精英炮塔全周期（入场 2s + 升起 1.5s + 30s 倒计时 + 撤离 ≈1.7s + Boss 恢复 4s ≈ 39.2s）取 2500 帧
run_case "elite turret smoke" 2500 "${LOG%.log}.elite.log" --event-probe=elite_turret
expect_marker "elite turret 全周期" "${LOG%.log}.elite.log" "[event-probe] elite_turret 全周期完成"
