#!/usr/bin/env python3
"""离线敌方单位贴图生成器（晶体棱镜风格，非游戏运行时依赖）。

重绘 4 普通机 + 3 精英 + 3 Boss，直接覆盖 assets/sprites/ 同名 PNG
（画布尺寸与原贴图一致：190/245/410，机头朝上，场景根节点 rotation=PI 翻转）。

精细化层次（在既有设计骨架上叠加，剪影与炮塔基座锚点不变）：
- 装甲板细分：子面（sub-facet）+ 接缝线 + 铆接点（panel_dot）+ 散热格栅（vent）+ 舱口（greeble）
- 晶体风：晶簇凸起（crystal）+ 同心环/内切六边/白芯核心（ring_core）+ 棱线高光
- 霓虹加密：二级能量走线（细 neon 沿 seam）+ 翼尖/尾部航行灯（lamp）
- 引擎区：喷管装甲环（nozzle_ring）+ 内焰白芯 + 微粒子点（engine_particles）
用法：python3 scripts/tools/generate_enemy_sprites.py
"""

import math
import os

from PIL import Image, ImageDraw, ImageFilter

S = 4  # 超采样抗锯齿

# 调色板双档（P0-5 敌机对比度）：DARK 为原暗紫档，Boss/航母/炮台沿用，保持既有贴图不变；
# BRIGHT 为普通机/精英提亮档——舰体亮度升至 70–100 段，色相由蓝紫（≈256°）移向紫红/品红（≈295°），
# 与清屏背景 RGB(5,5,15) 拉开明度与色相距离；霓虹能量缝 +2 设计 px（1–3 → 3–5），棱线 2 → 3 px。
PALETTE_DARK = {
    "hull": ((22, 18, 34, 255), (34, 28, 52, 255), (48, 40, 72, 255), (62, 52, 92, 255)),
    "seam": (10, 8, 18, 255),
    "rim": (150, 140, 185, 255),
    "rivet": (96, 86, 140, 255),
    "neon_boost": 0,
    "rim_w": 2,
}
PALETTE_BRIGHT = {
    "hull": ((96, 50, 100, 255), (112, 60, 118, 255), (130, 72, 136, 255), (150, 86, 156, 255)),
    "seam": (20, 12, 30, 255),
    "rim": (200, 156, 224, 255),
    "rivet": (140, 110, 168, 255),
    "neon_boost": 2,
    "rim_w": 3,
}

# 当前生效调色板（由 _apply_palette 切换；默认 DARK，与旧行为一致）
HULL_A, HULL_B, HULL_C, HULL_D = PALETTE_DARK["hull"]
SEAM = PALETTE_DARK["seam"]
RIM = PALETTE_DARK["rim"]
RIVET = PALETTE_DARK["rivet"]   # 铆接点（亮于 SEAM 暗于 RIM）
NEON_BOOST = PALETTE_DARK["neon_boost"]  # 霓虹能量缝加粗量（设计 px）
RIM_W = PALETTE_DARK["rim_w"]            # 棱线宽（设计 px）


def _apply_palette(p) -> None:
    global HULL_A, HULL_B, HULL_C, HULL_D, SEAM, RIM, RIVET, NEON_BOOST, RIM_W
    HULL_A, HULL_B, HULL_C, HULL_D = p["hull"]
    SEAM = p["seam"]
    RIM = p["rim"]
    RIVET = p["rivet"]
    NEON_BOOST = p["neon_boost"]
    RIM_W = p["rim_w"]

ENEMY_ACCENT = (255, 72, 56)    # 普通机：猩红
ENEMY_CORE = (255, 150, 70)
ELITE_ACCENT = (255, 64, 190)   # 精英：品红
ELITE_CORE = (215, 135, 255)
BOSS_ACCENTS = [                # Boss：琥珀 / 紫罗兰 / 红宝石
    (255, 170, 50),
    (170, 90, 255),
    (255, 60, 90),
]
BOSS_CORES = [
    (255, 215, 100),
    (225, 175, 255),
    (255, 170, 170),
]


