# 战斗公平感机制设计 — 受击宽限帧 / 擦弹得分 / Boss 阶段转场与分段血条

> **状态**：**已实现**（2026-08-03 落地并通过全量验证，实现细节见文末「实现说明与特性」）。
> **定位**：三个公平感机制的单一事实源——机制一「受击宽限帧」、机制二「擦弹得分」、机制三「Boss 阶段转场清弹 + 分段血条」。实现时改动本文件相关部分必须同步更新本文档。
> **来源**：2026-08-02 竞品差评调研（`docs/archive/2026-08-02-competitor-review-audit.md`，P1-4 难度公平感审计结论）+ 网络资料调研（Flukz shmup 设计系列《Hitbox Design in Shmups》《Boss Fight Design for Indie Shmups》、Psyvariar 3 擦弹复兴、DDA 综述论文）。
> **关联**：`docs/DESIGN_BASELINE.md`（设计基线）、`docs/ARCHITECTURE.md`（脚本职责）、`docs/TESTING.md`（测试策略）。

---

## 1. 背景与目标

竞品差评聚类（44 条）暴露的三大共性问题为**战场可读性、难度公平感、重复感**。2026-08-02 公平感审计结论：InfiAir 机制层面公平性已完备（极小受击盒 2.8、入场预告线 0.6s、Boss telegraph 前摇 0.35~0.6s、spread 同屏上限、致死弹高亮、受击定向波纹），但存在两个结构性缺口：

1. **死亡感知**：单帧碰撞即结算，存在「回放里明明躲过了却被判死」的 ghost hit 可能；Boss 阶段切换无清弹/无敌过渡，玩家可能「惊喜阶段」后被残余弹幕淹没。
2. **技巧深度**：躲弹是纯防守行为，无限流刷分的重复感缺少一条「风险-回报」的主动技巧轴。

三个机制按「低成本、高回报、不扰动既有数值与构筑」原则选定：

| 机制 | 针对痛点 | 依据 |
| --- | --- | --- |
| 一 · 受击宽限帧 | 死亡感知（ghost hit） | Flukz：命中前 2–4 帧宽限，擦过不计伤 |
| 二 · 擦弹得分 | 重复感 / 技巧深度 | Psyvariar 3 复兴；Flukz：graze 环 = 风险-回报 |
| 三 · 阶段转场清弹 + 分段血条 | 公平感（惊喜阶段） | Flukz：转场清弹/无敌喘息 + 分段血条管理预期 |
| 四 · F 键弧光弹反盾 | 操控深度 / 死亡感知 | 用户提案（2026-08-02）：主动防御反击技，弹反 = 防守即进攻 |

**设计约束**（项目既定不变量，机制不得违反）：
- 可调数值只写 `data/balance.json`，脚本内保留同名回退默认值（AGENTS.md 数值约定）。
- 擦弹环属**游戏性范围族**（锁定/清弹半径、减速环同级），**不乘 `world_scale`**；玩家受击盒等机体尺寸族仍乘。
- 纯得分制：擦弹只给分，不接 buff/天赋/掉落，避免构筑爆炸。
- 高帧路径不新增每帧 JSON 查询；碰撞检测复用物理引擎 overlap，不新增热路径遍历。

---

## 2. 机制一：受击宽限帧（Bullet Grace Period）

### 2.1 概念与定位

玩家受击判定从「弹进入 Hitbox 即结算」改为「**进入暂缓 + 停留超窗结算**」：弹进入 Hitbox 后若在宽限窗口内离开（玩家正在位移、弹只是擦过边缘），不计伤；只有停留超过宽限窗口才结算。消灭「单帧碰撞误杀」——玩家在回放里明明已经躲开却被判死的差评头号来源。

只作用于敌弹→玩家的结算路径，不影响：玩家子弹、碰撞层、受击后的无敌 1.5s / 闪避 / 护甲 / 致死弹高亮等既有流程（结算入口 `take_damage` 不变，宽限只推迟"何时调用"）。

**定稿语义**：
- 宽限窗口用**时间**而非帧数（避免高刷新率下窗口漂移）。
- 上限钳制 `(0, 0.15]`（超长宽限会让「明显该中的弹穿过」，同样破坏公平感）。
- 与受击无敌正交：无敌期内进入的弹本就不结算（`take_damage` 首行守卫），宽限帧只处理无敌期外的正常结算路径。

### 2.2 数值草案（定稿）

| balance.json 键 | 默认值 | 说明 |
| --- | --- | --- |
| `player.grace_period` | `0.05` | 宽限窗口（秒），≈3 帧 @60fps（Flukz 建议 2–4 帧） |

### 2.3 实现要点（定稿）

