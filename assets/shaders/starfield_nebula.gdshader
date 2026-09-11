shader_type canvas_item;

// P1-5：星云双 tint 层单次绘制（Starfield）。
// 原 _Draw 路径：3×3 平铺 × 暖/冷 2 层 = 18 次 DrawTextureRect，每格 ≈半屏 alpha 混合
// （≈8.8 屏/帧填充率）；本 shader 单全屏精灵 1 draw、1 屏/帧，GPU repeat 平铺。
// 相位还原（精灵 UV 0..1 ↔ 可见区域，tile = 区域×0.7）：
//   暖层格内相位 ph = fract(UV/0.7 + phase_off)，phase_off = (0.15, 1 − scroll/tile.y)
//   （x 偏 0.15 格、y 下卷回绕，同原 PosMod 基线）；冷层错半格 = fract(ph − 0.5)。
// 混合序还原：原绘制行优先、格内暖先冷后——冷层仅当 ph.x≥0.5 且 ph.y≥0.5（与暖同格）
// 时后绘压暖层，其余区域暖层在上；下方按同序 over 合成，与两次绘制逐位等价。
// 贴图为环面无缝灰度能量场（rgba=v），repeat 跨缝采样与原逐格 clamp 边缘视觉等价。
uniform vec2 phase_off = vec2(0.15, 1.0); // CPU 每帧 1 次上传（滚动唯一动态量）
uniform vec4 warm : source_color = vec4(0.72, 0.42, 0.16, 0.30);  // 暖琥珀主层（战术主色温）
uniform vec4 cool : source_color = vec4(0.16, 0.38, 0.55, 0.21);  // 冷青副层（全息青色温）

void fragment() {
    vec2 ph = fract(UV / 0.7 + phase_off);
    vec4 p = texture(TEXTURE, ph) * warm;                // 同 DrawTextureRect modulate（rgba 全乘）
    vec4 t = texture(TEXTURE, fract(ph - 0.5)) * cool;
    bool cool_top = ph.x >= 0.5 && ph.y >= 0.5;          // 原格内绘制序：同格冷层后绘
    vec4 top = cool_top ? t : p;
    vec4 bot = cool_top ? p : t;
    float a = top.a + bot.a * (1.0 - top.a);
    // over 合成后反预乘输出；alpha=0（双层皆空）输出全零免 NaN
    COLOR = a > 0.0 ? vec4((top.rgb * top.a + bot.rgb * bot.a * (1.0 - top.a)) / a, a) : vec4(0.0);
}
