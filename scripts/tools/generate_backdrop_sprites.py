#!/usr/bin/env python3
"""离线深空背景贴图生成器（远景行星 + 中层残骸剪影，非游戏运行时依赖）。

重绘 assets/sprites/backdrop/ 下 5 张背景贴图：
- planet_1.png（900×900）：暖炭色大型行星圆盘 + 琥珀大气边缘辉光（画面最远端天体）。
- planet_2.png（640×640）：残月——暗面炭灰盘 + 一弯冷色受光缘（第二颗远景天体）。
- debris_1..3.png（300~500px 不等）：空间站残骸剪影 3 变体，暗钢色壳体 +
  微弱舷窗灯点（琥珀/全息青），运行期由 DeepSpaceBackdrop.cs 压暗降透明度使用。

纯确定性绘制（无随机源、无种子）：全部形面由固定顶点表/解析函数绘制，
4× 超采样降采样抗锯齿，与舰队贴图同管线；重跑输出逐字节一致。

用法：python3 scripts/tools/generate_backdrop_sprites.py
"""

import math
import os

import sprite_polish
from PIL import Image, ImageDraw, ImageFilter

S = 4  # 超采样抗锯齿（与舰队生成器一致）

# 战术琥珀 / 全息青（与 UITheme 色温同源的光点色）
AMBER = (255, 186, 100)
TEAL = (120, 220, 255)

# 残骸暗钢分层（冷调蓝灰，由暗到亮，与母舰生成器同族）
STEEL_A = (24, 29, 40, 255)
STEEL_B = (34, 42, 56, 255)
STEEL_C = (48, 60, 80, 255)
SEAM = (10, 14, 22, 255)


def out_path(name: str) -> str:
    # 输出路径锚定脚本位置（同其余生成器口径），不依赖调用时 cwd
    return os.path.normpath(
        os.path.join(os.path.dirname(__file__), "..", "..", "assets", "sprites", "backdrop", name)
    )


def save(img: Image.Image, w: int, h: int, path: str, polish: bool = True) -> None:
    out = img.resize((w, h), Image.LANCZOS)
    if polish:
        out = sprite_polish.polish(out)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    out.save(path)
    print("saved", path)


def disc_mask(size: int, cx: float, cy: float, r: float) -> Image.Image:
    """4× 画布上的实心圆 alpha 掩模（L 模式，与绘制层同尺寸）。"""
    m = Image.new("L", (size * S, size * S), 0)
    d = ImageDraw.Draw(m)
    d.ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], fill=255)
    return m


# ---- planet_1：暖炭色行星圆盘 + 琥珀大气辉光 ----

def planet_1() -> None:
    size, cx, cy, r = 900, 450, 452, 392
    mask = disc_mask(size, cx, cy, r)

    # 地表：暖炭基底 + 正弦纬度条带（解析函数，确定性），右上受光、左下明暗界线
    body = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    bd = ImageDraw.Draw(body)
    base = (46, 36, 30)
    for y in range(size):
        band = math.sin(y * 0.055) * 0.5 + math.sin(y * 0.013 + 1.7) * 0.5  # [-1,1] 双层条带
        lift = int(10 * band)
        bd.line([(0, y * S), (size * S, y * S)], fill=(base[0] + lift, base[1] + lift // 2, base[2] + lift // 3, 255), width=S)

    # 明暗界线：左下叠大块暗面（重模糊），右上叠受光面，均裁进圆盘
    shade = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shade)
    sd.ellipse([(cx - r * 1.9) * S, (cy - r * 0.4) * S, (cx + r * 0.35) * S, (cy + r * 1.9) * S], fill=(8, 7, 9, 200))
    shade = shade.filter(ImageFilter.GaussianBlur(60 * S))
    lit = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    ld = ImageDraw.Draw(lit)
    ld.ellipse([(cx + r * 0.1) * S, (cy - r * 1.6) * S, (cx + r * 1.8) * S, (cy + r * 0.5) * S], fill=(255, 208, 150, 46))
    lit = lit.filter(ImageFilter.GaussianBlur(50 * S))
    for layer in (shade, lit):
        layer.putalpha(Image.composite(layer.getchannel("A"), Image.new("L", layer.size, 0), mask))
        body = Image.alpha_composite(body, layer)
    body.putalpha(mask)

    # 大气辉光：盘外晕（琥珀，厚模糊）垫底 + 盘缘亮环（受光侧加强）
    halo = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    hd = ImageDraw.Draw(halo)
    hd.ellipse([(cx - r - 26) * S, (cy - r - 26) * S, (cx + r + 26) * S, (cy + r + 26) * S], outline=AMBER + (110,), width=30 * S)
    halo = halo.filter(ImageFilter.GaussianBlur(14 * S))

    rim = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    rd = ImageDraw.Draw(rim)
    rd.ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], outline=AMBER + (150,), width=3 * S)
    rd.arc([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], start=250, end=40, fill=(255, 224, 170, 230), width=5 * S)  # 右上受光缘
    rim = rim.filter(ImageFilter.GaussianBlur(1.2 * S))

    img = Image.alpha_composite(halo, body)
    img = Image.alpha_composite(img, rim)
    save(img, size, size, out_path("planet_1.png"), polish=False)


