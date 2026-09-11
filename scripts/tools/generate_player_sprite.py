#!/usr/bin/env python3
"""离线玩家战机贴图生成器（暖钛/青铜钢甲 + 琥珀能量，非游戏运行时依赖）。

重绘 assets/sprites/player_ship.png（画布 254×254 与原版一致，机头朝上即游戏内 -Y）。
设计语言参考经典纵版射击主机（Vic Viper / Raiden 系）：尖锐长机头、鸭翼、
后掠三角主翼、双引擎尾喷；与敌方晶体棱镜风格区分——玩家为分层装甲板 + 铆接接缝。
视觉升级：机体改暖钛/青铜色温 + 琥珀能量语言，装甲板细分/铆接/散热格栅/
舱口加密；外形轮廓与下列锚点严格不变（几何坐标零改动，仅换色与叠加细节）。

精细化层次（外形轮廓与下列锚点严格不变，仅叠加细节）：
- 装甲板细分：子面 + 环带/板划接缝 + 铆接点（panel_dot）+ 散热格栅（vent）+ 舱口（greeble）
- 引擎区：喷管装甲环（nozzle_ring）+ 内焰白芯 + 微粒子点（engine_particles）
- 霓虹加密：脊线/翼后缘二级走线 + 节点航行灯（lamp）
- 座舱：钢甲框缘 + 原有琥珀玻璃高光

另输出能量发光遮罩 player_ship_glow.png（同画布 254×254，黑底）：
从 glow/engine_glow 图元层提取发光像素——R=霓虹走线/航行灯/座舱，B=引擎喷口，
供 ship_energy.gdshader 运行期叠加（基础贴图生成逻辑零改动）。

附件锚点（贴图像素坐标，供 scripts/player_buff_visuals.gd 对齐机体部位）：
    机头尖端 (127, 16)   座舱 (127, 92)    鸭翼翼尖 (84/170, 96)
    主翼翼尖 (12/242, 206)                翼根前缘 (104/150, 118)
    背部脊线 y≈118-200   引擎喷口 (108/146, 230)   机尾端 (127, 236)
用法：python3 scripts/tools/generate_player_sprite.py
"""

import os

import sprite_polish
from PIL import Image, ImageDraw, ImageFilter

S = 4  # 超采样抗锯齿

# 暖钛/青铜钢甲分层（暖调褐灰，由暗到亮）——战术琥珀视觉语言
HULL_A = (30, 27, 25, 255)
HULL_B = (46, 41, 37, 255)
HULL_C = (66, 59, 52, 255)
HULL_D = (94, 85, 74, 255)
SEAM = (14, 12, 11, 255)
RIM = (228, 210, 178, 255)
RIVET = (150, 136, 116, 255)   # 铆接点（亮于 SEAM 暗于 RIM）

ACCENT = (255, 166, 46)     # 琥珀能量
CORE = (255, 214, 140)

# 玩家机专用后期色（暖描边 + 暖缘光，与机体色温统一；敌方晶体族用模块默认冷色）
PLAYER_OUTLINE = (12, 9, 7, 255)
PLAYER_RIM = (255, 232, 196)


