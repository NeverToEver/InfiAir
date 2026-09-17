#!/usr/bin/env bash
# 素材生成可复现性门禁：重跑离线生成器，判定「生成器确定」与「产物与生成器同步」（AGENTS §1 单源政策）。
# 用法：bash scripts/ci/check_assets_reproducible.sh（输出走 stdout，由 gates.py / CI 捕获）
#
# 抓的静默错误：`scripts/tools/regenerate_all.sh` 声称「重跑后 git diff 应为空」，但这条不变式此前
# 没有任何自动判定——只写在脚本尾部提示里，靠人工执行核对。生成器引入时间戳 / 未播种随机 / 顺序不稳定，
# 或改了生成器却忘了提交产物时，产物会静默漂移：游戏照跑、单测照绿、冒烟照过，直到某次真要重跑素材才
# 暴露（那时已分不清是本次漂移还是历史累积漂移）。
#
# 判定分两段，都不依赖「本机恰好是产出这些产物的那台机器」——跨机逐字节一致对这条管线**原理上做不到**：
# 生成器全程 4× 超采样 + LANCZOS 降采样 + 高斯滤波，这些卷积核的定点实现随 Pillow 里的
# FreeType/图像库构建与浮点路径变化，换机器必然出现抗锯齿级像素差（实测：同一份源码 + Pillow 12.2，
# macOS 漂 6 张、CI 的 ubuntu 几乎全部产物都漂；而同机两次重跑逐字节一致）。故：
#
#   段一 同步（重跑一次）：逐文件与入库资产比对——尺寸/模式必须相同；PNG **非像素块**（文本、时间、
#        物理分辨率等元数据）必须相同（时间戳类漂移在此判红）；像素差异占比必须 ≤ 抗锯齿包络。
#        全字节一致 → 通过（本机即产出环境，确定性与同步一次性成立）。
#   段二 本机确定（仅当段一有残差时执行）：再重跑一次，比对两次重跑的输出是否逐字节一致——
#        不一致即「生成器非确定」，任何机器上都能判红；一致则确认残差来自跨机栅格化，放行并打印
#        残差摘要与「产物同步未在本机判定」的显式声明（不静默放过）。
#
# 抗锯齿包络（ENVELOPE_PCT）：单文件差异像素占比上限，实测口径为「跨机栅格化噪声远低于真实改动」——
# 栅格化差异只落在图元边缘且面积很小（实测 macOS 最坏 3.6%：logo 字形边缘的亚像素落点，最大通道差
# 可达 255 但面积占比低），而真实改动（新增/移动/改色元素）会形成连续区域，占比高出一个量级。
# 取值 10 是实测最坏值的约 3 倍余量：超出即判「生成器与产物不同步」，而不是继续当成噪声放行。
#
# 失败即**工作区已被本门禁改写**：脚本刻意不自动还原（自动还原会掩盖漂移现场，也可能覆盖跑者自己
# 的未提交素材改动），故必须打印醒目提示与 diff 摘要，让读者知道要去看 git status。
# 通过时则相反：判为跨机噪声的那几张图由本门禁自己还原回入库状态——基线已证明跑前干净，留着不还原
# 会让工作区带上「像未提交改动」的图，污染下一次跑的基线检查。
#
# 时间预算：段一实测约 33 秒（产出环境到此为止）；段二只在跨机残差时执行，本步最多约 66 秒，是全量
# 门禁里唯一超 30 秒的单条——AGENTS §6 时间预算允许「单条超 30 秒在上表注明理由」，理由是：它是唯一
# 能判「生成器确定 + 产物与生成器同步」的手段，无法用静态扫描替代（需要真跑 Pillow 绘制管线）；
# 它不依赖引擎、不改写 csharp/，失败面与其它门禁完全独立。
#
# 依赖口径：Pillow 属**构建期素材生成器**依赖（AGENTS §1：构建期生成器允许 Python 第三方库，
# 产物入库、不进发布包），不是运行时依赖；缺失即显式判红（不得静默跳过——跳过等于假绿）。
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1

ARTIFACT_PATHS=(assets data)
ENVELOPE_PCT=10

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

snapshot() {
    # 产物树的内容指纹（排序后逐文件 sha256）：段二用它判「两次重跑是否一致」
    find "${ARTIFACT_PATHS[@]}" -type f -print0 2>/dev/null \
        | sort -z \
        | xargs -0 shasum -a 256 2>/dev/null \
        | shasum -a 256
}

echo "==> 重跑素材生成器（scripts/tools/regenerate_all.sh，实测约 33s）"
if ! bash scripts/tools/regenerate_all.sh; then
    echo "::error::regenerate_all.sh 自身失败（非漂移判定）——生成器写坏或环境缺依赖，门禁不代它下结论，先修生成器"
    exit 1
fi

drift="$(git status --porcelain -- "${ARTIFACT_PATHS[@]}")"
if [ -z "$drift" ]; then
    echo "assets-reproducible gate: clean（重跑生成器后 ${ARTIFACT_PATHS[*]} 与入库资产逐字节一致，" \
         "无改动也无新增文件——本机即产出环境）"
    exit 0
fi

echo "==> 产物与入库资产不完全一致，按「结构 + 抗锯齿包络」逐文件判定（漂移清单见下）"
printf '%s\n' "$drift"

