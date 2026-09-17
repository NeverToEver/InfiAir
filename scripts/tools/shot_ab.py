#!/usr/bin/env python3
"""引擎升级的**观感等价**判定工具：同机同显卡跑两版引擎的固定帧截图并量化比对。

用法：
    python3 scripts/tools/shot_ab.py --godot-a <引擎A> --godot-b <引擎B>
    # 例：--godot-a ~/Applications/Godot_mono-4.6.2.app/Contents/MacOS/Godot \
    #     --godot-b ~/Applications/Godot_mono.app/Contents/MacOS/Godot

为什么需要它：引擎升级的既有判据只有「人类目视没看出退化」——不可复现、不可留痕，
而截图门禁（check_visual.sh）刻意不做像素比对（跨渲染器与占位内容会误报）。升级场景
与前者的区别是「同机、同显卡、同渲染后端，只差引擎版本」，因此可以比，但必须先有
**噪声底线**：画面里含墙钟驱动的元素（帧率读出、星空脉动），同版本两遍也不会逐位相同。
本工具据此分三步判定，任一步取不到判据就显式失败（不静默放过）：

  1) 噪声底线：每个引擎各跑 N 遍（默认 2）。同引擎两遍的差异是「判定可达精度」。
  2) 跨引擎差异：与噪声底线同量级 → 视觉等价（无需再看别的）。
  3) 超出噪声时逐条归因，区分「等价级」与「结构级」：
     - 几何位移：±2px 平移若能显著减小差异 = 布局/对齐变了（结构级）；
     - 整体色偏：**平坦区**（梯度 <8）逐通道有符号均值 > 0.5（≈ 半个 8 位色阶）＝全局明暗/色调变了；
     - 差异落点：**平坦区**里「差 >8」的命中率 > 2% ＝差异不是边缘性的（阴影/贴图/大面积色块变了）。
     两条都取平坦区：边缘像素的抗锯齿差异会把整幅的有符号均值抬起来（实测琥珀色 UI 边缘
     会把 R/G 均值抬到 0.8 量级），那是判据的污染而不是画面变化。

退出码：0 = 视觉等价（含「差异在噪声内」与「仅边缘光栅化差异」两种）；1 = 检出结构差异
或判据不可用（探针跑挂、截图数不符、同引擎两遍差异大于跨引擎差异）。

依赖：Pillow（与素材生成器同一条离线工具链口径）。需要真实渲染器，故不进 CI 门禁。
"""

import argparse
import os
import pathlib
import shutil
import subprocess
import sys

try:
    from PIL import Image, ImageChops, ImageFilter, ImageStat
except ImportError:
    print("::error::需要 Pillow：pip install pillow==12.2（与素材可复现性门禁同一依赖）")
    sys.exit(1)

REPO = pathlib.Path(__file__).resolve().parents[2]
VISUAL = REPO / "scripts" / "ci" / "check_visual.sh"
PAGES = (
    "hud.png",
    "settings-controls.png",
    "settings-gameplay.png",
    "settings-display.png",
    "settings-audio.png",
    "settings-about.png",
)

# 判定阈值（口径写在这里，避免散落）
NOISE_FACTOR = 1.5     # 跨引擎 MAD 超过「同引擎最大 MAD」的多少倍即视为超出噪声
NOISE_ABS = 0.05       # 绝对余量：噪声极小时（逐位相同）避免比例判定过苛
SHIFT_GAIN = 0.6       # 最佳平移的 MAD 低于原样该比例 → 判为几何位移
SIGNED_MEAN_MAX = 0.5  # 平坦区逐通道有符号均值上限（≈ 半个 8 位色阶，0..255）
EDGE_FLAT_MAX = 2.0    # 平坦区(梯度<8)里「差 >8」的命中率上限（%）
NOISE_MAX_MAD = 0.5    # 噪声底线上限：超过它说明同引擎两遍就不可重复，判据不可用


def capture(godot: str, out_dir: pathlib.Path, log: pathlib.Path) -> bool:
    """用指定引擎跑一遍截图探针并把 PNG 留到 out_dir（复用门禁脚本，不改其判定）。"""
    out_dir.mkdir(parents=True, exist_ok=True)
    # 继承调用方环境、只覆盖两个变量：引擎要按 PATH 找 .NET SDK（hostfxr/hostpolicy/coreclr），
    # 环境一裁就变成「.NET 加载失败」的启动崩溃，看着像引擎坏了，其实是工具把环境弄没了。
    env = {**os.environ, "GODOT": godot, "KEEP_SHOTS": str(out_dir)}
    proc = subprocess.run(
        ["bash", str(VISUAL), str(log)], cwd=str(REPO), env=env, capture_output=True, text=True
    )
    if proc.returncode != 0:
        print(f"::error::截图探针失败（{godot}）：见 {log}")
        print((proc.stdout or "")[-800:])
        return False
    return True


