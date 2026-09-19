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

**每型一件「看剪影就认得出」的专属特征**（机型面板 1.1× 与标题屏悬挂 2× 是主要观看面，
特征一律做到 ≥8 贴图像素、带自己的棱线与板缝，不做只能凑近看的碎花）：

| 机型 | 剪影 | 专属特征 |
| --- | --- | --- |
| 游隼 | 细长薄刃、后缘三段锯齿 | 翼尖外挂短舱（探针 + 舱体）、翼根长边条、一条长直线前缘 |
| 重锤 | 短展厚弦、截角翼尖 | 肩部双联重炮（伸出机首）、翼下六管火箭巢、机首撞角犁 |
| 连弩 | 折线前缘（内陡外缓） | 机首并列双炮管（冷却套 + 炮口制退器）、翼根弹鼓 + 弹链、密布散热格栅 |
| 壁垒 | 截角方翼尖、最钝 | 前缘五段叠层装甲带、翼面盾形分段甲片、机背大盾板、内嵌式小座舱 |
| 巨像 | 最宽最厚、深弦宽翼 | 翼下八角外挂装甲舱、机背五段环带、侧舷装甲舱、加大双发整流罩 |

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


def _tail_fairing(s: Ship, hw, y0=194, y1=232, fill=None) -> None:
    """尾部整流板：把两侧引擎舱与机身连成一体（尾段因此读作一整块，而不是两条吊腿）。

    同时承接受击帧的尾部裂纹——裂纹路径 (115,210)→(127,225) 与 (139,210)→(127,225)
    落在这一片上，机型把尾段收得太窄时裂纹会飘在机体外（生成器自检会拦下）。"""
    s.facet(_half([(127, y0), (127 + hw, y0 + 3), (127 + hw - 5, y1), (127, y1 + 4)]),
            fill or _deck()[1], mirror=False)


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


# ---- 图元助手（五型共用的「外观语汇」；只调用 Ship 既有方法，不动标准型绘制）----


def _band(s: Ship, x, y, w, h, fill, mirror=True) -> None:
    """一段矩形甲带（外挂装甲块 / 翼面叠片：给外框板缝与内芯亮面）。"""
    s.greeble(x, y, w, h, fill=fill, mirror=mirror)
    s.greeble(x + 1, y + 1, w - 2, max(h - 2, 1), fill=_deck()[3], mirror=mirror, outline=False)


def _bolt_row(s: Ship, x, y0, y1, n, r=1.1, mirror=True) -> None:
    """等距铆钉列（沿纵向；确定性排布）。"""
    for i in range(n):
        y = y0 + (y1 - y0) * (i / max(n - 1, 1))
        s.panel_dot(x, y, r=r, mirror=mirror)


def _grille(s: Ship, x, y, w, h, bars=4, fill=None) -> None:
    """格栅板：外框 + 竖条（散热 / 进气语汇；比 s.vent 的横线更适合窄面）。"""
    s.greeble(x, y, w, h, fill=fill or _deck()[0])
    step = w / (bars + 1)
    for i in range(bars):
        bx = x + step * (i + 1)
        s.bd.line(s.p([(bx, y + 1), (bx, y + h - 1)]), fill=player_sprite.RIVET, width=player_sprite.S)


def _decal_bars(s: Ship, x, y, n=3, w=10, h=2, gap=3) -> None:
    """机身编号条（短横列）：给平面甲板一个「这里有编制」的读数，不引入文字。"""
    for i in range(n):
        s.bd.rectangle(s.p([(x, y + i * (h + gap)), (x + w, y + i * (h + gap) + h)]),
                       fill=player_sprite.RIVET)
        s.bd.rectangle(s.p([(254 - x - w, y + i * (h + gap)), (254 - x, y + i * (h + gap) + h)]),
                       fill=player_sprite.RIVET)


def _chevron(s: Ship, x, y, w, h, kind="seam") -> None:
    """V 形标识（翼面朝向语汇；kind 取 seam 暗刻 / rim 亮刻）。"""
    getattr(s, kind)([(x, y), (x + w * 0.5, y + h), (x + w, y)], width=1)


