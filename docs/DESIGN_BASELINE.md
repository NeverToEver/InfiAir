# InfiAir 设计基线（DESIGN_BASELINE）

> **文档地位**：本项目设计意图与架构约定的**唯一修正文档**。审阅技术债、评估改动影响、规划未来工作时，以本文为权威基准；专项设计文档（`BOSS_REDESIGN` / `META_HUD_DESIGN` / `ELITE_TURRET_EVENT` / `FORMATION_STRIKE_EVENT` / `INTRO_CINEMATIC` / `RETURN_HOME_CINEMATIC` / `ENDLESS_BALANCE_PLAN` / `EXIT_FLOW`）提供各系统的实现级细节，冲突时以本文为纲、专项文档为目，二者不一致时以本文为准并修订专项文档。
>
> **维护约定**：任何方向/架构/数值口径调整，须在此登记并同步 `AGENTS.md`「文档同步要求」；技术债修复后在此回填状态并同步 `docs/AUDIT_VAULT.md`。
>
> **状态快照（2026-08-03 修订）**：十轮系统审计（A–L 系列，含 2026-08-03 软件工程维度全面评价，见 `docs/AUDIT_VAULT.md`）全部处置完毕，无 P0 遗留；37 个无头断言场景 0 FAIL（2026-08-03 实测）；A 系列 SOLID 遗留仅 **A8**（Player 视觉职责抽离）与 **A5**（残余依赖收敛，注入已落地）未收敛（见 §7）；性能优化与公平感四机制（2026-08-03）全量落地。

---

## 目录