class Ship:
    """分层绘制：body（实体面）+ glow（霓虹线/能量，模糊光晕 + 清晰本体）。"""

    def __init__(self, w: int, h: int, accent: tuple, core: tuple) -> None:
        self.w, self.h = w, h
        self.accent, self.core = accent, core
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

    def shade(self, pts, alpha=50, dark=True, mirror=True):
        """半透明明暗叠加面，用于轻微渐变感。"""
        c = (4, 2, 12, alpha) if dark else (235, 225, 255, alpha)
        self.bd.polygon(self.p(pts), fill=c)
        if mirror:
            self.bd.polygon(self.p(self.mirror(pts)), fill=c)

    def seam(self, pts, width=2, mirror=True):
        self.bd.line(self.p(pts), fill=SEAM, width=width * S, joint="curve")
        if mirror:
            self.bd.line(self.p(self.mirror(pts)), fill=SEAM, width=width * S, joint="curve")

    def rim(self, pts, width=None, mirror=True):
        w = RIM_W if width is None else width
        self.bd.line(self.p(pts), fill=RIM, width=w * S, joint="curve")
        if mirror:
            self.bd.line(self.p(self.mirror(pts)), fill=RIM, width=w * S, joint="curve")

    def neon(self, pts, width=3, color=None, mirror=True):
        c = (color or self.accent) + (255,)
        w = (width + NEON_BOOST) * S
        self.gd.line(self.p(pts), fill=c, width=w, joint="curve")
        if mirror:
            self.gd.line(self.p(self.mirror(pts)), fill=c, width=w, joint="curve")

    def lamp(self, x, y, r=2.0, color=None, mirror=True):
        """节点灯/航行灯（glow 层小光点）。"""
        c = (color or self.accent) + (255,)
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

    def crystal(self, cx, cy, r, color=None, mirror=True):
        """晶簇凸起：菱形面 + 顶部高光小面（body 层）。"""
        c = color or HULL_D
        pts = [(cx, cy - r), (cx + r * 0.62, cy), (cx, cy + r), (cx - r * 0.62, cy)]
        hi = [(cx, cy - r), (cx + r * 0.3, cy - r * 0.2), (cx, cy + r * 0.1)]
        self.bd.polygon(self.p(pts), fill=c)
        self.bd.polygon(self.p(hi), fill=RIM)
        if mirror:
            self.bd.polygon(self.p(self.mirror(pts)), fill=c)
            self.bd.polygon(self.p(self.mirror(hi)), fill=RIM)

    def energy_core(self, cx, cy, r, color=None):
        c = (color or self.core) + (255,)
        self.gd.ellipse(self.p([(cx - r, cy - r), (cx + r, cy + r)]), fill=c)
        w = max(r * 0.45, 2.0)
        self.gd.ellipse(self.p([(cx - w, cy - w), (cx + w, cy + w)]), fill=(255, 255, 255, 240))

    def ring_core(self, cx, cy, r, color=None):
        """晶体能量核：外环 + 内切六边晶体面 + 白芯（glow 层）。"""
        c = (color or self.core) + (255,)
        # 外环（描边圆）
        self.gd.ellipse(self.p([(cx - r, cy - r), (cx + r, cy + r)]), outline=c, width=max(int(1.5 * S), S))
        # 内切六边晶体
        hex_pts = [
            (cx + r * 0.78 * math.cos(math.pi / 6 + i * math.pi / 3),
             cy + r * 0.78 * math.sin(math.pi / 6 + i * math.pi / 3))
            for i in range(6)
        ]
        self.gd.polygon(self.p(hex_pts), fill=c)
        # 白芯
        w = max(r * 0.34, 1.8)
        self.gd.ellipse(self.p([(cx - w, cy - w), (cx + w, cy + w)]), fill=(255, 255, 255, 245))

    def nozzle_ring(self, cx, cy, rx, ry):
        """喷管装甲环：SEAM 外圈 + 钢面内圈（body 层，先于 engine 调用）。"""
        self.bd.ellipse(self.p([(cx - rx - 2.5, cy - ry - 2.5), (cx + rx + 2.5, cy + ry + 2.5)]), fill=SEAM)
        self.bd.ellipse(self.p([(cx - rx - 1, cy - ry - 1), (cx + rx + 1, cy + ry + 1)]), fill=HULL_C)

    def engine(self, cx, cy, rx, ry, color=None):
        c = (color or self.accent) + (230,)
        self.gd.ellipse(self.p([(cx - rx, cy - ry), (cx + rx, cy + ry)]), fill=c)
        # 内焰白芯
        w = max(rx * 0.42, 1.8)
        self.gd.ellipse(self.p([(cx - w, cy - ry * 0.5), (cx + w, cy + ry * 0.5)]), fill=(255, 255, 255, 235))

    def engine_particles(self, cx, cy, n=3, drop=9, spread=5, color=None):
        """喷口微粒子点（glow 层，确定性排布）。"""
        c = (color or self.accent) + (200,)
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


# ---------------- 普通机（190×190，猩红） ----------------

def enemy_1() -> Ship:  # 飞镖：菱形机身 + 后掠刀翼
    s = Ship(190, 190, ENEMY_ACCENT, ENEMY_CORE)
    s.facet([(88, 78), (28, 142), (68, 150), (91, 118)], HULL_B)       # 外翼
    s.facet([(86, 84), (58, 112), (76, 120), (90, 98)], HULL_C)        # 外翼子面（提亮板块）
    s.facet([(90, 82), (52, 132), (72, 138), (91, 112)], HULL_C)       # 内翼面
    s.facet([(91, 88), (62, 128), (74, 133), (91, 110)], HULL_D)       # 内翼子面
    s.shade([(60, 122), (68, 148), (90, 120)], alpha=45)               # 翼根阴影
    s.facet([(95, 22), (111, 92), (95, 168), (79, 92)], HULL_A, False)  # 菱形机身
    s.facet([(95, 22), (111, 92), (95, 110)], HULL_C, False)            # 机头亮面
    s.facet([(95, 34), (103, 62), (95, 84)], HULL_D, False)             # 机头子面
    s.seam([(95, 22), (95, 168)], mirror=False)
    s.seam([(84, 92), (48, 134)])                                       # 翼面板划分
    s.seam([(74, 146), (88, 122)], width=1)
    s.rim([(95, 22), (111, 92)], mirror=False)
    s.rim([(88, 78), (28, 142)])                                            # 外翼前缘棱线
    s.rim([(111, 92), (95, 168)], mirror=False)                             # 机身侧缘棱线
    s.panel_dot(87, 104)
    s.panel_dot(85, 128)
    s.vent(77, 138, length=9, gap=3, n=3)                               # 翼根散热格栅
    s.crystal(58, 118, 4)                                               # 翼面晶簇
    s.neon([(90, 84), (30, 142)])                                       # 翼前缘霓虹
    s.neon([(95, 30), (95, 86)], width=1, mirror=False)                 # 背部二级走线
    s.lamp(30, 142, 2.5)                                                # 翼尖灯
    s.lamp(70, 148, 1.5)                                                # 后缘航行灯
    s.ring_core(95, 96, 9)
    s.nozzle_ring(95, 158, 7, 4)
    s.engine(95, 158, 7, 4)
    s.engine_particles(95, 164)
    return s


