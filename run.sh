#!/bin/bash
# InfiAir 启动脚本（Linux / macOS 终端）
# 用法：./run.sh            直接启动游戏
#       ./run.sh --editor   打开 Godot 编辑器
#       ./run.sh --help     查看引擎参数（其余参数原样透传给引擎）
set -e
cd "$(dirname "$0")" || exit 1

# 引擎探测：PATH → ~/.local/bin → macOS /Applications
if command -v godot >/dev/null 2>&1; then
    GODOT="godot"
elif [ -x "$HOME/.local/bin/godot" ]; then
    GODOT="$HOME/.local/bin/godot"
elif [ -d "/Applications/Godot.app" ]; then
    GODOT="/Applications/Godot.app/Contents/MacOS/Godot"
else
    echo "[InfiAir] 未找到 Godot 引擎（需要 4.6+，标准版即可）。"
    echo "          下载：https://godotengine.org/download"
    echo "          或将 godot 加入 PATH / 放置到 ~/.local/bin/godot"
    exit 1
fi

# 版本检查：低于 4.6 提示但尝试继续（仅警告不阻断）
VER="$("$GODOT" --version 2>/dev/null | head -n1)"
if [ -n "$VER" ]; then
    MAJOR="${VER%%.*}"
    REST="${VER#*.}"
    MINOR="${REST%%.*}"
    if [ "$MAJOR" -lt 4 ] || { [ "$MAJOR" -eq 4 ] && [ "$MINOR" -lt 6 ]; }; then
        echo "[InfiAir] 警告：检测到 Godot $VER，本项目按 4.6+ 构建，可能无法正常运行。"
    fi
fi

echo "[InfiAir] 使用引擎：$GODOT（$VER）"
exec "$GODOT" --path . "$@"
