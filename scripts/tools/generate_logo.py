#!/usr/bin/env python3
"""离线标题 logo 生成器（assets/sprites/ui/logo.png，非游戏运行时依赖）。

输出 900×260 透明底字标：「InfiAir」琥珀渐变（上亮下深）+ 深色描边 + 顶部受光高光 +
微弱外辉光，左侧切角菱形徽章内嵌三角翼剪影（呼应玩家机轮廓）。视觉语言与
ChamferedPanel 切角钢板 / 琥珀仪表盘一致。

字标用 assets/fonts/NotoSansSC.ttf 渲染，逐字符排布加字距（对应标题屏原 Label 的
SpacingGlyph 风格）。全程 4× 超采样绘制后 LANCZOS 降采样抗锯齿；纯确定性绘制
（无随机源），重跑逐字节一致。

用法：python3 scripts/tools/generate_logo.py
"""

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

S = 4  # 超采样抗锯齿

ROOT = Path(__file__).resolve().parents[2]
FONT_PATH = ROOT / "assets" / "fonts" / "NotoSansSC.ttf"
OUT_PATH = ROOT / "assets" / "sprites" / "ui" / "logo.png"

W, H = 900, 260          # 成品画布（最终坐标，绘制时 ×S）
TEXT = "InfiAir"
FONT_SIZE = 150          # 字号（最终坐标）
TRACKING = 7             # 逐字符附加字距（最终坐标）

# 战术琥珀色板（与 UITheme / 玩家机贴图同源）
AMBER_TOP = (255, 198, 96)     # 字标渐变顶（受光）
AMBER_MID = (255, 159, 28)     # 主强调琥珀 #FF9F1C
AMBER_BOT = (168, 88, 8)       # 字标渐变底（背光）
OUTLINE = (26, 15, 8)          # 深色描边（暖黑）
HIGHLIGHT = (255, 240, 214)    # 顶部受光高光
STEEL_TOP = (62, 55, 48)       # 徽章钢板渐变顶
STEEL_BOT = (30, 27, 24)       # 徽章钢板渐变底

OUTLINE_W = 7            # 描边宽（最终坐标）
EMBOLDEN = 3             # 字形加粗量（遮罩外扩，最终坐标）
HIGHLIGHT_H = 3          # 高光带厚（最终坐标）
GLOW_R = 12              # 外辉光半径（最终坐标）
GLOW_A = 0.42            # 外辉光峰值不透明度

# 徽章：切角菱形（外接半径 R，切角距 CHAMFER）；徽章与字标成组水平居中
BADGE_R = 74.0
BADGE_CHAMFER = 20.0
BADGE_GAP = 56.0         # 徽章右缘到字标左缘（最终坐标）
BORDER_W = 5             # 徽章描边宽（最终坐标）
INNER_SCALE = 0.86       # 内圈细线相对外轮廓缩放


def vgrad(w: int, h: int, stops: list[tuple[float, tuple[int, int, int]]]) -> Image.Image:
    """纵向分段线性渐变（t=0 顶 → t=1 底）。"""
    col = Image.new("RGB", (1, h))
    px = []
    for y in range(h):
        t = y / max(1, h - 1)
        for i in range(len(stops) - 1):
            t0, c0 = stops[i]
            t1, c1 = stops[i + 1]
            if t <= t1 or i == len(stops) - 2:
                f = 0.0 if t1 == t0 else min(1.0, max(0.0, (t - t0) / (t1 - t0)))
                px.append(tuple(int(round(c0[k] + (c1[k] - c0[k]) * f)) for k in range(3)))
                break
    col.putdata(px)
    return col.resize((w, h))


def chamfered_diamond(cx: float, cy: float, r: float, chamfer: float) -> list[tuple[float, float]]:
    """切角菱形八边形：四个顶点各沿邻边回退 chamfer 形成切角。"""
    verts = [(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)]
    pts: list[tuple[float, float]] = []
    for i, (vx, vy) in enumerate(verts):
        for j in ((i - 1) % 4, (i + 1) % 4):
            nx, ny = verts[j]
            dx, dy = nx - vx, ny - vy
            length = (dx * dx + dy * dy) ** 0.5
            pts.append((vx + dx / length * chamfer, vy + dy / length * chamfer))
    return pts


def scaled(pts: list[tuple[float, float]], cx: float, cy: float, f: float) -> list[tuple[float, float]]:
    return [(cx + (x - cx) * f, cy + (y - cy) * f) for x, y in pts]


def p(pts: list[tuple[float, float]]) -> list[tuple[float, float]]:
    return [(x * S, y * S) for x, y in pts]


