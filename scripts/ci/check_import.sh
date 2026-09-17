#!/usr/bin/env bash
# 资源导入门禁：导入退出码 + 日志引擎错误分判（真实导入错误 / 已知退出期噪声）。
# Usage: check_import.sh [log_path]   (default /tmp/import.log)
#
# 抓的静默错误：资源导入静默坏（缺依赖 / 路径错 / 资源损坏）不改变退出码，只在日志留 ERROR；
# 旧的 `Warning treated as error` 判据在零 GDScript 后永不出现，是取不到判据的死判据。
#
# 判定四档（单源在本脚本的 NOISE_RE 与其下的分判逻辑）：
#   1) 导入退出码非 0 → 红；
#   2) 日志零 ERROR → 绿；
#   3) 有 ERROR，且**每一条**都命中已登记的退出期噪声模板 → 绿，打印放行条数、命中行与理由；
#   4) 其余（含「噪声 + 真实错误」混排）→ 红，只打印非噪声的 ERROR 行。
#
# 已登记的退出期噪声（引擎侧时序，与本项目状态无关）：编辑器设置未实例化时读 android 导出组设置所打的
# 那条 ERROR——逐字模板即下方 NOISE_RE 单源，此处不再抄一份（注释与判据各写一遍必然分叉）。
#   出处是 editor/settings/editor_settings.cpp 的 _EDITOR_GET：编辑器退出时 EditorSettings 已析构，
#   而 android 导出平台的收尾代码仍在读编辑器设置——后台轮询线程 `_check_for_changes_poll_thread`
#   的循环体（每次迭代读 android_sdk_path 求 adb 路径）与线程末尾的 shutdown_adb_on_exit 收尾块
#   都在这个窗口里，命中哪一条取决于线程正好停在循环体还是已跳出。
#   实测（Godot 4.7.2.stable.mono，darwin，与 CI 同版本）：工程里存在**可运行的 Android 预设**时，
#   每次 `--headless --import` 都在全部进度步骤 DONE **之后**打这条（连续 3 次 3/3 命中
#   shutdown_adb_on_exit）；本仓库的预设集（Linux/Windows，无可运行 Android 预设）不启动该线程，
#   实测 12 次 0 命中。即：它出现与否只取决于工程是否带可运行 Android 预设，与导入是否干净无关。
#
# 为什么是「条件式放行」而不是「白名单整类」或「探针式重跑」：
#   - 只放行这一条模板（引擎 _EDITOR_GET 原样文案）且设置名必须落在 android 导出组内——模板本身
#     只有引擎那一处能打出，项目侧资源损坏 / 导入器失败的消息来自别的调用点，一条都进不来；
#   - 再加「同次导入再无其它 ERROR」：真实导入错误与噪声混排时，非噪声行即判红（不会因为有一条
#     噪声就把整份日志放过）；
#   - 不用「出现噪声就重跑一次导入、第二次干净才放行」：实测该噪声在**每次**运行都出现（见上，
#     3/3），「第二次干净」这条事实不成立，照抄会把带 Android 预设的工程重新判成假红；更要命的是
#     **真实导入错误本身也不保证第二次出现**——实测把一个坏 PNG 导入失败后，Godot 把失败记进
#     `.import` 的 `valid=false` 与 `.godot/imported/*.md5`，源文件不变的下一次导入不再报这条错误
#     （改内容也照样跳过）。探针式判据只读第二次的日志，会把这种「首次报过就不再报」的真实错误
#     当成干净放行——按第一次日志判才是真实错误的可见面；
#   - 也不用「本次导入确实增删了 .cs」：实测噪声与脚本集合变化无因果（不改任何 .cs、连跑 3 次全中），
#     且门禁侧没有可靠单源能判「引擎本次确实看到了脚本集合变化」（git status 在正常开发中本就脏、
#     提交后恰好干净），把它写进判据只会制造新的假红/假绿。
#   仍然判红的（本判据能区分 / 不能区分的边界）：任何非 android 组的 EditorSettings 未实例化消息
#   （另一处读取时序，未验证，不许搭车放行）、模板不逐字相同（如设置名含大写或去掉了句点）、
#   以及所有真实的导入 / 资源错误。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

# 引擎默认值按「.NET 版优先」探测（与 run.sh / gates.py 同一口径）：裸 `godot` 在 macOS 上常是标准版，
# 打不开含 C# 的工程——跑到错引擎上不只判据失真（.cs 全部加载失败），引擎还会把 InfiAir.csproj 的
# Godot.NET.Sdk 改写成自己那版（副产物污染工作区）。显式给 GODOT 时（gates.py / CI 的软链）不受影响。
if [ -z "${GODOT:-}" ]; then
  for candidate in godot-mono godot godot4; do
    if command -v "$candidate" >/dev/null 2>&1; then
      GODOT="$candidate"
      break
    fi
  done
  GODOT="${GODOT:-godot}"        # 三个名字都没有时回落裸命令，让导入以「引擎找不到」失败，不静默假绿
fi
LOG="${1:-/tmp/import.log}"

# 已登记的退出期噪声模板（单源）。逐字匹配引擎文案：android 导出组的设置名 + 结尾句点。
NOISE_RE='ERROR: EditorSettings not instantiated yet when getting setting "export/android/[a-z0-9_/]+"\.'

if ! "$GODOT" --headless --import --path . > "$LOG" 2>&1; then
  echo "::error::import failed"
  tail -30 "$LOG"
  exit 1
fi

# 命中行先各自落临时文件再判：不用 `grep -q` 串管道——`set -o pipefail` 下 `grep -q` 找到即退出会让
# 上游 grep 收 SIGPIPE（141），整条管道非 0，日志里有成百上千行 ERROR 也照样被读成「无命中」判绿
# （同类门禁踩过的假绿形态）。这里的 grep 都不带 -q，且退出码只用于取行。
HITS="$(mktemp)" NOISE="$(mktemp)" REAL_ERRORS="$(mktemp)"
trap 'rm -f "${HITS}" "${NOISE}" "${REAL_ERRORS}"' EXIT

grep -n "ERROR" "$LOG" > "$HITS" || true        # 无命中时 grep 退出码为 1，脚本未开 -e，不中断
TOTAL="$(wc -l < "$HITS" | tr -d ' ')"
grep -nE "$NOISE_RE" "$HITS" > "$NOISE" || true
NOISE_N="$(wc -l < "$NOISE" | tr -d ' ')"
grep -nvE "$NOISE_RE" "$HITS" > "$REAL_ERRORS" || true

if [ "${TOTAL}" -eq 0 ]; then
  tail -3 "$LOG"
  exit 0
fi

if [ "${NOISE_N}" -eq "${TOTAL}" ]; then
  echo "[import] 放行 ${NOISE_N} 条已知退出期噪声（EditorSettings 已析构后 android 导出平台收尾读设置，与导入结果无关）："
  grep -m3 -E "$NOISE_RE" "$HITS"
  tail -1 "$LOG"
  exit 0
fi

echo "::error::import log contains ERROR（真实导入错误，或噪声与真实错误混排）："
sed -n '1,30p' "$REAL_ERRORS"
echo "::error::ERROR 共 ${TOTAL} 条，其中非噪声 $((TOTAL - NOISE_N)) 条（完整日志：${LOG}）"
exit 1
