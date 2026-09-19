#!/usr/bin/env python3
"""离线「初始机型」五套玩家机贴图生成器（暖钛/青铜钢甲 + 琥珀能量，非游戏运行时依赖）。

产物（assets/sprites/，每型 4 张，画布一律 254×254、机头朝 -Y）：
    player_ship_peregrine.png / _glow.png / _hit_1.png / _hit_2.png   「游隼」移动速度
    player_ship_sledge.png    / _glow.png / _hit_1.png / _hit_2.png   「重锤」攻击力
    player_ship_repeater.png  / _glow.png / _hit_1.png / _hit_2.png   「连弩」攻击速度
    player_ship_bulwark.png   / _glow.png / _hit_1.png / _hit_2.png   「壁垒」防御
    player_ship_colossus.png  / _glow.png / _hit_1.png / _hit_2.png   「巨像」血量上限

贴图主干与 `csharp/core/Machines/MachineRoster.cs` 的 MachineSpec.SpriteStem 同一口径；
_glow 供 ship_energy.gdshader（R=霓虹走线/航灯/座舱，B=引擎喷口），_hit_1/_hit_2 复用
`generate_player_sprite.apply_hit_1/apply_hit_2` 的同一套裂纹。

**骨架契约**（九锚点 + 挂点，五型逐像素共用；C# 侧 `csharp/core/Visual/PlayerHullLayout.cs`
的挂点坐标按这套锚点写死，故只改细节不改骨架）：
    机头尖端 (127, 16)   座舱中心 (127, 92)   鸭翼翼尖 (84/170, 96)
    主翼翼尖 (12/242, 206)                   机尾端 (127, 236)
    引擎喷口 (108/146, 230)
    挂点实体甲板：机炮 (97/157, 127)  散热口 (117/137, 173)  航灯 (49/205, 179)
                  损伤烟 (127, 189)  机首反推 (127, 23)
`_assert_sprite` 在落盘后把上面每一点连同画布尺寸一起断言——形状悄悄坏掉时生成即失败。

家族口径：与标准型同族（暖描边 + 暖缘光、分层甲板 + 铆接接缝 + 琥珀能量），靠**剪影与细节**
区分五型而非换色相；敌方晶体棱镜族是冷色描边 + 紫晶面，两者不混。
用法：python3 scripts/tools/generate_machine_sprites.py
"""

import os
import sys

from PIL import Image

import generate_player_sprite as player_sprite
from generate_player_sprite import Ship, apply_hit_1, apply_hit_2
from generate_player_sprite import (
    ACCENT as STD_ACCENT,
    CORE as STD_CORE,
    HULL_A as STD_HULL_A,
    HULL_B as STD_HULL_B,
    HULL_C as STD_HULL_C,
    HULL_D as STD_HULL_D,
    RIM as STD_RIM,
    RIVET as STD_RIVET,
    SEAM as STD_SEAM,
)

CX = 127  # 中线

# ---- 骨架九锚点（左半侧写一份，右半侧由 _mirror 推出；中线点镜像后即自身） ----
SKELETON = {
    "机头尖端": (127, 16),
    "座舱中心": (127, 92),
    "鸭翼翼尖": (84, 96),
    "主翼翼尖": (12, 206),
    "机尾端": (127, 236),
    "左引擎喷口": (108, 230),
    "右引擎喷口": (146, 230),
}

# ---- 挂点（PlayerHullLayout 的运行期挂件落点，必须有实体甲板） ----
HARDPOINTS = {
    "左机炮": (97, 127),
    "右机炮": (157, 127),
    "左散热口": (117, 173),
    "右散热口": (137, 173),
    "左航灯": (49, 179),
    "右航灯": (205, 179),
    "损伤烟": (127, 189),
    "机首反推": (127, 23),
}

# 受击帧裂纹路径（generate_player_sprite.apply_hit_1/2 里写死的同一批折线）：
# 机型在机身中段与机翼内段必须有实体甲板，否则裂纹会飘在空中——落盘后沿路径采样断言。
DAMAGE_PATHS = (
    ((115, 130), (122, 155), (135, 170)),
    ((139, 130), (132, 155), (119, 170)),
    ((90, 150), (65, 185), (50, 200)),
    ((164, 150), (189, 185), (204, 200)),
    ((120, 100), (125, 120), (118, 145)),
    ((134, 100), (129, 120), (136, 145)),
    ((100, 190), (115, 210), (127, 225)),
    ((154, 190), (139, 210), (127, 225)),
)


def _mix(color, tint, k):
    """两色线性混合（k=0 原色，k=1 全 tint；确定性整数运算）。"""
    return tuple(round(c + (t - c) * k) for c, t in zip(color[:3], tint)) + (color[3],)


def _scale(color, f):
    """明度缩放（夹在 0–255）。"""
    return tuple(max(0, min(255, round(c * f))) for c in color[:3]) + (color[3],)


def _palette(hull, seam, rim, rivet, accent, core):
    return {"hull": hull, "seam": seam, "rim": rim, "rivet": rivet, "accent": accent, "core": core}


_STD_HULL = (STD_HULL_A, STD_HULL_B, STD_HULL_C, STD_HULL_D)


def _tint_ratio(color, tint, k):
    """按 tint 的通道比例做相对偏移：**只改色相不改明度**——往暖色偏时压 G/B，暗板仍是暗板
    （用线性混合调色会把暗端抬亮，整机因此读成一块扁平的暖灰板）。"""
    mx = max(tint) or 1
    return tuple(
        max(0, min(255, round(c * (1.0 + k * (t / mx - 1.0))))) for c, t in zip(color[:3], tint)
    ) + (color[3],)


def _hull_ramp(tint, tint_k, dark, light):
    """四档甲板色：把标准型四档按 dark→light 重新标定亮度，再往 tint 偏一点铜调。

    **必须成套给**：标准型的 A→D 跨 3 倍明度，只整体提亮/压暗会让整机读成一块平灰板
    （板块层次全靠这四档拉开）；各机型的 dark/light 只决定「同族里偏亮还是偏暗」。"""
    return tuple(
        _tint_ratio(_scale(c, dark + (light - dark) * (i / 3.0)), tint, tint_k)
        for i, c in enumerate(_STD_HULL)
    )


