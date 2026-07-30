# Meta HUD 血量与受击反馈系统设计（实施规格）

状态：**实施就绪**。本文档自包含到可直接编码的程度：所有参数有名值、所有集成点有文件行号锚点、所有决策已定案（决策类一律偏向性能优化）。实施后验收标准见 §7。

技术前提：

- Godot 4.6 + GL Compatibility，纯 GDScript，设计视口 1920×1080。
- Compatibility 无 HDR bloom、无 Compositor：自发光用 ADD 混合伪泛光（项目 `_glow()` 惯例）；全屏后处理走 canvas_item shader + `hint_screen_texture` 的全屏 ColorRect [R4]。
- Compatibility 已知坑（实现时必须处理）：屏幕纹理 Y 方向可能翻转，运行时按渲染器判定；无 mipmap，`textureLod` 不可用，模糊手写多 tap [R4]。

---

## 1. 调研结论（可追溯性基线）

| 编号 | 参考 | 采纳 | 规避 |
| --- | --- | --- | --- |
| R1 | 死亡空间 Diegetic UI | 「零视觉搜索」原则：状态被动传达，不需找 UI | 脊柱式载体（2D 俯视机体太小不可行）→ 改屏幕层承载 |
| R2 | 径向色差/RGB 分离（Xor gmshaders、godotshaders） | 3-tap 径向色差作受击主效果，边缘强中心弱 | 全屏均匀色差（糊中心视野） |
| R3 | Voronoi 程序化裂纹（godotshaders Impact Glass、GitHub Godot-Glass-Break-Effect） | 预烘焙距离场 + 进度阈值化出裂纹；「开裂-消散」双向 | 全分辨率实时 Voronoi |
| R4 | Godot 4 全屏后处理（The Shaggy Dev、官方文档、issue #50976） | ColorRect + `hint_screen_texture` 管线；手写多 tap 模糊 | `textureLod` 模糊（Compatibility 不可靠）。注：Godot 4.3+ 的 gl_compatibility 已对 screen_texture 做 Y 归一，直接采 `SCREEN_UV`；早期方案的手动 Y 翻转会把世界垂直镜像（实机截图审计证实后移除） |
| R5 | 赛博朋克2077 故障美学（公知模式，无一手文献） | 抖动只作用 HUD 数据层，不动场景画面 | 全屏故障（掩盖弹幕判读） |
| R6 | 泰坦陨落2 头盔 HUD（公知模式，无一手文献） | 攻击方向 → 边缘弧形定向波纹，替代全屏闪红 | 宽弧遮弹幕（波纹限边缘 12% 带） |
| R7 | 地狱潜者2 危险晕影（官方补丁佐证） | 晕影/去饱和作暗示层血量语义 | 晕影过强遮视野 → 内圈半径上限 + 自适应补偿 |

管线顺序（§3 shader 按此实现）：

```text
屏幕抓取(hint_screen_texture，Godot 4.3+ 已 Y 归一直接采样) [R4]
 → 受击层：径向色差 + 手写 6-tap 径向模糊 [R2]
 → 定向波纹叠加（边缘弧形，ADD）[R6]
 → 去饱和/色偏 + 晕影 [R7]
 → 裂纹合成（采样预烘焙距离场）[R3]
渲染位置：CanvasLayer layer=1（世界之上、HUD 之下，主场景 HUD 相应抬至 layer=2；低于 OrbitalStrike 24、过场 35）
```

瞬时层先于持续层：冲击改变采样结果，持续状态叠加其上，避免裂纹边缘被色差拉花。

---

## 2. 决策记录（全部偏向性能优化）

