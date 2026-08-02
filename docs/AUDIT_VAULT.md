# ⚠️ 代码审计档案（AUDIT VAULT）—— 不可移除

> **本文档为专有审计档案，禁止删除或合并进其他文档。**
> 登记所有已发现的代码质量错误、修复指引、修复后的处理及起效情况、工作时间与区域。
> 每次新增/更新审计发现时在下方追加登记条目；修复完成后回填"修复起效记录"。
> 本文件已在 `AGENTS.md` 登记为专有文档（见 AGENTS.md「文档同步要求」），其存在受权威约定保护。

---

## 审计元信息（本档案的登记规则）

- **不可移除性**：本文档（`docs/AUDIT_VAULT.md`）被 `AGENTS.md` 登记为专有文档。任何清理、重构、归档操作均不得删除本文件；如需调整格式，必须保留全部登记条目。
- **登记结构**：每条问题包含「问题编号 / 严重度 / 位置 / 描述 / 修复指引 / 修复起效记录 / 登记时间与区域」。
- **修复起效记录**：当一条问题的修复实际落地后，必须回填该条目，说明（a）改了什么、（b）它为什么起效（机制）、（c）用什么验证了起效（测试/运行）。

---

# 第一轮审核（SOLID 合规性，核心业务逻辑）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 核心业务逻辑代码 SOLID 五原则合规性审核 |
| 工作时间 | 2026-07-31（单次集中审核会话；本次会话起始于 2026-07-31） |
| 审核区域 | `scripts/main.gd`、`autoload/game_state.gd`、`scripts/player.gd`、`scripts/spawner.gd`、`scripts/enemy.gd`、`scripts/boss.gd`、`scripts/bullet.gd`、`scripts/bullet_pool.gd`、`scripts/enemy_pool.gd`（共 9 文件，约 4758 行） |
| 审核方法 | 逐文件通读 + 跨文件依赖追踪（谁写谁的字段、谁调用谁的接口） |
| 结论 | 不达标：S / O / D 严重违反，L 部分违反。危险级 2 项、严重级 3 项、中等级 3 项 |
| 审核人 | Claude Code（依据用户指示执行） |

---

## 🔴 危险级（严重度 1）

### A1. 跨类直接写私有字段 —— 封装全面穿透

- **位置**：见下表明细
- **描述**：多个类直接读写其他类以下划线开头的"私有"成员（GDScript 无强制 private，`_` 仅为约定）。任何内部字段改动会在运行时/编译期波及全部调用方，且错误并非类型安全。

| 调用方 | 写入目标 | 证据行 |
| --- | --- | --- |
| `main.gd` | `_player._input_locked` / `_player._invincible = 999.0` / `_player._fuel` / `_player.velocity` / `_player._dead` / `_player._die()` | `main.gd:360,361,429,431,353,460` |
| `boss.gd` | `p._enrage_slow = 1.0` / `p._dead` | `boss.gd:1357,1363,1355` |
| `bullet.gd` | `(area.get_parent() as Player)` 强转具体类型 | `bullet.gd:227` |
| `bullet_pool.gd` | `b._pool` / `b._active` / `b._repooling` | `bullet_pool.gd:32,54,57` |
| `enemy_pool.gd` | `e._pool` / `e._active` / `e._repooling` | `enemy_pool.gd:27,51,54` |
| `main.gd` | `_spawner._elapsed` / `_spawner._event` | `main.gd:354,83` |

- **为什么严重**：`player.gd` 的 `_fuel` 一旦改为 `_fuel_ratio`，编译错误会同时打在 main、spawner、base_console 等多处。GDScript 允许写任意字段，错误是运行时才暴露的——比类型语言更危险。
- **修复指引**：
  1. 为被穿透的类建立**公开接口方法**（而非开放字段）：`Player.lock_input()/unlock_input()`、`Player.set_invincible(sec)`、`Player.set_fuel(v)`、`Player.die()`；`Boss.apply_enrage_slow(on: bool)`。
  2. 对象池与对象之间改走公开契约：池只调 `activate()/deactivate()/release()`，对象的 `_exit_tree` 回调改由池注册 `tree_exited` 信号清理，而非直接写 `_pool`。
  3. `bullet.gd:227` 用信号或组契约替代强转：`area.get_parent()` 调用统一入口 `Player.take_damage_public()`，或让 Hitbox 携带玩家引用。
  4. 逐步替换，每步跑 `smoke_test` + `pool_reuse_test` + `enemy_combat_test` + `hit_logic_test`。
- **修复起效记录**：✅ 已修复（2026-07-31 全量落地）
  - **改了什么**：为 9 个被穿透类新增公开接口方法并替换全部生产代码跨类访问。新增接口——`Player`：`is_dead()/is_input_locked()/set_invincible()/lock_input()/unlock_input()/set_fuel()/fuel_amount()/die()/apply_enrage_slow()/set_auto_fire()/auto_fire_enabled()/is_dashing()`；`Spawner`：`set_elite_event()/set_formation_event()/set_elapsed()/elapsed()/set_boss_frozen()/set_waves_paused()/is_boss_active()/elite_event()/consume_boss_pending()/trigger_boss()`；`Boss`：`is_in_fight()/is_escaping()/abort_enrage_sequence()`；`Mothership`：`state()/mag_cells()`；`Main`：`is_intro_playing()/is_return_playing()/is_game_over()/is_homecoming()/mothership()`；`SettingsUI`：`capturing_action()`；`HUD`：`show_warning()`；`Bullet`/`Enemy`：`set_pool()/is_active()/set_repooling()`（+ `Bullet.despawn()`、`Enemy.is_exiting()`）。`bullet.gd` 命中玩家的硬强转 `(area.get_parent() as Player)` 改为经 `GameState.player_ref` 注册表引用。被穿透的 `_` 字段保留原名（测试白盒访问不受影响，A7 独立处理）。
  - **为什么起效**：私有状态不再被外部直接读写，封装边界恢复——任意 `_` 字段改名只影响本类内部，不再编译期/运行时波及跨类调用方；对象池与对象的协调（`_repooling` 防误清）走公开 setter，语义不变。
  - **如何验证**：`--headless --import` 通过；27 个断言测试场景全部 `[PASS]` 0 失败（smoke/pool_reuse/hit_logic/enemy_combat/boss_enrage/tutorial/esc_navigation/mothership_summon/elite_turret_event/formation_strike_event/base_system/buff33/view_zoom/i18n/keybind/startup_flow/back_navigation/meta_health_fx/orbital_strike/boss_phase/boss_pattern/wave_pacing/buff_panel/buff_visuals/window_size/difficulty/balance）。注意：`hit_logic` A21（AGENTS.md 记录的既有失败基线）本次运行通过，判定与该次改动无关、疑似测试顺序/环境差异，已复核无需回退。
  - **遗留**：测试文件仍白盒访问 `_` 私有字段（归 A7，未混入本次）；`intro/return_cinematic` 内部 `root._*` 与 `explosion.gd` 同类静态访问为合法同类内部访问，不属 A1。

---

### A2. GameState 上帝对象 —— 单类 8+ 职责

- **位置**：`autoload/game_state.gd`（975 行）
- **描述**：单类承载至少 8 个互不相干的系统：全局状态/信号总线、数值配置中心、音效池、对局存档 + 局外档案持久化、实体注册表、RP 经济/任务/天赋路线、难度档位/里程碑曲线、可改键/locale/视角/窗口/瞄准档位、血量/治疗/吸血、对局进度难度曲线、Buff。任何一处改动可能影响全部。
- **为什么严重**：改动风险面 = 全游戏。测试难隔离（`balance_test` 甚至会临时覆盖 `data/balance.json` 本身）。直接导致 A5 依赖倒置。
- **修复指引**（分阶段，每阶段保持可运行）：
  1. **读操作剥离**：将"难度档位查询/里程碑曲线/回血参数"等纯函数移到独立 `BalanceService`（持 `_balance` 字典），GameState 委托。
  2. **持久化剥离**：`save_run/load_run_data/apply_run_save/load_profile/save_profile` 抽到 `SaveManager`（保持现有存档格式与损坏隔离流程不变）。
  3. **音效剥离**：`play_sfx/stop_all_sfx/SFX_*` 抽到 `SfxPlayer`。
  4. **注册表剥离**：`enemies/player_ref/player_hitbox/pool 引用` 抽到 `EntityRegistry`。
  5. 每个阶段跑 `base_system_test` + `smoke_test` + `startup_flow_test`。