def enemy_2() -> Ship:  # 蝠鲼：新月宽翼
    s = Ship(190, 190, ENEMY_ACCENT, ENEMY_CORE)
    s.facet([(95, 42), (18, 118), (52, 132), (95, 104)], HULL_B)
    s.facet([(88, 54), (34, 112), (54, 122), (92, 96)], HULL_C)        # 翼中段子面
    s.facet([(95, 58), (44, 112), (62, 120), (95, 98)], HULL_C)
    s.facet([(80, 66), (52, 104), (64, 110), (86, 92)], HULL_D)        # 内翼子面
    s.shade([(60, 118), (52, 130), (92, 104)], alpha=45)
    s.facet([(95, 32), (107, 108), (95, 162), (83, 108)], HULL_A, False)
    s.facet([(95, 32), (107, 108), (95, 96)], HULL_D, False)
    s.facet([(95, 44), (101, 72), (95, 92)], HULL_C, False)            # 机头子面
    s.seam([(95, 60), (52, 122)])
    s.seam([(86, 62), (40, 112)], width=1)                             # 翼面板划分
    s.rim([(95, 32), (107, 108)], mirror=False)
    s.rim([(95, 42), (18, 118)])                                            # 翼前缘棱线
    s.rim([(107, 108), (95, 162)], mirror=False)                            # 机身侧缘棱线
    s.panel_dot(90, 74)
    s.panel_dot(88, 96)
    s.vent(64, 118, length=10, gap=3, n=3)
    s.crystal(46, 114, 4)
    s.neon([(93, 46), (22, 118)])
    s.neon([(54, 128), (92, 104)], width=1)                            # 翼下缘二级走线
    s.neon([(95, 38), (95, 84)], width=1, mirror=False)
    s.lamp(20, 118, 2.5)
    s.lamp(54, 130, 1.5)
    s.ring_core(95, 92, 9)
    s.nozzle_ring(95, 152, 7, 4)
    s.engine(95, 152, 7, 4)
    s.engine_particles(95, 158)
    return s


def enemy_3() -> Ship:  # 重锤：前掠翼厚机身
    s = Ship(190, 190, ENEMY_ACCENT, ENEMY_CORE)
    s.facet([(82, 88), (38, 48), (56, 112), (85, 118)], HULL_B)        # 前掠翼
    s.facet([(80, 92), (52, 66), (62, 106), (84, 112)], HULL_D)
    s.facet([(78, 94), (56, 72), (64, 100), (82, 106)], HULL_C)        # 翼子面
    s.shade([(58, 104), (84, 114), (84, 118)], alpha=40)
    s.facet([(95, 28), (116, 88), (109, 162), (81, 162), (74, 88)], HULL_A, False)
    s.facet([(95, 28), (116, 88), (95, 104), (74, 88)], HULL_C, False)
    s.facet([(95, 40), (104, 66), (95, 88)], HULL_D, False)            # 机头子面
    s.seam([(95, 104), (95, 162)], mirror=False)
    s.seam([(82, 120), (108, 120)], mirror=False)                      # 机身环带接缝
    s.seam([(83, 140), (107, 140)], mirror=False)
    s.seam([(76, 96), (58, 76)], width=1)                              # 翼面板划分
    s.rim([(95, 28), (116, 88)], mirror=False)
    s.rim([(82, 88), (38, 48)])                                             # 前掠翼前缘棱线
    s.rim([(116, 88), (109, 162)], mirror=False)                            # 机身侧缘棱线
    s.greeble(99, 124, 6, 5)                                           # 舱口
    s.panel_dot(88, 112)
    s.panel_dot(87, 150)
    s.vent(99, 108, length=8, gap=3, n=3)
    s.crystal(44, 58, 4)                                               # 翼尖晶簇
    s.neon([(81, 90), (40, 50)])
    s.neon([(58, 108), (83, 115)], width=1)                            # 翼后缘二级走线
    s.neon([(95, 34), (95, 96)], width=1, mirror=False)
    s.lamp(40, 50, 2.5)
    s.lamp(84, 158, 1.5)
    s.ring_core(95, 104, 11)
    s.nozzle_ring(95, 156, 9, 4)
    s.engine(95, 156, 9, 4)
    s.engine_particles(95, 162)
    return s


def enemy_4() -> Ship:  # 针刺：双叉机头
    s = Ship(190, 190, ENEMY_ACCENT, ENEMY_CORE)
    s.facet([(86, 96), (46, 134), (79, 126)], HULL_B)                   # 小翼
    s.facet([(84, 100), (56, 128), (76, 122)], HULL_C)                  # 小翼子面
    s.shade([(60, 126), (78, 124), (84, 104)], alpha=40)
    s.facet([(80, 32), (90, 26), (93, 100), (83, 106)], HULL_C)         # 左叉
    s.facet([(83, 40), (88, 36), (90, 92), (85, 96)], HULL_D)           # 叉体亮面
    s.facet([(95, 58), (106, 128), (95, 166), (84, 128)], HULL_A, False)
    s.facet([(95, 58), (106, 128), (95, 118)], HULL_D, False)
    s.seam([(93, 100), (95, 166)])
    s.seam([(80, 104), (58, 124)], width=1)                             # 翼面板划分
    s.seam([(84, 44), (89, 92)], width=1)                               # 叉体接缝
    s.rim([(80, 32), (90, 26)])
    s.rim([(86, 96), (46, 134)])                                            # 小翼前缘棱线
    s.rim([(106, 128), (95, 166)], mirror=False)                            # 机身侧缘棱线
    s.panel_dot(90, 132)
    s.panel_dot(90, 148)
    s.crystal(64, 118, 3)
    s.neon([(86, 98), (47, 134)])
    s.neon([(85, 36), (90, 92)], width=1)                               # 叉体二级走线
    s.lamp(85, 30, 2)                                                   # 叉尖灯
    s.lamp(48, 132, 1.5)
    s.ring_core(95, 108, 9)
    s.nozzle_ring(95, 156, 7, 4)
    s.engine(95, 156, 7, 4)
    s.engine_particles(95, 162)
    return s


# ---------------- 精英（245×245，品红） ----------------

