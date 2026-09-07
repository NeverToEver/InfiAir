#!/usr/bin/env python3
"""生成 UI 金属贴图（assets/sprites/ui/）。仅标准库，无 PIL 依赖；固定种子可重现。

设计约定：所有贴图均为近白灰度（基色 ~205），色相/明度层次在运行时由
modulate_color / 顶点色派生（单源），贴图只承担「拉丝纹理 + 预烘焙倒角」。

- metal_streak.png        128×128 拉丝钢板，双轴无缝平铺（ChamferedPanel 填充）
- button_plate.png        48×48 凸起钢板（九宫格 margin=8，倒角已烘焙进贴图）
- button_plate_pressed.png 48×48 凹陷钢板（倒角反转：上暗下亮，受光方向不变）
"""

import random
import struct
import zlib
from pathlib import Path

OUT_DIR = Path(__file__).resolve().parents[2] / "assets" / "sprites" / "ui"
SEED = 20260907


def write_png(path: Path, w: int, h: int, rows: list[list[int]]) -> None:
    raw = b"".join(b"\x00" + bytes(row) for row in rows)
    ihdr = struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", ihdr)
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def circular_smooth(values: list[float], passes: int) -> list[float]:
    """循环卷积 [1,2,1]/4：保证首尾衔接（平铺无缝）。"""
    n = len(values)
    for _ in range(passes):
        values = [
            (values[(i - 1) % n] + 2 * values[i] + values[(i + 1) % n]) / 4.0
            for i in range(n)
        ]
    return values


def normalize(values: list[float]) -> list[float]:
    peak = max(abs(v) for v in values) or 1.0
    return [v / peak for v in values]


def row_walk(rng: random.Random, n: int) -> list[float]:
    """一行内的横向随机游走（循环）→ 运动模糊式拉丝纹，水平无缝。"""
    walk = 0.0
    out = []
    for _ in range(n):
        walk = walk * 0.88 + rng.uniform(-1.0, 1.0) * 0.6
        out.append(walk)
    return normalize(circular_smooth(out, 2))


def clamp8(v: float) -> int:
    return max(0, min(255, int(round(v))))


def gen_streak() -> None:
    """面板拉丝钢板：行带（宽尺度明暗）+ 行内横向拉丝 + 细颗粒，双轴无缝。
    对比度按「深色 tint × 0.8 透明度下仍可辨」校准。"""
    rng = random.Random(SEED)
    w = h = 128
    bands = normalize(circular_smooth([rng.uniform(-1, 1) for _ in range(h)], 4))
    walks = [row_walk(rng, w) for _ in range(h)]
    rows: list[list[int]] = []
    for y in range(h):
        base = 212 + bands[y] * 26.0
        row: list[int] = []
        for x in range(w):
            v = base + walks[y][x] * 14.0 + rng.uniform(-4.0, 4.0)
            row += [clamp8(v)] * 3 + [255]
        rows.append(row)
    write_png(OUT_DIR / "metal_streak.png", w, h, rows)


# 九宫格 margin=8：0..7 / 40..47 为倒角带，中间体区平铺。受光方向统一「上偏左」。
RAISED_PROFILE = {
    0: 104, 1: 216, 2: 246, 3: 228, 4: 210, 5: 202, 6: 199, 7: 198,
    40: 194, 41: 176, 42: 154, 43: 130, 44: 110, 45: 94, 46: 82, 47: 70,
}
PRESSED_PROFILE = {
    0: 104, 1: 78, 2: 94, 3: 118, 4: 144, 5: 164, 6: 176, 7: 184,
    40: 190, 41: 202, 42: 220, 43: 236, 44: 246, 45: 228, 46: 168, 47: 96,
}


def bevel_col_profile(kind: str) -> dict[int, int]:
    """列向倒角（左受光/右背光）；pressed 左缘阴影、右缘受光（凹陷反转）。"""
    if kind == "raised":
        return {0: 104, 1: 208, 2: 224, 3: 212, 4: 202, 5: 198, 6: 196, 7: 196,
                40: 188, 41: 172, 42: 152, 43: 134, 44: 118, 45: 104, 46: 90, 47: 76}
    return {0: 104, 1: 84, 2: 104, 3: 130, 4: 152, 5: 168, 6: 178, 7: 184,
            40: 186, 41: 202, 42: 220, 43: 236, 44: 244, 45: 226, 46: 178, 47: 92}


def gen_button_plate(name: str, kind: str) -> None:
    rng = random.Random(SEED + (1 if kind == "raised" else 2))
    n = 48
    prof = RAISED_PROFILE if kind == "raised" else PRESSED_PROFILE
    cprof = bevel_col_profile(kind)
    body = 206 if kind == "raised" else 196
    walks = [row_walk(rng, n) for _ in range(n)]
    rows: list[list[int]] = []
    for y in range(n):
        base = prof.get(y, body)
        row: list[int] = []
        for x in range(n):
            v = min(base, cprof.get(x, body))  # 角部取更暗者 → 立体折角
            v += walks[y][x] * 9.0 + rng.uniform(-3.0, 3.0)
            row += [clamp8(v)] * 3 + [255]
        rows.append(row)
    write_png(OUT_DIR / name, n, n, rows)


def main() -> None:
    gen_streak()
    gen_button_plate("button_plate.png", "raised")
    gen_button_plate("button_plate_pressed.png", "pressed")
    for p in sorted(OUT_DIR.glob("*.png")):
        print(f"generated {p.relative_to(OUT_DIR.parents[2])} ({p.stat().st_size} B)")


if __name__ == "__main__":
    main()
