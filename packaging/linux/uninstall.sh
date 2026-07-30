#!/usr/bin/env bash
# InfiAir Linux 卸载脚本
# 用法：./uninstall.sh [--purge]
#   --purge  同时删除存档与设置（默认保留）
set -euo pipefail

APP_ID="infiair"
APP_NAME="InfiAir"
BINARY="InfiAir.x86_64"

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
INSTALL_DIR="$DATA_HOME/$APP_ID"
BIN_DIR="$HOME/.local/bin"
APPLICATIONS_DIR="$DATA_HOME/applications"
USER_DATA_DIR="$DATA_HOME/godot/app_userdata/$APP_NAME"

purge=0
for arg in "$@"; do
	case "$arg" in
		--purge) purge=1 ;;
		*) echo "未知参数：$arg（仅支持 --purge）" >&2; exit 2 ;;
	esac
done

rm -rf "$INSTALL_DIR"
rm -f "$BIN_DIR/$APP_ID"
rm -f "$APPLICATIONS_DIR/$APP_ID.desktop"

if command -v update-desktop-database >/dev/null 2>&1; then
	update-desktop-database "$APPLICATIONS_DIR" || true
fi

echo "已卸载 $APP_NAME。"
if [[ "$purge" -eq 1 ]]; then
	rm -rf "$USER_DATA_DIR"
	echo "已删除存档与设置：$USER_DATA_DIR"
else
	echo "存档与设置保留在 $USER_DATA_DIR（如需删除请运行 $0 --purge）"
fi
