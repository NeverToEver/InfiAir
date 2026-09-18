#!/usr/bin/env bash
# InfiAir 发布构建：资源导入 → 导出 Linux/Windows → 打包（含安装/卸载脚本）
# 用法：./release.sh           输出 builds/release/InfiAir-<版本>-<平台>.<tar.gz|zip>
#       ./release.sh --publish 打包后继续发布（发布前置见 --help：人工验收清零 + 全量质量门禁）
#       ./release.sh --help   显示用法后退出
# 环境变量：VERSION（默认读取 project.godot config/version）、GODOT（默认探测链 godot-mono → ~/.local/bin → PATH 的 godot/godot4）
set -euo pipefail
cd "$(dirname "$0")"

# ${1:-} 兼容无参数调用（set -u 下裸 $1 报 unbound variable）
PUBLISH=0
SKIP_GATES=0
for arg in "$@"; do
    case "$arg" in
        --help|-h)
            echo "用法: ./release.sh [--publish] [--skip-gates]"
            echo "  默认         导出+打包到 builds/release/"
            echo "  --publish    打包后继续发布：推送 main → 打 tag v<版本> → 建 GitHub Release → 上传资产"
            echo "               （发布策略为本地编译；发布通道为 GitHub API，替代旧 release.yml 工作流）"
            echo "               发布前置（任一不过即非零退出，且都发生在导出之前）："
            echo "                 ① 干净工作树；版本号 MAJOR.MINOR 且与 project.godot config/version 一致"
            echo "                 ② origin 指向发布通道（推送目标与 Release 同源，防推错地方/建错 Release）"
            echo "                 ③ tag v<版本> 未被占用；已取得 GitHub 凭据"
            echo "                 ④ docs/ROADMAP.md「发布前人工验收」清零（AGENTS §6：不清零不发布）"
            echo "                 ⑤ 质量门禁全绿：python3 scripts/ci/gates.py（四步主干，本机实测约 10 秒）"
            echo "  --skip-gates 显式跳过前置 ⑤（质量门禁）——只该在门禁刚跑过、工作区未变时用，输出会留痕"
            echo "  -h, --help   显示本帮助"
            echo "环境变量: VERSION（默认 project.godot config/version）、GODOT（探测链 godot-mono → ~/.local/bin → PATH）、"
            echo "          GITHUB_TOKEN（--publish 可选；缺省经 git credential fill 取 github.com 已存凭据）"
            exit 0
            ;;
        --publish) PUBLISH=1 ;;
        --skip-gates) SKIP_GATES=1 ;;
        *) echo "[release] 未知参数: ${arg}（--help 查看用法）" >&2; exit 1 ;;
    esac
done
if [ "$SKIP_GATES" = 1 ] && [ "$PUBLISH" = 0 ]; then
    echo "[release] --skip-gates 只对 --publish 生效（默认打包本来就不跑发布前置）" >&2
fi

