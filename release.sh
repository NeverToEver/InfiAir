#!/usr/bin/env bash
# InfiAir 发布构建：资源导入 → 导出 Linux/Windows → 打包（含安装/卸载脚本）
# 用法：./release.sh           输出 builds/release/InfiAir-<版本>-<平台>.<tar.gz|zip>
#       ./release.sh --help   显示用法后退出
# 环境变量：VERSION（默认读取 project.godot config/version）、GODOT（默认探测链 godot-mono → ~/.local/bin/godot → PATH 的 godot/godot4）
set -euo pipefail
cd "$(dirname "$0")"

# ${1:-} 兼容无参数调用（set -u 下裸 $1 报 unbound variable）
PUBLISH=0
for arg in "$@"; do
    case "$arg" in
        --help|-h)
            echo "用法: ./release.sh [--publish]"
            echo "  默认       导出+打包到 builds/release/"
            echo "  --publish  打包后继续发布：推送 main → 打 tag v<版本> → 建 GitHub Release → 上传资产"
            echo "             （发布策略 2026-09-07 起为本地编译；发布通道为 GitHub API，替代原 release.yml 工作流）"
            echo "环境变量: VERSION（默认 project.godot config/version）、GODOT（探测链 godot-mono → ~/.local/bin/godot → PATH）、"
            echo "          GITHUB_TOKEN（--publish 可选；缺省经 git credential fill 取 github.com 已存凭据）"
            exit 0
            ;;
        --publish) PUBLISH=1 ;;
        *) echo "[release] 未知参数: $arg（--help 查看用法）" >&2; exit 1 ;;
    esac
done

# R07：版本号自动读取 project.godot（L 系列工具链登记遗留）——本地跑 release.sh 忘传
# VERSION 不再产出与项目版本不符的包名；sed 取不到时硬失败并提示显式传 VERSION
# （原回退 3.26 会静默产出与 project.godot 脱节的包名，2026-08-06 规范化修正）
VERSION="${VERSION:-$(sed -n 's/^config\/version="\([^"]*\)"/\1/p' project.godot)}"
if [ -z "$VERSION" ]; then
    echo "[release] 无法从 project.godot 读取 config/version；请显式传入 VERSION=x.y" >&2
    exit 1
fi

if [ "$PUBLISH" = 1 ]; then
    # 发布前置检查：干净工作树、版本格式、tag 未占用、发布凭据与仓库定位
    # （快速失败——任何一项不过都不该白白跑完十来分钟导出）
    [ -z "$(git status --porcelain)" ] || {
        echo "[release] --publish 要求干净工作树（发布内容必须先提交）" >&2; exit 1; }
    [[ "$VERSION" =~ ^[0-9]+\.[0-9]+$ ]] || {
        echo "[release] --publish 版本号须为 MAJOR.MINOR：$VERSION" >&2; exit 1; }
    # 推送通道固定走 HTTPS + 已存凭据（origin 可能是本机不可用的 SSH 形态）；slug 从 origin 推导
    REPO_SLUG=$(printf '%s' "$(git remote get-url origin)" | sed -E 's#.*github\.com[:/]##; s#\.git$##')
    PUSH_URL="https://github.com/$REPO_SLUG.git"
    if git ls-remote --exit-code --tags "$PUSH_URL" "refs/tags/v$VERSION" >/dev/null 2>&1; then
        echo "[release] tag v$VERSION 已存在于 origin，换一个版本号" >&2; exit 1
    fi
    if [ -z "$GITHUB_TOKEN" ]; then
        GITHUB_TOKEN=$(printf "protocol=https
host=github.com
" | GIT_TERMINAL_PROMPT=0 git credential fill 2>/dev/null | sed -n 's/^password=//p')
    fi
    [ -n "$GITHUB_TOKEN" ] || {
        echo "[release] 未取得 GitHub 凭据：设 GITHUB_TOKEN，或在凭据管理器保存 github.com 凭据" >&2; exit 1; }
    echo "==> --publish 模式：版本 v$VERSION，前置检查通过"
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
command -v tar >/dev/null 2>&1 || {
	echo "[release] 缺少打包工具: tar" >&2
	exit 1
}
# zip 打包器回退链（Windows git-bash 常无 zip）：zip → 7z → bsdtar（系统 libarchive tar 支持 zip 格式）
ZIP_TOOL=""
if command -v zip >/dev/null 2>&1; then
	ZIP_TOOL="zip"
elif command -v 7z >/dev/null 2>&1; then
	ZIP_TOOL="7z"
elif [ -x "/c/Windows/System32/tar.exe" ]; then
	ZIP_TOOL="bsdtar"
fi
[ -n "$ZIP_TOOL" ] || {
	echo "[release] 缺少 zip 打包器：无 zip/7z，且未找到 /c/Windows/System32/tar.exe（bsdtar）" >&2
	exit 1
}

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
	# Godot 4.6 headless 导出在引擎退出阶段可能打印「RID allocations leaked at exit」
	# （dummy 渲染器 teardown 噪音，发生在产物落盘之后，与包内容无关——审计档案有
	# 基线对照先例）；精确豁免该行，其余 ^ERROR 仍中止（防空壳包的门禁语义不变）
	if grep -vE "^ERROR: [0-9]+ RID allocations? of type .* leaked at exit" "$log" | grep -q "^ERROR"; then
		grep -vE "^ERROR: [0-9]+ RID allocations? of type .* leaked at exit" "$log" | grep "^ERROR" | sort -u | head -5 >&2
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
ZIP_OUT="$(pwd)/$OUT_DIR/InfiAir-$VERSION-windows-x86_64.zip"
	case "$ZIP_TOOL" in
		zip)    (cd "$STAGE_DIR/windows" && zip -q -r "$ZIP_OUT" .) ;;
		7z)     (cd "$STAGE_DIR/windows" && 7z a -tzip -bso0 "$ZIP_OUT" .) ;;
		bsdtar) (cd "$STAGE_DIR/windows" && /c/Windows/System32/tar.exe --format zip -cf "$ZIP_OUT" .) ;;
	esac

