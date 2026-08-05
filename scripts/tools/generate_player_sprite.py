#!/usr/bin/env python3
"""离线玩家战机贴图生成器（钛灰钢甲 + 青色能量，非游戏运行时依赖）。

重绘 assets/sprites/player_ship.png（画布 254×254 与原版一致，机头朝上即游戏内 -Y）。
设计语言参考经典纵版射击主机（Vic Viper / Raiden 系）：尖锐长机头、鸭翼、
后掠三角主翼、双引擎尾喷；与敌方晶体棱镜风格区分——玩家为分层装甲板 + 铆接接缝。

精细化层次（外形轮廓与下列锚点严格不变，仅叠加细节）：
- 装甲板细分：子面 + 环带/板划接缝 + 铆接点（panel_dot）+ 散热格栅（vent）+ 舱口（greeble）
- 引擎区：喷管装甲环（nozzle_ring）+ 内焰白芯 + 微粒子点（engine_particles）
- 霓虹加密：脊线/翼后缘二级走线 + 节点航行灯（lamp）
- 座舱：钢甲框缘 + 原有青色玻璃高光

附件锚点（贴图像素坐标，供 scripts/player_buff_visuals.gd 对齐机体部位）：
    机头尖端 (127, 16)   座舱 (127, 92)    鸭翼翼尖 (84/170, 96)
    主翼翼尖 (12/242, 206)                翼根前缘 (104/150, 118)
    背部脊线 y≈118-200   引擎喷口 (108/146, 230)   机尾端 (127, 236)
用法：python3 scripts/tools/generate_player_sprite.py
"""

import os

from PIL import Image, ImageDraw, ImageFilter

S = 4  # 超采样抗锯齿