def max_channel_diff(a: Image.Image, b: Image.Image) -> Image.Image:
    """逐像素取三通道最大差（灰度图）——单通道比较会漏掉「等亮度换色」这类差异。"""
    d = ImageChops.difference(a, b)
    mx = d.getchannel("R")
    for ch in ("G", "B"):
        mx = ImageChops.lighter(mx, d.getchannel(ch))
    return mx


def stats(mx: Image.Image, width: int, height: int) -> tuple[float, float, float]:
    """返回 (MAD, >8 占比%, >32 占比%)。"""
    hist = mx.histogram()
    total = width * height
    mad = sum(i * n for i, n in enumerate(hist)) / total
    return mad, sum(hist[9:]) * 100.0 / total, sum(hist[33:]) * 100.0 / total


def compare(reference_dir: pathlib.Path, other_dir: pathlib.Path) -> dict:
    """逐页比较两个截图目录，返回 {页面: 指标}；尺寸不符即判据不可用。"""
    result = {}
    for page in PAGES:
        pa, pb = reference_dir / page, other_dir / page
        if not pa.exists() or not pb.exists():
            raise SystemExit(f"::error::缺少截图 {page}（{reference_dir} / {other_dir}）")
        a, b = Image.open(pa).convert("RGB"), Image.open(pb).convert("RGB")
        if a.size != b.size:
            raise SystemExit(f"::error::{page} 两版尺寸不同（{a.size} vs {b.size}）——判据不可用")
        mx = max_channel_diff(a, b)
        mad, over8, over32 = stats(mx, *a.size)
        result[page] = {"mad": mad, "over8": over8, "over32": over32, "a": a, "b": b, "mx": mx}
    return result


