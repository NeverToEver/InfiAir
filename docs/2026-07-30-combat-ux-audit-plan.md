# 2026-07-30 战斗可读性 / Boss 走位 / 辅助瞄准 / 弹速 审计与修复计划

来源：2026-07-30 用户反馈六条（敌机对比度低、小视角不攻击/弹道不可见、猎手 Boss 大视角贴顶、全视角敌机过小、辅助瞄准重设计、弹速过慢）。全部条目已源码复核，根因带 file:line 与实测数值。
完成定义：对应修复合入且全部无头测试 0 FAIL（`--import` / `--quit-after 300` / 相关 `test/*.tscn`），窗口截图人工核对可读性项。

**执行状态**：已完成（2026-07-30 全量实施：P0-1~P0-5、P1-1、P1-2 全部合入工作区；全量无头回归 30 项 0 FAIL，autoplay 480s 探针 0 异常，窗口截图可读性逐项核对通过；未 git commit）。

---

## 现状关键事实（审计基准）

- 视角档：`VIEW_ZOOM_LEVELS = {small: 1.0, medium: 1.35, large: 1.7}`（`game_state.gd:414`），默认 **small**；可见世界 = 1920×1080 ÷ zoom，相机固定 (960,540)。
- 全局尺寸杠杆 `world_scale = 1/3`；尺寸族（贴图 scale、碰撞 radius、子弹视觉）存设计值、运行时乘 ws；游戏性数值（弹速、范围、悬停带）不乘。
- 敌机运行时尺寸：贴图 × 设计 scale × 1/3 —— 普通机 **28.5 px**（玩家机 55 px 的 52%），精英 40.8–57.2 px，Boss 157.2 px；三档视角下普通机始终 ≤4.5% 屏高，且小于自身锁定环直径（52 px）。
- 敌机配色：舰体 HULL_A–D 暗紫（亮度 44–54，`scripts/tools/generate_enemy_sprites.py:22-43`）贴深蓝黑背景（清屏色 RGB(5,5,15)，亮度 ≈6）；亮色霓虹仅 1–3 设计 px，×0.15 有效缩放后亚像素化消失。
- 敌弹：22×6 设计 × `bullet_visual_scale 1.3` × 1/3 = **9.5×2.6 px**（small 档屏幕像素），红色，与 5 px 星点同量级；laser/重弹用绝对 scale 不走 ws（`enemy.gd:441`、`boss.gd:897,1094`），反衬普通弹更小。
- 敌机开火无任何视角/距离门控（`enemy.gd:401-405` 纯计时器）；「不攻击」感知来自 ① fire 概率 0.25–0.8（`balance.json` enemies.types，一波 4 架 1 型全哑火概率 ≈31.6%）② 弹不可见 ③ 大视角下悬停带顶部 25.9% 在屏外（`hover_band [150,430]` 绝对坐标，`spawner.gd:285/295/306` 与 `enemy.gd:184-189` 均未加 view 偏移）。
- 猎手 = Ⅱ型 Boss（`translations.csv:27`）。`FIGHT_Y = 230` 是按完整设计矩形写死的绝对 y（`boss.gd:74,329`、`balance.json:180`），从未做 view 适配；`_move_dash` 只写 `position.x`（`boss.gd:787-802`），x 边界 `_strafe_range()` 已 view 适配（`boss.gd:771-776`）。large 档可见顶 y≈222.4 → 锚线与屏幕顶边重合，约 45% 舰体被裁到屏外；1/3 型同基线但速度慢不易察觉。
- 玩家弹速 1200 px/s（不乘 ws），small 档纵向跨屏 0.9s；标称 DPS 66.7（10 伤 × 6.67 发/s），与弹速无关。
- 现有辅助瞄准 = 磁吸式锁定（`player.gd:382-484`：最近敌吸附 + 锁定环贴敌机 + 子弹朝锁定点直射，三档 low/medium/high 无 off 档）；无鼠标跟随准星；玩家弹无追踪（homing 仅敌方弹追踪玩家，`bullet.gd:140-150`）。

---

## P0-1 猎手 Boss（及 1/3 型）大视角贴顶 —— 纯 bug

**根因**：`FIGHT_Y` 绝对锚点未走 `view_world_rect()`，违反 AGENTS.md「所有屏幕边缘计算必须使用 `GameState.view_world_rect()`」。