1. [产品与玩法设计基线](#1-产品与玩法设计基线)
2. [技术架构基线](#2-技术架构基线)
3. [全局不变量与开发约定](#3-全局不变量与开发约定)
4. [数据驱动与数值体系](#4-数据驱动与数值体系)
5. [测试与验证基线](#5-测试与验证基线)
6. [持久化与安全边界](#6-持久化与安全边界)
7. [已知技术债清单](#7-已知技术债清单)
8. [未来工作方向](#8-未来工作方向)

---

## 1. 产品与玩法设计基线

### 1.1 产品定位

单机 2D 俯视空战射击（shoot 'em up / score attack），Godot 4.6 + GDScript，GL Compatibility 渲染器，设计视口 1920×1080（`canvas_items` 拉伸、`keep` 宽高比）。**纯得分制**：无掉落、无拾取物、无装备；得分是唯一进度货币。早期重制自 Python/Pygame `airwar-game`，已脱离原作独立演进（历史对齐记录归档冻结于 `docs/archive/PORTING_PARITY.md`）。

### 1.2 核心对局循环

```
自动射击与波次刷怪 → 分数里程碑 Buff 三选一 → 3 类 Boss 轮换及狂暴阶段
→ 母舰补给/火力平台 → 返航基地中场整备 → 回到同一局继续
```

单局无限延伸（无限流，见 §1.4），无固定关卡终点；终局范式为**必死曲线**（玩家成长有限、敌方无限加压，玩家终将败北，得分才有意义）。

### 1.3 评分与经济

- **得分**：`GameState.add_score(v)`，内部**乘难度倍率**（易 ×1 / 中 ×2 / 难 ×3）。击杀普通敌机、Boss、事件单位均由此入账。
- **Boss 击杀**：`GameState.add_boss_kill(score_scale)` → `add_score(500 × score_scale)`，并推进 RP / boss_kills 计数 / 难度成长。
- **RP（局内经济）**：随击杀/分数积累，供母舰补给使用；不跨局继承。
- **里程碑**：分数里程碑到达时触发 Buff 三选一（`buff_select`），Buff 卡由 `buff33_test` 与 `buff_panel_test` 覆盖。

### 1.4 难度与无限流曲线（单一事实源 `docs/ENDLESS_BALANCE_PLAN.md`）

**终局范式（决策 D1，2026-07-29 收口）**：必死曲线——玩家成长有硬上限，敌方成长无顶。

- **难度乘数**：`mult = 1 + progression.per_boss_kill(0.5) × boss_kills + 时间分量`。
  - **时间分量**：按 `progression.time_step_seconds`（30s）量化步进，每 10 分钟 + `progression.per_ten_minutes`（1.0），即 `floor(run_time / 30) × 0.05`；只计对局存活时间 `run_time`（树暂停不计），量化避免 HUD 连续漂移、测试可钉档。
  - **完全去硬顶**：废弃旧 `2^n + ×8 封顶` 公式。`GameState._recompute_difficulty()` 统一计算（击杀触发 + 时间档触发 + 存档恢复重算），跨档时广播 `difficulty_changed`。
- **敌方成长对顶**：Boss HP 随 mult 线性放大（50s 逃跑 DPS 检查自然转化为「打不死则逃跑」的压力阀）；`enemies.hp_ramp_factor` / `enemies.damage_ramp_factor`（k=0.08）/ 刷怪间隔 ramp 全部获得无限加压通道。
- **生存轴收紧**：`extra_life` 上限 99→**10 层**（总 HP 100+500=600 封顶），卡面「可无限叠加」→「最多 10 层」；吸血按上限 10% 回血的正反馈由 HP 上限 + 无限伤害 ramp 共同对冲。
- **事件单位吃难度**：炮塔/编队战机 HP 乘 `GameState.enemy_hp_ramp()`，统一口径。
- **决策 D2**：hard 难度 Buff 节奏最快（得分 ×3、里程碑阈值仅 ×1.5）为**有意设计**（避免高难 Buff 节奏过稀），数值不动。
- 新键顶层段 `progression`；脚本 `cfg()` 回退值与 json 一致。

### 1.5 Buff 体系

- 16 种 buff（`ui_buff_icons` 程序化字形 + 分类配色），经里程碑三选一获得，多数可多层堆叠（层数上限由 `buffs.*.max_stacks` 约束，extra_life 例外收紧至 10）。
- 卡面文本走 `BUFF_%s_DESC` 翻译键（单一事实源），池内 `desc` 死文本已删除。
- 关键缩放系数：`buffs.rapid_fire.factor`（射速间隔 ×0.75 = +33%/层，卡面文案一致）、`armor.multiplier`、`evasion.chance`、`regen.heal_per_sec`、`slow_field.factor`、`laser_beam.*`（线段判定，非弹丸）、`explosive.*`（解锁门槛 `boss_kills>=3` 入配置）、`mothership_recall.cooldown_factor` 等。
- **辅助瞄准**（`player.aim_assist`）：敌机按 `mark_ratio`（0.25）掷 `aim_marked` 标记，AimFrameLayer 画 bracket 框，AimCrosshair 跟随 `aim_point()`；准星入框时新弹写 `Bullet.homing_target` 追踪（`homing_time` 限制），未入框朝准星直射；磁吸/弱追踪共用距离衰减（400px 内全辅助 → 1400px 线性降至 0.3 下限）。

### 1.6 Boss 体系（单一事实源 `docs/BOSS_REDESIGN.md`）

- **3 类 Boss 轮换**：第 N 只 = 第 `(N-1) % 3 + 1` 种，由 `spawner._spawn_boss()` 按击杀数轮换。
- **阶段模式表驱动**：P1/P2/ENRAGE，模式表 `boss.phases.typeN` + telegraph 前摇；三型差异化狂暴（`boss.enrage.type_*`），狂暴期玩家减速 ×0.35 而非定身；难度分档在 `_ready` 一次性乘算（`boss.difficulty_scaling`：弹数/间隔/弹速三档）。
- **战斗锚线**：`FIGHT_Y` 为距 view 顶缘偏移，使用点一律走 `_fight_anchor_y()`。
- **逃跑机制**：50s 超时逃跑（DPS 检查的压力阀）；逃跑**不推进轮换、不给休整**（B3 契约），血条隐藏 + 生成器重排。
- **实现结构**：门面 `Boss` + 4 职责类 `BossFire`（弹幕）/`BossAttacks`（攻击状态机）/`BossMovement`（移动 + P1 下压）/`EnrageSequence`（狂暴状态机）。已随 A3/A4 收敛（2026-08-03）：攻击/移动/狂暴三张注册表 + 机型参数表驱动，无按机型分支特判（原「集中 match 仅搬迁、残留 7 处」遗留已关闭）。

### 1.7 母舰与返航

- **母舰召唤**：蓄力（`dock` H）完成触发，对局不暂停、演出期玩家锁输入 + 事件驱动无敌。机库小窗 → 穿梭门 → 母舰 DESCEND 穿出减速 → 双环减速带 → DOCKING 牵引回收玩家进保护舱（`player.enter_pod()`）→ 补给 → RELEASE（`exit_pod()`）→ 驻留/离场。数值在 `effects.mothership_summon`。
- **火力平台**：驻留期 GATLING 扫射 / MISSILE 目标打击，火力掩护。
- **返航**：长按 B（`homecoming`，`effects.home_charge_time` 蓄力）→ 锁输入 → 停 spawner → 收回母舰 → `save_run()` → `starfield.warp(18)` → 返航过场 → 落基地 UI（树保持暂停）。
- **基地整备**：`base_console.gd`，虚影空间站皮肤（`dawn_station.gd`），「继续出击」触发轨道打击清场（`orbital_strike`，Boss 保留、逐机爆炸）后播战机入场动画恢复对局。
- **入场衔接动画**：开场过场播完与「继续出击」清场后播放（`player.play_entry_animation()`，数值 `player.entry`）——高速冲入定位到屏幕下 1/3 → 向后（下）缓移一小节，期间仅左右可调/上下锁定、全程无敌（不闪烁），敌机生成延迟到动画结束；替代原"原地无敌闪现"入场。

### 1.8 事件系统

**精英炮塔事件**（`docs/ELITE_TURRET_EVENT.md`，重型 30s 事件）：
- 打击航母自屏顶降入为背景舞台，升起多座自动索敌炮台（每座独立血条/开火/弱锁定追踪）；纯得分制，弹药全复用敌侧弹种，不新增弹药类型。
- **与 Boss 互斥**：Boss 触发被冻结至多一次不累积（`_boss_frozen`/`_boss_pending`），事件期普通波次暂停（`_waves_paused`），事件结束经 `BOSS_DELAY` 恢复。
- 三节点敌方台词（`ETQ_1..10`，10 选 3 无放回）+ 左下通讯浮层（`comm_overlay`）；奖励 `reward_score` 500（乘难度倍率），超时失败无奖励。
- 触发：分数 ≥`min_score`(800) 后每 `trigger_interval`(45s) 以 `trigger_chance`(0.35) 掷签，事件后 `cooldown`(60s)。

**轰炸编队事件**（`docs/FORMATION_STRIKE_EVENT.md`，最低优先级随机事件）：
- 3/4/5 架（按难度）攻击机楔形编队下降 → 90° 转航向横穿 → 逐架投引信制炸弹（落点预警环随引信收缩，AoE 只伤玩家）→ 离场。全歼有奖励。
- **不冻结 Boss**（Boss 到期照常触发，靠预警圈控叠加），但**占用波次槽**（运行期暂停普通波次，共用 `_waves_paused`，与精英炮塔互斥）；可被返航 `abort()` 打断。
- 触发：Boss 未激活 + 精英炮塔非激活 + 冷却结束 + 分数 ≥`min_score`(500)。

**优先级链**（spawner `_process` 每 tick 顺序检查，前者启动则本 tick 跳过）：Boss（最高）→ 精英炮塔 → 轰炸编队（最低）。

### 1.9 Meta HUD 血量与受击反馈（单一事实源 `docs/META_HUD_DESIGN.md`）

- 全屏后处理 CanvasLayer layer=1（世界之上、HUD 之下；HUD 抬至 layer=2），`meta_health.gdshader` + `hint_screen_texture`。
- 管线：受击层（径向色差 + 手写 6-tap 径向模糊）→ 定向波纹（边缘 12% 带）→ 去饱和/冷青色偏 + 晕影 → 裂纹合成（Voronoi 距离场一次性预烘焙，窗口 SubViewport GPU 512² / headless CPU 64² 等价回退）。
- 血量状态机：NORMAL/CAUTION/DAMAGED/CRITICAL/DYING（阈值 0.75/0.50/0.25/0.20）；下行快入（tau 0.10s）、上行慢出（tau 0.80s + 错峰消散）；DYING 心跳 1.0–1.2Hz、呼吸 ±1.5%、HUD 抖动 ±2px、视野收窄 6%。
- 明示层（SegmentedBar 血条，数值兜底）+ 暗示层（去饱和/晕影/心跳，可被「减少闪光」降级）；`reduce_flash` 开启时色差 ×0.4、禁呼吸/抖动/心跳视觉脉冲（音效保留）。
- 自适应可读性：注册表代理亮度（bullet 活跃数 ×0.002 + 爆炸数 ×0.15），零 GPU 回读；LOD1 降级跳过色差/模糊/波纹。

### 1.10 过场演出

- **开场过场**（`docs/INTRO_CINEMATIC.md`）：6 镜头 17.3s 硬科幻开场（站毁→逃生→驶向深空），2.35:1 letterbox，字幕卡 `INTRO_SUB_1..6`；开始面板「新游戏」触发；门禁 `current_scene == Main`（继续对局/教程/测试不触发）；Esc/任意键/点击跳过。播放期树暂停，根 `process_mode=Always`。阶段 1–3 已实施，**阶段 4（低配复测/手柄移动端适配/README 说明）未完成**。
- **返航过场**（`docs/RETURN_HOME_CINEMATIC.md`）：7 镜头 11.8s（跃迁→被捕获→入睡），架构镜像开场；Esc 经 `SKIP_RETURN` 跳过（1.2s 输入宽限防误触，`effects.return_skip_grace`）；播完/跳过统一落基地 UI，树保持暂停，镜头 7 渐暗期 BGM 淡出。
- **共享工厂**：`cinematic_fx.gd`（soft_glow/particles/shockwave/beam/speed_lines/radial_streaks，驱动类零堆分配）、`dawn_station.gd`（站体毁灭态/全息虚影态工厂，开场镜头 1、返航镜头 2/3/4、基地背景共用）。

### 1.11 教程

- 独立场景 `scenes/tutorial.tscn`，自处理返回（Esc 退出教程回主界面，不进 BackNavigator 状态机）。
- 与正局逻辑对齐：`_ready` 创建 AimFrameLayer（辅助瞄准在教程内有效），阶段 1 强制标记靶机，阶段 4 长按 H 蓄力 → 穿梭门 → 母舰 `begin_warp_in` → 对接补给（略去机库小窗，实体路径同 main）。
- 进入时隔离对局状态与存档，离开必须恢复 `Engine.time_scale = 1`。

### 1.12 退出/返回导航（单一事实源 `docs/EXIT_FLOW.md`）

- 所有平台返回输入统一收敛 `BackNavigator.go_back()`，经纯决策函数 `decide_back_action()` 分发（确认窗→过场跳过→设置/基地/阻塞态/结算→buff 栏→暂停→顶层→战斗）。
- 页面层级：L3 模态 ExitConfirm → L2 覆盖（Settings/Base/GameOver/Buff/过场）→ L1 对局（HUD⇄Pause + buff 滚动栏）→ L0 StartPanel。
- 战斗中退出需二次确认（红色警告丢进度），确认后 `_execute_exit_cleanup`：存 profile、战斗中删 save、停止未播完音效、淡出退出。
- 平台：PC Esc / 手柄 `ui_cancel` / Android 系统返回手势，同一状态机。

### 1.13 战斗公平感机制（机制与数值定稿见本小节；实现与验证细节见 `docs/archive/2026-08-03-combat-fairness-plan.md`）

- **受击宽限帧**：敌弹进入玩家 Hitbox 暂缓 `player.grace_period`（0.05s）结算，窗口内离开（擦过边缘）不计伤——消灭 ghost hit；只改敌弹→玩家的结算时序，`take_damage` 守卫（无敌/闪避/单帧）零改动。
- **擦弹得分**：受击盒外环形带（`player.graze_radius` 20，游戏性范围族不乘 world_scale）进入即计 `player.graze_score`（10，经难度倍率入账），同一弹至多 1 次；纯得分制不接 buff/天赋；受击区（受击盒内）不计擦弹。
- **Boss 阶段转场公平感**：P1→P2 与 ENRAGE 切换清全部活跃弹丸（含编队炸弹）+ 玩家短暂无敌（`boss.phases.transition_invincible` 1.0s，只增不减）；逃跑期不清弹不给无敌。Boss 血条分段（`hud.boss_bar_segments` 3，段序 P1 琥珀/P2 橙/ENRAGE 红，段界 = 阶段阈值宽占比，消耗从左端开始）。
- **F 键弧光弹反盾**：140° 机头前方扇区 0.5s 有效窗（前摇 0.15/后摇 0.15），弹反 = 镜面反射（y 取反）×2 速 ×1.5 伤（四舍五入）转玩家弹；硬冷却 3.0s 自流程结束起算（完整周期 3.8s，决策性资源）；`player.parry.*` 数值全部入 balance.json。手柄 LT 装配。

---

## 2. 技术架构基线

### 2.1 技术栈

- **引擎**：Godot 4.6（标准版，无 .NET），`project.godot` 声明 `4.6` + `GL Compatibility`，桌面/移动端均 `gl_compatibility`。
- **语言**：纯 GDScript；`scripts/tools/` 为离线 Python 工具（标准库，贴图生成器另需 PIL），非运行时依赖。
- **资源**：`assets/sprites/` PNG、`assets/audio/` WAV、`assets/fonts/NotoSansSC.ttf`（OFL 开源）。
- **渲染**：无 HDR bloom/Compositor；自发光用 ADD 混合伪泛光（`_glow()` 惯例）；全屏后处理走 canvas_item shader + `hint_screen_texture`。
- **唯一 autoload**：`GameState`（`autoload/game_state.gd`）。

### 2.2 主节点树（`scenes/main.tscn`）

```
Main (scripts/main.gd)
├─ Starfield / Camera2D
├─ Player
├─ Spawner
├─ BulletPool / EnemyPool
├─ HUD（layer=2）/ BuffUI / PauseUI / SettingsUI / GameOverUI / BaseUI
├─ StartPanel / ExitConfirm
├─ BackNavigator
├─ MetaHealthFX（运行时 _ready 创建，layer=1）
├─ AimFrameLayer（运行时 _ready 创建，世界坐标）
├─ IntroCinematic / ReturnCinematic（layer=35，运行时按需实例化）
├─ OrbitalStrike（layer=24，继续出击清场）
├─ MothershipSummonWindow（layer=24）+ WarpGate（世界坐标）
└─ EliteTurretEvent / FormationStrikeEvent（_ready 创建并登记给 spawner）
```

**约定**：所有动态对局实体挂在 Main 下，以便清场逻辑与测试遍历可见。`scenes/` 含主场景、玩家、敌机、Boss、子弹、母舰、过场、教程场景；同名行为脚本位于 `scripts/`。

### 2.3 职责与服务拆分（A2 拆分基线）

- **GameState 门面**：全局分数/HP/Buff/难度/RP/任务/路线/设置与信号总线，公开 API 委托转发，调用方与测试零感知。
- 四个**非 autoload 组合服务类**（保持「唯一 autoload」约定）：
  - `BalanceService`（RefCounted）：持 `_balance`，`load()/cfg()/enemy_hp_ramp()/enemy_damage_ramp()`。
  - `SaveManager`（RefCounted）：`exists/save/load/delete/quarantine/sanitize_num`，损坏隔离置 `last_was_corrupt`。
  - `SfxPlayer`（Node，挂 GameState 子节点）：`build_pool/play/stop_all`，headless 短路；`SFX_*` 常量保留。
  - `EntityRegistry`（RefCounted）：`enemies/player_ref/player_hitbox/bullet_pool/enemy_pool/aim_frame_layer/camera_ref` 注册增删。
- **要点**：热路径避免每帧 `get_nodes_in_group`，用注册表 `GameState.enemies` / `player_ref` / `player_hitbox`。

### 2.4 对象池与注册表

- **子弹**：统一 `GameState.bullet_pool.fire()`；`Bullet` 为 Area2D，位移在 `_physics_process`（C04），阵营由 `setup()/activate()` 区分；`activate()` 重置追踪/视觉字段清单。
- **敌机**：**已统一走对象池**（2026-08-02，性能优化计划 `920e5e9`）：普通波次、Boss-3 小怪、编队机均经 `GameState.enemy_pool.spawn()`（`USE_POOL=false` 时退化为直接实例化，作性能 A/B 对照开关）。池化实体 `reactivate()/deactivate()` 负责状态重置、注册表登记/注销、死亡信号；不要把"所有敌机已池化"当作当前事实——`USE_POOL=false` 对照模式仍直接实例化。
- **防护**：池化实体必须保留 `_active`（延迟守卫）与 `_repooling`（包 reparent 防 `_exit_tree` 误清）；`reactivate()/deactivate()` 负责状态重置、注册表登记/注销、死亡信号；外部不得绕过生命周期释放池对象。
- **爆炸**：统一 `Explosion.spawn_at()`，复用对象池（`pool_cap` 配置），`process_mode=Always`（死亡爆炸在暂停树仍播放）。

### 2.5 输入与设置

- 输入映射由 `project.godot` 定义（移动/`boost`/`fine_move`/`dash`/`dock`/`homecoming`/`give_up`/`buff_panel`/`restart`/`parry`（F，弧光弹反盾）），不改既有映射完成无关需求；键位可改（`keybind`），持久化于 profile。**手柄默认绑定运行时装配**（P0-1）：`GameState._bind_joypad_defaults()` 启动时经 InputMap 追加左摇杆移动/动作键（A/RB/LB/X/Y/L3/R3）/LT（`parry` 弹反盾）/右摇杆瞄准动作（`aim_x`/`aim_y`，`player.aim_point` 增量驱动虚拟准星）；死区经 `set_joy_deadzone()` 应用到全部手柄动作。**PS 手柄自动识别**（GUID vendor 054c，位置一致仅标签对应 ✕○□△/L1-R1，`joy_button_label()` 供 UI 显示）。
- 设置项：难度、键位、语言、视角缩放、窗口尺寸、辅助瞄准档位、`reduce_flash`、`mouse_lock`（鼠标锁定窗口内，默认开启：仅对局准星活跃且窗口聚焦时把移出内容区的鼠标拉回边缘内侧，防准星出框失控；暂停/非准星态与失焦放行）、手柄参数（`joy_aim_speed` 右摇杆瞄准灵敏度、`joy_deadzone` 摇杆死区，设置页「手柄」分区滑杆调节）、音效/音量；语言切换经 `GameState.set_locale()`，UI 监听 `locale_changed` 刷新。
- 视角缩放与窗口尺寸是**两套独立** profile 设置。

### 2.6 渲染与视觉层级

| layer | 内容 |
| --- | --- |
| 1 | MetaHealthFX（全屏后处理，世界之上、HUD 之下） |
| 2 | HUD |
| 12 | CommOverlay（通讯浮层） |
| 24 | OrbitalStrike / MothershipSummonWindow |
| 35 | Intro/Return Cinematic |
| 40 | ExitConfirm |

暂停态 UI 一律 `process_mode = Always`，经 `get_tree().paused` 管理暂停；BGM 循环只设 `loop_mode = LOOP_FORWARD`（不在 `_exit_tree` 停 BGM）。

---

## 3. 全局不变量与开发约定

> 这些是本项目代码的"定律"。任何改动（修复、重构、新功能）都必须维持，否则视为破坏设计基线。

### 3.1 碰撞与伤害

- **碰撞层**：1=`player`、2=`player_bullet`、3=`enemy`（含 Boss）、4=`enemy_bullet`。
- 玩家子弹以 `enemy` 组结算；敌方子弹与敌方实体以 `player_hitbox` 组结算。
- **玩家受击只认 `Player/Hitbox` 的 Area2D**（设计值 r=7 × `world_scale`，当前运行值 2.8）；`CharacterBody2D` 本体半径 22 圆无碰撞用途（mask=0），不得用于受击判定。
- 玩家受击调用统一 `take_damage(amount, from_pos := Vector2.INF)`，发射 `player_damaged` 信号（D8 定向反馈）。

### 3.2 机体缩放（world_scale 杠杆）

- **唯一杠杆**：`balance.json` 顶层 `world_scale`（当前 **0.4**），运行时缓存 `GameState.world_scale`。
- **机体尺寸族**（贴图 scale、碰撞 radius、muzzle/对接/炮位/牵引偏移、随机体特效比例）在 json/tscn/脚本回退三处一律存**设计值**（1.0 基准），实体 `_ready()/setup()` 统一乘 `world_scale`。
- **游戏性范围族**（AoE 半径、锁定/清弹半径、减速环）与指示器/过场/UI **不乘**。
- **幂等赋值**（`radius = 设计值 × world_scale`），严禁 `*=` 累乘（共享 sub_resource 会逐实例重复缩放）；运行时写半径的场景须 `resource_local_to_scene = true`。
- **例外**：`mothership.DRIVE_MARGIN` 乘 `world_scale` 是有意例外（舰体边缘视觉屏距恒定，B11）。

### 3.3 视口与坐标

- 相机固定在 `(960, 540)` 只调 `zoom`；一切屏幕边缘/出界/刷怪/可见区域计算必须用 `GameState.view_world_rect()`，**不得硬编码 1920×1080 / 960 / ±1600**。
- Boss 战斗锚线 `_fight_anchor_y()`、敌机悬停带/入场锚点基线均按此适配。
- 过场按 1920×1080 设计坐标布局（固定机位，属有意例外）。

### 3.4 数值访问（cfg）

- 统一 `GameState.cfg("分层.路径", default)`；缺键/损坏 JSON 回退脚本默认值，两者必须一致。
- **热路径禁止每帧 cfg()**：`_ready()/setup()` 一次性读入缓存；高频 `_process/_physics_process` 不得查 JSON 字典。
- 可调数值只改 `data/balance.json`（用 `scripts/tools/balance_editor.py`），不改脚本回退值；改完跑 `gen_balance_map.py` 刷新 `docs/BALANCE_MAP.md`。

### 3.5 三角函数与热路径

- 禁止 `_physics_process` 直调 `sin()/cos()`；用 `Enemy.sin_fast()/cos_fast()` 查表。
- 热路径不得每帧分配：演出点集预分配 + 原地写（`points[i]=` 值语义副本不生效）、`PackedVector2Array` 复用、节点引用懒加载缓存。
- 高频字段在 `_ready` 缓存；`Time.get_ticks_msec()` 每帧取一次复用。

### 3.6 协程纪律

- **禁止 `await get_tree().create_timer()` / 挂起计时器协程**（进程退出时协程状态泄漏并连带资源）。
- 延迟回调用**一次性 `Timer` 节点 + `timeout` 信号**（参考 `spawner._schedule()`），Timer 随场景树释放。
- 禁止 `await get_tree().process_frame` 后无 `is_inside_tree()` 守卫的越界访问。

### 3.7 GDScript 风格与类型

- Godot 4 官方风格：Tab 缩进、类型标注、`CONSTANT_CASE` 常量、`_` 私有前缀、`signal.emit()/connect()`。
- `setup()` 在 `_ready()` 之前调用，勿依赖 `@onready`，用 `$节点路径`。
- 新增 `class_name` 脚本后必须 `--headless --import` 刷新全局类缓存，否则引用编译失败。
- 标具体类型：`Array[int]`、`EnemyPool`、`enemy: Enemy`；裸 `Array`/`Node` 尽量收敛（C18/C20 已大部清理）。
- **已知惯例例外（C19，设计确认）**：`CONSTANT_CASE` 命名用于可变脚本回退默认值 var——项目数据模式，维持现状，不视为违规。

### 3.8 信号与生命周期安全

- 连接用 Callable；重入树须 `is_connected` 守卫；`_exit_tree` 显式断开/清理注册。
- `get_parent().get_node("X")` 链式访问须判空（`get_node_or_null`）或标 unique name 用 `%X`；热路径不得逐帧字符串节点查找（懒加载缓存）。
- 池 `_exit_tree` 清空 GameState 全局池注册，防场景卸载悬空。

### 3.9 i18n

- 所有用户可见文本走 `tr("UPPER_SNAKE_CASE_KEY")`；新增键同步 `data/translations.csv` 中英双列，重新 import 生成 `.translation`。
- 动态文本用 `%d`/`%s` 占位符；语言切换经 `GameState.set_locale()`，UI 监听 `locale_changed`。
- **禁止硬编码中文用户可见字符串**（C08/C26 已清理）。

### 3.10 对象生命周期

- 教程进入隔离对局状态与存档，离开恢复 `Engine.time_scale = 1`。
- 运行期创建节点要保存引用，不依赖 Godot 自动生成节点名。
- 母舰/过场等演出节点 `skip()/abort()` 幂等，统一出口信号。

---

## 4. 数据驱动与数值体系

### 4.1 `data/balance.json`

顶层分区（Tab 缩进、无行内对象规范 JSON，由 `balance_editor.py` 维护落盘、自动备份 `.bak`）：
`world_scale` / `player` / `enemies` / `elites` / `boss` / `spawner` / `mothership` / `buffs` / `milestones` / `difficulty` / `progression` / `effects` / `tutorial` / `elite_turret_event` / `formation_strike_event`。

关键段：
- `difficulty`：难度档倍率（得分 ×1/×2/×3、HP ×0.75/×1/×1.5、里程碑阈值）。
- `progression`：必死曲线（per_boss_kill / per_ten_minutes / time_step_seconds）。
- `boss.phases` / `boss.enrage.type_*` / `boss.difficulty_scaling`：Boss 阶段模式表、三型差异化狂暴、难度分档表。
- `effects.*`：starfield、shake、meta_health、mothership_summon、orbital_strike、explosion 等表现数值。
- `elite_turret_event.*` / `formation_strike_event.*`：事件参数。

### 4.2 数值访问与文档

- 全部 `cfg()` 调用点索引与 json/脚本双写对齐反查见 `docs/BALANCE_MAP.md`（**生成文件**，改键后 `gen_balance_map.py` 重新生成）。
- 调数值优先 `balance_editor.py`（浏览器编辑、校验、备份）。

---

## 5. 测试与验证基线

> 完整命令清单见 `docs/TESTING.md`。测试不是单元测试框架：`test/*.tscn` 启动 GDScript 场景，以 `[PASS]/[FAIL]` 输出和退出码自检。

- **最小必跑集**：`--headless --import`、`--quit-after 300`、`smoke_test.tscn`；涉存档/基地/母舰加跑 `base_system_test.tscn`。
- **全量断言**：37 个断言场景（35 既有 + 2 架构断言：`buff_effects_test`（A4 效果表）/ `boss_registry_test`（A3 注册表），断言数以 CI 实跑为准）；专项按子系统选跑（boss/事件/过场/对象池/i18n/导航等）。
- **特殊场景**：`perf_bench` 必须 `--fixed-fps 1000`；`autoplay_test` 长时异常探针（注册表一致性双向比对、动效路径、卡死计时、buff 封顶、阶段计数）。
- **测试副作用**：测试可能读写 `user://savegame.json` / `profile.json`，新测试先 `GameState.delete_save()` 并清理自身持久化；`balance_test` 会覆盖 `data/balance.json` 验证损坏回退再恢复，勿并发手编。
- **视觉验证**：窗口模式截图人工核对（headless 无可用截图）；`visual/ui/return/intro/summon/meta_fx/hud` capture 工具。

---

## 6. 持久化与安全边界

- **对局存档** `user://savegame.json`、**局外档案** `user://profile.json`；二者由 GameState 管理带版本字段；profile 保存最高分/本地高分榜/难度/键位/语言/视角/窗口尺寸/教程状态/手柄参数。
- **损坏隔离**：损坏 JSON 隔离为 `<file>.corrupt` 并置 `save_corrupt`/`profile_corrupt` 标记通知开始界面；不绕过恢复流程。
- **健壮性**：`load_profile` key_bindings 类型守卫（C02）；`_apply_balance` 校验难度表子键与 `milestones.base` 非空（C03）；布尔安全读取防 `bool("false")→true`（C16）。
- **无外部交互**：无网络/插件/远程服务/密钥；离线 `balance_editor.py` 仅监听 127.0.0.1。
- **发布**：`export_presets.cfg` + `release.sh` 双平台导出，产物 `builds/release/`（gitignore），GitHub Releases 分发不入库；`packaging/` 双平台安装/卸载脚本。

---

## 7. 已知技术债清单

> 状态图例：✅ 已修复 / ⚠️ 部分 / ❌ 未修复。完整登记与修复起效记录见 `docs/AUDIT_VAULT.md`（专有档案，禁止删除/合并）。

### 7.1 架构债（A 系列遗留）

| 编号 | 内容 | 状态 | 影响与修复方向 |
| --- | --- | --- | --- |
| A3 | Boss 攻击集中 `match` 收敛为注册表、按机型分支收敛为数据驱动（2026-08-03） | ✅ | 攻击/移动/狂暴三注册表 + 机型参数表，新增机型/攻击仅需注册，O 原则达成 |
| A4 | 开闭原则：Boss 攻击 match + 机型分支已随 A3 收敛；`player.gd` Buff 已改声明式效果表（2026-08-03） | ✅ | `BUFF_EFFECTS` 声明式效果表（pow/cap/bool 三类），新增数值型 buff 只需表加一行 |
| A5 | 依赖倒置：Boss/事件对 Spawner 依赖应注入而非 group 查找 | ⚠️ | **注入已落地（2026-08-02 订正，`bdb0274`）**：Boss/精英炮塔经 `set_spawner()` 注入引用，替换 group 查找；GameState 作配置中心+注册表是有意性能权衡，保留。方向：残余依赖收敛 |
| A8 | Player 职责拆分：受击/冲刺已抽组件，**视觉职责（尾焰/残影/准星/碰撞点/PlayerBuffVisuals）仍驻留 Player**（约 697 行） | ⚠️ | 方向：视觉类抽 `PlayerVisuals` 组件 |

### 7.2 规范/性能遗留

| 编号 | 内容 | 状态 |
| --- | --- | --- |
| C34 | `boss_pattern_test` 弹速/伤害已改读实例常量；`difficulty/buff33/elite/formation` 硬编码判定为逻辑验证锚点保留 | ⚠️（部分为设计确认） |
| 敌机生成路径 | 普通波次直接实例化 vs Boss-3 小怪走对象池，两条路径并存 | ✅ **已统一（2026-08-02）**：普通波次经 `EnemyPool.spawn()` 入池（`spawn`/`reactivate` 扩展 `p_bullet_type` 可选参），`USE_POOL` 开关保留作 A/B 对照；回归 smoke 142 / pool_reuse 12 / enemy_combat 33 PASS |

### 7.3 阶段遗留（ROADMAP Phase 0 待办）

- 死代码清理：`main.gd` 未用引用、`hud.gd` 恒假分支、零 connect 信号等（见 `docs/archive/2026-07-22-audit-fix-plan.md`）。
- 母舰 `_start_release()` 幂等守卫；`profile_corrupt` 损坏档案提示消费。
- 过场阶段 4：低配机复测、手柄/移动端输入适配、README 补过场说明（`INTRO_CINEMATIC`）。

---

## 8. 未来工作方向

> 方向类决策单一事实源：`docs/ROADMAP.md`。本文是方向拆解与落点索引。

### 8.1 近期（技术债收尾，无新玩法）

1. **架构债收敛**（§7.1）：A3（Boss 攻击注册表 + 机型数据驱动）与 A4（Player buff 声明式效果表）✅ **已落地（2026-08-03，全量 37 断言场景 0 FAIL）**；剩余 **A8**（Player 视觉职责抽 `PlayerVisuals` 组件）。所有改造须维持 §3 全部不变量 + 全量测试 0 FAIL。
2. **敌机生成路径统一**：✅ **已落地（2026-08-02，随性能优化计划）**——普通波次已统一经 `EnemyPool.spawn()` 入池（§7.2 状态更新），`USE_POOL` 开关保留作 A/B 对照。
3. **死代码清理**与幂等守卫（§7.3）。

### 8.2 中期（体验深化）

- **无限段实机标定**：`progression.per_boss_kill / per_ten_minutes / ramp 系数` 的深段（>15 分钟）手感微调，直接改 `balance.json` 并在 `ENDLESS_BALANCE_PLAN §6` 追加记录；用 `autoplay_test` 长时探针验证后期无「HP 单边膨胀、压力归零」稳态。
- **过场收尾**：`INTRO_CINEMATIC` 阶段 4（低配复测/手柄移动端适配/README）。

### 8.3 暂缓/已砍（重启需用户明确决策，登记于 `ROADMAP.md` Phase 3）

- 本地账号系统（规格存档于提交 `7aacd3f`）、独立主场景版进入页（附录 B）、联机排行榜（已决策不做）、协作与发布工程化（CONTRIBUTING/CI/语义化版本）、内容演进（新 Buff/敌机/精英/Boss/移动端触控/母舰扩展）。

### 8.4 任何未来改动必须遵守

1. 维持 §3 全部全局不变量（碰撞层/world_scale/view_world_rect/cfg/协程/i18n/热路径/池防护）。
2. 可调数值只改 `balance.json`，跑 `gen_balance_map.py` 与最小验证集。
3. 新增功能在本文 §8 与 `ROADMAP.md` 登记方向，专项设计文档落实现级规格。
4. 修复/新代码全量测试 0 FAIL（37 断言 + autoplay 探针），视觉改动窗口截图核对。
5. 技术债修复在 `AUDIT_VAULT.md` 回填"修复起效记录"。

---

*文档性质：设计意图与架构约定的单一修正基准。审核人：依据用户指示执行 · 生成：2026-08-01。*
