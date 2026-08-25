#!/usr/bin/env python3
"""为 Boss 精灵生成 P2 阶段损伤变体（裂纹 + 暗淡 + 火花）。

基于现有 boss_ship_N.png 叠加损伤效果，输出 boss_ship_N_p2.png。
用法：python3 scripts/tools/generate_boss_p2_frames.py
"""

import os
from PIL import Image, ImageDraw, ImageFilter

SPRITE_DIR = os.path.normpath(
    os.path.join(os.path.dirname(__file__), "..", "..", "assets", "sprites")
)

# 损伤色调
CRACK = (180, 80, 30, 255)
CRACK_GLOW = (220, 100, 30, 160)
SPARK = (255, 180, 40, 200)
DARK_OVERLAY = (20, 10, 5, 40)


def add_damage(src_path: str, dst_path: str, cracks: list, sparks: list) -> None:
    """叠加损伤效果到现有精灵。"""
    img = Image.open(src_path).convert("RGBA")
    w, h = img.size
    S = 4  # 超采样

    # 放大绘制
    big = img.resize((w * S, h * S), Image.LANCZOS)
    draw = ImageDraw.Draw(big)
    glow = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)

    # 绘制裂纹
    for crack in cracks:
        scaled = [(x * S, y * S) for x, y in crack]
        draw.line(scaled, fill=CRACK, width=2 * S, joint="curve")
        gd.line(scaled, fill=CRACK_GLOW, width=5 * S, joint="curve")

    # 绘制火花
    for sx, sy in sparks:
        r = 3 if (sx + sy) % 5 < 2 else 2
        gd.ellipse([(sx * S - r * S, sy * S - r * S),
                     (sx * S + r * S, sy * S + r * S)], fill=SPARK)

    # 合成辉光
    blurred = glow.filter(ImageFilter.GaussianBlur(4 * S))
    big = Image.alpha_composite(big, blurred)
    big = Image.alpha_composite(big, glow)

    # 暗淡叠加
    dark = Image.new("RGBA", (w * S, h * S), DARK_OVERLAY)
    big = Image.alpha_composite(big, dark)

    # 缩小输出
    out = big.resize((w, h), Image.LANCZOS)
    out.save(dst_path)
    print(f"saved {dst_path}")


def main() -> None:
    # 每种 Boss 的裂纹/火花位置（相对坐标，适配 410×410 画布）
    # Boss 1（君王：重型宽翼）
    b1_cracks = [
        [(180, 140), (200, 180), (220, 220), (205, 260)],
        [(230, 140), (210, 180), (190, 220), (205, 260)],
        [(120, 200), (160, 230), (200, 250)],
        [(290, 200), (250, 230), (210, 250)],
    ]
    b1_sparks = [(195, 160), (215, 160), (205, 200), (170, 220), (240, 220),
                 (205, 240), (185, 180), (225, 180)]

    # Boss 2（九头：三联舰体）
    b2_cracks = [
        [(160, 120), (175, 160), (165, 200)],
        [(250, 120), (235, 160), (245, 200)],
        [(205, 100), (200, 150), (210, 200)],
        [(140, 250), (170, 270), (200, 280)],
        [(270, 250), (240, 270), (210, 280)],
    ]
    b2_sparks = [(170, 140), (240, 140), (205, 170), (155, 230), (255, 230),
                 (205, 260), (180, 190), (230, 190)]

    # Boss 3（巨柱：六边要塞）
    b3_cracks = [
        [(170, 130), (185, 170), (175, 210), (190, 250)],
        [(240, 130), (225, 170), (235, 210), (220, 250)],
        [(205, 110), (200, 160), (210, 210)],
        [(150, 280), (180, 300), (210, 310)],
        [(260, 280), (230, 300), (200, 310)],
    ]
    b3_sparks = [(180, 150), (230, 150), (205, 190), (165, 260), (245, 260),
                 (205, 290), (190, 170), (220, 170)]

    # Boss 4（琥珀/紫罗兰/红宝石/霜蓝）
    b4_cracks = [
        [(160, 150), (180, 190), (170, 230)],
        [(250, 150), (230, 190), (240, 230)],
        [(205, 120), (200, 170), (210, 220)],
        [(130, 260), (165, 285), (200, 300)],
        [(280, 260), (245, 285), (210, 300)],
    ]
    b4_sparks = [(175, 170), (235, 170), (205, 200), (150, 250), (260, 250),
                 (205, 280), (185, 150), (225, 150)]

    configs = [
        ("boss_ship_1.png", "boss_ship_1_p2.png", b1_cracks, b1_sparks),
        ("boss_ship_2.png", "boss_ship_2_p2.png", b2_cracks, b2_sparks),
        ("boss_ship_3.png", "boss_ship_3_p2.png", b3_cracks, b3_sparks),
        ("boss_ship_4.png", "boss_ship_4_p2.png", b4_cracks, b4_sparks),
    ]

    for src, dst, cracks, sparks in configs:
        add_damage(
            os.path.join(SPRITE_DIR, src),
            os.path.join(SPRITE_DIR, dst),
            cracks, sparks,
        )


if __name__ == "__main__":
    main()