| # | 决策点 | 定案 | 否决项与理由 |
| --- | --- | --- | --- |
| D1 | 裂纹距离场来源 | **运行时 SubViewport 单帧预烘焙**（启动一次，512²，成本 1 帧） | 离线 PNG 工具：新增资产管线与二进制资产，收益仅省 1 帧；实时 Voronoi：每帧全屏迭代 [R3] |
| D2 | LOD 实现 | **单 shader + `u_lod` uniform 分支**（uniform 分支全像素一致，GPU 开销≈0） | 双 shader/序列帧资产：双份维护 + 显存 + 资产管线 |
| D3 | 自适应可读性 | **注册表代理亮度**：`bullet_pool 活跃数×0.002 + 活跃爆炸数×0.15` 估算画面亮度，零 GPU 回读 | 屏幕像素回读（`get_texture().get_image()`）：Compatibility 下每帧回读是性能灾难，即使 0.25s 节流也有卡顿风险 |
| D4 | 径向模糊 | **手写 6-tap 向心采样**，`u_radial_blur_strength < 0.001` 时整段跳过 | `textureLod`：Compatibility 无 mipmap；半分辨率中间纹理：多一次全屏拷贝 |
| D5 | shader 参数更新 | **epsilon 变化检测**：GDScript 缓存上次值，变化 <0.001 不调用 `set_shader_parameter`；全量稳定时 `_process` 早退 | 每帧无条件上传：uniform 更新虽廉价但 16 个参数 × 60fps 是纯浪费 |
| D6 | 呼吸缩放 | **main.gd 组合**：`zoom = view_zoom_factor × breath_scale()`，仅 DYING 期逐帧应用 | MetaFX 直持相机引用：破坏层级；每帧无条件设 zoom：无谓脏标记 |
| D7 | 心跳音频 | **GameState 音效池单发触发**，`generate_audio.py` 离线生成 0.28s 双脉冲 WAV | 循环 stream：启停有硬切；心率随血量变化无法表达 |
| D8 | 受击方向传递 | **`take_damage(amount, from_pos := Vector2.INF)` 默认参数**，四处玩家受击调用点补传位置，其余调用方零改动 | 新信号在调用点发射：方向逻辑散在 4 个文件 |
| D9 | HUD 抖动 | **只抖 `_hp_bar` 与 buff chips 容器**两个控件的 position（±2px，80ms burst） | 全 HUD 抖动：几十个控件每帧位移 |
| D10 | 状态机更新 | 每帧纯算术（无三角函数热路径，正弦用引擎 `sin()` 每帧≤3 次）+ 早退 | 0.1s 节流：视觉连续性受损，脉动会跳 |

---

## 3. Shader 规格（`assets/shaders/meta_health.gdshader`）

### 3.1 uniform 表

| 参数 | 类型 | 默认 | 驱动源 | 说明 | 来源 |
| --- | --- | --- | --- | --- | --- |
| `screen_texture` | `sampler2D hint_screen_texture, filter_linear` | — | 引擎 | 屏幕抓取（4.3+ 已 Y 归一，采 `SCREEN_UV`） | [R4] |
| `u_crack_field` | sampler2D | — | D1 预烘焙 | R=距裂隙线距离(0 在线上)，G=单元 hash，B=生长门(0 边缘→1 中心) | [R3] |
| `u_hit_intensity` | float | 0.0 | hit_pulse × ripple_alpha | 受击总强度 | [R2] |
| `u_hit_dir` | vec2 | (0,0) | 攻击方向（世界→屏幕归一化） | 定向波纹方位；零向量=全边缘均匀环 | [R6] |
| `u_chromatic_amount` | float | 0.0 | pulse 包络 | 径向色差偏移（0..0.02） | [R2] |
| `u_radial_blur_strength` | float | 0.0 | pulse 包络 | <0.001 整段跳过（D4） | [R2][R4] |
| `u_ripple_phase` | float | 0.0 | GDScript 0→1 (0.4s) | 波纹推进相位 | [R6] |
| `u_crack_progress` | float | 0.0 | §4.2 曲线 | 裂纹蔓延进度 | [R3] |
| `u_crack_color` | vec4 source_color | 青 | 状态插值 | 裂纹发光色 | [R3] |
| `u_crack_density` | float | 0.0 | 状态分层上限 | 裂纹 alpha 上限 | [R3] |
| `u_crack_spread_min` | float | 0.10 | `_ready` 读配置 | 生长门下限（满血不出裂纹） | [R3] |
| `u_crack_edge_softness` | float | 0.08 | `_ready` 读配置 | 生长门激活柔和度 | [R3] |
| `u_crack_width` | float | 0.03 | `_ready` 读配置 | 裂隙线半宽（场空间） | [R3] |
| `u_heal_jitter` | float | 0.0 | GDScript 修复期 0→0.35→0 | 单元错峰消散幅度 | [R3] |
| `u_desaturation` | float | 0.0 | damage_x | 暗示层去饱和 | [R7] |
| `u_hue_cool` | float | 0.0 | damage_x | 冷青色偏强度 | [R7] |
| `u_vignette_strength` | float | 0.0 | damage_x + LOD1 回退 | 晕影 | [R7] |
| `u_vignette_inner` | float | 0.62 | DYING 收窄至 0.56 | 保中心视野 | [R7] |
| `u_heartbeat` | float | 0.0 | 心跳相位 0→1→0 | 晕影随心跳脉冲 | [R7] |
| `u_adapt_gain` | float | 1.0 | D3 代理亮度 | 可读性补偿 | [R7] |
| `u_lod` | int | 0 | 设置 | 0=全管线，1=跳过色差/模糊/波纹（D2） | [R3] |

