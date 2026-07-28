#!/usr/bin/env python3
"""离线敌方单位贴图生成器（晶体棱镜风格，非游戏运行时依赖）。

重绘 4 普通机 + 3 精英 + 3 Boss，直接覆盖 assets/sprites/ 同名 PNG
（画布尺寸与原贴图一致：190/245/410，机头朝上，场景根节点 rotation=PI 翻转）。
用法：python3 scripts/tools/generate_enemy_sprites.py
"""

from PIL import Image, ImageDraw, ImageFilter

S = 4  # 超采样抗锯齿

# 深色晶舰体分段面
HULL_A = (22, 18, 34, 255)
HULL_B = (34, 28, 52, 255)
HULL_C = (48, 40, 72, 255)
HULL_D = (62, 52, 92, 255)
SEAM = (10, 8, 18, 255)
RIM = (150, 140, 185, 255)

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

    def seam(self, pts, width=2, mirror=True):
        self.bd.line(self.p(pts), fill=SEAM, width=width * S, joint="curve")
        if mirror:
            self.bd.line(self.p(self.mirror(pts)), fill=SEAM, width=width * S, joint="curve")

    def rim(self, pts, width=2, mirror=True):
        self.bd.line(self.p(pts), fill=RIM, width=width * S, joint="curve")
        if mirror:
            self.bd.line(self.p(self.mirror(pts)), fill=RIM, width=width * S, joint="curve")

    def neon(self, pts, width=3, color=None, mirror=True):
        c = (color or self.accent) + (255,)
        self.gd.line(self.p(pts), fill=c, width=width * S, joint="curve")
        if mirror:
            self.gd.line(self.p(self.mirror(pts)), fill=c, width=width * S, joint="curve")

    def energy_core(self, cx, cy, r, color=None):
        c = (color or self.core) + (255,)
        self.gd.ellipse(self.p([(cx - r, cy - r), (cx + r, cy + r)]), fill=c)
        w = max(r * 0.45, 2.0)
        self.gd.ellipse(self.p([(cx - w, cy - w), (cx + w, cy + w)]), fill=(255, 255, 255, 240))

    def engine(self, cx, cy, rx, ry, color=None):
        c = (color or self.accent) + (230,)
        self.gd.ellipse(self.p([(cx - rx, cy - ry), (cx + rx, cy + ry)]), fill=c)

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
    s.facet([(90, 82), (52, 132), (72, 138), (91, 112)], HULL_C)       # 内翼面
    s.facet([(95, 22), (111, 92), (95, 168), (79, 92)], HULL_A, False)  # 菱形机身
    s.facet([(95, 22), (111, 92), (95, 110)], HULL_C, False)            # 机头亮面
    s.seam([(95, 22), (95, 168)], mirror=False)
    s.rim([(95, 22), (111, 92)], mirror=False)
    s.neon([(90, 84), (30, 142)])                                       # 翼前缘霓虹
    s.energy_core(95, 96, 9)
    s.engine(95, 158, 7, 4)
    return s


def enemy_2() -> Ship:  # 蝠鲼：新月宽翼
    s = Ship(190, 190, ENEMY_ACCENT, ENEMY_CORE)
    s.facet([(95, 42), (18, 118), (52, 132), (95, 104)], HULL_B)
    s.facet([(95, 58), (44, 112), (62, 120), (95, 98)], HULL_C)
    s.facet([(95, 32), (107, 108), (95, 162), (83, 108)], HULL_A, False)
    s.facet([(95, 32), (107, 108), (95, 96)], HULL_D, False)
    s.seam([(95, 60), (52, 122)])
    s.rim([(95, 32), (107, 108)], mirror=False)
    s.neon([(93, 46), (22, 118)])
    s.energy_core(95, 92, 9)
    s.engine(95, 152, 7, 4)
    return s


def enemy_3() -> Ship:  # 重锤：前掠翼厚机身
    s = Ship(190, 190, ENEMY_ACCENT, ENEMY_CORE)
    s.facet([(82, 88), (38, 48), (56, 112), (85, 118)], HULL_B)        # 前掠翼
    s.facet([(80, 92), (52, 66), (62, 106), (84, 112)], HULL_D)
    s.facet([(95, 28), (116, 88), (109, 162), (81, 162), (74, 88)], HULL_A, False)
    s.facet([(95, 28), (116, 88), (95, 104), (74, 88)], HULL_C, False)
    s.seam([(95, 104), (95, 162)], mirror=False)
    s.rim([(95, 28), (116, 88)], mirror=False)
    s.neon([(81, 90), (40, 50)])
    s.energy_core(95, 104, 11)
    s.engine(95, 156, 9, 4)
    return s


