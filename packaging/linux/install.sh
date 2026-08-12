#!/usr/bin/env bash
# InfiAir Linux 安装脚本（用户态安装，无需 root）
# 用法：./install.sh
set -euo pipefail

APP_ID="infiair"
APP_NAME="InfiAir"
BINARY="InfiAir.x86_64"
# C# 托管运行时目录（coreclr + InfiAir.dll 等），必须与可执行文件同目录安装
DATA_DIR="data_InfiAir_linuxbsd_x86_64"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
INSTALL_DIR="$DATA_HOME/$APP_ID"
BIN_DIR="$HOME/.local/bin"
APPLICATIONS_DIR="$DATA_HOME/applications"

if [[ ! -f "$SCRIPT_DIR/$BINARY" ]]; then
	echo "错误：未在脚本旁找到 $BINARY" >&2
	exit 1
fi
if [[ ! -d "$SCRIPT_DIR/$DATA_DIR" ]]; then
	echo "错误：未在脚本旁找到 $DATA_DIR/（C# 运行时，缺失会导致启动后即崩溃）" >&2
	exit 1
fi

mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$APPLICATIONS_DIR"
cp "$SCRIPT_DIR/$BINARY" "$INSTALL_DIR/$BINARY"
cp -r "$SCRIPT_DIR/$DATA_DIR" "$INSTALL_DIR/$DATA_DIR"
chmod +x "$INSTALL_DIR/$BINARY"

# 命令行入口
ln -sf "$INSTALL_DIR/$BINARY" "$BIN_DIR/$APP_ID"

# 桌面入口（Exec 使用安装后的绝对路径）
sed "s|@INSTALL_DIR@|$INSTALL_DIR|g" "$SCRIPT_DIR/$APP_ID.desktop" \
	> "$APPLICATIONS_DIR/$APP_ID.desktop"
chmod 644 "$APPLICATIONS_DIR/$APP_ID.desktop"

if command -v update-desktop-database >/dev/null 2>&1; then
	update-desktop-database "$APPLICATIONS_DIR" || true
fi

echo "已安装 $APP_NAME："
echo "  程序目录  $INSTALL_DIR"
echo "  命令入口  $BIN_DIR/$APP_ID"
echo "  桌面入口  $APPLICATIONS_DIR/$APP_ID.desktop"
echo "卸载：运行本包中的 uninstall.sh"
