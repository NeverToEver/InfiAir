#!/usr/bin/env python3
"""精灵统一后期增强层（共享模块，被各 generate_*_sprite.py 的 finish() 调用）。

在既有手绘分层（body + glow halo）合成并降采样之后、落盘之前追加三道确定性处理，
让全部机体获得一致的美术方向（深空环境光下的"实体感"），且不改动任何外形锚点：

1. 深色描边：以实心甲板（alpha 阈值）外扩 2px 勾一圈深藏青轮廓——机体在任何背景下
   与背景分离（深空/星云/亮特效都不糊边）；辉光 halo 阈值外不描，霓虹不受伤。
2. 顶部环境光：自上而下轻微乘法压暗（顶 1.0 → 底 0.93），给平面填充增加纵向体积。
3. 左上冷色缘光：实心区域朝上/朝左的边缘描一层冷白高光（alpha ~50%）——模拟远星
   侧逆光，机脊/翼前缘出现一条受光棱线。

全部为纯 PIL 确定性运算（无随机源），满足 regenerate_all.sh 的逐字节可复现约束。
"""

from PIL import Image, ImageChops, ImageFilter

OUTLINE_COLOR = (7, 10, 20, 255)
OUTLINE_WIDTH = 2          # 最终分辨率像素
RIM_COLOR = (205, 228, 255)
RIM_ALPHA = 128            # 缘光峰值 alpha（实心边缘 mask 255 → 128 = 50%）
TOP_FACTOR = 255           # 顶部乘数（1.0）
BOTTOM_FACTOR = 237        # 底部乘数（≈0.93）
SOLID_ALPHA_THRESHOLD = 180  # ≥ 视为实心甲板（辉光 halo 的半透明像素不参与描边/缘光）


def polish(img: Image.Image, outline_color=None, rim_color=None) -> Image.Image:
    """输入/输出均为 RGBA、最终分辨率；返回新图，不改入参。

    outline_color/rim_color：可选覆盖（默认 None = 用本模块常量，敌方晶体棱镜族保持原口径，
    输出逐字节不变）。玩家机走暖钛/琥珀语言时传暖色描边与暖缘光，与机体色温统一。
    """
    img = img.convert("RGBA")
    w, h = img.size
    o_color = outline_color if outline_color is not None else OUTLINE_COLOR
    r_color = rim_color if rim_color is not None else RIM_COLOR
    alpha = img.getchannel("A")
    solid = alpha.point(lambda v: 255 if v >= SOLID_ALPHA_THRESHOLD else 0)

    # ---- 1. 深色描边（合成在底层） ----
    size = OUTLINE_WIDTH * 2 + 1
    dilated = solid.filter(ImageFilter.MaxFilter(size))
    outline = Image.new("RGBA", (w, h), o_color)
    outline.putalpha(dilated)
    out = Image.alpha_composite(outline, img)

    # ---- 2. 顶部环境光（乘法纵向压暗，仅作用 RGB，alpha 原样保留） ----
    grad = Image.linear_gradient("L").resize((w, h), Image.BILINEAR)
    lut = [min(255, int(BOTTOM_FACTOR + (TOP_FACTOR - BOTTOM_FACTOR) * (255 - v) / 255)) for v in range(256)]
    shade = grad.point(lut).convert("RGB")
    shaded_rgb = ImageChops.multiply(out.convert("RGB"), shade)
    shaded = Image.merge("RGBA", (*shaded_rgb.split(), out.getchannel("A")))
    out = Image.alpha_composite(out, shaded)

    # ---- 3. 左上冷色缘光 ----
    # solid 下移右 2px：原本实心、平移后变空的像素 = 朝上/朝左的受光边缘
    edge = ImageChops.subtract(solid, ImageChops.offset(solid, 2, 2))
    edge = edge.filter(ImageFilter.GaussianBlur(0.8))
    rim = Image.new("RGBA", (w, h), (*r_color, 0))
    rim.putalpha(edge.point(lambda v: int(v / 255 * RIM_ALPHA)))
    out = Image.alpha_composite(out, rim)
    return out


def save_glow_mask(glow: Image.Image, engine_glow: Image.Image, size, blur_px: float, path: str) -> None:
    """能量发光遮罩落盘（黑底 RGBA，与机体贴图同画布）。

    从机体分层绘制的 glow/engine_glow 两图元层提取发光像素：R=霓虹走线/航行灯/能量核，
    B=引擎喷口，各带一圈柔光晕（光晕半径与 finish() 同源）；alpha=max(R,B) 仅供编辑器预览，
    shader（ship_energy.gdshader）只采 R/B 通道做分通道动画（R 流动 / B 呼吸）。
    输入为超采样画布，输出最终分辨率；纯 PIL 确定性运算，逐字节可复现。
    """
    def _channel(layer: Image.Image) -> Image.Image:
        halo = layer.filter(ImageFilter.GaussianBlur(blur_px))
        comp = Image.alpha_composite(halo, layer)
        return comp.resize(size, Image.LANCZOS).getchannel("A")

    r = _channel(glow)
    b = _channel(engine_glow)
    # 引擎图元双写于 glow 层（基础贴图需要）——R 通道减去 B 还原「纯走线」语义，
    # 两通道同图元同合成，引擎区逐位抵消为零，仅残留邻域光晕渗透
    r = ImageChops.subtract(r, b)
    a = ImageChops.lighter(r, b)
    mask = Image.merge("RGBA", (r, Image.new("L", size, 0), b, a))
    mask.save(path)
    print("saved", path)