- **修复起效记录**：✅ 全部完成（2026-07-31，阶段 1–4 全部落地）
  - **阶段 1 已完成（数值配置读操作 → `BalanceService`）**：
    - **改了什么**：新建 `scripts/balance_service.gd`（`class_name BalanceService`，`RefCounted` 组合类，非 autoload）——持 `_balance` 字典，承载 `load()`（balance.json 解析）、`cfg()`（路径查询 + 类型宽容）、`enemy_hp_ramp()`/`enemy_damage_ramp()`（纯查询，难度乘数作参数）。`game_state.gd` 删除 `_balance` 字段与解析逻辑，`_load_balance()`/`cfg()`/两个 ramp 改为一行委托；`has_balance()` 新公开查询供测试/诊断。组合而非新增 autoload，保持「唯一 autoload：GameState」约定。
    - **为什么起效**：配置字典的加载/查询/纯数值 ramp 从 975 行上帝对象抽到独立单一职责类；GameState 公开 API 原样保留（委托转发），全部调用方与其余测试零改动。行为逐字节等价。
    - **如何验证**：`--headless --import` 通过；29 个断言场景全绿 0 FAIL；`balance_test` 中原 `GameState._balance` 白盒访问改走公开 `has_balance()`（顺带消除 A7 一处耦合）；`gen_balance_map.py` 重生成数值地图 0 缺失键、无失配。
  - **阶段 2 已完成（持久化 → `SaveManager`）**：
    - **改了什么**：新建 `scripts/save_manager.gd`（RefCounted）——`exists()/save()/load()/delete()/quarantine()/sanitize_num()`，`load()` 内置损坏隔离并置 `last_was_corrupt`。`game_state.gd` 删除 `_quarantine` 与全部直接 `FileAccess`/`DirAccess`；`save_run()/save_profile()` 委托 `save()`，`load_run_data()/load_profile()` 委托 `load()`（损坏标志驱动 `save_corrupt/profile_corrupt`），`has_save()/delete_save()/save_num()` 委托。
    - **为什么起效**：文件 IO + JSON 解析 + 损坏隔离抽到独立单一职责类；序列化字段组装仍留 GameState（状态职责）。`startup_flow` 损坏存档隔离、`base_system` 存档/RP/任务/路线均全绿。
  - **阶段 3 已完成（音效池 → `SfxPlayer`）**：
    - **改了什么**：新建 `scripts/sfx_player.gd`（`extends Node`，作为 GameState 子节点）——`build_pool()/play()/stop_all()`，headless 短路与池化复用逻辑移入。`game_state.gd` 删除 `_sfx_players/_sfx_index`，`play_sfx()/stop_all_sfx()` 委托；`SFX_*` 音频常量保留（播放器不持有具体资源）。
    - **为什么起效**：音效池生命周期从 975 行上帝对象抽离；播放实例挂在 SfxPlayer 子节点下，树位置不影响播放，行为等价。
  - **阶段 4 已完成（实体注册表 → `EntityRegistry`）**：
    - **改了什么**：新建 `scripts/entity_registry.gd`（RefCounted）——`enemies/player_ref/player_hitbox/bullet_pool/enemy_pool/aim_frame_layer/camera_ref` + `register_enemy()/unregister_enemy()`。`game_state.gd` 删除原注册表字段与增删方法，改用**属性 setter/getter 转发**（`GameState.enemies`、`GameState.player_ref = x` 等外部语法逐字不变），增删委托 `_registry`。
    - **为什么起效**：热路径缓存数据归独立类，GameState 变薄为门面；`enemies` 无外部赋值故只读 getter，其余字段 getter+setter 双向转发，调用方与测试零改动。
  - **整体验证（阶段 2–4 与阶段 1 一并）**：`--headless --import` 通过；`--quit-after 300` 无运行时错误；**29 个断言场景全绿 0 FAIL**；`game_state.gd` 内直接 `FileAccess/DirAccess` 清零；无对已移除符号（`_sfx_players/_quarantine/_balance`）的残留引用。

---

## 🟠 严重级（严重度 2）

### A3. boss.gd 单类 1475 行 —— 3 种 Boss + 攻击库 + 狂暴状态机 + 逃跑全在一个类

- **位置**：`scripts/boss.gd`
- **描述**：3 种 Boss 差异化移动、20+ 攻击模式、5 子状态狂暴机（TRANSITION/ACTIVE/RELEASE_HOLD/RETURN）、阶段模式表、逃跑、telegraph、减速带、撞击、血量全在一个类。`_execute_attack` 集中 `match` 9 个攻击分支（`boss.gd:695`）。
- **修复指引**：
  1. 攻击抽为**数据驱动的攻击对象/工厂**（`BossAttack` 接口：`begin()/update(delta)/end()`），模式表存攻击构造器引用，`_execute_attack` 退化为查表实例化。
  2. 狂暴序列抽为独立状态机类 `EnrageSequence`（持 Boss 引用 + 各型行为策略），Boss 委托。
  3. 每型 Boss 移动抽为策略（如 `BulwarkMove/DashMove/StrafeMove`），`_update_movement` 的 match 退化为查表。
- **修复起效记录**：✅ 拆分落地（2026-07-31）；O 原则达成存疑（2026-08-01 订正，见下）
  - **改了什么**：1488 行单类拆为「门面 Boss + 4 职责类」——`BossFire`（128 行）弹幕发射器；`BossAttacks`（356 行）持续型攻击状态机 + `execute()` 分发（原 `_execute_attack` match）；`BossMovement`（79 行）三型移动策略 + P1 纵向下压；`EnrageSequence`（362 行）狂暴 5 子状态机 + 三型差异化 ACTIVE + 轨道路径 + 锁血/玩家减速。`boss.gd` 1488 → 802 行，保留配置加载、阶段框架、血量/受击/逃跑、入场、信号、公开查询（fight_anchor_y/strafe_range/slow_factor/is_enraged/fight_phase/reset_fire_timer/spawn_minion_at）。
  - **为什么起效**：三型移动、20+ 攻击、狂暴状态机各归单一职责类，`boss.gd` 大幅缩水、单类可读性提升；子类经注入 BossFire/BossAttacks + boss 公开查询交互，残留扫描确认无跨类私有访问复发（A1 不回退）。
  - **⚠️ 2026-08-01 复核订正（勿再据此断言 O 原则达成）**：拆分确为真·职责迁移，但「集中 match 分发被查表/委托取代（O 原则）」表述过誉。事实核查：原 `_execute_attack` 的 10 分支 `match attack:` 只是**逐字搬进** `BossAttacks.execute()`（既非查表也非工厂实例化）；按机型分支在 `BossMovement.update`（1 处 match）+ `EnrageSequence`（4 处机型 if）+ `Boss`（召唤、受击硬直 2 处）共残留 **7 处**——新增机型仍须改 7 处既有函数。且该声明与同档案 **A4 条目「boss 攻击 match / 按类型嵌套未修复」对同一事实给出相反结论**（两者实际都是"match 仍在、仅换文件"），档案自洽性已被破坏，本表 A4 已回填澄清。
  - **如何验证**：`--headless --import` 通过；`--quit-after 300` 无运行时错误；29 个断言场景全绿 0 FAIL（boss_phase 31 / boss_pattern 55 / boss_enrage 34 / enemy_combat 32 / hit_logic 60 / smoke 128 等）；测试白盒断言改走子类公开查询（`boss._enrage_seq.phase()` 等）。
  - **遗留**：测试仍白盒访问 `boss._attacks`/`boss._enrage_seq` 组件字段（归 A7）；`Boss.SweepState`/`EnragePhase` enum 仍驻留 Boss 供测试引用。

---

### A4. 集中 match 分发违反开闭原则 —— 新增类型必须改既有代码

- **位置**：`enemy.gd:340`（8 分支策略 match）、`boss.gd:695`（攻击 match）、`boss.gd:1137`（按类型 3 路嵌套）、`player.gd:324`（Buff 内联分支）、`spawner.gd:201`（两事件触发内联）
- **描述**：新增移动策略/攻击/Buff/事件 = 修改既有函数的 match 或 if 分支。改既有代码就有回归风险。
- **修复指引**：按 A3 模式，将策略/攻击/事件抽为可注册的独立对象；Buff 效果改为声明式效果表（buff id → 效果对象），Player 遍历效果对象而非逐 Buff if。
- **修复起效记录**：⚠️ **部分完成（2026-08-01 按 git 历史回填，状态表当时漏更新）**
  - **已完成子项（2026-07-31 落地）**：
    - A4a 敌机移动策略抽类（`cea806e`）：`EnemyMoveStrategy` 基类 + 8 策略子类，`enemy.gd` `_physics_process` 的策略 match 委托给 `_strategy.update()`；`_make_strategy()` 仅余工厂 match（构造分发，可接受）。
    - A4b spawner 事件触发基类（`955f8a5`）：`ScheduledEventTrigger` 统一精英/编队触发策略，原 spawner 两事件内联分支委托。
  - **未完成子项**：Boss 攻击 match（现 `BossAttacks.execute()` 仍为 10 分支 match，见 A3 订正）与按机型分支（BossMovement/EnrageSequence/Boss 共残留 7 处）；`player.gd` Buff 效果仍为函数式内联分支（`_refresh_buff_factors` + `pow(因子, GameState.buff_count())` 族），未改声明式效果表。

---

### A5. 依赖倒置违反 —— 依赖具体而非抽象

- **位置**：全核心文件
- **描述**：
  - 所有实体直接拉取全局单例 `GameState.buff_count()/enemies/player_ref`（Service Locator 反模式，无抽象层）。
  - `bullet.gd:227` `(area.get_parent() as Player)` 强转具体类型。
  - `boss.gd:1010,1073` `get_tree().get_first_node_in_group("spawner")` 每次调用做 group 查找拿依赖。
  - 池与对象互相通过私有字段 `_pool` 回调。
- **修复指引**：
  1. 区分可接受部分：GameState 作配置中心 + 信号总线 + 注册表是**有意的性能权衡**（热路径避免每帧 `get_nodes_in_group`），保留但收敛接口。
  2. 事件依赖注入：Boss/事件需要的 Spawner 引用在 `_ready`/`setup` 时由注入方传入，避免 group 查找。
  3. `bullet.gd` 的 Player 强转改信号或接口。
- **修复起效记录**：⚠️ 未修复（登记于 2026-07-31）

---

## 🟡 中等级（严重度 3）

### A6. L 违反 —— Enemy 与 Boss 的 take_damage 多态契约不一致

- **位置**：`enemy.gd:508` vs `boss.gd:1371`；调用方 `bullet.gd:216`
- **描述**：两类型 `take_damage` 语义不同（Enemy 直扣直死；Boss 锁血/阶段推进/狂暴），调用方被迫 `if area is Boss` 特判 —— 说明多态接口不完整，Boss 与 Enemy 不应共用同一调用点。
- **修复指引**：将玩家弹命中结算改为「命中事件」信号驱动，或定义 `Hittable` 契约（`take_damage(amount, score_scale)`），Boss/Enemy 各自实现并移除调用方类型特判；特判移入各自实现内部。
- **修复起效记录**：✅ **已修复（2026-07-31 `68fea1e`「A6 语义化特判」；vault 状态表当时漏更新，2026-08-01 回填）**
  - **改了什么**：`Enemy.is_boss()` 语义特判 + `Boss` override；`bullet.gd` 命中结算的硬类型特判 `if area is Boss` 改为 `if e.is_boss()` / `if explosive and not area.is_boss()`（爆炸 AoE/溅射对 Boss 的排除逻辑语义化）。
  - **为什么起效**：调用方不再依赖具体类型（`Enemy`/`Boss` 共用 `is_boss()` 查询），满足「类型特判移入各自实现内部」的指引方向。实现走语义化分支而非完整 `Hittable` 契约，属指引允许的分支方案。