def elite_1() -> Ship:  # 枪骑：长机身 + 侧刃 + 鸭翼
    s = Ship(245, 245, ELITE_ACCENT, ELITE_CORE)
    s.facet([(110, 88), (42, 158), (72, 192), (113, 150)], HULL_B)      # 侧刃
    s.facet([(108, 96), (58, 152), (76, 172), (111, 142)], HULL_C)      # 侧刃子面
    s.facet([(111, 100), (66, 152), (82, 168), (113, 140)], HULL_C)
    s.facet([(110, 108), (76, 148), (86, 160), (112, 134)], HULL_D)     # 内刃子面
    s.shade([(70, 168), (72, 190), (110, 152)], alpha=45)
    s.facet([(112, 58), (76, 92), (110, 96)], HULL_C)                   # 鸭翼
    s.facet([(110, 64), (88, 88), (109, 92)], HULL_D)                   # 鸭翼子面
    s.facet([(122, 24), (139, 118), (122, 218), (105, 118)], HULL_A, False)
    s.facet([(122, 24), (139, 118), (122, 132)], HULL_D, False)
    s.facet([(122, 36), (131, 70), (122, 100)], HULL_C, False)          # 机头子面
    s.seam([(122, 24), (122, 218)], mirror=False)
    s.seam([(109, 152), (112, 196)], mirror=False)                      # 机身环带
    s.seam([(135, 152), (132, 196)], mirror=False)
    s.seam([(104, 104), (62, 154)], width=1)                            # 刃面板划分
    s.seam([(86, 176), (108, 148)], width=1)
    s.rim([(122, 24), (139, 118)], mirror=False)
    s.rim([(110, 88), (42, 158)])                                           # 侧刃前缘棱线
    s.rim([(139, 118), (122, 218)], mirror=False)                           # 机身侧缘棱线
    s.panel_dot(110, 144)
    s.panel_dot(111, 170)
    s.panel_dot(92, 178)
    s.vent(99, 186, length=10, gap=3, n=3)                              # 尾段散热格栅
    s.greeble(126, 156, 6, 8)                                           # 机身舱口
    s.crystal(70, 150, 5)                                               # 刃面晶簇
    s.crystal(92, 86, 3)
    s.neon([(111, 92), (44, 158)])
    s.neon([(111, 60), (78, 92)], width=2)
    s.neon([(122, 40), (122, 108)], width=1, mirror=False)              # 背部二级走线
    s.neon([(74, 190), (110, 152)], width=1)                            # 刃后缘二级走线
    s.lamp(44, 158, 3)                                                  # 刃尖灯
    s.lamp(78, 92, 2)                                                   # 鸭翼尖灯
    s.lamp(112, 210, 1.5)                                               # 尾航行灯
    s.ring_core(122, 120, 12)
    s.nozzle_ring(122, 206, 8, 5)
    s.engine(122, 206, 8, 5)
    s.engine_particles(122, 214)
    return s


def elite_2() -> Ship:  # 卫盾：宽盾翼 + 装甲板
    s = Ship(245, 245, ELITE_ACCENT, ELITE_CORE)
    s.facet([(122, 48), (28, 108), (44, 152), (122, 126)], HULL_B)      # 盾翼
    s.facet([(114, 58), (44, 106), (54, 132), (116, 116)], HULL_C)      # 盾翼子面A
    s.facet([(70, 100), (44, 108), (52, 136), (76, 128)], HULL_D)       # 盾翼子面B（外段提亮）
    s.shade([(50, 140), (46, 150), (118, 126)], alpha=45)
    s.facet([(118, 66), (52, 108), (62, 136), (118, 118)], HULL_C)      # 装甲板
    s.facet([(96, 84), (66, 106), (74, 126), (98, 112)], HULL_D)
    s.facet([(122, 38), (135, 138), (122, 202), (109, 138)], HULL_A, False)
    s.facet([(122, 38), (135, 138), (122, 120)], HULL_D, False)
    s.facet([(122, 50), (129, 84), (122, 110)], HULL_C, False)          # 机头子面
    s.seam([(118, 68), (60, 112)])
    s.seam([(122, 126), (44, 152)])
    s.seam([(112, 62), (48, 108)], width=1)                             # 盾翼板划分
    s.seam([(84, 122), (116, 118)], width=1)
    s.rim([(122, 38), (135, 138)], mirror=False)
    s.rim([(122, 48), (28, 108)])                                           # 盾翼前缘棱线
    s.rim([(135, 138), (122, 202)], mirror=False)                           # 机身侧缘棱线
    s.panel_dot(116, 100)
    s.panel_dot(100, 120)
    s.panel_dot(64, 118)
    s.vent(104, 130, length=10, gap=3, n=3)                             # 翼根散热格栅
    s.greeble(80, 96, 7, 6)                                             # 盾面舱口
    s.crystal(56, 112, 5)                                               # 盾面晶簇
    s.crystal(88, 100, 3)
    s.neon([(120, 52), (30, 108)])
    s.neon([(46, 148), (118, 124)], width=1)                            # 盾翼后缘二级走线
    s.neon([(122, 46), (122, 106)], width=1, mirror=False)              # 背部二级走线
    s.lamp(30, 108, 3)                                                  # 翼尖灯
    s.lamp(46, 150, 1.5)
    s.lamp(112, 196, 1.5)
    s.ring_core(122, 116, 12)
    s.nozzle_ring(122, 192, 8, 5)
    s.engine(122, 192, 8, 5)
    s.engine_particles(122, 200)
    return s


