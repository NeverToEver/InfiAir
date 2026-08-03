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
  - **如何验证**：`--headless --import` 通过；29 个断言测试场景全部 `[PASS]` 0 失败（smoke/pool_reuse/hit_logic/enemy_combat/boss_enrage/tutorial/esc_navigation/mothership_summon/elite_turret_event/formation_strike_event/base_system/buff33/view_zoom/i18n/keybind/startup_flow/back_navigation/meta_health_fx/orbital_strike/boss_phase/boss_pattern/wave_pacing/buff_panel/buff_visuals/window_size/difficulty/balance/intro_cinematic/return_cinematic。注：原记录漏写 intro/return_cinematic 两个、误记为 27，2026-08-02 口径统一订正为 29）。注意：`hit_logic` A21（AGENTS.md 记录的既有失败基线）本次运行通过，判定与该次改动无关、疑似测试顺序/环境差异，已复核无需回退——**2026-08-02 已查明实为 profile 视角档巧合（见下「既有失败基线处置记录」），根因已修复**。
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
- **修复起效记录**：⚠️ **部分完成（2026-08-02 订正，此前误记「未修复」）**
  - **已落地（2026-07-31 `bdb0274`「A5 依赖注入」）**：Boss/精英炮塔对 Spawner 的依赖改为注入——`boss.gd` 新增 `_spawner` + `set_spawner()`，`spawn_minion_at()`/`_summon_minions()` 不再 `get_first_node_in_group("spawner")`；`elite_turret_event.gd` 同法替换 3 处 group 查找（指引第 2 条）；`bullet.gd` 的 Player 强转已由 A1 经 `GameState.player_ref` 落地（指引第 3 条）；指引第 1 条「GameState 作配置中心+注册表」为有意性能权衡保留。
  - **未收敛**：残余依赖点（`hud`/`pause_ui` 等对 Main 的引用）仍经注册表/组间接获取，未全量改显式注入（详见 `DESIGN_BASELINE.md` §7.1）。

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
| A5 依赖倒置 | 严重 | ⚠️ 部分完成（依赖注入已落地 `bdb0274`；GameState 配置中心有意保留，2026-08-02 订正） | 2026-07-31 |
| A6 L 违反 | 中等 | ✅ 已修复（is_boss 语义化特判，2026-08-01 回填） | 2026-07-31 |
| A7 测试耦合 | 中等 | ✅ 已修复 | 2026-07-31 |
| A8 Player 膨胀 | 中等 | ⚠️ 部分完成（PlayerDamage/PlayerDash 已抽，视觉未抽） | 2026-07-31 |

> **修复后处理**：任何一条修复落地后，须回到本表更新状态，并在对应条目回填「修复起效记录」——说明改了什么、为什么起效、用什么验证（相关测试场景：`smoke_test` / `base_system_test` / `pool_reuse_test` / `enemy_combat_test` / `hit_logic_test`）。

## 既有失败基线处置记录（A21：hit_logic_test「Boss 入场降入期玩家弹可伤 Boss」）

> 本条不是 A 系列审计条目，而是 `docs/TESTING.md` 曾登记的既有失败基线（PORTING_PARITY 附录 A 的 A21 断言）。因 2026-08-01 复核曾误判「已自愈」、根因未除，特立档记录，防止同类误判再犯。

- **登记**：2026-07-31（AGENTS.md「既有失败基线」）。描述：`hit_logic_test` A21 断言「Boss 入场降入期玩家弹可伤 Boss」稳定失败。
- **2026-08-01 复核误判**：干净 HEAD 复跑通过（hit_logic 20 断言含 A21），判定「疑似测试顺序/环境差异」、记为已自愈——**结论错误**：通过只是因为当时 `user://profile.json` 的 `view_zoom` 恰为 medium/small 档，失败条件未复现，根因从未排查。
- **根因（2026-08-02 定位）**：A21 把 Boss 与玩家弹放在**硬编码绝对坐标** `(960, 100)`，并假定该处「仍在降入」。`view_zoom=large`（相机 zoom 1.7）时可见区顶缘 `view_world_rect().position.y = 222`，该坐标已在可见区之外——玩家弹下一帧触发 `view_world_rect(80)` 出界判定被 `_despawn()` 销毁，从未与 Boss 碰撞，`hp == max_hp` 恒成立 → 断言稳定失败。物理碰撞本身与视角档无关，失败纯由测试坐标未适配视角档导致。
- **修复（2026-08-02，`test/hit_logic_test.gd`）**：A21 段 Boss/子弹位置改按战斗锚线动态计算 `fight_anchor_y() - 75`（= view 顶缘 + FIGHT_Y - 75 = view 顶缘 + 155）：仍在降入（< 锚线）且恒在 `view_world_rect(80)` 出界判定内（FIGHT_Y=230，任意档位余量充足）。A2 段同类硬编码 `y=150` 一并改 `fight_anchor_y() - 80`（同一脆弱模式：绝对坐标依赖视角档/FIGHT_Y 配置）。
- **为什么起效**：断言不再依赖「small 档下 y=100 恰在可见区内」的隐式前提；任意视角档、任意 FIGHT_Y（>155）下，「Boss 仍在降入」与「玩家弹可命中」都由位置公式直接保证，无环境状态参与。
- **如何验证**：视角档 × 难度 9 组合矩阵（small/medium/large × easy/medium/hard）hit_logic_test 全部 61 PASS 0 FAIL（修复前 large 档必失败）；large/medium 档各连跑 5 轮稳定；smoke_test 142 PASS、view_zoom_test 0 FAIL、boss_pattern/boss_phase/boss_enrage/enemy_combat/wave_pacing 全绿无回归。
- **经验教训**：失败基线登记后必须定位根因并验证根因消除，不能因一次干净环境通过就标记「自愈」；`user://` 等共享持久化状态是测试顺序相关失败的高频来源，涉及视角/窗口/难度档的测试断言不得硬编码绝对世界坐标。

## 文档口径统一处置记录（2026-08-02）

> **触发**：用户指出文档健康度极低，要求「彻头彻尾统一口径，有问题就标记，已完成就标记」。对 `docs/` 全部 26 份文档做了全量交叉核对（3 路并行只读核查：专项设计文档 vs 代码、计划文档 vs git 落地、本档案内部一致性；核心活文档 ROADMAP/DESIGN_BASELINE/ARCHITECTURE/EXIT_FLOW/TESTING/README 亲自核对）。权威基准：31 断言场景 / 版本 3.26 / 提交时间线实测。

**确认并修复的口径问题（按类别）**：

1. **状态误记（最严重）**：A5 依赖倒置状态表/条目/ROADMAP/DESIGN_BASELINE 四处均写「未修复」，实际 `bdb0274`（2026-07-31）已落地 Boss/精英炮塔 Spawner 依赖注入——统一订正为「⚠️ 部分完成（注入已落地，GameState 配置中心有意保留）」。
2. **文档内部自相矛盾**：`DESIGN_BASELINE` §2.4 仍写敌机「两条路径并存」、§7.2 却标「已统一池化」（2026-08-02 性能计划）——§2.4 订正为已统一；`RETURN_HOME_CINEMATIC` §6 写 16.8s、§2/§7 写 11.8s；`INTRO_CINEMATIC` §4 letterbox 110px、§2 与 tscn 为 132px；META_HUD 文档 6-tap vs shader 4-tap（shader 自身注释也过期，一并修）。
3. **断言场景数四代并存（27/29/30/31）**：AUDIT_VAULT A1「27」→29（补漏 intro/return_cinematic）、D 系列两处 29→30、F 系列 23→25、性能记录「27」→31、ROADMAP/DESIGN_BASELINE 29/30→31、计划文档 30→31 等；保留各轮时点正确者（B/C/E 系列）。
4. **失效提交哈希**：`dcef9b6`（ROADMAP/DESIGN_BASELINE 本地账号规格）→ `7aacd3f`；`b02be46`/`57c778b`（07-22 计划）→ `4df9e02`/`7f0aa42`。
5. **G 系列计数**：结论「2 项 P1、9 项 P2」→「3 项 P1、8 项 P2」（清单与提交实测）；批次 2「P2×9」→「P2×8」（提交消息自身笔误）；D 系列结论 P3×6→P3×8、基线 60 提交/195 文件→59/196/+14.6k。
6. **计划文档 7 份无/缺完成标记**：2026-07-30（「未 git commit」过期）、2026-08-01 C 系列报告（零标记）、2026-08-02 D 系列、boss-p2（20 checkbox 全未勾）、E 系列、性能计划（补 920e5e9）、core-logic（补 4 批哈希）——全部回填 ✅ 状态与落地提交；mouse-lock F02 补 `c48383f`。
7. **专项设计文档过期数值**：ELITE §1.2 敌机 HP 采样（55~130/80 → 48~112/65-72，精英 150-230→135-210）；META_HUD uniform 默认值（u_ripple_phase 0.0→1.0、u_crack_spread_min 0.10→0.15、u_crack_width 0.03→0.10）、meta_fx_lod 默认 0→1；BOSS_REDESIGN B5 注记补「已修复」。
8. **release.sh**：L4 注释默认版本 3.25→3.26。