---

### A7. 测试大量直接访问私有字段 —— 测试与实现细节强耦合

- **位置**：`test/*.gd`（多处 `_input_locked`、`_fire_timer`、`_elapsed` 等直接访问）
- **描述**：测试直接依赖私有实现，A1 重构会连带击碎全部测试；当前 `AGENTS.md` 已登记 2 条既有失败基线（`hit_logic_test` A21、`smoke_test` 偶发）。
- **修复指引**：测试优先走公开接口/信号；确需注入的用 `@export`/`set()` 公开测试口（项目已有 `aim_point_override`、`_set_milestone_override` 先例）。
- **修复起效记录**：✅ 全部完成（2026-07-31）
  - **已清理（核心类）**：`Player`（补 `enrage_slow/set_dead/set_dash_cooldown/reset_combat_state/fire/reset_fire_cooldown/boost_toggle_active/fine_toggle_active/aim_assist_params/hitbox_enabled/dash_cooldown/since_damage/set_boost_toggle/set_fine_toggle` 等）、`Boss`（补 `enrage_sequence/attacks/fire_tool/set_fire_timer/patterns/set_pattern_index/start_pattern/base_modulate_color/set_survival/set_in_fight/escape_warned/begin_escape` 等）、`Spawner`（补 `spawn_boss/spawn_enemy/wave_size/count_spread_enemies/current_interval/set_* 计时器` 等）、`Main`（补 `player/hud/base_ui/pause_ui/meta_fx/event/formation/strike/summon_window/intro/return_cinematic` 引用 getter + `play_intro/skip_intro/play_return/skip_return/start_homecoming/summon_mothership/resume_from_base/stop_charging/continue_run` 动作方法 + `set_*` 测试口）。测试全部改用公开接口（sed 批量 + 手动修残留），每类跑对应测试全绿。
  - **验证**：全量 29 断言场景 0 FAIL；`--headless --import` 无错；过程中修复 2 处 sed 产生的非法赋值（`toggle_active() = `、`boss_pending() = `、`set_patterns({` 括号）与 1 处多行字典括号。
  - **剩余子批也已全部完成（同一会话续跑）**：事件类（EliteTurretEvent/FormationStrikeEvent 补 `state()/lines()/turrets()/total()/line_stage()/comm()/crafts()/alive_count()/dropped()/cooldown_left()/set_cooldown_left()` 等）、UI 类（Hud 节点 getter + `toggle_buff_panel()`、BuffSelect `pick_buff()/current_available()`、StartPanel `press_new_game()/press_continue()/dismiss()`、SettingsUI/PauseUI/BaseConsole 公开动作方法）、特效（MetaHealthFX `set_test_state()` 统一平滑参数测试口 + `crack_progress()/hit_pulse()/damage_x()/state()/heart_rate()` 等 getter）、过场（Cinematic `set_shot_durations()/shot_index()/current_shot()/subtitle()`）、杂项（Mothership 弹匣/光束 getter 与 setter、LaserWeapon/Enemy/Bullet/WarpGate/TurretBattery/AimFrameLayer/ExitConfirm/Bullet 等公开查询）。测试全部改公开接口；**最终 29 断言场景全绿 0 FAIL + autoplay 120s 伪实机探针 0 异常**。过程中修复多批 sed 产生的非法赋值/子串误伤/`"_\1"` 反向引用转义（如 `ms.state()_timer`、`fx.set_test_state({"_\\1": ...})`）。
  - **A7 遗留清理（2026-07-31 续跑，autoplay 审计触发）**：批量 sed 把 `en._active` 误替换为不存在的 `en.active()`（应为 `is_active()`），autoplay 探针 `_checks()` 每 500ms 抛运行时错误中断，**注册表双向差集/registry_stale/player_ref/pool_ref 监控静默失效**；另残留 `_state` 私有直读 ×3（Mothership/两事件类，均有公开 `state()`）。全量复查共清理测试侧私有访问 28 处（含 balance/boss_enrage/buff_panel/difficulty/elite_turret_event/formation_strike_event/hud_capture/keybind/meta_fx_capture/meta_health_fx_test/mothership_summon_test/pool_reuse_test/smoke_test/startup_flow_test/ui_capture/visual_capture）与游戏侧跨类私有调用 5 处（BackNavigator `_on_back_pressed/_on_resume_pressed/_skip_intro/_skip_return` → `back()/resume()/skip_intro()/skip_return()`、Mothership `e._exiting` → `e.is_exiting()`）。补公开接口：GameState `reload_balance()/set_milestone_override()/apply_key_bindings()`、MetaHealthFX `set_lod()`、SettingsUI `show_page()`、Mothership `start_release()`、StartPanel `press_settings()`、WelcomeScreen `reset_entry_shown()`、Enemy `summon_slow_timer()`、HUD `early_leave_box()/early_leave_fill()`、BulletPool/EnemyPool `free_count()`。**验证：29 断言场景全绿 0 FAIL + autoplay 60s 0 异常 0 stderr 运行时错误（修复前同窗口 59 条退出泄漏警告）**。

---

### A8. Player 也是小号上帝对象（506 行，9 类职责）

- **位置**：`scripts/player.gd`
- **描述**：移动、瞄准/辅助瞄准、开火、Dash、燃料、受击/减免、回血、尾焰/残影视觉、碰撞清除同处一类。外部（main/boss）还直接写其私有字段（见 A1）。
- **修复指引**：受击减免（闪避/护甲/吸血）与回血抽为 `DamagePipeline` 效果组件；Dash 抽独立组件；视觉（尾焰/残影/准星/碰撞点）抽 `PlayerVisuals`。
- **修复起效记录**：⚠️ **部分完成（2026-08-01 按 git 历史回填，状态表当时漏更新）**
  - **已完成（2026-07-31 `9174a52`「A8 Player 职责拆分」）**：受击/回血抽为 `PlayerDamage` 组件、冲刺抽为 `PlayerDash` 组件（属性经 Player 转发，外部 API 不变，A1 无穿透）。
  - **未完成**：视觉职责（尾焰/残影/准星/碰撞点/`PlayerBuffVisuals`）仍驻留 Player 本体；Player 仍约 697 行。

---

## 修复状态总览

| 编号 | 严重度 | 状态 | 登记时间 |
| --- | --- | --- | --- |
| A1 封装穿透 | 危险 | ✅ 已修复 | 2026-07-31 |
| A2 上帝对象 | 危险 | ✅ 已修复 | 2026-07-31 |
| A3 boss 单类 | 严重 | ⚠️ 拆分落地、O 原则未达成（2026-08-01 订正） | 2026-07-31 |
| A4 开闭违反 | 严重 | ⚠️ 部分完成（A4a/A4b 落地，Boss 分支与 Player buff 未治理） | 2026-07-31 |
| A5 依赖倒置 | 严重 | 未修复 | 2026-07-31 |
| A6 L 违反 | 中等 | ✅ 已修复（is_boss 语义化特判，2026-08-01 回填） | 2026-07-31 |
| A7 测试耦合 | 中等 | ✅ 已修复 | 2026-07-31 |
| A8 Player 膨胀 | 中等 | ⚠️ 部分完成（PlayerDamage/PlayerDash 已抽，视觉未抽） | 2026-07-31 |

> **修复后处理**：任何一条修复落地后，须回到本表更新状态，并在对应条目回填「修复起效记录」——说明改了什么、为什么起效、用什么验证（相关测试场景：`smoke_test` / `base_system_test` / `pool_reuse_test` / `enemy_combat_test` / `hit_logic_test`）。

<!-- 新发现追加区：后续审核轮次在此编号继续（B1, B2, ...） -->

---

# 第二轮审核（2026-08-01 并行业务逻辑复核 + 文档口径统一）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 主要业务逻辑问题与矛盾复核（6 路并行代理：对局编排/玩家瞄准/刷怪池/Boss/事件演出/数值一致性）+ 文档-代码口径统一 |
| 工作时间 | 2026-08-01 |
| 审核区域 | `scripts/` 全部脚本 + `autoload/game_state.gd` + `data/balance.json` + 7 份设计文档 |
| 结论 | 无危险级运行时崩溃；2 个真实泄漏类缺陷、8 个对局影响级缺陷、一批文档-代码矛盾（口径已统一并登记）。A2/P1-3/五套演出系统判定为真·达成设计目标；A3/A4/A8 为部分完成（见 A 系列订正） |

## 发现清单

