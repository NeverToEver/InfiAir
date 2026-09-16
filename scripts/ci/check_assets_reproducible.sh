#!/usr/bin/env bash
# 素材生成可复现性门禁：重跑离线生成器，断言产物与入库资产逐字节一致（AGENTS §1 单源政策）。
# 用法：bash scripts/ci/check_assets_reproducible.sh
#
# 抓的静默错误：`scripts/tools/regenerate_all.sh` 声称「重跑后 git diff 应为空」，但这条不变式此前
# 没有任何自动判定——只写在脚本尾部提示里，靠人工执行核对。生成器引入时间戳 / 字体度量变化 / 浮点
# 抖动 / 依赖版本行为变化时，产物会静默漂移：游戏照跑、单测照绿、冒烟照过，直到某次真要重跑素材才
# 暴露（那时已分不清是本次改动漂移还是历史累积漂移）。
#
# 判定：先在干净基线上跑一遍 generate_all（生成器 + 音频微调），再断言**产物路径**没有被改写。
#   - 产物路径 = `assets/`（生成器全部输出在此：sprites/audio/fonts 之外只读）与 `data/`
#     （当前生成链不写 data/，一并纳入断言：新生成器若往数值/翻译表写会立刻暴露）；
#   - 断言用 `git status --porcelain`（含未跟踪文件）而不是只 `git diff`：生成器新增一个产物文件时
#     `git diff` 是空的，未跟踪文件才是真相——「多出文件」与「文件变了」同属漂移；
#   - 基线必须先干净：产物路径带着未提交改动时，重跑后的差异分不清是本次漂移还是跑前就有，
#     判据取不到 → 判红（先提交或还原再跑）。
#
# 失败即**工作区已被本门禁改写**：脚本刻意不自动还原（自动还原会掩盖漂移现场，也可能覆盖跑者自己
# 的未提交素材改动），故必须打印醒目提示与 diff 摘要，让读者知道要去看 git status。
#
# 时间预算：本步实测约 33 秒（Pillow 12.2、Linux CI 软光栅无关；生成器是纯 CPU 绘制的 Python），
# 是全量门禁里唯一超 30 秒的单条——AGENTS §6 时间预算允许「单条超 30 秒在上表注明理由」，理由是：
# 它是唯一能判「素材产物逐字节可复现」的手段，无法用静态扫描替代（需要真跑 Pillow 绘制管线）；
# 它不依赖引擎、不改写 csharp/，失败面与其它门禁完全独立。
#
# 依赖口径：Pillow 属**构建期素材生成器**依赖（AGENTS §1：构建期生成器允许 Python 第三方库，
# 产物入库、不进发布包），不是运行时依赖；缺失即显式判红（不得静默跳过——跳过等于假绿）。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

ARTIFACT_PATHS=(assets data)

if ! command -v git >/dev/null 2>&1 || ! git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    echo "::error::git 不可用或当前目录不是 git 工作区——复现性判据（产物 diff）取不到，拒绝判 clean"
    exit 1
fi

if ! python3 -c "import PIL" >/dev/null 2>&1; then
    echo "::error::python3 无法 import PIL（Pillow 缺失）——素材生成可复现性判据取不到，拒绝判 clean。"
    echo "::error::装法：pip install pillow==12.2（版本与本机一致，避免上游改渲染导致误报）；" \
         "CI 见 .github/workflows/ci.yml 的 Pillow 安装步骤。"
    exit 1
fi

baseline="$(git status --porcelain -- "${ARTIFACT_PATHS[@]}")"
if [ -n "$baseline" ]; then
    echo "::error::产物路径（${ARTIFACT_PATHS[*]}）已有未提交改动——重跑后的差异分不清是本次漂移还是跑前就有，"
    echo "::error::判据取不到，拒绝判 clean。先提交或还原下列改动再跑："
    printf '%s\n' "$baseline"
    exit 1
fi

echo "==> 重跑素材生成器（scripts/tools/regenerate_all.sh，实测约 33s）"
if ! bash scripts/tools/regenerate_all.sh; then
    echo "::error::regenerate_all.sh 自身失败（非漂移判定）——生成器写坏或环境缺依赖，门禁不代它下结论，先修生成器"
    exit 1
fi

drift="$(git status --porcelain -- "${ARTIFACT_PATHS[@]}")"
if [ -n "$drift" ]; then
    echo "::error::素材产物已漂移：重跑生成器后工作区被改写（门禁自己改了工作区，见 git status）"
    printf '%s\n' "$drift"
    echo "::error::diff 摘要（git diff --stat）："
    git diff --stat -- "${ARTIFACT_PATHS[@]}"
    echo "::error::处置：核对是否为有意改动——有意改素材就把新产物一起提交（并在提交正文说明生成器改了什么）；"
    echo "::error::非有意则生成器引入了非确定性（时间戳 / 字体度量 / 浮点 / 依赖版本），修成确定性重跑。"
    exit 1
fi

echo "assets-reproducible gate: clean（重跑生成器后 ${ARTIFACT_PATHS[*]} 与入库资产逐字节一致，无改动也无新增文件）"
