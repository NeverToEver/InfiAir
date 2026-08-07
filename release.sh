#!/usr/bin/env bash
# InfiAir 发布构建：资源导入 → 导出 Linux/Windows → 打包（含安装/卸载脚本）
# 用法：./release.sh           输出 builds/release/InfiAir-<版本>-<平台>.<tar.gz|zip>
#       ./release.sh --help   显示用法后退出
# 环境变量：VERSION（默认读取 project.godot config/version）、GODOT（默认 ~/.local/bin/godot，回退 PATH 的 godot/godot4）
set -euo pipefail
cd "$(dirname "$0")"

# 2026-08-06 审计：--help（违反 .agents/shell-scripts.md 自约——「支持 --help」）；
# ${1:-} 兼容无参数调用（set -u 下裸 $1 报 unbound variable）
if [ "${1:-}" = "--help" ] || [ "${1:-}" = "-h" ]; then
    echo "用法: ./release.sh [--help]"
    echo "输出: builds/release/InfiAir-<版本>-<平台>.<tar.gz|zip>"
    echo "环境变量: VERSION（默认 project.godot config/version）、GODOT（默认 ~/.local/bin/godot，回退 PATH 的 godot/godot4）"
    exit 0
fi

# R07：版本号自动读取 project.godot（L 系列工具链登记遗留）——本地跑 release.sh 忘传
# VERSION 不再产出与项目版本不符的包名；sed 取不到时硬失败并提示显式传 VERSION
# （原回退 3.26 会静默产出与 project.godot 脱节的包名，2026-08-06 规范化修正）
VERSION="${VERSION:-$(sed -n 's/^config\/version="\([^"]*\)"/\1/p' project.godot)}"
if [ -z "$VERSION" ]; then
    echo "[release] 无法从 project.godot 读取 config/version；请显式传入 VERSION=x.y" >&2
    exit 1
fi
GODOT="${GODOT:-$HOME/.local/bin/godot}"
command -v "$GODOT" >/dev/null 2>&1 || GODOT="godot"
# 2026-08-07 环境适配：仅安装 godot4 命名的发行版（如多数 Linux 仓库包）也可直接发布
command -v "$GODOT" >/dev/null 2>&1 || GODOT="godot4"
# 2026-08-06 审计：GODOT 兜底链断裂无诊断（原回退链末端 command not found 裸报错）——
# 最终探测失败立即给出引擎安装指引（对齐 run.sh 诊断口径）
if ! command -v "$GODOT" >/dev/null 2>&1; then
    echo "[release] 未找到 Godot 引擎：$GODOT（需要 4.6+ 标准版）" >&2
    echo "         下载：https://godotengine.org/download 或放置到 ~/.local/bin/godot" >&2
    exit 1
fi

# 2026-08-06 审计：打包工具前置检查移到导出之前（原位于两次导出之后——
# 缺 tar/zip 时白白跑完两次导出才报错，stage 残留）
for tool in tar zip; do
	command -v "$tool" >/dev/null 2>&1 || {
		echo "[release] 缺少打包工具: $tool（macOS: brew install $tool）"
		exit 1
	}
done

BUILD_DIR="builds"
STAGE_DIR="$BUILD_DIR/stage"
OUT_DIR="$BUILD_DIR/release"

echo "==> 资源导入"
"$GODOT" --headless --import --path .

echo "==> 导出 Linux/X11"
mkdir -p "$BUILD_DIR/linux"
"$GODOT" --headless --path . --export-release "Linux/X11" "$BUILD_DIR/linux/InfiAir.x86_64"

echo "==> 导出 Windows Desktop"
mkdir -p "$BUILD_DIR/windows"
"$GODOT" --headless --path . --export-release "Windows Desktop" "$BUILD_DIR/windows/InfiAir.exe"

echo "==> 打包"
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR/linux" "$STAGE_DIR/windows" "$OUT_DIR"

cp "$BUILD_DIR/linux/InfiAir.x86_64" "$STAGE_DIR/linux/"
cp packaging/linux/install.sh packaging/linux/uninstall.sh packaging/linux/infiair.desktop "$STAGE_DIR/linux/"
chmod +x "$STAGE_DIR/linux/install.sh" "$STAGE_DIR/linux/uninstall.sh"
tar -C "$STAGE_DIR/linux" -czf "$OUT_DIR/InfiAir-$VERSION-linux-x86_64.tar.gz" .

cp "$BUILD_DIR/windows/InfiAir.exe" "$STAGE_DIR/windows/"
cp packaging/windows/install.bat packaging/windows/uninstall.bat "$STAGE_DIR/windows/"
(cd "$STAGE_DIR/windows" && zip -q -r "$(cd ../../.. && pwd)/$OUT_DIR/InfiAir-$VERSION-windows-x86_64.zip" .)

rm -rf "$STAGE_DIR"
echo "==> 完成"
ls -lh "$OUT_DIR"