def elite_3() -> Ship:  # 掠舰：爪形翼
    s = Ship(245, 245, ELITE_ACCENT, ELITE_CORE)
    s.facet([(113, 78), (36, 58), (58, 138), (40, 172), (101, 150)], HULL_B)  # 爪翼
    s.facet([(108, 84), (50, 68), (62, 120), (96, 136)], HULL_C)        # 爪翼上段子面
    s.facet([(60, 130), (48, 162), (92, 146), (96, 138)], HULL_C)       # 爪翼下段子面
    s.facet([(110, 90), (56, 76), (66, 130), (100, 138)], HULL_C)
    s.facet([(104, 96), (66, 84), (72, 122), (98, 130)], HULL_D)        # 内翼子面
    s.shade([(56, 142), (46, 168), (96, 148)], alpha=45)
    s.facet([(122, 34), (137, 128), (122, 208), (107, 128)], HULL_A, False)
    s.facet([(122, 34), (137, 128), (122, 124)], HULL_D, False)
    s.facet([(122, 46), (130, 82), (122, 112)], HULL_C, False)          # 机头子面
    s.seam([(110, 92), (58, 138)])
    s.seam([(104, 86), (52, 72)], width=1)                              # 爪翼板划分
    s.seam([(62, 134), (50, 164)], width=1)
    s.seam([(122, 140), (122, 196)], mirror=False)                      # 机身中脊
    s.rim([(122, 34), (137, 128)], mirror=False)
    s.rim([(113, 78), (36, 58)])                                            # 爪翼前缘棱线
    s.rim([(137, 128), (122, 208)], mirror=False)                           # 机身侧缘棱线
    s.panel_dot(104, 116)
    s.panel_dot(76, 128)
    s.panel_dot(111, 160)
    s.vent(100, 166, length=10, gap=3, n=3)
    s.greeble(70, 104, 6, 7)
    s.crystal(48, 66, 4)                                                # 爪尖晶簇
    s.crystal(50, 158, 4)
    s.crystal(84, 92, 3)
    s.neon([(112, 80), (38, 60)])
    s.neon([(58, 138), (42, 170)], width=2)
    s.neon([(122, 44), (122, 108)], width=1, mirror=False)              # 背部二级走线
    s.lamp(38, 60, 3)                                                   # 爪尖灯
    s.lamp(42, 170, 2.5)
    s.lamp(112, 202, 1.5)
    s.ring_core(122, 116, 12)
    s.nozzle_ring(122, 198, 8, 5)
    s.engine(122, 198, 8, 5)
    s.engine_particles(122, 206)
    return s


# ---------------- Boss（410×410） ----------------

def boss_1() -> Ship:  # 君王：重型宽翼母舰
    s = Ship(410, 410, BOSS_ACCENTS[0], BOSS_CORES[0])
    s.facet([(58, 168), (18, 232), (86, 236)], HULL_C)                  # 外刃
    s.facet([(56, 178), (30, 224), (74, 228)], HULL_D)                  # 外刃子面
    s.facet([(205, 88), (26, 188), (90, 252), (205, 212)], HULL_B)      # 主翼
    s.facet([(190, 100), (108, 168), (140, 196), (196, 150)], HULL_C)   # 主翼子面A
    s.facet([(120, 178), (96, 210), (120, 232), (150, 204)], HULL_D)    # 主翼子面B
    s.shade([(96, 216), (92, 246), (190, 210)], alpha=45)
    s.facet([(196, 110), (72, 186), (108, 224), (200, 196)], HULL_C)
    s.facet([(186, 122), (96, 188), (118, 212), (194, 178)], HULL_D)    # 内翼子面
    s.facet([(205, 66), (236, 208), (205, 336), (174, 208)], HULL_A, False)  # 主机身
    s.facet([(205, 66), (236, 208), (205, 218)], HULL_D, False)
    s.facet([(205, 82), (220, 132), (205, 180)], HULL_C, False)         # 机头子面
    s.seam([(200, 118), (96, 208)])
    s.seam([(205, 212), (90, 252)])
    s.seam([(186, 108), (96, 192)], width=1)                            # 主翼板划分
    s.seam([(140, 170), (104, 226)], width=1)
    s.seam([(60, 172), (24, 228)], width=1)                             # 外刃接缝
    s.seam([(186, 240), (224, 240)], mirror=False)                      # 机身环带
    s.seam([(190, 272), (220, 272)], mirror=False)
    s.rim([(205, 66), (236, 208)], mirror=False)
    s.greeble(168, 180, 10, 8)                                          # 翼面舱口
    s.greeble(212, 244, 8, 10)                                          # 机身舱口
    s.panel_dot(190, 130)
    s.panel_dot(182, 160)
    s.panel_dot(120, 200)
    s.panel_dot(196, 232)
    s.panel_dot(194, 260)
    s.vent(150, 216, length=14, gap=4, n=4)                             # 翼根散热格栅
    s.crystal(90, 196, 6)                                               # 翼面晶簇
    s.crystal(140, 150, 4)
    s.crystal(60, 200, 5)
    s.neon([(200, 96), (30, 190)])
    s.neon([(60, 170), (20, 230)], width=2)
    s.neon([(205, 80), (205, 170)], width=1, mirror=False)              # 背部二级走线
    s.neon([(120, 190), (92, 238)], width=1)                            # 翼面二级走线
    s.lamp(28, 188, 4)                                                  # 翼尖灯
    s.lamp(88, 250, 2)                                                  # 后缘航行灯
    s.lamp(174, 320, 2)                                                 # 尾航行灯
    s.ring_core(205, 190, 24)
    s.ring_core(140, 196, 8)
    s.ring_core(270, 196, 8)
    s.nozzle_ring(205, 320, 12, 6)
    s.engine(205, 320, 12, 6)
    s.engine_particles(205, 330, n=4, drop=12, spread=7)
    return s