# 五型同族配色：甲板色只在暖钛/青铜段内做明度与铜调偏移，能量一律琥珀（只动浓淡）。
# 标准型不在此表——它直接用 generate_player_sprite 自己的暖钛调色板（本模块不重绑即原样）。
# 游隼：同族里偏亮的暖钛钢（速度感），能量走线细而密
PALETTE_PEREGRINE = _palette(
    _hull_ramp((180, 150, 112), 0.40, 0.95, 1.34),
    STD_SEAM, _mix(STD_RIM, (255, 244, 226), 0.35), _mix(STD_RIVET, (198, 176, 146), 0.30),
    (255, 182, 70), (255, 228, 172),
)
# 重锤：深琥珀/橙铜（重甲），能量更浓
PALETTE_SLEDGE = _palette(
    _hull_ramp((150, 74, 26), 0.55, 0.90, 1.22),
    STD_SEAM, _mix(STD_RIM, (255, 214, 150), 0.30), _mix(STD_RIVET, (176, 126, 74), 0.45),
    (250, 142, 32), (255, 198, 116),
)
# 连弩：白热金（最亮），霓虹最多
PALETTE_REPEATER = _palette(
    _hull_ramp((228, 194, 134), 0.35, 1.16, 1.62),
    _mix(STD_SEAM, (44, 28, 12), 0.30), _mix(STD_RIM, (255, 250, 236), 0.55),
    _mix(STD_RIVET, (236, 214, 168), 0.45), (255, 200, 92), (255, 246, 216),
)
# 壁垒：暗铜褐（同族最深），以板缝为主、少霓虹
PALETTE_BULWARK = _palette(
    _hull_ramp((140, 88, 52), 0.45, 0.70, 1.12),
    _mix(STD_SEAM, (6, 4, 3), 0.40), _mix(STD_RIM, (228, 204, 166), 0.40),
    _mix(STD_RIVET, (156, 116, 78), 0.50), (238, 162, 56), (252, 210, 142),
)
# 巨像：青铜深钢（皮厚），能量标准琥珀
PALETTE_COLOSSUS = _palette(
    _hull_ramp((124, 100, 66), 0.40, 0.92, 1.28),
    STD_SEAM, _mix(STD_RIM, (240, 224, 190), 0.20), _mix(STD_RIVET, (166, 146, 112), 0.30),
    (255, 158, 40), (255, 208, 134),
)


def use_palette(p) -> None:
    """把一套暖系配色装进 `generate_player_sprite` 的模块级调色板。

    Ship 的图元方法在运行期查该模块的全局名，故逐机型重绑即可换色；PLAYER_OUTLINE /
    PLAYER_RIM 不在其中（玩家机一律暖描边 + 暖缘光，与敌方晶体族的冷色默认值区分）。"""
    player_sprite.HULL_A, player_sprite.HULL_B, player_sprite.HULL_C, player_sprite.HULL_D = p["hull"]
    player_sprite.SEAM = p["seam"]
    player_sprite.RIM = p["rim"]
    player_sprite.RIVET = p["rivet"]
    player_sprite.ACCENT = p["accent"]
    player_sprite.CORE = p["core"]


def _deck():
    """当前生效调色板的四档甲板色（A 最暗 → D 最亮）。"""
    return (player_sprite.HULL_A, player_sprite.HULL_B,
            player_sprite.HULL_C, player_sprite.HULL_D)


def _half(half_pts):
    """右半侧轮廓 → 闭合整幅多边形（左半侧镜像；用于沿中线的对称面/环带）。"""
    return list(half_pts) + [(254 - x, y) for x, y in reversed(half_pts)]


def _sym(s: Ship, half_pts, fill) -> None:
    """沿中线的对称面（机头亮面 / 腹板 / 环带）：给右半侧轮廓，自动补左半。"""
    s.facet(_half(half_pts), fill, mirror=False)


def _ring(s: Ship, y, hw, fill, h=9, shrink=1) -> None:
    """机身环带：横跨机背的一段横向板带（对称；colossus 的多段环带、repeater 的散热缝都用它）。"""
    _sym(s, [(CX, y), (CX + hw, y), (CX + hw - shrink, y + h), (CX, y + h)], fill)


def _cross(s: Ship, y, hw, kind="seam") -> None:
    """横跨整幅机身的一条线（seam 板缝 / rim 棱线）。"""
    getattr(s, kind)([(CX - hw, y), (CX + hw, y)], width=1, mirror=False)


def _root_glove(s: Ship, w0, w1, tuck_x, fill) -> None:
    """翼根整流罩（LERX）：把机翼前/后根点连进机身内部，消除窄机身与翼根之间的空缝。

    tuck_x 必须落在机身轮廓内（机身后画，会盖住这段），留出的可见部分即翼根过渡面。"""
    s.facet([w0, w1, (tuck_x, w1[1]), (tuck_x, w0[1])], fill)


def _thruster(s: Ship, cx, cy, rx, ry, particles=True) -> None:
    """喷口组：装甲环 + 内焰白芯 + 微粒子点（B 通道发光源）。"""
    s.nozzle_ring(cx, cy, rx, ry)
    s.engine(cx, cy, rx, ry)
    if particles:
        s.engine_particles(cx, cy + ry + 3)


def _thrusters(s: Ship, rx, ry, cy=230) -> None:
    """左右双发（喷口锚点 108/146, 230）。"""
    _thruster(s, 108, cy, rx, ry)
    _thruster(s, 146, cy, rx, ry)


def _nav_housing(s: Ship, y=179) -> None:
    """航灯座：翼面外段一小块深色底座 + 节点灯（挂点 (49/205, 179) 落在实体上）。"""
    s.greeble(44, y - 4, 9, 8)
    s.lamp(48.5, y, 1.6)
    s.lamp(48.5, y, 1.0, color=player_sprite.CORE)


def _vent_housing(s: Ship, y=173) -> None:
    """散热口座：脊线中后段的一对格栅（挂点 (117/137, 173) 落在实体上）。"""
    s.vent(114, y - 5, length=10, gap=3, n=3)
    s.greeble(113, y + 5, 11, 7, fill=player_sprite.HULL_B)