**接入点**：`scripts/bullet.gd` `_on_area_entered()` 的 `player_hitbox` 分支（现 305 行）——受击检测在**子弹侧**（子弹 Area2D 检测到 `player_hitbox` 组），不是玩家 Hitbox 侧。改动集中此处，`player_damage.gd` 的 `take_damage()` 零改动。

1. **事件驱动流程**（无候选表、零逐帧轮询）：
   - `area_entered(player_hitbox)`：不再立即调 `take_damage`，改启动一个**一次性 Timer**（`grace_period` 秒，挂子弹下、随场景释放，遵 AGENTS 协程纪律），连接到期回调；同时连接 `area_exited`（子弹离开玩家 Hitbox）→ 取消该 Timer。
   - **Timer 到期**：调一次 `overlaps_area(player_hitbox)` 单次查询——仍在内则走既有 `take_damage(damage, global_position)` 链路（含致死高亮 `_linger_fatal()` / `_despawn()`）；已离开（极少数窗口边界情形）则放弃结算，弹按原路径继续飞行。
   - `area_exited` 先触发 → 取消 Timer（**不计伤**，弹穿过）。
2. **配置缓存**：`GRACE_PERIOD` 存 `bullet.gd` 类静态，`_ready()` 读 `player.grace_period` 一次（钳制 (0,0.15]）；热路径零 `cfg()` 查询。
3. **避免运行时计算**：`overlaps_area` 仅在 Timer 到期调用**一次**（每颗进入 Hitbox 的弹至多 1 次），不是逐帧查询；同时进入 Hitbox 的弹通常 1–3 颗（弹幕虽有 800 上限但汇聚点有限），开销可忽略。
4. **边界**：
   - 无敌/闪避/护甲/单帧守卫全部保持原语义（`take_damage` 内部不变，宽限只是"进入→暂缓→到点结算"的时序包裹）。
   - 弹在宽限期内被其他路径 `despawn`（玩家受击清弹、离屏回收）→ `area_exited` 或树退出触发 Timer 清理，防悬挂。
   - 与擦弹正交：擦弹按"进入 graze 环"计数，宽限只影响受击结算时序。

### 2.4 测试计划

新增 `test/grace_period_test.tscn`（断言场景）：

| 用例 | 断言 |
| --- | --- |
| 弹沿 Hitbox 边缘切向快速穿过（停留 < 窗口） | 无伤（Timer 被 `area_exited` 取消） |
| 弹在 Hitbox 内停留 ≥ 窗口 | 受击 1 次，且只 1 次（Timer 到点结算） |
| 停留时间恰在窗口边界两侧 | < 窗口无伤 / ≥ 窗口有伤 |
| 宽限结算后仍触发既有受击流程 | 无敌计时、清弹、致死高亮路径生效 |
| 无敌期内弹进入 | 不结算（`take_damage` 守卫回归） |
| 窗口期内弹被清弹/离屏回收 | 无悬挂 Timer（候选清理） |

回归：`hit_logic` 相关既有断言场景全绿；全量 31 断言场景 0 FAIL。

---

## 3. 机制二：擦弹得分（Grazing）

### 3.1 概念与定位

在玩家受击盒之外设**擦弹环**（环形区域：环内=受击致死区，环外但入环=擦弹）。敌弹进入擦弹环即计分，同一弹至多计 1 次。把「躲弹」从纯防守升级为「靠近擦弹得分、拉开避弹」的风险-回报决策——为无限流刷分新增一条主动技巧轴，对冲重复感。

**定位决策（定稿）**：
- 纯得分：走 `GameState.add_score()`（内部按难度倍率 1/2/3 入账），不接 buff/天赋/掉落。
- 不设擦弹得分上限：自然受弹幕密度约束，站弹海里刷分本身就是高风险高回报。
- 擦弹与受击宽限帧正交：擦弹按「进入环」计数，与受击结算独立；宽限期内的擦过弹仍计入擦弹。

### 3.2 数值草案（定稿）

| balance.json 键 | 默认值 | 说明 |
| --- | --- | --- |
| `player.graze_radius` | `20` | 擦弹环半径（运行值，**不乘 world_scale**）；受击盒 2.8 之外的环形带 |
| `player.graze_score` | `10` | 每次擦弹得分（`add_score` 内乘难度倍率） |

- 环半径相对受击盒 2.8 约 7 倍、相对机身 sprite 约 2 倍，符合「明显大于 sprite 但远小于全场弹幕」的擦弹环惯例。
- 参考节奏：普通波 3–5 机、单弹种，单波擦弹约 3–10 次；Boss 弹幕阶段单次攻击可达 10–20 次擦弹——量级与击杀分（100–500）互补而不喧宾夺主。