「减少闪光」**不进 shader**：GDScript 侧在传参前折算（色差 ×0.4、心跳/抖动置零），shader 零分支（D5 精神延伸）。

### 3.2 fragment 管线（按序实现，即管线顺序的可执行版）

```glsl
shader_type canvas_item;
// …uniform 声明如上表…

void fragment() {
    vec2 uv = SCREEN_UV;                                        // 4.3+ 已 Y 归一，勿再手动翻转 [R4]
    vec2 to_center = uv - vec2(0.5);
    // 16:9 修正 + 归一：边中点 dist≈1.0、四角≈1.15，晕影/波纹/色差阈值按此标定
    float dist = length(to_center * vec2(1.0, 0.5625)) * 2.0;
    float radial_w = smoothstep(0.25, 1.0, dist);              // 边缘强、中心弱 [R2]

    // ── 1. 受击层：径向色差 + 6-tap 径向模糊（LOD1 整段跳过，D2/D4）──
    vec3 col;
    if (u_lod == 0 && (u_chromatic_amount > 0.0001 || u_radial_blur_strength > 0.001)) {
        vec2 off = normalize(to_center + 1e-6) * radial_w * u_chromatic_amount;
        col.r = texture(screen_texture, uv + off).r;
        col.g = texture(screen_texture, uv).g;
        col.b = texture(screen_texture, uv - off).b;
        if (u_radial_blur_strength > 0.001) {                  // D4：手写 6-tap 向心
            vec3 acc = col;
            for (int i = 1; i <= 5; i++) {
                float t = float(i) / 6.0 * u_radial_blur_strength * radial_w * 0.06;
                acc += texture(screen_texture, mix(uv, vec2(0.5), t)).rgb;
            }
            col = acc / 6.0;
        }
    } else {
        col = texture(screen_texture, uv).rgb;
    }

    // ── 2. 定向波纹（边缘 12% 带，ADD，LOD1 跳过）[R6] ──
    if (u_lod == 0 && u_hit_intensity > 0.01) {
        float ang = atan(to_center.y, to_center.x);
        float dir_w = pow(max(cos(ang - atan(u_hit_dir.y, u_hit_dir.x)), 0.0), 3.0);
        float band = smoothstep(0.88, 0.98, dist);             // 只取边缘带
        float wave = sin((dist - u_ripple_phase * 1.1) * 40.0) * 0.5 + 0.5;
        float ripple = band * dir_w * wave * (1.0 - u_ripple_phase) * u_hit_intensity;
        col += vec3(0.4, 0.9, 1.0) * ripple;                   // alpha 已并入 u_hit_intensity（GDScript 侧）
    }

    // ── 3. 暗示层：去饱和 + 冷青色偏 [R7] ──
    float lum = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(col, vec3(lum), u_desaturation);
    col = mix(col, col * vec3(0.85, 1.0, 1.1), u_hue_cool);

    // ── 4. 晕影 + 心跳脉冲 + 视野收窄（u_adapt_gain 补偿，D3）[R7] ──
    float vig = smoothstep(u_vignette_inner, 1.15, dist);
    float vig_a = clamp(u_vignette_strength * (1.0 + 0.35 * u_heartbeat), 0.0, 0.6) * u_adapt_gain;
    col = mix(col, vec3(0.35, 0.02, 0.05), vig * vig_a);

    // ── 5. 裂纹合成（裂隙线细带 × 生长门：progress 从边缘向中心逐区激活，修复期按单元 hash 错峰）[R3] ──
    vec3 field = texture(u_crack_field, uv).rgb;
    float gate = mix(u_crack_spread_min, 1.0, field.b) + (field.g - 0.5) * u_heal_jitter;
    float on = smoothstep(gate, gate + u_crack_edge_softness, u_crack_progress);
    float line = 1.0 - smoothstep(0.0, u_crack_width, field.r);
    float mask = line * on * u_crack_density;
    col = mix(col, u_crack_color.rgb * 0.25, mask * 0.6);         // 裂隙暗底
    col += u_crack_color.rgb * mask * 0.8 * u_adapt_gain;         // ADD 伪泛光

    COLOR = vec4(col, 1.0);
}
```