# --------------------------------------------------------------------------- #
# 一、游隼 peregrine：细长低阻——长尖机头、大后掠鸭翼、锐利长直线前缘、细密能量走线
# --------------------------------------------------------------------------- #
def peregrine() -> Ship:
    use_palette(PALETTE_PEREGRINE)
    A, B, C, D = _deck()
    s = Ship(254, 254)

    # ---- 主翼：薄前缘 + 翼根长边条（strake）+ 后缘锯齿，全薄 ----
    w0, w1 = (104, 114), (100, 174)
    s.facet([w0, (12, 206), (20, 209), w1], B)                     # 外翼面（薄，翼尖收成刀锋）
    s.facet([(104, 120), (36, 196), (48, 204), (100, 168)], C)     # 内翼承力面
    s.facet([(100, 126), (54, 188), (66, 196), (98, 162)], D)      # 内翼子面
    s.facet([(100, 168), (20, 210), (42, 222), (96, 192)], A)      # 翼根后缘（锯齿深切）
    _root_glove(s, w0, w1, 122, B)
    s.facet([(104, 114), (118, 138), (116, 170), (100, 174)], C)   # 翼根长边条（细长、带亮棱线）
    s.rim([(104, 114), (118, 138)])
    s.shade([(32, 210), (44, 218), (94, 194)], alpha=40)
    s.rim([w0, (12, 206)])                                         # 锐利前缘棱线（一条长直线）
    s.rim([(12, 206), (20, 209)])
    s.seam([(102, 150), (117, 140)], width=1)
    s.seam([(103, 120), (36, 198)])
    s.seam([(100, 172), (44, 206)])
    s.seam([(96, 138), (56, 194)], width=1)
    s.seam([(78, 200), (96, 186)], width=1)
    s.panel_dot(100, 142, r=1.2)
    s.panel_dot(96, 160, r=1.2)
    s.panel_dot(66, 192, r=1.2)
    s.panel_dot(80, 186, r=1.0)
    s.vent(60, 200, length=11, gap=3, n=2)
    s.greeble(84, 180, 6, 5)
    s.neon([(103, 116), (14, 205)], width=1)                       # 前缘细走线（细而密）
    s.neon([(101, 124), (38, 198)], width=1)
    s.neon([(32, 210), (96, 186)], width=1)
    s.neon([(72, 200), (94, 190)], width=1)
    s.neon([(106, 116), (117, 138)], width=1)                      # 边条走线
    s.lamp(104, 188, 1.3)
    s.lamp(76, 188, 1.2)
    _nav_housing(s)

    # ---- 鸭翼：加大成大后掠细长前翼（前缘一条直线收成尖点，根部埋进细机头） ----
    s.facet([(128, 62), (84, 96), (90, 106), (128, 94)], C)
    s.facet([(124, 72), (88, 96), (94, 104), (126, 92)], D)
    s.rim([(128, 64), (84, 96)])
    s.seam([(122, 74), (91, 102)], width=1)
    s.panel_dot(108, 86, r=1.1)
    s.neon([(92, 106), (86, 97)], width=2)                         # 鸭翼尖灯
    s.neon([(126, 68), (90, 96)], width=1)

    # ---- 尾翼（细长外八小垂尾，根部接翼根） ----
    s.facet([(104, 168), (86, 224), (96, 229), (106, 202)], B)
    s.facet([(102, 176), (90, 218), (95, 220), (104, 202)], C)
    s.seam([(102, 178), (90, 218)], width=1)
    s.seam([(104, 172), (88, 222)], width=1)
    s.panel_dot(98, 196, r=1.1)
    s.greeble(95, 206, 4, 5)
    s.neon([(88, 220), (94, 224)], width=2)

    # ---- 主机身：针状长机头（前段收成细杆，进气口处才放款）、脊线区收窄 ----
    prof = [(127, 16), (130, 48), (133, 72), (141, 120), (140, 168), (137, 206), (127, 236)]
    s.facet(_half(prof), A, mirror=False)
    s.facet(_half([(127, 16), (130, 48), (133, 72), (141, 120), (127, 138)]), D, mirror=False)
    s.facet(_half([(127, 20), (129, 48), (127, 92)]), C, mirror=False)               # 机头子面
    s.facet([(119, 112), (127, 134), (127, 236), (120, 206)], B, mirror=False)       # 左腹板（暗）
    s.facet([(141, 112), (127, 134), (127, 236), (137, 206)], C, mirror=False)       # 右腹板
    s.facet(_half([(127, 112), (135, 160), (133, 200)]), C, mirror=False)            # 窄脊线
    s.shade([(120, 210), (127, 232), (127, 236)], alpha=40, mirror=False)
    s.seam([(127, 134), (127, 236)], mirror=False)                                   # 中脊接缝
    s.seam([(119, 112), (127, 134)])
    s.rim([(127, 16), (130, 48)], mirror=False)
    s.rim([(127, 16), (124, 48)], mirror=False)
    s.rim([(130, 48), (133, 72)], mirror=False)
    s.rim([(124, 48), (121, 72)], mirror=False)
    s.seam([(127, 34), (129, 50)], width=1, mirror=False)
    s.seam([(127, 34), (125, 50)], width=1, mirror=False)
    s.seam([(127, 56), (130, 74)], width=1, mirror=False)
    s.seam([(127, 56), (124, 74)], width=1, mirror=False)
    s.seam([(120, 128), (134, 128)], width=1, mirror=False)
    s.seam([(121, 152), (135, 152)], width=1, mirror=False)
    s.seam([(122, 176), (136, 176)], width=1, mirror=False)
    s.panel_dot(133, 132, r=1.1)
    s.panel_dot(121, 132, r=1.1)
    s.panel_dot(132, 170, r=1.0)
    s.panel_dot(122, 170, r=1.0)
    s.panel_dot(130, 196, r=1.0)
    s.panel_dot(124, 196, r=1.0)
    s.greeble(130, 108, 5, 6)
    _vent_housing(s)

    # ---- 引擎舱（窄整流罩 + 双发） ----
    s.facet([(94, 190), (110, 184), (116, 226), (98, 234)], C)
    s.facet([(97, 194), (103, 192), (105, 226), (99, 230)], D)
    s.seam([(95, 200), (111, 196)], width=1)
    s.seam([(96, 214), (114, 211)], width=1)
    s.seam([(97, 224), (115, 221)], width=1)
    s.panel_dot(106, 194, r=1.1)
    s.panel_dot(104, 210, r=1.0)
    _thrusters(s, 6, 4)

    # ---- 座舱（窄长）+ 能量细节 ----
    s.canopy_frame(CX, 92, 7, 16)
    s.canopy(CX, 92, 7, 16)
    s.neon([(116, 128), (119, 198)], width=1)                                      # 侧舷能量缝（细）
    s.neon([(138, 128), (135, 198)], width=1)
    s.neon([(127, 122), (127, 200)], width=1, mirror=False)                        # 脊线走线
    s.neon([(123, 132), (125, 190)], width=1)
    s.neon([(131, 132), (129, 190)], width=1)
    s.lamp(CX, 24, 1.4, mirror=False)                                              # 机头信标
    s.lamp(CX, 106, 1.2, mirror=False)
    return s


