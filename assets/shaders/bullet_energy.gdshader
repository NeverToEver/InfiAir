shader_type canvas_item;
render_mode unshaded;

// 弹体能量尾动效（全部弹体共用**一份**材质，由 VisualRhythm 持有并逐帧上传）：只调制弹尾区间，
// 做两件事——能量沿尾迹朝弹头方向流动（空间行波）＋ 随音乐整拍的一次微亮。
//
// 为什么动效做在弹体自身的四边形内：GL Compatibility 后端官方对照表里 `Particle trails` 明确
// **不支持**（REFERENCES §4.7），而可读性口径要求「拖尾不得比判定活得久」——任何滞后于弹体的
// 几何都会骗玩家去躲一个已经不存在的威胁，并盖住下一段前摇。故本材质不新增 draw call、不新增
// 纹理采样、不新增几何：同一张图集、同一个四边形、同一个 UV 域内做调制；弹体主体
// （UV.x ≥ u_tail_end）、碰撞圆（CollisionShape2D，半径与节点原点）与判定几何逐位不变。
//
// **片元入参 COLOR 的语义（写错不报错、只会悄悄变暗）**：Godot 4 的 canvas_item 片元里，
// 入参 COLOR 已经等于「顶点色（CanvasItem.modulate × self_modulate）× 默认贴图采样」——
// 引擎在用户片元代码之前就执行了 `color *= texture(color_texture, uv)`（canvas.glsl 的
// fragment main；官方文档 canvas_item_shader 的 COLOR and TEXTURE 一节同此）。故这里读 COLOR
// 即得「贴图 × 染色」，**不得**再乘一次 texture(TEXTURE, UV)：再乘等于把贴图平方——尾迹 alpha
// 在受控区最亮处（px11）由 0.27 掉到 0.07、弹体整体变暗，且 u_intensity = 0 时不再逐位等于无材质。
// 弹体的激光染色 / 致死高亮 / 玩家弹呼吸脉冲全走 modulate / self_modulate，读 COLOR 才能把它们一并保留。
//
// 时间基准：流动相位取引擎 TIME（墙钟时基，不受 Engine.TimeScale 缩放），与既有
// ship_energy.gdshader 的走线流动同族——子弹时间（Boss 狂暴）下尾迹不随之减速，属刻意取舍
// （要跟模拟时间走须由 VisualRhythm 另上传相位 uniform）。拍点相位来自音乐播放位置
// （§2.10 已登记的真实时间面），由 VisualRhythm 取包络后经 u_beat 传入，本 shader 不做时基判断。
// GL Compatibility 前提：只有基础纹理读取、sin 与常量，无导数 / mipmap / 额外采样依赖。

uniform float u_beat = 0.0;       // 拍点包络 0..1（VisualRhythm 每帧上传；减少闪光下恒 0）
uniform float u_intensity = 1.0;  // 动效强度 0..1（设置项 fx_intensity；0 = 回到本批之前的画面）
uniform float u_flow_hz = 0.35;   // 尾迹流动的相位速度（周/秒）＝ balance effects.motion.bullet_tail_flow_hz
// 弹尾动效的总预算：**乘性增益的上限**（不是亮度增量）。受控区只有弹尾低 alpha 段（可见值约
// 0.02–0.17），增量式取值等于看不见——实测 0.12 只产生 1–2/255 的变化；取增益才有可读脉动。
uniform float u_tail_amp = 1.5;   // ＝ effects.motion.bullet_tail_amp（增益上限，尾部最亮处 ×2.5）
uniform float u_tail_end = 0.25;  // 受控区间右界（UV.x）；像素换算见下

// 预算切分（流动是底噪、拍点是重音）与流动的空间频率。
// 两项之和恒为 u_tail_amp，故「总预算」只有 balance 一处事实；空间频率取受控区间内约 1.5 个波长
// ——更密读成条纹噪声，更疏读不出「在流动」。二者都是纯表现取值，不属玩家可感数值。
const float FlowShare = 0.7;
const float BeatShare = 0.3;
const float FlowCycles = 6.0;

// 尾迹透明度的同行波调制深度（占受控区**现有** alpha 的比例）。比亮度项保守：alpha 是图集
// 烘焙出来的轮廓，动多了会读成弹体在抖（可读性），故只让远端尾迹随波形轻微呼吸。
const float AlphaShare = 0.35;

// u_tail_end 的取值依据：**必须落在纯尾段内**——弹体多边形（Bullet.ArrowBody）与高对比描边
// （轮廓膨胀半径 1.4px）的最左像素都不许被调制，否则「弹体主体与轮廓逐位不变」不成立。
// 换算（图集 48×16、Bullet.TexOffset=(23,8)，像素 x 的采样中心 UV=(x+0.5)/48）：
//   弹体 px 13..38（左缘 = TexOffset.X − 10 = 13）、描边 px 12..40、尾迹 px 1..16（x1 = TexOffset.X − 6 = 17）
//   ⇒ 纯尾段 = px 1..11；0.25 ＝ px 12.0 处的 UV 边界，即受控与不受控两像素中心的中点
//   （px11 中心 0.2396 < 0.25 ≤ px12 中心 0.2604），两侧各留 0.5px 余量。
// 改 Bullet.TexOffset 必须同步改这里（换算关系就是上面这条，不是估计）。

void fragment() {
    vec4 c = COLOR; // 已含贴图采样与 modulate/self_modulate（见头注释），不要重复采样
    // u_intensity = 0 时不进分支：分支外无任何写入，输出逐位等于「本材质不存在」
    if (u_intensity > 0.0001 && UV.x < u_tail_end) {
        // 越靠尾端越强：尾端（UV.x = 0）权重 1，区间右界权重 0——与未受控区连续，不出现亮度台阶
        float w = 1.0 - UV.x / u_tail_end;
        // 行波朝 UV.x 增大方向推进（＝弹体飞行方向，UV.x = 1 是弹头）：能量随弹体前推，
        // 而不是读成「尾迹被甩在原地」。相位取 TIME 而非逐帧上传（见头注释的时间基准）。
        float flow = 0.5 + 0.5 * sin((UV.x * FlowCycles - TIME * u_flow_hz) * TAU);
        // 两项都恒非负：弹尾只会被提亮、不会被压暗（可读性——尾迹变暗等于弹体形状在抖）
        float gain = u_tail_amp * u_intensity * w * (FlowShare * flow + BeatShare * u_beat);
        c.rgb *= 1.0 + gain;
        // 尾迹长度随同一列行波呼吸：波谷处远端尾迹变淡（读作能量被抽走），峰处复原。
        // 只作用于受控区（弹体主体在 u_tail_end 之外，逐位不变），深度比亮度项保守。
        c.a *= 1.0 - AlphaShare * u_intensity * w * (1.0 - flow);
    }

    COLOR = c;
}