### 3.3 实现要点（定稿）

1. **检测（事件驱动）**：玩家下挂一个 graze `Area2D`（圆形 `radius = graze_radius`，`collision_layer = 0`、`collision_mask = enemy_bullet` 层），仅连接 `area_entered`——**物理引擎 overlap 信号驱动，零逐帧遍历/零距离计算**（弹幕 800 上限下不做每帧距离判定）。
2. **单次计数**：`bullet.gd` 加 `_graze_done: bool` 字段，`activate()` 重置清单内新增（与 `homing_target` 同级重置，池复用自动复位）。
3. **结算**：graze `area_entered`（敌弹）→ 若弹 `_graze_done == false` → `GameState.add_score(graze_score)` + 置标志 + 触发反馈。
4. **反馈三件套**（Flukz 强调：无反馈玩家无法刻意擦弹）：
   - 机身短闪光：复用 player.gd 现有「每帧按状态调 `_sprite.modulate`」模式（无敌闪烁同款），擦弹瞬间金色微闪一次（独立短计时）。
   - 小粒子迸发：复用 `Explosion.spawn_at()`（按项目惯例，不新建粒子方案）。
   - 擦弹音效：经 `GameState.play_sfx()` 池播放；若音频资产缺位则暂缺不阻塞（登记为后续音频项）。
5. **配置缓存**：`graze_radius`/`graze_score` 在 `_ready()` 读一次缓存；热路径零 `cfg()` 查询。
6. **边界**：
   - 弹反后的弹转玩家弹，不触发 graze（环只监控 enemy_bullet 层，天然排除）。
   - 环与受击盒同层监控无冲突；环不参与伤害结算。
   - 暂停态：环随玩家 process_mode 暂停，无特殊处理。

### 3.4 测试计划

新增 `test/graze_test.tscn`（断言场景）：

| 用例 | 断言 |
| --- | --- |
| 单弹进入擦弹环 | 计 1 次分（`add_score` 调用于 `area_entered` 时刻） |
| 同一弹反复进出环 | 只计 1 次（`_graze_done` 生效） |
| 弹进入受击区（< 受击盒） | 不计擦弹，走受击流程（两 Area 互不干扰） |
| 擦弹得分经难度倍率 | 中难度入账 = 10 × 2 |
| 弹池复用后擦弹标志复位 | 二次激活的弹可再次擦弹 |
| 宽限帧擦过弹 | 既擦弹（计分）又无伤（宽限） |
| 弹反后弹经过玩家 | 不计擦弹（转玩家弹，层排除） |

回归：全量 31 断言场景 0 FAIL；`autoplay_test` 长局探针（擦弹得分持续入账、无异常）。

---

## 4. 机制三：Boss 阶段转场清弹 + 分段血条

### 4.1 概念与定位

Boss 现有三阶段阈值：P1（100–70%）→ P2（70–30%）→ ENRAGE（<30%），`phase_changed` 信号已存在（HUD 血条短闪）。补两块 Flukz 明确建议的公平感设计：

1. **转场清弹 + 玩家无敌**：阶段切换瞬间回收全部活跃敌弹、取消 Boss 持续型攻击（狙击线/蓄力/冲刺线），并给玩家短暂无敌——提供喘息与「阶段边界」的明确信号，避免「惊喜阶段」后被残余弹幕压制。
2. **分段血条**：血条按阶段阈值分段（段界 = 70%/30%），每段对应一个阶段且可区分着色——玩家开打即知有 3 个阶段，管理预期，消除「以为打完却出狂暴」的挫败。

**边界（定稿）**：Boss 逃跑期（50s 超时撤离）不算阶段转换——不清弹、不给无敌（玩家主动撤离语境）。

### 4.2 数值草案（定稿）

| balance.json 键 | 默认值 | 说明 |
| --- | --- | --- |
| `boss.phases.clear_on_shift` | `true` | 阶段切换时清敌弹 + 取消持续攻击 |
| `boss.phases.transition_invincible` | `1.0` | 转场玩家无敌（秒），短于受击无敌 1.5s，只盖转场窗口 |
| `hud.boss_bar_segments` | `3` | Boss 血条分段数 |

- 转场时长沿用既有 `boss.phases.phase_shift_duration`（0.6s）。
- 段界固定为阶段阈值 `[0.7, 0.3]`（P1 段宽 30% / P2 段宽 40% / ENRAGE 段宽 30%）。

### 4.3 实现要点（定稿）

