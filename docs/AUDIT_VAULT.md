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
