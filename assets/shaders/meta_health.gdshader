shader_type canvas_item;
// Meta HUD 血量/受击后处理（docs/META_HUD_DESIGN.md §3，管线顺序即 §1）：
// 屏幕抓取 → 径向色差+手写 4-tap 径向模糊 → 定向波纹(边缘弧形 ADD)
// → 去饱和/冷青色偏 → 晕影/心跳/视野收窄 → 裂纹距离场合成。
// 挂在 MetaHealthFX（CanvasLayer，世界之上、HUD 之下）的全屏 ColorRect 上。
// 注：Godot 4.3+ 的 gl_compatibility 已对 screen_texture 做过 Y 归一，直接采 SCREEN_UV，
// 不要再手动翻转（手动翻转会把世界垂直镜像）。
// 「减少闪光」不进 shader：GDScript 侧传参前折算，shader 零分支。

uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
uniform sampler2D u_crack_field : filter_linear;  // R=距裂隙线距离(0在线上)，G=单元 hash，B=生长门(0边缘→1中心)（D1 预烘焙）
uniform float u_hit_intensity = 0.0;         // 受击总强度（hit_pulse）
uniform vec2 u_hit_dir = vec2(0.0, 0.0);     // 攻击方向（屏幕归一化；零向量=全边缘均匀环）
uniform float u_chromatic_amount = 0.0;      // 径向色差偏移（0..0.02）
uniform float u_radial_blur_strength = 0.0;  // <0.001 整段跳过（D4）
uniform float u_ripple_phase = 1.0;          // 波纹推进相位 0→1（0.4s）
uniform float u_crack_progress = 0.0;        // 裂纹蔓延进度（含生长过冲）
uniform vec4 u_crack_color : source_color = vec4(0.208, 0.878, 1.0, 1.0);
uniform float u_crack_glow = 0.8;            // 裂纹 ADD 伪泛光强度（effects.meta_health.crack.glow）
uniform float u_crack_density = 0.0;         // 裂纹 alpha 上限（状态分层）
uniform float u_heal_jitter = 0.0;           // 修复期单元错峰消散幅度
uniform float u_crack_spread_min = 0.15;     // 生长门下限（边缘最早激活，满血不出裂纹）
uniform float u_crack_edge_softness = 0.08;  // 生长门激活柔和度
uniform float u_crack_width = 0.10;          // 裂隙线半宽（场空间）
uniform float u_desaturation = 0.0;          // 暗示层去饱和
uniform float u_hue_cool = 0.0;              // 冷青色偏强度
uniform float u_vignette_strength = 0.0;     // 晕影（含 DYING 警告脉动，GDScript 折算）
uniform float u_vignette_inner = 0.62;       // 内圈半径（DYING 收窄至 0.56，保中心视野）
uniform float u_heartbeat = 0.0;             // 心跳脉冲包络 0→1→0
uniform float u_adapt_gain = 1.0;            // D3 代理亮度可读性补偿
uniform int u_lod = 0;                       // 0=全管线，1=跳过色差/模糊/波纹（D2）

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 to_center = uv - vec2(0.5);
	// 16:9 修正 + 归一：边中点 dist≈1.0、四角≈1.15，晕影/波纹/色差阈值按此标定
	float dist = length(to_center * vec2(1.0, 0.5625)) * 2.0;
	float radial_w = smoothstep(0.25, 1.0, dist);            // 边缘强、中心弱

	// ── 1. 受击层：径向色差 + 手写 4-tap 径向模糊（LOD1 整段跳过，D2/D4；P1-7 减采样至 4-tap）──
	vec3 col;
	if (u_lod == 0 && (u_chromatic_amount > 0.0001 || u_radial_blur_strength > 0.001)) {
		vec2 off = normalize(to_center + 1e-6) * radial_w * u_chromatic_amount;
		// P1-7：色差 3 采样 → 2 采样（G 通道取两端平均，色差感不变）
		vec3 a = texture(screen_texture, uv + off).rgb;
		vec3 b = texture(screen_texture, uv - off).rgb;
		col = vec3(a.r, mix(a.g, b.g, 0.5), b.b);
		if (u_radial_blur_strength > 0.001) {
			vec3 acc = col;
			for (int i = 1; i <= 3; i++) {
				float t = float(i) / 4.0 * u_radial_blur_strength * radial_w * 0.06;
				acc += texture(screen_texture, mix(uv, vec2(0.5), t)).rgb;
			}
			col = acc / 4.0;
		}
	} else {
		col = texture(screen_texture, uv).rgb;
	}

	// ── 2. 定向波纹（边缘 12% 带，ADD，LOD1 跳过；u_hit_dir 零向量退化为全边缘均匀环）──
	if (u_lod == 0 && u_hit_intensity > 0.01) {
		float ang = atan(to_center.y, to_center.x);
		float dir_w = 1.0;
		if (length(u_hit_dir) > 0.01) {
			dir_w = pow(max(cos(ang - atan(u_hit_dir.y, u_hit_dir.x)), 0.0), 3.0);
		}
		float band = smoothstep(0.88, 0.98, dist);             // 只取边缘带，不遮弹幕判读区
		float wave = sin((dist - u_ripple_phase * 1.1) * 40.0) * 0.5 + 0.5;
		float ripple = band * dir_w * wave * (1.0 - u_ripple_phase) * u_hit_intensity;
		col += vec3(0.4, 0.9, 1.0) * ripple;
	}

	// ── 3. 暗示层：去饱和 + 冷青色偏 ──
	float lum = dot(col, vec3(0.299, 0.587, 0.114));
	col = mix(col, vec3(lum), u_desaturation);
	col = mix(col, col * vec3(0.85, 1.0, 1.1), u_hue_cool);

	// ── 4. 晕影 + 心跳脉冲 + 视野收窄（u_adapt_gain 补偿，D3）──
	float vig = smoothstep(u_vignette_inner, 1.15, dist);
	float vig_a = clamp(u_vignette_strength * (1.0 + 0.35 * u_heartbeat), 0.0, 0.6) * u_adapt_gain;
	col = mix(col, vec3(0.35, 0.02, 0.05), vig * vig_a);

	// ── 5. 裂纹合成（裂隙线细带 × 生长门：progress 从边缘向中心逐区激活，修复期按单元 hash 错峰）──
	vec3 field = texture(u_crack_field, uv).rgb;
	float gate = mix(u_crack_spread_min, 1.0, field.b) + (field.g - 0.5) * u_heal_jitter;
	float on = smoothstep(gate, gate + u_crack_edge_softness, u_crack_progress);
	float line = 1.0 - smoothstep(0.0, u_crack_width, field.r);
	float mask = line * on * u_crack_density;
	col = mix(col, u_crack_color.rgb * 0.25, mask * 0.6);         // 裂隙暗底
	col += u_crack_color.rgb * mask * u_crack_glow * u_adapt_gain;       // ADD 伪泛光

	COLOR = vec4(col, 1.0);
}