1. **接入点（事件驱动）**：`boss.gd` 现有 `phase_changed` 信号发出处（HUD 短闪同点）追加转场处理——**阶段判定由既有 HP 阈值逻辑驱动，不新增任何轮询**。
2. **清弹（复用，非热路径）**：复用 `main.gd` `_on_orbital_struck()` 的同款清弹遍历——`for child in main.get_children(): if child is Bullet: child.queue_free()`（同时覆盖 FormationBomb）。阶段切换低频（一局数次），无需缓存/池化，直接遍历可接受。
3. **取消持续攻击**：调用 `BossAttacks` 既有取消方法（`_cancel_aim_line()` / 蓄力复位 / `_cancel_sweep()`），转场前遗留的狙击线/蓄力/冲刺状态归位。
4. **玩家无敌**：复用既有公开接口 `player.set_invincible(transition_invincible)`（`player.gd:322`，转发到 PlayerDamage），零新状态机。
5. **分段血条**：`scripts/ui_segmented_bar.gd` 加**可选** `seg_weights: Array[float]` 属性（默认空 = 等分，HP/燃料/dash 条零改动）；`_draw()` 按权重分格宽、按 `value/max_value` 平滑填充末段（延续现有部分填充语义）。`_boss_bar` 设 `seg_weights = [0.3, 0.4, 0.3]` 并登记段色（P1 琥珀 / P2 橙 / ENRAGE 红）与消耗段暗化。
6. **配置缓存**：`seg_weights`/段色在 HUD `_ready()` 读一次缓存；`_draw()` 只读缓存值，值变化才 `queue_redraw()`（沿用现有节流更新语义）。
7. **边界**：
   - 逃跑期分支不触发清弹/无敌（阶段切换判定在逃跑分支之外）。
   - 段权与阶段阈值解耦：改 `phase2_hp_ratio`/`enrage_hp_ratio` 不强制同步段权（文档记录两者默认一致）。
   - 转场期间 `_waves_paused`/Boss 自身暂停语义不变，清弹不破坏事件互斥。

### 4.4 测试计划

新增 `test/boss_phase_transition_test.tscn`（断言场景）：

| 用例 | 断言 |
| --- | --- |
| HP 降到 70% 触发 P1→P2 | `phase_changed` 发出；活跃敌弹数归零；持续攻击状态复位 |
| 转场瞬间玩家受击 | 无敌期内不结算（`transition_invincible` 生效） |
| ENRAGE 触发（<30%） | 同样清弹 + 无敌 |
| 逃跑期（50s 超时） | 不清弹、不给无敌（回归既有逃跑流程） |
| 分段血条 | 段权 [0.3,0.4,0.3] 绘制；P1 段消耗后暗化；当前段高亮 |
| 非 Boss 场景 | HP/燃料/dash 条分段不变（默认等分回归） |
| 清弹性能 | 阶段切换清弹单次遍历，无逐帧轮询（代码审查断言） |

回归：`boss_pattern`、`mothership` 相关既有断言场景全绿；全量 31 断言场景 0 FAIL。

---

## 5. 机制四：F 键弧光弹反盾（Parry Shield）

### 5.1 概念与定位

按 F 展开一面**金色弧形盾**（覆盖机头前方 140° 扇区，0.5s 窗口）；窗口内进入盾区的敌方子弹被**弹反**——沿入射方向的镜面反射角、以 2 倍速度飞回，命中任意敌方单位（普通机/精英/Boss/编队机/精英炮台，全部在 `enemy` 组，结算路径全覆盖）时造成**来源弹伤害 × 1.5** 的伤害。

定位：**主动防御反击技**，与被动防御（dash 无敌、护甲、闪避）构成操作决策层——"什么时候顶盾、赌不赌这波弹"。弹反把敌弹变成自己的高伤武器，是纯得分制下「防守即进攻」的技巧轴，强化操控深度（竞品差评主题之一）。

与既有机制的关系（正交性）：
- **擦弹**：只计敌弹；弹反后的弹转玩家弹，不计擦弹。
- **受击宽限帧**：弹反在受击盒之外的盾区完成，不进入受击结算路径，两者互不干扰。
- **dash / 护甲 / 闪避**：被动保命，弹反是主动反击，共处但决策独立。

### 5.2 数值草案（机制平衡决定）

| balance.json 键 | 默认值 | 说明 |
| --- | --- | --- |
| `player.parry.duration` | `0.8` | 完整流程时长（秒）= 前摇 0.15 + 有效 0.5 + 后摇 0.15 |
| `player.parry.active_time` | `0.5` | 有效弹反窗口（秒，居中）；前后摇 = `(duration - active_time) / 2` 均分 |
| `player.parry.cooldown` | `3.0` | 硬冷却（秒，**自护盾流程结束——RECOVER 完成——起算**）；完整技能周期 = 0.8 + 3.0 = 3.8s |
| `player.parry.arc_deg` | `140` | 盾扇区角度（度），机头前方固定方向 |
| `player.parry.radius` | `60` | 盾判定半径（运行值，游戏性范围族**不乘 world_scale**） |
| `player.parry.reflect_speed_mult` | `2.0` | 弹反弹速度倍率（用户指定） |
| `player.parry.reflect_damage_mult` | `1.5` | 弹反弹伤害倍率（用户指定） |