def boss_2() -> Ship:  # 九头：三联舰体
    s = Ship(410, 410, BOSS_ACCENTS[1], BOSS_CORES[1])
    s.facet([(142, 188), (184, 198), (142, 216)], HULL_B, False)        # 左连接梁
    s.facet([(268, 188), (226, 198), (268, 216)], HULL_B, False)        # 右连接梁
    s.facet([(150, 194), (176, 200), (150, 210)], HULL_C, False)        # 梁子面
    s.facet([(260, 194), (234, 200), (260, 210)], HULL_C, False)
    s.facet([(118, 108), (141, 228), (120, 302), (99, 226)], HULL_C)    # 左舰体
    s.facet([(292, 108), (311, 226), (290, 302), (269, 228)], HULL_C)   # 右舰体
    s.facet([(120, 122), (134, 224), (120, 282), (106, 224)], HULL_B)   # 侧舰体子面
    s.facet([(290, 122), (304, 224), (290, 282), (276, 224)], HULL_B)
    s.facet([(118, 108), (130, 150), (118, 176)], HULL_D)               # 侧舰首亮面
    s.facet([(292, 108), (302, 150), (292, 176)], HULL_D)
    s.shade([(104, 230), (118, 296), (120, 300)], alpha=45)
    s.shade([(306, 230), (292, 296), (290, 300)], alpha=45)
    s.facet([(205, 52), (229, 198), (205, 344), (181, 198)], HULL_A, False)  # 中舰体
    s.facet([(205, 52), (229, 198), (205, 214)], HULL_D, False)
    s.facet([(205, 68), (217, 122), (205, 172)], HULL_C, False)         # 中舰首子面
    s.seam([(120, 112), (120, 298)], mirror=False)
    s.seam([(290, 112), (290, 298)], mirror=False)
    s.seam([(104, 240), (136, 240)], mirror=False)                      # 侧舰体环带
    s.seam([(274, 240), (306, 240)], mirror=False)
    s.seam([(186, 232), (224, 232)], mirror=False)                      # 中舰体环带
    s.seam([(190, 268), (220, 268)], mirror=False)
    s.seam([(194, 300), (216, 300)], mirror=False)
    s.rim([(205, 52), (229, 198)], mirror=False)
    s.greeble(212, 236, 8, 10)                                          # 中舰体舱口
    s.greeble(126, 246, 7, 9)                                           # 侧舰体舱口
    s.greeble(277, 246, 7, 9)
    s.panel_dot(196, 224)
    s.panel_dot(194, 256)
    s.panel_dot(112, 232)
    s.panel_dot(298, 232)
    s.vent(148, 196, length=12, gap=3, n=3, mirror=False)               # 梁散热格栅
    s.vent(240, 196, length=12, gap=3, n=3, mirror=False)
    s.crystal(112, 160, 5)                                              # 侧舰体晶簇
    s.crystal(298, 160, 5)
    s.crystal(205, 120, 4)
    s.neon([(143, 192), (183, 200)], mirror=False, width=2)
    s.neon([(267, 192), (227, 200)], mirror=False, width=2)
    s.neon([(114, 116), (102, 222)], mirror=False, width=2)
    s.neon([(296, 116), (308, 222)], mirror=False, width=2)
    s.neon([(205, 66), (205, 168)], width=1, mirror=False)              # 中脊二级走线
    s.neon([(126, 120), (134, 220)], width=1, mirror=False)             # 侧舰体二级走线
    s.neon([(284, 120), (276, 220)], width=1, mirror=False)
    s.lamp(118, 108, 3)                                                 # 侧舰首灯
    s.lamp(292, 108, 3)
    s.lamp(205, 56, 3)                                                  # 中舰首灯
    s.lamp(104, 296, 2)
    s.lamp(306, 296, 2)
    s.ring_core(205, 186, 22)
    s.ring_core(120, 196, 11)
    s.ring_core(290, 196, 11)
    s.nozzle_ring(205, 328, 11, 6)
    s.engine(205, 328, 11, 6)
    s.engine_particles(205, 338, n=4, drop=11, spread=6)
    s.nozzle_ring(120, 290, 7, 4)
    s.engine(120, 290, 7, 4)
    s.nozzle_ring(290, 290, 7, 4)
    s.engine(290, 290, 7, 4)
    return s


def boss_3() -> Ship:  # 巨柱：六边要塞
    s = Ship(410, 410, BOSS_ACCENTS[2], BOSS_CORES[2])
    s.facet([(138, 148), (68, 198), (134, 232)], HULL_C)                # 侧鳍
    s.facet([(132, 158), (84, 196), (128, 222)], HULL_D)                # 侧鳍子面
    s.shade([(80, 200), (130, 228), (134, 232)], alpha=45)
    s.facet([(205, 42), (272, 118), (272, 252), (205, 342), (138, 252), (138, 118)], HULL_A, False)
    s.facet([(205, 78), (248, 128), (248, 238), (205, 306), (162, 238), (162, 128)], HULL_B, False)
    s.facet([(205, 42), (272, 118), (205, 148), (138, 118)], HULL_C, False)  # 顶部晶面
    s.facet([(205, 78), (248, 128), (205, 160), (162, 128)], HULL_D, False)
    s.facet([(222, 170), (240, 190), (240, 240), (222, 262)], HULL_C, False)  # 侧壁板条
    s.facet([(188, 170), (170, 190), (170, 240), (188, 262)], HULL_C, False)
    s.seam([(205, 148), (205, 342)], mirror=False)
    s.seam([(138, 118), (205, 148)])
    s.seam([(142, 180), (268, 180)], mirror=False)                      # 六面环带
    s.seam([(142, 230), (268, 230)], mirror=False)
    s.seam([(150, 276), (260, 276)], mirror=False)
    s.seam([(130, 160), (78, 198)], width=1)                            # 鳍面板划分
    s.rim([(205, 42), (272, 118)], mirror=False)
    s.greeble(230, 196, 10, 12)                                         # 壁面舱口
    s.greeble(170, 196, 10, 12)
    s.greeble(222, 286, 9, 8)
    s.panel_dot(152, 168)
    s.panel_dot(152, 246)
    s.panel_dot(160, 292)
    s.vent(156, 208, length=12, gap=4, n=3)                             # 散热格栅
    s.vent(238, 208, length=12, gap=4, n=3, mirror=False)
    s.crystal(96, 192, 5)                                               # 鳍面晶簇
    s.crystal(205, 80, 4)
    s.crystal(232, 140, 3)
    s.neon([(140, 152), (70, 198)])
    s.neon([(205, 46), (270, 118)], width=2)
    s.neon([(142, 182), (268, 182)], width=1, mirror=False)             # 环带二级走线
    s.neon([(142, 232), (268, 232)], width=1, mirror=False)
    s.neon([(205, 160), (205, 178)], width=1, mirror=False)
    s.lamp(272, 120, 3)                                                 # 六角节点灯
    s.lamp(138, 120, 3)
    s.lamp(272, 250, 3)
    s.lamp(138, 250, 3)
    s.lamp(70, 198, 3)                                                  # 鳍尖灯
    s.ring_core(205, 196, 28)
    s.ring_core(205, 108, 9)
    s.nozzle_ring(205, 322, 13, 6)
    s.engine(205, 322, 13, 6)
    s.engine_particles(205, 332, n=4, drop=12, spread=7)
    return s


