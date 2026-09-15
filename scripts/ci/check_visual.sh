#!/usr/bin/env bash
# 截图探针门禁（辅助）：固定帧捕获 HUD 与五张设置页，存 PNG 供人工/文档使用；
# 探针内部做两条廉价自检（画面非空白、五页互不相同），全过才打完成标记。
# Usage: check_visual.sh [log_path]           (default /tmp/visual.log)
#        KEEP_SHOTS=<dir> check_visual.sh      # 把 PNG 留到该目录（生成截图用）
#
# 定位：**辅助手段，不是像素级回归**。占位内容（帧率/垂直同步读出、背景星空动画）
# 本就随环境变化，跨渲染器比像素必然误报；细粒度退化归 core 单测与断言探针。
# 本门禁只抓「渲染整体坏掉/页面切换失效」这类结构性故障。
#
# 需要真实渲染器（headless dummy 截不到画面）：本机走 GPU；Linux 无 DISPLAY 时走
# Xvfb + Mesa 软件光栅（系统包，非项目代码依赖）。
set -uo pipefail

GODOT="${GODOT:-godot}"
LOG="${1:-/tmp/visual.log}"
SHOT_DIR="$(mktemp -d)"
FRAMES=340
# 引擎错误正则：与 check_smoke.sh 同口径。`ERROR:` 是通用引擎错误前缀，兜住未列举的错误类别
# （此前只有 SCRIPT ERROR 等五类：截图写出失败、`Invalid polygon data` 这类静默坏点全漏判——
# 探针只打一条 PushError 就跳过该图，日志有 ERROR 而正则抓不到，门禁照样判绿）。
ERR="SCRIPT ERROR|Parse Error|Compile Error|Nonexistent function|Unhandled exception|Invalid polygon data|ERROR:"
# 白名单：退出期资源统计噪声（RefCounted 释放顺序告警，非功能坏点），口径同 check_smoke.sh
ERR_ALLOW="ERROR: [0-9][0-9]* resources still in use at exit"

cleanup() { rm -rf "$SHOT_DIR"; }
trap cleanup EXIT

# Git Bash 下 bash 与 Godot 均为原生 Windows 程序：显式转 Windows 路径（不依赖 MSYS 静默转换）
SHOT_DIR_ARG="$SHOT_DIR"
if command -v cygpath >/dev/null 2>&1; then
  SHOT_DIR_ARG="$(cygpath -w "$SHOT_DIR")"
fi

# 渲染器：Linux 无 DISPLAY 时用 Xvfb + 软件光栅（CI runner 无 GPU）
RENDER_ENV=()
if [ "$(uname -s)" = "Linux" ]; then
  if [ -z "${DISPLAY:-}" ]; then
    command -v xvfb-run >/dev/null 2>&1 || {
      echo "::error::截图探针需要显示环境：无 DISPLAY 且无 xvfb-run（系统依赖：xvfb + mesa）" >&2
      exit 1
    }
    RENDER_ENV=(xvfb-run -a -s "-screen 0 1920x1080x24")
  fi
  RENDER_ENV+=("env" "LIBGL_ALWAYS_SOFTWARE=1")
fi

# 窗口移出可视区，本机跑时不在用户眼前弹窗
if ! "${RENDER_ENV[@]}" "$GODOT" --path . --resolution 1920x1080 --position -4000,-4000 \
  --fixed-fps 60 --quit-after "$FRAMES" --scene res://scenes/probe_host.tscn \
  -- --shot-probe --shot-dir="$SHOT_DIR_ARG" > "$LOG" 2>&1; then
  echo "::error::截图探针运行失败（Godot 退出码非 0）——崩溃/启动即失败时探针根本不执行"
  tail -30 "$LOG"
  exit 1
fi

if ! grep -qF "[shot-probe] 截图序列完成" "$LOG"; then
  echo "::error::截图探针未通过（缺完成标记）——画面空白/页面未切换/捕获张数不足/帧数不足"
  grep -F "[shot-probe]" "$LOG" | head -10
  tail -20 "$LOG"
  exit 1
fi
if grep -E "$ERR" "$LOG" | grep -vE "$ERR_ALLOW" | grep -q .; then
  echo "::error::截图探针日志有引擎错误"
  grep -E "$ERR" "$LOG" | grep -vE "$ERR_ALLOW" | head -10
  exit 1
fi

# 生成截图模式：把 PNG 留到指定目录
if [ -n "${KEEP_SHOTS:-}" ]; then
  mkdir -p "$KEEP_SHOTS"
  cp "$SHOT_DIR"/*.png "$KEEP_SHOTS"/ 2>/dev/null || true
  echo "截图已留到：$KEEP_SHOTS"
fi

echo "截图探针：ok"