**未改动（有意保留）**：各轮审核的「时点数字」作为历史快照保留（B/C/E/F 系列断言场景数、计划文档执行时点计数）；设计文档中标注「已废弃/被取代」的旧行为快照保留；行号锚点类引用因代码持续演进普遍漂移，本次未逐行更新（以函数名为准，随下次专项维护处理）。

**验证**：`--headless --import` 0 错误（含 shader 注释修订编译）；`smoke_test` 142 PASS 0 FAIL；`--quit-after 300` 0 error；`bash -n release.sh` 通过。改动 20 份文档 + release.sh + shader 注释 + 上一条 A21 修复，共 21 文件 +114/-82。

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
| `docs/archive/2026-07-30-combat-ux-audit-plan.md` | P1-1 mark_ratio 0.4/40% → 落地值 0.25（正文/回退/目标态）；P0-3「不动 world_scale 1/3」加 2026-07-31 上调 0.4 决策变更注 |
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
| 完整报告 | `docs/archive/2026-08-01-godot-best-practice-audit.md` |

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
| 审核区域 | 基线 `8c6dfff`→HEAD（59 提交、196 文件、+14.6k/-3.9k 行，2026-08-02 口径统一订正）涉及 `scripts/` + `autoload/` + `data/` + `docs/` + `test/` |
| 审核方法 | 6 分区并行审核（`docs/AUDIT_REVIEW_SOP.md`）+ 主控交叉核验 + 实证证伪（D03 Label mouse_filter 默认值） |
| 结论 | 无 P0/P1；P2×4 修复 + P2×1 文档登记（D05）+ P3×9 修复 + P3×8 登记不修 + 文档同步 8 处；D03 误报证伪；修复后全量 30 断言场景 0 FAIL |
| 审核人 | Kimi Code CLI（依据用户指示执行） |
| 完整报告 | `docs/archive/2026-08-02-audit-fix-plan.md`（发现-判定-修复追踪单一事实源） |

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

> 修复后回归：`--import` / `--quit-after 300` / **30 断言场景全绿 0 FAIL** / perf_bench rc=0 / autoplay 探针完整跑（480s、3 对局、0 死亡、孤儿 0、帧耗时峰值 7.43ms）——1 个 `score_stagnant` 偶发（Boss 战专注期分数停滞 + 逃跑空窗竞态，该 run 返航 0 次、与 D 系列改动路径无交集，判定为既有探针偶发非本次引入）。

### E 系列（2026-08-02 存量盲区补充审查，只登记未修复）

> 补充审查 D 系列未作为主审对象的存量盲区（敌人体系 / 演出·特效·母舰 / 系统服务·杂项，28 脚本），3 路并行 + 主控核验。**按用户指示只登记不修复**，判定建议供后续决策；完整报告见 `docs/archive/2026-08-02-audit-fix-plan.md` 第四节。

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

## E 系列修复起效记录（2026-08-02 全量处置）

> 按登记判定建议全量落地；修复批次见 `docs/archive/2026-08-02-e-series-fix-plan.md`（发现-判定-修复追踪单一事实源）。

| 编号 | 状态 | 改了什么 / 为什么起效 / 验证 |
| --- | --- | --- |
| E01 | ✅ 已修复 | `bullet.gd _splash()` 的 `as Enemy` 改 Variant 鸭子调用 `take_damage(amount, score_scale)`——注册表含 Boss（Boss 非 Enemy 子类），as Enemy 对 Boss cast 得 null 致溅射 20 伤害静默丢失；与 `laser_weapon._damage_tick`「含 Boss」同模式。`_explode()` Boss 排除为有意设计行为不变。验证：hit_logic_test 新增「溅射对 Boss 生效 20 伤害」断言 PASS |
| E02 | ✅ 已修复 | `start_panel.gd` 教程按钮通关后 `disabled` + `_on_tutorial_pressed()` 加 `tutorial_done` 守卫——重进教程触发 `tutorial._ready` 无条件 `delete_save()`，静默删进行中存档。验证：startup_flow_test 新增「通关后按钮禁用」断言 PASS |
| E03 | ✅ 已修复 | `game_state.gd _valid_difficulty_defs` 增加 `DIFFICULTY_DEF_KEYS` 8 数值键存在+类型校验——缺子键通过后下游 8 处 `DIFFICULTY_DEFS[difficulty][...]` KeyError→0（敌方 0 HP 秒死/得分倍率 0）。验证：balance_test 新增「难度段缺子键回退默认」断言 PASS |
| E04 | ✅ 已修复 | `dawn_station.gd` PHANTOM 全部视觉挂 `BreatheRoot` 呼吸容器，4s 慢呼吸写容器 `modulate:a` 而非站体本身——调用方压 `station.modulate.a`（return_cinematic 0.35/0.5）不再被抬高 2.5~3 倍，与 base_console 包装用法统一。验证：return_cinematic/intro_cinematic/base_system 0 FAIL |
| E05 | ✅ 已修复 | `mothership.start_release()` 入口统一清 HUD 提前离舰进度条——H 按住被强制离舰（警告到期/弹匣耗尽）进度条残留可见。验证：mothership_summon_test 新增「前置可见 + start_release 清除」断言 PASS |
| E06 | ✅ 已修复 | `enemy.gd` 侧方离场 `position.x < 960.0` → `view_world_rect().get_center().x`（D10 同类收口）。验证：enemy_combat/wave_pacing 0 FAIL |
| E07 | ✅ 已修复 | `bullet.gd _explode()` 注释修正——注册表含 Enemy 与 Boss，Boss 由 as Enemy null 排除为有意设计（非「注册表全为 Enemy」）。随 E01 验证 |
| E08 | ✅ 已修复 | `laser_weapon.gd` buff 归零早退前 `if _active: _end_beam()`——防未来 buff 移除机制引入后激活态光束冻结、autofire 卡禁。验证：buff33/smoke 0 FAIL |
| E09 | 🟦 登记不修 | `BEAM_HALF_WIDTH` 乘 ws（0.4）后判定 26→10.4px 显著削弱激光命中，属游戏性变更需产品判断；现视觉可接受 |
| E10 | ✅ 已修复 | `game_state.gd load_profile` locale 经 zh/en 白名单守卫——手改非法值保持默认 zh，与 TranslationServer 状态一致；不调 `set_locale` 免 load 期 `save_profile`/`locale_changed` 副作用。验证：startup_flow/base_system 0 FAIL |
| E11 | ✅ 已修复 | `game_state.gd load_profile` key_bindings 数组元素 int/float 判型——手改字符串 keycode 直接跳过，不再 `int()` 转换错误刷屏（C02 外层守卫的元素级补全）。验证：startup_flow/base_system 0 FAIL |
| E12 | ✅ 已修复 | `save_manager.gd save()` 先写 `.tmp` 临时文件再替换正本（原子写，防写入中途崩溃产生截断 JSON 丢进度）；最坏情况（删旧后 rename 前崩溃）正本缺失 → load 返回 {} 无存档不置 corrupt，优于现状（截断 → 隔离 .corrupt → 丢进度 + 弹损坏提示）。验证：base_system/startup_flow 0 FAIL |
| E13 | 🟦 登记不修 | 缓存被动回血参数与「难度可中途切换」语义冲突（切换后缓存过期需信号刷新链路），超低风险修复范围 |
| E14 | 🟦 登记不修 | `beam_pts[i] *= ws` 当前安全（polygon 为节点内联属性非共享 sub_resource） |
| E15 | 🟦 登记不修 | 每帧 `buff_count(&"slow_field")` 字典 get 无分配，开销极小 |

> 修复后回归：`--headless --import` / `--quit-after 300` 0 错误 / **全量 30 断言场景 0 FAIL**（含新增 E01/E02/E03/E05 断言）。

### F 系列（2026-08-02 鼠标出框准星失控——登记 + 修复）

> 对局中鼠标移出游戏窗口后 Godot 停止派发鼠标移动事件，`get_global_mouse_position()` 冻结在最后位置，准星卡在屏幕边缘、移回时位置跳变；此前尝试未彻底解决。本次新增「鼠标锁定窗口内」设置项（`mouse_lock`，默认开启，profile 持久化）从根上消除出框前提。执行计划存档见 `docs/archive/2026-08-02-mouse-lock-plan.md`。