class Ship:
    """分层绘制：body（实体面）+ glow（霓虹线/能量，模糊光晕 + 清晰本体）。

    engine_glow 为引擎喷口图元的独立副本层（engine/engine_particles 同时写入 glow 与本层，
    glow 合成保持原样——基础贴图逐字节不变），仅供发光遮罩导出 B 通道。"""

    def __init__(self, w: int, h: int) -> None:
        self.w, self.h = w, h
        self.body = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
        self.glow = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
        self.engine_glow = Image.new("RGBA", (w * S, h * S), (0, 0, 0, 0))
        self.bd = ImageDraw.Draw(self.body)
        self.gd = ImageDraw.Draw(self.glow)
        self.ed = ImageDraw.Draw(self.engine_glow)

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
        # 同图元双写 glow/engine_glow（遮罩 B 通道取后者）
        self.gd.ellipse(self.p([(cx - rx, cy - ry), (cx + rx, cy + ry)]), fill=ACCENT + (230,))
        self.ed.ellipse(self.p([(cx - rx, cy - ry), (cx + rx, cy + ry)]), fill=ACCENT + (230,))
        w = max(rx * 0.45, 2.0)
        self.gd.ellipse(self.p([(cx - w, cy - ry * 0.5), (cx + w, cy + ry * 0.5)]), fill=(255, 255, 255, 240))
        self.ed.ellipse(self.p([(cx - w, cy - ry * 0.5), (cx + w, cy + ry * 0.5)]), fill=(255, 255, 255, 240))

    def engine_particles(self, cx, cy, n=3, drop=8, spread=4):
        """喷口微粒子点（glow + engine_glow 双写，确定性排布）。"""
        c = ACCENT + (200,)
        for i in range(n):
            t = i + 1
            x = cx + ((-1) ** i) * spread * (0.4 + 0.3 * i)
            y = cy + drop * t / n + 2
            r = max(1.2 - 0.2 * i, 0.7)
            self.gd.ellipse(self.p([(x - r, y - r), (x + r, y + r)]), fill=c)
            self.ed.ellipse(self.p([(x - r, y - r), (x + r, y + r)]), fill=c)

    def finish(self, path: str, blur: float = 6.0) -> None:
        halo = self.glow.filter(ImageFilter.GaussianBlur(blur * S))
        out = Image.alpha_composite(halo, self.body)
        out = Image.alpha_composite(out, self.glow)
        out = out.resize((self.w, self.h), Image.LANCZOS)
        out = sprite_polish.polish(out, outline_color=PLAYER_OUTLINE, rim_color=PLAYER_RIM)
        out.save(path)
        print("saved", path)

    def finish_glow(self, path: str, blur: float = 6.0) -> None:
        """能量发光遮罩导出（黑底 R/B 通道，见 sprite_polish.save_glow_mask）。"""
        sprite_polish.save_glow_mask(self.glow, self.engine_glow, (self.w, self.h), blur * S, path)


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
    # 视觉升级：外翼加密——第二道板划分缝 + 翼中铆钉 + 翼尖散热格栅 + 副舱口
    s.seam([(98, 146), (62, 192)], width=1)                                  # 外翼二级板划分
    s.seam([(86, 160), (52, 204)], width=1)                                  # 外翼三级细分
    s.panel_dot(88, 170, r=1.2)
    s.panel_dot(74, 188, r=1.2)
    s.vent(40, 208, length=12, gap=3, n=2)                                   # 翼尖散热格栅
    s.greeble(62, 194, 6, 5)                                                 # 外翼副舱口

    # ---- 鸭翼（前部小翼） ----
    s.facet([(108, 86), (84, 96), (92, 106), (108, 100)], HULL_C)
    s.facet([(106, 90), (92, 97), (97, 102), (106, 98)], HULL_D)             # 鸭翼子面
    s.rim([(108, 86), (84, 96)])
    s.panel_dot(104, 94, r=1.2)
    s.seam([(107, 88), (90, 100)], width=1)                                  # 鸭翼板划分
    s.panel_dot(98, 98, r=1.0)

    # ---- 尾翼（小垂尾外八，根部接翼根） ----
    s.facet([(102, 168), (78, 228), (92, 232), (106, 206)], HULL_B)
    s.facet([(100, 176), (86, 222), (92, 224), (102, 204)], HULL_C)          # 垂尾子面
    s.seam([(100, 178), (86, 222)], width=1)
    s.panel_dot(98, 196, r=1.2)
    s.seam([(104, 172), (82, 226)], width=1)                                 # 垂尾二级板划分
    s.greeble(94, 208, 4, 5)                                                 # 垂尾小舱口

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
    # 视觉升级：机身加密——机头分段缝 + 前段环带 + 侧舷铆钉列 + 机头副舱口/散热缝
    s.seam([(cx, 48), (128, 66)], width=1, mirror=False)                     # 机头前段环带
    s.seam([(cx, 48), (126, 66)], width=1, mirror=False)
    s.seam([(113, 118), (139, 118)], width=1, mirror=False)                  # 座舱后前段环带
    s.panel_dot(122, 132, r=1.2)
    s.panel_dot(132, 132, r=1.2)
    s.panel_dot(121, 172, r=1.0)
    s.panel_dot(133, 172, r=1.0)
    s.greeble(124, 106, 6, 6)                                                # 机头副舱口
    s.vent(114, 100, length=9, gap=3, n=2)                                   # 机头侧散热缝

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
    # 视觉升级：整流罩加密——第三道节缝 + 加强环铆钉
    s.seam([(99, 222), (113, 219)], width=1)
    s.panel_dot(104, 206, r=1.0)
    s.panel_dot(104, 220, r=1.0)

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
    # 视觉升级：能量加密——脊线三级走线 + 翼面航灯 + 座舱边缘描线
    s.neon([(cx, 136), (cx, 176)], width=1, mirror=False)                    # 脊线复走
    s.lamp(72, 190, 1.3)                                                     # 外翼航灯
    s.lamp(96, 176, 1.2)                                                     # 内翼航灯
    s.neon([(119, 80), (119, 104)], width=1)                                 # 座舱侧描线
    s.neon([(135, 80), (135, 104)], width=1)
    return s


