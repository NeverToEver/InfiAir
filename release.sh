#!/usr/bin/env bash
# InfiAir 发布构建：资源导入 → 导出 Linux/Windows → 打包（含安装/卸载脚本）
# 用法：./release.sh           输出 builds/release/InfiAir-<版本>-<平台>.<tar.gz|zip>
# 环境变量：VERSION（默认读取 project.godot config/version）、GODOT（默认 ~/.local/bin/godot，回退 PATH）
set -euo pipefail
cd "$(dirname "$0")"

# R07：版本号自动读取 project.godot（L 系列工具链登记遗留）——本地跑 release.sh 忘传
# VERSION 不再产出与项目版本不符的包名（sed 取不到时回退 3.26）
VERSION="${VERSION:-$(sed -n 's/^config\/version="\([^"]*\)"/\1/p' project.godot)}"
VERSION="${VERSION:-3.26}"
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

# R07：打包工具前置检查（L 系列工具链登记遗留）——缺 tar/zip 时 set -e 中止但
# 无诊断且 stage 残留；提前检查给出明确报错
for tool in tar zip; do
	command -v "$tool" >/dev/null 2>&1 || {
		echo "[release] 缺少打包工具: $tool（macOS: brew install $tool）"
		exit 1
	}
done

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