# ---------------- 精英炮塔事件：打击航母（1200×700）与炮塔（96×96，品红） ----------------

## 炮台基座位（贴图像素坐标，供 strike_carrier 绘制与运行时基座环对齐）
TURRET_WELLS = [
    (430, 470),   # 左翼台内
    (770, 470),   # 右翼台内
    (290, 430),   # 左翼台外
    (910, 430),   # 右翼台外
    (600, 520),   # 中央前甲板
]


def _octagon(cx, cy, r):
    return [
        (cx + r * math.cos(math.pi / 8 + i * math.pi / 4),
         cy + r * math.sin(math.pi / 8 + i * math.pi / 4))
        for i in range(8)
    ]


def strike_carrier() -> Ship:  # 拉长六边梭形母舰 + 阶梯甲板翼台 + 三层六边塔楼
    s = Ship(1200, 700, ELITE_ACCENT, ELITE_CORE)
    # 阶梯甲板翼台（左右镜像，三级收缩）
    s.facet([(480, 330), (240, 360), (240, 500), (480, 540)], HULL_B)          # 翼台基座
    s.facet([(470, 344), (258, 370), (258, 488), (470, 526)], HULL_A)          # 翼台基座子面（压暗）
    s.facet([(480, 340), (300, 366), (300, 486), (480, 524)], HULL_C)          # 翼台承力面
    s.facet([(460, 352), (320, 374), (320, 420), (460, 440)], HULL_D)          # 承力面子面A
    s.facet([(460, 452), (320, 432), (320, 474), (460, 508)], HULL_B)          # 承力面子面B
    s.shade([(250, 480), (250, 496), (470, 522)], alpha=45)
    s.facet([(240, 380), (120, 400), (120, 480), (240, 500)], HULL_B)          # 外伸台
    s.facet([(240, 388), (160, 404), (160, 472), (240, 492)], HULL_D)          # 外伸台亮面
    s.facet([(226, 396), (174, 408), (174, 468), (226, 484)], HULL_C)          # 外伸台子面
    s.seam([(480, 330), (240, 360), (120, 400)])
    s.seam([(480, 540), (240, 500), (120, 480)])
    s.seam([(250, 380), (470, 352)], width=1)                                  # 甲板板划分
    s.seam([(250, 434), (470, 446)], width=1)
    s.seam([(250, 486), (470, 514)], width=1)
    s.seam([(160, 404), (160, 472)], width=1)
    s.greeble(340, 386, 22, 14)                                                # 甲板舱口
    s.greeble(400, 470, 18, 12)
    s.greeble(184, 424, 16, 20)                                                # 外伸台设备舱
    s.panel_dot(440, 348)
    s.panel_dot(380, 356)
    s.panel_dot(320, 364)
    s.panel_dot(264, 372)
    s.vent(420, 500, length=24, gap=5, n=4)                                    # 翼台散热格栅
    s.crystal(180, 440, 8)                                                     # 外伸台晶簇
    s.crystal(360, 452, 6)
    # 中央主舰体（高耸六棱柱，舰首朝上）
    s.facet([(600, 60), (690, 160), (690, 560), (600, 640), (510, 560), (510, 160)], HULL_A, False)
    s.facet([(600, 110), (660, 180), (660, 540), (600, 600), (540, 540), (540, 180)], HULL_B, False)
    s.facet([(600, 60), (690, 160), (600, 210), (510, 160)], HULL_C, False)     # 舰首晶面
    s.facet([(600, 110), (660, 180), (600, 224), (540, 180)], HULL_D, False)
    s.facet([(616, 240), (648, 262), (648, 520), (616, 560)], HULL_C, False)    # 右舷壁板条
    s.facet([(584, 240), (552, 262), (552, 520), (584, 560)], HULL_C, False)    # 左舷壁板条
    s.shade([(620, 560), (600, 596), (600, 640), (648, 552)], alpha=40, mirror=False)
    s.seam([(600, 210), (600, 640)], mirror=False)
    s.seam([(510, 160), (600, 210)])
    s.seam([(544, 260), (656, 260)], mirror=False)                              # 舰体环带
    s.seam([(544, 410), (656, 410)], mirror=False)
    s.seam([(552, 590), (648, 590)], mirror=False)
    s.seam([(556, 240), (556, 580)], width=1, mirror=False)                     # 舷侧纵缝
    s.seam([(644, 240), (644, 580)], width=1, mirror=False)
    s.rim([(600, 60), (690, 160)], mirror=False)
    s.greeble(560, 280, 12, 16)                                                 # 舰体舱口
    s.greeble(628, 280, 12, 16)
    s.greeble(566, 430, 10, 14)
    s.greeble(624, 430, 10, 14)
    s.panel_dot(548, 236)
    s.panel_dot(652, 236)
    s.panel_dot(548, 392)
    s.panel_dot(652, 392)
    s.panel_dot(556, 570)
    s.panel_dot(644, 570)
    s.vent(566, 448, length=10, gap=4, n=3, mirror=False)
    s.vent(624, 448, length=10, gap=4, n=3, mirror=False)
    s.crystal(600, 250, 6)                                                      # 舰体晶簇
    s.crystal(560, 360, 4)
    s.crystal(640, 360, 4)
    # 三层收分六边塔楼（舰桥）
    s.facet([(600, 96), (648, 132), (648, 190), (600, 216), (552, 190), (552, 132)], HULL_C, False)
    s.facet([(600, 108), (636, 136), (636, 168), (600, 190), (564, 168), (564, 136)], HULL_D, False)
    s.facet([(600, 96), (618, 116), (600, 132), (582, 116)], HULL_B, False)     # 塔尖晶体
    s.seam([(552, 132), (600, 108)])
    s.seam([(600, 190), (600, 216)], width=1, mirror=False)
    s.rim([(600, 96), (648, 132)], mirror=False)
    s.panel_dot(566, 144)
    s.panel_dot(634, 144)
    s.neon([(566, 158), (634, 158)], mirror=False, width=3)                     # 观察缝
    s.neon([(574, 172), (626, 172)], mirror=False, width=1)                     # 二层观察缝
    s.lamp(600, 100, 3)                                                         # 塔尖灯
    s.lamp(648, 134, 2)
    s.lamp(552, 134, 2)
    # 派系徽记：六边形霓虹框 + 白芯（主舰体正面中央）
    s.neon(_octagon(600, 330, 34), mirror=False, width=3)
    s.ring_core(600, 330, 12)
    # 翼台前缘霓虹走线
    s.neon([(478, 334), (242, 362), (122, 402)])
    s.neon([(478, 536), (242, 496), (122, 476)], width=2)                       # 翼台后缘二级走线
    s.neon([(516, 170), (516, 550)], width=1, mirror=False)                     # 舷侧二级走线
    s.neon([(684, 170), (684, 550)], width=1, mirror=False)
    s.lamp(120, 400, 4)                                                         # 台角航行灯
    s.lamp(120, 480, 4)
    s.lamp(240, 360, 3)
    s.lamp(480, 332, 3)
    s.lamp(510, 162, 3)
    s.lamp(690, 162, 3)
    # 八角炮台基座（闭合盖板 + 接缝勾边 + 暗红待命环；基座位置与运行时 SOCKETS 严格对齐，禁止移动）
    for cx, cy in TURRET_WELLS:
        s.facet(_octagon(cx, cy, 34), HULL_C, False)
        s.facet(_octagon(cx, cy, 26), HULL_B, False)
        s.facet(_octagon(cx, cy - 4, 18), HULL_D, False)                        # 盖板受光子面
        s.seam(_octagon(cx, cy, 34) + [_octagon(cx, cy, 34)[0]], mirror=False)
        s.seam([(cx - 18, cy), (cx + 18, cy)], mirror=False)
        s.seam([(cx, cy - 18), (cx, cy + 18)], width=1, mirror=False)
        for bx, by in ((cx - 26, cy - 26), (cx + 26, cy - 26), (cx - 26, cy + 26), (cx + 26, cy + 26)):
            s.panel_dot(bx, by, r=2, mirror=False)                              # 基座角螺栓
        s.neon(_octagon(cx, cy, 40), mirror=False, width=2, color=(120, 30, 60))
    # 舰尾三组引擎光斑（中央大、两侧小）
    s.nozzle_ring(600, 632, 16, 8)
    s.engine(600, 632, 16, 8)
    s.engine_particles(600, 646, n=4, drop=14, spread=9)
    s.nozzle_ring(500, 622, 10, 5)
    s.engine(500, 622, 10, 5)
    s.nozzle_ring(700, 622, 10, 5)
    s.engine(700, 622, 10, 5)
    return s