def enemy_4() -> Ship:  # 针刺：双叉机头
    s = Ship(190, 190, ENEMY_ACCENT, ENEMY_CORE)
    s.facet([(86, 96), (46, 134), (79, 126)], HULL_B)                   # 小翼
    s.facet([(80, 32), (90, 26), (93, 100), (83, 106)], HULL_C)         # 左叉
    s.facet([(95, 58), (106, 128), (95, 166), (84, 128)], HULL_A, False)
    s.facet([(95, 58), (106, 128), (95, 118)], HULL_D, False)
    s.seam([(93, 100), (95, 166)])
    s.rim([(80, 32), (90, 26)])
    s.neon([(86, 98), (47, 134)])
    s.energy_core(95, 108, 9)
    s.engine(95, 156, 7, 4)
    return s


# ---------------- 精英（245×245，品红） ----------------

def elite_1() -> Ship:  # 枪骑：长机身 + 侧刃 + 鸭翼
    s = Ship(245, 245, ELITE_ACCENT, ELITE_CORE)
    s.facet([(110, 88), (42, 158), (72, 192), (113, 150)], HULL_B)      # 侧刃
    s.facet([(111, 100), (66, 152), (82, 168), (113, 140)], HULL_C)
    s.facet([(112, 58), (76, 92), (110, 96)], HULL_C)                   # 鸭翼
    s.facet([(122, 24), (139, 118), (122, 218), (105, 118)], HULL_A, False)
    s.facet([(122, 24), (139, 118), (122, 132)], HULL_D, False)
    s.seam([(122, 24), (122, 218)], mirror=False)
    s.rim([(122, 24), (139, 118)], mirror=False)
    s.neon([(111, 92), (44, 158)])
    s.neon([(111, 60), (78, 92)], width=2)
    s.energy_core(122, 120, 12)
    s.engine(122, 206, 8, 5)
    return s


def elite_2() -> Ship:  # 卫盾：宽盾翼 + 装甲板
    s = Ship(245, 245, ELITE_ACCENT, ELITE_CORE)
    s.facet([(122, 48), (28, 108), (44, 152), (122, 126)], HULL_B)      # 盾翼
    s.facet([(118, 66), (52, 108), (62, 136), (118, 118)], HULL_C)      # 装甲板
    s.facet([(96, 84), (66, 106), (74, 126), (98, 112)], HULL_D)
    s.facet([(122, 38), (135, 138), (122, 202), (109, 138)], HULL_A, False)
    s.facet([(122, 38), (135, 138), (122, 120)], HULL_D, False)
    s.seam([(118, 68), (60, 112)])
    s.seam([(122, 126), (44, 152)])
    s.rim([(122, 38), (135, 138)], mirror=False)
    s.neon([(120, 52), (30, 108)])
    s.energy_core(122, 116, 12)
    s.engine(122, 192, 8, 5)
    return s


def elite_3() -> Ship:  # 掠舰：爪形翼
    s = Ship(245, 245, ELITE_ACCENT, ELITE_CORE)
    s.facet([(113, 78), (36, 58), (58, 138), (40, 172), (101, 150)], HULL_B)  # 爪翼
    s.facet([(110, 90), (56, 76), (66, 130), (100, 138)], HULL_C)
    s.facet([(122, 34), (137, 128), (122, 208), (107, 128)], HULL_A, False)
    s.facet([(122, 34), (137, 128), (122, 124)], HULL_D, False)
    s.seam([(110, 92), (58, 138)])
    s.rim([(122, 34), (137, 128)], mirror=False)
    s.neon([(112, 80), (38, 60)])
    s.neon([(58, 138), (42, 170)], width=2)
    s.energy_core(122, 116, 12)
    s.engine(122, 198, 8, 5)
    return s


# ---------------- Boss（410×410） ----------------