def player_ship_hit_1() -> Ship:
    """受击帧1：轻度损伤——裂纹 + 能量闪烁 + 小火花。"""
    s = player_ship()  # 基于正常帧叠加损伤
    # 裂纹线（橙红色，跨越机身）
    CRACK = (180, 80, 30, 255)
    CRACK_GLOW = (255, 140, 40, 180)
    # 机身裂纹
    s.bd.line(s.p([(115, 130), (122, 155), (135, 170)]), fill=CRACK, width=2 * S, joint="curve")
    s.bd.line(s.p([(139, 130), (132, 155), (119, 170)]), fill=CRACK, width=2 * S, joint="curve")
    # 左翼裂纹
    s.bd.line(s.p([(90, 150), (65, 185), (50, 200)]), fill=CRACK, width=2 * S, joint="curve")
    # 右翼裂纹（镜像）
    s.bd.line(s.p([(164, 150), (189, 185), (204, 200)]), fill=CRACK, width=2 * S, joint="curve")
    # 裂纹辉光
    s.gd.line(s.p([(115, 130), (122, 155), (135, 170)]), fill=CRACK_GLOW, width=4 * S, joint="curve")
    s.gd.line(s.p([(139, 130), (132, 155), (119, 170)]), fill=CRACK_GLOW, width=4 * S, joint="curve")
    # 小火花点（橙色，机身周围）
    SPARK = (255, 200, 60, 220)
    sparks = [(110, 145), (144, 145), (127, 160), (95, 175), (159, 175)]
    for sx, sy in sparks:
        s.gd.ellipse(s.p([(sx - 2, sy - 2), (sx + 2, sy + 2)]), fill=SPARK)
    return s


def player_ship_hit_2() -> Ship:
    """受击帧2：重度损伤——更多裂纹 + 暗淡 + 火花扩散 + 座舱受损。"""
    s = player_ship_hit_1()  # 基于轻度损伤叠加
    CRACK = (160, 60, 20, 255)
    CRACK_GLOW = (220, 100, 30, 200)
    # 额外裂纹（更密）
    s.bd.line(s.p([(120, 100), (125, 120), (118, 145)]), fill=CRACK, width=2 * S, joint="curve")
    s.bd.line(s.p([(134, 100), (129, 120), (136, 145)]), fill=CRACK, width=2 * S, joint="curve")
    s.bd.line(s.p([(100, 190), (115, 210), (127, 225)]), fill=CRACK, width=2 * S, joint="curve")
    s.bd.line(s.p([(154, 190), (139, 210), (127, 225)]), fill=CRACK, width=2 * S, joint="curve")
    # 裂纹辉光
    s.gd.line(s.p([(120, 100), (125, 120), (118, 145)]), fill=CRACK_GLOW, width=4 * S, joint="curve")
    s.gd.line(s.p([(134, 100), (129, 120), (136, 145)]), fill=CRACK_GLOW, width=4 * S, joint="curve")
    # 座舱暗淡（覆盖半透明暗色）
    s.bd.ellipse(s.p([(118, 74), (136, 110)]), fill=(40, 20, 10, 120))
    # 更多火花
    SPARK = (255, 180, 40, 200)
    sparks2 = [(105, 125), (149, 125), (127, 140), (88, 200), (166, 200),
               (127, 110), (112, 195), (142, 195)]
    for sx, sy in sparks2:
        r = 2.5 if (sx + sy) % 7 < 3 else 1.8
        s.gd.ellipse(s.p([(sx - r, sy - r), (sx + r, sy + r)]), fill=SPARK)
    # 整体暗淡叠加
    dark = Image.new("RGBA", (254 * S, 254 * S), (20, 10, 5, 60))
    s.body = Image.alpha_composite(s.body, dark)
    return s


def main() -> None:
    sprite_dir = os.path.normpath(
        os.path.join(os.path.dirname(__file__), "..", "..", "assets", "sprites")
    )
    player_ship().finish(os.path.join(sprite_dir, "player_ship.png"))
    player_ship().finish_glow(os.path.join(sprite_dir, "player_ship_glow.png"))
    player_ship_hit_1().finish(os.path.join(sprite_dir, "player_ship_hit_1.png"))
    player_ship_hit_2().finish(os.path.join(sprite_dir, "player_ship_hit_2.png"))


if __name__ == "__main__":
    main()