平衡理由（核验后决策）：

- **0.8s 三阶段时间轴（用户定稿）**：`WINDUP 0.15s（前摇，无判定）→ ACTIVE 0.5s（有效弹反）→ RECOVER 0.15s（后摇，无判定）`。前摇把「弹反」从纯反应变成**预判**——玩家必须提前 0.15s 按下；后摇让机身回归常态，形成完整的一次动作节拍。
- **冷却 3.0s 自护盾流程结束起算（完整周期 3.8s，占空比 0.8/3.8 ≈ 21%）**：按下 F → 0.8s 流程（前摇/有效/后摇）→ 流程结束才进入 3.0s 冷却，HUD 槽按此周期充能。普通波 3.2s 一波、Boss 弹幕 0.9–1.6s 间隔——玩家无法「盾覆盖全场」，只能在关键弹幕波前顶盾，是**决策性资源**而非常驻免伤。若取消冷却，0.5s 盾可高频展开，Boss 弹幕将全被 1.5 倍伤害弹回，数值爆炸且被动化（失去"选时机"的技巧）。
- **伤害随局成长**：来源弹 damage 已乘 `enemy_damage_ramp`（Boss 击杀成长），弹反 1.5 倍自然随对局变强——后期弹反 Boss 狙击（21→32）、重炮（21→32），成为后期重要输出来源；因弹反机会受冷却限制，不超模。
- **2 倍速返回**：single 420→840、laser 720→1440、Boss 狙击 650→1300——快速命中但更快离屏，玩家需靠近目标弹反才有收益（决策深度），弹反弹离屏走既有池回收。
- **盾朝机头前方 140° 固定**：主威胁（敌机/Boss）恒在屏上方；侧/后方威胁（编队横穿、事件）不覆盖，保留闪避决策。**不随瞄准方向旋转**：0.5s 窗口内转瞄准负担过重，固定方向零操作成本，视觉与判定一致。
- **纯得分制兼容**：弹反不加额外分，回报即「1.5 倍伤害转嫁」本身；不接 buff/天赋，避免构筑爆炸。
- 弹反弹伤害取 `int(round(damage × 1.5))`；多弹同时入盾全部弹反（无单次上限），受盾区几何自然约束。
- 前后摇期间玩家可正常移动与受击（弹反判定关闭）；`RECOVER` 结束机身回归原色即恢复常态，不产生额外脆弱期。

### 5.3 实现要点

1. **输入**：`project.godot` 新增 `parry` 动作绑定 **F**（Keycode 70，已核验空闲：现有键为 WASD/方向/Shift/R/Space/H/B/Ctrl/K/L）；加入 `REBINDABLE_ACTIONS` 与 `ACTION_LABELS`（「弹反盾」）以支持改键页；手柄经 `_bind_joypad_defaults` 装配 **LT 左扳机**（轴 4，阈值 0.5，空闲未占用）——扣扳机顶盾的操作语义贴合。
2. **时间轴状态机**（玩家侧，`PlayerParry` 组件）：`IDLE → WINDUP(0.15) → ACTIVE(0.5) → RECOVER(0.15) → IDLE`；**冷却 3.0s 自 RECOVER 完成起算**（进入 `IDLE` 的瞬间启动冷却计时，冷却期内不可再次按下）。仅 `ACTIVE` 阶段启用盾 Area2D 判定；`WINDUP`/`RECOVER` 期间判定关闭但机身反馈进行。
3. **机身反馈三阶段**（用户定稿，复用受击闪白同路径的轻量 tint）：
   - `WINDUP`：机身金色调渐强（发光至展开），盾从玩家中心小弧展开到全弧 140°
   - `ACTIVE`：盾全弧展开，金色珍珠流光，弹反判定开启
   - `RECOVER`：盾收回，机身金色调渐弱回归原色