注：裂纹早期方案把「距裂隙线距离」与「径向生长门」混在 R 通道单值阈值化，阈值扫大后选中的是整片单元区域（全屏斑块/红光淹没）；拆成 R(线距)×B(生长门) 双通道后才是细裂纹网（实机截图审计证实后修正）。

距离场烘焙 shader（独立小 shader，仅 D1 启动用一次）：SubViewport(512×512) + ColorRect，fragment 内对 12 个固定种子点（常量数组，hash 于 `uv`）算 F1/F2 Voronoi（裂隙线位于单元边界，F2−F1≈0），写 R=距裂隙线距离 / G=最近单元 hash / B=生长门（径向，0 边缘→1 中心）；`_ready` 中 `RenderingServer.frame_post_draw` 后 `get_texture().get_image()` 取回转 `ImageTexture`，随即释放 SubViewport。**一次性操作，回读成本可接受**（区别于 D3 否决的每帧回读）。headless dummy 渲染走 `_cpu_bake_image()` CPU 等价回退（64²，公式与 shader 一致）。

---

## 4. GDScript 规格

### 4.1 `scripts/meta_health_fx.gd`（新文件，`class_name MetaHealthFX extends CanvasLayer`）

```text
layer = 1（世界之上、HUD 之下，主场景 HUD 抬至 layer=2）；process_mode 默认 Pausable（暂停时血量不变，效果冻结即可，早退使成本≈0）

const STATE_NORMAL := 0 / STATE_CAUTION := 1 / STATE_DAMAGED := 2 / STATE_CRITICAL := 3 / STATE_DYING := 4
const THRESHOLDS := [0.75, 0.50, 0.25, 0.20]        # 下行边界
const DENSITY_CAPS := [0.0, 0.30, 0.50, 0.75, 1.0]  # 各状态裂纹密度上限

成员：
  _state: int；_damage_x := 0.0（平滑值）；_target_x := 0.0
  _hit_pulse := 0.0；_hit_dir := Vector2.ZERO（零向量=全边缘均匀环）；_ripple_t := 2.0（>1 表示无波纹）
  _heal_jitter := 0.0；_heart_phase := -1.0（<0 表示非 DYING）；_breath := 1.0
  _mat: ShaderMaterial；_last: Dictionary（D5 epsilon 缓存）
  _cfg: Dictionary（_ready 一次性读 effects.meta_health.*）

_ready()：
  1. 全屏 ColorRect（mouse_filter=IGNORE）+ ShaderMaterial（§3）
  2. 静态 uniform：u_lod / u_crack_spread_min / u_crack_edge_softness / u_crack_width
  3. _bake_crack_field()（D1）
  4. 连接：GameState.health_changed → _on_health_changed
          GameState.player_damaged → _on_player_damaged
          GameState.player_died → _on_player_died
  5. GameState.meta_fx_lod = _cfg lod（供 hud.gd 回退判断）

_process(delta)：
  早退：|_target_x-_damage_x|<0.001 且 _hit_pulse<0.001 且 _ripple_t>1.0 且 _heart_phase<0 且 _heal_jitter 稳定 → return
  1. tau = _cfg.smooth.down_tau if 下行 else up_tau；_damage_x 指数趋近 _target_x
  2. 状态跃迁检测 → 跨阈值时触发裂纹生长过冲（+0.08，0.6s 回落）或修复期 _heal_jitter 0→0.35→0（0.7s）
  3. _hit_pulse 指数衰减（tau=0.09）；_ripple_t += delta/0.4
  4. DYING：_heart_phase 按心率推进（1.0→1.2Hz，随 x 插值）；
     每拍：_heartbeat 脉冲包络(0.3s)、_breath = 1 + 0.015*sin(相位)、触发心跳音（D7）、
     HUD 抖动 burst（调 hud 组节点 "meta_jitter" 方法，D9）
     非 DYING：_breath 趋 1，0.3s 淡出心如果包络（无硬切）
  5. D3：0.25s 节流算代理亮度 → u_adapt_gain（clamp 0.8..1.3）
  6. D5：epsilon 检测后 set_shader_parameter

_on_player_damaged(amount: float, from_pos: Vector2)：
  r = amount / GameState.max_health()
  _hit_pulse = maxf(_hit_pulse, clampf(r * 2.5, 0.15, 1.0))   # max 池化，防高频低伤累积 [R2]
  _hit_dir = 世界→屏幕方向（from_pos==Vector2.INF 时波纹退化为全边缘均匀环）
  _ripple_t = 0.0

breath_scale() -> float：供 main.gd D6 组合
breath_active() -> bool：_state == STATE_DYING 且未开减少闪光
```