| 编号 | 严重度 | 位置 | 类别 | 描述 | 判定建议 |
| --- | --- | --- | --- | --- | --- |
| F01 | P2 | `player.gd:585-605`（`aim_point()`） | 纯 bug（输入边界） | 鼠标移出窗口后 `get_global_mouse_position()` 冻结，准星停在边缘；移回时平滑增量 `raw - _aim_last_raw` 跳变。此前修复未彻底 | **修** |

## F 系列修复起效记录（2026-08-02）

| 编号 | 状态 | 改了什么 / 为什么起效 / 验证 |
| --- | --- | --- |
| F01 | ✅ 已修复 | 新增 `mouse_lock` 设置项（默认开启，profile 持久化）+ `scripts/mouse_trap.gd`（挂 Main，`PROCESS_MODE_ALWAYS`）：窗口聚焦期间鼠标移出内容区 `mouse_exited` 信号触发即 `Input.warp_mouse()` 拉回边缘内侧 1px（`_process` 每帧防御兜底），失焦放行不阻碍切换应用。从根上消除"鼠标出框 → 位置冻结"前提，`aim_point()`/`AimCrosshair` 逻辑零改动。设置页「显示」区开关 + 中英双语说明。验证：mouse_lock_test 13 断言 0 FAIL + 全量断言场景回归 0 FAIL（`warp` 窗口事件行为需真机验收） |

> 修复后回归：`--headless --import` / `--quit-after 300` 0 错误 / **全量 31 断言场景 0 FAIL**（含新增 mouse_lock_test）。

### F02（2026-08-02 暂停后 confine 未放行——F01 修复引入缺陷，登记 + 修复）

> F01 落地后实测反馈：暂停时 MouseTrap 仍按 `PROCESS_MODE_ALWAYS` 持续 confine，鼠标被锁在窗口内容区内，无法移到系统标题栏点关闭按钮退出游戏。设计缺陷：confine 生效范围过宽（所有聚焦态），应按"对局准星态"限定。

| 编号 | 严重度 | 位置 | 类别 | 描述 | 判定建议 |
| --- | --- | --- | --- | --- | --- |
| F02 | P2 | `scripts/mouse_trap.gd:48-56`（`_trap_active`） | 纯 bug（交互阻断） | 暂停/Buff/基地/结算等暂停态鼠标仍被 confine，无法移出窗口点系统标题栏关闭按钮退出游戏 | **修** |

## F02 修复起效记录（2026-08-02）

| 编号 | 状态 | 改了什么 / 为什么起效 / 验证 |
| --- | --- | --- |
| F02 | ✅ 已修复 | `_trap_active()` 增加「未暂停」+「系统光标隐藏（准星态）」两放行条件（`AimCrosshair` 在暂停/非准星态恢复系统光标，两条件相互印证，不依赖处理时序），并抽 `_trap_enabled()` 静态纯函数供断言。confine 从此仅限对局准星活跃且窗口聚焦：暂停后鼠标自由移出窗口（点标题栏 × 退出游戏不受阻），对局准星态照常拉回。验证：mouse_lock_test 新增 7 项放行判定断言（23 项全绿 0 FAIL）+ smoke_test 0 FAIL |

> **F02 核查注记（2026-08-02，warp 是否导致准星抖动）**：结论——**不会**。warp 目标恒取 `_last_known_pos`（出框前最后窗口内位置，移出后冻结），位移 ≤1-2px；鼠标在窗口外时 `get_global_mouse_position()` 本就冻结在最后内部位置，warp 后读值连续，`aim_point()` 平滑增量 ≈0。warp 反而把"移回窗口时的数十 px 位置跳变"钳在边缘内侧。边界仅左/上缘第 0 列/行触发单次 1px 回拉（右/下缘最后列在 clamp 范围内不触发）。验证：mouse_lock_test 新增 2 项「warp 位移 ≤2px」断言（25 项全绿 0 FAIL）。

> 修复后回归：`--headless --import` / `--quit-after 300` 0 错误 / mouse_lock_test 25 断言 0 FAIL（13 基础 + F02 放行判定 7 + warp 位移核查 2，2026-08-02 口径统一订正）/ smoke_test 0 FAIL。

# 第五轮审核（2026-08-02 核心逻辑全量代码审查）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 核心逻辑实现代码审核（现代流行标准 + 项目约定），4 分区并行 + 主控 P1 亲验 |
| 工作时间 | 2026-08-02 |
| 审核区域 | 对局编排/状态（main/game_state/spawner/tutorial）、玩家系统（player/player_damage/player_dash/aim_crosshair/aim_frame_layer）、战斗实体（enemy/boss/bullet/laser_weapon/explosion）、服务与对象池（balance_service/save_manager/sfx_player/entity_registry/bullet_pool/enemy_pool/mothership），17 文件约 6900 行 |
| 审核方法 | 分区通读 + 跨文件依赖追踪 + P1 证据亲验（读源码确证） |
| 结论 | 3 项 P1、8 项 P2、21 项 P3；整体质量高，未见协程违规/信号重连/池防护缺失（2026-08-02 口径统一订正：P1=G01–G03、P2=G04–G011） |
| 审核人 | Kimi Code（依据用户指示执行） |

### G 系列（2026-08-02 核心逻辑全量审查，只登记未修复）

> 完整报告（范围/规则/证据/修复优先级）见 `docs/archive/2026-08-02-core-logic-audit.md`。判定建议供后续决策。

