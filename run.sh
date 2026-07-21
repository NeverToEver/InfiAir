#!/bin/bash
# InfiAir 启动脚本（Linux / macOS 终端）
cd "$(dirname "$0")" || exit 1

if command -v godot >/dev/null 2>&1; then
    GODOT="godot"
elif [ -x "$HOME/.local/bin/godot" ]; then
    GODOT="$HOME/.local/bin/godot"
elif [ -d "/Applications/Godot.app" ]; then
    GODOT="/Applications/Godot.app/Contents/MacOS/Godot"
else
    echo "未找到 Godot（需要 4.6+）。请先安装：https://godotengine.org/download"
    exit 1
fi

exec "$GODOT" --path . "$@"