### 4.2 血量-裂纹映射曲线（`_on_health_changed` 内计算）

```text
x = 1 − hp/max_hp
_target_x = x
crack_progress = pow(_damage_x, 1.6)            # shader 参数，采样：x=0.25→0.11，0.50→0.33，0.75→0.63，0.90→0.84
颜色 crossfade（带宽 0.08）：x<0.25 青 #35E0FF → <0.5 黄 #FFD23F → <0.8 橙 #FF8A3D → ≥0.8 红 #FF3B4E
去饱和 = 0.35 × pow(x, 2.0)；色偏 u_hue_cool = 0.6 × x
晕影 = min(0.5, crack_progress × 0.55)；DYING 时 u_vignette_inner 0.62→0.56（视野收窄 6%，0.3s 平滑）
```

### 4.3 集成点改动（文件 + 锚点）

| 文件 | 锚点 | 改动 |
| --- | --- | --- |
| `autoload/game_state.gd:18` 后 | 信号区 | 新增 `signal player_damaged(amount: float, from_pos: Vector2)`；新增 `var meta_fx_lod: int = 0`、`var reduce_flash: bool = false`（setter 持久化 profile，仿 aim_assist 模式） |
| `scripts/player.gd:527` | `take_damage` | 签名加 `from_pos: Vector2 = Vector2.INF`（D8）；结算成功后 `GameState.player_damaged.emit(final_amount, from_pos)` |
| `scripts/bullet.gd:196`、`scripts/enemy.gd:197`、`scripts/boss.gd:1388`、`scripts/formation_bomb.gd:87` | 玩家受击调用点 | 补传伤害源 `global_position` |
| `scripts/main.gd:85,113` | 相机 zoom | 抽 `_apply_camera_zoom()`：`zoom = Vector2.ONE × GameState.view_zoom_factor() × (_meta_fx.breath_scale() if 激活)`（D6）；`_process` 中仅 `breath_active()` 时逐帧调用；`_ready` 创建 MetaHealthFX 并挂为子节点 |
| `scripts/hud.gd:597-608` | `_update_vignette` | 顶部加 `if GameState.meta_fx_lod == 0: return`（LOD0 移交 MetaFX，`_vignette` alpha 恒 0；LOD1 保留现状作回退，D2） |
| `scripts/hud.gd` `_hp_bar` 构建处 | HpBar 全息化 | 底盘 alpha 0.25、填充段 `CanvasItemMaterial BLEND_MODE_ADD`；新增 `meta_jitter()` 方法供 D9 调用（position ±2px，80ms，仅 `_hp_bar` 与 `_buff_flow`） |
| `scripts/settings_ui.gd:214` `SET_DISPLAY` 分区后 | 新分区 | `SET_ACCESSIBILITY` 分区 + `SET_REDUCE_FLASH` toggle（`make_section_header`/`make_toggle_button` 惯例），绑定 `GameState.set_reduce_flash()` |
| `data/translations.csv` | 新键 | `SET_ACCESSIBILITY` 无障碍/Accessibility；`SET_REDUCE_FLASH` 减少闪光/Reduce flashes |
| `scripts/tools/generate_audio.py` | 新增函数 | `heartbeat.wav`：55Hz 正弦双脉冲（lub-dub），0.28s，指数包络（D7） |