first_snapshot="$(snapshot)"
drift_files="$(printf '%s\n' "$drift" | awk '{print $NF}')"
verdict="$(
    DRIFT_FILES="$drift_files" ENVELOPE_PCT="$ENVELOPE_PCT" python3 - <<'PY'
import io
import os
import struct
import subprocess
import sys

from PIL import Image, ImageChops

envelope = float(os.environ["ENVELOPE_PCT"])
paths = [p for p in os.environ["DRIFT_FILES"].splitlines() if p]

# PNG 非像素块：文本/时间/色彩/物理分辨率等元数据块的内容要逐字节相同（时间戳类漂移在此判红）；
# IDAT（像素数据）不进签名——它的字节差异正是本判据要度量的东西。
_META_CHUNKS = (b"tEXt", b"zTXt", b"iTXt", b"tIME", b"pHYs", b"sRGB", b"gAMA", b"iCCP", b"sBIT")


def chunk_signature(data: bytes) -> list:
    out = []
    pos = 8
    while pos + 8 <= len(data):
        length, ctype = struct.unpack(">I4s", data[pos : pos + 8])
        body = data[pos + 8 : pos + 8 + length]
        if ctype in _META_CHUNKS:
            out.append((ctype, body))
        elif ctype != b"IDAT":
            out.append((ctype, b""))
        pos += 12 + length
    return out


structural = []
over = []
within = []
identical = 0

for path in paths:
    shown = subprocess.run(["git", "show", f"HEAD:{path}"], capture_output=True)
    if shown.returncode != 0:
        structural.append((path, "本次重跑新增的产物（入库资产里没有这个文件）"))
        continue

    committed = shown.stdout
    with open(path, "rb") as fh:
        current = fh.read()
    if committed == current:
        identical += 1
        continue

    left = Image.open(io.BytesIO(committed))
    right = Image.open(io.BytesIO(current))
    if left.size != right.size or left.mode != right.mode:
        structural.append((path, f"尺寸/模式变了：{left.size}/{left.mode} → {right.size}/{right.mode}"))
        continue

    if chunk_signature(committed) != chunk_signature(current):
        structural.append((path, "PNG 非像素块不同（元数据/编码参数漂移，如时间戳）"))
        continue

    diff = ImageChops.difference(left.convert("RGBA"), right.convert("RGBA"))
    bands = diff.split()
    mask = None
    for band in bands:
        one = band.point(lambda v: 255 if v else 0)
        mask = one if mask is None else ImageChops.lighter(mask, one)

    total = left.size[0] * left.size[1]
    differing = total - mask.histogram()[0]
    ratio = 100.0 * differing / total
    max_delta = max(band.getextrema()[1] for band in bands)
    if ratio > envelope:
        over.append((path, f"差异像素 {ratio:.2f}%（上限 {envelope:g}%）、最大通道差 {max_delta}"))
    else:
        within.append((path, f"差异像素 {ratio:.2f}%、最大通道差 {max_delta}"))

print(f"identical={identical}")
for label, rows in (("STRUCTURAL", structural), ("OVER", over), ("WITHIN", within)):
    for path, detail in rows:
        print(f"{label}\t{path}\t{detail}")
sys.exit(1 if (structural or over) else 0)
PY
)"
rc=$?
printf '%s\n' "$verdict"

if [ "$rc" -ne 0 ]; then
    echo "::error::素材产物与生成器不同步：上面的结构与超包络条目是「生成器改了但产物没提交」或「新增/删除产物没同步」的证据。"
    echo "::error::diff 摘要（git diff --stat）："
    git diff --stat -- "${ARTIFACT_PATHS[@]}"
    echo "::error::处置：核对是否为有意改动——有意改素材就把新产物一起提交（并在提交正文说明生成器改了什么）；"
    echo "::error::非有意则生成器引入了非确定性（时间戳 / 未播种随机 / 遍历顺序），修成确定性后重跑。"
    exit 1
fi

echo "==> 段二：残差是否为跨机栅格化——再重跑一次，比对两次重跑是否逐字节一致"
if ! bash scripts/tools/regenerate_all.sh; then
    echo "::error::regenerate_all.sh 第二次运行失败（非漂移判定）——生成器不稳定，先修生成器"
    exit 1
fi

second_snapshot="$(snapshot)"
if [ "$first_snapshot" != "$second_snapshot" ]; then
    echo "::error::生成器**非确定**：同机连续两次重跑的输出不一致——产物会随运行时刻/随机序漂移。"
    echo "::error::常见来源：写进 PNG 的时间戳或版本串、未播种的随机、遍历顺序依赖、并发写同一文件。"
    echo "::error::本判据与平台无关（同机两次重跑），修成确定性后重跑本门禁。"
    exit 1
fi

# 判定为跨机噪声：把本门禁自己改写过的产物还原回入库状态——基线已证明跑前是干净的，故只可能
# 是本次重跑的产物；不还原会留下 6 张「看起来像未提交改动」的图，污染下一次跑的基线检查。
printf '%s\n' "$drift_files" | while IFS= read -r path; do
    [ -n "$path" ] && git checkout -- "$path"
done

echo "assets-reproducible gate: clean（生成器本机确定：两次重跑逐字节一致；"
echo "产物与入库资产的差异全部落在抗锯齿包络内——本机不是产出这些产物的那台机器，"
echo "逐字节同步未在本机判定，残差摘要见上；已把本门禁重跑出的产物还原回入库状态。"
echo "跨机差异口径见 AGENTS §6 与本步脚本头注）"