| 编号 | 严重度 | 位置 | 类别 | 描述 | 判定建议 |
| --- | --- | --- | --- | --- | --- |
| G01 | P1 | `spawner.gd:501-506,563-572` | 纯bug（整局瘫痪） | Boss 预警 2s 窗口内返航：`clear_pending()` 只停 Timer 不复位 `_boss_active`，无其他复位路径 → continue 后波次/Boss/事件三守卫永久冻结，整局空转；注释"之后按门控再触发属预期"与实际不符（D01 口径不成立） | **修** |
| G02 | P1 | `boss.gd:828`/`laser_weapon.gd:152-158`/`bullet.gd:250-257` | 纯bug（奖励失真） | Boss 逃跑期 `_begin_escape` 只置碰撞层 0 挡 Area2D 路径；激光 `_damage_tick`/溅射 `_splash` 按注册表+距离绕碰撞层 → 逃跑窗口内补刀致死 → `add_boss_kill` 加分/升难度，与 :905 注释及 `fire_enrage_snapshot` 同款 `_escaping` 防护模式矛盾 | **修** |
| G03 | P1 | `tutorial.gd:97`/`start_panel.gd:244-247,282-288` | 玩家有损（E02 补全） | 教程 `_ready` 无条件 `delete_save()`；E02 守卫只拦 `tutorial_done`，漏「有存档且未通关教程」→ 点教程按钮静默删进行中存档 | **修** |
| G04 | P2 | `game_state.gd:706-716` | 逻辑bug | `rebind_action` 冲突清理只扫 `key_bindings` 不扫 `_default_bindings`：未自定义动作的默认键被改绑后与另一动作同键冲突 | **修** |
| G05 | P2 | `tutorial.gd:310-311` | 热路径性能 | 阶段 2 每物理帧 `max_health()` ×2（内部 2 次 cfg + split 分配） | **修**（_ready 缓存） |
| G06 | P2 | `spawner.gd:160-161,178-185` | 健壮性 | `_apply_balance` 嵌套结构（hover_band/types）无判型，手改 JSON 结构损坏时越界崩溃，与 C03/E03 回退口径不一致 | **修** |
| G07 | P2 | `aim_frame_layer.gd:74-80` | 池化复用失配 | `frame_half_size` 首调缓存碰撞半径进 meta，池化实例被不同半径机型复用则框尺寸/入框判定过期（当前唯一池化路径恒同半径未触发） | 待判定 |
| G08 | P2 | `boss.gd:640` | 项目约定 | 逃跑离场 `position.y < -280.0` 硬编码，违反 view_world_rect 约定（enemy 已相对化） | **修** |
| G09 | P2 | `bullet.gd:60`/`balance_service.gd:57` | 热路径性能 | 每发敌弹创建时 `enemy_damage_ramp()` JSON 查询（split+遍历），弹幕压力每秒 30+ 次 | **修**（启动缓存） |
| G010 | P2 | `bullet.gd:197`/`entity_registry.gd:12` | 性能 | `GameState.enemies.has()` 每追踪弹每帧 O(N) 线性扫描 | 待判定 |
| G011 | P2 | `mothership.gd:670-676`/`main.gd:665` | UI 残留（E05 补全） | 返航提前 `queue_free` 母舰不清 HUD 提前离舰进度条（`set_early_leave_charge(-1.0)` 唯一隐藏入口） | **修** |
| G012 | P3 | `game_state.gd:595` | 魔法数字 | `add_boss_kill` 加分基准 500.0 硬编码未入 balance | 修 |
| G013 | P3 | `game_state.gd:900-906` | 边界条件 | `apply_run_save` 恢复 buffs 层数无钳制，手改存档可溢出放大 max_health | 修 |
| G014 | P3 | `tutorial.gd:156,179,204,220` | 一致性 | 教程 4 处硬编码世界坐标（960/600/300），D10 未同步 | 修 |
| G015 | P3 | `tutorial.gd:320,329` | 性能 | 蓄力期间每物理帧 tr()+Label 赋值，超 HUD 0.1s 节流约定 | 修 |
| G016 | P3 | `aim_frame_layer.gd:41-43` | 信号清理 | `_exit_tree` 未显式断开 `aim_assist_changed`（C22 模式对照） | 修 |
| G017 | P3 | `aim_crosshair.gd:48`/`aim_frame_layer.gd:172` | 性能 | `_draw` 每帧直接 `sin()`（未走 sin_fast 查表） | 不修 |
| G018 | P3 | `player.gd:368-373`/`aim_frame_layer.gd:163-168` | 重复代码 | 距离衰减分段函数双实现（`aim_dist_falloff`/`_dist_falloff`），改一侧忘另一侧破坏一致性 | 待判定 |
| G019 | P3 | `player.gd:481-484` | 边界条件 | `movement_locked` 直接 `_dashing=false` 中断冲刺，dash_timer 残留/冷却燃料不返还 | 待判定 |
| G020 | P3 | `laser_weapon.gd:136-137,147` | 防御性缺口 | `_start_beam` 无条件覆写 `_saved_autofire`（当前不可达，E08 同类） | 待判定 |
| G021 | P3 | `laser_weapon.gd:96` | 死代码 | `_aim_dir(_start)` 参数未使用 | 修 |
| G022 | P3 | `explosion.gd:24`/`enemy.gd:237` | 性能 | `spawn_at` 每次 cfg 查询；Enemy 每机新建相同 material | 修 |
| G023 | P3 | `explosion.gd:59-61` | 生命周期 | parent 无效时对已随父销毁的 timer 调 queue_free（UAF 危险行，分支不可达） | 待判定 |
| G024 | P3 | `boss.gd:255,705` | 魔法数字 | 三型普通阶段召唤间隔 6.0 硬编码，无 balance 键 | 修 |
| G025 | P3 | 多文件（enemy/bullet/boss/boss_movement/boss_attacks） | 热路径性能 | 每实体每帧重复 `view_world_rect()`（~130 次/帧） | 待判定 |
| G026 | P3 | `enemy.gd:430`/`boss_fire.gd:19` | 边界条件 | 射手与玩家圆心重合时零向量弹方向，子弹永不销毁 | 待判定 |
| G027 | P3 | `mothership.gd:543,577` | 热路径性能 | `_live_targets` 空目标分支每帧分配数组+全表扫描（C28 口径仅在有目标时成立） | 修 |
| G028 | P3 | `sfx_player.gd:25-26` | 防御性 | `play()` 无池空守卫（build_pool 未调用时越界/除零） | 修 |
| G029 | P3 | `balance_service.gd:36-39` | 类型健壮性 | `cfg()` 数值分支原样返回 JSON 节点，手改 int 字段漂 float（与 C18 显式转换不一致） | 修 |
| G030 | P3 | `mothership.gd:588` | 命名一致性 | 导弹复用 `GATLING_SCORE_SCALE` 得分系数，语义混用 | 修 |
| G031 | P3 | `mothership.gd:182-184` | 资源共享 | 双炮塔共享 ParticleProcessMaterial 写 scale（幂等同值安全，E14 同族） | 不修（注明安全） |
| G032 | P3 | `mothership.gd:168-170`/`mothership.tscn:22` | 注释不符 | 脚本注释"tscn 存 1.0 基准"，tscn 实际 1.25 且脚本硬编码 1.25*ws | 修 |

> 修复后回归口径：修复批次落地后按 `docs/archive/2026-08-02-core-logic-audit.md` 优先级执行，逐条回填本表「修复起效记录」。

## G 系列修复起效记录（2026-08-02 全量处置）

> 修复批次提交：批次 1（P1×3，cb8511b）、批次 2（P2×8，b7b2cc8；提交消息标题写 P2×9 系笔误，正文实列 G04–G011 共 8 项，2026-08-02 口径统一订正）、批次 3+4（P3+待判定，ffef641）。完整审核报告见 `docs/archive/2026-08-02-core-logic-audit.md`。

| 编号 | 状态 | 改了什么 / 为什么起效 / 验证 |
| --- | --- | --- |
| G01 | ✅ 已修复 | `spawner.clear_pending()` 复位 `_boss_active=false`——预警 2s 窗口内返航取消后波次/Boss/事件三守卫不再被永久冻结（D01「按门控再触发」口径恢复成立）。验证：difficulty_test 新增 2 断言 PASS |
| G02 | ✅ 已修复 | `boss.take_damage` 入口加 `_escaping` 拦截——激光/溅射按注册表+距离判定绕碰撞层，防逃跑窗口补刀致死触发 add_boss_kill 奖励失真（对齐 fire_enrage_snapshot 同款防护）。验证：enemy_combat_test 新增「逃跑期 take_damage 无效」断言 PASS |
| G03 | ✅ 已修复 | 教程按钮禁用条件扩为 `tutorial_done or has_save`（E02 补全「有存档未通关」路径），防 tutorial._ready 无条件 delete_save 删进行中存档。验证：startup_flow_test 新增 1 断言 PASS |
| G04 | ✅ 已修复 | `rebind_action` 冲突清理扫默认绑定——未自定义动作默认键被占用时置空绑定覆盖默认，防同键双动作。验证：keybind_test 新增 3 断言 PASS |
| G05 | ✅ 已修复 | tutorial 阶段 2 锁血缓存 `_max_hp`（_ready 一次读，免每物理帧 2 次 cfg）。验证：tutorial_test 0 FAIL |
| G06 | ✅ 已修复 | spawner `hover_band`/`_merge_type` 嵌套结构判型（损坏 JSON 回退默认，对齐 C03/E03）。验证：wave_pacing 0 FAIL |
| G07 | ✅ 已修复 | `Enemy.setup` 每次激活刷新 `aim_frame_radius` meta——池化实例复用不同半径机型不过期。验证：enemy_combat/pool_reuse 0 FAIL |
| G08 | ✅ 已修复 | Boss 逃跑离场基线改 `view_world_rect().position.y - 280.0`（去 280 硬编码，enemy 同口径）。验证：enemy_combat 0 FAIL |
| G09 | ✅ 已修复 | `BalanceService` ramp 因子 load 时缓存（免每发敌弹 path.split+字典遍历 JSON 查询）。验证：enemy_combat/wave_pacing 0 FAIL |
| G010 | ✅ 已修复 | `EntityRegistry` 增 `_enemy_set` O(1) 存在性索引 + `GameState.enemies_has()`，追踪弹每帧判定改走（免 Array.has 线性扫描）。验证：pool_reuse/hit_logic/buff33 0 FAIL |
| G011 | ✅ 已修复 | `mothership._exit_tree` 补 HUD 提前离舰进度条隐藏（E05 只覆盖 start_release，返航回收路径漏清）。验证：mothership_summon_test 新增 2 断言 PASS |
| G012 | ✅ 已修复 | `add_boss_kill` 加分基准 500.0 入 balance（`milestones.boss_kill_base`，BALANCE_MAP 收录）。验证：balance_test 0 FAIL |
| G013 | ✅ 已修复 | `apply_run_save` buffs 恢复层数钳制 ≥0（手改存档负层数不再破坏 buff_count 逻辑）。验证：startup_flow 0 FAIL |
| G014 | ✅ 已修复 | 教程 4 处硬编码世界坐标改 `view_world_rect()` 基线（960/600/300 收敛，对齐 D10）。验证：tutorial_test 0 FAIL |
| G015 | ✅ 已修复 | 教程蓄力百分比文本 0.1s 节流（对齐 HUD 仪表约定，免每物理帧 tr()+Label 赋值）。验证：tutorial_test 0 FAIL |
| G016 | ✅ 已修复 | `aim_frame_layer._exit_tree` 显式断开 `aim_assist_changed`（对齐 player C22 模式）。验证：smoke 0 FAIL |
| G017 | 🟦 不修 | `_draw` 每帧 1 次 sin() 量级可忽略（非热路径瓶颈） |
| G018 | ✅ 已修复 | 距离衰减抽 `Player.dist_falloff_curve` 静态单实现（player/aim_frame_layer 共用，改一侧不再破坏另一侧）。验证：smoke/buff33 0 FAIL |
| G019 | 🟦 登记不修 | `movement_locked` 冻结移动/冲刺为**死字段路径**（全项目无任何写 true 的代码，恒 false、不可达）；狂暴期移动约束由 `apply_enrage_slow` ×0.35 减速实现。2026-08-02 口径修正：狂暴设计独立演进（BOSS_REDESIGN §4.3），不再归因「对齐原作 controls_locked」 |
| G020 | ✅ 已修复 | `_start_beam` 仅非激活时记录 `_saved_autofire`（防御性，当前 _active 门闩下不可达）。验证：buff33 0 FAIL |
| G021 | ✅ 已修复 | `_aim_dir` 删未使用参数（防误导调用方）。验证：buff33 0 FAIL |
| G022 | ✅ 已修复 | 爆炸视觉比例静态缓存 + `CinematicFx.additive_material` 材质静态共享（N 机 N 份→1 份）。验证：enemy_combat/smoke 0 FAIL |
| G023 | ✅ 已修复 | 删 `_boss_seq_step` parent 无效分支对已随父销毁 timer 的 queue_free（UAF 危险行）。验证：enemy_combat 0 FAIL |
| G024 | ✅ 已修复 | 三型普通召唤间隔入配置（`boss.phases.type3.summon_interval`，BALANCE_MAP 收录）。验证：boss_pattern 0 FAIL |
| G025 | 🟦 登记不修 | 每帧重复 view_world_rect()（~130 次）单次开销极小，全局缓存改动影响面大、收益低 |
| G026 | ✅ 已修复 | 敌弹/编队弹 `(player-from).normalized()` 零向量回退 DOWN（圆心重合时防零方向弹永不销毁）。验证：enemy_combat/boss_pattern 0 FAIL |
| G027 | ✅ 已修复 | 母舰加特林/导弹先置位间隔再判空（空目标不每物理帧分配数组+扫注册表）。验证：mothership_summon 0 FAIL |
| G028 | ✅ 已修复 | `SfxPlayer.play()` 池空守卫（build_pool 未调用时防越界/除零）。验证：headless 全量 0 报错 |
| G029 | ✅ 已修复 | `BalanceService.cfg()` 数值分支按 default 类型显式转换（JSON float 不再漂 typed int 字段，对齐 C18）。验证：balance_test 0 FAIL |
| G030 | ✅ 已修复 | 导弹得分系数独立 `MISSILE_SCORE_SCALE` 常量（原复用 GATLING 语义混用）。验证：mothership_summon 0 FAIL |
| G031 | 🟦 不修 | 双炮塔共享 ParticleProcessMaterial 写 scale 为幂等同值安全（E14 同族注明口径成立） |
| G032 | ✅ 已修复 | 母舰注释修正（tscn 实存 1.25 基线）+ `SHIP_SCALE` 具名常量。验证：mothership_summon 0 FAIL |

