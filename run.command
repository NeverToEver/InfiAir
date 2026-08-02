#!/bin/bash
# InfiAir 双击启动（macOS）+ 终端参数透传（2026-08-02 与 run.sh 对齐）
# 双击启动：直接开游戏；异常退出保留窗口与输出，方便排查。
# 终端用法：./run.command [引擎参数]——与 run.sh 同一参数协议：
#           例：./run.command --editor              （打开编辑器）
#               ./run.command --headless --quit-after 300（无头快速启动自检）
# 若双击无反应：右键 → 打开；或先执行 chmod +x run.command
# 若提示"无法验证开发者"：系统设置 → 隐私与安全性 → 仍要打开
cd "$(dirname "$0")" || exit 1

# 引擎候选：PATH → ~/.local/bin → /Applications → ~/Applications（含 Godot*.app 变体名）
CANDIDATES=()
add_candidate() {
    local c
    for c in ${CANDIDATES[@]+"${CANDIDATES[@]}"}; do
        [ "$c" = "$1" ] && return
    done
    CANDIDATES+=("$1")
}
if command -v godot >/dev/null 2>&1; then add_candidate "godot"; fi
[ -x "$HOME/.local/bin/godot" ] && add_candidate "$HOME/.local/bin/godot"
for app in "/Applications/Godot.app" "$HOME/Applications/Godot.app" \
    /Applications/Godot*.app "$HOME"/Applications/Godot*.app; do
    bin="$app/Contents/MacOS/Godot"
    [ -x "$bin" ] && add_candidate "$bin"
done

# 版本判定：4.6+ 返回 0；版本号无法解析视为不满足（避免误用 Godot 3 / 4.5）
version_ok() {
    local ver major rest minor
    ver="$("$1" --version 2>/dev/null | head -n1)"
    [[ "$ver" =~ ^[0-9]+\.[0-9]+ ]] || return 1
    major="${ver%%.*}"
    rest="${ver#*.}"
    minor="${rest%%.*}"
    { [ "$major" -gt 4 ] || { [ "$major" -eq 4 ] && [ "$minor" -ge 6 ]; }; }
}

# 优先选第一个 4.6+ 的候选；全部不满足时回退第一个候选并警告
GODOT=""
for c in ${CANDIDATES[@]+"${CANDIDATES[@]}"}; do
    if version_ok "$c"; then
        GODOT="$c"
        break
    fi
done
if [ -z "$GODOT" ] && [ ${#CANDIDATES[@]} -gt 0 ]; then
    GODOT="${CANDIDATES[0]}"
    VER="$("$GODOT" --version 2>/dev/null | head -n1)"
    echo "[InfiAir] 警告：只检测到 Godot ${VER:-未知版本}，本项目按 4.6+ 构建，可能无法正常运行。"
fi

if [ -z "$GODOT" ]; then
    echo "[InfiAir] 未找到 Godot 引擎（需要 4.6+，标准版即可）。"
    echo "          下载：https://godotengine.org/download"
    echo "          安装到 /Applications 或 ~/Applications（改带版本的名字也能识别），"
    echo "          或将 godot 加入 PATH / 放置到 ~/.local/bin/godot。"
    read -r -p "按回车退出…"
    exit 1
fi

VER="$("$GODOT" --version 2>/dev/null | head -n1)"
echo "[InfiAir] 使用引擎：$GODOT（$VER）"
if [ $# -gt 0 ]; then
    echo "[InfiAir] 透传参数：$*"
fi

# 不用 exec：异常退出时保留窗口与输出，方便排查。
# 双击无参数时等价纯启动；终端传入的引擎参数（--editor / --headless 验证等）原样透传，
# 与 run.sh 参数协议一致（--help 也由引擎处理）。
"$GODOT" --path . "$@"
CODE=$?
if [ "$CODE" -ne 0 ]; then
    echo "[InfiAir] 游戏异常退出（代码 $CODE），可把上方输出截图反馈。"
    read -r -p "按回车关闭窗口…"
fi
exit "$CODE"