def draw_text_masks() -> tuple[Image.Image, Image.Image, tuple[int, int, int, int]]:
    """逐字符排布字标，返回（内芯遮罩, 含描边整体遮罩, 内芯外接框），遮罩为 L 模式、S 倍画布。

    内芯遮罩按 EMBOLDEN 外扩加粗；整体遮罩在其外再扩 OUTLINE_W 作描边层。
    徽章与字标视为一组水平居中：徽章居左、字标居右，组垂直居中于画布。"""
    font = ImageFont.truetype(str(FONT_PATH), FONT_SIZE * S)
    inner = Image.new("L", (W * S, H * S), 0)
    full = Image.new("L", (W * S, H * S), 0)
    di = ImageDraw.Draw(inner)
    df = ImageDraw.Draw(full)

    adv = [font.getlength(ch) for ch in TEXT]
    text_w = sum(adv) + TRACKING * S * (len(TEXT) - 1)
    group_w = (BADGE_R * 2 + BADGE_GAP) * S + text_w
    badge_cx = (W * S - group_w) / 2.0 + BADGE_R * S
    x = (W * S - group_w) / 2.0 + (BADGE_R * 2 + BADGE_GAP) * S
    bbox = font.getbbox(TEXT)
    y = (H * S - (bbox[1] + bbox[3])) / 2.0
    for ch, a in zip(TEXT, adv):
        di.text((x, y), ch, font=font, fill=255,
                stroke_width=EMBOLDEN * S, stroke_fill=255)
        df.text((x, y), ch, font=font, fill=255,
                stroke_width=(EMBOLDEN + OUTLINE_W) * S, stroke_fill=255)
        x += a + TRACKING * S
    return inner, full, inner.getbbox(), badge_cx


def composite_solid(base: Image.Image, mask: Image.Image,
                    color: tuple[int, int, int], alpha_scale: float = 1.0) -> Image.Image:
    """纯色层按遮罩（可选衰减）合成到 base 上。"""
    m = mask if alpha_scale >= 1.0 else mask.point(lambda a: int(a * alpha_scale))
    layer = Image.new("RGBA", base.size, color + (255,))
    layer.putalpha(m)
    return Image.alpha_composite(base, layer)


def build() -> Image.Image:
    cy = H / 2.0
    base = Image.new("RGBA", (W * S, H * S), (0, 0, 0, 0))

    inner, full, text_box, badge_cx = draw_text_masks()
    cx = badge_cx / S  # 以下图形常量均为最终坐标

    # 外辉光：整体遮罩高斯模糊后低透明度垫底
    glow_mask = full.filter(ImageFilter.GaussianBlur(GLOW_R * S))
    base = composite_solid(base, glow_mask, AMBER_MID, GLOW_A)

    # 徽章钢板：切角菱形纵向渐变填充
    plate = chamfered_diamond(cx, cy, BADGE_R, BADGE_CHAMFER)
    plate_mask = Image.new("L", base.size, 0)
    ImageDraw.Draw(plate_mask).polygon(p(plate), fill=255)
    steel = vgrad(base.size[0], base.size[1], [(0.0, STEEL_TOP), (1.0, STEEL_BOT)])
    base.paste(Image.merge("RGBA", (*steel.split(), plate_mask)), (0, 0), plate_mask)

    # 徽章描边 + 内圈细线 + 左右顶点铆点
    bd = ImageDraw.Draw(base)
    bd.polygon(p(plate), outline=AMBER_MID + (255,), width=BORDER_W * S)
    bd.polygon(p(scaled(plate, cx, cy, INNER_SCALE)), outline=AMBER_MID + (110,), width=2 * S)
    for vx in (cx - BADGE_R + BADGE_CHAMFER * 0.5, cx + BADGE_R - BADGE_CHAMFER * 0.5):
        r = 2.5 * S
        bd.ellipse([(vx * S - r, cy * S - r), (vx * S + r, cy * S + r)], fill=(150, 136, 116, 255))

    # 字标琥珀渐变：映射到字标外接框高度（上亮下深），徽章剪影共用
    ty0, ty1 = text_box[1], text_box[3]
    amber = Image.new("RGB", base.size, AMBER_TOP)
    amber.paste(vgrad(base.size[0], ty1 - ty0,
                      [(0.0, AMBER_TOP), (0.55, AMBER_MID), (1.0, AMBER_BOT)]), (0, ty0))
    amber.paste(Image.new("RGB", (base.size[0], base.size[1] - ty1), AMBER_BOT), (0, ty1))

    # 三角翼剪影：琥珀渐变填充 + 深色描边 + 脊线
    wing = [(cx, cy - 40.0), (cx + 34.0, cy + 30.0), (cx, cy + 12.0), (cx - 34.0, cy + 30.0)]
    wing_mask = Image.new("L", base.size, 0)
    ImageDraw.Draw(wing_mask).polygon(p(wing), fill=255)
    base.paste(Image.merge("RGBA", (*amber.split(), wing_mask)), (0, 0), wing_mask)
    bd.polygon(p(wing), outline=OUTLINE + (255,), width=2 * S)
    bd.line(p([(cx, cy - 30.0), (cx, cy + 8.0)]), fill=OUTLINE + (150,), width=1 * S)

    # 字标：深色描边层垫底，琥珀渐变内芯叠上
    base = composite_solid(base, full, OUTLINE)
    base.paste(Image.merge("RGBA", (*amber.split(), inner)), (0, 0), inner)

    # 顶部受光高光：内芯遮罩与其下移副本相减，只留各笔画顶部一条亮带
    edge = ImageChops.subtract(inner, ImageChops.offset(inner, 0, HIGHLIGHT_H * S))
    base = composite_solid(base, edge, HIGHLIGHT, 0.7)

    return base.resize((W, H), Image.LANCZOS)


def main() -> None:
    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    img = build()
    img.save(OUT_PATH, optimize=True)
    print(f"generated {OUT_PATH.relative_to(ROOT)} ({OUT_PATH.stat().st_size} B)")


if __name__ == "__main__":
    main()