def boss_1() -> Ship:  # 君王：重型宽翼母舰
    s = Ship(410, 410, BOSS_ACCENTS[0], BOSS_CORES[0])
    s.facet([(58, 168), (18, 232), (86, 236)], HULL_C)                  # 外刃
    s.facet([(205, 88), (26, 188), (90, 252), (205, 212)], HULL_B)      # 主翼
    s.facet([(196, 110), (72, 186), (108, 224), (200, 196)], HULL_C)
    s.facet([(205, 66), (236, 208), (205, 336), (174, 208)], HULL_A, False)  # 主机身
    s.facet([(205, 66), (236, 208), (205, 218)], HULL_D, False)
    s.seam([(200, 118), (96, 208)])
    s.seam([(205, 212), (90, 252)])
    s.rim([(205, 66), (236, 208)], mirror=False)
    s.neon([(200, 96), (30, 190)])
    s.neon([(60, 170), (20, 230)], width=2)
    s.energy_core(205, 190, 24)
    s.energy_core(140, 196, 8)
    s.energy_core(270, 196, 8)
    s.engine(205, 320, 12, 6)
    return s


def boss_2() -> Ship:  # 九头：三联舰体
    s = Ship(410, 410, BOSS_ACCENTS[1], BOSS_CORES[1])
    s.facet([(142, 188), (184, 198), (142, 216)], HULL_B, False)        # 左连接梁
    s.facet([(268, 188), (226, 198), (268, 216)], HULL_B, False)        # 右连接梁
    s.facet([(118, 108), (141, 228), (120, 302), (99, 226)], HULL_C)    # 左舰体
    s.facet([(292, 108), (311, 226), (290, 302), (269, 228)], HULL_C)   # 右舰体
    s.facet([(205, 52), (229, 198), (205, 344), (181, 198)], HULL_A, False)  # 中舰体
    s.facet([(205, 52), (229, 198), (205, 214)], HULL_D, False)
    s.seam([(120, 112), (120, 298)], mirror=False)
    s.seam([(290, 112), (290, 298)], mirror=False)
    s.rim([(205, 52), (229, 198)], mirror=False)
    s.neon([(143, 192), (183, 200)], mirror=False, width=2)
    s.neon([(267, 192), (227, 200)], mirror=False, width=2)
    s.neon([(114, 116), (102, 222)], mirror=False, width=2)
    s.neon([(296, 116), (308, 222)], mirror=False, width=2)
    s.energy_core(205, 186, 22)
    s.energy_core(120, 196, 11)
    s.energy_core(290, 196, 11)
    s.engine(205, 328, 11, 6)
    s.engine(120, 290, 7, 4)
    s.engine(290, 290, 7, 4)
    return s


def boss_3() -> Ship:  # 巨柱：六边要塞
    s = Ship(410, 410, BOSS_ACCENTS[2], BOSS_CORES[2])
    s.facet([(138, 148), (68, 198), (134, 232)], HULL_C)                # 侧鳍
    s.facet([(205, 42), (272, 118), (272, 252), (205, 342), (138, 252), (138, 118)], HULL_A, False)
    s.facet([(205, 78), (248, 128), (248, 238), (205, 306), (162, 238), (162, 128)], HULL_B, False)
    s.facet([(205, 42), (272, 118), (205, 148), (138, 118)], HULL_C, False)  # 顶部晶面
    s.facet([(205, 78), (248, 128), (205, 160), (162, 128)], HULL_D, False)
    s.seam([(205, 148), (205, 342)], mirror=False)
    s.seam([(138, 118), (205, 148)])
    s.rim([(205, 42), (272, 118)], mirror=False)
    s.neon([(140, 152), (70, 198)])
    s.neon([(205, 46), (270, 118)], width=2)
    s.energy_core(205, 196, 28)
    s.energy_core(205, 108, 9)
    s.engine(205, 322, 13, 6)
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
    import math
    return [
        (cx + r * math.cos(math.pi / 8 + i * math.pi / 4),
         cy + r * math.sin(math.pi / 8 + i * math.pi / 4))
        for i in range(8)
    ]