> 修复后回归：`--headless --import` 0 错误 / BALANCE_MAP 刷新（2 新键、0 缺失、未引用键仅既有 `version`）/ 批次相关断言场景（difficulty/enemy_combat/startup_flow/keybind/mothership_summon/tutorial/pool_reuse/boss_phase/boss_pattern/balance/buff33/hit_logic/wave_pacing/smoke）全 0 FAIL。

# 性能优化落地记录（2026-08-02，全量）

> 依据 `docs/archive/2026-08-02-performance-optimization-plan.md` 全量落地（P0×4 / P1×7 / P2×8），改动 24 个源码文件（git 统计 27 文件，另含 3 份 docs，2026-08-02 口径统一订正）。本节登记与既有审计条目的交集回填；完整落地摘要、A/B 数据与回归见计划书 §12。改动面：`game_state.gd`（view_world_rect 帧缓存 / 回血链缓存 / mission 守卫）、`spawner.gd`+`enemy_pool.gd`+`enemy.gd`（敌机池化统一）、`enemy/boss/turret_battery/formation_craft`（受击闪白手动衰减）、`player.gd`（残影池 / ticks 单取）、`aim_frame_layer.gd`（扫描缓存）、`starfield.gd`（绘制合批）、`hud.gd`（文本档位守卫 / sin_fast）、`meta_health.gdshader`（减采样）、P2 各项。

| 编号 | 原状态 | 回填 | 改了什么 / 为什么起效 / 验证 |
| --- | --- | --- | --- |
| D08 | ✅ 已修复 | ✅ 补充 | 既有 `_cached_max_hp` 之上再断深层链路：`max_health()` 基础值 `_apply_balance` 缓存、`passive_regen_delay/rate` 难度变更刷新——`heal_tick→heal→max_health` 全链不再每物理帧 cfg（全仓唯一每帧 cfg 违规点消除）。验证：smoke 142 / buff33 29 PASS |
| D11 | 🟦 观察级不修 | ✅ **已修复** | 受击闪白多 tween 竞争改为**手动衰减**（`_flash_timer` + `_physics_process` 逐帧 lerp，enemy/boss/turret_battery/formation_craft 四实体）——消灭每命中一次 Tween 分配与竞争，Godot 4.6 Tween 无 `reset()` 故不走预建复用。验证：hit_logic 61 / enemy_combat 33 / smoke 142 PASS |
| E13 | 🟦 登记不修 | ✅ **已修复** | `heal_tick` 每物理帧嵌套字典查询——`passive_regen_delay/rate` 缓存 + `set_difficulty`/`_apply_balance` 刷新（原"难度中途切换缓存过期"顾虑由刷新链路解决）。验证：smoke 142 / base_system 46 PASS |
| G017 | 🟦 不修 | ✅ **已修复** | `aim_crosshair`/`aim_frame_layer` 每帧直接 sin() 改 `Enemy.sin_fast`（连同对局路径 11 处批量清扫；过场/一次性构建豁免）。验证：i18n / mouse_lock / smoke 0 FAIL |
| G025 | 🟦 登记不修 | ✅ **已修复** | 每帧重复 `view_world_rect()`（~130 次）——`GameState` 物理帧号守卫缓存（同帧子弹×N/敌机×N/玩家/Boss 共享一次视口查询，zoom/camera 变更四点失效）。验证：view_zoom 50 / smoke 142 / perf_bench A/B -8~9% |
| G027 | ✅ 已修复 | ✅ 补充 | 既有空目标早退之上补 `_live_targets()` 输出缓冲复用（免每次发射分配新 Array）。验证：mothership_summon 32 PASS |

> 回归：`--headless --import` / `--quit-after 300` 0 错误；smoke 142 / pool_reuse 12 / base_system 46 / 全量 31 断言场景 0 FAIL（落地时点实际为 31，含 mouse_lock_test；原记 27 系口径笔误，2026-08-02 订正）；perf_bench 同环境 A/B 中位数 0.131→0.120 ms/帧（约 -8~9% CPU 逻辑耗时）。

# 第二轮性能优化落地记录（2026-08-03）

> 依据第二轮性能目标（2026-08-03：剖析 → 修复 → A/B → 全量回归）落地。剖析范围：8-03 公平感机制新增代码（player_parry / graze / grace / ui_segmented_bar / boss 转场清弹——均事件驱动、无常驻热点，与 perf_bench 基线持平印证）+ 约 50 处每帧回调全量静态扫描（explore 子代理）+ 窗口模式渲染基准（临时诊断场景，跑完即删）。改动 6 个源码文件。验证：perf_bench 无回归（基线 0.121 → 优化后 0.122 ms/帧中位数，0.113-0.126 同分布噪声）+ 窗口渲染基准 0 SCRIPT ERROR + 全量 **35** 断言场景 0 FAIL。

| 编号 | 等级 | 改了什么 / 为什么起效 / 验证 |
| --- | --- | --- |
| J01 | P0 | **`mouse_trap.gd` Godot 4.6 API 变更 bug**：`win.mouse_position` 属性在 4.6 移除（改为 `get_mouse_position()` 方法），窗口模式下 `_process` **每帧 SCRIPT ERROR**——报错打印开销 + 鼠标锁定功能从未真机生效（F01「warp 需真机验收」验证缺口；headless 首行早退不可见）。改 `win.get_mouse_position()`（同步更新注释）。验证：窗口模式渲染基准 0 SCRIPT ERROR；mouse_lock_test 0 FAIL。**本轮实际收益最大项**（headless perf_bench 测不到：首行早退） |
| J02 | P1 | `main.gd:349` 每帧 `InputMap.has_action(&"give_up")` 字典查找（give_up 静态定义，结果全程不变）→ `_ready` 缓存 `_give_up_bound`。验证：smoke 0 FAIL |
| J03 | P1 | `player.aim_point()` 鼠标/摇杆采样（`get_global_mouse_position` + `Input.get_vector`）移入渲染帧守卫——同帧 Player/LaserWeapon 双采样（120 次/秒）消除；同帧采样值一致，行为零变化。验证：smoke / parry / graze / hit_logic 0 FAIL |
| J04 | P2 | `warp_gate` 召唤期每帧 ~126 次直接 cos/sin（椭圆环 48×2 + 弧 3×10，全程每帧）→ `Enemy.sin_fast/cos_fast`（G017 同族最后一处多点循环）。验证：mothership_summon 0 FAIL |
| J05 | P2 | `mothership` 牵引光束同帧 2 次 `Time.get_ticks_msec()` 合并为帧首一次（`_update_beam_fx` 增 now_s 参数）。验证：mothership_summon / base_system 0 FAIL |
| J06 | P2 | `orbital_strike` 瞄准环脉冲 3 次直接 sin → `Enemy.sin_fast`。验证：orbital_strike 0 FAIL |

