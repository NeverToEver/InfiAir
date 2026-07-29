#!/usr/bin/env python3
"""离线母舰贴图生成器（钛灰钢甲 + 青色能量，非游戏运行时依赖）。

重绘 assets/sprites/mothership.png（画布 506×261 与原贴图一致，舰首朝上）。
母舰是友方补给/火力平台，沿用玩家侧的钛灰钢甲分层 + 铆接接缝 + 青色能量体系，
与敌方晶体棱镜风格区分。剪影保持「宽翼平台」：两侧梯形翼台 + 中央高舰体 +
顶部舰桥塔楼 + 中部货舱条 + 下方机库灯带 + 舰尾引擎组。

用法：python3 scripts/tools/generate_mothership_sprite.py
"""

from PIL import Image, ImageDraw, ImageFilter

S = 4  # 超采样抗锯齿

# 钛灰钢甲分层（冷调蓝灰，由暗到亮，与玩家生成器一致）
HULL_A = (26, 32, 44, 255)
HULL_B = (38, 48, 64, 255)
HULL_C = (54, 68, 88, 255)
HULL_D = (76, 94, 118, 255)
SEAM = (10, 14, 22, 255)
RIM = (168, 196, 226, 255)
RIVET = (122, 142, 170, 255)
DARK_BAY = (8, 10, 16, 255)   # 机库开口

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
        c = (color or ACCENT) + (255,)
        self.gd.ellipse(self.p([(x - r, y - r), (x + r, y + r)]), fill=c)
        if mirror:
            self.gd.ellipse(self.p([(self.w - x - r, y - r), (self.w - x + r, y + r)]), fill=c)

    def panel_dot(self, x, y, r=1.5, mirror=True):
        self.bd.ellipse(self.p([(x - r, y - r), (x + r, y + r)]), fill=RIVET)
        if mirror:
            self.bd.ellipse(self.p([(self.w - x - r, y - r), (self.w - x + r, y + r)]), fill=RIVET)

    def vent(self, x, y, length=8, gap=3, n=3, width=1, mirror=True):
        for i in range(n):
            yy = y + i * gap
            self.bd.line(self.p([(x, yy), (x + length, yy)]), fill=SEAM, width=width * S)
            if mirror:
                self.bd.line(self.p([(self.w - x, yy), (self.w - x - length, yy)]), fill=SEAM, width=width * S)

    def greeble(self, x, y, w, h, fill=None, mirror=True, outline=True):
        c = fill or HULL_B
        kw = {"fill": c}
        if outline:
            kw["outline"] = SEAM
            kw["width"] = S
        self.bd.rectangle(self.p([(x, y), (x + w, y + h)]), **kw)
        if mirror:
            self.bd.rectangle(self.p([(self.w - x - w, y), (self.w - x, y + h)]), **kw)

    def nozzle_ring(self, cx, cy, rx, ry):
        self.bd.ellipse(self.p([(cx - rx - 2.5, cy - ry - 2.5), (cx + rx + 2.5, cy + ry + 2.5)]), fill=SEAM)
        self.bd.ellipse(self.p([(cx - rx - 1, cy - ry - 1), (cx + rx + 1, cy + ry + 1)]), fill=HULL_C)

    def engine(self, cx, cy, rx, ry):
        self.gd.ellipse(self.p([(cx - rx, cy - ry), (cx + rx, cy + ry)]), fill=ACCENT + (230,))
        w = max(rx * 0.45, 2.0)
        self.gd.ellipse(self.p([(cx - w, cy - ry * 0.5), (cx + w, cy + ry * 0.5)]), fill=(255, 255, 255, 240))

    def engine_particles(self, cx, cy, n=3, drop=8, spread=4):
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