4. **盾视觉——珍珠流光反射**：盾面为金色半透明弧形（程序化 Polygon2D 描边 + 微光晕），其上一条**窄高光带自弧线左端（-70°）扫至右端（+70°）**，整个 `ACTIVE` 期完成一次扫过（类似珍珠被光影灯从左到右照射的反射感）；高光带角度按 active 进度线性插值（Tween 或 `_process` 进度驱动，零 shader 依赖）。
5. **HUD 护盾反应能量槽**：左下角 `DashBar` 下方新增金色能量槽（SegmentedBar 组件，复用 Hp/Fuel/Dash 同款样式，金色 `UITheme` 色）：满格=可用（金色亮）；按下 F 即清空，**0.8s 流程期间保持空**，流程结束（RECOVER 完成）起按 `cooldown 3.0s` 匀速充能回满——冷却状态直观可见。锚点左下（offset 对齐 DashBar，y 下移约 14px）。
6. **弹反**（`bullet.gd` 新增 `reflect()`，O(1) 阵营翻转，子弹池零新增）：
   - `is_player_bullet = true`（自动获得玩家弹白芯描边视觉，collision_mask=4/enemy 层，走玩家弹结算路径）
   - `direction = 镜面反射`（以盾法线=机头前方为对称轴，即 2D 下 `direction.y` 取反）
   - `speed *= 2.0`；`damage = round(damage × 1.5)`
   - `homing_target = null`（敌弹追踪玩家语义终止，反射后直行）
   - 弹反点金色爆点粒子 + 弹反音效
7. **性能**：盾 Area2D 仅 `ACTIVE` 期启用、冷却期 `disabled`，零常驻碰撞开销；热路径无新增遍历；HUD 槽充能走既有节流更新（值变化才重绘）。
8. **边界**：
   - 玩家弹/母舰弹（玩家侧）不进入盾区（盾只监控 enemy_bullet 层）。
   - 暂停态（Boss 转场/返航/页面）：盾状态机随玩家 process_mode 暂停，冷却计时同步暂停，恢复后流程/冷却延续（沿用项目暂停语义）。
   - 弹反后的弹不再可能伤害玩家（转阵营），不会与受击宽限帧冲突。

### 5.4 测试计划

新增 `test/parry_test.tscn`（断言场景）：

| 用例 | 断言 |
| --- | --- |
| 完整时间轴 | WINDUP 0.15s 内弹入盾区**不**弹反；ACTIVE 0.5s 内弹入**弹反**；RECOVER 0.15s 内**不**弹反 |
| 弹反属性 | 弹转玩家弹；方向为镜面反射（y 取反）；speed ×2；damage = 原×1.5（四舍五入） |
| 弹反弹命中普通敌机 | 按 1.5 倍伤害结算，敌机 HP 扣减正确 |
| 弹反弹命中 Boss | 同样生效（Boss 在 enemy 组） |
| 扇区外（140° 外）敌弹 | 不弹反，走既有受击流程 |
| 硬冷却 | 流程结束（RECOVER 完成）起 3.0s 内再按无效；满 3.0s 后可再次展开（完整周期 3.8s） |
| 机身反馈 | WINDUP 金色 tint 渐强、ACTIVE 保持、RECOVER 回归原色（逐阶段断言 tint 值） |
| HUD 能量槽 | 满格=可用；按下即清空且流程期保持空；流程结束起按 3.0s 匀速充能回满 |
| 弹反弹离屏/命中后 | 走既有池回收；二次激活状态复位（含 `reflect` 相关字段） |
| 与擦弹/宽限帧正交 | 弹反弹不计擦弹；盾展开期受击宽限帧不受影响 |

回归：全量 31 断言场景 0 FAIL；`autoplay_test` 长局（弹反不破坏既有受击/擦弹计数）。

---

## 6. balance.json 新增键总览

| 区块 | 键 | 默认值 | 归类 | 脚本回退位置 |
| --- | --- | --- | --- | --- |
| `player` | `grace_period` | `0.05` | 玩家（秒） | `player.gd` 常量区 |
| `player` | `graze_radius` | `20` | 游戏性范围（不乘 world_scale） | `player.gd` 常量区 |
| `player` | `graze_score` | `10` | 玩家（得分） | `player.gd` 常量区 |
| `boss.phases` | `clear_on_shift` | `true` | Boss | `boss.gd` 常量区 |
| `boss.phases` | `transition_invincible` | `1.0` | Boss | `boss.gd` 常量区 |
| `hud` | `boss_bar_segments` | `3` | HUD | `hud.gd` 常量区 |
| `player.parry` | `duration` | `0.8` | 玩家（秒，完整流程） | `player.gd` 常量区 |
| `player.parry` | `active_time` | `0.5` | 玩家（秒，有效窗口居中） | `player.gd` 常量区 |
| `player.parry` | `cooldown` | `3.0` | 玩家（秒，硬冷却） | `player.gd` 常量区 |
| `player.parry` | `arc_deg` | `140` | 游戏性范围（不乘 world_scale） | `player.gd` 常量区 |
| `player.parry` | `radius` | `60` | 游戏性范围（不乘 world_scale） | `player.gd` 常量区 |
| `player.parry` | `reflect_speed_mult` | `2.0` | 玩家（倍率） | `bullet.gd` 常量区 |
| `player.parry` | `reflect_damage_mult` | `1.5` | 玩家（倍率） | `bullet.gd` 常量区 |