# --------------------------------------------------------------------------- #
# 一、游隼 peregrine：细长低阻——针状长机头、薄刃大后掠主翼（一条长直线前缘 + 琥珀走线）、
#    翼尖外挂短舱（探针 + 舱体）、后缘三段锯齿、翼根长边条；同族里最瘦最亮、走线最细密
# --------------------------------------------------------------------------- #
def peregrine() -> Ship:
    use_palette(PALETTE_PEREGRINE)
    A, B, C, D = _deck()
    s = Ship(254, 254)

    # ---- 主翼：薄刃大后掠（前缘一条长直线）+ 后缘三段锯齿 ----
    s.facet([(104, 112), (12, 206), (32, 214), (46, 203), (62, 214), (78, 201), (100, 178)], B)
    s.facet([(104, 122), (46, 194), (62, 202), (100, 170)], C)          # 内翼承力面
    s.facet([(102, 130), (66, 184), (80, 190), (98, 162)], D)           # 内翼子面（提亮板块）
    _root_glove(s, (104, 112), (100, 178), 122, B)
    s.shade([(40, 204), (56, 200), (94, 184)], alpha=42)
    s.rim([(104, 112), (12, 206)])                                      # 锐利前缘棱线（一条长直线）
    s.rim([(12, 206), (32, 214)])
    s.seam([(103, 122), (46, 194)])
    s.seam([(100, 174), (50, 201)])
    s.seam([(100, 142), (62, 188)], width=1)
    s.seam([(84, 196), (97, 186)], width=1)
    _chevron(s, 68, 172, 18, 10)                                        # 翼面 V 形标识
    _bolt_row(s, 99, 136, 168, 4, r=1.1)
    s.panel_dot(74, 186, r=1.1)
    s.greeble(86, 174, 6, 5)
    s.vent(60, 196, length=11, gap=3, n=2)
    s.neon([(103, 114), (13, 205)], width=2)                            # 前缘琥珀走线（细而亮）
    s.neon([(103, 122), (48, 192)], width=1)
    s.neon([(38, 206), (94, 186)], width=1)
    s.lamp(76, 186, 1.2)
    _nav_housing(s)

    # ---- 翼尖短舱：沿前缘外挂的细长吊舱（舱体 + 前端探针），六型里只有它带 ----
    s.facet([(12, 206), (20, 194), (62, 152), (68, 160), (26, 208)], C)
    s.facet([(19, 195), (60, 155), (63, 160), (24, 202)], D)
    s.facet([(62, 152), (74, 143), (67, 160)], B)                       # 前端探针
    s.seam([(15, 199), (62, 155)], width=1)
    s.rim([(20, 194), (62, 152)])
    s.seam([(36, 178), (44, 170)], width=1)
    s.panel_dot(40, 176, r=1.0)
    s.greeble(30, 188, 5, 4)
    s.neon([(60, 155), (67, 160)], width=1)
    s.lamp(63, 153, 1.3)

    # ---- 翼根长边条（strake）：自翼根前缘沿机身向前拉出的一把薄刃 ----
    s.facet([(104, 112), (118, 78), (127, 78), (127, 106), (104, 118)], C)
    s.rim([(104, 112), (118, 78)])
    s.seam([(107, 110), (122, 82)], width=1)

    # ---- 鸭翼：大后掠细长前翼（前缘一条直线收成尖点，根部埋进细机头） ----
    s.facet([(128, 64), (84, 96), (90, 106), (128, 94)], C)
    s.facet([(124, 74), (88, 96), (94, 104), (126, 92)], D)
    s.rim([(128, 66), (84, 96)])
    s.seam([(122, 76), (91, 102)], width=1)
    s.panel_dot(108, 86, r=1.1)
    s.neon([(92, 106), (86, 97)], width=2)                              # 鸭翼尖灯
    s.neon([(126, 70), (90, 96)], width=1)

    # ---- 尾翼（细长外八小垂尾，根部接翼根） ----
    s.facet([(104, 168), (86, 224), (96, 229), (106, 202)], B)
    s.facet([(102, 176), (90, 218), (95, 220), (104, 202)], C)
    s.seam([(102, 178), (90, 218)], width=1)
    s.seam([(104, 172), (88, 222)], width=1)
    s.panel_dot(98, 196, r=1.1)
    s.greeble(95, 206, 4, 5)
    s.neon([(88, 220), (94, 224)], width=2)

    # ---- 主机身：针状长机头（前段收成细杆）+ 亮脊 + 暗舷板 ----
    prof = [(127, 16), (130, 32), (134, 66), (141, 118), (139, 170), (137, 206), (127, 236)]
    s.facet(_half(prof), A, mirror=False)
    s.facet(_half([(127, 16), (130, 32), (134, 66), (141, 118), (127, 140)]), D, mirror=False)
    s.facet(_half([(127, 22), (129, 44), (127, 96)]), C, mirror=False)               # 机头子面
    s.facet([(118, 110), (127, 138), (127, 236), (119, 206)], A, mirror=False)       # 左舷板（暗）
    s.facet([(141, 110), (127, 138), (127, 236), (137, 206)], C, mirror=False)       # 右舷板
    s.facet(_half([(127, 104), (136, 150), (134, 196)]), C, mirror=False)            # 脊线
    s.facet(_half([(127, 110), (133, 148), (132, 190)]), D, mirror=False)            # 脊线亮芯
    s.shade([(121, 210), (127, 232), (127, 236)], alpha=40, mirror=False)
    s.seam([(127, 138), (127, 236)], mirror=False)                                   # 中脊接缝
    s.seam([(118, 110), (127, 138)])
    s.rim([(127, 16), (130, 32)], mirror=False)
    s.rim([(127, 16), (124, 32)], mirror=False)
    s.rim([(130, 32), (134, 66)], mirror=False)
    s.rim([(124, 32), (120, 66)], mirror=False)
    s.seam([(127, 30), (129, 46)], width=1, mirror=False)
    s.seam([(127, 30), (125, 46)], width=1, mirror=False)
    s.seam([(127, 54), (132, 74)], width=1, mirror=False)
    s.seam([(127, 54), (122, 74)], width=1, mirror=False)
    s.seam([(119, 132), (135, 132)], width=1, mirror=False)
    s.seam([(120, 156), (136, 156)], width=1, mirror=False)
    s.seam([(122, 180), (136, 180)], width=1, mirror=False)
    _bolt_row(s, 133, 136, 172, 4, r=1.1, mirror=False)
    s.panel_dot(121, 136, r=1.1)
    s.panel_dot(130, 196, r=1.0)
    _grille(s, 128, 100, 7, 14, bars=2)
    _decal_bars(s, 131, 188, n=3, w=7, h=2, gap=3)
    _vent_housing(s)

    # ---- 引擎舱（窄整流罩 + 双发） ----
    _tail_fairing(s, 14)
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
    s.neon([(117, 130), (120, 198)], width=2)                                      # 侧舷能量缝
    s.neon([(137, 130), (134, 198)], width=2)
    s.neon([(127, 124), (127, 204)], width=1, mirror=False)                        # 脊线走线
    s.lamp(CX, 24, 1.4, mirror=False)                                              # 机头信标
    s.lamp(CX, 106, 1.2, mirror=False)
    return s