**修复**：
- `boss.gd` 新增 `_fight_anchor_y() -> float`：返回 `GameState.view_world_rect().position.y + FIGHT_Y`（`FIGHT_Y` 语义改为「距 view 顶缘偏移」，与 `_strafe_range()` 边距处理对齐）。
- 替换三处使用点：入场停线（`boss.gd:553-558`，逐帧求值以支持战斗中途切视角）、P2 冲刺 RETURN 目标（`:977`）、狂暴 RETURN 目标（`:1324`）。
- 1 型 `press_depth` 下压为相对增量，随锚点自动正确。

**验证**：`boss_enrage_test` / `boss_phase_test` / `boss_pattern_test` 在默认 small 档运行（view.position.y=0，行为逐位不变）须 0 FAIL；`view_zoom_test` 新增断言：large 档下 `_fight_anchor_y()` 落在可见矩形内且距顶 = FIGHT_Y。

## P0-2 敌机悬停带 view 适配（与 P0-1 同类隐患，并案）

**根因**：`enemies.hover_band = [150,430]` 被当作绝对世界 y（`spawner.gd:285/295/306` 锚点分配、`enemy.gd:184-189` `_resolve_anchor` 钳制），large 档顶部 150–222.4 一段在屏外；`enemy.gd:29` 注释自称「view 顶部起算」，代码与注释不符。

**修复**：spawner 锚点分配与 `_resolve_anchor` 统一加 `GameState.view_world_rect().position.y` 基线（每次求解时实时取，支持中途切档）。

**验证**：`wave_pacing_test` / `enemy_combat_test` 默认档 0 FAIL；`view_zoom_test` 补 large 档锚点 ≥ 可见顶的断言。

## P0-3 敌机尺寸（全视角过小）

**根因**：enemies.types scale 0.45–0.5 × ws = 0.15–0.167 有效缩放 → 28.5–31.7 px，只有玩家机的 52%，违背 `spawner.gd:13`「舰船视觉应明显大于指示器」的设计锚（锁定环直径 52 px > 机体）。

**修复**（`data/balance.json` + `spawner.gd:15-71` 回退表同步，碰撞半径小比例跟随以保持命中手感）：

| 类型 | scale 现 | scale 新 | 运行时现 | 运行时期 | radius 现→新 |
|---|---|---|---|---|---|
| 普通 1/2/4 型 | 0.45 | **0.62** | 28.5 px | **39.3 px** | 30 → **34** |
| 普通 3 型 | 0.50 | **0.68** | 31.7 px | **43.1 px** | 34 → **38** |
| 精英 1 重甲 | 0.70 | **0.90** | 57.2 px | **73.5 px** | 34 → **38** |
| 精英 2 游击 | 0.50 | **0.68** | 40.8 px | **55.5 px** | 30 → **34** |
| 精英 3 炮艇 | 0.60 | **0.78** | 49.0 px | **63.7 px** | 34 → **38** |

- Boss 不动（157 px 已足够）；玩家机不动（55 px → 敌我比例恢复合理层次）。
- 命中域：半径 +13%（直径 20→22.7 px），仍小于机体画面，命中率轻微上浮属预期 buff 效应，与 P1 瞄准重做联动评估，不预先补偿。
- 波及面核对：波次编队槽位均分逻辑与尺寸无关；锁定环半径派生自碰撞半径自动跟随；Boss-3 池化小怪共用 enemy.tscn 同步变大；`aim_frame`（P1）框半径同样派生。

**验证**：`enemy_combat_test` / `wave_pacing_test` / `smoke_test` 0 FAIL；`visual_capture` gameplay 档截图人工核对比例。

## P0-4 敌弹可见性（小视角弹道不可见）

**根因**：敌弹 9.5×2.6 px 红色细镖混 5 px 星点背景；`effects.bullet_visual_scale=1.3` 被统一乘 ws 后有效 0.433。

**修复**：
- `balance.json` 新增 `effects.enemy_bullet_visual_scale = 2.4`（设计值；脚本 `bullet.gd` 回退同值）。敌阵营分支（`bullet.gd:_apply_faction`）改用它：运行时 2.4×1/3 = 0.8 → **17.6×4.8 px**（small 档），约为现状 1.85 倍；玩家弹仍用 `bullet_visual_scale` 不动。
- 敌阵营颜色提亮 `(1.0, 0.25, 0.2)` → `(1.0, 0.38, 0.3)`（同分支内一行）。
- 可选清理（顺手、结果逐位不变）：laser/重弹绝对 scale（`enemy.gd:441`、`boss.gd:897,1094`）改相对倍率写法（以 VISUAL_SCALE 为底，k≈5.08/1.27 与 5.53/5.53），使未来调 `enemy_bullet_visual_scale` 时比例自动跟随。
- 「不攻击」感知残余：fire 概率（0.25–0.8）是刻意难度分层，**不改**；弹可见 + 机体变大 + P0-2 屏外锚点修复后重新评估，若仍偏弱再列为独立平衡项。

