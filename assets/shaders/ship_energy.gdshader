shader_type canvas_item;
render_mode unshaded, blend_add;

// 机体能量发光层（GlowLayer 专用）：采样发光遮罩 *_glow.png（生成器自机体 glow 图元层提取：
// R=霓虹走线/航行灯/能量核心，B=引擎喷口），叠加三种克制的时间动画——
// 能量沿走线缓慢流动（UV.y 滚动相位带）+ 引擎喷口呼吸闪烁（B 通道独立频率）+ 整体微弱明暗脉动。
// blend_add 加法混合：遮罩黑底区域零贡献；tint 承载阵营色分工（玩家琥珀 / 敌侧紫晶品红 / Boss 阶段色）。
// GL Compatibility：仅基础纹理采样与 sin，无导数/mipmap 依赖。

uniform float flow_speed = 0.55;  // 走线流动相位速度（周/秒）
uniform float pulse_hz = 0.8;     // 整体明暗脉动频率（Hz）
uniform float breath_hz = 1.7;    // 引擎喷口呼吸频率（Hz）
uniform float intensity = 0.4;    // 总强度（克制区间 0.3–0.5；Boss P2 上调）
uniform vec4 tint : source_color = vec4(1.0, 0.65, 0.18, 1.0); // 阵营能量色

void fragment() {
    vec4 mask = texture(TEXTURE, UV);
    // 流动相位沿 UV.y 推进：机头→机尾方向读出走线能量涌动（TAU 为内置常量）
    float flow = 0.72 + 0.28 * sin((UV.y * 3.0 - TIME * flow_speed) * TAU);
    float pulse = 0.82 + 0.18 * sin(TIME * pulse_hz * TAU);
    float breath = 0.62 + 0.38 * sin(TIME * breath_hz * TAU);
    float energy = clamp((mask.r * flow + mask.b * breath) * pulse * intensity, 0.0, 1.0);
    // blend_add 混合方程为 src.rgb * src.a + dst——能量值放 alpha，颜色幅值放 rgb，单层叠加强度
    COLOR = vec4(tint.rgb * tint.a, energy);
}