rm -rf "$STAGE_DIR"
echo "==> 打包完成"
ls -lh "$OUT_DIR"

if [ "$PUBLISH" = 1 ]; then
	# 本地编译发布（替代原 release.yml 工作流）：推送 main → tag → 建 Release → 上传资产
	API="https://api.github.com/repos/NeverToEver/InfiAir"
	# 资产上传必须走 uploads.github.com 专用主机（api.github.com 上该路径 404）
	UPLOAD="https://uploads.github.com/repos/NeverToEver/InfiAir"

	echo "==> 推送 main 与 tag v$VERSION"
	git push "$PUSH_URL" HEAD:main
	git tag "v$VERSION"
	git push "$PUSH_URL" "v$VERSION"

	NOTES_FILE="$(mktemp)"
	# 发布说明取 CHANGELOG.md 对应版本章节
	# index() 前缀匹配而非正则——版本号中的 `.` 与章节名的 `[]` 免转义，且不会被当 ERE 元字符
	awk -v sec="## [$VERSION]" 'index($0, sec) == 1 { flag = 1; next } flag && /^## /{ exit } flag { print }' CHANGELOG.md > "$NOTES_FILE"
	[ -s "$NOTES_FILE" ] || echo "[release] 提示：CHANGELOG.md 未找到 [$VERSION] 章节，发布说明为空" >&2
	PAYLOAD=$(python3 -c '
import json, sys
print(json.dumps({"tag_name": "v" + sys.argv[1], "name": "InfiAir v" + sys.argv[1], "body": sys.stdin.read()}))' "$VERSION" < "$NOTES_FILE")

	echo "==> 创建 GitHub Release"
	RESP_FILE="$(mktemp)"
	CODE=$(curl -s -o "$RESP_FILE" -w "%{http_code}" -X POST 		-H "Authorization: token $GITHUB_TOKEN" -H "Accept: application/vnd.github+json" 		-d "$PAYLOAD" "$API/releases")
	[ "$CODE" = "201" ] || { echo "[release] 建 Release 失败 HTTP $CODE" >&2; cat "$RESP_FILE" >&2; exit 1; }
	RELEASE_ID=$(python3 -c 'import json, sys; print(json.load(sys.stdin)["id"])' < "$RESP_FILE")
	echo "    release id=$RELEASE_ID"

	echo "==> 上传资产"
	for ASSET in "$OUT_DIR/InfiAir-$VERSION-linux-x86_64.tar.gz" "$OUT_DIR/InfiAir-$VERSION-windows-x86_64.zip"; do
		ANAME=$(basename "$ASSET")
		ACODE=$(curl -s -o /dev/null -w "%{http_code}" -X POST 			-H "Authorization: token $GITHUB_TOKEN" -H "Content-Type: application/octet-stream" 			--data-binary @"$ASSET" 			"$UPLOAD/releases/$RELEASE_ID/assets?name=$ANAME")
		[ "$ACODE" = "201" ] || { echo "[release] 上传 $ANAME 失败 HTTP $ACODE" >&2; exit 1; }
		echo "    已上传 $ANAME"
	done
	echo "==> 发布完成：https://github.com/NeverToEver/InfiAir/releases/tag/v$VERSION"
fi