| 编号 | 严重度 | 位置 | 描述 | 修复指引 |
| --- | --- | --- | --- | --- |
| B1 | 严重 | `enrage_sequence.gd:237` | 二型狂暴 `_aim_line` 每次瞬停点 `make_aim_line()` 创建 Line2D 存本类字段，全文件无 `queue_free`；`abort()`/RELEASE/RETURN 均不清理，残留静态瞄准线显示到 Boss 释放，每次二型狂暴泄漏约 6 个 Line2D | 创建时纳入统一生命周期（复用 `BossAttacks.cancel_aim_line()` 或本类持有后于 RELEASE/RETURN/abort 清理） |
| B2 | 中等 | `main.gd:291-305,508-513,610-644` | 狂暴子弹时间 `Engine.time_scale=0.24` 在返航/死亡/放弃三条对局终态路径不复位；返航过场以 4 倍慢速播放直至轨道打击解除暂停自愈 | `_start_homecoming()`/`_on_player_died()`/`_give_up()` 入口复位 `time_scale = 1.0` |
| B3 | 中等 | `spawner.gd:503-513` + `boss.gd:608-611` | Boss 逃跑同时 emit `escaped`+`died`；`_on_boss_died` 无 `is_escaped` 守卫，误走击杀结算（推进 `_next_boss_score` + 给休整波），违背「逃跑不推进轮换、不给休整」契约 | `_on_boss_died` 先判 `boss.is_escaped` 跳过击杀侧结算 |
| B4 | 中等 | `bullet.gd:172-190` + `enemy.gd:329-338` | 追踪弹对池化回收目标仅查 `is_instance_valid`（池实例仍合法），目标死亡后弹追向 `(-500,-500)`、复用后追向无关新敌 | 失效判定补 `not homing_target.is_active()` 或注册表成员检查 |
| B5 | 中等 | `balance_service.gd:40-42` + `boss.gd:420-421,522-523` | `cfg()` 对数组返回共享 JSON 引用；`FIRE_INTERVALS[i] *= interval_mult` 就地污染缓存，easy/hard 跨 Boss 复合叠加（BOSS_REDESIGN §8.2 同类已修 bug 漏此路径） | `_apply_difficulty_scaling()` 对 `FIRE_INTERVALS` 先 `duplicate(true)`（与 `_load_patterns` 同法） |
| B6 | 中等 | `game_state.gd:72` | `world_scale` 脚本回退默认 1/3 未随 json 0.4 上调；损坏 JSON 时全游戏机体缩放/碰撞半径系统性错位 | 回退默认值改 0.4（**2026-08-01 本次已修**） |
| B7 | 中等 | `laser_weapon.gd:74-91` | 激光光束走原始鼠标，与磁吸/粘性后的准星 `aim_point()` 指向不一致（P1-3 磁吸放大偏差） | `_aim_dir()` 改用 `_player.aim_point()` |
| B8 | 中等 | `spawner.gd:393-399` | `_count_spread_enemies()` 用 group 遍历（池化敌机 `deactivate()` 不 `remove_from_group`），池中闲置实例虚抬 spread 同屏上限 → spread 弹种频繁退化 | 改遍历 `GameState.enemies` 注册表 |
| B9 | 中等 | `boss.gd:252-257` vs `enemy.gd:124-131` | Boss HP ramp 整倍乘 vs 敌机阻尼 ramp（0.12/单位），后期 Boss 血量可能在 50s 逃跑窗口内无法击杀（mult≈5 时 Boss-3 hard ≈9600 HP） | 确认设计意图或统一 ramp 语义 |
| B10 | 中等 | `formation_strike_event.gd:212-222` | BOMBING_RUN 投弹横穿实际 1.1–1.8s（3/4/5 机），设计文档承诺 2.6–3.8s（投完即离场） | 调投弹间隔/延长横穿段至设计区间（游戏性变更，需人决） |
| B11 | 轻微 | `mothership.gd:134-136` | 母舰 `drive.margin_*` 被乘 `world_scale`，同族屏幕边界值（strafe/hover_band/fight_y）不乘，归类口径不一致 | 统一为不乘或补注释说明例外 |
| B12 | 轻微 | `enemy.gd:139` | 敌机速度 ramp 系数 0.1 硬编码无 json 键（HP/伤害 ramp 均有键） | 补 `enemies.speed_ramp_factor` 键 |
| B13 | 轻微 | `elite_turret_event.gd:151-165` / `formation_strike_event.gd:163-169` | 事件 `abort()` 不清理 CommOverlay 已显台词，返航恢复后台词残留 | `abort()` 内清空/隐藏 `_comm` |
| B14 | 轻微 | `meta_health_fx.gd:188-194` | 状态边界严格小于比较，恰好 20% 血量不进入 DYING（差一档；浮点连续值实际几乎不可达） | 边界改 `<=` 或文档注明 |
| B15 | 轻微 | `return_cinematic.gd:99-113` | `skip()` 的 SKIP_GRACE（1.2s）输入宽限同时门控程序化自然结束；未来过场时长 <1.2s 会被永久拦截 | 自然结束与输入跳过分流（`_finish()` 独立于 `skip()`） |
| B16 | 轻微 | `main.gd:610-614` | `_give_up()` 死亡爆炸生成于已暂停树不播放（纯视觉） | 先 `_player.die()` 再暂停，或爆炸挂 `process_mode=Always` |

## 口径统一记录（2026-08-01 一并修正的文档-代码矛盾）