# ---- planet_2：残月（暗面炭灰盘 + 冷色受光弯缘） ----

def planet_2() -> None:
    size, cx, cy, r = 640, 320, 322, 252
    mask_lit = disc_mask(size, cx + 34, cy - 30, r - 8)   # 受光盘（偏移出弯月）
    mask_dark = disc_mask(size, cx, cy, r)

    # 暗面盘：炭灰微蓝，极低调（背景天体，运行期再压暗）
    body = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    bd = ImageDraw.Draw(body)
    bd.ellipse([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], fill=(26, 28, 36, 255))

    # 弯月受光面 = 受光盘 − 暗面盘，填冷灰；缘光沿弯月外缘勾冷白
    crescent = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    cd = ImageDraw.Draw(crescent)
    cd.ellipse([(cx + 34 - r + 8) * S, (cy - 30 - r + 8) * S, (cx + 34 + r - 8) * S, (cy - 30 + r - 8) * S], fill=(104, 118, 142, 255))
    cmask = Image.composite(mask_lit, Image.new("L", crescent.size, 0), mask_dark.point(lambda v: 255 - v))
    crescent.putalpha(cmask)

    glow = Image.new("RGBA", (size * S, size * S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gd.ellipse([(cx + 34 - r + 8) * S, (cy - 30 - r + 8) * S, (cx + 34 + r - 8) * S, (cy - 30 + r - 8) * S], outline=(208, 228, 250, 200), width=3 * S)
    glow.putalpha(Image.composite(glow.getchannel("A"), Image.new("L", glow.size, 0), cmask))
    halo = glow.filter(ImageFilter.GaussianBlur(6 * S))

    img = Image.alpha_composite(halo, body)
    img = Image.alpha_composite(img, crescent)
    img = Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(0.8 * S)))
    save(img, size, size, out_path("planet_2.png"), polish=False)


# ---- 残骸剪影：固定顶点表 + 接缝线 + 舷窗灯点 ----

class Wreck:
    """分层绘制：body（壳体面/接缝）+ glow（灯点，模糊光晕 + 清晰本体），同舰队生成器口径。"""

    def __init__(self, w: int, h: int) -> None:
        self.w, self.h = w, h
        self.body = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
        self.glow = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
        self.bd = ImageDraw.Draw(self.body)
        self.gd = ImageDraw.Draw(self.glow)

    def p(self, pts):
        return [(x * S, y * S) for x, y in pts]

    def plate(self, pts, fill):
        self.bd.polygon(self.p(pts), fill=fill)

    def seam(self, pts, width=2):
        self.bd.line(self.p(pts), fill=SEAM, width=width * S, joint="curve")

    def lamp(self, x, y, r=2.0, color=AMBER):
        self.gd.ellipse(self.p([(x - r, y - r), (x + r, y + r)]), fill=color + (255,))

    def finish(self, path: str) -> None:
        halo = self.glow.filter(ImageFilter.GaussianBlur(3.0 * S))
        out = Image.alpha_composite(halo, self.body)
        out = Image.alpha_composite(out, self.glow)
        save(out, self.w, self.h, path)


