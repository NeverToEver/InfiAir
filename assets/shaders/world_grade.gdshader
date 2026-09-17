shader_type canvas_item;
render_mode unshaded;

// 世界层画面增强（单趟全屏后处理）：亮部提取柔光辉光 + 暖调色彩分级 + 晕影 + 胶片颗粒。
// layer=1：世界之上、HUD 之下；由 WorldPostFx.cs 承载全屏 ColorRect 驱动。
//
// GL Compatibility 前提：项目锁定 gl_compatibility，Godot 的 Environment 辉光/SSAO 在该后端
// 不可用，故辉光全部在此以屏幕纹理（hint_screen_texture）手写实现——这是本作画面"平/廉"
// 观感的主要补齐手段。
//
// 性能纪律（Manager 侧）：关闭时整层隐藏（零 GPU）；reduce_flash 时颗粒置零、辉光减半、
// 重击脉冲减半、动态色差/扫描线置零；
// quality 档控制环采样数（低=4 tap 单环 / 高=8 tap 双环 + 2 tap 宽晕，
// uniform 分支在全屏绘制中无分支发散）。
// 动态战斗分级（u_combat_ramp 等）全部按均匀量分支：中性时零额外 tap、零分析开销。
// 不引 mipmap 依赖（textureLod 在部分 GL 后端无 back-buffer mip 链），全部走显式环采样。

uniform sampler2D screen_texture : hint_screen_texture, filter_linear, repeat_disable;

uniform float u_time = 0.0;
uniform vec2  u_texel = vec2(0.0005208, 0.0009259); // 1/1920, 1/1080（运行期由 texel uniform 覆盖）

// 辉光
uniform float u_bloom_threshold = 0.52;   // 亮部阈值（低于此不参与泛光）
uniform float u_bloom_intensity = 0.75;   // 泛光强度
uniform float u_bloom_radius = 5.5;       // 环采样半径（像素）
uniform vec3  u_bloom_tint = vec3(1.0, 0.741176, 0.360784); // 泛光暖调（战术琥珀）
uniform float u_quality = 1.0;            // >0.5 = 双环 8 tap，否则单环 4 tap

// 色彩分级（暖调 gain + 轻微 lift + S 曲线对比）
// 下列字面默认值是「死默认」：WorldPostFx._Ready 的 PushStaticParams 即按 data/balance.json
// effects.world_post.* 全量覆盖；默认值仍与 balance.json 同值，防漏写时落到旧常数。
uniform float u_grade = 0.55;
uniform vec3  u_gain = vec3(1.085, 1.0, 0.90);
uniform vec3  u_lift = vec3(0.015686, 0.011765, 0.007843);
uniform float u_contrast = 0.18;

// 晕影 / 颗粒
uniform float u_vignette = 0.33;
uniform float u_grain = 0.028;

// 动态战斗分级 / 重击脉冲（C# 侧 epsilon 守卫上传；四者全 0 即中性，
// 此时 gain/contrast/vignette/bloom 逐位等于静态调色，画面无任何永久变化）。
// u_combat_ramp：时间缩放低于 1（狂暴子弹时间）时 0→1 的热档；
// u_hit_pulse：重击瞬时脉冲（reduce_flash 时 C# 侧减半）；
// u_chroma / u_scanline：仅 ramp 非零才出现的附加层——色差多 2 次采样，
// 扫描线为分析式零采样；分支按均匀量取，空闲时零额外 tap。
uniform float u_combat_ramp = 0.0;
uniform float u_hit_pulse = 0.0;
uniform float u_chroma = 0.0;
uniform float u_scanline = 0.0;

vec3 bright(vec2 uv) {
    vec3 c = texture(screen_texture, uv).rgb;
    return max(c - vec3(u_bloom_threshold), vec3(0.0));
}