def mothership() -> Ship:
    s = Ship(506, 261)
    cx = 253

    # ---- 侧翼平台（梯形宽翼，分层甲板） ----
    s.facet([(228, 92), (14, 134), (14, 196), (228, 208)], HULL_B)           # 翼台基面
    s.facet([(220, 102), (36, 140), (36, 160), (220, 138)], HULL_C)          # 前段子面
    s.facet([(214, 150), (90, 170), (90, 188), (214, 196)], HULL_A)          # 后段暗面
    s.shade([(40, 176), (40, 192), (200, 202)], alpha=40)
    s.seam([(140, 128), (140, 200)], width=1)                                # 甲板板划分
    s.seam([(70, 140), (70, 198)], width=1)
    s.seam([(36, 160), (220, 138)], width=1)
    s.panel_dot(140, 140)
    s.panel_dot(140, 160)
    s.panel_dot(140, 180)
    s.panel_dot(96, 146)
    s.greeble(100, 150, 12, 8)                                               # 甲板舱口
    s.greeble(160, 168, 10, 8)
    s.vent(178, 192, length=16, gap=3, n=3)                                  # 翼根散热格栅
    # 翼台尾喷吊舱
    s.facet([(70, 196), (96, 198), (96, 220), (70, 218)], HULL_C)
    s.facet([(74, 200), (80, 200), (80, 216), (74, 216)], HULL_D)
    s.facet([(146, 200), (168, 202), (168, 224), (146, 222)], HULL_C)
    s.facet([(150, 204), (156, 204), (156, 220), (150, 220)], HULL_D)

    # ---- 翼根连接块（深色设备舱） ----
    s.facet([(204, 110), (226, 104), (226, 196), (204, 188)], HULL_A)
    s.seam([(208, 128), (222, 126)], width=1)
    s.seam([(208, 142), (222, 141)], width=1)
    s.seam([(208, 156), (222, 156)], width=1)
    s.vent(210, 168, length=10, gap=3, n=2)

    # ---- 中央舰体（高耸六棱柱，舰首朝上） ----
    s.facet([(cx, 22), (306, 68), (306, 196), (cx, 242), (200, 196), (200, 68)], HULL_A, False)
    s.facet([(cx, 42), (290, 76), (290, 190), (cx, 226), (216, 190), (216, 76)], HULL_B, False)
    s.facet([(cx, 22), (306, 68), (cx, 102), (200, 68)], HULL_C, False)      # 舰首晶面
    s.facet([(cx, 42), (290, 76), (cx, 108), (216, 76)], HULL_D, False)
    s.shade([(262, 200), (cx, 240), (cx, 242), (290, 194)], alpha=40, mirror=False)
    s.seam([(cx, 102), (cx, 242)], mirror=False)                             # 中脊接缝
    s.seam([(200, 68), (cx, 102)])
    s.seam([(206, 164), (300, 164)], width=1, mirror=False)                  # 舰体环带
    s.seam([(210, 198), (296, 198)], width=1, mirror=False)
    s.rim([(cx, 22), (306, 68)], mirror=False)
    s.greeble(210, 140, 8, 10)                                               # 舷侧舱口
    s.greeble(288, 140, 8, 10)
    s.panel_dot(208, 120)
    s.panel_dot(298, 120)
    s.panel_dot(212, 210)
    s.panel_dot(294, 210)

    # ---- 舰桥塔楼（顶部收分六边） ----
    s.facet([(cx, 36), (274, 50), (274, 82), (cx, 94), (232, 82), (232, 50)], HULL_C, False)
    s.facet([(cx, 46), (266, 56), (266, 74), (cx, 84), (240, 74), (240, 56)], HULL_D, False)
    s.facet([(cx, 30), (262, 44), (cx, 54), (244, 44)], HULL_B, False)       # 塔尖
    s.seam([(cx, 84), (cx, 94)], width=1, mirror=False)
    s.rim([(cx, 36), (274, 50)], mirror=False)
    s.panel_dot(240, 58, r=1.2)
    s.panel_dot(266, 58, r=1.2)

    # ---- 货舱（中部条纹集装箱面） ----
    s.greeble(222, 116, 62, 42, fill=HULL_A, mirror=False)                   # 货舱底板
    for i in range(6):
        bx = 226 + i * 9
        s.greeble(bx, 120, 6, 34, fill=HULL_D if i % 2 == 0 else HULL_C, mirror=False)

    # ---- 机库（下方开口 + 着舰灯带） ----
    s.greeble(228, 170, 50, 30, fill=DARK_BAY, mirror=False)
    for i in range(4):
        s.lamp(238 + i * 10, 178, 1.5, mirror=False)
    s.lamp(cx, 192, 2, mirror=False)
    s.neon([(232, 196), (274, 196)], width=1, mirror=False)                  # 机库门槛灯带

    # ---- 能量细节 ----
    s.neon([(226, 94), (16, 136)])                                           # 翼前缘能量走线
    s.neon([(18, 194), (226, 206)], width=1)                                 # 翼后缘二级走线
    s.neon([(206, 76), (206, 188)], width=1, mirror=False)                   # 舷侧二级走线
    s.neon([(300, 76), (300, 188)], width=1, mirror=False)
    s.neon([(240, 64), (266, 64)], width=2, mirror=False)                    # 舰桥观察缝
    s.lamp(16, 140, 3)                                                       # 翼尖灯
    s.lamp(16, 192, 2)
    s.lamp(cx, 34, 2, mirror=False)                                          # 塔尖灯
    s.lamp(204, 70, 2)                                                       # 舰肩节点灯
    s.lamp(302, 70, 2)

    # ---- 引擎组（翼台双吊舱 + 舰尾主喷） ----
    s.nozzle_ring(83, 214, 8, 5)
    s.engine(83, 214, 8, 5)
    s.nozzle_ring(423, 214, 8, 5)
    s.engine(423, 214, 8, 5)
    s.nozzle_ring(157, 220, 7, 4)
    s.engine(157, 220, 7, 4)
    s.nozzle_ring(349, 220, 7, 4)
    s.engine(349, 220, 7, 4)
    s.nozzle_ring(cx, 234, 11, 6)
    s.engine(cx, 234, 11, 6)
    s.engine_particles(cx, 244, n=3, drop=9, spread=6)
    return s


def main() -> None:
    mothership().finish("assets/sprites/mothership.png")


if __name__ == "__main__":
    main()