# --------------------------------------------------------------------------- #
# 二、重锤 sledge：攻击力——钝厚机首、肩部炮荚舱、加厚翼内段、机腹加强肋、粗能量走线
# --------------------------------------------------------------------------- #
def sledge() -> Ship:
    use_palette(PALETTE_SLEDGE)
    A, B, C, D = _deck()
    s = Ship(254, 254)

    # ---- 主翼：内段加厚承力（短而厚） ----
    w0, w1 = (106, 120), (104, 186)
    s.facet([w0, (12, 206), (40, 222), w1], B)
    s.facet([(104, 124), (34, 198), (52, 210), (102, 172)], C)      # 内翼承力面（加厚）
    s.facet([(102, 130), (52, 192), (68, 202), (100, 164)], D)
    s.facet([(102, 176), (38, 220), (54, 228), (96, 200)], A)       # 翼根后段暗面
    _root_glove(s, w0, w1, 118, B)
    s.shade([(40, 216), (54, 224), (96, 198)], alpha=45)
    s.rim([w0, (12, 206)])
    s.rim([(12, 206), (40, 222)])
    s.seam([(105, 124), (36, 200)])
    s.seam([(102, 174), (44, 212)])
    s.seam([(100, 140), (58, 196)], width=1)
    s.seam([(84, 206), (98, 194)], width=1)
    s.seam([(60, 202), (92, 180)], width=1)                        # 承力梁板缝
    s.panel_dot(101, 146, r=1.4)
    s.panel_dot(97, 164, r=1.4)
    s.panel_dot(74, 190, r=1.3)
    s.panel_dot(88, 182, r=1.2)
    s.vent(62, 206, length=14, gap=3, n=3)
    s.greeble(80, 184, 8, 7)
    s.neon([(105, 122), (15, 205)], width=3)                       # 粗能量走线
    s.neon([(102, 174), (42, 212)], width=2)
    s.neon([(66, 204), (96, 186)], width=2)
    s.lamp(103, 190, 1.6)
    s.lamp(70, 194, 1.4)
    _nav_housing(s)

    # ---- 肩部炮荚舱：落在机炮挂点 (97/157, 127) 上的凸起 ----
    s.facet([(88, 116), (104, 110), (111, 130), (107, 152), (90, 156)], C)
    s.facet([(92, 118), (104, 114), (108, 130), (101, 138)], D)
    s.facet([(89, 140), (107, 138), (107, 150), (90, 152)], B)
    s.seam([(105, 112), (110, 130)])
    s.seam([(90, 132), (108, 130)], width=1)
    s.seam([(89, 146), (107, 144)], width=1)
    s.rim([(104, 110), (108, 130)])
    s.facet([(93, 112), (98, 102), (103, 112)], B)                 # 炮口短管
    s.seam([(96, 106), (99, 112)], width=1)
    s.panel_dot(95, 124, r=1.3)
    s.panel_dot(95, 146, r=1.2)
    s.neon([(91, 120), (94, 150)], width=2)
    s.lamp(97, 114, 1.5)

    # ---- 鸭翼（短厚） ----
    s.facet([(112, 82), (84, 96), (92, 108), (112, 100)], C)
    s.facet([(108, 88), (90, 98), (96, 104), (108, 98)], D)
    s.rim([(112, 82), (84, 96)])
    s.seam([(108, 86), (92, 102)], width=1)
    s.panel_dot(102, 96, r=1.2)
    s.neon([(94, 104), (87, 97)], width=2)

    # ---- 尾翼（短厚） ----
    s.facet([(103, 170), (80, 226), (94, 232), (106, 206)], B)
    s.facet([(101, 178), (86, 220), (92, 222), (103, 204)], C)
    s.seam([(101, 180), (87, 220)], width=1)
    s.seam([(103, 174), (83, 224)], width=1)
    s.panel_dot(98, 198, r=1.3)
    s.greeble(94, 206, 5, 6)
    s.neon([(83, 224), (92, 228)], width=2)

    # ---- 主机身：机首加粗加厚（钝锥） ----
    prof = [(127, 16), (138, 44), (146, 96), (146, 158), (143, 206), (127, 236)]
    s.facet(_half(prof), A, mirror=False)
    s.facet(_half([(127, 16), (138, 44), (146, 96), (127, 116)]), D, mirror=False)
    s.facet(_half([(127, 24), (136, 48), (127, 100)]), C, mirror=False)
    s.facet(_half([(127, 108), (144, 160), (141, 208), (127, 224)]), B, mirror=False)  # 腹板（厚）
    s.facet([(112, 96), (127, 116), (127, 236), (117, 208)], A, mirror=False)
    s.facet([(146, 96), (127, 116), (127, 236), (143, 208)], B, mirror=False)
    s.shade([(122, 212), (127, 232), (127, 236)], alpha=45, mirror=False)
    s.seam([(127, 116), (127, 236)], mirror=False)
    s.seam([(112, 96), (127, 116)])
    s.rim([(127, 16), (138, 44)], mirror=False)
    s.rim([(127, 16), (116, 44)], mirror=False)
    s.rim([(138, 44), (146, 96)], mirror=False)
    s.rim([(116, 44), (108, 96)], mirror=False)
    # 机首加强环带 + 侧舷板缝
    _cross(s, 52, 21)
    _cross(s, 52, 21, kind="rim")
    _cross(s, 74, 19)
    _cross(s, 120, 19)
    _cross(s, 120, 19, kind="rim")
    s.seam([(118, 140), (139, 140)], width=1, mirror=False)
    s.panel_dot(132, 132, r=1.4)
    s.panel_dot(122, 132, r=1.4)
    s.panel_dot(134, 168, r=1.3)
    s.panel_dot(120, 168, r=1.3)
    s.greeble(130, 104, 7, 8)
    # 机腹加强肋（三道横肋 + 铆钉列）
    for i, y in enumerate((178, 198, 218)):
        hw = 17 - i * 3
        _ring(s, y, hw, B, h=7)
        _cross(s, y, hw)
        _cross(s, y + 7, hw - 1)
        s.panel_dot(CX + hw - 3, y + 3, r=1.3, mirror=False)
        s.panel_dot(CX - hw + 3, y + 3, r=1.3, mirror=False)
    _vent_housing(s)

    # ---- 引擎舱（加厚整流罩） ----
    s.facet([(92, 188), (112, 180), (118, 226), (96, 234)], C)
    s.facet([(95, 192), (106, 188), (109, 226), (98, 230)], D)
    s.seam([(93, 198), (113, 194)], width=1)
    s.seam([(94, 212), (116, 209)], width=1)
    s.seam([(95, 224), (117, 221)], width=1)
    s.panel_dot(107, 190, r=1.3)
    s.panel_dot(104, 208, r=1.2)
    _thrusters(s, 6.5, 4.5)

    # ---- 座舱（宽厚）+ 能量 ----
    s.canopy_frame(CX, 92, 10, 17)
    s.canopy(CX, 92, 10, 17)
    s.neon([(115, 126), (118, 196)], width=3)
    s.neon([(139, 126), (136, 196)], width=3)
    s.neon([(127, 122), (127, 200)], width=2, mirror=False)
    s.lamp(CX, 26, 1.8, mirror=False)
    return s


