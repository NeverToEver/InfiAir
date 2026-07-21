#!/bin/bash
# InfiAir 双击启动（macOS）
# 若双击无反应：右键 → 打开；或先执行 chmod +x run.command
# 若提示"无法验证开发者"：系统设置 → 隐私与安全性 → 仍要打开
cd "$(dirname "$0")" || exit 1

# 引擎探测：PATH → ~/.local/bin → /Applications/Godot.app
if command -v godot >/dev/null 2>&1; then
    GODOT="godot"
elif [ -x "$HOME/.local/bin/godot" ]; then
    GODOT="$HOME/.local/bin/godot"
elif [ -d "/Applications/Godot.app" ]; then
    GODOT="/Applications/Godot.app/Contents/MacOS/Godot"
else
    echo "[InfiAir] 未找到 Godot 引擎（需要 4.6+，标准版即可）。"
    echo "          下载：https://godotengine.org/download"
    echo "          安装后放到 /Applications 或 ~/.local/bin 均可识别。"
    read -r -p "按回车退出…"
    exit 1
fi

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
exec "$GODOT" --path .