**验证**：`enemy_combat_test` / `boss_pattern_test`（敌弹速/伤断言不受影响）0 FAIL；`visual_capture` gameplay + boss_fight 截图核对。

## P0-5 敌机对比度（机体 vs 背景）

**根因**：暗紫舰体亮度 44–54 vs 背景 ≈6 但色相相近（蓝紫对蓝黑）；亮色霓虹亚像素化；运行时零提亮（无 modulate/描边）。

**修复（双轨）**：
- 主轨 · 贴图重生成（治本，工具链已验证 PIL 12.2 可用）：改 `scripts/tools/generate_enemy_sprites.py` 调色板 —— HULL_A–D 亮度提升至 70–100 段并拉开与背景的色相距离（紫→紫红/品红系），霓虹能量缝加粗 1–3 px → 3–5 设计 px，RIM 棱线用量增加；重新生成 enemy_ship_1..4 / elite_ship_1..3 PNG（P0-3 尺寸放大后霓虹在运行时再放大，进一步缓解亚像素）。Boss 贴图本已更亮（亮度 51 但体积大），本轮不动。
- 副轨 · 运行时辨识增强（成熟 shmup 惯例，独立于贴图）：`enemy.gd` `_ready` 为每台敌机加一枚尾焰软光点（`CinematicFx.soft_glow`，红/品红低 alpha，尺寸族 ×ws），随舰体朝向贴尾——运动中提供轨迹可读性；精英加同色稍微光。draw call 每机 +1，场上敌机通常 ≤30，预算内。
- 不做：全机体 modulate 提亮（会洗白贴图层次，且与受击闪白 tween 冲突）。

**验证**：`visual_capture` gameplay/boss_fight 截图 A/B 人工核对（重生成前后各一张）；无头测试不受影响（贴图不换路径）。

---

## P1-1 辅助瞄准重设计（磁吸锁定 → 准星 + 40% 标记 + 框内追踪）

**目标态**（用户定义）：
1. 鼠标跟随准星（世界坐标），默认弹道 = 朝准星方向直射；
2. 随机约 40% 敌机出生即带「强辅助」标记并显示辅助框；
3. 准星置入某标记敌框内 → 出膛弹获得对该敌的追踪（homing）修正；
4. 准星不在任何标记框内 → 朝准星直射（规则 1）。

**子系统设计**：

- **准星构件**：新世界空间节点（建议挂 Player，`top_level=true` Node2D + 程序化 bracket/点，复用 `_aim_ring` 模式）。每帧跟随 `get_global_mouse_position()`；`Input.mouse_mode = MOUSE_MODE_HIDDEN` 仅在对局活跃（未暂停、未锁输入、存活）时生效，暂停/Buff/基地/结算/死亡/过场恢复系统光标并隐藏准星——同一条件驱动两处，避免双光标/无光标死角。`laser_weapon` 光束本就走原始鼠标，与准星天然一致（`buff33_test` 光束段不动）。
- **40% 标记**：`Enemy.setup()` 掷 `randf() < cfg("player.aim_assist.mark_ratio", 0.4)`（直实例化与池化 `reactivate` 均过 setup；`deactivate` 复位，防池残留）。标记终生稳定。**排除** Boss、精英炮塔事件炮台、轰炸编队战机（决策点：它们非 `Enemy` 类或属事件单位；若后续要含精英/事件单位再开配置）。精英（`Enemy` 子行为）纳入。
- **辅助框覆盖层**：单管理节点（建议 `scripts/aim_frame_layer.gd`，挂 Main，世界坐标），`_draw` 每帧遍历 `GameState.enemies` 中带标记者统一画四角 bracket 框（单节点一次 `_draw`，零逐敌节点开销）；框半径 = 碰撞半径 + `frame_pad`（指示器族不乘 ws），青色强对比 + 低频频闪；准星入框的个体框转金色高亮（即时反馈「追踪已生效」）。
- **追踪弹**：`Bullet` 新增 `homing_target: Node2D` 字段（默认 null），`_process` 优先于现有玩家追踪逻辑：目标有效则 `lerp_angle(direction.angle(), 到目标角度, homing_turn_rate × delta)`，失效则直行；字段纳入 `activate()` 池化重置清单（AGENTS 强约束）。`player._fire()` 在准星入框时把该敌写入新弹；散射多发全部追踪同一目标。`homing_time` 取大值（配置 4.0s ≈ 弹寿命），不逐帧改方向即无 DPS 口径变化。
- **档位重映射**（保留三档无 off，`smoke_test` 拒绝 off 的断言不动）：

  | 档 | frame_pad | homing_turn_rate |
  |---|---|---|
  | low | 10 | 3.5 |
  | medium | 16 | 5.5 |
  | high | 24 | 8.0 |

  新配置：`player.aim_assist.mark_ratio 0.4`、`frame_pad/homing_turn_rate` 三档表、`homing_time 4.0`（json + 脚本回退一致）。旧磁吸参数（radius/break/switch/cone/pull）删除（json + `game_state.gd` + `player.gd` 三处同步清理）。