# --------------------------------------------------------------------------- #
# 三、连弩 repeater：攻击速度——机首并列双炮管、翼面散热格栅密布、机身环绕散热缝、
#    尾部多发喷口节、白热金 + 最多霓虹
# --------------------------------------------------------------------------- #
def repeater() -> Ship:
    use_palette(PALETTE_REPEATER)
    A, B, C, D = _deck()
    s = Ship(254, 254)

    # ---- 主翼：散热格栅密布 ----
    w0, w1 = (104, 118), (102, 178)
    s.facet([w0, (12, 206), (34, 218), w1], B)
    s.facet([(104, 124), (30, 198), (44, 206), (102, 170)], C)
    s.facet([(102, 130), (44, 190), (58, 198), (100, 162)], D)
    s.facet([(102, 172), (32, 216), (46, 222), (98, 194)], A)
    _root_glove(s, w0, w1, 120, B)
    s.shade([(36, 214), (46, 220), (96, 196)], alpha=40)
    s.rim([w0, (12, 206)])
    s.rim([(12, 206), (34, 218)])
    s.seam([(103, 122), (30, 200)])
    s.seam([(102, 174), (44, 208)])
    s.seam([(100, 138), (50, 194)], width=1)
    s.panel_dot(101, 144, r=1.2)
    s.panel_dot(97, 162, r=1.2)
    # 翼面散热格栅阵（4 组）+ 后缘格栅列
    s.vent(76, 176, length=9, gap=3, n=3)
    s.vent(62, 194, length=9, gap=3, n=3)
    s.vent(50, 206, length=8, gap=3, n=2)
    s.vent(88, 158, length=7, gap=3, n=3)
    s.vent(34, 210, length=12, gap=3, n=2)
    s.greeble(80, 182, 6, 6)
    s.greeble(66, 198, 5, 5)
    s.neon([(103, 120), (14, 205)], width=2)
    s.neon([(101, 128), (34, 202)], width=2)
    s.neon([(30, 212), (96, 186)], width=1)
    s.neon([(66, 202), (94, 190)], width=1)
    s.lamp(103, 188, 1.4)
    s.lamp(74, 190, 1.3)
    s.lamp(96, 172, 1.2)
    _nav_housing(s)

    # ---- 鸭翼（带格栅） ----
    s.facet([(112, 82), (84, 96), (92, 108), (112, 100)], C)
    s.facet([(108, 88), (90, 98), (96, 104), (108, 98)], D)
    s.rim([(112, 82), (84, 96)])
    s.seam([(108, 86), (92, 102)], width=1)
    s.vent(98, 92, length=7, gap=3, n=2)
    s.neon([(94, 104), (86, 97)], width=2)

    # ---- 尾翼（多发喷口节的前置支撑） ----
    s.facet([(103, 170), (82, 226), (94, 232), (106, 206)], B)
    s.facet([(101, 178), (88, 220), (93, 222), (103, 204)], C)
    s.seam([(101, 180), (89, 220)], width=1)
    s.seam([(103, 174), (85, 224)], width=1)
    s.vent(96, 208, length=6, gap=3, n=2)
    s.neon([(84, 224), (92, 228)], width=2)

    # ---- 主机身：脊线两侧并列双炮管 ----
    prof = [(127, 16), (133, 34), (139, 68), (142, 96), (143, 160), (140, 206), (127, 236)]
    s.facet(_half(prof), A, mirror=False)
    s.facet(_half([(127, 16), (134, 52), (141, 96), (127, 114)]), D, mirror=False)
    s.facet([(112, 96), (127, 114), (127, 236), (117, 206)], B, mirror=False)
    s.facet([(141, 96), (127, 114), (127, 236), (139, 206)], C, mirror=False)
    s.facet(_half([(127, 104), (136, 152), (134, 202)]), C, mirror=False)
    s.shade([(122, 210), (127, 232), (127, 236)], alpha=40, mirror=False)
    s.seam([(127, 114), (127, 236)], mirror=False)
    s.seam([(112, 96), (127, 114)])
    s.rim([(127, 16), (133, 34)], mirror=False)
    s.rim([(127, 16), (121, 34)], mirror=False)
    # 机首双炮管：脊线两侧各一根**伸出机头**的短管（镜像自动成对），中缝留出机鼻脊线
    s.facet([(118, 26), (121, 16), (126, 16), (126, 60), (118, 66)], C)             # 管身
    s.facet([(119.5, 28), (122.5, 19), (124.6, 19), (124.6, 56)], D)                # 管身高光
    s.seam([(118, 34), (126, 34)], width=1)
    s.seam([(118, 50), (126, 50)], width=1)
    s.facet([(119, 16), (126, 16), (126, 23), (119, 23)], player_sprite.SEAM)       # 炮口（暗）
    s.rim([(118, 26), (121, 16)])                                                   # 炮口外缘棱线
    s.seam([(127, 12), (127, 74)], width=1, mirror=False)                           # 双管中缝
    s.neon([(123, 17), (123, 19)], width=3)                                         # 炮口焰心
    s.lamp(123, 17, 1.6)
    # 机身环绕散热缝（五道；缝长随机身收窄，霓虹隔道点，避免读成等距梯子）
    for i, y in enumerate((116, 134, 152, 172, 192)):
        hw = 15 - (i // 2)
        _cross(s, y, hw)
        if i % 2 == 0:
            s.neon([(CX - hw + 3, y + 4), (CX + hw - 3, y + 4)], width=1, mirror=False)
        else:
            s.vent(116, y + 3, length=9, gap=3, n=2)
            s.greeble(129, y + 2, 6, 5)
    s.panel_dot(134, 126, r=1.2)
    s.panel_dot(120, 126, r=1.2)
    s.panel_dot(135, 164, r=1.2)
    s.panel_dot(119, 164, r=1.2)
    s.greeble(131, 104, 6, 6)
    _vent_housing(s)

    # ---- 引擎舱：多发喷口节（三道节环 + 尾板格栅） ----
    s.facet([(93, 188), (111, 182), (117, 228), (97, 234)], C)
    s.facet([(96, 192), (104, 188), (107, 228), (99, 231)], D)
    s.seam([(94, 196), (112, 192)], width=1)
    s.seam([(95, 206), (115, 203)], width=1)
    s.seam([(96, 216), (116, 213)], width=1)
    s.seam([(96, 226), (117, 223)], width=1)
    s.vent(100, 218, length=8, gap=3, n=2)
    s.panel_dot(105, 194, r=1.1)
    _thrusters(s, 6, 4)

    # ---- 座舱（窄）+ 能量 ----
    s.canopy_frame(CX, 92, 8, 16)
    s.canopy(CX, 92, 8, 16)
    s.neon([(116, 130), (119, 196)], width=2)
    s.neon([(138, 130), (135, 196)], width=2)
    s.neon([(127, 120), (127, 204)], width=1, mirror=False)
    s.neon([(122, 128), (124, 188)], width=1)
    s.neon([(132, 128), (130, 188)], width=1)
    s.lamp(CX, 24, 1.5, mirror=False)
    return s


# --------------------------------------------------------------------------- #
# 四、壁垒 bulwark：防御——前缘加切角装甲板、小内嵌座舱、盾形分段甲片、机首撞角带、
#    暗铜褐 + 以板缝为主少霓虹
# --------------------------------------------------------------------------- #
def bulwark() -> Ship:
    use_palette(PALETTE_BULWARK)
    A, B, C, D = _deck()
    s = Ship(254, 254)

    # ---- 主翼：前缘整条切角装甲板 + 盾形分段甲片 ----
    w0, w1 = (106, 118), (104, 182)
    s.facet([w0, (12, 206), (38, 220), w1], B)
    s.facet([(104, 126), (36, 198), (52, 208), (102, 174)], C)
    s.facet([(102, 132), (50, 190), (66, 200), (100, 166)], D)
    s.facet([(102, 174), (36, 218), (52, 226), (98, 198)], A)
    _root_glove(s, w0, w1, 120, B)
    s.shade([(38, 214), (52, 222), (96, 196)], alpha=45)
    s.rim([w0, (12, 206)])
    s.rim([(12, 206), (38, 220)])
    # 前缘装甲带（切角板）：沿前缘一条带宽板 + 五道切角肋
    s.facet([(106, 114), (12, 202), (26, 214), (116, 128)], C)
    s.facet([(106, 116), (14, 204), (22, 210), (112, 124)], D)
    s.rim([(106, 112), (12, 200)])
    for i in range(5):
        u = 0.13 + i * 0.19
        x0 = 106 + (12 - 106) * u
        y0 = 114 + (204 - 114) * u
        s.seam([(x0, y0), (x0 + 11, y0 + 11)], width=1)
    s.neon([(104, 120), (16, 203)], width=1)
    s.panel_dot(96, 152, r=1.2)
    s.panel_dot(78, 174, r=1.2)
    s.panel_dot(60, 194, r=1.2)
    # 盾形分段甲片（两块）
    s.facet([(98, 142), (74, 178), (84, 192), (98, 162)], D)
    s.seam([(98, 142), (74, 178)], width=1)
    s.seam([(74, 178), (84, 192)], width=1)
    s.seam([(84, 192), (98, 162)], width=1)
    s.seam([(98, 162), (98, 142)], width=1)
    s.facet([(64, 196), (40, 208), (48, 216), (72, 206)], C)
    s.seam([(64, 196), (40, 208)], width=1)
    s.seam([(40, 208), (48, 216)], width=1)
    s.seam([(48, 216), (72, 206)], width=1)
    s.greeble(80, 184, 7, 6)
    s.vent(58, 206, length=11, gap=3, n=2)
    s.neon([(105, 122), (16, 204)], width=1)
    s.neon([(38, 214), (96, 190)], width=1)
    s.lamp(103, 190, 1.3)
    _nav_housing(s)

    # ---- 鸭翼（厚钝） ----
    s.facet([(112, 84), (84, 96), (92, 106), (112, 100)], C)
    s.facet([(108, 88), (90, 98), (96, 103), (108, 98)], D)
    s.rim([(112, 84), (84, 96)])
    s.seam([(108, 88), (92, 102)], width=1)
    s.panel_dot(101, 96, r=1.2)

    # ---- 尾翼 ----
    s.facet([(103, 170), (80, 226), (94, 232), (106, 206)], B)
    s.facet([(101, 178), (86, 220), (92, 222), (103, 204)], C)
    s.seam([(101, 180), (87, 220)], width=1)
    s.seam([(103, 174), (83, 224)], width=1)
    s.panel_dot(98, 196, r=1.2)
    s.greeble(94, 206, 5, 6)

    # ---- 主机身：短钝撞角机首 + 小内嵌座舱 ----
    prof = [(127, 16), (134, 34), (143, 64), (146, 96), (144, 156), (141, 206), (127, 236)]
    s.facet(_half(prof), A, mirror=False)
    s.facet(_half([(127, 16), (134, 34), (143, 64), (146, 96), (127, 116)]), D, mirror=False)
    s.facet(_half([(127, 22), (133, 40), (127, 96)]), C, mirror=False)
    s.facet([(114, 96), (127, 116), (127, 236), (118, 206)], A, mirror=False)
    s.facet([(146, 96), (127, 116), (127, 236), (140, 206)], B, mirror=False)
    s.shade([(121, 210), (127, 232), (127, 236)], alpha=50, mirror=False)
    s.seam([(127, 116), (127, 236)], mirror=False)
    s.seam([(114, 96), (127, 116)])
    s.rim([(127, 16), (134, 34)], mirror=False)
    s.rim([(127, 16), (120, 34)], mirror=False)
    s.rim([(134, 34), (143, 64)], mirror=False)
    s.rim([(120, 34), (111, 64)], mirror=False)
    # 撞角装甲带（机首一道厚 V 形板，读作「拿去撞」）
    _sym(s, [(127, 24), (136, 42), (140, 68), (132, 60), (127, 40)], C)
    s.rim([(127, 22), (136, 40)], mirror=False)
    s.rim([(127, 22), (118, 40)], mirror=False)
    s.seam([(127, 42), (134, 60)], width=2, mirror=False)
    s.seam([(127, 42), (120, 60)], width=2, mirror=False)
    # 机身板缝（以缝为主）
    _cross(s, 74, 18)
    _cross(s, 118, 20)
    _cross(s, 138, 20)
    _cross(s, 158, 19)
    _cross(s, 178, 17)
    _cross(s, 198, 15)
    s.panel_dot(133, 128, r=1.2)
    s.panel_dot(121, 128, r=1.2)
    s.panel_dot(134, 166, r=1.2)
    s.panel_dot(120, 166, r=1.2)
    s.panel_dot(131, 192, r=1.1)
    s.panel_dot(123, 192, r=1.1)
    s.greeble(131, 106, 6, 7)
    _vent_housing(s)

    # ---- 引擎舱 ----
    s.facet([(93, 188), (111, 182), (117, 228), (97, 234)], C)
    s.facet([(96, 192), (104, 188), (107, 228), (99, 231)], D)
    s.seam([(94, 198), (112, 194)], width=1)
    s.seam([(95, 212), (115, 209)], width=1)
    s.seam([(96, 224), (116, 221)], width=1)
    s.panel_dot(106, 192, r=1.2)
    _thrusters(s, 6.5, 4.5)

    # ---- 座舱：更小更内嵌（钢甲框缘加厚） ----
    s.bd.ellipse(s.p([(CX - 11, 92 - 15), (CX + 11, 92 + 15)]), fill=player_sprite.SEAM)
    s.bd.ellipse(s.p([(CX - 9, 92 - 13), (CX + 9, 92 + 13)]), fill=D)
    s.canopy(CX, 92, 6, 11)
    s.neon([(118, 128), (120, 194)], width=1)
    s.neon([(136, 128), (134, 194)], width=1)
    s.lamp(CX, 26, 1.4, mirror=False)
    return s


# --------------------------------------------------------------------------- #
# 五、巨像 colossus：血量上限——最宽最厚机身、腹板外凸、双引擎整流罩加大、
#    翼根承力厚重、机背多段环带、铆钉最密
# --------------------------------------------------------------------------- #
def colossus() -> Ship:
    use_palette(PALETTE_COLOSSUS)
    A, B, C, D = _deck()
    s = Ship(254, 254)

    # ---- 主翼：翼根承力结构厚重（深弦短展） ----
    w0, w1 = (106, 112), (104, 190)
    s.facet([w0, (12, 206), (48, 228), w1], B)
    s.facet([(104, 128), (36, 200), (58, 214), (102, 178)], C)
    s.facet([(102, 136), (56, 194), (74, 206), (100, 172)], D)
    s.facet([(102, 178), (40, 224), (58, 232), (98, 204)], A)
    # 翼根承力梁（厚重楔形 + 两道加强缝）
    s.facet([(104, 120), (76, 176), (94, 194), (104, 156)], D)
    s.seam([(104, 120), (76, 176)], width=1)
    s.seam([(76, 176), (94, 194)], width=1)
    s.seam([(94, 194), (104, 156)], width=1)
    _root_glove(s, w0, w1, 116, B)
    s.shade([(42, 222), (58, 228), (96, 200)], alpha=45)
    s.rim([w0, (12, 206)])
    s.rim([(12, 206), (48, 228)])
    s.seam([(105, 126), (38, 202)])
    s.seam([(102, 180), (48, 218)])
    s.seam([(100, 146), (62, 198)], width=1)
    s.seam([(90, 210), (99, 198)], width=1)
    s.panel_dot(100, 152, r=1.5)
    s.panel_dot(96, 170, r=1.5)
    s.panel_dot(84, 192, r=1.4)
    s.panel_dot(70, 204, r=1.4)
    s.panel_dot(90, 182, r=1.3)
    s.vent(64, 210, length=14, gap=3, n=3)
    s.greeble(82, 188, 8, 7)
    s.neon([(105, 124), (15, 205)], width=3)
    s.neon([(102, 178), (46, 220)], width=2)
    s.lamp(103, 192, 1.6)
    _nav_housing(s)

    # ---- 鸭翼（宽厚） ----
    s.facet([(112, 82), (84, 96), (92, 110), (112, 100)], C)
    s.facet([(108, 88), (90, 98), (96, 105), (108, 98)], D)
    s.rim([(112, 82), (84, 96)])
    s.seam([(108, 86), (92, 104)], width=1)
    s.panel_dot(101, 96, r=1.3)
    s.neon([(94, 106), (86, 97)], width=2)

    # ---- 尾翼（厚） ----
    s.facet([(102, 170), (78, 226), (94, 234), (106, 206)], B)
    s.facet([(100, 178), (84, 220), (92, 224), (103, 204)], C)
    s.seam([(100, 180), (85, 220)], width=1)
    s.seam([(102, 174), (81, 224)], width=1)
    s.panel_dot(97, 196, r=1.4)
    s.greeble(93, 206, 5, 6)
    s.neon([(82, 224), (91, 229)], width=2)

    # ---- 主机身：最宽最厚 + 腹板外凸 ----
    prof = [(127, 16), (137, 52), (151, 96), (150, 158), (141, 200), (127, 236)]
    s.facet(_half(prof), A, mirror=False)
    s.facet(_half([(127, 16), (137, 52), (151, 96), (127, 118)]), D, mirror=False)
    s.facet(_half([(127, 26), (135, 56), (127, 102)]), C, mirror=False)
    s.facet(_half([(127, 110), (150, 158), (141, 200), (127, 220)]), B, mirror=False)
    s.facet([(104, 96), (127, 118), (127, 236), (112, 202)], A, mirror=False)
    s.facet([(151, 96), (127, 118), (127, 236), (141, 202)], B, mirror=False)
    # 腹板厚实外凸：两侧厚板（外缘伸出轮廓 + 棱线）
    s.facet([(104, 150), (100, 206), (112, 232), (127, 236), (127, 150)], C)
    s.rim([(104, 150), (100, 206)])
    s.rim([(100, 206), (112, 232)])
    s.seam([(107, 156), (104, 206)], width=1)
    s.seam([(110, 176), (107, 208)], width=1)
    s.panel_dot(106, 164, r=1.4)
    s.panel_dot(105, 186, r=1.4)
    s.panel_dot(108, 206, r=1.3)
    s.shade([(122, 212), (127, 232), (127, 236)], alpha=45, mirror=False)
    s.seam([(127, 118), (127, 236)], mirror=False)
    s.seam([(104, 96), (127, 118)])
    s.rim([(127, 16), (137, 52)], mirror=False)
    s.rim([(127, 16), (117, 52)], mirror=False)
    s.rim([(137, 52), (151, 96)], mirror=False)
    s.rim([(117, 52), (103, 96)], mirror=False)
    # 机背多段环带（四段，宽度不一，读作「皮厚」）+ 密集铆钉
    for i, (y, hw, h) in enumerate(((112, 23, 12), (132, 22, 10), (152, 21, 10), (172, 18, 9))):
        _ring(s, y, hw, C if i % 2 == 0 else B, h=h, shrink=2)
        _cross(s, y, hw)
        s.panel_dot(CX + hw - 4, y + h // 2, r=1.4, mirror=False)
        s.panel_dot(CX - hw + 4, y + h // 2, r=1.4, mirror=False)
    _ring(s, 196, 15, B, h=8, shrink=2)
    _cross(s, 196, 15)
    _cross(s, 128, 18)
    s.panel_dot(134, 126, r=1.4)
    s.panel_dot(120, 126, r=1.4)
    s.panel_dot(136, 166, r=1.4)
    s.panel_dot(118, 166, r=1.4)
    s.greeble(131, 104, 7, 8)
    s.greeble(128, 224, 6, 7, mirror=False)
    _vent_housing(s)

    # ---- 引擎舱：双发整流罩加大 ----
    s.facet([(88, 184), (114, 176), (122, 232), (94, 236)], C)
    s.facet([(92, 190), (108, 184), (112, 230), (98, 234)], D)
    s.seam([(90, 196), (116, 190)], width=1)
    s.seam([(91, 210), (118, 205)], width=1)
    s.seam([(92, 224), (120, 219)], width=1)
    s.seam([(94, 234), (121, 229)], width=1)
    s.panel_dot(110, 188, r=1.5)
    s.panel_dot(106, 206, r=1.4)
    s.panel_dot(102, 224, r=1.4)
    _thrusters(s, 7, 5)

    # ---- 座舱（宽厚舱盖）+ 能量 ----
    s.canopy_frame(CX, 92, 11, 18)
    s.canopy(CX, 92, 11, 18)
    s.neon([(114, 126), (118, 200)], width=3)
    s.neon([(140, 126), (136, 200)], width=3)
    s.neon([(127, 122), (127, 206)], width=2, mirror=False)
    s.lamp(CX, 24, 1.8, mirror=False)
    return s


# ---- 名册（顺序 = 面板行顺序；stem 与 MachineRoster.SpriteStem 同源） ----
MACHINES = (
    ("player_ship_peregrine", peregrine),
    ("player_ship_sledge", sledge),
    ("player_ship_repeater", repeater),
    ("player_ship_bulwark", bulwark),
    ("player_ship_colossus", colossus),
)


def _mirror_pts(pts):
    return [(254 - x, y) for x, y in pts]


def _sample_path(points, step=2.0):
    """沿折线等距取样（含端点；确定性，无随机）。"""
    out = []
    for i in range(len(points) - 1):
        (x0, y0), (x1, y1) = points[i], points[i + 1]
        span = max(abs(x1 - x0), abs(y1 - y0))
        n = max(int(span / step), 1)
        for k in range(n + 1):
            t = k / n
            out.append((round(x0 + (x1 - x0) * t), round(y0 + (y1 - y0) * t)))
    return out


def _deck_ratio(alpha, x, y, r=4):
    """点周围 (2r+1)² 邻域里满格像素的占比。

    只看目标像素会被「画在空中的细线」骗过：棱线 / 霓虹线本身 alpha 满，sprite_polish 的描边
    还会把它外扩 2px——**一条从挂点穿过的 1px 亮线就足以让挂点判绿**，运行期挂件照样飘在
    空气里。邻域占比把「点落在实体甲板上」与「点搭在一根线上」分开。"""
    solid = total = 0
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            total += 1
            if alpha.getpixel((x + dx, y + dy)) >= 250:
                solid += 1
    return solid / total


def _assert_sprite(path, anchors=True):
    """落盘后护栏：画布 254×254（全部四张）+ 九锚点/八挂点处的实体甲板（本体帧）。

    形状悄悄坏掉（轮廓改错、机身变窄、挂点落空）时这里直接失败——挂件会飘在空气里，
    运行期零报错，肉眼也未必看得出来。判据分三档，各按该点的语义取：
      - 九锚点：alpha 满即可——它们本就是轮廓极值点（机头尖端、翼尖），邻域必然含空；
      - 八挂点：alpha 满 **且** 9×9 邻域满格率 ≥ 0.75——运行期挂件要落在这一片甲板上；
        （窗口取得比描边外扩量（2px）大得多：2px 细线连同描边约 6px 宽，9×9 窗口下占比
        ≈0.67 会被拦下，而六型实测的真甲板最低 0.83，判据踩在两者之间）
      - 受击帧裂纹路径：alpha ≥ 250——只要求「不飘在机体外」；这一档不能加邻域判据，
        否则标准型自己也会被判红（它的 hit 帧裂纹本就跨过机翼与引擎舱之间那道 4–6px 的缝）。
    受击帧的甲板被裂纹与暗淡覆盖，故锚点判据只在**本体**帧上生效，其余三张只判画布。"""
    img = Image.open(path).convert("RGBA")
    name = os.path.basename(path)
    if img.size != (254, 254):
        raise AssertionError(f"{name}: 画布 {img.size} ≠ (254, 254)")
    if not anchors:
        print(f"    [自检] {name} 画布 254×254")
        return
    alpha = img.getchannel("A")
    bad = []
    for label, pt in SKELETON.items():
        for x, y in (pt, _mirror_pts([pt])[0]):
            if alpha.getpixel((x, y)) < 255:
                bad.append(f"{label}({x},{y})={alpha.getpixel((x, y))}")
    for label, pt in HARDPOINTS.items():
        for x, y in (pt, _mirror_pts([pt])[0]):
            ratio = _deck_ratio(alpha, x, y)
            if alpha.getpixel((x, y)) < 255 or ratio < 0.75:
                bad.append(f"{label}({x},{y})=alpha{alpha.getpixel((x, y))}/邻域{ratio:.2f}")
    if bad:
        raise AssertionError(f"{name}: 锚点/挂点没有实体甲板 → {', '.join(bad)}")
    far = []
    for path_pts in DAMAGE_PATHS:
        for x, y in _sample_path(path_pts):
            if alpha.getpixel((x, y)) < 250:
                far.append(f"({x},{y})={alpha.getpixel((x, y))}")
    if far:
        raise AssertionError(f"{name}: 受击帧裂纹会飘在机体外 → {', '.join(far[:8])}")
    print(f"    [自检] {name} 画布 254×254 · 锚点满格 · 挂点邻域甲板 · 裂纹落在机体上")


def main() -> None:
    sprite_dir = os.path.normpath(
        os.path.join(os.path.dirname(__file__), "..", "..", "assets", "sprites")
    )
    for stem, build in MACHINES:
        normal = os.path.join(sprite_dir, stem + ".png")
        build().finish(normal)
        build().finish_glow(os.path.join(sprite_dir, stem + "_glow.png"))
        hit1 = apply_hit_1(build())
        hit1.finish(os.path.join(sprite_dir, stem + "_hit_1.png"))
        apply_hit_2(hit1).finish(os.path.join(sprite_dir, stem + "_hit_2.png"))
        _assert_sprite(normal)
        for suffix in ("_glow", "_hit_1", "_hit_2"):
            _assert_sprite(os.path.join(sprite_dir, stem + suffix + ".png"), anchors=False)
    print("==> 五型 20 张贴图生成完毕，锚点自检通过。")


if __name__ == "__main__":
    try:
        main()
    except AssertionError as exc:
        print(f"错误: 贴图自检失败 —— {exc}", file=sys.stderr)
        sys.exit(1)