实现时：新增键后运行 `python3 scripts/tools/gen_balance_map.py` 刷新 `docs/BALANCE_MAP.md`，并检查「双向反查」无新增失配条目。

---

## 7. 文档与翻译同步

- 实现落地后：本文档回填「实现说明与特性」小节（参照 `docs/ELITE_TURRET_EVENT.md` 惯例），状态改为**已实现**，登记落地提交。
- 玩法规则变化 → 同步 `docs/DESIGN_BASELINE.md`（设计基线）相关小节；方向决策 → 同步 `docs/ROADMAP.md`。
- **输入映射变化 → 同步 `AGENTS.md` 输入清单**（新增 `parry`（F/手柄 LT））；`project.godot`、`game_state.gd`（REBINDABLE_ACTIONS/ACTION_LABELS/_bind_joypad_defaults）与设置页改键区需一并变更。
- 新增用户可见文本 → 中英双语写入 `data/translations.csv`（本设计预计无新增文本；弹反反馈为视觉/音效与数字伤害，无文案；若设置页改键区需「弹反盾」标签则补 `ACTION_LABELS` 中文标签即可）。
- 完成后按 `docs/TESTING.md` 五层门禁验证，并登记 `docs/AUDIT_VAULT.md` 修复起效记录（如适用）。

---

## 8. 后续方向（B 档，本文档范围外，按实机反馈排期）

| 机制 | 说明 | 成本 |
| --- | --- | --- |
| 每攻击独特 tell | fan/homing/cross 等瞬发攻击补独特音效/起手动作，玩家可区分「来的是什么」 | 低 |
| DDA 敌弹密度动态降档 | 受击后短暂降敌弹密度/拉长波次间隔（竞品文档 P0-2 缓行项激活）；**须保持分数公平，不降收益** | 中 |
| 死亡回放（5–10s） | 死亡后重放死因片段，最强公平感信号 | 中高 |

---

## 9. 验证清单（实施后逐项勾选）

- [x] `gdformat --check` + `gdlint`（五层门禁 ①②，2026-08-03 全绿）
- [x] `godot --headless --import --path .` 引擎警告门禁（③，无 error）
- [x] 四个新断言场景 `grace_period_test`（14 断言）/ `graze_test`（12）/ `boss_phase_transition_test`（31）/ `parry_test`（36）0 FAIL
- [x] 全量断言场景 0 FAIL（④⑤，35 场景 = 31 既有 + 4 新增，2026-08-03 全绿；含 `hit_logic_test` 4 处等待按宽限帧语义适配、`elite_turret_event_test` 场景 3 失败奖励断言改为 RP 载体——玩家弹幕中自然擦弹使 score 不再恒等）
- [x] `autoplay_test` 长局探针（180s）：擦弹得分持续入账、宽限帧不破坏既有受击、弹反不破坏计数、无新异常
- [x] `gen_balance_map.py` 刷新 BALANCE_MAP 无失配（新增 13 键全部被脚本引用）
- [ ] 实机 15 分钟+ 长局：擦弹反馈可感知、阶段转场喘息有效、弹反手感（窗口/冷却/朝向）合理、无「死得莫名」残留（**发布前人工验证项**）

---

## 10. 实现说明与特性（2026-08-03 落地）

四个机制按本文档定稿语义落地并通过全量验证（五层门禁全绿、35 断言场景 0 FAIL、180s autoplay 探针无新异常、BALANCE_MAP 双向反查无失配）。落地提交见 2026-08-03 git 记录。

### 机制一 · 受击宽限帧（`scripts/bullet.gd` + `test/grace_period_test`）

- 事件驱动实现与定稿一致：`area_entered(player_hitbox)` 启动一次性 Timer（`player.grace_period`，钳制 (0, 0.15]），`area_exited` 取消，到期单次 `overlaps_area` 复核后走既有 `take_damage` 链路；`deactivate()` 停 Timer 防悬挂；`reflect()`（机制四）同步取消宽限。
- `hit_logic_test` 4 处「静止弹重叠玩家」用例的等待按新语义适配（宽限期后结算），A16 同帧单结算语义不变（单帧守卫兜底）。

### 机制二 · 擦弹得分（`scripts/player.gd` + `scenes/player.tscn` + `test/graze_test`）