def strike_carrier() -> Ship:  # 拉长六边梭形母舰 + 阶梯甲板翼台 + 三层六边塔楼
    s = Ship(1200, 700, ELITE_ACCENT, ELITE_CORE)
    # 阶梯甲板翼台（左右镜像，三级收缩）
    s.facet([(480, 330), (240, 360), (240, 500), (480, 540)], HULL_B)          # 翼台基座
    s.facet([(480, 340), (300, 366), (300, 486), (480, 524)], HULL_C)          # 翼台承力面
    s.facet([(240, 380), (120, 400), (120, 480), (240, 500)], HULL_B)          # 外伸台
    s.facet([(240, 388), (160, 404), (160, 472), (240, 492)], HULL_D)          # 外伸台亮面
    s.seam([(480, 330), (240, 360), (120, 400)])
    s.seam([(480, 540), (240, 500), (120, 480)])
    # 中央主舰体（高耸六棱柱，舰首朝上）
    s.facet([(600, 60), (690, 160), (690, 560), (600, 640), (510, 560), (510, 160)], HULL_A, False)
    s.facet([(600, 110), (660, 180), (660, 540), (600, 600), (540, 540), (540, 180)], HULL_B, False)
    s.facet([(600, 60), (690, 160), (600, 210), (510, 160)], HULL_C, False)     # 舰首晶面
    s.facet([(600, 110), (660, 180), (600, 224), (540, 180)], HULL_D, False)
    s.seam([(600, 210), (600, 640)], mirror=False)
    s.seam([(510, 160), (600, 210)])
    s.rim([(600, 60), (690, 160)], mirror=False)
    # 三层收分六边塔楼（舰桥）
    s.facet([(600, 96), (648, 132), (648, 190), (600, 216), (552, 190), (552, 132)], HULL_C, False)
    s.facet([(600, 108), (636, 136), (636, 168), (600, 190), (564, 168), (564, 136)], HULL_D, False)
    s.facet([(600, 96), (618, 116), (600, 132), (582, 116)], HULL_B, False)     # 塔尖晶体
    s.seam([(552, 132), (600, 108)])
    s.neon([(566, 158), (634, 158)], mirror=False, width=3)                     # 观察缝
    # 派系徽记：六边形霓虹框 + 白芯（主舰体正面中央）
    s.neon(_octagon(600, 330, 34), mirror=False, width=3)
    s.energy_core(600, 330, 12)
    # 翼台前缘霓虹走线
    s.neon([(478, 334), (242, 362), (122, 402)])
    # 八角炮台基座（闭合盖板 + 接缝勾边 + 暗红待命环）
    for cx, cy in TURRET_WELLS:
        s.facet(_octagon(cx, cy, 34), HULL_C, False)
        s.facet(_octagon(cx, cy, 26), HULL_B, False)
        s.seam(_octagon(cx, cy, 34) + [_octagon(cx, cy, 34)[0]], mirror=False)
        s.seam([(cx - 18, cy), (cx + 18, cy)], mirror=False)
        s.neon(_octagon(cx, cy, 40), mirror=False, width=2, color=(120, 30, 60))
    # 舰尾三组引擎光斑（中央大、两侧小）
    s.engine(600, 632, 16, 8)
    s.engine(500, 622, 10, 5)
    s.engine(700, 622, 10, 5)
    return s


def turret() -> Ship:  # 小型六棱柱基座 + 单管晶体炮身（炮口朝上）
    s = Ship(96, 96, ELITE_ACCENT, ELITE_CORE)
    s.facet(_octagon(48, 56, 34), HULL_B, False)                                # 基座
    s.facet(_octagon(48, 56, 26), HULL_C, False)
    s.seam(_octagon(48, 56, 34) + [_octagon(48, 56, 34)[0]], mirror=False)
    s.facet([(42, 12), (54, 12), (53, 52), (43, 52)], HULL_A, False)            # 炮身
    s.facet([(44, 12), (48, 12), (48, 50), (44, 50)], HULL_D, False)            # 炮身亮面
    s.rim([(42, 12), (54, 12)], mirror=False)
    s.neon(_octagon(48, 56, 30), mirror=False, width=2)                         # 充能环
    s.energy_core(48, 18, 7)                                                    # 炮口能量核心
    return s


def main() -> None:
    base = "assets/sprites/"
    ships = [
        (enemy_1, "enemy_ship_1.png"), (enemy_2, "enemy_ship_2.png"),
        (enemy_3, "enemy_ship_3.png"), (enemy_4, "enemy_ship_4.png"),
        (elite_1, "elite_ship_1.png"), (elite_2, "elite_ship_2.png"),
        (elite_3, "elite_ship_3.png"),
        (boss_1, "boss_ship_1.png"), (boss_2, "boss_ship_2.png"),
        (boss_3, "boss_ship_3.png"),
        (strike_carrier, "strike_carrier.png"), (turret, "elite_turret.png"),
    ]
    for fn, name in ships:
        fn().finish(base + name)


if __name__ == "__main__":
    main()