> **登记不修（论证后收敛）**：① E15 延续 3 个未登记位置——`player.gd:565,570`（fuel_drain/regen 率）/`boss.gd:639`（slow_factor）/`laser_weapon.gd:66` 每帧 `buff_count` 字典 get（StringName 键无分配，与 E15 口径一致，补注）；② G007 收敛登记——`aim_frame_layer.gd:82-88` 碰撞半径 meta 缓存对池化异半径复用的过期失配，当前唯一池化路径恒同半径未触发，登记不修；③ `player._update_parry_visuals` ACTIVE 期每帧 6 元素 PackedVector2Array（Polygon2D 赋值值语义，无法像 Line2D 那样预建原地写；占空比 ~13% 收益微小）；④ `start_radar` 主菜单每帧 ~400 draw_arc（静态几何与扫描线同节点，视觉设计取舍）；⑤ `tutorial` 阶段 2 每帧 `get_children()` 计数（教程瞬态，收益低）。

> 回归：`--headless --import` 0 错误 / `--quit-after 300` 0 错误 / gdformat + gdlint 全绿 / 全量 **35** 断言场景 0 FAIL（31 既有 + 4 公平感机制新增：grace_period / graze / boss_phase_transition / parry）/ perf_bench 中位数 0.121→0.122 ms/帧（同分布噪声，无回归）/ 窗口渲染基准（200 敌机压力、600 渲染帧）：avg 22.4ms（44.7fps）、draw calls 均值 ~944 峰值 1130、**0 SCRIPT ERROR**。

---

# 第六轮审核（2026-08-02 健壮性专项）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 健壮性（鲁棒性）专项——崩溃/挂起/状态错乱/数据损坏路径（空输入、资源加载失败、除零/NaN、节点生命周期、信号重入、幂等、状态机非法转换、池边界、配置无域校验） |
| 工作时间 | 2026-08-02 |
| 审核区域 | `scripts/` 全部 + `autoload/game_state.gd`（三路并行：对局编排+玩家 / 战斗实体+事件 / UI+服务+表现） |
| 审核方法 | 三路并行只读扫描 + 主控交叉核验（对照 A-G 系列基线去重，G026/C03/E03/G06 等已处理项不重复）+ 判定分类 |
| 结论 | 无 P0；P1×3 + P2×6 + P3×20（组）；整体健壮性强（对象池双防护/注册表去重/Timer 替代协程/幂等守卫覆盖大多数重入与生命周期路径）；真实风险集中在手改 balance.json 损坏数据的判型缺口与少量零值/除零边缘 |
| 审核人 | Kimi Code CLI（依据用户指示执行） |
| 完整报告 | `docs/archive/2026-08-02-robustness-audit.md`（发现-判定-修复追踪） |

## H 系列修复起效记录（2026-08-02 全量落地，3 批提交）

| 编号 | 严重度 | 改了什么 / 为什么起效 / 验证 |
| --- | --- | --- |
| H01 | P1 | 右摇杆瞄准改四向独立动作（`aim_left/right/up/down`，axis 2/3 ±1）——`Input.get_vector` 正负同动作恒为零，P0-1 虚拟准星完全失效；base_system_test 新增四向动作/轴事件断言 |
| H02 | P1 | `apply_key_bindings` 按事件类型只擦键盘（`action_erase_event` 单事件）——原 `action_erase_events` 连手柄事件一起清，改键/重置后本会话手柄失效；测试新增改键/重置后手柄事件保留断言 |
| H03 | P1 | 难度档 `milestone/cycle_mult ≤ 0` 数值域校验——恒 0 阈值致 continue_run 里程碑 while 永不退出挂死；difficulty_test/balance_test 回归 |
| H04 | P2 | BGM 运行时 `ResourceLoader.load` 判空降级（push_warning + return）——缺资源不再空引用崩溃 |
| H05 | P2 | homing 追踪 `dist <= 0` 保持原向——除零产生 inf/NaN 角度污染弹坐标 |
| H06 | P2 | laser `_saved_autofire` 捕获移到 `_active=true` 之前——死守卫修复，`_end_beam` 不再无条件强开 autofire |
| H07 | P2 | spawner `unlocked_types`/`_pick_bullet_type`/enemy 弹种池空池回退首型/单发——`randi()%0` 崩溃防护 |
| H08 | P2 | meta_health `crack.density` 长度+元素校验回退默认档——越界索引/float 转换错误防护 |
| H09 | P2 | hud 警告横幅闪烁对循环外置淡出+hide + tween 互斥缓存——旧实现 set_loops 包住淡出，首轮末尾即永久隐藏（声称 2s 实 0.9s） |
| H10 | P3 | bullet `setup`/boss_attacks 编队齐射零方向弹统一回退 DOWN（G026 同族） |
| H11 | P3 | boss `hp_mults` 长度+元素校验/`STRAFE_SPEEDS` 短数组回退/`fire_intervals` 非数组判型——Boss HP=0 免疫伤害静默与 _ready 崩溃防护 |
| H12 | P3 | enrage `square_path_ratio` 钳制 (0.05, 1.0]——0 值除零产生 inf 轨道 NaN |
| H13 | P3 | elite `fire_interval`/mothership_summon `shot_durations` 判型+判长回退（G06 口径） |
| H14 | P3 | mothership `_warp_gate` 调用 `is_instance_valid` 判空（场景卸载时序悬挂引用） |
| H15 | P3 | 配置 clamp 批量：事件间隔/震动衰减/HUD 轮询与低血周期/时间步进/蓄力时长/meta_health 时长键（tau/duration/fade）——≤0 除零/节流失效/永不衰减防护 |
| H16 | P3 | `world_scale` 域校验钳制 ≥0.01——0/负使机体归零或镜像翻转 |
| H17 | P3 | exit_confirm 淡出退出改 `tween_callback(get_tree().quit)`——替代 `await tween.finished` 挂起协程（AGENTS 协程纪律） |
| H18 | P3 | missions 存档恢复保留 `goal` 键——整体替换丢 goal 致 `mission_completed` 永久哑火（潜伏） |
| H19 | P3 | enemy `hover_band` 判型+判长回退（对齐 spawner G06） |
| H20 | P3 | 生命周期/边界守卫组：buff_select 重建时 _closing 软锁复位、tutorial 失败/结束态防阶段推进、ui_theme 按钮 tween 互斥 kill、base_console 路线 buff 名缺键兜底 + 负治疗钳制、settings_ui `_pages` 空防御、cinematic_fx 点列<2 防御、return_cinematic 镜头时长 0 除零防御 |

> 修复后回归：`--headless --import` 0 错误；批次相关断言场景（smoke/tutorial/buff33/base_system/return_cinematic/back_navigation/i18n/boss_pattern/mothership_summon/elite_turret_event/meta_health_fx/enemy_combat/difficulty/balance）全 0 FAIL。

## GDScript 引擎警告分层与持续改进清单（2026-08-02，chore/gdscript-warnings 分支）

> 在 `project.godot` `[debug]` 段部署 Godot 4 编译器警告系统（`debug/gdscript/warnings/*`），三层分级：

| 级别 | 配置 | 处置 |
| --- | --- | --- |
| **error（零容忍，CI 门禁）** | 未使用变量/私有字段/信号、遮蔽变量与内置函数、整数除法、冗余 await、注解顺序等 25 类 | CI import 出现 "Warning treated as error" 即失败；已修复全部现存 error（含本轮 20 条 InputEvent 判型注解、2 处遮蔽改名、6 处子类引用字段注解） |
| **warn（编辑器 GUI 可见）** | unsafe_cast / unsafe_method_access / unsafe_property_access / untyped_declaration / untyped_variable / unsafe_line 共 6 类 | **持续改进清单 202 条**：unsafe_method 91（Variant/Node 上调用子类方法，多为判型后安全）、unsafe_cast 54（`as` 受检查 cast，失败返回 null 不崩溃）、untyped 66（`for x in dict/array` 迭代变量，盲加类型有运行时断言风险，需逐处确认容器类型）、unsafe_property 33（InputEvent 等，已修 20 条判型注解，其余待收口）。**修复路径**：逐处类型收敛或 `@warning_ignore` 声明判型已保证安全；编辑器脚本状态栏实时可见 |
| **ignore（项目风格确认真冲突）** | inferred_declaration（`:=` 是 Godot 官方推荐）、return_value_discarded（Tween 链式标准写法） | 项目风格冲突，关闭 |

