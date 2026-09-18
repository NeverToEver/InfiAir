#!/usr/bin/env bash
# 无头冒烟（固定步长）：生产 main.tscn 开机全链路直达标题屏。
# Usage: check_smoke.sh [log_path]   (default /tmp/smoke.log)
#
# 判据三件事，缺一不可：a) 退出码为 0；b) 日志无引擎错误；c) 出现完成标记
# `[boot] 标题屏就绪`（由 TitleScreen._Ready 打印）。帧数只是上限——只判「不崩」时，
# 切场景静默失败（路径错/资源缺失）会停在 main 空战场上，零错误退出而看不出来。
# --fixed-fps 60：固定步长让帧数＝模拟时长，且不等真实时间（300 帧＝5 模拟秒）。
#
# 用户目录隔离：本趟走生产读档链，不隔离就会读走开发者本机配置。Windows 读 APPDATA、
# Linux 读 XDG_DATA_HOME、macOS 读 HOME，三处都指到临时目录（macOS 不认前两个，
# user:// 会落到真实用户目录）。
set -uo pipefail

# 引擎探测与 run.sh 同口径：**.NET 版优先**。裸 `godot` 在装了标准版的机器上会命中不含 C# 的
# 那一版（标准版打不开含 C# 的工程，且引擎跑起来会把 InfiAir.csproj 的 Godot.NET.Sdk 版本
# 改写成自己那版，污染工作树）。gates.py/CI 会显式传 GODOT，这里是手跑时的兜底。
if [ -z "${GODOT:-}" ]; then
  for candidate in godot-mono godot godot4; do
    if command -v "$candidate" >/dev/null 2>&1; then GODOT="$candidate"; break; fi
  done
fi
GODOT="${GODOT:-godot}"
LOG="${1:-/tmp/smoke.log}"
FRAMES=300
USERDIR="${LOG%.log}.userdata"

# 引擎错误正则。`Invalid polygon data, triangulation failed.` 是程序化绘制的静默坏点：
# headless 走 dummy 渲染仍会执行 _Draw，自交/退化多边形在该处报错并**整块不画**——不崩、
# 不看日志完全无感。`ERROR:` 是通用前缀，兜住上面未列举的错误类别。
ERR="SCRIPT ERROR\|Parse Error\|Compile Error\|Nonexistent function\|Unhandled exception\|Invalid polygon data\|ERROR:"
# 白名单：退出期资源统计噪声（RefCounted 释放顺序告警，非功能坏点）。
ERR_ALLOW="ERROR: [0-9][0-9]* resources still in use at exit"
# 单趟墙钟上限（秒）：引擎挂死（死锁/等真实时间/驱动卡住）时判失败并杀掉，不无限等待。
# 这是挂死安全阀，不是时长判据——模拟时长仍由 --fixed-fps 60 与帧数决定。
CASE_TIMEOUT="${SMOKE_CASE_TIMEOUT:-240}"

rm -rf "$USERDIR"
mkdir -p "$USERDIR"
win_dir="$USERDIR"
command -v cygpath >/dev/null 2>&1 && win_dir="$(cygpath -w "$USERDIR")"

# 后台跑 + 轮询墙钟上限：Git Bash 的 `timeout` 会落到 Windows 的 timeout.exe（语义完全不同，
# 是等按键），故不依赖外部 timeout 命令。
env "APPDATA=$win_dir" "XDG_DATA_HOME=$USERDIR" "HOME=$USERDIR" \
  "$GODOT" --headless --path . --fixed-fps 60 --quit-after "$FRAMES" > "$LOG" 2>&1 &
pid=$!
waited=0
rc=0
while kill -0 "$pid" 2>/dev/null; do
  if [ "$waited" -ge "$CASE_TIMEOUT" ]; then
    echo "::error::主场景冒烟超过单趟上限 ${CASE_TIMEOUT}s 未退出（挂死）——杀掉引擎并判失败" \
         "（SMOKE_CASE_TIMEOUT 可调上限；模拟时长与帧数无关于此值）"
    kill -9 "$pid" 2>/dev/null
    wait "$pid" 2>/dev/null
    tail -30 "$LOG"
    exit 1
  fi
  sleep 1
  waited=$((waited + 1))
done
wait "$pid" || rc=$?

if [ "$rc" -ne 0 ]; then
  echo "::error::主场景冒烟退出码 ${rc}"
  tail -30 "$LOG"
  exit 1
fi

# 完成标记：切场景静默失败时不会出现，只判「不崩」抓不到。
if ! grep -qF "[boot] 标题屏就绪" "$LOG"; then
  echo "::error::主场景冒烟：日志无完成标记「[boot] 标题屏就绪」——开机没有落到标题屏"
  tail -30 "$LOG"
  exit 1
fi

# 隔离判据：引擎的 user:// 若没落在本趟临时目录内，这里就看不到它写出的 user://logs/godot.log
# ——隔离失效必须显式判红，否则本趟可能已读写开发者本机数据而门禁照旧全绿。
if [ -z "$(find "$USERDIR" -type f -name 'godot.log' -print -quit 2>/dev/null)" ]; then
  echo "::error::用户目录隔离未生效：${USERDIR} 下没有引擎写出的 user:// 日志" \
       "（隔离环境变量没被引擎采纳？引擎版本换了日志落点？）——本趟可能已读写开发者本机数据"
  ls -la "$USERDIR" 2>/dev/null | head -5
  exit 1
fi
rm -rf "$USERDIR"

# 错误行判定先落文件再取内容，不用 `grep | grep -v | grep -q` 管道：错误行极多时 `grep -q`
# 一命中就退出，上游 grep 可能收到 SIGPIPE，pipefail 下整条管道返回非 0（141）而把有错的
# 日志判成无错。模式沿用 BRE（ERR 内是 `\|` 交替）——**不得改成 grep -E**：`\|` 在 ERE 里
# 是字面竖线，全部错误类别会一起失配，门禁静默变成永不报错。
errs="${LOG}.errs"
grep "$ERR" "$LOG" > "$errs" 2>/dev/null
if [ -s "$errs" ]; then
  grep -v "$ERR_ALLOW" "$errs" > "$errs.kept" 2>/dev/null
  if [ -s "$errs.kept" ]; then
    echo "::error::主场景冒烟：日志含引擎错误"
    head -10 "$errs.kept"
    rm -f "$errs" "$errs.kept"
    exit 1
  fi
fi
rm -f "$errs" "$errs.kept"

echo "无头冒烟通过：main.tscn 开机 ${FRAMES} 帧直达标题屏"
