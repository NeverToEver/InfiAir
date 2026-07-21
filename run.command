#!/bin/bash
# InfiAir 双击启动（macOS）
# 若双击无反应：右键 → 打开；或先执行 chmod +x run.command
cd "$(dirname "$0")" || exit 1

if command -v godot >/dev/null 2>&1; then
    GODOT="godot"
elif [ -x "$HOME/.local/bin/godot" ]; then
    GODOT="$HOME/.local/bin/godot"
elif [ -d "/Applications/Godot.app" ]; then
    GODOT="/Applications/Godot.app/Contents/MacOS/Godot"
else
    echo "未找到 Godot（需要 4.6+）。请先安装：https://godotengine.org/download"
    read -r -p "按回车退出…"
    exit 1
fi

echo "使用引擎: $GODOT"
exec "$GODOT" --path .