**验证**：配置定稿后 `--headless --import` 0 error；InputEvent 判型注解后 meta_health_fx/smoke/base_system/back_navigation 回归通过。

---

# 第七轮审核（2026-08-02 全链路静态分析与修复）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 全游戏链路深度语法与逻辑探查（静态门禁 + 9 路子代理并行深读 + 主控复核） |
| 工作时间 | 2026-08-02 |
| 审核区域 | `scripts/` 61 文件 + `autoload/game_state.gd`，按链路分 9 组并行（玩家/战斗/敌人/Boss/母舰返航/事件/UI/核心编排/过场） |
| 审核方法 | 静态门禁（gdformat/gdlint/引擎 import+冒烟）全绿后派 9 路子代理并行深读（交叉核对 balance.json 键、信号配对、池生命周期、热路径、world_scale 幂等），主控对 P1 级发现读码复核，最后全量 31 断言场景回归 |
| 结论 | 无 P0；P1×2 + P2×17 + P3×约 20（组）。其中：2 项 P2 经核实无需修（编队炸弹保护舱/鼠标 warp 坐标待实测）、2 项与既有「登记不修」决策冲突已回退（E09/E15）、1 项 P3 死标记核实为测试契约非死代码（hive_volley） |
| 审核人 | Kimi Code CLI（依据用户指示执行） |

## I 系列发现与处置（全量修复，5 批提交 025b393/18b5ad8/89ee243/ecb9d33/6bbbf8b）

| 编号 | 严重度 | 位置 | 类别 | 描述 | 处置 |
| --- | --- | --- | --- | --- | --- |
| I01 | P1 | `bullet.gd:295` | 纯bug（运行时错误+伤害放大） | 爆炸弹命中分支对 enemy 组 Area2D 鸭子调用 `is_boss()`；TurretBattery/FormationCraft（extends Area2D）无此方法 → 运行时错误中断函数 → 子弹不销毁二次命中 | ✅ 已修复（批次1）：改「`not area.has_method("is_boss") or not area.is_boss()`」双条件——无方法（炮台/编队机）或返回值 false（普通敌机）均爆炸，Boss/精英 true 不爆炸。验证：hit_logic_test A12 全 PASS |
| I02 | P1 | `formation_strike_event.gd:222` | 纯bug（玩法节奏失真） | `_begin_run` 循环 k 外层 i 内层生成非排序时刻表 `[0,0.8,1.6,2.4,0.4,...]`，`_process_drops` 按单调 `_state_time` 贪心消费 → 第二波炸弹堆积到末尾同一帧（4 机时同帧 4 弹），与注释「僚机错开 bomb_interval」设计意图相悖 | ✅ 已修复（批次1）：循环转置为 i 外层 k 内层，时刻表单调递增。验证：formation_strike_event_test 0 FAIL |
| I03 | P2 | `player.gd:199` | 信号清理 | `_exit_tree` 漏断 `joy_settings_changed`（buffs_changed/aim_assist_changed 均断开），重入树重复连接致回调重复执行 | ✅ 已修复（批次2）：补对称断开。验证：smoke 0 FAIL |
| I04 | P2 | `laser_weapon.gd:29` | 尺寸族一致性 | `BEAM_HALF_WIDTH` 未乘 world_scale 而 `ENEMY_HIT_RADIUS` 乘了（同一命中公式两杠杆） | 🟦 登记不修：AUDIT_VAULT E09 既有决策（乘后 26→10.4px 显著削弱激光命中，属游戏性变更需产品判断），本轮改动已回退 |
| I05 | P2 | `spawner.gd:501` | 资源清单管理 | `_pending_telegraphs` 悬空引用只增不减（SpawnTelegraph 0.6s 自毁但未从数组移除），长局累积到返航 | ✅ 已修复（批次1）：telegraph 连接 `tree_exited` 自动 erase，与 `_pending_timers` 对称。验证：enemy_combat/wave_pacing 0 FAIL |
| I06 | P2 | `enemy.gd:383` | 生命周期防御 | `_exit_tree` 的 `_pool.forget(self)` 缺 `is_instance_valid(_pool)`（与 `_despawn` 不对称），池先于实例释放时踩悬空引用 | ✅ 已修复（批次1）：补齐判空。验证：pool_reuse 0 FAIL |
| I07 | P2 | `orbital_strike.gd` | 配置无域校验（潜在软锁） | `IMPACT_AT>=1.0` 时 finished 先于 struck → main 收不到 `_on_orbital_struck` → 树保持暂停+玩家锁输入**永久卡死**；`DURATION=0`/`MISSILE_FROM>=IMPACT_AT` 同族 | ✅ 已修复（批次3）：时轴序钳制（duration≥0.01、impact_at≤0.95、missile_from<impact_at）。验证：orbital_strike_test 0 FAIL |
| I08 | P2 | `boss_fire.gd` | 除零/NaN | `fire_ring`/`fire_enrage_wave`/`fire_bullet_wall` 对 cfg 直读弹数/墙数（如 ENRAGE_SNAPSHOT_*）无下限钳制，误写 0 时 `float(count-1)` 除零 NaN 方向 | ✅ 已修复（批次3）：入口 `maxi(2, count)` 钳制。验证：boss_pattern/boss_enrage 0 FAIL |
| I09 | P2 | `turret.tscn:7` | 资源共享 | CircleShape2D 被 `turret_battery.gd` 运行时写半径但缺 `resource_local_to_scene=true`（AGENTS 明文约定，enemy.tscn 已修同型），当前全实例写同值未暴露 | ✅ 已修复（批次3）：补 `resource_local_to_scene = true`。验证：elite_turret_event 0 FAIL |
| I010 | P2 | `mothership.gd:462-480` | 同帧双触发 | STAY 态警告到期 `start_release()` 与 `_early_timer` 到点 `_early_depart()` 同帧双入口 → 二次 start_release 重复释放演出+计时重置 | ✅ 已修复（批次3）：`start_release` 幂等守卫（`_state != STAY` 早退）。验证：mothership_summon 0 FAIL |
| I011 | P2 | `buff_select.gd:99` | 隐性软锁 | `_on_locale_changed` 在 `_closing` 分支只复位暂停未置 `visible=false`/复位 modulate → 面板残留+对局不暂停+`if visible: return` 致里程碑**永久跳过** | ✅ 已修复（批次2）：补齐关闭语义 + 重建后 grab_focus。验证：buff_panel/buff33 0 FAIL |
| I012 | P2 | `formation_bomb.gd:93` | 语义核实 | 编队炸弹 AoE 直判伤害绕 monitoring，疑穿透母舰保护舱 | 🟦 核实无需修：母舰 `_start_docking` 即 `set_invincible(999.0)` 覆盖整个驻留期，`take_damage` 的 invincible 检查免疫该伤害 |
| I013 | P2 | `hud.gd:556` | tween 竞态 | `_show_warning` 淡出 tween 与 label 闪烁 t2 未纳入 `_warning_tween` 互斥，二次警告被旧淡出 hide() 提前压制整段失效 | ✅ 已修复（批次2）：blink/fade 合并管理，阶段切换 kill 当前活跃 tween。验证：smoke/buff_panel 0 FAIL |
| I014 | P2 | `main.gd:519` | 流程遗漏 | 「继续对局」恢复数据后缺 `_start_entry_sequence()`（开场/返航继续出击均有），与 ARCHITECTURE/D01 注释声明不符 | ✅ 已修复（批次4）：补入场衔接序列（内部 is_connected 守卫幂等）。验证：entry_animation/startup_flow 0 FAIL |
| I015 | P2 | `game_state.gd:132` | 挂死（H03 补全） | 全局 `milestones.cycle_mult` 无单调性校验，≤0 阈值平台化 → `apply_run_save` while 里程碑永不退出挂死；原 H03 检查在 difficulty 子表（无 cycle_mult 键）为死代码 | ✅ 已修复（批次4）：全局键 `maxf(...,0.01)` 域校验 + 删死代码。验证：difficulty/balance 0 FAIL |
| I016 | P2 | `game_state.gd:1246/1314` | 判型缺口 | `load_profile` 的 high_score/date 直 `int()`，未走 save_num 判型惯例，手改档案非法类型中断加载链 | ✅ 已修复（批次4）：改 `int(save_num(...))`。验证：startup_flow/base_system 0 FAIL |
| I017 | P2 | `tutorial.gd:262` | 悬挂引用 | `_mothership` 释放后引用未置空（main.gd:636 有 tree_exited 置空模式），阶段 3 判空依赖释放对象 `==null` 语义 | ✅ 已修复（批次4）：补 `tree_exited` 置空连接。验证：tutorial_test 0 FAIL |
| I018 | P2 | `cinematic_fx.gd:202` | 潜伏崩溃 | BeamFlow `_sample_at` 缺空数组守卫，`points.size()<2` 时负索引越界（当前调用方传 24 点不可达） | ✅ 已修复（批次4）：`_samples.is_empty()` 早退。验证：return_cinematic/intro_cinematic 0 FAIL |
| I019 | P2 | `mouse_trap.gd:84/94` | 坐标语义待实测 | `Input.warp_mouse` 窗口相对 vs 全局屏幕坐标语义跨平台不一，加 `win.get_position()` 的正确性需窗口环境实测 | 🟦 待判定：注释含落地调研结论（接受屏幕坐标），无头环境无法实测，登记待窗口环境验证 |
| I020 | P3 | `enemy.gd:405` | 热路径缓存 | `_physics_process` 每帧 `buff_count(&"slow_field")` 字典查询 | 🟦 登记不修：AUDIT_VAULT E15 既有决策（开销极小），本轮改动已回退 |
| I021 | P3 | `enemy.gd:247` | 视觉错档 | 精英尾焰光点 `_ready` 时 `is_elite` 恒 false（池化实例 setup 未跑），半径恒取普通档 | ✅ 已修复（批次1）：半径档移入 `_update_tail_glow` 按 is_elite 绝对 scale 重算（幂等）。验证：enemy_combat/pool_reuse 0 FAIL |
| I022 | P3 | `enemy.gd`（2 处） | 参数倒置 | `randf_range(1.0, fire_interval)` 当 fire_interval<1.0 参数倒置报错 | ✅ 已修复（批次1）：`maxf(fire_interval,1.0)` 钳制 |
| I023 | P3 | `spawner.gd:428` | 越界 | `unlocked_types` 用 `UNLOCK_SCORES[i]` 按机型表索引，短数组越界崩溃 | ✅ 已修复（批次1）：循环上界 `mini` 钳制 |
| I024 | P3 | `boss_movement.gd:30` | 死代码 | `reset_press` 内 `_press_timer = _press_timer` 自赋值空语句，注释暗示有保留语义 | ✅ 已修复（批次3）：删除自赋值，注释修正 |
| I025 | P3 | `boss_attacks.gd:319` | 死标记核实 | `hive_volley` meta 疑只写不读 | 🟦 核实非死代码：`boss_pattern_test.gd:304` 场景 4 断言依赖该 meta 计数，保留 |
| I026 | P3 | `mothership_summon_window.gd:315-340` | 帧率相关失真 | 插值基准读上一帧 `set_point_position` 后的当前值（非原始端点），形成帧率相关累积轨迹 | ✅ 已修复（批次3）：构建期缓存原始端点。验证：mothership_summon 0 FAIL |
| I027 | P3 | `dawn_station.gd:250` | tween 空转 | 毁灭态碎片 `set_loops()` 但目标为固定值，循环重放立即完成、碎片冻结首圈末位 | ✅ 已修复（批次3）：改往复段（外飘→返回）。验证：return_cinematic 0 FAIL |
| I028 | P3 | `scheduled_event_trigger.gd:22` | 无域校验 | `_chance` 未钳制 [0,1]，越界必触发/永不触发 | ✅ 已修复（批次3）：`clampf` |
| I029 | P3 | `main.gd:69-70/314` | 除零/窗口 | `HOME_CHARGE_TIME`/`GIVE_UP_HOLD_TIME` 无除零钳制（H15 只钳 DOCK）；H 蓄力未检查召唤小窗 | ✅ 已修复（批次4）：`maxf(...,0.01)` + `_summon_window == null` 守卫 |
| I030 | P3 | `game_state.gd:133-143/1176` | 防御/注释 | `_prog_per_*` 负值、`_max_hp_base` 无下限；add_buff 注释声称有 max_stacks 钳制实无 | ✅ 已修复（批次4）：负值/下限钳制 + 注释修正 |
| I031 | P3 | `meta_health_fx.gd:329/204` | 配置双源 | 心跳淡出硬编码 0.3s（应读 `dying_fade`）；DYING 阈值 THRESHOLDS 硬编码 0.20 与 cfg 双源 | ✅ 已修复（批次2）：统一读 `_cfg`。验证：meta_health_fx 0 FAIL |
| I032 | P3 | `settings_ui.gd:447` | 守卫无效 | `_on_locale_changed` 先直写节点文本后 `if _pages.is_empty(): return`——守卫位置无效 | ✅ 已修复（批次2）：守卫提前至函数首行 |
| I033 | P3 | `cinematic_fx.gd:83/257` | 边界/回卷 | `ring_points(n=0)` 读未写元素；SpeedLine 斜向 dir 不回卷（当前调用方只传 DOWN 不可达） | ✅ 已修复（批次4）：`n<=1` 早退 + 按分量回卷 |
| I034 | P3 | 多文件（player_dash 等） | 热路径缓存 | `dash_cooldown_max()` HUD 轮询路径每调用查 cfg（A5 缓存模式未覆盖） | ✅ 已修复（批次2）：并入 buffs_changed 缓存。验证：buff_panel 0 FAIL |
| I035 | P3 | `warp_gate.gd:163` | 注释失实 | 注释称附件走 set_point_position 不随 scale 变，实 `_swirls/_lip` 用节点 scale | ✅ 已修复（批次3）：注释修正（不改实现） |
| I036 | P3 | `formation_bomb.gd:38` | 死配置 | `collision_mask=1` 无碰撞信号连接 | ✅ 已修复（批次1）：加注释说明保留为语义文档 |
| I037 | P3 | `player.gd` 弹反盾判定 | Godot API 行为陷阱 | `ConvexPolygonShape2D` 对「圆心+弧」扇形（顶点含圆心、跨 140°）的内外判定不可靠——实测扇区外（玩家正下方 20px 处）弹被误判命中弹反；反转顶点环序无效 | ✅ 已规避（2026-08-03，公平感机制四实现期）：盾判定改**圆盘 shape 触发 + 回调内精确扇形过滤**（距离 ≤ 半径 且 角度在机头前方 ±arc），几何与视觉严格一致；`parry_test` 扇区外用例覆盖 |
| I038 | P3 | `player.gd` 擦弹受击区排除 | Godot API 行为陷阱 | 物理回调内（area_entered flush 阶段）`Area2D.overlaps_area()` 返回陈旧结果——受击盒内生成的弹实测查询 false 被误放行计擦弹分；另 `area_entered` 信号延迟 flush：回调执行时弹可能已被清弹回收（位置移入池位），实时位置判定失效 | ✅ 已规避（2026-08-03，公平感机制二实现期）：受击区排除改**单次距离判定**（事件驱动一次计算非逐帧），并加 `Bullet.is_active()` 守卫排除已回收弹；`graze_test` 用例 3/2 覆盖 |