### 4.4 `data/balance.json` 新增（`effects.meta_health.*`，脚本默认值须一致）

```json
"meta_health": {
  "pulse": { "scale": 2.5, "min": 0.15, "decay_tau": 0.09 },
  "chromatic": { "base": 0.006, "peak": 0.014 },
  "blur": { "strength": 0.6 },
  "ripple": { "duration": 0.4, "alpha": 0.8 },
  "crack": { "exponent": 1.6, "spread_min": 0.1, "edge_softness": 0.08, "width": 0.03,
             "glow": 0.8, "heal_jitter": 0.35, "grow_overshoot": 0.08, "grow_time": 0.6,
             "density": [0.0, 0.30, 0.50, 0.75, 1.0] },
  "desat": { "max": 0.35, "exponent": 2.0 },
  "vignette": { "max_alpha": 0.5, "inner": 0.62, "dying_shrink": 0.06 },
  "dying": { "threshold": 0.2, "heart_min_hz": 1.0, "heart_max_hz": 1.2,
             "breath": 0.015, "jitter_px": 2.0, "warn_hz": 2.5, "fade": 0.3 },
  "smooth": { "down_tau": 0.10, "up_tau": 0.80 },
  "adapt": { "interval": 0.25, "min": 0.8, "max": 1.3,
             "bullet_weight": 0.002, "explosion_weight": 0.15 },
  "reduce_flash": { "chromatic_scale": 0.4 }
}
```

---

## 5. 状态机与 VFX 时序（行为验收基准）

状态转换：下行快入（tau=0.10s）、上行慢出（tau=0.80s + 错峰消散 0.7s）；HitPulse 与状态正交；DYING 进出均 0.3s 淡入淡出，无硬切。

| 时刻 | 色差/模糊 [R2] | 定向波纹 [R6] | 裂纹 [R3] | DYING 临界层 [R5][R7] |
| --- | --- | --- | --- | --- |
| t=0–80ms | 峰值 `0.006+0.014×pulse` | 边缘 12% 带弧形波涌入 | — | — |
| t=80–300ms | 指数衰减（tau≈90ms） | 波峰向心推进 8%，alpha 衰减 | — | — |
| t=300–400ms | — | 消散完毕 | — | — |
| t=0–600ms | — | — | 跨阈值簇生长（过冲+8% 回落） | — |
| 常驻 | — | — | §4.2 曲线 | 心跳 1.0–1.2Hz；呼吸 ±1.5%；抖动 ±2px/拍；警告边框 2.5Hz 正弦；视野收窄 6% |