| 项 | 修正内容 |
| --- | --- |
| AUDIT_VAULT A 系列状态表 | A3/A4/A6/A8 按实际完成度订正（上文）；ROADMAP/AGENTS 同步 |
| `docs/2026-07-30-combat-ux-audit-plan.md` | P1-1 mark_ratio 0.4/40% → 落地值 0.25（正文/回退/目标态）；P0-3「不动 world_scale 1/3」加 2026-07-31 上调 0.4 决策变更注 |
| `docs/META_HUD_DESIGN.md` §7 | 裂纹曲线验收 0.11/0.33/**0.72/0.93** → 与 §4.2/代码一致的 **0.63/0.84** |
| `docs/EXIT_FLOW.md` | 状态机伪代码方法名改公开接口（`main.skip_intro()/skip_return()`、`base_ui.resume()`、`settings_ui.back()`） |
| `AGENTS.md` | GameState 描述补 A2 组合服务；编队事件「不暂停波次」→「占用波次槽暂停普通波次」；失败基线（A21/母舰击杀偶发）标注已通过 |
| 代码注释/回退 | `game_state.gd` world_scale 回退 1/3→0.4（含注释）；`formation_strike_event.gd` 类注释；`smoke_test.gd`「40%」→「25%」；`main.gd` 两处误导注释 |
| `BOSS_REDESIGN.md` §8.2 | duplicate(true) 自决点补注 FIRE_INTERVALS 同路径漏拷贝（B5） |

## 修复起效记录（2026-08-01 全量修复，见当次提交）

| 编号 | 状态 | 改了什么 / 为什么起效 / 验证 |
| --- | --- | --- |
| B1 | ✅ 已修复 | `EnrageSequence` 新增 `_free_aim_line()`，创建/出弹/RELEASE_HOLD/abort 四处清理；`BossAttacks.cancel_aim_line()` 只清自身 `_aim_line` 到不了此处。验证：autoplay 60s 孤儿节点 0 |
| B2 | ✅ 已修复 | `main._reset_global_time_scale()`（清子弹时间状态 + `Engine.time_scale=1`），挂到 `_on_player_died`/`_start_homecoming`（放弃经 player_died 覆盖） |
| B3 | ✅ 已修复 | `boss.died.connect(_on_boss_died.bind(boss))` + `is_escaped` 守卫；逃跑期 `collision_layer=0` 故无"逃跑中击毁"歧义。enemy_combat_test「逃跑不升难度」保持通过 |
| B4 | ✅ 已修复 | `Bullet._process` 失效判定改 `GameState.enemies.has(homing_target)`（活跃敌机都在注册表、deactivate 已注销）；不能用 `is_active()`（直实例化敌机恒 false）。smoke 追踪段重跑通过 |
| B5 | ✅ 已修复 | `FIRE_INTERVALS` 从 cfg 取后 `.duplicate()`，与 `_load_patterns` 同法；boss_pattern_test easy/hard 场景通过 |
| B6 | ✅ 已修复 | `game_state.gd` world_scale 回退默认 1/3→0.4（已含在首提 commit） |
| B7 | ✅ 已修复 | `laser_weapon._aim_dir`/触发判定改用 `_player.aim_point()`，光束与磁吸准星指向一致。buff33_test 通过 |
| B8 | ✅ 已修复 | `_count_spread_enemies` 改遍历 `GameState.enemies` 注册表，池中闲置实例不再虚抬 spread 上限。wave_pacing/enemy_combat 通过 |
| B9 | 🟦 设计确认 | **非缺陷**：`enemy_hp_multiplier()` 实为难度档倍率（0.75/1/1.5），敌机 HP = 基准×难度档×阻尼 ramp 无叠加；Boss 线性放大 + 50s 逃跑压力阀为 ENDLESS_BALANCE_PLAN D1 明文设计。不改码 |
| B10 | ✅ 已修复 | `bomb_interval` 0.35→0.8（json+脚本回退）；投弹段 1.1/1.45/1.8s → 2.0/2.8/3.6s（3/4/5 机），中/难落入设计带。formation_strike_event_test 通过 |
| B11 | 📄 口径澄清 | 补注释：DRIVE_MARGIN 乘 ws 是有意例外（舰体边缘视觉屏距恒定），归类机体偏移族；不改行为 |
| B12 | ✅ 已修复 | 新增 `enemies.speed_ramp_factor=0.1` json 键，`enemy.gd` 改 `cfg` 读取（原硬编码 0.1） |
| B13 | ✅ 已修复 | `CommOverlay` 新增 `clear()`，精英炮塔/编队事件 `abort()` 调用清台词 |
| B14 | 🟦 设计确认 | **非缺陷**：设计 §7 明确「hp<20% 进 DYING」，`ratio < t` 严格小于正是该语义；恰好 20% 不进 DYING 正确。不改码 |
| B15 | ✅ 已修复 | `skip()` 拆 `_do_skip(bypass_grace)`；自然结束 `_advance` 走 `_do_skip(true)` 绕过输入宽限，未来压缩总时长不误拦截。return_cinematic_test 通过 |
| B16 | ✅ 已修复 | `Explosion._init()` 设 `process_mode=Always`——死亡爆炸生成于已暂停树仍播放（覆盖正常死亡 `player_damage` 与 `_give_up` 两条路径） |

> 修复后回归：`--import` / `--quit-after 300` / **29 断言场景全绿 0 FAIL** / autoplay 60s 0 异常 0 孤儿节点。

---

# 第三轮审核（2026-08-01 Godot 最佳实践与语法规范审计）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | Godot 4.x 最佳实践与 GDScript 语法规范（新维度，区别于 A 系列 SOLID / B 系列业务逻辑） |
| 工作时间 | 2026-08-01 |
| 审核区域 | `scripts/` 全部 76 脚本 + `autoload/game_state.gd` + `scenes/*.tscn` + `test/*.gd` + 配置/资源（约 1.8 万行） |
| 审核方法 | 7 分区并行审核（对局编排/玩家武器/刷怪敌人/Boss/事件演出/UI/测试）+ 主控交叉核验 + 判定分类（`docs/AUDIT_REVIEW_SOP.md`） |
| 结论 | 无危险级崩溃；2 严重 + 15 中等 + 18 轻微，共 35 项（生产 28 / 测试 7）。基线 `--import` 与 `--quit-after 300` 全绿；Godot 3.x 残留 API 全库 0 处 |
| 审核人 | Claude Code（依据用户指示执行） |
| 完整报告 | `docs/2026-08-01-godot-best-practice-audit.md` |

## 发现清单（C 系列，登记待修复）

| 编号 | 严重度 | 位置 | 类别 | 描述 |
| --- | --- | --- | --- | --- |
| C01 | 严重 | `tutorial.gd:276,338` | 协程泄漏 | 两处 `await create_timer` 违反 AGENTS.md 禁令；教程中途切场景协程悬死，`_advancing`/`_close_base` 不执行 |
| C02 | 严重 | `game_state.gd:920` | 纯bug/健壮性 | `load_profile` key_bindings 无类型守卫，手改档案 typed 赋值运行期报错提前返回、后续字段不加载且不置 corrupt |
| C03 | 中等 | `game_state.gd:96-105` | 纯bug/健壮性 | `_apply_balance` 只校验顶层类型，缺子键/空数组时 KeyError/除零，违背损坏回退宣称 |
| C04 | 中等 | `bullet.gd:171-208` | 生命周期/物理 | Area2D 位移在 `_process` 而非 `_physics_process`，物理步进采样错位，高速弹穿越风险 |
| C05 | 中等 | `player.gd:533,538` | 性能/规范 | `_physics_process` 热路径直调 `sin()` 违反查表约定；get_ticks_msec 每帧两次 |
| C06 | 中等 | `enemy.gd:391-401` | 性能 | 每帧每敌机构造 9 键 ctx Dictionary + 3 次 view_world_rect()，池规模 GC 压力 |
| C07 | 中等 | `starfield.gd:29-43` | 规范 | 星域范围/回绕硬编码 1920×1080，违反 view_world_rect() 约定 |
| C08 | 中等 | `boss.gd:869` | i18n | 逃跑警告硬编码中文绕过 tr()，英文环境显示中文 |
| C09 | 中等 | `boss_movement.gd:45`/`boss_attacks.gd:108,240`/`enrage_sequence.gd:230` | 性能/规范 | 四处 `_physics_process` 直调 sin() 违反查表约定 |
| C10 | 中等 | `enrage_sequence.gd:300-307` | 性能 | `_path_center` 每帧构建 5 元素 Array[Vector2]，狂暴 ACTIVE 全程堆分配 |
| C11 | 中等 | `boss.gd:711-714`/`boss_movement.gd:38-47` | 纯bug | P1→P2 段切换落在下压窗口内时 `_press_offset` 残留，机身最多 80px 永久偏移至锚线下 |
| C12 | 中等 | `return_cinematic.gd:1357-1362` | 纯bug | 镜头 7 推近 `set_parallel` 下 `tween_interval` 不延迟，特写在人物躺下前完成 |
| C13 | 中等 | `comm_overlay.gd:80-86` | 纯bug | 淡出 tween 不被 show_line/clear kill，新台词落窗口时被拉回 alpha=0 并 hide |
| C14 | 中等 | `main.gd:113,382`/`boss_attacks.gd:214,228,266`/`boss_movement.gd:66` | 规范 | 硬编码 960.0/±1600 世界坐标绕过 view_world_rect()（同文件 606 已正确） |
| C15 | 轻微 | `main.gd:419,425` | 生命周期 | `await process_frame` 缺 is_inside_tree 守卫，首帧前释放则 freed add_child |
| C16 | 轻微 | `game_state.gd:886-887,929-930` | 纯bug | `bool()` 字符串真值陷阱：手改存档 "false"/"0" 字符串转 true |
| C17 | 轻微 | `back_navigator.gd:22-31`/`welcome_screen.gd:33,101,121`/`pause_ui.gd:132` | 节点安全 | `get_parent().get_node("X")` 链式兄弟访问无判空，未用唯一名 % |
| C18 | 轻微 | `game_state.gd:67`/`spawner.gd:74`/`boss.gd:81-82`/`enemy.gd:72` | 类型 | 裸 Array/Node 未标元素/具体类型 |
| C19 | 轻微 | `main.gd:10-17`/`game_state.gd:31`/`hud.gd:46-53`/`tutorial.gd:10-11` 遍布 | 可读性 | CONSTANT_CASE 命名用于可变 var（回退默认值模式），与官方约定冲突；判定为项目数据模式维持现状 |
| C20 | 轻微 | `player_buff_visuals.gd:51`/`bullet.gd:218-233`/`enemy_move_strategy.gd:122` | 类型 | 弱类型返回/参数（裸 Array、Area2D 调 Enemy 专有方法、Node2D 访问私有成员） |
| C21 | 轻微 | `bullet_pool.gd:11-12` | 生命周期 | `_ready` 注册 GameState.bullet_pool 无 `_exit_tree` 清空 |
| C22 | 轻微 | `player.gd:693-697`/`camera_shake.gd:11` | 信号 | `_exit_tree` 未断信号/connect 无 is_connected 守卫，重入树重复连接 |
| C23 | 轻微 | `laser_weapon.gd:76`/`boss_attacks.gd:106`/`enrage_sequence.gd:229`/`aim_frame_layer.gd:72` | 性能 | 每帧 PackedVector2Array 分配/每帧 get_node_or_null |
| C24 | 轻微 | `boss_fire.gd:56,84`/`enemy.gd:453-455`/`mothership.gd:561` | 性能 | 每次发射 get_node("Polygon2D"/"MuzzleFlash") 字符串查找，可缓存 |
| C25 | 轻微 | `main.gd:633-660,510,624` | 生命周期 | 返航/死亡/放弃终局路径不调 `_stop_charging`，蓄力特效瞬态残留（自动修复） |
| C26 | 轻微 | `start_panel.gd:122,128,134`/`base_console.gd:373` | i18n | 硬编码中文按钮文案/任务格式串绕过 tr() |
| C27 | 轻微 | `ui_chamfered_panel.gd:34`/`start_radar.gd:18` | 性能 | `_process` 轮询自适应/隐藏期仍每帧 queue_redraw |
| C28 | 轻微 | 演出类 `_process` 每帧重建点集（intro:1375/warp_gate:150-158/summon_window:303-314/orbital_strike:162-190/mothership:517-529） | 性能 | 短时演出的每帧 PackedVector2Array/闭包分配 |
| C29 | 中等 | `enemy_combat_test.gd:187` | 测试规范 | 直读 `_exiting`，`is_exiting()` 公开接口已存在（A7 残留） |
| C30 | 中等 | `back_navigation_test.gd:128`/`keybind_test.gd:65,74` | 测试规范 | 直调 `_notification`/`_unhandled_input` 虚回调绕过公开路由/输入管线 |
| C31 | 轻微 | `tutorial_test.gd:160` | 测试规范 | 直调 `_exit_tutorial()` 私有方法 |
| C32 | 轻微 | `base_system_test.gd:81` | 测试规范 | 直调 `_init_missions()`，无干净公开替代 |
| C33 | 中等 | `test/*.gd` 约 120 处 | 测试规范 | `await create_timer` 系统性偏离协程约定（部分经 _wait_real 正确包装） |
| C34 | 轻微 | 多个测试 | 测试规范 | 硬编码 balance.json 数值（改 JSON 漂移不报错）；view_zoom_test:38 硬编码 1920×1080 |
| C35 | 轻微 | `meta_health_fx_test.gd:66-154` | 测试规范 | set_test_state 字符串键直写私有字段，键名强耦合实现 |

## 判定分类记录（2026-08-01）

| 项 | 判定 | 理由 |
| --- | --- | --- |
| C19 CONSTANT_CASE 可变 var | 🟦 设计确认 | 项目数据模式（AGENTS/CLAUDE 明文"脚本回退默认值"），大范围改名收益低风险高，维持现状 |
| `buff_select.gd:157` child.free() | 🟦 合理 | stagger_open 紧接遍历 children，queue_free 会新旧卡同帧共存 |
| `enemy.tscn` resource_local_to_scene | 🟦 约定正确 | AGENTS.md:204 规定，无共享污染 |
| `mothership.gd` 六处 group hud | 🟦 非热路径 | 全部事件驱动/一次性缓存 |
| 组件 boss 参数无类型（boss_attacks.gd:70） | 🟦 取舍可接受 | A1/A3 文档化取舍，可优化非必须 |
| 测试 create_timer | 🟦 泄漏影响有限 | 收尾即 quit；仍建议收敛（C33） |

## 修复起效记录（2026-08-01 全量修复，见当次批次提交）

| 编号 | 状态 | 改了什么 / 为什么起效 / 验证 |
| --- | --- | --- |
| C01 | ✅ 已修复 | `tutorial.gd` 两处 `await create_timer` 改一次性 `Timer` 节点 + `timeout` 信号（process_mode=ALWAYS 对齐原 SceneTreeTimer）；教程切场景不再协程悬死。验证：tutorial_test 0 FAIL |
| C02 | ✅ 已修复 | `game_state.gd` load_profile key_bindings 类型守卫——非 Dictionary/子值非 Array 跳过该字段，不崩溃不提前返回。验证：startup_flow/base_system 0 FAIL |
| C03 | ✅ 已修复 | `_apply_balance` 校验难度表 easy/medium/hard 子键齐全（`_valid_difficulty_defs`）+ milestones.base 非空数组，损坏 JSON 不再 KeyError/除零。验证：balance_test 0 FAIL |
| C04 | ✅ 已修复 | `bullet.gd` 位移 `_process`→`_physics_process`，activate/deactivate 的 set_process→set_physics_process 配对；view_zoom_test 等待改 physics_frame。验证：pool_reuse/hit_logic/enemy_combat/smoke 0 FAIL |
| C05 | ✅ 已修复 | `player.gd` 无敌闪烁/碰撞点脉动改 `Enemy.sin_fast` 查表（`Time.get_ticks_msec()` 缓存为常量倍率）。验证：--quit-after 300 无错 |
| C06 | ✅ 已修复 | `enemy.gd` 移动 ctx 改 `_move_ctx` 字典复用（每帧只更新字段）；主路径 view 复用到出界销毁。验证：enemy_combat/wave_pacing 0 FAIL |
| C07 | ✅ 已修复 | `starfield.gd` 星点范围/回绕改 `view_world_rect().size` 缓存，不再写死 1920×1080。验证：view_zoom_test 0 FAIL |
| C08 | ✅ 已修复 | `boss.gd` 逃跑警告改 `tr("BOSS_ESCAPE_WARNING")`（translations.csv 补 zh/en）。验证：i18n_test 0 FAIL |
| C09 | ✅ 已修复 | boss 体系四处 `_physics_process` 直调 sin 改 `Enemy.sin_fast`（下压/telegraph/猎杀闪烁）。验证：boss_pattern/boss_enrage 0 FAIL |
| C10 | ✅ 已修复 | `enrage_sequence._path_center` 方形路径改 `_square_corner` 两端点 lerp，消除每帧 5 元素数组。验证：boss_enrage 0 FAIL |
| C11 | ✅ 已修复 | `BossMovement.reset_press()` 段切换归零下压偏移 + `boss._enter_phase` 调用；boss_phase_test 新增"P2 后机身回锚线"断言（容差 4px）。验证：boss_phase 0 FAIL |
| C12 | ✅ 已修复 | `return_cinematic` 镜头 7 推近 `set_parallel` 改顺序 tween + 前置 interval + `.parallel()` 属性组，特写在人物躺下后开始。验证：return_cinematic 0 FAIL |
| C13 | ✅ 已修复 | `comm_overlay` 缓存 `_fade_tween`，show_line/clear 先 kill；新台词不再被残留淡出拉没。验证：elite_turret/formation_strike 0 FAIL |
| C14 | ✅ 已修复 | 硬编码 960/±1600 世界坐标改 `view_world_rect().get_center()`（main 蓄力/冲刺预警线/strafe 方向）。验证：view_zoom/boss_pattern 0 FAIL |
| C15 | ✅ 已修复 | `main.gd` await process_frame 后加 `is_inside_tree()` 守卫。验证：--quit-after 300 + startup_flow 0 FAIL |
| C16 | ✅ 已修复 | `game_state.gd` 新增 `save_bool()` 安全布尔读取，7 处 `bool(手改值)` 全替换（"false"/"0" 字符串不再误读 true）。验证：startup_flow/base_system 0 FAIL |
| C17 | ✅ 已修复 | welcome_screen/pause_ui 的 `get_parent().get_node` 链改 `get_node_or_null` + 判空。验证：back_navigation 0 FAIL。**2026-08-02 补注（D29）**：登记中的 `back_navigator.gd:22-31` 另有 8 处同类裸 `get_node("固定兄弟")` 未改——访问对象为 main.tscn 固定子节点、风险低，判定为合理模式不修（档案复核口径） |
| C18 | ✅ 已修复 | milestone_base→Array[int]、UNLOCK_SCORES→Array[int]、STRAFE_SPEEDS→Array[float]（cfg 显式转换）、enemy._pool→EnemyPool。验证：--import + 全量测试 |
| C19 | 🟦 设计确认 | **非缺陷**：CONSTANT_CASE 可变 var 为项目"脚本回退默认值"数据模式（CLAUDE.md 明文），大范围改名收益低风险高，维持现状。不改码 |
| C20 | ✅ 已修复 | spread_pods()→Array[Node2D]、bullet 爆炸/溅射 `as Enemy`、EnemyMoveStrategy 8 update+4 reset 参数 Node2D→Enemy。验证：--import + 全量测试 |
| C21 | ✅ 已修复 | bullet_pool/enemy_pool 加 `_exit_tree` 清空 GameState 全局池注册。验证：pool_reuse 0 FAIL |
| C22 | ✅ 已修复 | player `_exit_tree` 显式断开 GameState 信号；camera_shake connect 加 is_connected 守卫 + _exit_tree 断开。验证：--quit-after 300 |
| C23 | ✅ 已修复 | laser_weapon/boss_attacks/enrage_sequence 每帧 PackedVector2Array 改预分配写元素；aim_frame_layer 碰撞半径 meta 缓存。验证：boss_pattern/boss_enrage 0 FAIL |
| C24 | ✅ 已修复 | Bullet.polygon_node() 懒加载缓存，boss_fire/enemy 不再每弹 get_node；mothership `_muzzles` 数组同序缓存。验证：boss_pattern/mothership_summon 0 FAIL |
| C25 | ✅ 已修复 | main 返航/死亡终局路径补 `_stop_charging`，蓄力特效不再残留。验证：mothership_summon 0 FAIL |
| C26 | ✅ 已修复 | start_panel 按钮初始化 `tr()` + base_console 任务格式串提 `BASE_MISSION_FMT`。验证：i18n_test 0 FAIL |
| C27 | ✅ 已修复 | ChamferedPanel/StartRadar `_process` 加 `is_visible_in_tree()` 早退。验证：--quit-after 300 |
| C28 | ✅ 已修复 | warp_gate 环/弧 + intro_cinematic 结构线 + orbital_strike 瞄准环/导弹拖尾 + summon_window 穿梭器环/拖尾——创建时预分配点集，帧内经 `set_point_position` 原地写（零分配、线宽不随 scale 变）。mothership `_live_targets` 判定低频函数返回数组（每 0.13-0.3s 开火一次，非每帧）保留并注记。同时修正 c526d79 的同类回归：`points[i]=` 是值语义副本不生效、`ring.scale=ONE*radius` 会连带放大线宽，全部改 `set_point_position`。验证：intro/return/mothership_summon/orbital_strike 0 FAIL |
| C29 | ✅ 已修复 | enemy_combat_test `_exiting`→`is_exiting()`。验证：enemy_combat 0 FAIL |
| C30 | ✅ 已修复 | back_navigation_test `_notification`→`go_back()`；keybind_test `_unhandled_input`→`Input.parse_input_event`。验证：back_navigation/keybind 0 FAIL |
| C31 | ✅ 已修复 | tutorial_test `_exit_tutorial`→注入 ui_cancel 动作。验证：tutorial_test 0 FAIL |
| C32 | ✅ 已修复 | base_system_test `_init_missions`→新增公开 `reset_missions()`。验证：base_system 0 FAIL |
| C33 | 📄 已核实无风险 | 核实：所有会改 `time_scale` 的测试关键路径已用 `_wait_real`（create_timer 4 参 ignore_time_scale，boss_*/elite/formation）；残留 ~118 处默认参数 create_timer 全部运行在 time_scale=1 段（smoke/tutorial/enemy_combat/capture 等），行为正确。判定为风格一致性而非功能 bug，机械替换回归风险超收益，不逐一替换。 |
| C34 | ⚠️ 部分完成 | boss_pattern_test 场景 1/2/4 的弹速/伤害硬编码（700/21/150/12/220）改读 boss 实例常量（CANNON_BULLET_SPEED/CANNON_DAMAGE/SWEEP_DROP_SPEED/SWEEP_DROP_DAMAGE/WALL_BULLET_SPEED），改 JSON 不漂移；场景 4/5 的 420（enemy.ENEMY_BULLET_SPEED 与 VOLLEY 同值）补来源注释。difficulty/buff33/elite/formation 硬编码判定为逻辑验证锚点保留（改读会降低测试独立价值）。验证：boss_pattern_test 0 FAIL |
| C35 | ✅ 已修复 | MetaHealthFX.set_test_state 接受无 `_` 前缀语义键（内部补 `_` 写私有字段），meta_health_fx_test 全部键去 `_` 前缀，不再与实现字段名强耦合。验证：meta_health_fx_test 0 FAIL |