def debris_1() -> None:
    # 断裂的舱段环：弧形主板 + 内缘桁架缺口
    k = Wreck(480, 360)
    k.plate([(40, 210), (120, 96), (268, 40), (420, 84), (452, 150), (360, 128), (250, 108), (150, 160), (96, 250)], STEEL_B)
    k.plate([(96, 250), (150, 160), (250, 108), (360, 128), (452, 150), (430, 210), (300, 180), (180, 220), (120, 300)], STEEL_A)
    k.plate([(120, 96), (268, 40), (420, 84), (396, 96), (270, 62), (136, 112)], STEEL_C)  # 背光棱面
    k.seam([(150, 160), (250, 108), (360, 128)])
    k.seam([(180, 220), (300, 180), (430, 210)])
    k.seam([(250, 108), (250, 62)], width=1)
    k.lamp(210, 150, 2.4)
    k.lamp(252, 138, 2.0)
    k.lamp(294, 140, 2.4, TEAL)
    k.lamp(340, 156, 1.8)
    k.lamp(160, 236, 2.0, TEAL)
    k.finish(out_path("debris_1.png"))


def debris_2() -> None:
    # 竖向桁架残段：主梁 + 三组横撑 + 断裂豁口
    k = Wreck(360, 440)
    k.plate([(150, 20), (208, 20), (222, 180), (196, 208), (230, 260), (214, 420), (146, 420), (132, 250), (158, 214), (136, 160)], STEEL_B)
    k.plate([(150, 20), (178, 20), (180, 420), (146, 420), (132, 250), (158, 214), (136, 160)], STEEL_C)  # 受光半梁
    for y0 in (70, 160, 250, 340):
        k.plate([(60, y0 + 14), (136, y0), (136, y0 + 18), (64, y0 + 32)], STEEL_A)   # 左横撑
        k.plate([(222, y0 + 6), (304, y0 + 20), (300, y0 + 38), (226, y0 + 24)], STEEL_A)  # 右横撑
        k.seam([(60, y0 + 14), (136, y0)], width=1)
        k.seam([(222, y0 + 6), (304, y0 + 20)], width=1)
    k.seam([(180, 24), (180, 416)], width=1)
    k.lamp(166, 60, 2.2)
    k.lamp(168, 122, 2.0, TEAL)
    k.lamp(190, 300, 2.2)
    k.lamp(160, 380, 1.8)
    k.finish(out_path("debris_2.png"))


def debris_3() -> None:
    # 厚壳体碎片：多折面板块 + 舷窗双排
    k = Wreck(420, 300)
    k.plate([(30, 190), (90, 80), (220, 30), (360, 70), (396, 150), (300, 240), (140, 258), (52, 232)], STEEL_B)
    k.plate([(90, 80), (220, 30), (360, 70), (330, 96), (216, 58), (108, 104)], STEEL_C)
    k.plate([(52, 232), (140, 258), (300, 240), (268, 262), (150, 276), (70, 250)], STEEL_A)
    k.plate([(220, 30), (300, 240), (268, 262), (216, 58)], STEEL_A)  # 中央纵向折面
    k.seam([(90, 80), (216, 58), (330, 96)])
    k.seam([(216, 58), (220, 30)], width=1)
    k.seam([(140, 258), (150, 276)], width=1)
    k.seam([(300, 240), (268, 262)], width=1)
    for i, x in enumerate((120, 156, 192, 250, 286)):
        k.lamp(x, 150 + (i % 2) * 26, 2.0, AMBER if i % 3 != 2 else TEAL)
    k.lamp(330, 130, 2.4, TEAL)
    k.finish(out_path("debris_3.png"))


def main() -> None:
    planet_1()
    planet_2()
    debris_1()
    debris_2()
    debris_3()


if __name__ == "__main__":
    main()
