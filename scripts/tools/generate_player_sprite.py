#!/usr/bin/env python3
"""离线玩家战机贴图生成器（钛灰钢甲 + 青色能量，非游戏运行时依赖）。

重绘 assets/sprites/player_ship.png（画布 254×254 与原版一致，机头朝上即游戏内 -Y）。
设计语言参考经典纵版射击主机（Vic Viper / Raiden 系）：尖锐长机头、鸭翼、
后掠三角主翼、双引擎尾喷；与敌方晶体棱镜风格区分——玩家为分层装甲板 + 铆接接缝。

附件锚点（贴图像素坐标，供 scripts/player_buff_visuals.gd 对齐机体部位）：
    机头尖端 (127, 16)   座舱 (127, 92)    鸭翼翼尖 (84/170, 96)
    主翼翼尖 (12/242, 206)                翼根前缘 (104/150, 118)
    背部脊线 y≈118-200   引擎喷口 (108/146, 230)   机尾端 (127, 236)
用法：python3 scripts/tools/generate_player_sprite.py
"""

from PIL import Image, ImageDraw, ImageFilter

S = 4  # 超采样抗锯齿

# 钛灰钢甲分层（冷调蓝灰，由暗到亮）
HULL_A = (26, 32, 44, 255)
HULL_B = (38, 48, 64, 255)
HULL_C = (54, 68, 88, 255)
HULL_D = (76, 94, 118, 255)
SEAM = (10, 14, 22, 255)
RIM = (168, 196, 226, 255)

ACCENT = (88, 216, 255)     # 青色能量
CORE = (150, 240, 255)


class Ship:
    """分层绘制：body（实体面）+ glow（霓虹线/能量，模糊光晕 + 清晰本体）。"""

    def __init__(self, w: int, h: int) -> None:
        self.w, self.h = w, h
        self.body = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
        self.glow = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
        self.bd = ImageDraw.Draw(self.body)
        self.gd = ImageDraw.Draw(self.glow)

    def p(self, pts):
        return [(x * S, y * S) for x, y in pts]

    def mirror(self, pts):
        return [(self.w - x, y) for x, y in pts]

    def facet(self, pts, fill, mirror=True):
        self.bd.polygon(self.p(pts), fill=fill)
        if mirror:
            self.bd.polygon(self.p(self.mirror(pts)), fill=fill)

    def seam(self, pts, width=2, mirror=True):
        self.bd.line(self.p(pts), fill=SEAM, width=width * S, joint="curve")
        if mirror:
            self.bd.line(self.p(self.mirror(pts)), fill=SEAM, width=width * S, joint="curve")

    def rim(self, pts, width=2, mirror=True):
        self.bd.line(self.p(pts), fill=RIM, width=width * S, joint="curve")
        if mirror:
            self.bd.line(self.p(self.mirror(pts)), fill=RIM, width=width * S, joint="curve")

    def neon(self, pts, width=3, color=None, mirror=True):
        c = (color or ACCENT) + (255,)
        self.gd.line(self.p(pts), fill=c, width=width * S, joint="curve")
        if mirror:
            self.gd.line(self.p(self.mirror(pts)), fill=c, width=width * S, joint="curve")

    def canopy(self, cx, cy, rx, ry):
        """座舱盖：青色玻璃 + 白色高光条。"""
        self.gd.ellipse(self.p([(cx - rx, cy - ry), (cx + rx, cy + ry)]), fill=ACCENT + (235,))
        self.gd.ellipse(
            self.p([(cx - rx * 0.42, cy - ry * 0.75), (cx + rx * 0.1, cy - ry * 0.1)]),
            fill=(255, 255, 255, 220),
        )

    def engine(self, cx, cy, rx, ry):
        self.gd.ellipse(self.p([(cx - rx, cy - ry), (cx + rx, cy + ry)]), fill=ACCENT + (230,))
        w = max(rx * 0.45, 2.0)
        self.gd.ellipse(self.p([(cx - w, cy - ry * 0.5), (cx + w, cy + ry * 0.5)]), fill=(255, 255, 255, 240))

    def finish(self, path: str, blur: float = 6.0) -> None:
        halo = self.glow.filter(ImageFilter.GaussianBlur(blur * S))
        out = Image.alpha_composite(halo, self.body)
        out = Image.alpha_composite(out, self.glow)
        out = out.resize((self.w, self.h), Image.LANCZOS)
        out.save(path)
        print("saved", path)


def player_ship() -> Ship:
    s = Ship(254, 254)
    cx = 127

    # ---- 主翼（后掠三角，分层装甲） ----
    s.facet([(104, 118), (12, 206), (36, 216), (102, 176)], HULL_B)          # 外翼面
    s.facet([(104, 124), (34, 200), (48, 206), (100, 170)], HULL_C)          # 内翼承力面
    s.facet([(100, 168), (36, 216), (52, 222), (96, 196)], HULL_A)           # 翼根暗面
    s.seam([(103, 122), (34, 200)])
    s.seam([(101, 170), (50, 206)])
    s.rim([(12, 206), (36, 216)])                                            # 翼尖后缘高光
    s.neon([(103, 120), (14, 206)])                                          # 前缘能量走线

    # ---- 鸭翼（前部小翼） ----
    s.facet([(108, 86), (84, 96), (92, 106), (108, 100)], HULL_C)
    s.rim([(108, 86), (84, 96)])

    # ---- 尾翼（小垂尾外八，根部接翼根） ----
    s.facet([(102, 168), (78, 228), (92, 232), (106, 206)], HULL_B)

    # ---- 主机身（细长梭形，多面装甲） ----
    s.facet([(cx, 16), (143, 96), (137, 196), (cx, 236), (117, 196), (111, 96)], HULL_A, False)
    s.facet([(cx, 16), (143, 96), (cx, 118), (111, 96)], HULL_D, False)      # 机头亮面
    s.facet([(111, 96), (cx, 118), (cx, 236), (117, 196)], HULL_B, False)    # 左腹板
    s.facet([(143, 96), (cx, 118), (cx, 236), (137, 196)], HULL_C, False)    # 右腹板
    s.seam([(cx, 118), (cx, 236)], mirror=False)                             # 中脊接缝
    s.seam([(111, 96), (cx, 118)])
    s.rim([(cx, 16), (143, 96)], mirror=False)                               # 机头描边
    s.rim([(cx, 16), (111, 96)], mirror=False)

    # ---- 引擎舱（尾部双发整流罩） ----
    s.facet([(96, 188), (110, 182), (112, 228), (98, 234)], HULL_C)
    s.facet([(98, 192), (102, 190), (103, 228), (99, 230)], HULL_D)
    s.engine(108, 230, 6, 4)
    s.engine(146, 230, 6, 4)

    # ---- 能量细节 ----
    s.canopy(cx, 92, 9, 18)                                                  # 座舱
    s.neon([(117, 130), (120, 190)], width=2)                                # 侧舷能量缝
    s.neon([(137, 130), (134, 190)], width=2)
    s.neon([(92, 104), (86, 98)], width=2)                                   # 鸭翼尖灯
    s.neon([(13, 205), (22, 201)], width=3)                                  # 主翼尖灯
    s.neon([(80, 226), (90, 230)], width=2)                                  # 垂尾尖灯
    return s


def main() -> None:
    player_ship().finish("assets/sprites/player_ship.png")


if __name__ == "__main__":
    main()