> 修复后回归：`--import` / `--quit-after 300` / **29 断言场景全绿 0 FAIL** / autoplay 120s 探针。

---

# 第四轮审核（2026-08-02 近期大改全量代码审查）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 近期 60 提交大改全量代码审查（入场衔接动画 / UI uplift / Boss·事件·演出 / 辅助瞄准弹道 / 数值一致性 / 文档-代码-测试三角） |
| 工作时间 | 2026-08-02 |
| 审核区域 | 基线 `8c6dfff`→HEAD（60 提交、195 文件、+14.2k/-3.9k 行）涉及 `scripts/` + `autoload/` + `data/` + `docs/` + `test/` |
| 审核方法 | 6 分区并行审核（`docs/AUDIT_REVIEW_SOP.md`）+ 主控交叉核验 + 实证证伪（D03 Label mouse_filter 默认值） |
| 结论 | 无 P0/P1；P2×4 修复 + P2×1 文档登记（D05）+ P3×9 修复 + P3×6 登记不修 + 文档同步 8 处；D03 误报证伪；修复后全量 29 断言场景 0 FAIL |
| 审核人 | Kimi Code CLI（依据用户指示执行） |
| 完整报告 | `docs/2026-08-02-audit-fix-plan.md`（发现-判定-修复追踪单一事实源） |