# 钛灰钢甲分层（冷调蓝灰，由暗到亮）
HULL_A = (26, 32, 44, 255)
HULL_B = (38, 48, 64, 255)
HULL_C = (54, 68, 88, 255)
HULL_D = (76, 94, 118, 255)
SEAM = (10, 14, 22, 255)
RIM = (168, 196, 226, 255)
RIVET = (122, 142, 170, 255)   # 铆接点（亮于 SEAM 暗于 RIM）

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

    def shade(self, pts, alpha=45, dark=True, mirror=True):
        """半透明明暗叠加面。"""
        c = (6, 8, 16, alpha) if dark else (220, 235, 255, alpha)
        self.bd.polygon(self.p(pts), fill=c)
        if mirror:
            self.bd.polygon(self.p(self.mirror(pts)), fill=c)

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

    def lamp(self, x, y, r=2.0, color=None, mirror=True):
        """节点灯/航行灯（glow 层小光点）。"""
        c = (color or ACCENT) + (255,)
        self.gd.ellipse(self.p([(x - r, y - r), (x + r, y + r)]), fill=c)
        if mirror:
            self.gd.ellipse(self.p([(self.w - x - r, y - r), (self.w - x + r, y + r)]), fill=c)

    def panel_dot(self, x, y, r=1.5, mirror=True):
        """铆接/焊点（body 层小圆点）。"""
        self.bd.ellipse(self.p([(x - r, y - r), (x + r, y + r)]), fill=RIVET)
        if mirror:
            self.bd.ellipse(self.p([(self.w - x - r, y - r), (self.w - x + r, y + r)]), fill=RIVET)

    def vent(self, x, y, length=8, gap=3, n=3, width=1, mirror=True):
        """散热格栅：一组平行短横线（向右延伸，镜像侧向左）。"""
        for i in range(n):
            yy = y + i * gap
            self.bd.line(self.p([(x, yy), (x + length, yy)]), fill=SEAM, width=width * S)
            if mirror:
                self.bd.line(self.p([(self.w - x, yy), (self.w - x - length, yy)]), fill=SEAM, width=width * S)

    def greeble(self, x, y, w, h, fill=None, mirror=True, outline=True):
        """舱口/设备小矩形。"""
        c = fill or HULL_B
        kw = {"fill": c}
        if outline:
            kw["outline"] = SEAM
            kw["width"] = S
        self.bd.rectangle(self.p([(x, y), (x + w, y + h)]), **kw)
        if mirror:
            self.bd.rectangle(self.p([(self.w - x - w, y), (self.w - x, y + h)]), **kw)

    def canopy(self, cx, cy, rx, ry):
        """座舱盖：青色玻璃 + 白色高光条。"""
        self.gd.ellipse(self.p([(cx - rx, cy - ry), (cx + rx, cy + ry)]), fill=ACCENT + (235,))
        self.gd.ellipse(
            self.p([(cx - rx * 0.42, cy - ry * 0.75), (cx + rx * 0.1, cy - ry * 0.1)]),
            fill=(255, 255, 255, 220),
        )

    def canopy_frame(self, cx, cy, rx, ry):
        """座舱钢甲框缘（body 层，先于 canopy 调用）。"""
        self.bd.ellipse(self.p([(cx - rx - 2, cy - ry - 2), (cx + rx + 2, cy + ry + 2)]), fill=SEAM)
        self.bd.ellipse(self.p([(cx - rx - 1, cy - ry - 1), (cx + rx + 1, cy + ry + 1)]), fill=HULL_D)

    def nozzle_ring(self, cx, cy, rx, ry):
        """喷管装甲环（body 层，先于 engine 调用）。"""
        self.bd.ellipse(self.p([(cx - rx - 2.5, cy - ry - 2.5), (cx + rx + 2.5, cy + ry + 2.5)]), fill=SEAM)
        self.bd.ellipse(self.p([(cx - rx - 1, cy - ry - 1), (cx + rx + 1, cy + ry + 1)]), fill=HULL_C)

    def engine(self, cx, cy, rx, ry):
        self.gd.ellipse(self.p([(cx - rx, cy - ry), (cx + rx, cy + ry)]), fill=ACCENT + (230,))
        w = max(rx * 0.45, 2.0)
        self.gd.ellipse(self.p([(cx - w, cy - ry * 0.5), (cx + w, cy + ry * 0.5)]), fill=(255, 255, 255, 240))

    def engine_particles(self, cx, cy, n=3, drop=8, spread=4):
        """喷口微粒子点（glow 层，确定性排布）。"""
        c = ACCENT + (200,)
        for i in range(n):
            t = i + 1
            x = cx + ((-1) ** i) * spread * (0.4 + 0.3 * i)
            y = cy + drop * t / n + 2
            r = max(1.2 - 0.2 * i, 0.7)
            self.gd.ellipse(self.p([(x - r, y - r), (x + r, y + r)]), fill=c)

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
    s.facet([(100, 130), (48, 192), (62, 198), (98, 162)], HULL_D)           # 内翼子面（提亮板块）
    s.facet([(100, 168), (36, 216), (52, 222), (96, 196)], HULL_A)           # 翼根暗面
    s.shade([(40, 210), (52, 220), (96, 198)], alpha=40)
    s.seam([(103, 122), (34, 200)])
    s.seam([(101, 170), (50, 206)])
    s.seam([(98, 134), (52, 194)], width=1)                                  # 翼面板划分
    s.seam([(70, 210), (96, 180)], width=1)
    s.rim([(12, 206), (36, 216)])                                            # 翼尖后缘高光
    s.panel_dot(100, 142)                                                    # 翼根铆钉列
    s.panel_dot(96, 158)
    s.panel_dot(60, 198)
    s.vent(54, 204, length=14, gap=3, n=3)                                   # 翼根散热格栅
    s.greeble(78, 186, 7, 6)                                                 # 翼面舱口
    s.neon([(103, 120), (14, 206)])                                          # 前缘能量走线
    s.neon([(38, 212), (96, 180)], width=1)                                  # 后缘二级走线
    s.lamp(104, 186, 1.5)                                                    # 引擎舱前节点灯

    # ---- 鸭翼（前部小翼） ----
    s.facet([(108, 86), (84, 96), (92, 106), (108, 100)], HULL_C)
    s.facet([(106, 90), (92, 97), (97, 102), (106, 98)], HULL_D)             # 鸭翼子面
    s.rim([(108, 86), (84, 96)])
    s.panel_dot(104, 94, r=1.2)

    # ---- 尾翼（小垂尾外八，根部接翼根） ----
    s.facet([(102, 168), (78, 228), (92, 232), (106, 206)], HULL_B)
    s.facet([(100, 176), (86, 222), (92, 224), (102, 204)], HULL_C)          # 垂尾子面
    s.seam([(100, 178), (86, 222)], width=1)
    s.panel_dot(98, 196, r=1.2)

    # ---- 主机身（细长梭形，多面装甲） ----
    s.facet([(cx, 16), (143, 96), (137, 196), (cx, 236), (117, 196), (111, 96)], HULL_A, False)
    s.facet([(cx, 16), (143, 96), (cx, 118), (111, 96)], HULL_D, False)      # 机头亮面
    s.facet([(cx, 28), (134, 72), (cx, 100)], HULL_C, False)                 # 机头子面
    s.facet([(111, 96), (cx, 118), (cx, 236), (117, 196)], HULL_B, False)    # 左腹板
    s.facet([(143, 96), (cx, 118), (cx, 236), (137, 196)], HULL_C, False)    # 右腹板
    s.shade([(118, 198), (cx, 234), (cx, 236)], alpha=40, mirror=False)
    s.seam([(cx, 118), (cx, 236)], mirror=False)                             # 中脊接缝
    s.seam([(111, 96), (cx, 118)])
    s.seam([(115, 150), (139, 150)], width=1, mirror=False)                  # 机身环带
    s.seam([(116, 176), (138, 176)], width=1, mirror=False)
    s.rim([(cx, 16), (143, 96)], mirror=False)                               # 机头描边
    s.rim([(cx, 16), (111, 96)], mirror=False)
    s.greeble(131, 146, 5, 7)                                                # 腹板舱口
    s.panel_dot(119, 160, r=1.2)
    s.panel_dot(119, 186, r=1.2)
    s.panel_dot(122, 210, r=1.2)

    # ---- 引擎舱（尾部双发整流罩） ----
    s.facet([(96, 188), (110, 182), (112, 228), (98, 234)], HULL_C)
    s.facet([(98, 192), (102, 190), (103, 228), (99, 230)], HULL_D)
    s.seam([(97, 200), (111, 196)], width=1)                                 # 整流罩节缝
    s.seam([(98, 214), (112, 211)], width=1)
    s.panel_dot(106, 192, r=1.2)
    s.nozzle_ring(108, 230, 6, 4)
    s.nozzle_ring(146, 230, 6, 4)
    s.engine(108, 230, 6, 4)
    s.engine(146, 230, 6, 4)
    s.engine_particles(108, 237)
    s.engine_particles(146, 237)

    # ---- 能量细节 ----
    s.canopy_frame(cx, 92, 9, 18)                                            # 座舱钢甲框缘
    s.canopy(cx, 92, 9, 18)                                                  # 座舱
    s.neon([(117, 130), (120, 190)], width=2)                                # 侧舷能量缝
    s.neon([(137, 130), (134, 190)], width=2)
    s.neon([(cx, 124), (cx, 196)], width=1, mirror=False)                    # 脊线二级走线
    s.neon([(92, 104), (86, 98)], width=2)                                   # 鸭翼尖灯
    s.neon([(13, 205), (22, 201)], width=3)                                  # 主翼尖灯
    s.neon([(80, 226), (90, 230)], width=2)                                  # 垂尾尖灯
    s.lamp(cx, 24, 1.5, mirror=False)                                        # 机头信标
    return s


def main() -> None:
    # R07（2026-08-05 独立审计）：输出路径锚定脚本位置（同 generate_audio 口径），
    # 不再依赖调用时 cwd——非仓库根运行不会在别处落盘或崩溃
    out = os.path.normpath(
        os.path.join(os.path.dirname(__file__), "..", "..", "assets", "sprites", "player_ship.png")
    )
    player_ship().finish(out)


if __name__ == "__main__":
    main()