void fragment() {
    vec3 base = texture(screen_texture, SCREEN_UV).rgb;

    // ---- 动态全局色差（仅 ramp 非零）：红/蓝通道反向横移；中性时回退单采样，逐位不变 ----
    if (u_chroma > 0.0001) {
        vec2 coff = (SCREEN_UV - vec2(0.5)) * u_chroma;
        base.r = texture(screen_texture, SCREEN_UV + coff).r;
        base.b = texture(screen_texture, SCREEN_UV - coff).b;
    }

    // ---- 亮部提取 + 环采样柔光（旋转 45° 双环，8 tap；低档单环 4 tap） ----
    float r = u_bloom_radius;
    float k1 = 0.7071;
    vec2 o1 = vec2(1.0, 0.0);
    vec2 o2 = vec2(k1, k1);
    vec3 bloom = bright(SCREEN_UV) * 0.28;
    bloom += bright(SCREEN_UV + o1 * u_texel * r);
    bloom += bright(SCREEN_UV - o1 * u_texel * r);
    bloom += bright(SCREEN_UV + vec2(0.0, 1.0) * u_texel * r);
    bloom += bright(SCREEN_UV - vec2(0.0, 1.0) * u_texel * r);
    if (u_quality > 0.5) {
        bloom += bright(SCREEN_UV + o2 * u_texel * r * 1.6);
        bloom += bright(SCREEN_UV - o2 * u_texel * r * 1.6);
        bloom += bright(SCREEN_UV + vec2(o2.x, -o2.y) * u_texel * r * 1.6);
        bloom += bright(SCREEN_UV + vec2(-o2.x, o2.y) * u_texel * r * 1.6);
        // 二级宽晕（仅高质量档，+2 tap）：对角 3.2× 半径的远场柔光，低档路径逐位不变
        bloom += (bright(SCREEN_UV + o2 * u_texel * r * 3.2) + bright(SCREEN_UV - o2 * u_texel * r * 3.2)) * 0.5;
        bloom *= 0.20;
    } else {
        bloom *= 0.40;
    }

    // 重击脉冲瞬时放大泛光（u_hit_pulse=0 时因子精确为 1）
    vec3 c = base + bloom * u_bloom_intensity * u_bloom_tint * (1.0 + u_hit_pulse * 1.6);

    // ---- 色彩分级：暖调 gain + lift，再叠 S 曲线对比（柔化黑位/提亮中间调） ----
    // ramp=0 时 gain/contrast 精确等于静态值（mix 权重为 0），不产生永久观感偏移
    vec3 gain = u_gain * mix(vec3(1.0), vec3(1.10, 1.0, 0.86), u_combat_ramp);
    float contrast = u_contrast + u_combat_ramp * 0.14;
    vec3 graded = c * gain + u_lift;
    vec3 s = clamp(graded, 0.0, 1.0);
    graded = mix(s, s * s * (3.0 - 2.0 * s), contrast);
    c = mix(c, graded, u_grade);

    // ---- 晕影：中心 1 → 边缘压暗（ramp 加深；重击脉冲瞬时加深） ----
    float d = distance(SCREEN_UV, vec2(0.5)) * 1.4142; // 归一化到角部 ≈1
    float vig = smoothstep(0.95, 0.35, d);
    float vig_amt = clamp(u_vignette + u_combat_ramp * 0.12 + u_hit_pulse * 0.22, 0.0, 1.0);
    c *= mix(1.0, vig, vig_amt);

    // ---- 扫描线微纹理（分析式，零采样；仅 ramp 非零时；周期 3px 抑制摩尔纹） ----
    if (u_scanline > 0.0001) {
        float scan = 0.5 + 0.5 * sin(FRAGCOORD.y * 2.0943951);
        c *= 1.0 - u_scanline * scan;
    }

    // ---- 胶片颗粒（静态灰噪 + 时间抖动；reduce_flash 时 u_grain=0） ----
    if (u_grain > 0.0001) {
        vec2 px = FRAGCOORD.xy;
        float g = fract(sin(dot(px, vec2(12.9898, 78.233)) + u_time * 19.7) * 43758.5453);
        c += (g - 0.5) * u_grain;
    }

    COLOR = vec4(clamp(c, vec3(0.0), vec3(1.0)), 1.0);
}