- **删除的旧系统**：`_resolve_aim_point` 磁吸全家（`player.gd:382-445`）、`_aim_lock_target`、`_aim_ring`（`:148-157,450-484`）——锁定环显示职责由辅助框覆盖层接管；`smoke_test:660,671-674,682` 的磁吸断言**重写**为新语义（标记敌入框 → 弹向目标追踪；未入框 → 弹向准星；档位参数联动；off 拒绝）。
- **新 UI 文本**：设置页辅助瞄准描述更新（`SET_AIM_ASSIST` 相关键 translations.csv 双语同步）。

**验证**：重写后 `smoke_test` 瞄准段 + `hit_logic_test` / `buff33_test` / `pool_reuse_test`（Bullet 字段重置）0 FAIL；`--quit-after 300`；窗口截图核对准星/框/高亮三态。

## P1-2 玩家弹速提升（DPS 持平）

**根因/口径**：弹速 1200 px/s 不乘 ws，small 档纵向跨屏 0.9s、典型交战命中延迟 0.42–0.67s，手感「慢」。标称 DPS 与弹速无关。

**修复**（最小口径）：`balance.json:11` `player.bullet_speed` 1200 → **1800**（+50%），`player.gd:17` 回退同步；伤害/射速/弹数不动 → **标称 DPS 逐位不变**（`balance_test:33` 不受影响）。跨屏时间 small 0.9→0.6s、large 0.53→0.35s。有效命中率上升（对机动目标提前量需求下降）是本次改动的预期收益，不做伤害补偿；autoplay 探针对比 TTK/清波节奏，若显著偏离再列为独立平衡项。
- 同屏弹数随弹寿命缩短而下降，`autoplay_test` 阈值（MAX_PLAYER_BULLETS 300 / MAX_BULLET_POOL 150）方向安全。

**验证**：`smoke_test` / `hit_logic_test` / `autoplay_test`（480s 探针 0 ANOMALY）。

---

## 实施阶段与回归矩阵

| 阶段 | 内容 | 回归 |
|---|---|---|
| 1 | P0-1 Boss 锚点 + P0-2 悬停带 view 适配 | boss_enrage/phase/pattern、wave_pacing、enemy_combat、view_zoom（新增断言） |
| 2 | P0-3 尺寸 + P0-4 敌弹 + P0-5 副轨尾焰光点 | enemy_combat、wave_pacing、smoke、visual_capture A/B |
| 3 | P0-5 主轨贴图重生成（PIL） | visual_capture A/B（无断言影响） |
| 4 | P1-1 瞄准重设计 | smoke（重写段）、hit_logic、buff33、pool_reuse、--quit-after 300 |
| 5 | P1-2 弹速 | smoke、hit_logic、autoplay 480s |

收尾同步：新数值键与新构件（准星/aim_frame_layer/Bullet.homing_target）写入 `AGENTS.md` 对应段落；`docs/BOSS_REDESIGN.md` 补 FIGHT_Y view 适配的决策记录；瞄准语义变更若影响教程文案则同步 `tutorial.gd` 与 translations.csv。

## 明确不做

- 不动 `world_scale` 全局杠杆（1/3 是 2026-07 既定决策，本审计全部按尺寸族/游戏性族归类处理）。
- 不动敌机 fire 概率与伤害 ramp（平衡分层，待可读性修复落地后单独评估）。
- 不做敌机/Boss 弹速调整（仅玩家弹速）。
- 不给追踪弹加伤害修正（追踪即收益，DPS 口径保持不变）。
