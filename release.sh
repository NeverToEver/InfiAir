#!/usr/bin/env bash
# InfiAir 发布构建：资源导入 → 导出 Linux/Windows → 打包（含安装/卸载脚本）
# 用法：./release.sh           输出 builds/release/InfiAir-<版本>-<平台>.<tar.gz|zip>
# 环境变量：VERSION（默认 3.22）、GODOT（默认 ~/.local/bin/godot，回退 PATH）
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${VERSION:-3.22}"
GODOT="${GODOT:-$HOME/.local/bin/godot}"
command -v "$GODOT" >/dev/null 2>&1 || GODOT="godot"

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