- GrazeArea（mask=enemy_bullet 层，`player.graze_radius` 运行值不乘 world_scale）事件驱动；同一弹至多 1 次（`Bullet.try_graze`，池化 activate 复位）。
- **受击区排除**：弹与受击盒重叠不计擦弹。定稿建议的 `overlaps_area` 在物理回调内返回陈旧结果（实测受击盒内弹被误放行），落地改单次距离判定（事件驱动内一次计算，非逐帧遍历）；另加 `is_active()` 守卫——area_entered 信号延迟 flush 时已回收弹不计分。
- 反馈三件套：机身金色短闪 + `Explosion.spawn_at` 小粒子 + 音效**暂用 `SFX_BUFF_PICK` 占位**（缺专用擦弹音效，登记为后续音频项）。

### 机制三 · Boss 转场清弹 + 分段血条（`scripts/boss.gd` / `ui_segmented_bar.gd` / `hud.gd` + `test/boss_phase_transition_test`）

- `_transition_cleanup()`：阶段切换（P1→P2 与 ENRAGE）清全部活跃弹丸（含 FormationBomb，main 同款遍历）+ 玩家短暂无敌（`boss.phases.transition_invincible`，**只增不减**——不覆盖受击 1.5s 等更长无敌）；逃跑期不经阶段切换天然豁免。
- 分段血条：`SegmentedBar` 新增 `seg_weights`/`seg_colors`（空 = 既有等分回归，HP/燃料/dash 条零改动）+ `segment_fill()` 纯函数（绘制与测试共用）。**段序从左到右 = P1→P2→ENRAGE（与文档数组顺序一致），消耗从血条左端（P1 段）开始**——段内亮区在右侧（靠满血侧），`hud.boss_bar_segments` 控制段数。
- 新增 `hud` 配置区块（`boss_bar_segments`），`docs/BALANCE_MAP.md` 已刷新。

### 机制四 · F 键弧光弹反盾（`scripts/player_parry.gd` 新组件 + `player.gd` / `bullet.gd` / `game_state.gd` / `project.godot` / HUD + `test/parry_test`）

- `PlayerParry` 组件（RefCounted，同 PlayerDash 模式）：IDLE→WINDUP(0.15)→ACTIVE(0.5)→RECOVER(0.15)→IDLE，冷却 3.0s 自 RECOVER 完成起算（相位边界 epsilon 容差）；暂停随玩家 process_mode 冻结。
- 输入：`parry` 动作绑定 F（keycode 70），入 `REBINDABLE_ACTIONS`/`ACTION_LABELS`（「弹反盾」）与 `JOYPAD_ACTIONS`，手柄经 `_bind_joypad_defaults` 装配 LT（轴 4 负向）。
- **判定实现偏离定稿**：定稿建议盾 Area2D 用扇形 shape（ConvexPolygonShape2D）；实测该 shape 对「圆心+弧」扇形的内外判定不可靠（后方扇区外弹被误弹反），落地改**圆盘 shape 触发 + 回调内精确扇形过滤**（距离 ≤ 半径 且 角度在机头前方 ±arc），几何与视觉严格一致。
- 弹反：`Bullet.reflect()` O(1) 阵营翻转（y 镜面反射、×2 速、×1.5 伤四舍五入、终止追踪、取消宽限）；命中普通机/精英/Boss 全覆盖（enemy 组）。
- 视觉：程序化金色弧 + 珍珠流光高光带（ACTIVE 期自弧线左端扫至右端，零 shader）；机身三阶段金色 tint（优先级高于无敌闪烁/擦弹闪光）；HUD 左下 DashBar 下方金色能量槽（满格可用 → 流程清空 → 3.0s 匀速充能，`UI_PARRY` 翻译键中英双语）。
- 音效**暂用 `SFX_DASH` 占位**（缺专用弹反音效，登记为后续音频项）。

### 测试与回归适配（设计变更引起的既有测试调整）

- `hit_logic_test`：4 处等待按宽限帧语义适配（见机制一）。
- `elite_turret_event_test` 场景 3「失败无奖励入账」：玩家在炮台弹幕中自然擦弹得分（机制二设计行为），score 不再恒等；断言改为奖励载体 RP 不变，并注释说明。
- `autoplay_test` 探针修复（2026-08-03）：7 处毫秒→秒显示除法（integer_division，66c1c9e 门禁升级遗留）加 `@warning_ignore` 注解——探针自 08-02 起编译失败未执行，属未登记失败基线（见 `docs/AUDIT_VAULT.md` 测试基础设施补记 2026-08-03）；修复后 60s/180s 探针 0 异常。
- 新增 4 个断言场景合计 93 断言；`docs/TESTING.md` 场景清单已同步（35 个断言场景）。