# --------------------------------------------------------------------------- #
# 二、重锤 sledge：攻击力——钝厚机首（撞角犁）+ 肩部双联重炮（伸出机首）+ 翼下重型挂弹、
#    机腹加强肋、厚板缝；短展厚弦、同族里最暖的橙铜色
# --------------------------------------------------------------------------- #
def sledge() -> Ship:
    use_palette(PALETTE_SLEDGE)
    A, B, C, D = _deck()
    s = Ship(254, 254)

    # ---- 主翼：短展厚弦（截角翼尖）+ 后缘折线 ----
    s.facet([(106, 118), (20, 198), (12, 206), (26, 214), (52, 224), (76, 210), (102, 190)], B)
    s.facet([(104, 126), (40, 196), (56, 206), (102, 176)], C)          # 内翼承力面（加厚）
    s.facet([(102, 134), (58, 192), (74, 200), (100, 168)], D)          # 内翼子面
    s.facet([(102, 178), (60, 214), (76, 218), (98, 200)], A)           # 翼根后段暗面
    _root_glove(s, (106, 118), (102, 190), 118, B)
    s.shade([(46, 208), (62, 212), (96, 196)], alpha=45)
    s.rim([(106, 118), (20, 198)])
    s.rim([(20, 198), (12, 206)])
    s.rim([(12, 206), (26, 214)])
    s.seam([(105, 126), (42, 196)])
    s.seam([(102, 180), (58, 212)])
    s.seam([(100, 146), (64, 194)], width=1)
    s.seam([(84, 202), (98, 192)], width=1)
    s.seam([(58, 200), (92, 178)], width=1)                             # 承力梁板缝
    _chevron(s, 62, 182, 20, 11, kind="rim")
    _bolt_row(s, 101, 142, 172, 4, r=1.3)
    s.panel_dot(76, 190, r=1.3)
    s.greeble(80, 180, 8, 7)
    s.vent(62, 204, length=14, gap=3, n=3)
    s.neon([(105, 120), (22, 197)], width=3)                            # 粗琥珀走线（厚前缘）
    s.neon([(102, 178), (58, 212)], width=2)
    s.neon([(70, 202), (96, 188)], width=2)
    s.lamp(70, 194, 1.5)
    _nav_housing(s)

    # ---- 翼下重型挂弹：挂架 + 弹体（伸出后缘之外，剪影上读作「带着家伙」） ----
    s.facet([(52, 198), (70, 194), (70, 208), (52, 211)], C)            # 挂架
    s.greeble(46, 206, 26, 30, fill=B)                                  # 火箭巢壳体
    s.greeble(48, 208, 22, 26, fill=D, outline=False)
    for ty in (213, 225):                                               # 六管发射口（2×3）
        for tx in (52, 58, 64):
            s.bd.ellipse(s.p([(tx - 2.6, ty - 2.6), (tx + 2.6, ty + 2.6)]), fill=player_sprite.SEAM)
            s.bd.ellipse(s.p([(tx - 1.6, ty - 1.6), (tx + 1.6, ty + 1.6)]), fill=A)
    s.seam([(48, 219), (70, 219)], width=1)
    s.neon([(48, 208), (48, 234)], width=1)
    s.lamp(50, 208, 1.2)

    # ---- 肩部双联重炮：炮荚舱 + 伸出机首的粗炮管（六型里只有它把炮扛在肩上） ----
    s.facet([(88, 104), (112, 110), (114, 154), (90, 160)], C)          # 炮荚舱
    s.facet([(91, 108), (108, 113), (110, 150), (92, 154)], D)
    s.facet([(90, 134), (112, 132), (112, 150), (91, 154)], B)          # 荚舱后段暗面
    s.seam([(112, 112), (114, 152)])
    s.seam([(90, 138), (112, 136)], width=1)
    s.seam([(90, 152), (112, 150)], width=1)
    s.rim([(88, 104), (91, 132)])
    s.facet([(89, 62), (99, 62), (99, 108), (89, 108)], C)              # 炮管（细长）
    s.facet([(91, 64), (94, 64), (94, 106), (91, 106)], D)              # 管身高光
    s.facet([(86, 52), (102, 52), (102, 64), (86, 64)], B)              # 炮口制退器
    s.seam([(86, 52), (102, 52)], width=1)
    s.seam([(86, 64), (102, 64)], width=1)
    s.seam([(89, 80), (99, 80)], width=1)                               # 冷却环
    s.seam([(89, 94), (99, 94)], width=1)
    s.rim([(86, 52), (86, 64)])
    s.rim([(102, 52), (102, 64)])
    s.rim([(89, 62), (89, 76)])
    s.facet([(99, 100), (110, 104), (110, 112), (99, 108)], B)          # 炮管支座（接到荚舱）
    s.panel_dot(95, 88, r=1.0)
    s.neon([(93, 55), (93, 61)], width=3)                               # 炮口焰心
    s.lamp(94, 56, 1.6)
    s.lamp(100, 122, 1.4)

    # ---- 鸭翼（短厚，收在炮架下方） ----
    s.facet([(114, 88), (84, 96), (90, 104), (114, 100)], C)
    s.facet([(110, 92), (90, 97), (94, 101), (110, 98)], D)
    s.rim([(114, 88), (84, 96)])
    s.seam([(110, 92), (92, 100)], width=1)
    s.panel_dot(102, 96, r=1.2)
    s.neon([(94, 102), (86, 97)], width=2)

    # ---- 尾翼（短厚外八） ----
    s.facet([(103, 170), (80, 226), (94, 232), (106, 206)], B)
    s.facet([(101, 178), (86, 220), (92, 222), (103, 204)], C)
    s.seam([(101, 180), (87, 220)], width=1)
    s.seam([(103, 174), (83, 224)], width=1)
    s.panel_dot(98, 198, r=1.3)
    s.greeble(94, 206, 5, 6)
    s.neon([(83, 224), (92, 228)], width=2)

    # ---- 主机身：钝锥机首 + 撞角犁 ----
    prof = [(127, 16), (136, 34), (144, 70), (147, 116), (145, 170), (141, 206), (127, 236)]
    s.facet(_half(prof), A, mirror=False)
    s.facet(_half([(127, 16), (136, 34), (144, 70), (147, 116), (127, 138)]), D, mirror=False)
    s.facet(_half([(127, 22), (134, 42), (127, 100)]), C, mirror=False)              # 机首子面
    s.facet([(114, 108), (127, 140), (127, 236), (118, 208)], A, mirror=False)       # 左舷板
    s.facet([(147, 108), (127, 140), (127, 236), (141, 208)], B, mirror=False)       # 右舷板
    s.shade([(122, 212), (127, 232), (127, 236)], alpha=45, mirror=False)
    s.seam([(127, 140), (127, 236)], mirror=False)
    s.seam([(114, 108), (127, 140)])
    s.rim([(127, 16), (136, 34)], mirror=False)
    s.rim([(127, 16), (118, 34)], mirror=False)
    s.rim([(136, 34), (144, 70)], mirror=False)
    s.rim([(118, 34), (110, 70)], mirror=False)
    _sym(s, [(127, 24), (137, 44), (142, 74), (133, 64), (127, 40)], C)              # 撞角装甲带
    s.rim([(127, 22), (137, 42)], mirror=False)
    s.rim([(127, 22), (117, 42)], mirror=False)
    s.seam([(127, 44), (135, 64)], width=2, mirror=False)
    s.seam([(127, 44), (119, 64)], width=2, mirror=False)
    _cross(s, 52, 22)                                                                # 机首环带
    _cross(s, 52, 22, kind="rim")
    _cross(s, 76, 20)
    _cross(s, 120, 20)
    _cross(s, 120, 20, kind="rim")
    _cross(s, 140, 20)
    _bolt_row(s, 140, 128, 164, 4, r=1.3, mirror=False)
    s.panel_dot(120, 128, r=1.3)
    s.greeble(134, 104, 7, 8)
    _decal_bars(s, 136, 184, n=2, w=8, h=3, gap=4)
    # 机腹加强肋（三道横肋 + 铆钉列）：读作「皮厚」
    for i, y in enumerate((176, 196, 216)):
        hw = 18 - i * 3
        _ring(s, y, hw, B, h=7)
        _cross(s, y, hw)
        _cross(s, y + 7, hw - 1)
        s.panel_dot(CX + hw - 3, y + 3, r=1.3, mirror=False)
        s.panel_dot(CX - hw + 3, y + 3, r=1.3, mirror=False)
    _vent_housing(s)

    # ---- 引擎舱（加厚整流罩 + 双发） ----
    _tail_fairing(s, 17)
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
    s.neon([(116, 126), (119, 200)], width=3)
    s.neon([(138, 126), (135, 200)], width=3)
    s.neon([(127, 124), (127, 206)], width=2, mirror=False)
    s.lamp(CX, 26, 1.8, mirror=False)
    return s