# R07：版本号自动读取 project.godot（L 系列工具链登记遗留）——本地跑 release.sh 忘传
# VERSION 不再产出与项目版本不符的包名；sed 取不到时硬失败并提示显式传 VERSION
# （回退硬编码版本号会静默产出与 project.godot 脱节的包名）
PROJECT_VERSION="$(sed -n 's/^config\/version="\([^"]*\)"/\1/p' project.godot)"
VERSION="${VERSION:-$PROJECT_VERSION}"
if [ -z "$VERSION" ]; then
    echo "[release] 无法从 project.godot 读取 config/version，且未传 VERSION；请显式传入 VERSION=x.y" >&2
    exit 1
fi

# 发布通道单源：推送目标、GitHub API、资产上传三处同源，分叉的表现是「包推到 A、Release 建在 B」
# 而两步各自都报成功。故目标是常量，origin 只作一致性断言——由 origin 推导会让「origin 指向 fork」
# 静默生效（覆盖 fork 的 main、Release 落到 fork）；确要改发别处必须显式改这里。
CANONICAL_SLUG="NeverToEver/InfiAir"

# origin URL → owner/repo（HTTPS 与 scp 形态都要认：https://github.com/owner/repo(.git) / git@github.com:owner/repo(.git)）
repo_slug_from_url() {
    printf '%s' "$1" \
        | sed -E 's#^[a-zA-Z][a-zA-Z0-9+.-]*://([^@/]*@)?[^/]+/##; s#^[^@/]+@[^:/]+:##; s#^/##; s#\.git$##'
}

# 工作树干净性判据：git 报错时**不得**静默按「干净」放行（铁律 2：取不到判据即失败），
# 也不许把「有改动」压成一行沉默——发布内容与提交内容不一致属于必须点名的拒绝
require_clean_worktree() {
    local why="$1" dirty
    dirty="$(git status --porcelain)" || {
        echo "[release] git status 失败——工作树干净性判据取不到，拒绝发布" >&2; return 1; }
    [ -z "$dirty" ] && return 0
    echo "[release] ${why}，当前有未提交改动：" >&2
    printf '%s\n' "$dirty" >&2
    return 1
}

# 发布前置：docs/ROADMAP.md「发布前人工验收」清零（AGENTS §7 明文承诺：不清零不发布）。
# 判据（静态解析该小节，不猜内容）：
#   1) 小节内的声明行「**当前待办 N 条…**」必须存在且 N=0——缺声明即判据取不到，判红（铁律 2）
#   2) 小节内顶层条目（`- **名字**：…`）必须为零。该节是发布门，条目做完即删除、不留收口记录
#      （AGENTS §8「活跃文档只写现状 + 开放项」；ROADMAP Maintenance「条目完成即删除」）。
#      记录块与待办条目在文本上不可区分（同在一节、同为顶层条目，往记录块里插一条待办几乎看不出来），
#      故只认「小节内零条目」这一可机械判定的形态：有记录时判红并打印条目，而不是替人猜哪些已收口。
check_manual_acceptance() {
    local roadmap="$1"
    python3 -c '
import re, sys
path = sys.argv[1]
try:
    lines = open(path, encoding="utf-8").read().splitlines()
except OSError as exc:
    print(f"[release] 读不到 {path}（{exc}）——「发布前人工验收」清零判据取不到，拒绝发布", file=sys.stderr)
    sys.exit(1)
heading = "## 发布前人工验收"
start = next((i for i, ln in enumerate(lines) if ln.strip() == heading), None)
if start is None:
    print(f"[release] {path} 里找不到「{heading}」小节——清零判据取不到，拒绝发布", file=sys.stderr)
    sys.exit(1)
end = next((j for j in range(start + 1, len(lines)) if lines[j].startswith("## ")), len(lines))
section = lines[start + 1:end]
declared = None
for ln in section:
    m = re.match(r"^\*\*当前待办\s*(\d+)\s*条", ln)
    if m:
        declared = int(m.group(1))
        break
entries = [ln for ln in section if ln.startswith("- ")]
if declared == 0 and not entries:
    print("[release] 「发布前人工验收」已清零（声明 0 条、小节内条目 0 条）")
    sys.exit(0)
bad = []
count_text = "缺失" if declared is None else f"{declared} 条"
bad.append(f"[release] {path} 的「{heading}」小节未清零：声明 {count_text}、小节内条目 {len(entries)} 条")
bad.append("         该节是发布门（AGENTS §7）：只列待办条目，做完即删除（AGENTS §8 与 ROADMAP Maintenance 都不留完成记录）。")
for ln in entries[:10]:
    text = ln[2:].strip()
    bad.append("           " + (text[:80] + "…" if len(text) > 80 else text))
if len(entries) > 10:
    bad.append(f"         （其余 {len(entries) - 10} 条见 {path} 第 {start + 1} 行的「{heading}」）")
if entries and any("收口" in ln and not ln.startswith(("-", ">")) for ln in section):
    bad.append("         注：本节里的条目看着都在一段「收口」记录里——记录与待办在文本上不可区分（同为一节内的顶层条目），")
    bad.append("             故本判据只认「小节内零条目」这一可机械判定的形态，不按内容猜哪些已收口。")
bad.append("         怎么算过：把小节内条目清零——已完成的删掉（证据在 git log），未完成的先按条目写明的判定过一遍；")
bad.append("                   声明行同步写成「**当前待办 0 条。**」。")
print("\n".join(bad), file=sys.stderr)
sys.exit(1)
' "$roadmap"
}

if [ "$PUBLISH" = 1 ]; then
    # 发布前置检查：干净工作树、版本号及其与项目版本一致、发布通道、tag 未占用、发布凭据、
    # 人工验收清零、质量门禁。全部先于导出——任何一项不过都不该白白跑完十来分钟导出，
    # 更不该把包发出去（AGENTS §7 的发布承诺要在这里变成判据，而不是注释里的建议）
    require_clean_worktree "--publish 要求干净工作树（发布内容必须先提交）" || exit 1
    [[ "$VERSION" =~ ^[0-9]+\.[0-9]+$ ]] || {
        echo "[release] --publish 版本号须为 MAJOR.MINOR：$VERSION" >&2; exit 1; }
    # tag 与包名都由 VERSION 派生；它与 project.godot 的 config/version 分叉后没有后续判据能发现
    # （tag/Release 报新版本、游戏自身仍报旧版本，两边各自都「正常」）
    [ "$VERSION" = "$PROJECT_VERSION" ] || {
        echo "[release] VERSION=${VERSION} 与 project.godot config/version=${PROJECT_VERSION} 不一致——" >&2
        echo "         发出去会让 tag、包名与游戏自身版本分叉，先对齐再发" >&2; exit 1; }
    ORIGIN_URL="$(git remote get-url origin 2>/dev/null || true)"
    if [ -z "$ORIGIN_URL" ]; then
        echo "[release] 未配置 git remote origin——发布通道无法核对，拒绝发布" >&2; exit 1
    fi
    if [ "$(repo_slug_from_url "$ORIGIN_URL")" != "$CANONICAL_SLUG" ]; then
        echo "[release] origin=${ORIGIN_URL} 不是发布通道 ${CANONICAL_SLUG}——推送与 GitHub Release 必须同源，" >&2
        echo "         否则会「包推到一处、Release 建到另一处」而两步都报成功；确要从别处发布请显式改 release.sh 的 CANONICAL_SLUG" >&2
        exit 1
    fi
    # 推送通道固定走 HTTPS + 已存凭据（origin 可能是本机不可用的 SSH 形态）
    PUSH_URL="https://github.com/${CANONICAL_SLUG}.git"
    if git ls-remote --exit-code --tags "$PUSH_URL" "refs/tags/v$VERSION" >/dev/null 2>&1; then
        echo "[release] tag v$VERSION 已存在于 origin，换一个版本号" >&2; exit 1
    fi
    # ${GITHUB_TOKEN:-}：本处按「未设即回退到凭据管理器」判（裸 $GITHUB_TOKEN 在 set -u 下直接中止，
    # 与 --help 承诺的「可选」不符）；取凭据失败（无凭据助手 / 未登录）时 git credential fill 返回非零，
    # pipefail 下会让赋值整体失败并**静默退出**，故显式吞掉退出码，改由下面的判据给出可读拒绝
    if [ -z "${GITHUB_TOKEN:-}" ]; then
        GITHUB_TOKEN=$(printf "protocol=https
host=github.com
" | GIT_TERMINAL_PROMPT=0 git credential fill 2>/dev/null | sed -n 's/^password=//p') || GITHUB_TOKEN=""
    fi
    [ -n "$GITHUB_TOKEN" ] || {
        echo "[release] 未取得 GitHub 凭据：设 GITHUB_TOKEN，或在凭据管理器保存 github.com 凭据" >&2; exit 1; }
    echo "==> 发布前置：docs/ROADMAP.md「发布前人工验收」清零判定"
    if ! check_manual_acceptance docs/ROADMAP.md; then
        echo "[release] 发布中止：先让该节清零再发（AGENTS §7，本条无跳过开关）" >&2
        exit 1
    fi
    if [ "$SKIP_GATES" = 1 ]; then
        echo "[release] ！！已显式跳过质量门禁（--skip-gates）：本次发布不带全量门禁判定" >&2
    else
        [ -f scripts/ci/gates.py ] || {
            echo "[release] 门禁入口 scripts/ci/gates.py 不存在——判据取不到，拒绝发布" >&2; exit 1; }
        echo "==> 发布前置：质量门禁 python3 scripts/ci/gates.py（四步主干，本机实测约 10 秒；跳过需显式 --skip-gates）"
        GATES_START=$(date +%s)
        if ! python3 scripts/ci/gates.py; then
            echo "[release] 质量门禁未全绿——发布中止（失败步骤与其日志见上面的门禁汇总）" >&2
            exit 1
        fi
        echo "==> 质量门禁全绿（用时 $(( $(date +%s) - GATES_START ))s）"
        # 门禁跑完后复检工作区：引擎与构建只写 .godot/、bin/ 这类忽略目录，被跟踪文件不该被动过；
        # 若仍有改动，此后打的包会带非提交态素材（与 tag 内容不一致）——故重查一次工作区
        require_clean_worktree "门禁跑完后工作区不再干净——打包会带非提交态内容，发布中止" || exit 1
    fi
    echo "==> --publish 模式：版本 v${VERSION}，发布通道 ${CANONICAL_SLUG}，前置检查通过"
fi
# 探测链 .NET 版优先（godot-mono）——含 .cs 工程标准版引擎无法导出
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
		# 环境适配：仅安装 godot4 命名的发行版（如多数 Linux 仓库包）也可直接发布
	command -v "$GODOT" >/dev/null 2>&1 || GODOT="godot4"
fi
# GODOT 兜底链断裂必须给出诊断——回退链末端 command not found 裸报错对用户无指引，
# 最终探测失败立即给出引擎安装指引（对齐 run.sh 诊断口径）
if ! command -v "$GODOT" >/dev/null 2>&1; then
    echo "[release] 未找到 Godot 引擎：${GODOT}（需要 4.7+，推荐 .NET 版）" >&2
    echo "         下载：https://godotengine.org/download 或放置到 ~/.local/bin/" >&2
    exit 1
fi

# 打包工具前置检查必须在导出之前——否则缺 tar/zip 时会白白跑完两次导出才报错，stage 残留
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

# Godot .NET 导出强依赖解决方案文件——缺失时 dotnet publish 被静默跳过，导出仍以
# exit 0「completed with warnings」收尾，产出不带任何 C# 程序集的空壳包（Windows 无控制台，
# 引擎 logo 后 .NET 初始化失败直接退出无可见报错）。必须事前硬检查，杜绝带病出包
if [ ! -f InfiAir.sln ]; then
	echo "[release] 缺少 InfiAir.sln（.NET 导出必需）" >&2
	echo "         生成：dotnet new sln -n InfiAir && dotnet sln add InfiAir.csproj csharp/core/InfiAir.Core.csproj" >&2
	exit 1
fi

BUILD_DIR="builds"
STAGE_DIR="$BUILD_DIR/stage"
OUT_DIR="$BUILD_DIR/release"

echo "==> 资源导入"
"$GODOT" --headless --import --path .

# 导出包装函数——Godot 导出即使 C# 构建失败也以 exit 0 收尾（日志仅见 ERROR），
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
	# Godot headless 导出在引擎退出阶段可能打印（4.6 起实测）「RID allocations leaked at exit」
	# （dummy 渲染器 teardown 噪音，发生在产物落盘之后，与包内容无关）；精确豁免该行，
	# 其余 ^ERROR 仍中止（防空壳包的门禁语义不变）
	if grep -vE "^ERROR: [0-9]+ RID allocations? of type .* leaked at exit" "$log" | grep -q "^ERROR"; then
		grep -vE "^ERROR: [0-9]+ RID allocations? of type .* leaked at exit" "$log" | grep "^ERROR" | sort -u | head -5 >&2
		rm -f "$log"
		echo "[release] 导出日志含 ERROR（${preset}），中止——产物不可信" >&2
		exit 1
	fi
	rm -f "$log"
	# C# 工程导出必须携带托管运行时目录（coreclr + InfiAir.dll 等）；缺失即空壳包
	if [ ! -d "$(dirname "$out")/$data_dir" ]; then
		echo "[release] 导出产物缺少 $data_dir/（${preset}）——C# 程序集未随包导出" >&2
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

# 托管运行时目录（coreclr + InfiAir.dll 等）必须随包发布——仅拷可执行文件的包在目标机启动 logo 后即闪退（.NET 初始化失败）
cp "$BUILD_DIR/linux/InfiAir.x86_64" "$STAGE_DIR/linux/"
cp -r "$BUILD_DIR/linux/data_InfiAir_linuxbsd_x86_64" "$STAGE_DIR/linux/"
cp packaging/linux/install.sh packaging/linux/uninstall.sh packaging/linux/infiair.desktop "$STAGE_DIR/linux/"
chmod +x "$STAGE_DIR/linux/install.sh" "$STAGE_DIR/linux/uninstall.sh"

cp "$BUILD_DIR/windows/InfiAir.exe" "$STAGE_DIR/windows/"
cp -r "$BUILD_DIR/windows/data_InfiAir_windows_x86_64" "$STAGE_DIR/windows/"
cp packaging/windows/install.bat packaging/windows/uninstall.bat "$STAGE_DIR/windows/"

# 随包授权文本：MIT 全文 + 第三方声明 + 字体许可全文。字体已随 embed_pck 嵌进可执行体，
# 而 OFL 第 2 条要求每份副本都含版权声明与许可全文——包内缺件即分发面授权不完整，
# 且这种缺失不报错、下载者根本看不出，故在打包处逐件断言（缺件非零退出）。
# 先查源再拷：源文件被删/改名时直接由这里点名（否则先炸在 cp 上，报错只说「没有这个文件」）。
for f in LICENSE NOTICE assets/fonts/NotoSansSC-OFL.txt; do
	if [ ! -s "$f" ]; then
		echo "[release] 授权文本源文件缺失或为空：${f}——OFL 要求随副本附版权声明与许可全文，中止" >&2
		exit 1
	fi
done
for d in linux windows; do
	cp LICENSE NOTICE assets/fonts/NotoSansSC-OFL.txt "$STAGE_DIR/$d/"
	for f in LICENSE NOTICE NotoSansSC-OFL.txt; do
		if [ ! -s "$STAGE_DIR/$d/$f" ]; then
			echo "[release] 包内缺少授权文本 ${f}（${d}）——OFL 要求随副本附版权声明与许可全文，中止" >&2
			exit 1
		fi
	done
done
tar -C "$STAGE_DIR/linux" -czf "$OUT_DIR/InfiAir-$VERSION-linux-x86_64.tar.gz" .
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
	# 三处端点与上面的推送目标同源于 CANONICAL_SLUG（各自硬编码会分叉）
	API="https://api.github.com/repos/${CANONICAL_SLUG}"
	# 资产上传必须走 uploads.github.com 专用主机（api.github.com 上该路径 404）
	UPLOAD="https://uploads.github.com/repos/${CANONICAL_SLUG}"

	echo "==> 推送 main 与 tag v$VERSION"
	git push "$PUSH_URL" HEAD:main
	git tag "v$VERSION"
	git push "$PUSH_URL" "v$VERSION"

	NOTES_FILE="$(mktemp)"
	# 发布说明单一维护点：docs/RELEASE_NOTES.md 按 `## v<版本>` 分节，取本版本那一节作 Release 正文。
	# 文件可累积保留历史节，正文不会把旧版本一起带上；缺文件/缺该版本节/节为空则回退空 body
	# （不阻断发布，也不静默编造内容——判断依据只有文件本身）
	if [ -s docs/RELEASE_NOTES.md ]; then
		awk -v ver="v$VERSION" '
			/^## / { inblock = ($2 == ver) }
			inblock && $0 !~ /^---[[:space:]]*$/ { print }
		' docs/RELEASE_NOTES.md > "$NOTES_FILE"
	fi
	if [ -s "$NOTES_FILE" ]; then
		echo "==> Release 正文取自 docs/RELEASE_NOTES.md 的 $VERSION 节（$(wc -c < "$NOTES_FILE") 字节）"
	else
		echo "[release] docs/RELEASE_NOTES.md 无 v$VERSION 节（或文件缺失），Release 正文为空" >&2
	fi
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
	echo "==> 发布完成：https://github.com/${CANONICAL_SLUG}/releases/tag/v$VERSION"
fi