def turret() -> Ship:  # 小型六棱柱基座 + 单管晶体炮身（炮口朝上）
    s = Ship(96, 96, ELITE_ACCENT, ELITE_CORE)
    s.facet(_octagon(48, 56, 34), HULL_B, False)                                # 基座
    s.facet(_octagon(48, 56, 26), HULL_C, False)
    s.facet(_octagon(48, 52, 18), HULL_D, False)                                # 基座受光子面
    s.seam(_octagon(48, 56, 34) + [_octagon(48, 56, 34)[0]], mirror=False)
    s.seam(_octagon(48, 56, 26) + [_octagon(48, 56, 26)[0]], width=1, mirror=False)
    s.seam([(24, 56), (72, 56)], width=1, mirror=False)                         # 基座横缝
    s.facet([(42, 12), (54, 12), (53, 52), (43, 52)], HULL_A, False)            # 炮身
    s.facet([(44, 12), (48, 12), (48, 50), (44, 50)], HULL_D, False)            # 炮身亮面
    s.seam([(42, 24), (54, 24)], width=1, mirror=False)                         # 炮身节缝
    s.seam([(42, 36), (54, 36)], width=1, mirror=False)
    s.seam([(43, 46), (53, 46)], width=1, mirror=False)
    s.rim([(42, 12), (54, 12)], mirror=False)
    s.panel_dot(28, 44)
    s.panel_dot(68, 44)
    s.panel_dot(30, 70)
    s.panel_dot(66, 70)
    s.vent(26, 76, length=9, gap=3, n=2)                                        # 基座散热格栅
    s.nozzle_ring(48, 15, 7, 4)                                                 # 炮口制退环
    s.neon(_octagon(48, 56, 30), mirror=False, width=2)                         # 充能环
    s.lamp(24, 48, 1.5)                                                         # 基座节点灯
    s.lamp(72, 48, 1.5)
    s.lamp(48, 86, 1.5)
    s.energy_core(48, 18, 7)                                                    # 炮口能量核心
    return s


def main() -> None:
    # R07（2026-08-05 独立审计）：输出路径锚定脚本位置（同 generate_audio 口径），
    # 不再依赖调用时 cwd——非仓库根运行不会在别处落盘或崩溃
    base = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "assets", "sprites")) + os.sep
    ships = [
        (enemy_1, "enemy_ship_1.png", PALETTE_BRIGHT), (enemy_2, "enemy_ship_2.png", PALETTE_BRIGHT),
        (enemy_3, "enemy_ship_3.png", PALETTE_BRIGHT), (enemy_4, "enemy_ship_4.png", PALETTE_BRIGHT),
        (elite_1, "elite_ship_1.png", PALETTE_BRIGHT), (elite_2, "elite_ship_2.png", PALETTE_BRIGHT),
        (elite_3, "elite_ship_3.png", PALETTE_BRIGHT),
        (boss_1, "boss_ship_1.png", PALETTE_DARK), (boss_2, "boss_ship_2.png", PALETTE_DARK),
        (boss_3, "boss_ship_3.png", PALETTE_DARK),
        (strike_carrier, "strike_carrier.png", PALETTE_DARK), (turret, "elite_turret.png", PALETTE_DARK),
    ]
    for fn, name, palette in ships:
        _apply_palette(palette)
        fn().finish(base + name)


if __name__ == "__main__":
    main()