## 测试基础设施补记（2026-08-02）

- **问题**：`test/*.tscn` 不被主场景引用 → `--import` 不解析 test/ 脚本；8e370f9e 警告门禁（narrowing_conversion/unused_variable 等 error 级）与 test/ 既有代码冲突时，**场景启动即静默挂起**（只打 banner 后空转、0% CPU、无错误输出），全量回归被单场景阻塞 30min+（CI 运行同型 cancelled）。
- **已修复**（批次5 6bbbf8b）：`boss_pattern_test:457` 窄化显式 `int()`（Boss.max_hp 为 float，2026-07-28 引入）、`wave_pacing_test:31` 未使用变量删除。修复后全量 31 断言场景 0 FAIL。
- **建议（待定）**：① `test/` 纳入 `gdformat --check` 范围（当前仅 `autoload/ scripts/`，test/ 长期未格式化）；② CI 断言场景循环加单场景超时，防单场景挂起阻塞整个 job（当前依赖 job 级 30min timeout 兜底）。

> 修复后回归（2026-08-02 晚）：`--headless --import` 0 错误 / `--quit-after 300` 0 错误 / **全量 31 断言场景 0 FAIL**（修复前 boss_pattern/wave_pacing 编译错误挂起、hit_logic A12 我方 has_method 语义缺陷已修正，全绿为修复后最终口径）。

## 测试基础设施补记（2026-08-03）

- **问题**：`autoplay_test.gd` 自 66c1c9e（2026-08-02）integer_division 升级 error 级后**编译失败**（7 处毫秒→秒显示除法，`X / 1000`）——探针场景启动即挂起，此前从未被 CI/本地流程执行（CI 显式跳过 autoplay、本地未跑），属**未登记失败基线**。2026-08-03 首次执行验证清单「autoplay_test 长局探针」时暴露。
- **已修复**：7 处加 `@warning_ignore("integer_division")`（对齐 66c1c9e「有意整数除法注解」先例，语义不变）。验证：60s + 180s 探针 0 异常。