光敏安全：全系统无方波；所有脉动 ≤2.5Hz。「减少闪光」开启：色差 ×0.4、禁呼吸/抖动/心跳视觉脉冲（音效保留）、警告改静态边框。

## 6. 明示层与暗示层

- 明示层：`$HpBar` 保留全息化（§4.3），兜底精确数值；**不加准星环形**（准星周边为弹幕判读区 [R2/R7]）。
- 暗示层：去饱和+冷青色偏+心跳声/呼吸同步，可由「减少闪光」降级。
- 冗余度：Meta 层承载「感觉」，SegmentedBar 承载「数值」——单点兜底，避免 Helldivers2 式晕影争议 [R7]。

## 7. 测试与验收

新增 `test/meta_health_fx_test.tscn/.gd`（无头断言）：

1. `take_damage(10)` → `player_damaged` 信号携带 amount 与 from_pos；无敌帧期不发射。
2. max 池化：连续 10 次 `r=0.05` 伤害，`_hit_pulse` 不超过 0.15（不累积）。
3. 曲线采样：hp=75%/50%/25%/10% → crack_progress ≈ 0.11/0.33/0.72/0.93（±0.02）。
4. 状态机：hp 跨越 0.25 下行 instant 快入；上行修复 0.8s 后 crack_progress 回落且 `_heal_jitter` 经历过 0→0.35→0 全程。
5. DYING：hp<20% 时心率在 [1.0,1.2]Hz、`breath_scale()` ∈ [0.985,1.015]；开「减少闪光」后 `breath_active()==false`。
6. LOD1：`u_lod==1` 时 hud `_vignette` 恢复低血脉动（回退路径，D2）。
7. 早退：满血静止 60 帧后 `_process` 早退命中（插桩计数为 0 次参数上传，D5）。

人工核对：`ui_capture.tscn` 惯例窗口截图——受击色差峰、定向波纹方位、各血量档裂纹密度、DYING 收窄与抖动。

必跑验证集：`--headless --import`、`--quit-after 300`、`smoke_test.tscn`、新专项测试；改 player/hud 涉及受击与低血，加跑 `hit_logic_test.tscn`、`balance_test.tscn`。

## 8. 实施顺序（每阶段独立可验证）

- P1 受击层：shader 色差/模糊/波纹 + D8 信号 + MetaFX 骨架 → P1 验收：测试 1、2 + 截图。
- P2 裂纹系统：D1 预烘焙 + 状态机 + 曲线 → 验收：测试 3、4。
- P3 暗示与临界层：晕影/去饱和/心跳/呼吸/抖动/收窄 + main/hud 集成 → 验收：测试 5 + 截图。
- P4 收尾：无障碍 + LOD + D3 自适应 + 音频 + 测试 6、7 + 更新 AGENTS.md。

## 附：调研来源索引

- [R1] https://blog.csdn.net/ludongguoa/article/details/120773182
- [R2] https://mini.gmshaders.com/p/gm-shaders-mini-chromatic-aberration ；https://godotshaders.com/shader/radial-chromatic-aberration/
- [R3] https://godotshaders.com/shader/impact-glass-shader/ ；https://github.com/Lord0Sanz/Godot-Glass-Break-Effect
- [R4] https://shaggydev.com/2025/04/09/godot-ui-postprocessing-shaders/ ；https://github.com/godotengine/godot/issues/50976
- [R7] Helldivers 2 官方补丁说明（危险晕影条目，经 Deltia's Gaming 转述）
- [R5][R6] 行业公知设计模式，未检索到一手技术文献，仅采纳结构模式。