## 发现清单（D 系列，登记待修复）

| 编号 | 严重度 | 位置 | 类别 | 描述 |
| --- | --- | --- | --- | --- |
| D01 | P2 | `spawner.gd:459-462,499,537` / `main.gd:652,695-699` | 纯bug/设计目标未达 | 入场动画"敌机延迟"只挂 spawner `_process` 开关；返航前排队的 `_schedule` Timer 与 SpawnTelegraph 不清，continue 后在入场窗口（0~0.6s）触发敌机/Boss 带预告进场 |
| D02 | P2 | `balance.json:22` vs `player.gd:21` | 一致性 | `player.entry.invincible` json 2.1 vs 脚本回退 1.65——全仓 363 键逐值核对唯一不一致；JSON 损坏时无敌窗口缩水 0.45s |
| D03 | P2→证伪 | `buff_select.gd:188` / `ui_theme.gd:50` | 误报 | 声称 Label 默认 STOP 阻断卡片点选热区——**实证 Godot 4.6 Label 默认 `mouse_filter=IGNORE`、Container 默认 PASS**，点文字穿透到卡片，无阻断 |
| D04 | P2 | `start_panel.gd:109` | 一致性 | 难度按钮取 `DIFFICULTY_DEFS["label"]`（数据驱动中文）不走 `tr()`，切 en 后 HUD 英文、按钮中文 |
| D05 | P2 | `BOSS_REDESIGN.md §5.1-5.3` vs `boss_movement.gd:30-39` | 文档-代码矛盾 | P2 阶段走位升级（一型 strafe 200+纵向、二型冲刺 0.4/0.5s、三型 strafe 100+纵向、三型 P1 纵向正弦）未实现；阶段 B 即有非 A3 引入，档案未登记。**2026-08-02 已按 §5.5 实现（见修复起效记录）** |
| D06 | P3 | `player.gd:640-642` / `main.gd:707-711` | 纯bug（边缘） | 入场起始帧内按 B 返航 → 锁输入冻结后撤、`_finish_entry` 不执行，新入场被守卫跳过；长按 K 自毁同源（`_die` 不清入场状态） |
| D07 | P3 | `test/entry_animation_test.gd:55-70` | 测试脆弱性 | `landed` 判据 `y<=land_y+5` 在冲入阶段（t≈0.88）提前 break，后撤断言余量仅 ~20px；不覆盖中断路径 |
| D08 | P3 | `hud.gd:723` | 性能约定 | vignette 每帧调 `GameState.max_health()`（内部 2 次 cfg JSON 查询），违反热路径缓存约定 |
| D09 | P3 | `back_navigator.gd:50` | 可访问性 | CANCEL_EXIT 只给开始面板还焦点；暂停→退出→Esc 后焦点留在隐藏确认窗 |
| D10 | P3 | `spawner.gd:510` / `elite_turret_event.gd:139` | 一致性 | 两处硬编码 960（C14 已收敛同类，未收敛此两处） |
| D11 | P3 | `boss.gd:828-830` | 观察级 | 狂暴锁血期多 tween 竞争闪白（无泄漏） |
| D12 | P3 | `test/boss_pattern_test.gd:254` | 一致性 | C34 例外：场景 3 `_bullets_by_speed(900.0)` 硬编码，未改读实例常量 |
| D13 | P3 | `player.gd:65` / `bullet.gd:194` | 一致性/文档矛盾 | `homing_time=4.0` 对玩家弹是死参数（出屏寿命≈1.07s），注释"≈弹寿命"不符 |
| D14 | P3 | `player.gd:664,680` | 一致性 | 入场起点屏外偏移 90px、后撤水平速度 0.6 倍率硬编码，未入 `player.entry` 配置 |
| D15 | P3 | `aim_frame_layer.gd:139` / `player.gd:597` | 设计权衡 | 磁吸/粘性每渲染帧增量式，绝对强度随刷新率缩放（60Hz 480px/s vs 144Hz 1152px/s） |
| D16 | P3 | `player.gd:74-82` / `aim_frame_layer.gd:17-26` | 维护 | 磁吸/距离衰减参数双份默认值（当前值一致） |
| D17 | P3 | `orbital_strike.gd:186` / `mothership_summon_window.gd:271` | 代码卫生 | 命中段每帧取视口尺寸可复用缓存；flash 衰减帧率依赖 |
| D18 | P3 | `return_cinematic.gd`（14 处 play_sfx） | 一致性（待判定） | 返航音效未应用 8-02 开场过场统一 -6dB/0.88 策略；返航各镜头本就压低，属产品判断 |
| D19 | P3 | `warp_gate.gd:157` 等 | 一致性 | C28 收口后残留节点 scale 线宽变化（收缩动画，均无回归级放大，视觉合理） |
| D20 | P3 | `data/balance.json.bak` | 维护 | bak 落后多段（缺 aim_assist/entry 段）——近期改数值绕过编辑器直落盘 |
| D21 | P3 | `EXIT_FLOW.md:49` | 文档-代码矛盾 | 伪代码注释残留"（开始面板 / 欢迎页）" |
| D22 | P3 | `README.md:92` / `README.en.md:92` | 文档-代码矛盾 | 仍描述"首启欢迎页与 6 阶段教程" |
| D23 | P3 | `AGENTS.md:104` | 文档-代码矛盾 | profile 字段描述仍含"欢迎页/"（welcome_seen 已删） |
| D24 | P3 | `DESIGN_BASELINE.md:301` | 文档-代码矛盾 | §6 持久化同 D23 表述 |
| D25 | P3 | `DESIGN_BASELINE.md:7,292,361,9` | 文档过期 | "29 断言场景"应为 30；"C 系列 35 项已全量修复"与档案 C34 部分完成/C19、C33 不修实况不符 |
| D26 | P3 | `ROADMAP.md:9` | 口径不一致 | "A7 855 处全清" vs 档案"测试侧 28 + 游戏侧 5" |
| D27 | P3 | `2026-08-01-remove-welcome-screen-plan.md` | 流程遗留 | 25 个 task checkbox 未勾、无完成注记、"29 断言"过期 |
| D28 | P3 | `translations.csv:103,213` | 一致性 | 孤儿键 `GO_SCORE` / `UI_KILLS_TAG`（零引用） |
| D29 | P3 | `AUDIT_VAULT.md:350` | 一致性 | C17 登记含 back_navigator 8 处裸 `get_node("固定兄弟")`，修复记录只提 welcome_screen/pause_ui；风险低 |
| D30 | P3 | `spawner.gd:123-124` / `scheduled_event_trigger.gd:16` | 一致性 | A4b 后 elite/formation 分数门槛语义分散两处（行为正确） |

## 判定分类记录（2026-08-02）

| 项 | 判定 | 理由 |
| --- | --- | --- |
| D03 | 🟥 误报证伪 | 实证（headless 打印默认值）：Godot 4.6 `Label.mouse_filter=IGNORE`、Container 系 `PASS`——点击文字穿透到卡片，无阻断。不改码 |
| D05 | ✅ 已实现 | P2 走位升级为阶段 B 即有缺口（`git show 3188902^` 逐行一致）；**2026-08-02 按 `BOSS_REDESIGN.md §5.5` 落地**（一型/三型 P2 strafe 提速 + 纵向正弦、三型 P1 缓慢下压回升、二型 P2 冲刺更频），配置入 `balance.json boss.movement`（11 键）；ENRAGE 走位不受影响 |
| D11 | 🟦 观察级不修 | 多 tween 竞争闪白，无泄漏无逻辑错误 |
| D15 | 🟦 设计权衡登记 | 帧率依赖为结构性选择，改 delta 归一属手感重构，超本轮范围 |
| D16 | 🟦 不修 | 双份默认值当前一致，已注释分工，低成本接受 |
| D18 | 🟦 待判定 | 返航音效口径已登记 `RETURN_HOME_CINEMATIC.md §9`，统一与否属产品判断 |
| D19 | 🟦 不修 | 线宽变化 ≤4%（HOLD 呼吸），无回归级放大，视觉合理 |
| D20 | 🟦 不修 | bak 为编辑器自动备份产物，下次打开保存自动刷新 |
| D29 | 🟦 不修 | main.tscn 固定兄弟节点访问风险低，补注说明 |
| D30 | 🟦 不修 | 行为正确（can_trigger 先于 tick 门控） |

## 修复起效记录（2026-08-02 全量修复）

