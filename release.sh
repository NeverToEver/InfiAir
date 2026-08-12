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
    echo "环境变量: VERSION（默认 project.godot config/version）、GODOT（默认探测链 godot-mono → ~/.local/bin/godot → PATH）"
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
# 2026-08-07 C# 立项：探测链 .NET 版优先（godot-mono）——含 .cs 工程标准版引擎无法导出
# 显式传 GODOT 时不回退（尊重调用方指定）
if [ -n "${GODOT:-}" ]; then
	command -v "$GODOT" >/dev/null 2>&1 || {
		echo "[release] 未找到指定引擎：$GODOT" >&2
		exit 1
	}
else
	GODOT="$HOME/.local/bin/godot-mono"
	command -v "$GODOT" >/dev/null 2>&1 || GODOT="$HOME/.local/bin/godot"
	command -v "$GODOT" >/dev/null 2>&1 || GODOT="godot-mono"
	command -v "$GODOT" >/dev/null 2>&1 || GODOT="godot"
	# 2026-08-07 环境适配：仅安装 godot4 命名的发行版（如多数 Linux 仓库包）也可直接发布
	command -v "$GODOT" >/dev/null 2>&1 || GODOT="godot4"
fi
# 2026-08-06 审计：GODOT 兜底链断裂无诊断（原回退链末端 command not found 裸报错）——
# 最终探测失败立即给出引擎安装指引（对齐 run.sh 诊断口径）
if ! command -v "$GODOT" >/dev/null 2>&1; then
    echo "[release] 未找到 Godot 引擎：$GODOT（需要 4.6+，推荐 .NET 版）" >&2
    echo "         下载：https://godotengine.org/download 或放置到 ~/.local/bin/" >&2
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

# 2026-08-12 修复（Windows 发布包 logo 后闪退）：Godot .NET 导出强依赖解决方案文件——
# 缺失时 dotnet publish 被静默跳过，导出仍以 exit 0「completed with warnings」收尾，
# 产出不带任何 C# 程序集的空壳包（引擎 logo 后 .NET 初始化失败直接退出，Windows 无控制台无可见
# 报错）。事前硬检查，杜绝带病出包
if [ ! -f InfiAir.sln ]; then
	echo "[release] 缺少 InfiAir.sln（.NET 导出必需）" >&2
	echo "         生成：dotnet new sln -n InfiAir && dotnet sln add InfiAir.csproj csharp/core/InfiAir.Core.csproj tests-csharp/InfiAir.Core.Tests.csproj" >&2
	exit 1
fi

BUILD_DIR="builds"
STAGE_DIR="$BUILD_DIR/stage"
OUT_DIR="$BUILD_DIR/release"

echo "==> 资源导入"
"$GODOT" --headless --import --path .

# 2026-08-12：导出包装函数——Godot 导出即使 C# 构建失败也以 exit 0 收尾（日志仅见 ERROR），
# 仅靠退出码会放行空壳包；故强制扫描日志 ERROR + 校验托管程序集目录存在
export_platform() {
	local preset="$1" out="$2" data_dir="$3" log
	log="$(mktemp)"
	if ! "$GODOT" --headless --path . --export-release "$preset" "$out" >"$log" 2>&1; then
		cat "$log" >&2
		rm -f "$log"
		echo "[release] 导出失败：$preset" >&2
		exit 1
	fi
	if grep -q "^ERROR" "$log"; then
		grep "^ERROR" "$log" | sort -u | head -5 >&2
		rm -f "$log"
		echo "[release] 导出日志含 ERROR（$preset），中止——产物不可信" >&2
		exit 1
	fi
	rm -f "$log"
	# C# 工程导出必须携带托管运行时目录（coreclr + InfiAir.dll 等）；缺失即空壳包
	if [ ! -d "$(dirname "$out")/$data_dir" ]; then
		echo "[release] 导出产物缺少 $data_dir/（$preset）——C# 程序集未随包导出" >&2
		exit 1
	fi
}

echo "==> 导出 Linux/X11"
mkdir -p "$BUILD_DIR/linux"
export_platform "Linux/X11" "$BUILD_DIR/linux/InfiAir.x86_64" "data_InfiAir_linuxbsd_x86_64"

echo "==> 导出 Windows Desktop"
mkdir -p "$BUILD_DIR/windows"
export_platform "Windows Desktop" "$BUILD_DIR/windows/InfiAir.exe" "data_InfiAir_windows_x86_64"

echo "==> 打包"
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR/linux" "$STAGE_DIR/windows" "$OUT_DIR"

# 2026-08-12：托管运行时目录（coreclr + InfiAir.dll 等）必须随包发布——
# 仅拷可执行文件的包在目标机启动 logo 后即闪退（.NET 初始化失败）
cp "$BUILD_DIR/linux/InfiAir.x86_64" "$STAGE_DIR/linux/"
cp -r "$BUILD_DIR/linux/data_InfiAir_linuxbsd_x86_64" "$STAGE_DIR/linux/"
cp packaging/linux/install.sh packaging/linux/uninstall.sh packaging/linux/infiair.desktop "$STAGE_DIR/linux/"
chmod +x "$STAGE_DIR/linux/install.sh" "$STAGE_DIR/linux/uninstall.sh"
tar -C "$STAGE_DIR/linux" -czf "$OUT_DIR/InfiAir-$VERSION-linux-x86_64.tar.gz" .

cp "$BUILD_DIR/windows/InfiAir.exe" "$STAGE_DIR/windows/"
cp -r "$BUILD_DIR/windows/data_InfiAir_windows_x86_64" "$STAGE_DIR/windows/"
cp packaging/windows/install.bat packaging/windows/uninstall.bat "$STAGE_DIR/windows/"
(cd "$STAGE_DIR/windows" && zip -q -r "$(cd ../../.. && pwd)/$OUT_DIR/InfiAir-$VERSION-windows-x86_64.zip" .)

rm -rf "$STAGE_DIR"
echo "==> 完成"
ls -lh "$OUT_DIR"