# --------------------------------------------------------------------------- #
# 三、连弩 repeater：攻击速度——折线前缘主翼（内段陡 / 外段缓）、机首并列双炮管（冷却套 +
#    炮口制退器）、翼根弹鼓与弹链、翼面与机身密布散热格栅、尾部多发喷口节；白热金 + 最多霓虹
# --------------------------------------------------------------------------- #
def repeater() -> Ship:
    use_palette(PALETTE_REPEATER)
    A, B, C, D = _deck()
    s = Ship(254, 254)

    # ---- 主翼：折线前缘（cranked delta）+ 直线后缘 ----
    s.facet([(104, 114), (62, 166), (12, 206), (30, 218), (102, 184)], B)
    s.facet([(104, 122), (66, 164), (34, 196), (56, 204), (102, 176)], C)    # 内翼承力面
    s.facet([(102, 132), (74, 166), (52, 192), (70, 200), (100, 168)], D)    # 内翼子面
    s.facet([(102, 176), (58, 214), (74, 218), (100, 196)], A)               # 翼根后段暗面
    _root_glove(s, (104, 114), (102, 184), 120, B)
    s.shade([(44, 210), (60, 212), (96, 194)], alpha=40)
    s.rim([(104, 114), (62, 166)])
    s.rim([(62, 166), (12, 206)])
    s.rim([(12, 206), (30, 218)])
    s.seam([(103, 124), (68, 164)])
    s.seam([(64, 168), (32, 198)], width=1)
    s.seam([(102, 178), (60, 212)])
    s.seam([(100, 146), (66, 192)], width=1)
    s.seam([(84, 202), (98, 190)], width=1)
    s.vent(78, 172, length=9, gap=3, n=3)                                    # 翼面散热格栅阵
    s.vent(64, 188, length=9, gap=3, n=3)
    s.vent(50, 202, length=8, gap=3, n=2)
    _grille(s, 80, 150, 9, 14, bars=2)
    s.greeble(64, 194, 6, 6)
    _bolt_row(s, 100, 140, 172, 4, r=1.1)
    s.panel_dot(72, 196, r=1.1)
    s.neon([(103, 116), (63, 165)], width=2)                                 # 折线前缘走线
    s.neon([(63, 165), (14, 204)], width=2)
    s.neon([(38, 210), (96, 190)], width=1)
    s.neon([(68, 200), (94, 192)], width=1)
    s.lamp(80, 176, 1.2)
    _nav_housing(s)

    # ---- 机首并列双炮管：冷却套 + 炮口制退器（脊线两侧各一根，伸出机头） ----
    s.facet([(112, 22), (120, 21), (120, 72), (112, 78)], C)                 # 管身（细长，与机鼻留出缝）
    s.facet([(114, 24), (117, 23), (117, 68), (114, 72)], D)                 # 管身高光
    for yy in (36, 52):                                                      # 冷却套（两道加宽环）
        s.facet([(110.5, yy), (120, yy - 1), (120, yy + 4), (110.5, yy + 5)], B)
    s.facet([(109, 12), (121, 10), (121, 24), (109, 26)], B)                 # 炮口制退器
    s.rim([(109, 12), (121, 10)])
    s.seam([(112, 22), (121, 20)], width=1)
    s.facet([(120, 68), (128, 74), (128, 82), (120, 78)], B)                 # 管座（接到机身）
    s.neon([(115, 13), (115, 20)], width=3)                                  # 炮口焰心
    s.lamp(116, 15, 1.6)
    s.seam([(127, 10), (127, 78)], width=1, mirror=False)                    # 双管中缝

    # ---- 鸭翼（带格栅） ----
    s.facet([(114, 84), (84, 96), (92, 108), (114, 100)], C)
    s.facet([(110, 88), (90, 98), (96, 104), (110, 98)], D)
    s.rim([(114, 84), (84, 96)])
    s.seam([(110, 88), (92, 102)], width=1)
    s.vent(98, 92, length=7, gap=3, n=2)
    s.neon([(94, 104), (86, 97)], width=2)

    # ---- 尾翼（多发喷口节的前置支撑） ----
    s.facet([(103, 170), (82, 226), (94, 232), (106, 206)], B)
    s.facet([(101, 178), (88, 220), (93, 222), (103, 204)], C)
    s.seam([(101, 180), (89, 220)], width=1)
    s.seam([(103, 174), (85, 224)], width=1)
    s.vent(96, 208, length=6, gap=3, n=2)
    s.neon([(84, 224), (92, 228)], width=2)

    # ---- 主机身：宽脊 + 环绕散热缝（五道，缝长随机身收窄，霓虹隔道点） ----
    prof = [(127, 16), (135, 38), (141, 72), (144, 118), (143, 170), (140, 206), (127, 236)]
    s.facet(_half(prof), A, mirror=False)
    s.facet(_half([(127, 16), (135, 38), (141, 72), (144, 118), (127, 140)]), D, mirror=False)
    s.facet(_half([(127, 22), (133, 44), (127, 98)]), C, mirror=False)               # 机首子面
    s.facet([(115, 110), (127, 140), (127, 236), (119, 206)], B, mirror=False)       # 左舷板
    s.facet([(144, 110), (127, 140), (127, 236), (140, 206)], C, mirror=False)       # 右舷板
    s.facet(_half([(127, 104), (137, 152), (135, 198)]), C, mirror=False)            # 脊线
    s.shade([(122, 210), (127, 232), (127, 236)], alpha=40, mirror=False)
    s.seam([(127, 140), (127, 236)], mirror=False)
    s.seam([(115, 110), (127, 140)])
    s.rim([(127, 16), (135, 38)], mirror=False)
    s.rim([(127, 16), (119, 38)], mirror=False)
    s.rim([(135, 38), (141, 72)], mirror=False)
    s.rim([(119, 38), (113, 72)], mirror=False)
    for i, y in enumerate((118, 138, 158, 178, 196)):
        hw = 16 - (i // 2)
        _cross(s, y, hw)
        if i % 2 == 0:
            s.neon([(CX - hw + 3, y + 4), (CX + hw - 3, y + 4)], width=1, mirror=False)
        else:
            s.vent(118, y + 3, length=9, gap=3, n=2)
            s.greeble(131, y + 2, 6, 5)
    _bolt_row(s, 137, 128, 164, 4, r=1.1, mirror=False)
    s.panel_dot(117, 128, r=1.1)
    s.greeble(133, 104, 7, 7)
    _decal_bars(s, 134, 186, n=3, w=7, h=2, gap=3)
    _vent_housing(s)

    # ---- 翼根弹鼓 + 弹链（六型里只有它把弹药挂在外面） ----
    s.bd.ellipse(s.p([(93, 114), (113, 150)]), fill=player_sprite.SEAM)
    s.bd.ellipse(s.p([(95, 116), (111, 148)]), fill=C)
    s.bd.ellipse(s.p([(98, 120), (108, 144)]), fill=D)
    for yy in (124, 132, 140):
        s.seam([(95, yy), (111, yy)], width=1)
    s.bd.ellipse(s.p([(100, 126), (108, 138)]), fill=player_sprite.RIVET)    # 鼓心
    s.bd.ellipse(s.p([(102, 129), (106, 135)]), fill=player_sprite.SEAM)
    s.rim([(95, 118), (96, 144)])
    s.panel_dot(103, 122, r=1.1)
    s.greeble(106, 146, 7, 6, fill=B)                                        # 供弹口
    for i, (bx, by) in enumerate(((112, 108), (114, 99), (116, 90), (117, 81), (118, 72))):
        s.greeble(bx, by, 7, 6, fill=B if i % 2 == 0 else C)
    s.neon([(114, 106), (119, 74)], width=1)

    # ---- 引擎舱：多发喷口节（四道节环 + 尾板格栅） ----
    _tail_fairing(s, 16)
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
    s.neon([(117, 130), (120, 200)], width=2)
    s.neon([(137, 130), (134, 200)], width=2)
    s.neon([(127, 124), (127, 206)], width=1, mirror=False)
    s.lamp(CX, 26, 1.5, mirror=False)
    return s


# --------------------------------------------------------------------------- #
# 四、壁垒 bulwark：防御——截角方翼尖、前缘四段叠层装甲带、翼面反应装甲块、机首撞角犁、
#    机背大盾板、内嵌式小座舱；暗铜褐、以板缝为主少霓虹
# --------------------------------------------------------------------------- #
def bulwark() -> Ship:
    use_palette(PALETTE_BULWARK)
    A, B, C, D = _deck()
    s = Ship(254, 254)

    # ---- 主翼：截角方翼尖 + 前缘五段叠层装甲带 + 盾形分段甲片 ----
    s.facet([(106, 116), (16, 198), (12, 206), (28, 216), (58, 228), (86, 208), (102, 188)], B)
    s.facet([(104, 126), (44, 194), (60, 204), (102, 178)], C)               # 内翼承力面
    s.facet([(102, 180), (60, 216), (78, 220), (98, 198)], A)                # 翼根后段暗面
    _root_glove(s, (106, 116), (102, 188), 120, B)
    s.shade([(46, 210), (62, 214), (96, 196)], alpha=45)
    s.rim([(106, 116), (16, 198)])
    s.rim([(16, 198), (12, 206)])
    s.rim([(12, 206), (28, 216)])
    # 前缘叠层装甲带：五段厚板逐段外错（每段自带棱线与板缝，读作「一层压一层」）
    for i in range(5):
        u0, u1 = i / 5.0, (i + 1) / 5.0
        x0 = 106 + (16 - 106) * u0
        y0 = 116 + (198 - 116) * u0
        x1 = 106 + (16 - 106) * u1
        y1 = 116 + (198 - 116) * u1
        off = 13.0 - i * 1.6
        s.facet([(x0, y0), (x1, y1), (x1 + off * 0.62, y1 + off), (x0 + off * 0.62, y0 + off)],
                D if i % 2 == 0 else C)
        s.seam([(x1, y1), (x1 + off * 0.62, y1 + off)], width=1)
        s.seam([(x0 + off * 0.62, y0 + off), (x1 + off * 0.62, y1 + off)], width=1)
        s.panel_dot(x0 + off * 0.35, y0 + off * 0.75, r=1.2)
    # 盾形分段甲片（外段两块：亮面 + 四边板缝，读作挂上去的盾）
    for i, (cx, cy, w, h) in enumerate(((46, 194, 26, 22), (72, 176, 24, 20))):
        s.facet([(cx, cy), (cx + w, cy + h * 0.45), (cx + w * 0.6, cy + h), (cx - w * 0.1, cy + h * 0.6)],
                D if i == 0 else C)
        s.seam([(cx, cy), (cx + w, cy + h * 0.45)], width=1)
        s.seam([(cx + w, cy + h * 0.45), (cx + w * 0.6, cy + h)], width=1)
        s.seam([(cx + w * 0.6, cy + h), (cx - w * 0.1, cy + h * 0.6)], width=1)
        s.seam([(cx - w * 0.1, cy + h * 0.6), (cx, cy)], width=1)
        s.panel_dot(cx + w * 0.3, cy + h * 0.45, r=1.2)
    s.seam([(102, 180), (62, 214)])
    s.seam([(100, 148), (66, 194)], width=1)
    s.seam([(86, 206), (98, 194)], width=1)
    _bolt_row(s, 101, 142, 172, 4, r=1.2)
    s.greeble(84, 178, 7, 6)
    s.neon([(105, 118), (18, 197)], width=1)                                 # 细走线（少霓虹）
    s.neon([(40, 212), (96, 192)], width=1)
    s.lamp(74, 190, 1.2)
    _nav_housing(s)

    # ---- 鸭翼（厚钝，带装甲缘） ----
    s.facet([(114, 84), (84, 96), (92, 108), (114, 100)], C)
    s.facet([(110, 88), (90, 98), (96, 105), (110, 98)], D)
    s.rim([(114, 84), (84, 96)])
    s.seam([(110, 88), (92, 104)], width=1)
    s.panel_dot(101, 96, r=1.2)

    # ---- 尾翼（厚钝，根部带装甲座） ----
    s.facet([(103, 170), (80, 226), (94, 232), (106, 206)], B)
    s.facet([(101, 178), (86, 220), (92, 222), (103, 204)], C)
    s.seam([(101, 180), (87, 220)], width=1)
    s.seam([(103, 174), (83, 224)], width=1)
    s.panel_dot(98, 196, r=1.2)
    s.greeble(94, 206, 5, 6)
    _band(s, 92, 200, 14, 10, C)

    # ---- 主机身：短钝撞角机首 + 机背大盾板 ----
    prof = [(127, 16), (136, 36), (145, 72), (148, 118), (146, 170), (142, 206), (127, 236)]
    s.facet(_half(prof), A, mirror=False)
    s.facet(_half([(127, 16), (136, 36), (145, 72), (148, 118), (127, 140)]), D, mirror=False)
    s.facet(_half([(127, 22), (134, 44), (127, 100)]), C, mirror=False)              # 机首子面
    s.facet([(115, 108), (127, 140), (127, 236), (119, 208)], A, mirror=False)       # 左舷板
    s.facet([(148, 108), (127, 140), (127, 236), (142, 208)], B, mirror=False)       # 右舷板
    s.shade([(122, 212), (127, 232), (127, 236)], alpha=50, mirror=False)
    s.seam([(127, 140), (127, 236)], mirror=False)
    s.seam([(115, 108), (127, 140)])
    s.rim([(127, 16), (136, 36)], mirror=False)
    s.rim([(127, 16), (118, 36)], mirror=False)
    s.rim([(136, 36), (145, 72)], mirror=False)
    s.rim([(118, 36), (109, 72)], mirror=False)
    _sym(s, [(127, 22), (138, 42), (143, 76), (133, 66), (127, 40)], C)              # 撞角装甲犁
    s.rim([(127, 20), (138, 40)], mirror=False)
    s.rim([(127, 20), (116, 40)], mirror=False)
    s.seam([(127, 42), (136, 66)], width=2, mirror=False)
    s.seam([(127, 42), (118, 66)], width=2, mirror=False)
    # 机背大盾板：一块压住脊线的厚板（两段 + 中央脊线 + 密集铆钉）
    s.facet(_half([(127, 106), (139, 128), (140, 172), (133, 204), (127, 208)]), C, mirror=False)
    s.facet(_half([(127, 112), (135, 130), (136, 170), (131, 198), (127, 200)]), D, mirror=False)
    s.seam([(127, 106), (127, 208)], mirror=False)
    s.seam([(127, 152), (139, 152)], width=1, mirror=False)
    s.rim([(127, 106), (139, 128)], mirror=False)
    s.rim([(127, 106), (115, 128)], mirror=False)
    _bolt_row(s, 134, 134, 196, 5, r=1.2, mirror=False)
    _bolt_row(s, 120, 134, 196, 5, r=1.2, mirror=False)
    # 侧舷装甲块（反应装甲列）
    for i, (bx, by) in enumerate(((140, 116), (141, 132), (139, 148))):
        _band(s, bx, by, 11, 12, C if i % 2 == 0 else D)
    _cross(s, 76, 20)
    _cross(s, 96, 19)
    _vent_housing(s)

    # ---- 引擎舱 ----
    _tail_fairing(s, 17)
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
    s.neon([(118, 130), (120, 196)], width=1)
    s.neon([(136, 130), (134, 196)], width=1)
    s.lamp(CX, 26, 1.4, mirror=False)
    return s


# --------------------------------------------------------------------------- #
# 五、巨像 colossus：血量上限——最宽最厚的机身（多段环带 + 侧舷装甲舱）、深弦宽翼、
#    翼下大型外挂装甲舱、加大双发整流罩；同族里最重的一块，铆钉最密
# --------------------------------------------------------------------------- #
def colossus() -> Ship:
    use_palette(PALETTE_COLOSSUS)
    A, B, C, D = _deck()
    s = Ship(254, 254)

    # ---- 主翼：深弦宽翼（外段折线收成宽翼尖） ----
    s.facet([(104, 110), (14, 196), (12, 206), (34, 220), (66, 232), (92, 212), (104, 190)], B)
    s.facet([(104, 120), (34, 194), (56, 206), (102, 176)], C)               # 内翼承力面
    s.facet([(102, 130), (54, 188), (72, 200), (100, 168)], D)               # 内翼子面
    s.facet([(102, 180), (56, 220), (76, 226), (98, 202)], A)                # 翼根后段暗面
    _root_glove(s, (104, 110), (104, 190), 116, B)
    s.shade([(46, 214), (66, 220), (96, 200)], alpha=45)
    s.rim([(104, 110), (14, 196)])
    s.rim([(14, 196), (12, 206)])
    s.rim([(12, 206), (34, 220)])
    # 翼根承力梁（厚重楔形 + 两道加强缝）
    s.facet([(104, 118), (74, 176), (94, 196), (104, 156)], D)
    s.seam([(104, 118), (74, 176)], width=1)
    s.seam([(74, 176), (94, 196)], width=1)
    s.seam([(94, 196), (104, 156)], width=1)
    s.seam([(103, 124), (38, 194)])
    s.seam([(102, 182), (58, 218)])
    s.seam([(100, 146), (64, 196)], width=1)
    s.seam([(90, 210), (100, 200)], width=1)
    _chevron(s, 66, 180, 22, 12, kind="seam")
    _bolt_row(s, 100, 152, 178, 4, r=1.4)
    s.panel_dot(78, 202, r=1.3)
    s.panel_dot(66, 210, r=1.3)
    s.vent(60, 214, length=14, gap=3, n=3)
    s.greeble(78, 194, 8, 7)
    s.neon([(103, 112), (15, 195)], width=3)                                 # 粗前缘走线
    s.neon([(102, 180), (60, 218)], width=2)
    s.lamp(74, 200, 1.4)
    _nav_housing(s)

    # ---- 翼下大型外挂装甲舱：伸出后缘之外的两具厚舱（六型里只有它挂这个尺寸） ----
    s.facet([(52, 194), (78, 190), (78, 206), (52, 210)], C)                 # 挂架
    s.facet([(44, 210), (52, 202), (74, 198), (82, 206), (82, 232), (74, 240),
             (52, 240), (44, 232)], B)                                       # 舱体（八角厚壳）
    s.facet([(47, 212), (54, 205), (72, 202), (79, 208), (79, 230), (72, 237),
             (54, 237), (47, 230)], C)                                       # 内壳
    s.facet([(52, 214), (58, 210), (70, 208), (74, 212), (74, 226), (70, 231),
             (58, 231), (52, 226)], D)                                       # 亮面
    s.seam([(44, 220), (82, 220)], width=1)                                  # 舱体环缝
    s.seam([(47, 212), (47, 230)], width=1)
    s.seam([(79, 208), (79, 230)], width=1)
    s.rim([(44, 210), (52, 202)])
    s.rim([(74, 198), (82, 206)])
    s.rim([(82, 232), (74, 240)])
    _bolt_row(s, 50, 214, 234, 4, r=1.2)
    _bolt_row(s, 76, 214, 234, 4, r=1.2)
    s.panel_dot(61, 214, r=1.3)
    s.lamp(61, 208, 1.3)
    s.neon([(46, 206), (46, 238)], width=1)

    # ---- 鸭翼（宽厚） ----
    s.facet([(114, 82), (84, 96), (92, 110), (114, 100)], C)
    s.facet([(110, 88), (90, 98), (96, 105), (110, 98)], D)
    s.rim([(114, 82), (84, 96)])
    s.seam([(110, 88), (92, 104)], width=1)
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

    # ---- 主机身：最宽最厚 + 侧舷装甲舱 ----
    prof = [(127, 16), (138, 40), (152, 84), (153, 126), (149, 176), (142, 208), (127, 236)]
    s.facet(_half(prof), A, mirror=False)
    s.facet(_half([(127, 16), (138, 40), (152, 84), (153, 126), (127, 146)]), D, mirror=False)
    s.facet(_half([(127, 24), (136, 48), (127, 104)]), C, mirror=False)              # 机首子面
    s.facet(_half([(127, 112), (151, 150), (145, 200), (127, 224)]), B, mirror=False)  # 腹板（厚）
    s.facet([(112, 108), (127, 146), (127, 236), (118, 210)], A, mirror=False)       # 左舷板
    s.facet([(153, 108), (127, 146), (127, 236), (142, 210)], B, mirror=False)       # 右舷板
    # 侧舷装甲舱（外凸厚板 + 棱线 + 铆钉）
    s.facet([(104, 148), (100, 206), (112, 232), (127, 236), (127, 148)], C)
    s.rim([(104, 148), (100, 206)])
    s.rim([(100, 206), (112, 232)])
    s.seam([(107, 156), (104, 206)], width=1)
    s.seam([(110, 178), (107, 210)], width=1)
    _bolt_row(s, 106, 162, 200, 4, r=1.4)
    s.shade([(122, 212), (127, 232), (127, 236)], alpha=45, mirror=False)
    s.seam([(127, 146), (127, 236)], mirror=False)
    s.seam([(112, 108), (127, 146)])
    s.rim([(127, 16), (138, 40)], mirror=False)
    s.rim([(127, 16), (116, 40)], mirror=False)
    s.rim([(138, 40), (152, 84)], mirror=False)
    s.rim([(116, 40), (102, 84)], mirror=False)
    # 机背多段环带（五段，宽度不一，读作「皮厚」）+ 密集铆钉
    for i, (y, hw, h) in enumerate(((112, 24, 12), (132, 23, 10), (152, 22, 10), (172, 19, 9), (192, 16, 8))):
        _ring(s, y, hw, C if i % 2 == 0 else B, h=h, shrink=2)
        _cross(s, y, hw)
        s.panel_dot(CX + hw - 4, y + h // 2, r=1.4, mirror=False)
        s.panel_dot(CX - hw + 4, y + h // 2, r=1.4, mirror=False)
    _cross(s, 128, 19)
    _bolt_row(s, 137, 132, 172, 4, r=1.4, mirror=False)
    s.panel_dot(118, 132, r=1.4)
    s.greeble(135, 104, 8, 8)
    s.greeble(129, 222, 7, 8, mirror=False)
    _decal_bars(s, 136, 184, n=3, w=8, h=2, gap=3)
    _vent_housing(s)

    # ---- 引擎舱：双发整流罩加大 ----
    _tail_fairing(s, 19, fill=_deck()[2])
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
    s.neon([(115, 126), (119, 202)], width=3)
    s.neon([(139, 126), (135, 202)], width=3)
    s.neon([(127, 124), (127, 208)], width=2, mirror=False)
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