| 编号 | 状态 | 改了什么 / 为什么起效 / 验证 |
| --- | --- | --- |
| D01 | ✅ 已修复 | `spawner.gd` 新增 `_pending_timers`/`_pending_telegraphs` 登记 + `_on_pending_timer_fired` 解除登记 + `clear_pending()`；`_queue_enemy`/`_schedule` 登记；`main.gd` 返航路径调 `clear_pending()`。continue 后入场窗口不再有敌机/Boss 进场。验证：entry_animation_test 13 PASS + smoke 142 PASS + 全量回归 0 FAIL |
| D02 | ✅ 已修复 | `player.gd` 回退值 1.65→2.1 并对齐注释（= 冲入 0.55 + 后撤 1.1 + 0.45s 缓冲，缓冲段按普通无敌闪烁路径）。验证：balance_test 28 PASS（损坏回退路径） |
| D03 | 🟥 证伪不修 | 见判定分类：Label 默认 IGNORE，原机制判断不成立 |
| D04 | ✅ 已修复 | `start_panel.gd` 难度按钮改 `tr("DIFF_"+String(d).to_upper())`，`_refresh_texts()` 同步刷新（与 HUD difficulty_label 同口径）。验证：startup_flow_test 36 PASS + back_navigation_test 24 PASS |
| D05 | ✅ 已修复 | `BOSS_REDESIGN §5.5` 落地——`boss_movement.gd` 新增 `_move_bob`（P2 纵向正弦，直接设置 y：`_in_fight` 后才调用，入场/逃跑/狂暴早退不干扰；`fight_anchor_y()` 逐帧求值支持切视角档）与 `_move_band`（三型 P1 缓慢下压回升，`_update_press` 同构 target 从 0 起步无跳变）；一型/三型 P2 strafe 提速 + 正弦、二型 P2 dash 0.4/0.5、三型 P1 下压 200–280 区间/9s；配置 `boss.movement` 11 键入 balance.json + 脚本回退同步 + BALANCE_MAP 重跑；ENRAGE 走位不变。实现中修复 2 个自身问题：`var target := boss.fight_anchor_y()+...` 因 boss 为 Variant 无法推断类型（改显式 float）；增量式施加初始跳变（改直接设置 y / band 从 0 起步）。验证：boss_phase_test 37 PASS（原 32 + 新增 5：一型 P2 正弦波动/振幅/strafe、三型 P1 下压/上界）；boss_pattern 55 / boss_enrage 34 / smoke 142 / 全量 30 断言场景 0 FAIL |
| D06 | ✅ 已修复 | `player.gd` 新增 `abort_entry()`（复位相位/恢复 auto_fire/kill tween）+ `_entry_tween` 成员；`main.gd` 返航调 `abort_entry()`；`_die()` 内调 `abort_entry()`。入场起始帧返航/自毁不再滞留相位。验证：entry_animation_test 13 PASS |
| D07 | ✅ 已修复 | `entry_animation_test` landed 判据改"定位线邻域连续 8 帧"（排除冲入阶段 y≥land_y 恒成立的假到达）；补"入场期间 auto_fire 暂停/结束后恢复"2 断言。验证：13 PASS 连续多轮稳定 |
| D08 | ✅ 已修复 | `hud.gd` 新增 `_cached_max_hp`，`_rebuild_buff_dock()` 开头刷新（buffs_changed 信号已有连接，extra_life 层数变化即刷新）；`_update_vignette`/`_on_health_changed` 改读缓存。热路径免 2 次 cfg JSON 查询。验证：smoke 142 + buff_panel 16 PASS |
| D09 | ✅ 已修复 | `back_navigator.gd` CANCEL_EXIT 分支补 `_pause_ui.visible → grab_primary_focus()`（暂停→退出→Esc 焦点回到暂停面板）。验证：esc_navigation 11 + back_navigation 24 PASS |
| D10 | ✅ 已修复 | `spawner.gd` Boss 入场锚点、`elite_turret_event.gd` 载体入场锚点改 `view_world_rect().get_center().x`（C14 收敛口径）。验证：elite_turret_event 57 + smoke 142 PASS |
| D11 | 🟦 不修 | 见判定分类 |
| D12 | ✅ 已修复 | `boss_pattern_test` 场景 3 `_bullets_by_speed(900.0)`→`boss3.E2_SNIPER_SPEED`，C34 收口补齐。验证：boss_pattern 55 PASS |
| D13 | ✅ 已修复 | `player.gd` `HOMING_TIME` 4.0→1.2（≈出屏寿命 1.07s）并修正注释；`balance.json` `player.aim_assist.homing_time` 同步 1.2。验证：enemy_combat 32 + smoke 142 PASS |
| D14 | ✅ 已修复 | 入场硬编码入配置：新增 `player.entry.spawn_clearance=90` / `rush_hspeed_ratio=0.6`（json + 脚本回退同步）；`gen_balance_map.py` 重跑双向反查干净。验证：entry_animation_test 13 PASS |
| D15 | 🟦 不修 | 见判定分类 |
| D16 | 🟦 不修 | 见判定分类 |
| D17 | ✅ 已修复 | `orbital_strike.gd` 视口尺寸 `_ready` 缓存 `_screen`、命中段复用；`mothership_summon_window.gd` `_update(t, delta)` 用 delta 参数替代 `get_process_delta_time()`。验证：orbital_strike 15 + mothership_summon 28 PASS |
| D18 | 📄 已登记 | `RETURN_HOME_CINEMATIC.md §9` 追加音频口径说明（沿用各镜头既有压低值，暂不统一；统一需同步改代码并回写本文档） |
| D19 | 🟦 不修 | 见判定分类 |
| D20 | 🟦 不修 | 见判定分类 |
| D21 | ✅ 已修复 | `EXIT_FLOW.md:49` 伪代码删" / 欢迎页" |
| D22 | ✅ 已修复 | `README.md` / `README.en.md:92` 欢迎页描述改"启动直达主菜单，首次进入有 6 阶段教程" |
| D23 | ✅ 已修复 | `AGENTS.md:104` profile 字段删"欢迎页/"（保留教程状态，`tutorial_done` 仍在用） |
| D24 | ✅ 已修复 | `DESIGN_BASELINE.md:301` §6 同删"欢迎页/" |
| D25 | ✅ 已修复 | `DESIGN_BASELINE.md` 三处 29→30 + "全量修复"→"已处理收尾"（与档案 C34/C19/C33 实况对齐），快照日期更新 2026-08-02 |
| D26 | ✅ 已修复 | `ROADMAP.md:9` A7 口径统一（档案口径 28+5，855 注为 sed 替换计数） |
| D27 | ✅ 已修复 | 移除欢迎页计划文档 25 checkbox 全勾 + 头部完成注记（2026-08-02，提交 2c16892）+ "29 断言"修正 |
| D28 | ✅ 已修复 | `translations.csv` 删 `GO_SCORE` / `UI_KILLS_TAG` 两孤儿键。验证：i18n_test 9 PASS |
| D29 | 🟦 不修 | 见判定分类（C17 条目已补注说明 back_navigator 属合理模式） |
| D30 | 🟦 不修 | 见判定分类 |

> 修复后回归：`--import` / `--quit-after 300` / **29 断言场景全绿 0 FAIL** / perf_bench rc=0 / autoplay 探针完整跑（480s、3 对局、0 死亡、孤儿 0、帧耗时峰值 7.43ms）——1 个 `score_stagnant` 偶发（Boss 战专注期分数停滞 + 逃跑空窗竞态，该 run 返航 0 次、与 D 系列改动路径无交集，判定为既有探针偶发非本次引入）。

### E 系列（2026-08-02 存量盲区补充审查，只登记未修复）

> 补充审查 D 系列未作为主审对象的存量盲区（敌人体系 / 演出·特效·母舰 / 系统服务·杂项，28 脚本），3 路并行 + 主控核验。**按用户指示只登记不修复**，判定建议供后续决策；完整报告见 `docs/2026-08-02-audit-fix-plan.md` 第四节。

| 编号 | 严重度 | 位置 | 类别 | 描述 | 判定建议 |
| --- | --- | --- | --- | --- | --- |
| E01 | P1 | `bullet.gd:240-246` | 纯bug（C20 静默回归） | 母舰溅射 `_splash()` `as Enemy` cast 对 Boss 失效（注册表含 Boss，Boss 非 Enemy），溅射伤害静默丢失；`_explode` Boss 排除为有意设计 | **修** |
| E02 | P2 | `start_panel.gd:275`/`tutorial.gd:97` | 纯bug（玩家有损） | 教程按钮通关后未禁用，重进教程 `delete_save()` 静默删进行中存档 | **修（最优先）** |
| E03 | P2 | `game_state.gd:118-125` | 设计目标未达（C03 半堵） | 难度表只校验三档是 Dictionary 不校验子键，部分损坏 KeyError→0 HP/0 得分 | **修** |
| E04 | P2 | `dawn_station.gd:282-286`/`return_cinematic.gd:568,702` | 设计目标未达/一致性 | PHANTOM 呼吸 tween 覆盖调用方 `modulate.a`（0.35/0.5 被抬到 0.85-1.0）；base_console 用包装节点规避，用法不一致 | **修** |
| E05 | P2 | `mothership.gd:414-432` | 纯bug（边缘） | H 按住时强制离舰不清 HUD 提前离舰进度条，残留可见 | **修** |
| E06 | P2 | `enemy.gd:469` | 一致性（D10 未收敛） | 侧方离场 `position.x < 960.0` 硬编码 | **修（一行）** |
| E07 | P3 | `bullet.gd:230` | 文档-代码矛盾 | `_explode` C20 注释"注册表全为 Enemy"前提错误（与 E01 同源） | **修（随 E01）** |
| E08 | P3 | `laser_weapon.gd:66-67` | 纯bug（不可达） | buff 归零早退冻结激活态光束；当前无 buff 移除机制 | 待判定（顺手兜底） |
| E09 | P3 | `laser_weapon.gd:13` | 一致性（待判定） | `BEAM_HALF_WIDTH` 未乘 ws（`ENEMY_HIT_RADIUS` 已乘） | 待判定 |
| E10 | P3 | `game_state.gd:947` | 一致性（低危） | `locale` 绕过 `set_locale()` 守卫，手改值状态不一致 | 待判定 |
| E11 | P3 | `game_state.gd:958-959` | 一致性（C02 元素级缺口） | key_bindings 数组元素 `int(k)` 未判型 | 待判定 |
| E12 | P3 | `save_manager.gd:21-28` | 一致性（存量） | `save()` 非原子写，崩溃截断 JSON 丢进度（自愈为 .corrupt） | 待判定 |
| E13 | P3 | `player_damage.gd:64-69` | 热路径边缘 | `heal_tick()` 每物理帧嵌套字典查询 | 待判定 |
| E14 | P3 | `mothership.gd:171-174` | 一致性 | `beam_pts[i] *= ws` 字面违反幂等约定（当前安全：非共享 sub_resource） | 不修（注明安全） |
| E15 | P3 | `enemy.gd:385` | 性能轻微 | 每帧 `buff_count(&"slow_field")` 字典 get | 不修（登记备查） |
