shader_type canvas_item;
// 裂纹距离场一次性预烘焙（docs/META_HUD_DESIGN.md D1）：SubViewport 512² 单帧渲染。
// 12 个固定种子点的 F1/F2 Voronoi：裂隙线位于单元边界（F2-F1≈0）。
// R=距裂隙线距离（0 在线上，单元内部饱和到 1）——主 shader 用它取细线带；
// G=最近单元 hash（修复期错峰消散用）；
// B=生长门（0 屏幕边缘 → 1 中心）——主 shader 用 progress 阈值扫过，裂纹从边缘向中心蔓延。
// scripts/meta_health_fx.gd 的 _cpu_bake_image() 是本 shader 的 CPU 等价回退（headless），
// 两者公式必须保持一致。

vec2 _seed(int i) {
	float f = float(i);
	return fract(sin(vec2(f * 12.9898, f * 78.233)) * 43758.5453);
}

void fragment() {
	vec2 uv = UV;
	// 16:9 修正：距离在等比空间计算，裂隙不随宽高比拉伸
	vec2 p = vec2(uv.x * 1.7778, uv.y);
	float f1 = 10.0;
	float f2 = 10.0;
	float h = 0.0;
	for (int i = 0; i < 12; i++) {
		vec2 s = vec2(_seed(i).x * 1.7778, _seed(i).y);
		float d = distance(p, s);
		if (d < f1) {
			f2 = f1;
			f1 = d;
			h = fract(sin(dot(s, vec2(12.9898, 78.233))) * 43758.5453);
		} else if (d < f2) {
			f2 = d;
		}
	}
	float border = f2 - f1;  // ≈0 于单元边界（裂隙线）
	float radial = length((uv - vec2(0.5)) * vec2(1.7778, 1.0));
	float gate = 1.0 - clamp(radial, 0.0, 1.0);  // 边缘 0（最先蔓延）→ 中心 1
	COLOR = vec4(clamp(border * 2.5, 0.0, 1.0), h, gate, 1.0);
}