def attribute(a: Image.Image, b: Image.Image, mx: Image.Image) -> dict:
    """超出噪声时的归因三连：几何位移 / 整体色偏 / 差异落点。"""
    w, h = a.size
    base_mad = stats(mx, w, h)[0]

    # 几何位移：试 ±2 像素平移，能显著减小差异即说明布局对不上
    best = (base_mad, 0, 0)
    for dy in range(-2, 3):
        for dx in range(-2, 3):
            if dx == 0 and dy == 0:
                continue
            shifted = max_channel_diff(a, ImageChops.offset(b, dx, dy))
            mad = stats(shifted, w, h)[0]
            if mad < best[0]:
                best = (mad, dx, dy)

    # 整幅的有符号均值（a - b）：只作参考打印——边缘抗锯齿会把 R/G 抬到 0.8 量级，
    # 判据不能落在这里（那是边缘差异的累加，不是画面变亮）。
    signed = [x - y for x, y in zip(ImageStat.Stat(a).mean, ImageStat.Stat(b).mean)]

    # 平坦区（梯度 <8）单独统计：有符号均值与「差 >8」的命中率才是「画面本身变了没变」的判据
    edge = a.convert("L").filter(ImageFilter.FIND_EDGES)
    ep, mp, ap, bp = edge.load(), mx.load(), a.load(), b.load()
    flat = 0
    flat_over8 = 0
    flat_signed = [0, 0, 0]
    for y in range(0, h, 2):
        for x in range(0, w, 2):
            if ep[x, y] >= 8:
                continue
            flat += 1
            if mp[x, y] > 8:
                flat_over8 += 1
            for c in range(3):
                flat_signed[c] += ap[x, y][c] - bp[x, y][c]

    return {
        "base_mad": base_mad,
        "best": best,
        "signed": signed,
        "flat_pct": flat * 100.0 / max(1, (w // 2) * (h // 2)),
        "flat": flat_over8 * 100.0 / max(1, flat),
        "flat_signed": [v / max(1, flat) for v in flat_signed],
    }


def main() -> int:
    ap = argparse.ArgumentParser(description="引擎升级的观感等价判定（同机 A/B 截图）")
    ap.add_argument("--godot-a", required=True, help="基准引擎（升级前）")
    ap.add_argument("--godot-b", required=True, help="待验引擎（升级后）")
    ap.add_argument("--runs", type=int, default=2, help="每个引擎的遍数（≥2 才能测噪声底线）")
    ap.add_argument("--work", default="/tmp/infiair-shot-ab", help="截图与日志的工作目录")
    ap.add_argument("--keep", action="store_true", help="保留工作目录（默认跑完删掉）")
    ap.add_argument("--reuse", action="store_true",
                    help="复用工作目录里已有的截图（不重新捕获；调阈值或做破坏验证时用）")
    args = ap.parse_args()

    if args.runs < 2:
        print("::error::--runs 至少为 2：没有同引擎两遍的差异就取不到噪声底线，判定无从成立")
        return 1
    for path in (args.godot_a, args.godot_b):
        if not pathlib.Path(path).exists():
            print(f"::error::引擎不存在：{path}")
            return 1

    work = pathlib.Path(args.work)
    if not args.reuse:
        if work.exists():
            shutil.rmtree(work)
        work.mkdir(parents=True)
        for tag, engine in (("a", args.godot_a), ("b", args.godot_b)):
            for i in range(1, args.runs + 1):
                if not capture(engine, work / f"{tag}{i}", work / f"{tag}{i}.log"):
                    return 1
                print(f"捕获完成：{tag}{i}")
    else:
        need = [work / f"{t}{i}" for t in ("a", "b") for i in range(1, args.runs + 1)]
        if not all(d.is_dir() for d in need):
            print(f"::error::--reuse 需要已有的截图目录：{work}（缺 {[str(d) for d in need if not d.is_dir()]}）")
            return 1
        print(f"复用已有截图：{work}")

    # 噪声底线**逐页**取：全局最大值会被单页的抖动抬起来，容忍随之膨胀、把真差异遮掉
    # （本工具首版正是这么漏判的：一处基线 21.9 把容忍抬到 32.8，换成另一页也照判「等价」）。
    noise = {page: 0.0 for page in PAGES}
    for tag in ("a", "b"):
        for i in range(2, args.runs + 1):
            for page, v in compare(work / f"{tag}1", work / f"{tag}{i}").items():
                noise[page] = max(noise[page], v["mad"])
    noise_max = max(noise.values())

    cross = compare(work / "a1", work / "b1")

    print(f"\n噪声底线（逐页，同引擎两遍的最大 MAD）：最大 {noise_max:.3f}")
    print(f"{'页面':<24}{'基线':>8}{'跨引擎 MAD':>12}{'>8 占比':>10}{'>32 占比':>10}{'容忍':>8}")
    for page in PAGES:
        m = cross[page]
        limit = noise[page] * NOISE_FACTOR + NOISE_ABS
        print(f"{page:<24}{noise[page]:>8.3f}{m['mad']:>12.3f}{m['over8']:>9.2f}%"
              f"{m['over32']:>9.2f}%{limit:>8.3f}")

    # 基线本身超出上限＝同引擎两遍就不可重复（截图序列含抖动元素，或被改过），此时任何
    # 「在容忍内」的结论都不成立——判据不可用必须显式失败，不能顺势把容忍放大后照判等价。
    if noise_max > NOISE_MAX_MAD:
        print(f"\n::error::噪声底线过高（{noise_max:.3f} > {NOISE_MAX_MAD}）：同引擎两遍的截图就"
              "不可重复，跨引擎结论不可用。先查截图序列里的墙钟元素（帧率读出/动画相位），"
              f"或确认工作目录未被改动。证据保留在 {work}")
        return 1

    worst = max(cross.items(), key=lambda kv: kv[1]["mad"])
    worst_limit = noise[worst[0]] * NOISE_FACTOR + NOISE_ABS
    if worst[1]["mad"] <= worst_limit:
        print(f"\n判定：视觉等价（最大 MAD {worst[1]['mad']:.3f} 在噪声容忍内 {worst_limit:.3f}）。")
        if not args.keep:
            shutil.rmtree(work)
        return 0

    print(f"\n跨引擎差异超出噪声（最大 {worst[1]['mad']:.3f} > 逐页容忍 {worst_limit:.3f}），逐条归因：")
    bad_reasons = []
    for page, m in cross.items():
        page_limit = noise[page] * NOISE_FACTOR + NOISE_ABS
        if m["mad"] <= page_limit:
            continue
        r = attribute(m["a"], m["b"], m["mx"])
        moved = r["best"][0] < r["base_mad"] * SHIFT_GAIN
        shift_desc = f"最佳平移 {r['best'][1:]} → MAD {r['best'][0]:.3f}" if moved else "平移无改善"
        print(f"· {page}: MAD {r['base_mad']:.3f}；{shift_desc}；"
              f"平坦区占比 {r['flat_pct']:.1f}%、均值偏移 {[round(v, 3) for v in r['flat_signed']]}、"
              f"命中率 {r['flat']:.2f}%")
        if moved:
            bad_reasons.append(f"{page}: 几何位移")
        elif max(abs(v) for v in r["flat_signed"]) > SIGNED_MEAN_MAX:
            bad_reasons.append(f"{page}: 整体色偏/明暗差（平坦区均值偏移超标）")
        elif r["flat"] > EDGE_FLAT_MAX:
            bad_reasons.append(f"{page}: 差异不止于边缘（平坦区命中率超标）")

    if bad_reasons:
        print("\n判定：检出**结构级差异**（需人工判读）：")
        for reason in bad_reasons:
            print(f"  - {reason}")
        print(f"截图与差异证据保留在 {work}")
        return 1

    print("\n判定：视觉等价（差异仅落在边缘，无几何位移、无整体色偏——抗锯齿/光栅化层面）。")
    if not args.keep:
        shutil.rmtree(work)
    return 0


if __name__ == "__main__":
    sys.exit(main())
