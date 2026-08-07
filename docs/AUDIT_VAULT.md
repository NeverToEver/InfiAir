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
  - **✅ 2026-08-03 注册表收敛（O 原则达成）**：攻击集中 match 与按机型分支全部收敛为注册表/数据表驱动——`BossAttacks.execute()` 10 分支 match → **攻击处理器注册表**（attack id → Callable，`_init` 装配，`execute` 查表委托 + 未知 id 回退警告）；`BossMovement.update` 机型 match → **移动器注册表**（`_movers`：1/2/3 型策略方法）；`EnrageSequence` ACTIVE/RELEASE_HOLD/释放起手 3 处机型 match → **三张处理器注册表**（`_active_handlers`/`_release_handlers`/`_release_begin_handlers`），TRANSITION 悬停特判 → `TRANSITION_HOVER_TYPES` 机型参数表；`Boss` 召唤特判 → `SUMMONER_TYPES`、受击闪白特判 → `HIT_FLASH_BY_TYPE`。**新增机型/攻击只需注册一行 + 一个策略方法，不再改既有分发函数**（原 7 处机型分支与 10 分支攻击 match 全部消除）。
  - **如何验证（2026-08-03）**：新增 `boss_registry_test` 29 断言（攻击注册表覆盖 10 已知 id、模式表（脚本默认 + balance.json 运行表）交叉引用全注册、移动/狂暴注册表覆盖 3 机型、机型参数表正确）；boss_phase/boss_pattern/boss_enrage/enemy_combat 0 FAIL；全量 37 断言场景 0 FAIL（265s）；import 0 error / gdformat+gdlint 全绿。

---

### A4. 集中 match 分发违反开闭原则 —— 新增类型必须改既有代码

- **位置**：`enemy.gd:340`（8 分支策略 match）、`boss.gd:695`（攻击 match）、`boss.gd:1137`（按类型 3 路嵌套）、`player.gd:324`（Buff 内联分支）、`spawner.gd:201`（两事件触发内联）
- **描述**：新增移动策略/攻击/Buff/事件 = 修改既有函数的 match 或 if 分支。改既有代码就有回归风险。
- **修复指引**：按 A3 模式，将策略/攻击/事件抽为可注册的独立对象；Buff 效果改为声明式效果表（buff id → 效果对象），Player 遍历效果对象而非逐 Buff if。
- **修复起效记录**：⚠️ **部分完成（2026-08-01 按 git 历史回填，状态表当时漏更新）**
  - **已完成子项（2026-07-31 落地）**：
    - A4a 敌机移动策略抽类（`cea806e`）：`EnemyMoveStrategy` 基类 + 8 策略子类，`enemy.gd` `_physics_process` 的策略 match 委托给 `_strategy.update()`；`_make_strategy()` 仅余工厂 match（构造分发，可接受）。
    - A4b spawner 事件触发基类（`955f8a5`）：`ScheduledEventTrigger` 统一精英/编队触发策略，原 spawner 两事件内联分支委托。
  - **未完成子项**：Boss 攻击 match（现 `BossAttacks.execute()` 仍为 10 分支 match，见 A3 订正）与按机型分支（BossMovement/EnrageSequence/Boss 共残留 7 处）；`player.gd` Buff 效果仍为函数式内联分支（`_refresh_buff_factors` + `pow(因子, GameState.buff_count())` 族），未改声明式效果表。**✅ 2026-08-03 全部收敛**：Boss 攻击/机型分支随 A3 注册表收敛（10 分支 match 与 7 处机型分支消除，见 :108）；Player buff 声明式效果表 `BUFF_EFFECTS` 落地（见 :123）。
  - **✅ Player buff 声明式效果表（2026-08-03）**：`player.gd` 新增 `BUFF_EFFECTS` 声明式效果表（buff id → 效果定义：kind=pow/cap/bool + cfg 数值键 + 回退默认值），`_refresh_buff_factors` 遍历表批量缓存（原 7 个分散因子变量与 7 行 cfg 分支删除）；求值统一走 `_buff_scale`（pow 乘算）/`_buff_cap`（堆叠截断）/`_buff_enabled`（布尔启用），`_fire` 的 spread/pierce/explosive 同步表化。**新增数值型 buff 只需表加一行 + 使用处一行求值调用**，不再改 `_refresh_buff_factors` 或既有公式；数值来源保持 balance.json 语义（cfg 路径 + 脚本回退默认值，AGENTS 约定不变）。
  - **如何验证（2026-08-03）**：新增 `buff_effects_test` 38 断言（表键集覆盖 8 项 player 侧 buff、pow/cap 的 cfg 键存在于 balance.json、pow/cap/bool 三类求值与重构前公式逐点一致、穿透/溅射/齐射行为断言）；buff33/buff_visuals/smoke 等 0 FAIL；全量 37 断言场景 0 FAIL。**范围说明**：`ARMOR_MULT`/`EVASION_CHANCE`/`REGEN_PER_SEC`（`_load_balance` 单点 cfg 读取、PlayerDamage 组件消费）非逐 buff 分支，保持原位。

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
  - **未收敛**：残余依赖点（`hud`/`pause_ui` 等对 Main 的引用）仍经注册表/组间接获取，未全量改显式注入（详见 `DESIGN_BASELINE.md` §7.1）。**✅ 2026-08-07 收敛（S04）**：mothership 8 处 hud 组查找统一 `_hud()` 延迟缓存；welcome/pause_ui/事件类低频组查找按 R12 先例判定为合理模式保留（行为零变化）。

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
  - **未完成**：视觉职责（尾焰/残影/准星/碰撞点/`PlayerBuffVisuals`）仍驻留 Player 本体；Player 仍约 697 行。**✅ 2026-08-03 拆分落地（见 :1061）**：`PlayerVisuals` 抽出（RefCounted 组合），~120 行移出。

---

## 修复状态总览

| 编号 | 严重度 | 状态 | 登记时间 |
| --- | --- | --- | --- |
| A1 封装穿透 | 危险 | ✅ 已修复 | 2026-07-31 |
| A2 上帝对象 | 危险 | ✅ 已修复 | 2026-07-31 |
| A3 boss 单类 | 严重 | ✅ 已修复（2026-08-03 注册表收敛，O 原则达成） | 2026-07-31 |
| A4 开闭违反 | 严重 | ✅ 已修复（2026-08-03：Boss 分支随 A3 收敛；Player buff 声明式效果表） | 2026-07-31 |
| A5 依赖倒置 | 严重 | ✅ 已修复（依赖注入 `bdb0274`；GameState 配置中心有意保留；2026-08-07 残余收敛：mothership 8 处 hud 组查找 → `_hud()` 延迟缓存，S04） | 2026-07-31 |
| A6 L 违反 | 中等 | ✅ 已修复（is_boss 语义化特判，2026-08-01 回填） | 2026-07-31 |
| A7 测试耦合 | 中等 | ✅ 已修复 | 2026-07-31 |
| A8 Player 膨胀 | 中等 | ✅ 已修复（PlayerDamage/PlayerDash 2026-07-31；PlayerVisuals 拆分 2026-08-03，见 :1061） | 2026-07-31 |

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
| C34 | ✅ 已收口（2026-08-07） | boss_pattern_test 场景 1/2/4 的弹速/伤害硬编码（700/21/150/12/220）改读 boss 实例常量（CANNON_BULLET_SPEED/CANNON_DAMAGE/SWEEP_DROP_SPEED/SWEEP_DROP_DAMAGE/WALL_BULLET_SPEED），改 JSON 不漂移；场景 4/5 的 420（enemy.ENEMY_BULLET_SPEED 与 VOLLEY 同值）补来源注释。difficulty/buff33/elite/formation 硬编码判定为逻辑验证锚点保留（改读会降低测试独立价值）——按既定口径完成，仅有意锚点保留。验证：boss_pattern_test 0 FAIL |
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


---

# 第八轮审核（2026-08-03 K 系列全面代码审计与修复）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 全仓库游戏逻辑全面审计（AgentSwarm 8 路并行 + 主控交叉核对 + 分批修复） |
| 工作时间 | 2026-08-03 |
| 审核区域 | `scripts/` 62 文件 + `autoload/game_state.gd` + `scenes/*.tscn` + `test/` 44 场景 + `assets/shaders/meta_health.gdshader` + 相关 docs |
| 审核方法 | 8 路 explore 子代理并行只读审计（对局编排与 HUD / 玩家辅助瞄准输入 / 刷怪池与敌人体系 / Boss 与母舰 / 事件演出过场 / 系统服务 UI 基础设施 / 数值三方一致性 / 测试契约），每路对照对应设计文档；主控对全部 P1/P2 与代表性 P3 逐条读码复核（K1 与 K4 两路独立发现同一 P1 互相印证）；判定分类后分批修复，每批跑针对性测试 |
| 结论 | P1×2（其中一条为两路独立发现的同一根因）+ P2×5 + P3×17；登记不修 5 项。无 P0 |
| 审核人 | Kimi Code CLI（依据用户指示执行） |

## K 系列发现与处置（全量修复，分批提交）

| 编号 | 严重度 | 位置 | 类别 | 描述 | 处置 |
| --- | --- | --- | --- | --- | --- |
| K01 | P1 | `main.gd` `_start_homecoming` / `_summon_mothership` / `_on_orbital_struck`；`mothership.gd` `_exit_tree`；`player.gd` `exit_pod` | 纯 bug（exploit） | 召唤/对接/驻留期长按 B 返航：`_summon_mothership` 设 `set_invincible(999.0)`，`_start_homecoming` 直接 `queue_free` 母舰，`_exit_tree` 仅 `exit_pod()`（恢复显示不清无敌）→ 玩家带 ~960s 无敌进入返航过场（树暂停冻结）与基地，「继续出击」后无敌继续倒计时约 16 分钟；正常 RELEASE 路径有 `set_invincible(2.0)` 覆盖，提前收回路径无。homecoming 检测（main.gd:341）不检查输入锁定，DOCKING/STAY 期对局不暂停故必然可触发；可反复 exploit | ✅ 修复（K 批次 1）：`_start_homecoming` 在 `_player.lock_input()` 后统一 `set_invincible(0.0)`（覆盖召唤后任意母舰状态），继续出击后的保护由入场序列接管（与正常返航基线一致） |
| K02 | P1 | `laser_weapon.gd:88,129` × `player.gd:795-796,854` | 纯 bug（跨组件竞态） | 入场动画期（不锁输入）激光冷却就绪即触发：`_start_beam` 的 `_saved_autofire` 捕获入场序列置的 **false**，`_finish_entry` 恢复的 true 被 3s 后 `_end_beam` 恢复的 false 覆盖 → **自动开火永久关闭**（纯自动射击游戏=失去输出，仅剩每 8s 的 3s 激光）；持有 laser_beam buff + 返航继续出击时必然触发；`entry_animation_test` 只测无 buff 场景未覆盖 | ✅ 修复（K 批次 1）：触发条件加 `not _player.is_entry_playing()` 守卫 + `_start_beam` 内部双保险早退（防直调路径） |
| K03 | P2 | `player.gd` `enter_pod`/`exit_pod`/`_die` | 纯 bug | 进舱只关 Hitbox monitoring，GrazeArea（擦弹环）仍开：驻留期敌弹飞过停驻点仍计擦弹分 + 特效 + 音效（玩家隐藏凭空得分，违背「擦弹=主动技巧」纯得分制）；`_die` 后 physics_process 停、弹反盾 monitoring 保持冻结残留 | ✅ 修复（K 批次 1）：`enter_pod` 同步关 `$GrazeArea` + `_parry_shield` monitoring，`exit_pod` 恢复 GrazeArea（盾由相位同步管理不强制恢复）；`_die` 关盾 |
| K04 | P2 | `player_dash.gd:47` | 设计目标未达 | 手柄玩家无方向冲刺回退取**真实鼠标位置**（P0-1 手柄瞄准语义是右摇杆虚拟准星），纯手柄玩家鼠标停在任意处，冲刺方向与机头/瞄准无关、基本随机 | ✅ 修复（K 批次 1）：回退改用 `player.aim_point()`（键鼠+摇杆统一平滑点，键鼠玩家语义不变） |
| K05 | P3 | `meta_health_fx.gd:187`（crack_grow_time）/ `main.gd:77`（ENRAGE_RAMP_TIME） | 边界缺陷 | 两键无 `maxf` 下限（H15 同族遗漏，同文件 193-195 均有防护）：损坏 JSON =0 时 `_grow_boost` 衰减除零（0/0=NaN 污染裂纹进度）、`_time_scale_ramp` 除零（狂暴恢复瞬间完成） | ✅ 修复（K 批次 1）：`maxf(..., 0.001)` / `maxf(..., 0.01)` |
| K06 | P2 | `game_state.gd` `set_joy_aim_speed`/`set_joy_deadzone`/`_apply_joy_settings`；`settings_ui.gd:361-365` | 纯 bug（性能） | 手柄滑杆拖动每步 value_changed → setter 无条件全量原子写盘（tmp 写+删旧+rename）+ aim_speed 变更时重设全部 17 个 action 死区（纯浪费）+ 广播；一次拖动几十至数百次磁盘写（HDD 可感知卡顿）；对比其它设置项均为点击提交 | ✅ 修复（K 批次 1）：setter 只更新内存 + 广播（deadzone setter 保留 InputMap 应用，`base_system_test` 契约）；新增 `persist_joy_settings()`，settings 滑杆 `drag_ended` 提交一次写盘 |
| K07 | P3 | `settings_ui.gd:184-206`；`back_navigator.gd:62-63` | 纯 bug | 改键捕获态下手柄 B（ui_cancel）无法取消捕获：BackNavigator 对捕获态 `CAPTURE_PASSTHROUGH` 放行不消费，SettingsUI `_unhandled_input` 只处理 `InputEventKey`，Joypad 事件无人消费——EXIT_FLOW「B=返回/取消」惯例唯一失灵的界面态 | ✅ 修复（K 批次 2）：捕获态先判 `event.is_action_pressed(&"ui_cancel")` 取消并 handled |
| K08 | P2 | `formation_bomb.gd:102` | 纯 bug（潜在） | `(hitbox.get_parent() as Player)` 硬强转——A1 已把 `bullet.gd` 同类改为 `GameState.player_ref` 注册表引用，本文件遗漏；Player 节点结构变动即 null 调用 SCRIPT ERROR | ✅ 修复（K 批次 2）：改 `GameState.player_ref as Player` 判空后调用（距离判定不变） |
| K09 | P3 | `turret_battery.gd` `rise`/`activate`/`cease_fire_and_retract` | 设计目标未达（机制归因错误） | 升起/收回期 `monitoring=false` **不阻止玩家弹命中**——Area2D 语义：A 检测 B 取决于 A.monitoring + A.mask∩B.layer + B.monitorable，B 自身 monitoring 与子弹侧检测无关；真实防护是 `take_damage` 的 `_rising/_ceased` 守卫，弹丸命中被守卫吃掉后正常销毁（被白吃，DPS 静默消耗）；`ELITE_TURRET_EVENT.md:169` 注释同步失实 | ✅ 修复（K 批次 2）：rise/cease 改 `monitorable = false`（activate 恢复 true），monitoring 保留；同步文档表述 |
| K10 | P3 | `bullet.gd:181` `_exit_tree` | 纯 bug（潜在） | 缺 `is_instance_valid(_pool)`（`enemy.gd:387` 已防护且注释承认该时序真实存在）：池节点先于活跃子弹释放（场景卸载时序）时悬空调用 `forget` | ✅ 修复（K 批次 2）：与 enemy 对称补判空 |
| K11 | P3 | `turret_battery.gd:201` | 风格未跟进 | 炮台 laser 弹每发射一次 `get_node("Polygon2D")` 字符串查找——C24 缓存模式（`b.polygon_node()`）后新增调用方漏改 | ✅ 修复（K 批次 2）：改 `b.polygon_node()` |
| K12 | P3 | `boss.gd:279` `setup` | 纯 bug（潜在） | `TEXTURES[p_type-1]` / `hp_mults[p_type-1]` 按公开接口入参 p_type 索引无越界钳制（H11 只校验了数组长度）；外部/测试传 >3 或 ≤0 越界崩溃 | ✅ 修复（K 批次 2）：入口 `clampi(p_type, 1, 3)` |
| K13 | P3 | `boss_movement.gd:35-58,79` | 边界缺陷/死代码 | `match int(boss.boss_type)` 无 `_` 分支（enrage_sequence 同型 match 有回退对比），非法值 Boss 完全静止；`_move_bob` 的 `y_center` 参数三处调用均未传（死参数，注释声称的偏移能力不存在） | ✅ 修复（K 批次 2）：补 `_` 分支回退一型走位；删死参数并修正注释 |
| K14 | P3 | `elite_turret_event.gd:105,113` | 边界缺陷 | `TURRET_COUNTS`/`AMMO_SEQUENCES` 无判型回退（H13/G06 口径只覆盖了 fire_interval 等标量）：非 Dictionary 时后续 `.get()` 在 Variant 上运行时崩溃，或空字典致事件空转 30s 无结算 | ✅ 修复（K 批次 2）：`is Dictionary` 判型回退默认 |
| K15 | P3 | `formation_strike_event.gd:80`；`main.gd` `_ready` | 设计目标未达 | A5 依赖注入（2026-07-31）只覆盖 Boss 与精英炮塔事件，编队事件仍 `get_first_node_in_group("spawner")` 现找——事件先于 spawner 入树时 `_spawner=null`，互斥检查与波次暂停钩子静默失效 | ✅ 修复（K 批次 2）：main 注入 `_formation.set_spawner(_spawner)`（新增 setter，仿 elite），`_ready` 保留兜底 |
| K16 | P3 | 测试侧：`mouse_lock_test.gd` / `tutorial_test.gd` / `difficulty_test.gd` | 纯 bug/痕迹/注释失实 | ① `MOUSE_TRAP._warp_target`/`_trap_enabled` 白盒直调 `_` 私有（A7 全清后新出现，且 6 个裸 bool 位置参数签名扩展即静默错位）；② `tutorial_test.gd:75` 残留 `[dbg]` 调试输出；③ tutorial 通关写 `tutorial_done=true` 入 profile 后不恢复（违反 TESTING.md「清理自身持久化」约定）；④ `difficulty_test.gd` 注释 HP/速度区间与 spawner 静态表不符 | ✅ 修复（K 批次 2）：`mouse_trap` 公开化两个 static 纯函数（`trap_enabled`/`warp_target`）+ 测试改公开调用；删 dbg；断言后恢复 `tutorial_done=false` 并写盘；注释修正为实际区间（easy 143-158 / medium 190-210 / hard 285-315） |
| K17 | P3 | `meta_health_fx.gd:177` + `meta_health.gdshader:89` | 文档-代码矛盾（死配置） | `crack_glow` 键读入后零使用（全项目无引用），用户调 `effects.meta_health.crack.glow` 无效；shader 裂纹 ADD 泛光强度为字面 0.8；balance.json 与 META_HUD_DESIGN §4.4 均登记该键 | ✅ 修复（K 批次 3）：接线——shader 增 `u_crack_glow` uniform（默认 0.8 与现状逐位一致），GDScript `set_shader_parameter` 传 cfg 值 |
| K18 | P3 | `TESTING.md:117` / `DESIGN_BASELINE.md:299,368` / `BALANCE_MAP.md` / `return_cinematic.gd:433,548,679,816` / `mothership.gd:604,639` / `ui_segmented_bar.gd:34-36` | 文档-代码矛盾 | ① 「31 断言场景」落后于实际 35（公平感 4 场景）；② BALANCE_MAP 行号漂移（J 系列提交改行后未重跑生成器，重跑 diff 189 行）；③ return_cinematic 镜头 1-4 注释时长仍为压缩前旧值（2.4/1.6/2.0/3.0 vs 实际 1.6/1.2/1.4/2.2）；④ mothership 加特林/导弹注释自称「仅驻留」但 DOCKING 火力掩护是有意设计；⑤ ui_segmented_bar 注释声称绘制时逐元素 float()/as Color 转换，代码未实现（当前唯一调用方恒传 const Color 数组） | ✅ 修复（K 批次 3）：口径统一（31→35；重跑生成器提交新 BALANCE_MAP；注释修正；seg_bar 注释如实描述防御不对称） |
| K19 | P3 | `scenes/player.tscn:7-14` | 约定违反 | 3 个 CircleShape2D 运行时被写半径但缺 `resource_local_to_scene = true`（AGENTS 明文约定；enemy.tscn 已修同型，当前 Player 单实例幂等赋值未暴露） | ✅ 修复（K 批次 3）：tscn 三处补字段 |

## 登记不修（论证后收敛）

1. **普通阶段召唤间隔不参与难度分档**（`boss.gd:534` vs `:587` 狂暴 E3 乘 `interval_mult`）：BOSS_REDESIGN §4.4 分档口径「开火间隔 ×1.15/×1/×0.85」未明确召唤间隔归属，改动属平衡性游戏决策 → 登记待产品判断，不擅自改。
2. **`boss_attacks` 编队齐射池引用复用**（`_volley_minions` 存池化小怪引用，0.8s 延迟内击杀 repool 后旧引用可能指向新激活对象误伤）：`minion_volley_fire` 为 P2 编队与狂暴倾巢**共用路径**，加 `hive_volley` meta 复查会破坏狂暴路径；触发概率极低（0.8s 窗口 + 池恰好复用）且后果轻微（向玩家多射一枚普通弹）→ 登记不修，修复需先梳理双路径身份标记。
3. **精英炮台与航母 HOVER 浮动 ±6px 周期错位**（`elite_turret_event.gd:189` 一次性锚定 vs `strike_carrier.gd:123-125` 正弦浮动）：纯视觉瑕疵，可接受。
4. **10 个 buff 无 `max_stacks` 键**（仅 6/16 在 balance.json 有）：叠加上限锁死在 `buff_select.gd` BUFF_POOL 池内值（`cfg("buffs.%s.max_stacks", b["max"])` 缺省用池内值，`:59` 注释已声明有意兜底），balance_editor 不可调 → 登记有意设计，如未来需要全部 buff 可调再补键。
5. **输入锁定期弹反盾保持 ACTIVE**（锁定瞬间恰在 ACTIVE 时，锁定全程盾持续弹反敌弹）：行为无害（弹反转玩家弹），与「输入锁定≠暂停」语义一致（计划书 §5.3）；死亡/进舱残留路径已由 K03 收束。

## 修复起效记录（回填）

- **改了什么**：18 个源码/场景/测试文件 + 1 shader + 4 文档（见上表逐条）。
- **为什么起效**：K01 把「召唤残留 999s 无敌」与「返航基线（无敌=0）」在统一复位点对齐，RELEASE 2s 保护与入场序列保护均不受影响；K02 双守卫消除入场期激光触发窗口（`_saved_autofire` 不再能捕获入场禁火态）；K03-K05/K07-K15 均为「同类防护/机制已存在、此处遗漏」补齐或语义收束，行为零变化；K06 把磁盘写从「每步」降为「每次拖动结束一次」且消除无谓死区循环；K17 配置键从死键变为生效（默认值与现状逐位一致）。
- **如何验证**：`--headless --import` 0 error（警告门禁干净）；`gdformat --check` + `gdlint`（autoload/ + scripts/，CI 口径）全绿；20 个针对性场景（smoke/base_system/mouse_lock/tutorial/entry_animation/mothership_summon/elite_turret_event/formation_strike_event/boss_pattern/boss_enrage/boss_phase/meta_health_fx/graze/parry/hit_logic/enemy_combat/pool_reuse/difficulty/grace_period/boss_phase_transition）全 PASS；全量 35 断言场景 0 FAIL（回归结果见当次提交记录）；`--quit-after 300` 0 错误；BALANCE_MAP 双向反查 0 缺失键。

---

# 第九轮审核（2026-08-03 全库薄弱点与业务冗杂点审计重构）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 全库程序薄弱点 + 业务逻辑冗杂点审计重构（AgentSwarm 9 路并行 + 主控逐条复核 P1/P2 + 分批修复即时验证） |
| 工作时间 | 2026-08-03 |
| 审核区域 | `scripts/` 63 文件 + `autoload/game_state.gd` + `scenes/return_cinematic.tscn` + `data/balance.json`/`translations.csv` + 相关 docs |
| 审核方法 | 9 路 explore 子代理并行只读审计（对局编排核心 / 玩家与辅助瞄准 / 刷怪与对象池 / Boss 体系 / 母舰与事件系统 / 过场与编队演出 / HUD 与战斗 UI / 页面与导航 / 数值与配置三方一致性），每路对照 DESIGN_BASELINE 与专项设计文档；主控对全部 P1/P2 逐条读码复核后才修复；每批修复跑针对性测试 |
| 结论 | P1×3 + P2×7 + P3×33（修复 29 项）；登记不修 5 项。无 P0 |
| 审核人 | Kimi Code CLI（依据用户指示执行） |

## E 系列发现与处置

| 编号 | 严重度 | 位置 | 类别 | 描述 | 处置 |
| --- | --- | --- | --- | --- | --- |
| E01 | P1 | `player.gd` 机身色调 if/elif 状态机（694-704 区） | 纯 bug | 弹反/擦弹分支整体赋值 `_sprite.modulate` 后 RGB 基准残留不恢复（弹反后提亮 `(1.35,1.4,1.55)` 永久丢失、擦弹后机身永久金色）；且 `_invincible -= delta` 位于 elif 内，弹反（0.8s）/擦弹（0.12s）/冲刺（0.25s return 早退）期间无敌倒计时冻结，受击无敌被视觉分支实际延长，Boss 转场注入的无敌读取失真 | ✅ 修复：提取 `BODY_TINT_BASE` 常量，弹反/擦弹改 `base.lerp(金色, t)` 基底；`_invincible` 递减移至视觉分支之前无条件执行；无敌/常态分支整体重置基准色再调 alpha |
| E02 | P1 | `boss_attacks.gd` `_start_dash_sweep`/`_update_sweep` | 设计目标未达 | 二型 P2「冲刺掠过」实际未横穿玩家高度：预警线画在玩家高度（`dy` 偏移），DASH 阶段只动 x、y 恒为 AIM 开始时 Boss 锚线（≈230），玩家常驻 400-900 区间 → 机身撞击与 3 枚拖弹全部落空，预警成虚假引导；`boss_pattern_test` 只断言 x 位移未覆盖 y | ✅ 修复：`_sweep_dash_y = boss.position.y + dy`（AIM 开始时玩家高度快照），AIM→DASH 转换时落位该 y；RETURN 复用锚线回位逻辑 |
| E03 | P1 | `buff_select.gd:254-262` | 纯 bug（手柄软锁） | 卡片确认分支被 `event is InputEventKey` 类型守卫锁死：Godot 把 ui_accept 以原始事件类型路由给焦点控件，手柄 A（InputEventJoypadButton）被排除；卡片是自定义 Control（非 Button），无其他输入路径 → 手柄玩家每次里程碑三选一被软锁，且 BackNavigator 对 buff_ui 可见态返回 IGNORE 无绕行 | ✅ 修复：去掉类型守卫，改 `event.is_action(&"ui_accept") and event.pressed and not (event is InputEventKey and event.echo)`（键盘+手柄通吃，echo 守卫保留） |
| E04 | P2 | `game_state.gd` `_apply_balance` milestones.base 元素 | 设计目标未达（健壮性） | `int(v)` 对字符串元素返回 0（阈值全 0 → 每次加分触发里程碑风暴）、对 Array/Dict 抛运行时错误（启动即崩）；C03 只防空数组，与「损坏回退默认」宣称矛盾 | ✅ 修复：元素级 `is int or is float` 判型 + `maxi(int(v), 1)`，非法元素跳过 |
| E05 | P2 | `game_state.gd` `_process` | 性能（热路径违规） | 每帧 `int(run_time)` + `_set_mission_progress` 内 `missions.has/索引` 字典访问（P0-3 只优化了整秒跳过写，字典读仍每帧） | ✅ 修复：`_survive_sec_cached` 整秒缓存，秒值变化才调 `_set_mission_progress` |
| E06 | P2 | `aim_frame_layer.gd:78-89` `frame_half_size` | 设计目标未达（缩放杠杆） | meta `aim_frame_radius` 已含 ws（enemy.setup 写入），再乘 `e.scale.x`（同样含 ws）= ws 平方；当前 ws=0.4 恰被 `maxf(scale.x, 0.5)` 钳制掩盖，ws 上调时框尺寸非线性暴涨，入框判定面积口径不符注释 | ✅ 修复：删 `* maxf(e.scale.x, 0.5)`，直接取 meta 半径 + frame_pad |
| E07 | P2 | `orbital_strike.gd:64-67` | 纯 bug（软锁） | `p >= 1.0` 分支先于 `struck.emit()`：单帧大 delta（窗口失焦恢复/低端机卡顿）可越过 IMPACT_AT 直达完成，`finished` 先发而 `struck` 不发——main 恢复对局（paused=false + unlock_input）的唯一入口缺失 → 树永久暂停 + 输入锁软锁，无 UI 可恢复 | ✅ 修复：`p >= 1.0` 分支补发 struck 兜底（`_impacted` 幂等，struck 消费方幂等） |
| E08 | P2 | `formation_strike_event.gd:204` BOMBING_RUN 离场判定 | 设计目标未达 | 出界（±120px 余量）与投弹完成是 or 关系：hard 5 机投弹表最长 3.6s（x 位移需 ~1224px），向右分支最坏窗口仅 ~1035px → 约 1/3 概率末机炸弹（3.2s/3.6s 时刻）被截断，最坏第 5 机 0 投弹；`formation_strike_event_test` 只用 4 机（2.8s）未暴露 | ✅ 修复：出界余量按投弹表末弹时刻 × RUN_SPEED 动态折算（`_drop_times.back() * RUN_SPEED`） |
| E09 | P2 | `back_navigator.gd:37-39` + `settings_ui.gd` 捕获态 | 文档-代码矛盾 | 右键分支无条件 `set_input_as_handled()`：改键捕获态（CAPTURE_PASSTHROUGH）被吞，SettingsUI 只处理 ui_cancel/InputEventKey 不处理鼠标 → 捕获态右键无反应，与 EXIT_FLOW「右键=返回/取消（与 Esc 同路由）」矛盾 | ✅ 修复：右键分支 CAPTURE_PASSTHROUGH 时不消费（改走 `_mark_handled` null 防御）；settings_ui 捕获态补右键取消（与 Esc 同路径） |
| E10 | P2 | `docs/BALANCE_MAP.md` | 文档-代码矛盾 | 310e0b9（A3/A4）改 boss.gd/player.gd 后未重跑生成器：boss.gd 全部 cfg 行号 +5 漂移、player.gd +9~+23 漂移、7 条 STALE 行指向已删除代码 | ✅ 修复：重跑 `gen_balance_map.py`（425 静态调用，0 缺失键） |
| E11 | P3 | `game_state.gd:148` `_max_hp_bonus` | 健壮性 | 负值使 extra_life 叠层降血上限，与 `_max_hp_base` 钳制不对称 | ✅ 修复：`maxf(..., 0.0)` |
| E12 | P3 | `game_state.gd` `_valid_difficulty_defs` | 健壮性 | hp/speed/spawn/score/spread_cap 负值致敌机 0 HP 秒死/反向移动/负得分倍率，仅 milestone 有域校验 | ✅ 修复：五个键补 ≥0 校验，任一负值整表回退默认 |
| E13 | P3 | `game_state.gd:14-16` rp_changed/mission_completed/route_chosen | 冗杂（死信号） | 生产代码零 connect（A7 零 connect 信号清理项残留），base_console 拉取驱动 | ✅ 修复：声明处注释「暂无消费方，轮询驱动」，保留 API |
| E14 | P3 | `game_state.gd` `try_lifesteal` | 性能 | 击杀帧每次 `cfg("buffs.lifesteal.max_hp_fraction")` 路径解析（P0-2 regen 缓存同款遗漏） | ✅ 修复：`_apply_balance` 缓存 `_lifesteal_fraction` |
| E15 | P3 | `game_state.gd` `score_multiplier` | 性能 | 每次加分双层字典查找，难度档是低频变更项；曾尝试缓存 | ⏸ 回退（测试证伪）：difficulty 是公开字段，测试/调用方直写不触发 `_refresh_regen_cache`（白盒契约），缓存返回旧值致 difficulty_test 分数倍率断言失败；与同族 enemy_hp_multiplier 等 4 函数一致保持直接查表（事件路径非每帧，收益低） |
| E16 | P3 | `game_state.gd` `_detect_joy_layout` | 边界缺陷 | 拔掉 PS 手柄后 `joy_layout` 保持 `&"ps"` 不回落，设置页按钮标签残留误导 | ✅ 修复：无手柄时回落 xbox + 广播 |
| E17 | P3 | `game_state.gd:766` `add_boss_kill` | 冗杂 | 门面内部用 `GameState.cfg` 自引用而非 `_balance_service.cfg`（A2 委托不彻底） | ✅ 修复：改类内 `cfg()` |
| E18 | P3 | `main.gd:75-76` ENRAGE_SLOW_SCALE/ENRAGE_BULLET_TIME | 健壮性 | 同块其它 5 个 cfg 读取均有 H15 钳制，唯这两行裸读（0 值全冻结/跳过演出） | ✅ 修复：`maxf(..., 0.01)` |
| E19 | P3 | `bullet.gd` `_on_area_entered` | 边界缺陷 | pierce=0 弹同物理帧重叠双敌：首命中已回收（deactivate/queue_free）但 monitoring 关闭延迟到帧末，第二目标仍被结算「无接触受击」 | ✅ 修复：入口加 `not _active and (_pool != null or is_queued_for_deletion())` 守卫（池化回收与直实例化 queue_free 双路径） |
| E20 | P3 | `bullet.gd` `_cancel_grace` | 生命周期 | 回收弹 `_grace_hitbox` 引用驻留（只停 timer 不清引用） | ✅ 修复：`_cancel_grace` 内置 null |
| E21 | P3 | `spawner.gd` `_merge_type` 标量字段 | 健壮性 | score/fire/fire_interval/scale/radius 无判型透传，坏值在击杀结算 `int()` 报类型错误 | ✅ 修复：标量 `is int/float`（排除 bool）判型，坏值跳过 |
| E22 | P3 | `enemy.gd:409` slow_field | 性能 | 每敌每物理帧 `GameState.buff_count(&"slow_field")` 字典查询（20-30 敌 × 60fps） | ✅ 修复：`buffs_changed` 信号缓存 `_slow_field_on` 布尔（`_ready` 连接、`_exit_tree` 断开，C22 模式） |
| E23 | P3 | `pause_ui.gd:65` 保存态 | 纯 bug | 跨语言文本比较判保存态：保存后切语言，旧语言「已保存」与新语言 `tr("PAUSE_SAVED")` 不等 → 误判未保存覆盖为 PAUSE_SAVE | ✅ 修复：`_saved` 布尔标志驱动文案 |
| E24 | P3 | `elite_turret_event.gd` `_on_event_timeout` | 生命周期 | 超时收回的炮塔引用驻留 `_turrets` 最长 ~6s（BOSS_DELAY 窗口），期间数组语义失真 | ✅ 修复：timeout 内 retract 后立即 clear 两数组（`_on_boss_delay_end` clear 幂等） |
| E25 | P3 | `tutorial.gd` 阶段 2 补刷轮询 | 性能 | 每物理帧 `get_children()` 全子节点遍历（分配数组）；曾尝试 0.25s 节流优化 | ⏸ 回退（测试证伪）：tutorial_test 依赖 queue_free 释放与检查窗口的即时性，节流窗口与释放帧交错会跳过补刷（连跑两次一次 105 行 FAIL 一次 PASS 证实竞态）；教程阶段 2 节点少开销可忽略，恢复每帧检查并注释说明契约依赖 |
| E26 | P3 | `ui_segmented_bar.gd:100` | 性能 | 段循环内每段重复 `_weights_total()` 全量累加 | ✅ 修复：循环外缓存 |
| E27 | P3 | `player.gd` 尾焰 | 冗杂 | 冲刺/加速/巡航/静止/入场五处重复三行参数 | ✅ 修复：提取 `_set_thruster()` |
| E28 | P3 | `player.gd:1011` 弹反爆点 | 文档-代码矛盾 | 爆点用玩家中心而非弹反命中处（计划书 §6「弹反点金色爆点」） | ✅ 修复：改 `area.global_position` |
| E29 | P3 | `player.gd`/`mouse_trap.gd` 信号 | 生命周期 | GrazeArea/弹反盾/Window 信号连接无 `_exit_tree` 断开（C22 模式不一致） | ✅ 修复：两文件补断开（is_connected 守卫） |
| E30 | P3 | `intro_cinematic.gd:339-342` 冲击波环 | 纯 bug（视觉） | parallel tween 把 modulate 淡出从起点开始，第二波环（interval 0.5s）扩散中段即不可见 | ✅ 修复：改顺序 tween + interval 后 scale/alpha 并行（与镜头 2 ripple 同款） |
| E31 | P3 | `boss.gd:842` `_fire_enrage_release` | 冗杂（死代码） | 零调用者，其职责与 `EnrageSequence._release_fallback` 重复实现 | ✅ 修复：删除函数 + 871 注释同步 |
| E32 | P3 | `boss_attacks.gd` homing2 | 冗杂 + 设计目标未达 | homing2 为死注册项（模式表/balance 均无此 id），`homing_delta` 只被它消费 → easy/hard 追踪弹数恒 1，§4.4「弹数 ±1」分档承诺未兑现 | ✅ 修复：删 homing2 注册与实现；`_handle_homing` 应用 `maxi(1, 1 + homing_delta)` 横向 80px 散开（medium 档恒单发行为不变）；`boss_registry_test` 断言 10→9 同步 |
| E33 | P3 | `boss.gd:542` `_summon_interval` | 设计目标未达 | 三型普通阶段召唤间隔未随难度分档（狂暴 E3 已乘 interval_mult），§8.3「各内部节奏 ×interval_mult」不完整 | ✅ 修复：分档乘算 + 同步首唤计时 |
| E34 | P3 | `return_cinematic.gd:257` `_kick_shake` + `return_cinematic.tscn` Flash 节点 | 冗杂（死代码） | 静态函数零调用、场景 Flash 节点无脚本引用（开场模板复制遗留） | ✅ 修复：两处删除 |
| E35 | P3 | `cinematic_fx.gd` `speed_lines`/`SpeedLineField` | 冗杂（死工厂） | 全仓零调用，约 60 行无人消费 | ✅ 修复：删除类与工厂，ARCHITECTURE 描述同步 |
| E36 | P3 | `formation_strike_event.gd:66` group 注册 | 冗杂（死注册） | 组零查询（K15 依赖注入后无消费方） | ✅ 修复：删除 |
| E37 | P3 | `buff_select.gd:269-273` card==null 分支 | 冗杂（死代码） | 唯一连接点恒传非空 card，直调走 pick_buff() | ✅ 修复：删除分支 |
| E38 | P3 | `settings_ui.gd` ESC 死分支 + locale 跳页 | 冗杂 | ① KEY_ESCAPE 分支不可达（ui_cancel 不可改键，先命中取消分支）；② `_on_locale_changed` 无条件跳回「控制」页 + 重建前重复刷新 | ✅ 修复：删死分支；记录当前页并恢复 + 删一次冗余刷新 |
| E39 | P3 | `game_over_ui.gd:65-70` `if visible` | 冗杂（恒假分支） | 死亡态无语言切换入口，locale_changed 不可能在 visible 时触发 | ✅ 修复：去掉包裹（刷新不可见文本无害） |
| E40 | P3 | `start_panel.gd:184-185` | 冗杂 | `_hero` 叠 animate_open + stagger_open 双 tween 重叠淡入 | ✅ 修复：删多余 animate_open |
| E41 | P3 | `mothership.gd` HOVER 死状态 + MS_HOVER 键 | 冗杂 | 枚举成员/state_text 分支/_physics_process pass 分支全无可达路径；translations.csv MS_HOVER 死键 | ✅ 修复：枚举/分支/翻译键删除 |
| E42 | P3 | `balance.json` difficulty.*.label + 脚本 DIFFICULTY_DEFS label | 冗杂（死键） | D04 已改走 tr()，全仓零消费者，调参者改 label 无效 | ✅ 修复：json 3 键 + 脚本 3 键删除 |
| E43 | P3 | 文档同步 | 文档-代码矛盾 | ARCHITECTURE.md:26「31 断言场景」落后（CI 实跑 37）、:60 生成路径旧描述、:66 cinematic_fx 描述含已删 speed_lines；DESIGN_BASELINE §1.6 A3/A4 遗留表述未关闭；ELITE_TURRET_EVENT.md:271 monitoring 单关表述滞后 K09；EXIT_FLOW 教程基地态例外未标注 | ✅ 修复：全部口径同步（31→37、池化路径、A3/A4 已收敛、monitoring/monitorable 双关、教程模态例外） |

## 登记不修（论证后收敛）

1. **`formation_bomb.gd:98` 引爆时 `cfg` 查询未缓存**：炸弹为一次性短生命对象，引爆频率极低（单事件 ≤10 弹），与 formation_craft 每帧热路径不同 → 缓存无收益，登记不修。
2. **`starfield.gd` 运行期切视角不重布星点**：当前 `VIEW_ZOOM_LEVELS` 均 ≥1（视野收窄），[0,1920]×[0,1080] 范围恒覆盖可见区，仅分布密度不均的视觉小瑕疵；重布涉及随机重生成与性能波动，风险/收益比低 → 登记不修（注释口径已澄清为启动快照）。
3. **`boss_attacks.gd:180` 狙击 3 连发内部间隔 0.12s 硬编码**：§4.4 分档承诺口径为「开火间隔」，burst 内部连发间隔属弹幕细节非承诺范围；入配置属平衡决策 → 登记待产品判断。
4. **`hud.gd` 血条变红阈值 0.3 硬编码**：与 `effects.low_hp.ratio`（0.2）是有意的两级口径（血条警示 vs 低血反馈），0.3 入配置为纯配置化改进无行为收益 → 登记待数值管理统一。
5. **编队楔形偏移方向**（`formation_strike_event.gd` 僚机偏移经 `rotated(_heading-PI/2)` 后可能落在飞行方向前方）：缺乏渲染验证且注释自洽，需窗口模式截图核对后定夺 → 登记待视觉确认。

## 修复起效记录（回填）

- **改了什么**：33 个源码/场景/数据/测试/文档文件（见上表逐条；另含 `test/boss_registry_test.gd` 注册表断言同步、`data/translations.csv` MS_HOVER 删除、`data/balance.json` label 死键删除、`docs/EXIT_FLOW.md` 教程例外说明）。
- **为什么起效**：E01 把无敌递减移出视觉分支并统一基准色基底，受击无敌时序恢复语义正确且弹反/擦弹后机身回归基准；E02 让冲刺路径与预警线同一高度快照（机身撞击/拖弹重新生效，消除虚假预警）；E03 放开类型守卫后手柄 A 经 Godot 焦点路由正常选取；E04-E06/E11/E12/E14-E18/E21/E22 均为「既有防护族存在、此处遗漏」补齐或热路径缓存化，行为零变化（默认值/回退值逐位一致）；E07/E08/E09 为确定性缺陷根因修复（软锁入口补全、投弹表余量折算、捕获态事件路由放行）；E19/E20/E24/E29 补齐生命周期守卫与引用清理；E30-E42 为死代码/死键/死注册清理与文档口径统一，均无可达路径或消费方，零回归风险。
- **如何验证**：`--headless --import` 0 error（警告门禁干净）；`gdformat --check` + `gdlint`（scripts/ + autoload/ + 相关 test，CI 口径）全绿；针对性场景 15+（smoke/pool_reuse/boss_registry/boss_pattern/buff33/buff_effects/mothership_summon/formation_strike_event/back_navigation/intro_cinematic/return_cinematic/tutorial/i18n/difficulty/orbital_strike 等）全 PASS 0 FAIL；全量断言场景回归 0 FAIL（结果见当次提交记录）；`--quit-after 300` 0 错误；BALANCE_MAP 重跑后 0 缺失键、双向反查一致。

---

# 第十轮审核（2026-08-03 L 系列：软件工程维度全面审查）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 全仓库软件工程维度审查（代码质量 / 架构 / 健壮性 / 性能 / 测试 / UI 输入 / 文档 / 配置构建工具链 / 安全持久化 / 提交规范），评价正文见 `docs/archive/2026-08-03-code-review.md` |
| 工作时间 | 2026-08-03 |
| 审核区域 | `scripts/` 62 文件 + `autoload/game_state.gd` + `test/` 46 场景 + `scenes/` + `data/` + `project.godot` + `.github/workflows/` + 启动/打包脚本 + `scripts/tools/` + 全部 docs |
| 审核方法 | 8 路 explore 并行只读审查（按子系统分组），每路对照既有登记去重；主控对全部 P1 与关键 P2 逐条读码复核（含 Godot 源码级生命周期验证与生成器复现验证）；修复后逐批跑针对性测试 + 逆验证 |
| 结论 | P1×3（全部修复并验证）+ P2×15（修复 10、登记待办 6）+ P3×40+（修复 6、登记 30+）。无 P0 |
| 审核人 | Kimi Code CLI（依据用户指示执行） |

## L 系列发现与处置

### P1（严重，全部修复）

| 编号 | 位置 | 类别 | 描述 | 处置 |
| --- | --- | --- | --- | --- |
| L01a | `test/intro_capture.gd:29`、`test/return_capture.gd:30` | 纯 bug（工具已坏） | 截图工具 `set_shot_durations().append(SHOT_LEN)` 链式调用 void 返回值——A7 重构把 setter 改 void 时未同步工具，编译错误已坏；CI 三道门禁全部无感知（静态检查只扫 autoload/+scripts/、import 不解析未引用场景、断言场景白名单不含 capture） | ✅ 修复：`var shots: Array[float]` 收集后整表一次传入 `set_shot_durations(shots)`；场景加载验证无 Compile Error |
| L01b | `test/autoplay_test.gd:38-39,858` | 纯 bug（探针漂移） | 母舰状态表 7 项 vs `Mothership.State` 实际 6 态（无 HOVER，`mothership.gd:15`）——整体错位一档：真实 STAY 被配 10s（驻留期必误报 mothership_stuck）、RELEASE 被配 70s（卡死漏报）、DEPART 被配 10s（误报），日志把 DOCKING 打印为 HOVER；下标越界无守卫 | ✅ 修复：重排 6 项 `[20000,10000,10000,70000,10000,30000]` 对齐枚举序 + `state < MS_STATE_TIMEOUTS.size()` 守卫 |
| L02 | `scripts/enemy.gd` `_exit_tree:393-397` / `reactivate` | 纯 bug（回归） | **slow_field 静默失效回归**（E22 引入）：`_ready` 每实例仅首次入树执行一次（Godot 4.5/4.6 node.cpp `_propagate_ready` 由 `ready_first` 守卫），而 `_exit_tree` 在每次 reparent（池化 release→pool、spawn→Main 均触发）断开 `buffs_changed` 连接且无重连 → `_slow_field_on`（E22 缓存字段）冻结陈旧值，首个回收循环后玩家获得 slow_field buff 时所有被复用敌机不受减速；注释自相矛盾（enemy.gd:256 vs :395）；`hit_logic_test` A13 只用全新实例未覆盖池化复用路径 | ✅ 修复：`reactivate()` 幂等重连 + 立即 `_on_buffs_changed()` 刷新；注释同步；`pool_reuse_test` 补 2 条池化复用断言（连接保持 + 加 buff 即时刷新）；**逆验证**：临时移除重连 → 2 断言 FAIL，恢复 → 14 PASS 0 FAIL |

### P2（中等：修复 9 项 / 登记待办 6 项）

| 编号 | 位置 | 类别 | 描述 | 处置 |
| --- | --- | --- | --- | --- |
| L03 | `scripts/player.gd:1048-1058` `_die` | 纯 bug（K03 补全遗漏） | 死亡路径关 Hitbox/弹反盾但漏关 GrazeArea：`_on_graze_entered` 由物理引擎驱动无 `_dead` 守卫，死亡当帧/死亡后敌弹飞过尸体位置仍计擦弹分+特效+音效（玩家隐藏凭空得分）；死亡无重生路径，关闭无副作用 | ✅ 补 `$GrazeArea.monitoring = false`（与 `enter_pod:1068` 对称） |
| L04 | `autoload/game_state.gd:181` `_valid_difficulty_defs` | 边界缺陷（bool 判型漏网） | bool 是 int 子类：`"score": false` 通过校验 → 得分倍率恒 0 → 里程碑永不触发（Buff 三选一软锁）；`"hp": false` → 敌机 0 HP 秒死。E21 已修 spawner 标量同型，此处 3 处遗漏 | ✅ 判型统一追加 `not v is bool` |
| L05 | `scripts/spawner.gd:188-189,220-223` | 边界缺陷（元素判型） | `unlock_scores` 元素无判型：Dict 元素 `int()` 启动即崩、字符串静默转 0（全部机型开局解锁）；`_merge_type` 的 hp/speed 嵌套数组同型（Dict 崩溃/字符串 0 HP）。E04/G06 只修容器层 | ✅ 元素级 `is int/float and not is bool` 判型，坏值跳过 |
| L06 | `scripts/spawner.gd:176-183` | 边界缺陷（钳制遗漏） | 波次间隔键无下限钳制：`wave_interval_start ≤ 0` 时 `_current_interval:411` 的 clampf 上界 ≤0 返回负值 → `_wave_timer` 恒 ≤0 → **每帧刷一波**（预告线/Timer 无界增长挂死） | ✅ `maxf(…, 0.05/0.01)` 钳制 wave_interval_start/end/ramp_time/interval_min |
| L07 | `scripts/boss.gd:566-571` `_load_patterns` | 边界缺陷（元素判型） | 模式表只判数组层：混入非 Dictionary 元素时 `_current_pattern()` typed 返回运行时类型错误、`pattern.has` 崩溃（战斗中途 SCRIPT ERROR） | ✅ 元素级 `is Dictionary` 判型 + 逐元素深拷贝，全坏回退默认表 |
| L08 | `scripts/base_console.gd:297` `show_base`；`scripts/settings_ui.gd:461-501` | 设计目标未达（焦点链） | ①基地控制台打开无 `grab_focus()`——全项目模态页唯一缺失（settings/pause/buff_select/exit_confirm/start_panel 均聚焦主按钮），手柄/键盘玩家进基地后方向键+Enter 无法操作；②设置页 locale 重建（queue_free 旧按钮）后焦点丢失，Tab 循环/手柄导航中断 | ✅ ①`show_base` 末尾 `_resume_button.grab_focus()`（按钮提升为成员）；②重建后 `(_nav_buttons[current] as Button).grab_focus()`；顺带修 L30 resume_button locale 刷新 |
| L09 | `scripts/tools/gen_balance_map.py:22-34` | 设计目标未达（生成器盲区） | 声明式效果表 `"cfg": "buffs.rapid_fire.factor"` 字符串键（player.gd BUFF_EFFECTS）不经 GameState.cfg 调用——7 键不参与缺失键检测（拼错/改名不报），`player.dash.cooldown_stack_factor` 被误列疑似死键（BALANCE_MAP.md:589） | ✅ 新增 `RE_EFFECT_CFG` 正则（`"cfg"\s*:\s*"([^"]+)"`）纳入扫描；重跑后 432 静态调用、0 缺失键、未引用仅剩 `version` |
| L10 | `docs/BALANCE_MAP.md` | 文档-代码矛盾 | 行号漂移 6 处（93837ba 同批改脚本未重跑生成器：tutorial.gd×4、game_state.gd×2，实测漂移 +1/+2 行） | ✅ 重跑生成器（含 L09 改动），双向反查一致 |
| L11 | `README.md`×2、`README.en.md`×2、`CONTRIBUTING.md`×2、`CHANGELOG.md`、`docs/TESTING.md:117`、`ci.yml:3` 注释 | 文档-代码矛盾 | 「31/35/37」断言场景计数残留 8 处（实际 37） | ✅ 全部统一为 37 |
| L12 | `test/perf_bench.gd:2,24` | 注释失实 | 基准注释「30 只敌机」落后于常量 200（压力场景口径失实） | ✅ 注释同步为 200 |
| L13 | `spawner.gd:394,401` × `mothership.gd:586-597` | 机制交叉（设计判断） | 母舰驻留/对接期精英炮塔与编队事件仍可触发（`can_trigger` 只查 IDLE+冷却不查母舰在场），母舰自动火力（玩家弹阵营）可摧毁事件单位并全额发奖——玩家进保护舱零参与挂机收益 | ✅ 已修复（cc422d1 Phase 0）：事件 `can_trigger` 加 `get_first_node_in_group("mothership") != null` 母舰在场检查（elite_turret_event.gd:135-138 / formation_strike_event.gd:129-131） |
| L14 | `boss.gd:780` × `boss_movement.gd:112-130` | 纯 bug（视觉，行为修改） | 段切换瞬间 y 垂直跳变：三型 P1 `_move_band` 增量式偏移（target 可达 depth+wob≈280px，band_offset 未在 reset_press 清理）→ P2 `_move_bob` 绝对赋值锚线 → 1/4 屏瞬移；一型 P1 press 窗口内切换 ≤80px 跳变（C11 只清 offset 不补偿当前 y） | ✅ 已修复（cc422d1 Phase 0）：BOB_SMOOTH ease-out 收敛 + reset_press 清 `_band_offset`（boss_movement.gd:19-23,34-37,42-44,136-146） |
| L15 | `test/` 21 个断言场景（smoke/i18n/tutorial/base_system 等） | 纯 bug（测试副作用） | 测试清空 `user://profile.json` 高分数据无快照还原（`GameState.high_score = 0` 经 setter 自动落盘；base_system/tutorial 直接清/改后落盘）——唯一正确范式在 `ui_capture.gd:12,111-113` | ✅ 已修复（Q23 2026-08-05：三账户测试快照还原；R07 2026-08-05 补 startup_flow 备份顺序——delete_save 原在快照前，savegame 还原后仍缺失） |
| L16 | `test/smoke_test.gd:796-800` | 弱断言 | 分支内断言 `wb2` 无 `!= null` 前置，弹未生成时用例静默通过（同文件 666/785 均有前置） | ✅ 已修复（cc422d1 Phase 0）：补 `_check(wb2 != null)` 前置 + 守卫（smoke_test.gd:795-799） |
| L17 | `scripts/settings_ui.gd:53,77` × `ui_chamfered_panel.gd:39-54` | 设计目标未达（布局） | 「操作模式」页内容 895px+ 溢出 480px 容器：面板被自适应撑到 ~1150px（>1080 屏幕，标题出屏），且自适应只放大不缩小（切页后不回落） | ✅ 已修复（2026-08-07）：ChamferedPanel 新增 `max_content_height` 内容自适应高度钳制（默认 0=不限，其他调用方零影响）+ settings 三页统一 `_wrap_scroll` 滚动容器；窗口实测面板 754px 不超屏、modes 内容 1056px 可滚动（overflow 548px）、滚动条可达、手柄焦点链保持 |
| L18 | `.github/workflows/release.yml:56-59` | 设计目标未达（CI） | 版本同步不落地：sed 仅改 CI 工作区不 commit，`git tag v3.27` 指向的提交 `config/version` 仍是 3.26——AGENTS.md「输入版本自动同步 project.godot」只对构建产物生效 | ✅ 已修复（P4 2026-08-05）：同步提交 `[skip ci]` + `git push origin HEAD:main`（release.yml:71-75）；R07 清理 L18 旧注释残留 |

### P3（轻微：修复 3 / 登记 30+，按类别合并）

- **修复**：L30 `resume_button` locale 刷新（随 L08）；`spawner.gd:548` Boss 预警 2.0s 硬编码（入报告待产品判断）；其余见下表类别。
- **登记清单（类别合并，位置详见报告 §4.3）**：
  - 判型/域校验同族遗漏（10 处）：starfield far/near_count、apply_run_save 负值、telegraph_duration、WEAK_LOCK、craft_counts、hp_mults 值域、狂暴计时键（E1_RING_INTERVAL/E2_POINT_COUNT/E3_SUMMON_INTERVAL 等）、mothership 时轴键 0 值 NaN、`bullet.gd` 零速度弹、`player.gd` dist_falloff/aim_frame 零距离除零。
  - 注释失实/文档-代码矛盾（5 处）：turret_battery:6 monitoring 表述、hud_capture 还原声称、AGENTS 与 run.sh set -e 表述、README 手柄键表漏 LT、README「CI 规划中」陈旧。
  - 防御缺口（低概率，5 处）：返航锁输入期弹反盾 monitoring 残留、boss volley 无进行中守卫、formation APPROACH_SPEED=0 卡死无兜底、boss `_begin_escape` 未 cancel_all、bullet 爆炸弹对炮台/编队机零 AoE（注释口径待确认）。
  - 性能遗留（4 处）：enemy 每帧 overlaps_area（perf 候选）、orbital_strike 每帧 288 次三角函数、player 弹反高光每帧重建点集、右摇杆 delta 上下文依赖。
  - 测试侧（6 处）：tutorial_test:75 调试 print、mothership_summon_test OR 弱断言、buff33 InputMap 无收尾清理、test/ 23 文件 gdformat 不合规（CI 盲区）、22 处括号尾随空格、hud_capture 注释。
  - 工具链/脚本（7 处）：run.bat 无版本判定、run.sh 与 run.command 策略不一致、release.sh zip 无前置检查、PIL 依赖未声明、balance_editor 500 裸异常、gdtoolkit 未锁版本、boss_fire/敌机机动参数硬编码魔法数。
  - **✅ 已收口（2026-08-07 复核）**：判型/域校验族 → R06+Q14；注释失实族 → R11；防御缺口 → R07①②③ + Q15（formation APPROACH_SPEED 兜底）+ 登记不修（bullet AoE，:1035）；性能遗留 → orbital_strike 已 P4 单位圆缓存 + 登记 perf 候选/待实证（:1036-1037）+ 登记不修（:715③）；测试侧 → R10+R11；工具链 → R08/R09 + R 复核 #6。无未处置项。

## 登记不修（论证后收敛）

1. **`bullet.gd` 爆炸弹对炮台/编队机零 AoE 伤害**（`_explode` 只 `as Enemy` 结算，`formation_craft`/`turret_battery` 注册在表内但被类型过滤跳过）：涉及「事件单位是否可被 AoE 清」的设计口径（玩家弹爆炸 AoE 是清场手段还是仅限普通敌机），现行行为无害（仅少一种清场路径）→ 登记待设计确认，不改。
2. **`enemy.gd:327` 每帧 `overlaps_area` 空间查询**（200 敌 ≈ 1.2 万次/秒）：注释自述「对齐原作逐帧轮询」，玩家受击判定语义依赖每帧重试（闪避重掷/无敌结束重命中），改信号驱动需重构受击语义 → 登记 perf 候选，不擅改。
3. **`player.gd:742` 右摇杆虚拟准星位移依赖 `get_process_delta_time()`**：60Hz 下物理/渲染步长无差异；高刷屏行为差异需实证（引擎内部赋值时序未确认）→ 登记待实证，不臆改。
4. **`smoke_test.gd` 其余分支内断言**（除 L16 外的同族弱断言）：L16 已登记，全量排查分支内断言属测试质量治理批次，不混入本轮。

## 修复起效记录（回填）

- **改了什么**：8 个源码/工具文件（enemy/player/spawner/boss/base_console/settings_ui/game_state/gen_balance_map）+ 6 个测试文件（pool_reuse 增断言、intro/return_capture 修复、autoplay 状态表、perf_bench 注释）+ 6 个文档/配置（BALANCE_MAP 重跑、TESTING、README×2、CONTRIBUTING、CHANGELOG、ci.yml 注释）+ 审查报告 `docs/archive/2026-08-03-code-review.md`。
- **为什么起效**：L02 把「断开→重连」在池化复用点补对称（`_ready` 不重跑的 Godot 语义下，只有 `reactivate` 是可靠重连点），`_on_buffs_changed()` 立即刷新保证缓存不陈旧；L01a 消除对 void 返回值的非法链式调用；L01b 使探针状态表与枚举序一一对应（阈值/名称/日志三处同源）；L03-L07 均为「既有防护族存在、此处遗漏」补齐（bool 排除、元素判型、域钳制），默认值/回退值逐位一致、行为零变化；L08 两处焦点归还对齐全项目模态页聚焦约定；L09/L10 让生成器覆盖声明式效果表并消除行号漂移（双向反查 0 缺失键）。
- **如何验证**：`--headless --import` 0 error（警告门禁干净）；`gdformat --check` + `gdlint`（autoload/ + scripts/，CI 口径）全绿；12 个针对性场景（smoke/pool_reuse/enemy_combat/difficulty/graze/boss_pattern/boss_phase/boss_registry/base_system/i18n/back_navigation/entry_animation）全 PASS 0 FAIL；`pool_reuse_test` 14 PASS 含 2 条新 L02 断言（逆验证：移除修复→FAIL，恢复→PASS）；capture 场景加载无 Compile Error；autoplay 短帧编译无 Parse Error；perf_bench 正常出结果；`--quit-after 300` 0 错误。

---

# Phase 0 技术债收尾批次（2026-08-03，ROADMAP Phase 0 全部开放项）

> 依据 `docs/ROADMAP.md` Phase 0（技术债收尾）执行；L 系列登记待办（L13-L16/L18）、A8、P2 清理、test/ 门禁盲区一次性收敛。验证见批次底部。

## 修复起效记录（回填）

| 编号 | 改了什么 / 为什么起效 / 验证 |
| --- | --- |
| L13 | ✅ **母舰×事件互斥**：`mothership.gd` `_ready` 注册 `add_to_group("mothership")`；`elite_turret_event`/`formation_strike_event` `can_trigger` 增加组查询——母舰在场（召唤/驻留/离场全程）事件不触发（母舰自动火力清事件单位全额发奖、玩家进舱零参与挂机）。**关键坑**：main 常驻蓄力虚影（`MOTHERSHIP_SCENE.instantiate()`）同样走 `_ready` 入组，会恒拦截事件——`main.gd` 创建虚影后 `remove_from_group("mothership")` 排除。验证：formation/elite 测试各 +2 断言（在场不可触发/离场恢复），49/59 PASS 0 FAIL；mothership_summon 32 PASS |
| L14 | ✅ **Boss 段切换 y 平滑过渡**：`boss_movement.gd` 新增 `begin_bob_smooth()` + `_move_bob` 过渡（`BOB_SMOOTH_TIME` 0.6s ease-out，从切换前 y 收敛到锚线正弦轨迹）；`reset_press` 补清三型 `_band_offset`（C11 只清一型 press）；`boss.gd _enter_phase` 切换帧记录当前 y。**语义变更**：C11「切换后立即回锚线」改为「平滑过渡不跳变」——`boss_phase_test` 断言同步更新（无跳变 <4px + 过渡后采样，采样窗口相位无关化：原峰谷差断言依赖相位从 0 起步，过渡等待后窗口相位任意会 flake，改「最大偏离锚线 ≥10px」）。验证：boss_phase 37 PASS（含新断言），boss_pattern 55 PASS |
| L15 | ✅ **测试 profile 快照还原**：20 个断言场景（smoke/i18n/tutorial/base_system 等开头 `high_score = 0` 落盘者）开头快照 `orig_high_score`、结尾还原 + `save_profile()`（ui_capture 范式推广；buff_visuals 双退出路径均还原）。为什么起效：high_score setter 自动落盘，测试跑完不再清空用户最高分。验证：受影响场景全量回归 0 FAIL |
| L16 | ✅ **smoke_test 弱断言**：分支内 `wb2` 断言补 `_check(wb2 != null)` 前置（与 666/785 行同款）。验证：smoke 143 PASS |
| L18 | ✅ **release.yml 版本同步落地**：Sync 步骤 sed 后 `git config` + `git add project.godot` + `git commit`（tag 前提交，tag push 携带该 commit 为祖先，`config/version` 不再滞后）。CI 改动本地无法实跑，语法经 yaml 结构审阅 |
| A8 | ✅ **PlayerVisuals 拆分**（架构债收敛，DESIGN_BASELINE §7.1 关闭）：新建 `scripts/player_visuals.gd`（`class_name PlayerVisuals extends RefCounted`，组合委托同 PlayerDamage/PlayerDash/PlayerParry 模式）聚合尾焰/残影池/机身色调四源/受击点脉动/弹反盾视觉/擦弹闪光状态；`player.gd` 删除对应 ~120 行与字段，`_visuals` 委托；`spawn_afterimage()` 公开接口保留转发（player_dash 依赖）；`engine_tint` 公开字段保留（PlayerBuffVisuals 写、buff_visuals_test 断言）。为什么起效：视觉状态与战斗逻辑（无敌倒计时/受击）解耦，无敌递减留在 player（视觉分支冻结 bug 的既有修复 E01 语义不变）。验证：smoke/buff_visuals/hit_logic/graze/parry/entry_animation/buff33 全 PASS（143/30/61/12/36/13/29） |
| P2 清理 | ✅ 三项：`game_state.gd` 删 `ACTION_LABELS` 死代码（全仓零引用）；`settings_ui.gd` `back_pressed` 死信号加「保留 API」注释（E13 先例）；`start_panel.gd` 补 `profile_corrupt` 生产侧提示（`START_PROFILE_CORRUPT` 双语键，与 save_corrupt 并列）。其余 P2 已由后续轮次覆盖（核实：main `_buff_ui` 已清、hud 假分支已修、`_start_release` 幂等 I010 已落地、toggle 测试在用） |
| test/ 门禁盲区 | ✅ **test/ 纳入 gdformat + gdlint**（23 文件格式化 + 18 条 gdlint 问题修复：4 处 `var A` 改名、3 处超长行折行、smoke/tutorial 重复 load 改 `preload` 常量）；**CI 新增编译探针步骤**（逐 `test/*.tscn` `--quit-after 2` + Parse/Compile/SCRIPT ERROR grep + timeout 60 兜底挂起——捕获 `--import` 不解析未引用场景的盲区，L01a/L01b 型问题不再潜伏）；断言场景循环加单场景 300s 超时。为什么起效：test/ 首次受格式/静态/编译三把锁约束。验证：`gdformat --check` + `gdlint`（autoload/ scripts/ test/）全绿，本地探针 46 场景 0 错误 |

## 验证

- `--headless --import` 0 error（警告门禁干净；PlayerVisuals class_name 注册）
- `gdformat --check` + `gdlint`（autoload/ scripts/ test/，CI 新口径）全绿
- 本地编译探针 46 场景 0 错误（新门禁）
- 针对性场景全 PASS：smoke 143 / formation_strike_event 49 / elite_turret_event 59 / mothership_summon 32 / boss_phase 37 / boss_pattern 55 / buff_visuals 30 / hit_logic 61 / graze 12 / parry 36 / entry_animation 13 / buff33 29 / startup_flow 38 / i18n 9
- 全量断言场景回归：35/37 一次通过；boss_enrage 一次 FAIL 系计时敏感 flake（重跑 0 FAIL，断言与本次改动无关）；formation 2 FAIL 系 L13 中间态（虚影拦截，修复后 49 PASS）

## CI 门禁 flake 修复补记（2026-08-03，Phase 0 批次后）

> GitHub Actions 三次复现的两个时序敏感断言，根因修复后 CI success（run 30827498885）。

| 断言 | 根因 | 处置 |
| --- | --- | --- |
| `boss_enrage_test`「RELEASE_HOLD 蓄力后 8 路重炮齐射」 | 8 路 360° 齐射向上路 ~0.27s 即出屏，场上计数依赖「发射时刻 vs 采样开始」竞争——慢 runner 上稳定失败（本地机器快、偶发） | `enrage_sequence.gd` 新增公开查询 `release_salvo_done()`（RELEASE_HOLD 复位、发射置位且保持），断言发射标记本身——不依赖弹在场时序；删除不再使用的 `_count_heavy_bullets` |
| `view_zoom_test`「刷怪 x 在可见区域内（60px 边距）」 | 敌机入场到达锚点后围绕锚点水平机动，固定 0.7s 后检查会测到机动后的 x（可越出 30px 边距）；生成范围 [left+60, right-60] 本应满足断言 | 改为轮询等敌机出现后立即检查出机位置（垂直下降阶段 x 不变 = 预告线 x） |

验证：两场景本地各连跑 3 次 0 FAIL；全量 37 断言场景本地 0 FAIL；GitHub CI 全绿（gdformat/gdlint 含 test/、import 门禁、编译探针、37 场景）。

---

# A 审计稳健性批次 + CI 编译探针事故修复（2026-08-04）

> A 审计（对局生命周期 / 数值边界 / 持久化）5 项修复落地；另修复 `f946f48`（accounts plan T4）引入的 test/ 编译探针事故 2 文件（远端 CI run 30875756367 失败的直接原因）。验证见批次底部。

## 修复起效记录（回填）

| 编号 | 改了什么 / 为什么起效 / 验证 |
| --- | --- |
| A 审计-1 | ✅ **reset_run 清 DDA 计时**：`game_state.gd reset_run()` 补 `_dda_timer = 0.0`——旧局受击降档残留渗透新局（新局开场弹幕密度被旧局降档）。验证：`hit_logic_test` 新增断言（受击激活 → reset_run → `dda_active()` false 且因子归 1.0） |
| A 审计-2 | ✅ **milestone_threshold 溢出钳制**：极大 index 时 `pow(cycle_mult, c)` 溢出 inf → `int(roundf(inf))` 行为未定义；`minf(pow(...), 1e15)` 钳至 finite，并补 `milestone_base` 空表守卫（除零）。验证：`difficulty_test` 新增断言（`milestone_threshold(99999)` 有限且非 INT32_MAX） |
| A 审计-3 | ✅ **apply_run_save 读档挂死保护**：里程碑定位 `while milestone_threshold(count) <= score` 原无上界——`milestone_base` 被手改非单调或 `cycle_mult` 极小（钳 0.01）时阈值增量收敛至有限值，大分数存档读档死循环。加 10000 迭代上限（1.01 倍率下万档阈值已超百亿，覆盖任何合理分数）。验证：`difficulty_test` 新增断言（`cycle_mult=0.01` + `score=999999999` 不挂死） |
| A 审计-4 | ✅ **cfg() 可变引用隔离**：`balance_service.gd cfg()` 对 Array/Dictionary 原返回 `_balance` 内部可变引用，调用方误写即污染配置真值；改返回浅拷贝（cfg 不在热路径，分配开销可接受）。验证：`balance_test` 新增断言（清空返回的 difficulty 字典后 `enemy_hp_multiplier()` 仍 1.0） |
| A 审计-5 | ✅ **SaveManager 原子写 rename 优先**：原实现先删正本再 rename——rename 失败则正本消失 + tmp 孤立 = 丢进度。改先尝试 rename（多数平台支持原子覆盖已存在文件），仅首次失败才删正本重试（回退路径风险窗口与原实现等价）。验证：`base_system_test` 新增断言（save 成功 / 正本存在非孤立 tmp / 覆盖写 999 往返正确） |
| CI 探针事故 | ✅ **f946f48 编译探针事故 2 文件**：① `autoplay_test.gd` 适配 StartPanel 退役时把 `_handle_pause_ui`/`_do_menu_return` 的 `func` 头注释而保留函数体与调用点——函数体游离并入前序函数（`_update_pause`/`_do_restart`），调用点 Parse Error「not found in base self」；且暂停链路状态机被并入 `_update_pause` 的间隔门之后（每 PAUSE_GAP_MS 才推进一次），行为亦失真。恢复两函数定义，语义文案同步（回开始面板 → StartPanel 退役后的「重进 main 启动自动读档」）。② `visual_capture.gd` 同提交误删 `const FRAMES_BEFORE_SHOT := 100`（gameplay/hud 两分支仍引用），恢复常量。验证：本地复刻 CI 编译探针 50 场景 0 错误（修复前本地可复现 2 文件 SCRIPT ERROR，与远端 run 30875756367 日志一致） |

## 验证

- 本地全量复刻 CI 五阶段全绿：`gdformat --check` + `gdlint`（autoload/ scripts/ test/）；`--headless --import` 警告门禁干净；主场景冒烟 300 帧正常退出；编译探针 50 场景 0 错误；断言场景 41/41 PASS 0 FAIL（autoplay_test 按 CI 口径跳过）
- 附带文档同步：`docs/BALANCE_MAP.md` 重生成（`88dcdd7` movement.type4 键统一后未重跑——「未引用键」「json 缺失键」两段 type4 条目消除 + 两处行号漂移修正）

---

# M 系列（2026-08-04，全仓库文档-事实口径统一轮）

> 依据用户指示「将目前所有和事实不符的文档做统一口径,留下真正的遗留项」执行;5 路并行只读核查(计数门禁/平衡数值/结构与入口/设计状态/翻译引用)对照代码与配置逐项取证,3 路并行文档修正 + 主控复核。本系列分「发现修复」与「登记遗留」两部分。

## 发现与修复（2026-08-04 本轮落地）

| 编号 | 位置 | 描述 | 修复与验证 |
| --- | --- | --- | --- |
| M01 | `scripts/boss.gd:313` | **第 4 Boss「月蚀」轮换不可达**（内容演化落地遗漏）：`setup()` 中 `clampi(p_type, 1, 3)` 系 K12（2026-08-03）越界钳制，注释称「spawner 轮换路径恒 1..3」；2026-08-04 内容演化把 `spawner.gd:615` 改为 `%4+1` 后上限未同步放开 → type4 被钳成 3，`hp_mults[3]`/`phases.type4`/`movement.type4`/`enrage.type_4` 全部不可达，轮换实际只有 3 型（Hive 重复出现）；`boss_enrage_test` 场景5 只数弹量未查 `boss_type` | ✅ `clampi(p_type, 1, 4)` + 注释同步（含 spawner.gd 轮换注释 `(N-1)%4+1`）；`boss_enrage_test` 场景5 补断言 `boss_type == 4`。验证：boss_enrage 40 PASS（含新断言）/boss_pattern 58/boss_registry 35 全绿 |
| M02 | `balance_service.gd:12-13,24-25`、`enemy.gd:41`、`game_state.gd:140-141,321-322` | **脚本回退值与 json 定稿值漂移**（2026-08-04 深局校准只改 json 未同步回退）：`hp_ramp_factor` 回退 0.12 vs json 0.25、`damage_ramp_factor` 0.08 vs 0.2、`per_boss_kill` 0.5 vs 0.6、`per_ten_minutes` 1.0 vs 1.5——json 缺失/损坏时回退旧值，违反 `.agents/balance-config.md`「script defaults must match」约定 | ✅ 4 处回退/初始值同步为 0.25/0.20/0.6/1.5，重跑 `gen_balance_map.py`（BALANCE_MAP 回退列 4 值更新）。验证：difficulty_test 63 PASS（曲线断言未受影响，cfg 路径读 json） |
| M03 | `data/translations.csv` | **翻译键缺失 2 个**：`BOSS_TYPE_4`（第 4 型 Boss 名牌 `tr("BOSS_TYPE_%d")` 回退显示字面）、`ACT_PARRY`（`REBINDABLE_ACTIONS` 12 动作 vs CSV 11 键，改键页「弹反」行回退字面） | ✅ 补 `BOSS_TYPE_4`（Ⅳ型 · 月蚀 / Type IV · Eclipse）+ `ACT_PARRY`（弧光弹反 / Arc Parry）。验证：i18n_test 9 PASS |
| M04 | `scripts/welcome.gd:391-397` | **welcome「设置」按钮静默死钮**：`_on_settings_pressed()` 经 `get_first_node_in_group("settings_ui")` 查找，welcome.tscn 无 SettingsUI 实例 → 返回 null 静默 return，按钮无任何反馈（账户计划声明 welcome 含设置入口） | ✅ `scenes/welcome.tscn` 挂载 SettingsUI（layer=16, process_mode=Always，与 main 配置一致）。验证：welcome_flow_test 28 PASS；设置开→关恢复 welcome 可见链路经代码核对 |
| M05 | 全仓库文档 | **文档-事实口径过期批量**（37 断言场景→41、46 场景→50、3 Boss→4 型、16 buff→19、校准旧值 0.5/1.0/0.08→0.6/1.5/0.25/0.20、StartPanel 残留→welcome、存档路径旧描述→账户后事实、计划文档路径缺 `archive/` 前缀等约 60 处；另 META_HUD_DESIGN Status 改 Implemented、INTRO P4 标注 README 子项完成） | ✅ 三路并行修正 + 主控全局残留扫描补齐（AGENTS/CLAUDE/README×2/CONTRIBUTING/ci.yml/ROADMAP/DESIGN_BASELINE/ARCHITECTURE/EXIT_FLOW/TESTING/ENDLESS_BALANCE_PLAN/ELITE_TURRET_EVENT/AUDIT_REVIEW_SOP/META_HUD_DESIGN/INTRO_CINEMATIC/RETURN_HOME_CINEMATIC/CHANGELOG/.agents×2）。验证：残留扫描 0 过期引用（历史记录与 archive 快照按规则保留） |
| M06 | `scripts/ui_buff_icons.gd:42-116` | **新 buff 无专属字形**（登记遗留后落地）：16 个 glyph 分支 vs 19 种 buff——`crit_shot`/`shield`/`bullet_speed`（2026-08-04 新增）走 `_` 回退圆环，HUD 图标格与三选一卡片无专属字形与分类色 | ✅ 补三字形 match 分支（暴击=十字准星+中心点、护盾=圆盾外环+菱形脊、弹速=水平弹头+三条速度线，均 24 单位坐标系与既有 glyph 同风格），分类色归位（`crit_shot`/`bullet_speed`→`_OFFENSE` 青、`shield`→`_SUSTAIN` 绿），头部注释 16→19。验证：CPU 渲染 19 字形一览目检（三新字形语义清晰、与 explosive/armor/regen/power_shot 区分、无越界）+ 全量测试 |

## 登记遗留（不修，保留待办）

| 编号 | 严重度 | 位置 | 描述 | 处置建议 |
| --- | --- | --- | --- | --- |
| M07 | P3 | `scripts/back_navigator.gd:19,94-96` | `CONFIRM_EXIT` 枚举+分支为死代码：`decide_back_action()` 决策表（:107-130）任何状态不返回该分支，顶层退出确认已由 welcome 场景自处理 | 低危死代码，择机删除枚举+分支并同步 `docs/EXIT_FLOW.md` 状态机清单（已标注 retired） |
| M08 | P3 | `scripts/start_panel.gd`、`scripts/start_radar.gd` | 孤儿脚本：StartPanel 2026-08-01 退役后无任何场景引用（`start_backdrop.gd` 由 welcome 复用，保留） | 确认无保留价值后删除两文件（含 README/ARCHITECTURE 已改口径） |
| M09 | P4 | `data/translations.csv:170-171` | `SET_LANGUAGE_ZH/EN` 孤儿键：语言按钮硬编码「中文/English」，全仓无 `tr()` 引用 | 无害冗余，择机删除或接 i18n 动态标签 |
| M10 | P4 | `docs/INTRO_CINEMATIC.md` §4 P4 | 真实人工遗留：低端机重测 + gamepad/mobile 输入检查（README 子项已由 README.md:41 覆盖完成；附注手柄跳过仅 B=ui_cancel 可用） | 发布前人工验证项，文档已标注为 leftover |

## 验证

- 代码改动（M01-M04）：boss_enrage 40 / boss_pattern 58 / boss_registry 35 / i18n 9 / welcome_flow 28 / difficulty 63 全部 0 FAIL。
- 代码改动（M06）：`ui_buff_icons.gd` 3 个新字形分支 + 分类色归属；CPU 渲染 19 字形一览目检（暴击准星/圆盾/弹头+速度线语义清晰、与既有字形区分、无越界）；全量断言场景 0 FAIL。
- 文档改动（M05）：全局残留扫描（37 断言 / 46 场景 / 16 buff / 3 型轮换 / StartPanel 活跃表述 / 旧校准值 / 缺 archive 路径）0 命中（历史记录与 archive 快照按规则保留）。
- 未改动：`docs/archive/`（历史快照豁免）、AUDIT_VAULT 历史条目、CHANGELOG 历史版本条目、BALANCE_MAP（生成文件，已重跑）。

---

# P 系列（2026-08-05，主架构运行效率审计全量执行，`docs/archive/2026-08-05-main-architecture-optimization-report.md`）

> 依据用户指示「全量级执行今日审计报告」执行（goal 模式）；报告为官方文档 + 社区实践双源审计 + 本机 `perf_bench` 实测（只读完成），本批 P0×3 + P1×5 + P2 可行条目全部落地。按 AUDIT_REVIEW_SOP：每项即时定向验证，批次底部全量回归。

## 发现登记与修复起效记录（回填）

| 编号 | 严重度 | 位置 | 发现 | 处置：改了什么 / 为什么起效 / 如何验证 |
| --- | --- | --- | --- | --- |
| P0-1 | 高 | `death_replay.gd`、`main.gd:375`、`entity_registry.gd`、`bullet.gd` | 死亡回放每渲染帧 `get_children()` 新建 Array + 全子节点 `as Bullet` cast + 每弹内层 `[x,y]` 小数组分配 + 缓冲满 `pop_front()` O(n) 整表移位——全对局唯一高危常驻分配链（官方 data_preferences 点名反模式） | ✅ 录制数据源改敌弹注册表（`EntityRegistry.enemy_bullets` + `_enemy_bullet_set`；`bullet._apply_faction` 按阵营幂等注册/注销——覆盖池化激活/直实例化/reflect 翻转，`deactivate`/`_exit_tree` 对称清理，池内 reparent 幂等无害）；帧缓冲改固定容量环形缓冲（`_write_idx` 取模写入，删除 `pop_front`；`play()` 环绕序化引用传递零拷贝）；内层 `[x,y]` 改 `PackedFloat32Array` 交错存储（帧槽 clear 复用保留容量，录制循环零分配）。验证：smoke/enemy_combat/grace_period/graze/parry/pool_reuse 0 FAIL；`--quit-after 300` 0 error |
| P0-2 | 高 | `enemy.gd:351-357` | 每物理帧每敌 1 次 `overlaps_area` 空间查询（N=在屏敌数，perf_bench 压力 200 只）——碰撞对/空间查询类社区点名隐藏瓶颈 | ✅ 信号事件驱动：`area_entered/exited` 标记 `_body_contact`（Enemy `collision_mask=3` 已含 player Hitbox 层 1，几何等价于原 `overlaps_area(hb)`）；重叠期每物理帧 O(1) `_try_body_collision()` 守卫重掷——`take_damage` 的无敌/闪避/单帧守卫使语义与逐帧轮询**完全等价**（无敌结束仍重叠再命中、闪避每帧重掷），空间查询从每帧 N 次降到事件 0 次。`reactivate`/`deactivate` 复位 `_body_contact` 防池残留；`_active` 守卫防物理回调延迟 flush 的陈旧事件（社区 §5.3 幽灵事件教训）。Boss 体碰（每帧 1 次查询）按最小改动原则保持轮询。验证：enemy_combat/grace_period/graze/parry/pool_reuse 0 FAIL |
| P0-3 | 中 | `scenes/bullet.tscn`、`bullet.gd`、`boss_fire.gd`、`turret_battery.gd`、`enemy.gd` | 每颗子弹 2 个 Polygon2D = 2 CanvasItem/draw call（峰值 300 弹 = 600）；GL Compatibility 下 draw call 更贵（官方 GPU 文档 + godot#85320 modulate 裂批次） | ✅ 弹体+白芯合并为**单 Sprite2D + 共享图集**：`Image` 扫描线填充光栅化共享纹理（4.6.2 实测无 `Image.draw_polygon`——三角扇分解 + 扫描线填充自实现；玩家金体白芯 / 敌弹红体两共享纹理，24×8px 箭头几何中心对齐）；`_apply_faction` 切纹理 + scale（语义与 polygon 等价），laser/heavy/linger 改 `self_modulate` tint（同色组内仍合批）；`polygon_node`/`core_node` 合并为 `sprite_node()`，4 个调用方同步。**窗口实测（Apple M2 / OpenGL 4.1 Metal）**：改造前 81 敌弹 ≈109 draw calls、+100 玩家弹 ≈245；改造后 181 颗总弹 ≈38（-85%，含场景基线）；单像素 ASCII 目检金体白芯箭头/敌弹红体形状与多边形设计一致。`enemy_combat_test` laser 细长化断言改 `Sprite2D` 路径 |
| P1-1 | 中 | `bullet.gd:288,303` | 爆炸/溅射命中对整张敌人注册表 `duplicate()` 浅拷贝（O(n) + 分配）——laser_weapon 已有同款倒序遍历已验证模式 | ✅ 倒序索引遍历（`take_damage→die→注销注册表 erase` 只影响已处理的高索引区，倒序不受突变破坏；`e.global_position.distance_to` 计算不变）。验证：enemy_combat 0 FAIL（爆炸弹 buff 路径覆盖） |
| P1-2 | 低 | `player.gd:869-880` | spread 循环内每弹重算 `_buff_scale`（含 `pow()`）与 `bullet_damage()`——同帧为恒定值（社区「循环不变量外提」直接靶点） | ✅ `loop_speed`/`loop_damage` 提出循环（纯函数依赖 buff_count，循环内不变，语义逐位等价）。验证：smoke/buff_effects/buff33 全绿 |
| P1-3 | 低 | `hud.gd:372-377` | 仪表轮询 0.1s 无条件写 setter（ProgressBar setter 内部 queue_redraw），值未变也触发无意义重绘 | ✅ epsilon 守卫（fuel/dash/parry 三 bar 值变化 >0.001 才写）。验证：hud_capture 窗口截图目测正常 |
| P1-4 | 中 | `main.gd:452-464`、`meta_health_fx.gd:437-463` | 启动一次性 stall：BGM 3.5MB WAV `CACHE_MODE_IGNORE` 每次进 main 重新 load+解码；裂纹场 SubViewport 512² 渲染 + `get_image()` GPU 回读占首帧 | ✅ BGM 改 `CACHE_MODE_REUSE`（静态音频缓存复用零副作用；音频路径保持等价——AGENTS.md「paths must stay equivalent」，不转 OGG）；裂纹场烘焙延后到首帧后（`_defer_bake` await 首帧 + `is_inside_tree` 守卫，C15 同款；`_field_ready=false` 期间 shader 已早退不显示裂纹，满血开局无感知）。验证：`--quit-after 300` 0 error；meta_health_fx_test 全绿 |
| P1-5 | 低 | `explosion.gd:131-135` | 池化爆炸回池不 reparent 统一池节点——隐藏爆炸堆积在各 parent（多为 Main）下，放大 Main 子节点数与死亡回放遍历成本（上限 24 有界，低危） | ✅ 回池 reparent 到统一 `ExplosionPool` 节点（`_ensure_pool_node` 挂 current_scene 下、跨场景重载失效重建）；`_repooling` 置位防 reparent 触发 `_exit_tree` 误清池清单。验证：pool_reuse 14 PASS |
| P2-1 | 低 | `meta_health_fx.gd:353-363` | 自适应增益扫描 4 次/秒 `get_parent().get_children()` 树遍历 | ✅ 注册表/静态计数替代：`Bullet.active_count()`（`activate`/`deactivate` 成对维护、`_exit_tree` 补减防泄漏） + `Explosion.live_count()`（`_ready`+1、finished/外部销毁 -1，`_settled` 防双减、池内 reparent 置位不计）——语义与原 get_children + is_active/visible 过滤等价。验证：meta_health_fx_test 全绿 |
| P2-2 | 低 | `enemy.gd:465-473` | `_move_ctx` 字典每物理帧每敌 8 次 dict hash | 按报告结论「收益最低，可保持现状」**不修**（C06 字典复用已是最优折中，改成员字段收益可忽略）——登记为设计确认 |
| P2-3 | 低 | `bullet_pool.gd` | 同屏弹量无显式硬上限（现靠 DDA 降档 + 出屏 margin 回收间接控制） | ✅ 敌弹硬上限 `MAX_ENEMY_ACTIVE=500`（远高于 perf_bench 实测 300+ 峰值，正常对局永不触发；**仅敌弹**——弹幕主力，玩家火力射速自限不受影响），超限 `fire` 返回 null，全部 15 处敌弹调用方判空（enemy/boss_fire×9/turret_battery×2/boss_attacks×2；sweep drop 循环内 break 防 cap 期死循环，其余 return/continue）。验证：全量断言场景 0 FAIL |
| P2-4 | 低 | 全仓碰撞配置 | 碰撞 mask 自查（§5.2 关注「能碰但代码过滤」的隐形碰撞对） | ✅ 自查通过无修复项：player 身体/Hitbox(layer1)/GrazeArea(mask8)/ParryShield(mask8)/enemy·Boss(mask3)/Turret·FormationCraft(mask0)/玩家弹(mask4)/敌弹(mask1)/FormationBomb(layer8 引信制) 各 layer/mask 精确细分；敌弹与 Enemy 的 `body_entered`（身体层 1）均未连接——`as Bullet`/`player_hitbox` 组判定天然分流，无隐形碰撞对 |
| 6-11/6-12 | 观察 | — | 爆炸池 reparent（6-11）、同屏弹量硬上限（6-12） | ✅ 观察项转修复落地（P1-5 / P2-3） |

## 验证

- 五层门禁：`gdformat --check` + `gdlint`（autoload/ scripts/ test/）全绿；`--headless --import` 0 error；`--quit-after 300` 0 error；smoke 0 FAIL；**41 断言场景全量 0 FAIL**（back_navigation/balance/base_system/boss_enrage/boss_pattern/boss_phase/boss_phase_transition/boss_registry/buff_effects/buff_panel/buff_visuals/buff33/difficulty/elite_turret_event/enemy_combat/entry_animation/esc_navigation/formation_strike_event/grace_period/graze/hit_logic/i18n/intro_cinematic/keybind/meta_health_fx/mothership_summon/mothership_upgrade/mouse_lock/orbital_strike/parry/pool_reuse/return_cinematic/smoke/startup_flow/tutorial/user_db/user_session/view_zoom/wave_pacing/welcome_flow/window_size）
- 定向回归：enemy_combat / grace_period / graze / parry / pool_reuse（14 PASS）/ meta_health_fx 全绿；P0-2 修复点（直实例化敌机 `_active` 恒 false 语义缺口，`_try_body_collision` 不加 `_active` 守卫）经 hit_logic A6 验证
- **P0-3 渲染实测**（窗口模式 Apple M2 / GL Compatibility）：改造前 81 敌弹 ≈109 draw calls、+100 玩家弹 ≈245；改造后 181 颗总弹 ≈38（-85%）；单像素 ASCII 目检金体白芯箭头 / 敌弹红体形状正确
- **perf_bench A/B**（同环境交错 3 次取中位数，`--fixed-fps 1000`，baseline=HEAD worktree）：BASE 0.622/0.483/0.759 → 中位数 **0.622ms**；CUR 0.587/0.553/0.811 → 中位数 **0.587ms**（约 **-5.6%**；噪声区间内方向一致，CPU 逻辑压力场景下 P0-1 分配链消除 + P0-2 空间查询归零的预期收益）

---

# Q 系列（2026-08-05，全仓库深度 review，只审计未修复）

> 依据用户指示「goal 模式，对仓库进行深度 review，不设 token 限制」执行；按 AUDIT_REVIEW_SOP（并行审计 → 分类 → 登记）。7 路并行只读审计（对局编排/战斗系统/服务层/事件系统/UI 导航文本/测试 CI 工具链/平衡数值内容），每路对照「设计文档 × 代码 × git 历史」三角验证；主控对全部 P1/P2 与部分 P3 亲自复核代码证据。完整报告：`docs/archive/2026-08-05-deep-review-report.md`。本批**只登记不修复**，修复待用户指示。

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 全仓库深度 review（代码质量/架构一致性/测试/文档/安全/性能/生命周期） |
| 工作时间 | 2026-08-05（单次集中审核会话，goal 模式，无 token 限制） |
| 审核区域 | `scripts/` 75 文件 24218 行 + `autoload/` + `test/` 54 文件 + 11 场景 + CI/工具链/文档 |
| 审核方法 | 7 路并行只读审计 + 主控交叉复核（含 BALANCE_MAP 重跑对比、TaskPool 概率模拟、CanvasLayer 语义查证） |
| 结论 | 无 P0；P1×1 / P2×9 / P3×20 / P4×20+；三类结构性缺口：①2026-08-04 内容演化（4 型扩容）伴生遗漏；②2026-08-05 事件/实体管理器迁移语义残留；③2026-08-04 账户批次新测试约定回退 |

## 发现清单（Q 系列，登记待修复）

| 编号 | 严重度 | 位置 | 描述 | 修复指引 |
| --- | --- | --- | --- | --- |
| Q01 | P1 | `boss.gd:668` × `boss_attacks.gd:163-173` × `balance.json:367` × `BOSS_REDESIGN.md:108` | ring_burst 难度弹数按增量语义消费绝对值：json `[10,12,14]`（§5.6 设计为每档弹数）被加到基准 12 上 → 实际 22/24/26 发，正常路径每局必现、密度约 2× 设计；counts 表其余 9 键均为增量格式，唯此键绝对值 | 消费侧改绝对值 `maxi(6, ring_delta)`（推荐）或 json 改增量 `[-2,0,2]`——需设计拍板；补 easy/hard 弹数断言 |
| Q02 | P2 | `boss.gd:317-324` | hp_mults 校验 `>=3` 与回退数组 `[1.3,0.7,1.6]` 未随 4 型扩容：json 缺键/截断时 type4 越界 → `float(null)=0` → `max_hp=0` → **type4 Boss 免疫伤害**（H11 防线被 3 元素回退绕过；M01 只改 setup 钳制） | 校验与回退扩至 4 元素；补「3 元素 + type4」组合断言 |
| Q03 | P2 | `boss.gd:605` | `_load_patterns` 的 `clampi(boss_type,1,3)` 未放开：DEFAULT_PATTERNS 键 4 死数据；type4 配置损坏时静默回退三型（母舰）模式表 | 改 `clampi(boss_type,1,4)`；补 type4 回退断言 |
| Q04 | P2 | `game_state.gd:159,598,675` × `:1697-1699` | regen 缓存重登录不刷新：`_refresh_regen_cache` 仅启动（默认 medium）与 set_difficulty 调用；`_apply_settings_dict` 恢复存档难度后不刷新 → hard 玩家重启后回血按 medium，easy 减半 | `_apply_settings_dict` 设置难度后补调用；补断言 |
| Q05 | P2 | `task_pool.gd:30-34` | TaskPool 批次耗尽不足额刷新：排除在场任务使批次提前耗尽（全池可用恒 ≥ 需求），Python 模拟 2000 局 99.3% 出现 ≥1 次 1-2/3 槽，REFRESH_COST 照扣 | 批次耗尽且全池可用 ≥ 剩余需求时 `_refill()` 继续；补槽位恒定断言 |
| Q06 | P2 | `user_db.gd:119-120` | game_over_stats 死亡统计缺失：total_kills/games_played 全仓无写入点（账户计划 Task 2 承诺未实现），game_over_ui 只 record_score | 按计划补 GameState 转发 + 结算调用（游客跳过） |
| Q07 | P2 | `event_manager.gd:306-316` × `:198-208` | `fog_events.enabled` 总开关在自动触发路径失效：`_process` fog 分支不查 FOG_ENABLED（仅 can_trigger_group 检查，生产无人调用）；json `enabled=false` 迷雾照常触发 | fog 分支头部补 `if not FOG_ENABLED: return` + 测试 |
| Q08 | P2 | `docs/BALANCE_MAP.md` | 生成物过期：未随 2026-08-05 事件管理器重构重跑，重跑 diff +228/-217（缺 event_manager 区块/残留已迁走区块/行号漂移） | 重跑 `gen_balance_map.py` 并提交 |
| Q09 | P2 | `welcome.gd:514-530` × `settings_ui.gd:186-187` × `welcome.tscn:9-11` | welcome 设置页打开时 Esc 断链：ui_cancel 分支无 settings 可见性检查 → Esc 打开隐藏层 exit_confirm（不可见但被 grab_focus），设置页 Esc 关不掉，与 EXIT_FLOW「settings back = Esc」矛盾（**注**：「设置黑屏」推断不成立——官方文档 CanvasLayer.visible 不传播到子 CanvasLayer） | ui_cancel 分支前查 settings_ui 可见则调 `settings.back()`；补「设置打开时 Esc」用例 |
| Q10 | P2 | `event_manager.gd:149-155` × `main.gd:97-100` | 遭遇事件触发计时器跨对局继承：`_encounter_timers` 挂 autoload 且 register_encounter 仅键缺失时初始化，`set_run_active(true)` 不重置 → 新局开局继承旧剩余值（可 ≤0）即触发；旧 ScheduledEventTrigger 每局归零，迁移改变语义未声明 | `set_run_active(true)` 时重置计时器 |
| Q11 | P3 | `welcome.gd:210-217` | `_show_msg` 消息互踩：SceneTreeTimer `time_left=0` 不取消，回调无条件清空 → 2s 内连发消息被下一帧清掉 | 代次计数或 disconnect 旧回调 |
| Q12 | P3 | `event_manager.gd:132-144` | fog first_delay 开局保护每进程一次而非每局一次（activate_fog 仅 wire 调一次）→ 同进程第二局开局 ~3s 可触发迷雾，与 FOG_EVENTS §2.2 不符 | set_run_active(true) 重置 first_delay |
| Q13 | P3 | `event_manager.gd:248-260,378-387` | 遭遇 abort 路径 event_ended 双发且发在事件仍活跃时（FSM 未回 IDLE，轮询重新登记）；当前无消费者 | end_active 记 pending 由轮询统一发信号 |
| Q14 | P3 | `formation_strike_event.gd:68` | CRAFT_COUNTS 直赋无判型（K14 只修精英侧），损坏配置 `:150` 崩溃 | 同 K14 口径加 `is Dictionary` 回退 |
| Q15 | P3 | `formation_strike_event.gd:192-194` | 编队事件无超时兜底：approach_speed ≤ 0（无 clamp）时永驻 FORMATION_ENTER + `_waves_paused` 常驻 → 波次与 Boss 调度全冻结 | 速度下限钳制或状态超时 |
| Q16 | P3 | `elite_turret_event.gd:194-201` | turret_counts 无上限钳制，>5 时 `SOCKETS[i]` 越界崩溃 | `mini(…, SOCKETS.size())` |
| Q17 | P3 | `user_db.gd:110,129-137,232` | users.json 结构守卫薄弱：`_users` 非 Dictionary/条目非 Dictionary 时直接报错，与 GameState 层守卫口径不一致 | `_ensure_loaded` 结构校验 + 隔离重建 |
| Q18 | P3 | `user_db.gd:83-87` | `_hex_decode` 奇数长度 hex 越界 + `-1` append PackedByteArray（手改 salt/password 触发） | 长度/白名单校验，非法回退空盐 |
| Q19 | P3 | `enemy.gd:453` × `:412` | 池化 reparent 无条件 unbind_enemy 误发 entity_unregistered，reactivate 只 register 不发信号——信号流不对称，与 ENTITY_MANAGER §4.2 矛盾（当前无消费者） | `_exit_tree` 按 `_repooling` 分流 unregister/bind |
| Q20 | P3 | `user_db.gd:292-300` × `welcome.gd:481-491` | 排行榜渲染无判型：非 Dictionary 条目 sort 崩溃、字符串 score 静默转 0 | sort 前过滤 `is Dictionary and score 数字` |
| Q21 | P3 | `welcome.gd:474-497` | 排行榜 overlay 打开无 grab_focus（welcome 唯一不聚焦模态），焦点停留被遮挡按钮，Enter 重复打开 | 打开时 close_button.grab_focus() |
| Q22 | P3 | `hud.gd:852-856` | buff 滚动明细栏 ScrollContainer 未设 vertical EXPAND_FILL → 内容超出不滚动、面板被撑大（buff ≥15 种触发） | 补 `size_flags_vertical = SIZE_EXPAND_FILL` |
| Q23 | P3 | `startup_flow_test.gd:48,174` / `welcome_flow_test.gd:35,145` / `user_session_test.gd:59,171` | 3 个账户批次测试 `_wipe_user_files()` 删 profile/users/存档且不还原——本地跑测试永久销毁开发者账户数据（L15 快照范式未推广） | 开头备份结尾还原（ui_capture 范式） |
| Q24 | P3 | `welcome_flow_test.gd:26-31,140` | 直调 `_unhandled_input` 绕过输入管线（C30 已修复模式回归，esc_navigation_test 同批已用 parse_input_event） | 改 `Input.parse_input_event` |
| Q25 | P3 | `user_session_test.gd:54,88-89,97` | 直读写私有 `_pending_legacy_profile`/直调私有迁移方法（A7 残留，无公开 API） | 补公开查询/触发接口 |
| Q26 | P3 | `.github/workflows/ci.yml:67-77` | 编译探针非零退出处理死代码：GH Actions `bash -e` 下非零直接中止，错误诊断/日志上传不执行；124=挂起语义本地(放行)与 CI(失败)相反 | timeout 包进 if + 日志加入上传路径 |
| Q27 | P3 | `boss_movement.gd:95-99` | 月蚀中心微摆振幅被 move_toward 速度上限压缩 ~一半（正弦峰值 78.5 > MOVE4_SPEED 40 px/s → 实际 ±15px 非 ±30px，波形低通失真） | MOVE4_SPEED ≥80 或直接绝对赋值；或文档注明实际振幅 |
| Q28 | P3 | `boss.gd:177-186,683` | DIFF_COUNT_DELTAS 回退表缺 ring_burst 键：json 缺键时三档恒 12 不分档（随 Q01 修） | 回退表补 `[10,12,14]` |
| Q29 | P3 | `enemy_move_strategy.gd:88-229` | 移动策略参数部分入库部分硬编码（sine 90/3.0、zigzag 0.7/0.9/0.15、dive 1.7/1.2、noise 谐波等）——平衡调整无法全经 balance.json | 入库或登记为有意保留 |
| Q30 | P3 | `boss_attacks.gd:239` | sniper3 三连发间隔 0.12s 硬编码（§8 设计数值），同族 charged_cannon.interval 有键——入库不一致 | 加键或登记为有意保留 |

## P4 观察项（按类别合并，详见报告 §6）

注释失实/文档口径 6 项（main.gd:43-44 B2 残留、cfg 注释、TESTING.md 53→54、autoplay BUFF_POOL_SIZE 16→19、comm_overlay 台词时长、main.tscn 7 处中文初始文本、MISSION_DEFS 双源）；硬编码坐标 2 项（boss strafe_range 1920、base_console 慢扫描带）；性能观察 5 项（orbital_strike 每帧 96 次三角、welcome 下拉重建、set_joy_deadzone 全量遍历、sfx 覆盖重播、排行榜路径）；边界/防御 7 项（missions progress 无钳制、difficulty score 无上限、delete_user 后 current_user 残留、全零权重退化、ConfusionEvent 降级信号不同步、reload_balance 不刷事件配置、hex 解码）；工具链/CI 5 项（gen_balance_map 裸异常、release.yml 重复版本/主分支版本滞后、event_manager_test 直写配置、smoke `== +33` 精确断言 flake 风险、entity_manager_test 断言顺序弱化）。

## 判定分类记录

- **真 bug（修复无需拍板）**：Q02/Q03/Q04/Q05/Q07/Q09/Q10/Q11/Q13/Q14/Q16-Q26 及 P4 项。
- **需设计拍板**：Q01（绝对值 vs 增量，消费与文档矛盾确定）、Q27（振幅可接受度）、Q15/Q29/Q30（入库 vs 有意保留）。
- **设计目标未达（计划未完成）**：Q06（账户计划 Task 2）、Q12（FOG_EVENTS §2.2）。
- **文档-代码矛盾（重跑生成物）**：Q08 + P4 注释口径项。
- 本批无「设计确认不改码」项。

## 验证

- 基线：`--headless --import` 0 error；smoke_test PASS exit=0；git 工作树干净（HEAD d6a1951）。
- 主控复核：Q01（json counts 表 9 键增量 vs ring_burst 绝对值 + §5.6 原文 + 消费链三方印证）、Q02/Q03（M01 只改 setup 钳制，git 确认）、Q04（两条调用路径确认）、Q05（Python 逐行模拟 2000 局 99.3%）、Q07（_process 分支无 FOG_ENABLED）、Q08（重跑生成器 diff +228/-217 后恢复原文件）、Q09（官方文档确认 CanvasLayer.visible 不传播，黑屏推断证伪；Esc 断链成立）。
- 待验证项（见报告 §7）：Q09 grab_focus 隐藏控件行为（是否升级手柄死锁）、遭遇自动触发对长跑测试暴露面、Q27 实机目检、smoke 精确相等断言、player/bullet 镜像字面量。

## 修复登记（2026-08-05 全量修复批次，一次性落地）

> 依据用户指示「goal 模式，最新报告，全量修复，仅提交」执行；五层门禁验证后提交。全部 30 项按报告修复指引落地，需设计拍板项按推荐方向执行（Q01 消费侧绝对值、Q27 绝对赋值、Q15 下限钳制、Q29/Q30 入库）。

- **Q01 ✅**：`boss_attacks._handle_ring_burst` 改 `maxi(6, ring_delta)` 绝对值消费（§5.6 语义）；`boss.gd:668` 注释标注例外；补 easy=10/medium=12/hard=14 断言（boss_pattern_test 场景 6/7）。
- **Q02 ✅**：hp_mults 校验 `>=4` + 回退数组扩 `[1.3,0.7,1.6,1.2]`；boss_phase_test 场景 5「3 元素 + type4」组合断言（max_hp>0 且模式表含 ring_burst）。
- **Q03 ✅**：`_load_patterns` clampi 放开 1..4；场景 5 断言 type4 配置缺失回退脚本默认表。
- **Q04 ✅**：`_apply_settings_dict` 恢复难度后补 `_refresh_regen_cache()`；difficulty_test Q04 断言（恢复 hard 后 rate=0.67/delay=5.0，修复前残留 medium 值）。
- **Q05 ✅**：TaskPool 批次耗尽且可用候选未抽完时 `_refill()` 继续（drawn_ids 防跨批重复）；Python 模拟 0/10000 不足额 0 重复；base_task_refresh_test 场景 9 固定种子 20 轮槽位恒满断言。
- **Q06 ✅**：GameState `record_game_over()`（登录用户累计 total_kills/games_played，游客/未登录跳过）+ game_over_ui 死亡结算调用；user_session_test 5b 断言。
- **Q07 ✅**：`_process` fog 自动触发分支加 `FOG_ENABLED` 短路（进行中事件不受影响）；event_manager_test 场景 6 enabled=false 惰性 + true 对照组。
- **Q08 ✅**：重跑 `gen_balance_map.py`（470 静态调用，+245/-233；event_manager 区块恢复、fog_event_manager 残留区块移除、type4.speed/sniper3 新键同步）。
- **Q09 ✅**：welcome `_unhandled_input` ui_cancel 最前查 settings_ui 可见则 `back()`；welcome_flow_test 场景 12（设置页 Esc 关闭 + 主层恢复）；**附带修复**：测试场景 8/9 重建实例未释放残留 SettingsUI 抢占 group；输入框 Enter 被 LineEdit 消费致真实登录断链 → 补 `text_submitted` 连接（Q24 走真实管线后暴露）。
- **Q10 ✅**：`set_run_active(true)` 重置 `_encounter_timers`（按 ENCOUNTER_CONFIG interval）+ `_fog_first_delay_left`/`_fog_check_timer`（Q12 同处）；event_manager_test 场景 6 断言计时回 interval。
- **Q11 ✅**：`_show_msg` 代次计数（旧 SceneTreeTimer 回调仅最新代清空）；删除 `_msg_timer` 声明。
- **Q12 ✅**：见 Q10（同处重置，测试断言 first_delay 回 FOG_FIRST_DELAY）。
- **Q13 ✅**：end_active 打断后按 is_active 分流——同步回 IDLE 即发、异步记 `_encounter_end_pending` 由轮询在 FSM 回 IDLE 后统一补发（恒一次且不早发）；event_manager_test 场景 6 断言恰好 1 次。
- **Q14 ✅**：`CRAFT_COUNTS` 判型回退（K14 同口径）。
- **Q15 ✅**：`APPROACH_SPEED` 下限钳制 `maxf(...,1.0)`（防永驻 FORMATION_ENTER 冻结波次调度）。
- **Q16 ✅**：`_total = clampi(raw, 0, StrikeCarrier.SOCKETS.size())`。
- **Q17 ✅**：`_ensure_loaded` 结构守卫（用户表非 Dictionary 按空库重建；榜单键缺失单独补空不丢用户表）；user_db_test 场景 11 断言。
- **Q18 ✅**：`_hex_decode` 奇数长度/非法字符回退空（不越界不 append -1）；user_db_test 场景 11 验密安全失败断言。
- **Q19 ✅**：enemy `_exit_tree` 按 `_repooling` 分流（池化 reparent 只 unregister 不发 entity_unregistered，与 reactivate 信号流对称）。
- **Q20 ✅**：welcome 榜单渲染条目级判型（非 Dictionary/字符串 score 跳过）+ user_db `get_leaderboard` 过滤 + `_sort_board` 回调兜底；user_db_test 场景 11 + welcome_flow 覆盖。
- **Q21 ✅**：`_open_leaderboard` 末尾 `_leaderboard_close.grab_focus()`（welcome 唯一缺失聚焦的模态）。
- **Q22 ⚪ 复核无误报**：`size_flags_vertical = SIZE_EXPAND_FILL` 自 2026-07-30（d51a03f）已具备，报告行号与当前代码一致但属性已在——登记为审计误报，无代码改动。
- **Q23 ✅**：startup_flow/welcome_flow/user_session 三测试开头快照、结尾还原全部用户文件（含 savegame_* 与 .corrupt 枚举），本地跑测试不再销毁开发者账户表。
- **Q24 ✅**：welcome_flow_test `_press_esc`/`_press_enter` 改 `Input.parse_input_event` 真实按键管线（esc_navigation_test 同款）；暴露并修复输入框 Enter 登录断链（见 Q09 附带）。
- **Q25 ✅**：GameState 补 `legacy_migration_pending()/scan_legacy_migration()/clear_legacy_migration()` 公开接口，user_session_test 私有访问收敛。
- **Q26 ✅**：ci.yml compile probe 裸 timeout 改包进 if 条件（`bash -e` 下非零不再中止步骤，::error::/tail 诊断生效；124 挂起统一按失败）。
- **Q27 ✅**：`_move_type4` 改直接绝对赋值（与 `_move_bob` 同模式，正弦峰值 78.5px/s 不再被速度上限压缩）；MOVE4_SPEED 变量与 json 键移除。
- **Q28 ✅**：DIFF_COUNT_DELTAS 补 `"ring_burst": [10,12,14]` 回退（json 缺键时绝对值分档仍成立）。
- **Q29 ✅**：sine/zigzag/dive/noise/aggressive 策略参数全部入库 `enemies.move_strategies`（含噪声谐波/相移/悬停系数；缺键回退脚本默认=现值，行为逐字节等价），`_make_strategy` 注入。
- **Q30 ✅**：`boss.phases.attacks.sniper3.burst_interval` 入库（0.12），`_burst_timer` 消费之。
- **P4 已修**：注释失实 6 项（main.gd B2 残留、cfg 浅拷贝口径、TESTING.md 53→54、autoplay BUFF_POOL_SIZE 16→19、comm_overlay 淡出段注释、main.tscn 7 处中文初始文本改英文占位）；MISSION_DEFS/POOL 删内嵌中文 name/desc（显示全走 tr()）；orbital_strike 单位圆点集缓存（帧内免 96 次三角）；missions progress 负值钳 0；add_score 得分总量钳 `SCORE_CAP=1e9`（防配置 score 倍率 int64 溢出）；event_manager 全零权重均匀回退；ConfusionEvent 缺键降级改空转（信号与生命周期统一由编排器驱动）；`reload_balance` 联动 `events.reload_config()`；gen_balance_map.py 损坏 JSON 友好报错；release.yml 重复版本友好失败 + 版本同步提交推 main；event_manager_test 直写配置结尾还原；entity_manager_test 基准断言前置。
- **P4 复核不改**：boss strafe_range `1920.0 - STRAFE_MAX_X` 为设计宽度常量（view_zoom_test 断言 large 档 hi = view.end.x − 300 固定边距，2026-08-05 复核后保留原语义仅补注释）；smoke `== +33` 精确断言（TESTING.md 已登记 flake 自愈基线）；welcome 下拉重建/set_joy_deadzone 遍历/sfx 覆盖重播/排行榜路径（性能观察，收益低保持现状）；delete_user 后 current_user 残留（报告已逐路径验证无幽灵路径）。

## 修复验证（2026-08-05 批次）

- 五层门禁：gdformat --check（全量 130 文件）+ gdlint（autoload/scripts/test）全绿；`--headless --import` 0 error；`--quit-after 300` 0 error；smoke_test PASS；45 断言场景全量 0 FAIL（含改动相关定向：boss_pattern/boss_phase/boss_registry/boss_phase_transition/boss_enrage/difficulty/base_task_refresh/user_session/user_db/welcome_flow/startup_flow/event_manager/entity_manager/elite_turret_event/formation_strike_event/enemy_combat/pool_reuse/esc_navigation/view_zoom/fog_event）。
- 测试过程中修复的测试自身缺陷：Q04 断言构造（set_difficulty 会写 profile，改直写档再恢复）；Q10 断言值（formation interval=40 非 44）；Q13 计数（GDScript lambda 捕获 int 为值拷贝，改数组承载）；Q09 残留实例（测试重建未释放）；Q24 输入框 Enter 被 LineEdit 消费（真实断链，补 text_submitted）。

---

# R 系列（2026-08-05，独立审计：Q 系列未涉及内容，审计+修复）

> 依据用户指示「goal 模式，参照上一次审计没涉及的内容进行独立审计，仅提交不推送」执行；按 AUDIT_REVIEW_SOP（并行审计 → 分类 → 批处理提交 → 迭代修复+即时验证 → 归档回填）。6 路并行只读审计（金库遗留复核 / 资产与场景 / 发布打包与项目配置 / 离线工具链 / Q 修复批次复核 / 待验证点与数据质量），主控对全部 P2 与关键 P3 亲自读码复核 + 生成器实证（音频重生成逐字节对比、BALANCE_MAP 实跑、sprite 生成器非根目录实跑）。完整报告：`docs/archive/2026-08-05-independent-audit-report.md`。

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | Q 系列排除项/遗留项复核 + 未覆盖区域（资产/场景/发布打包/工具链）+ Q 修复批次复核 |
| 工作时间 | 2026-08-05（单次集中审核会话，goal 模式） |
| 审核区域 | `assets/`（26 .import + 2 shader）+ `scenes/` 11 场景 + `export_presets.cfg`/`release.sh`/`run.bat`/`packaging/`/`project.godot`/CI 工作流 + `scripts/tools/` 6 工具 + 金库遗留（M07-M10/L13-L18/C17/L 系列 P3 清单）+ Q 系列 §7 待验证点 + dac5d3f 修复批次 30 项复核 + 翻译数据完整性 |
| 审核方法 | 6 路并行只读 + 主控复核；生成器实跑（音频逐字节 A/B、BALANCE_MAP、sprite 非根目录） |
| 结论 | 无 P0；P2×3 / P3×12 / P4×15（观察）；复核误报 3 项；修复 15 项 + 登记不修 15 项 |

## 发现与处置（R 系列）

| 编号 | 严重度 | 位置 | 类别 | 描述 | 处置 |
| --- | --- | --- | --- | --- | --- |
| R01 | P2 | `export_presets.cfg:8-10,36-38` | 纯 bug（打包泄露） | `exclude_filter=""` → 全部 test/ 场景与脚本随发布包分发（3.26 PCK 实锤：`test/window_size_test.tscn.remap` 明文、91 个 `res://test/*.gd`）；test/ 无 .gdignore（docs/ 有，8-03 只修了 docs 泄露） | ✅ 两 preset `exclude_filter="test/*"` |
| R02 | P2 | `generate_audio.py:206-214` × `assets/audio/bgm_loop.wav` | 纯 bug（已烘焙进资产） | `chord_weight` 有效区间 `[0,CHORD_DUR)` 严格截断 → 相邻和弦交界权重和 = 0，每 5s 一次 pad/bass 零谷塌陷（资产实测 RMS 0.27→0.045）；**顺带**：HEAD 生成器全量重跑 bf×3 ≠ 提交资产（~3% RMS）——资产为 random 流起点独立生成的历史产物 | ✅ 区间扩至 `CHORD_DUR+XFade`（交界权重和恒 1）+ main() bf×3 前重置 `random.seed(20260720)` 对齐资产；重生成后仅 bgm_loop.wav 变化，bf×3 逐字节一致 |
| R03 | P2 | `generate_{enemy,player,mothership}_sprite.py` | 纯 bug（工具口径） | 三生成器输出路径 cwd 相对 `"assets/sprites/"`——非仓库根运行在别处落盘或崩溃（同目录其余 3 工具均 `__file__` 锚定） | ✅ 统一 `os.path.join(os.path.dirname(__file__),"..","..",...)`；/tmp 实跑落盘正确、14 PNG 零变化 |
| R04 | P3 | `enemy_pool.gd:46-47` | 纯 bug（Q19 修复不完整） | Q19 只修回收侧：spawn 侧 `reparent` 时 `_repooling=false` → 每次池化 spawn 发无配对 `entity_unregistered`（reactivate 只 register 不发信号，ENTITY_MANAGER §4.2 矛盾仍存） | ✅ spawn 侧 reparent 包 `set_repooling(true/false)`（与 `_reparent_deferred` 对称） |
| R05 | P3 | `startup_flow_test.gd:86-87` | 纯 bug（Q23 修复不完整） | `delete_save()` 在 `_backup_user_files()` 之前 → savegame.json 快照为空，还原后进行中存档仍缺失（Q23「三测试全部还原」登记夸大） | ✅ 快照移到 `_ready` 首行 |
| R06 | P3 | `starfield.gd:29-30`/`spawner.gd:546`/`elite_turret_event.gd:119`/`boss.gd`（interval×6 + hp_mults 正值域）/`game_state.gd:1579-1581`/`bullet.gd:140` | 纯 bug（判型/域校验同族，L 系列登记遗留） | starfield count 负值负尺寸 resize；telegraph_duration 0/负立即触发；WEAK_LOCK 非 Dictionary 消费方崩溃；狂暴 interval 0/负每帧攻击风暴；hp_mults 0/负 → max_hp≤0 → Boss 免疫伤害（Q02 缺失分支）；存档 score/kills/boss_kills 负值；bullet 零速弹永驻视野 | ✅ 判型/钳制（口径同 G06/H11/E04/L04/Q02），默认/回退值逐位不变，行为零变化 |
| R07 | P3 | `player.gd:666-667`/`boss_attacks.gd:428`/`boss.gd:1012` | 防御缺口（L 系列登记遗留） | ①锁输入期（母舰召唤/返航过场）早退使弹反盾 monitoring 残留，ACTIVE 期锁定则盾全程被动生效；②`_start_minion_volley` 无进行中守卫；③`_begin_escape` 常规攻击（瞄准线/蓄力/齐射计时）不清理 | ✅ ①锁输入强制关盾；②`_volley_timer>0` 早退；③补 `_attacks.cancel_all()` |
| R08 | P3 | `balance_editor.py:276` | 纯 bug（L 系列登记遗留） | 读侧 `json.loads` 无 try——balance.json 损坏裸 traceback 500（Q/P4 只修 gen_balance_map 侧） | ✅ 读失败友好 400 |
| R09 | P3 | `release.sh`/`run.bat`/`ci.yml:33` | 工具链（L 系列登记遗留） | release.sh tar/zip 无前置检查 + 版本不自动读 project.godot；run.bat 无版本判定 + pause 归零退出码；gdtoolkit 未锁版本 | ✅ `command -v` 检查 + VERSION 自动读取；`--version` <4.6 警告 + `endlocal & exit /b %EXIT_CODE%`；`gdtoolkit==4.5.0` |
| R10 | P3 | `tutorial_test.gd:79`/`mothership_summon_test.gd:84`/`buff33_test.gd:24-25` | 测试规范（L 系列登记遗留） | 调试 print 残留；穿梭门三重 OR 弱断言可空过；InputMap.add_action 无收尾 | ✅ 删 print；拆状态断言；`added_give_up` 标记收尾 erase |
| R11 | P3 | `turret_battery.gd:6`/`boss_movement.gd:97`/`elite_turret_event.gd:199`/`hud_capture.gd:2,34,60`/`autoplay_test.gd:5`/`README.md:80` | 文档-代码矛盾（L 系列登记遗留） | monitoring 表述未随 K09；「战斗期独占 y」未含警告期；「防负循环」表述失实；「全 16 种 buff」实为 15 distinct/池 19；手柄键表漏 LT | ✅ 全部同步 |
| R12 | P3 | `boss.gd:113-117,590`+`balance.json:647`/`back_navigator.gd:19,94-96`/`scripts/start_panel.gd`、`start_radar.gd`/`translations.csv:171-172` | 死代码/孤儿（M07/M08/M09 落地） | RING_BURST_COUNT 与 json 键 `boss.ring_burst.count` 零消费方（Q01 后死数据）；CONFIRM_EXIT 决策表无可达路径；start_panel/start_radar 退役零引用；SET_LANGUAGE_ZH/EN 零 tr() 引用 | ✅ 删除（EXIT_FLOW.md 同步；BALANCE_MAP 重跑 469 调用 0 缺失） |
| R13 | P4 | `bullet.gd:230`×`player.gd:1007` | 观察（Q §7 待验证点 5） | 弹碰撞半径 6.0 与擦弹环形带判定镜像字面量，双改风险成立 | ✅ `Bullet.COLLISION_RADIUS := 6.0` 常量互引 |
| R14 | P4 | `enemy_move_strategy.gd:212-214,241-243` | 边界防御遗漏 | Q29 入库的 freqs/phases 数组长度无校验——短数组 `_freqs[2]` 越界崩溃（Q02 同族未推广） | ✅ `_init` 注入处长度 ≥3 校验，坏值回退默认 |
| R15 | P4 | `main.tscn:1`/`player.tscn:1`/`AGENTS.md:25`/`shell-scripts.md:12`/`release.yml:66-68`/`AUDIT_VAULT` L 系列状态表/`docs/BALANCE_MAP.md` | 文档-代码矛盾/陈旧计数 | load_steps 20→18、7→8；AGENTS 53→54；shell-scripts version_ok 归属（在 run.command）；release.yml L18 注释残留；L13/L14/L15/L16/L18 状态表未同步；BALANCE_MAP 行号漂移（dac5d3f 改码后未重跑，24+24 行） | ✅ 全部同步/重跑 |

## 登记不修（论证后收敛，R 系列复核）

1. **L17 设置页溢出**：复核仍存（modes 页内容已增至手柄/无障碍段，溢出更甚）——维持登记待办，需窗口模式实测像素后定方案（ScrollContainer vs 压缩节奏）。**✅ 后续已修复（2026-08-07）**：见 L17 状态表（面板高度钳制 + 滚动容器，窗口实测不超屏）。
2. **M10 INTRO 人工验证项**：保留（发布前人工验证，文档已标注 leftover）。
3. **C17 back_navigator 7 处裸 get_node**：合理模式维持（main.tscn 固定子节点，风险低）。
4. **mothership 时轴 30+ 键 0 值**：维持 L 系列登记（手改 json 触发、改动面大收益低；tween 时长 0 为演出崩坏非系统崩溃）。
5. **dist_falloff/aim_frame「除零」**：**复核误报**——`d<=peak` 与 `d>=end` 分支覆盖全部定义域，`peak==end` 时 lerpf 不可达，无除零路径（NaN 配置才可达，理论级）。
6. **boss_fire 20°/15° 弹幕几何魔法数**：登记观察（几何常量非平衡数值，入库收益低；与 Q29 移动策略参数不同族）。
7. **SCORE_CAP 乘法后钳制**：登记观察（倍率 ≥1e13 理论溢出 int64，现实量级 ≤1e6 防御成立）。
8. **smoke `== +33` 精确断言**：登记观察（受控条件取整确定性成立，TESTING.md 已登记 flake 基线）。
9. **遭遇自动触发暴露面**：各长跑测试已核实安全（smoke 累计处理 ~6-8s ≪ 40s 阈值）——建议后续在 smoke 敏感段补遭遇契约断言，登记待办。**✅ 已落地（2026-08-07）**：`encounter_flow_contract_test` T3a 断言 3s 窗口无自动触发 + 计时未归零 + interval 配置下界锚点（≥20s）。
10. **`.godot/imported` 孤儿 ctex×4**：登记观察（源已删缓存残留，.gitignore 排除，不影响构建）。**✅ 2026-08-07 复核自然消解**：`.godot/imported/` 现存 ctex 均有对应 `assets/sprites/*.png` 源，孤儿 0。
11. **gen_balance_map 惰性正则局限 / balance_editor 新增键静默放行**：工具启发式已知局限，登记观察。
12. **builds/ 旧产物过期**：登记观察（3.26 含 docs 截图已随 .gdignore 修复；test/ 泄露随 R01 修复，下次重出即净）。
13. **Q09 焦点细节 / Q27 目检 / smoke flake / 镜像字面量**：Q §7 待验证点已全部定论（见报告 §6），无需再修。

## 修复起效记录（回填）

- **改了什么**：21 个文件 + 1 资产重生成 + 2 文件删除 + 2 生成物重跑（详见上表；另含 `docs/EXIT_FLOW.md` CONFIRM_EXIT 行删除、`docs/archive/2026-08-05-independent-audit-report.md` 报告新增）。
- **为什么起效**：R01 排除 filter 使 test/ 不再进入 PCK（重出即净）；R02 区间扩至 CHORD_DUR+XFade 后槽尾衰减与下一槽上升重叠、交界权重和恒 1（零谷消除），bf 种子重置使生成器全量重跑与资产逐字节一致（消除工具-资产漂移）；R03 路径锚定脚本位置（非仓库根运行不再错落盘）；R04/R05 补齐 Q19/Q23 的两侧遗漏（信号流对称、快照捕获原始状态）；R06/R07 判型/钳制/守卫与既有防护族口径一致、默认值与回退值逐位不变（行为零变化）；R08/R09 工具链诊断与退出码保真；R10-R15 测试规范、注释口径、死数据清理与文档同步，均无可达路径或行为差异。
- **如何验证**：五层门禁——gdformat --check（128 文件）/gdlint 全绿；`--headless --import` 0 error；18 个定向场景（startup_flow/mothership_summon/tutorial/buff33/entity_manager/pool_reuse/boss_pattern/boss_phase/boss_enrage/boss_phase_transition/difficulty/i18n/esc_navigation/base_task_refresh/event_manager/user_session/welcome_flow/smoke）全 PASS；全量 45 断言场景 0 FAIL（见当次提交记录）；生成器实证——BALANCE_MAP 实跑 469 静态调用 0 缺失、音频重生成仅 bgm_loop.wav 变化、sprite 三生成器非根目录实跑零 diff、release.sh `bash -n` + 6 Python 工具 `py_compile` 通过。

---

# 2026-08-06 全项目审核（12 领域并行 + 人工复核）

## 工作时间与区域

| 字段 | 值 |
| --- | --- |
| 审核类型 | 12 领域并行审核（GDScript 纪律/平衡配置/碰撞伤害视图/UI·i18n·导航/性能与对象池/持久化安全/文档漂移/主流程状态机/战斗实体/测试与 CI/Shell 与工具/玩法数值）+ 高危与部分中危逐行人工复核 + 全量修复（goal 模式） |
| 工作时间 | 2026-08-06（单次集中会话） |
| 审核区域 | 全仓（报告 `docs/archive/2026-08-06-audit-report.md`：高危 2 / 中危 8 / 低危 30 项按主题归并） |
| 审核方法 | 12 路并行只读 + 协调者人工复核（H1/H2/M1/M2/M3/M6 逐行取证）；分类口径：纯 bug / 设计目标未达成 / 应急补丁痕迹 / 文档与代码矛盾 |
| 结论 | 高危 2 全修、中危 8 全修、低危 30 项批量处置（修复 + 登记不修）；补回归测试 9 处；CI 增 BALANCE_MAP 零 diff 闸 |

## 发现与处置（2026-08-06 批次，编号延续报告）

| 编号 | 严重度 | 位置 | 类别 | 描述 | 处置 |
| --- | --- | --- | --- | --- | --- |
| H1 | P0 | `spawner.gd:651-661` × `main.gd:710` | 纯 bug（状态机漏洞） | Boss 战中返航 → `clear_pending()` 无条件 `_boss_active=false` → 继续出击后双 Boss 同场（`_boss_timer` 战时持续增长、`_next_boss_score` 仅击杀推进 → 分数门控立即满足；轮换/休整/狂暴单槽编排脱节，可链式出第三只）；G01 只覆盖预警 2s 窗口 case | ✅ `clear_pending` 按注册表存活 Boss 区分：`count_enemies(e is Boss)==0` 才复位；wave_pacing_test 场景 6 双断言（在场保持占用/无 Boss 解除） |
| H2 | P0 | `spawner.gd:467-475` × `balance.json:659` × `enemy.gd:185` | 设计目标未达成 | 分裂者（第 5 型）永不生成——`unlocked_types()` 上界 `mini(5, 4)` 截断，`unlock_scores` 自平衡化起 4 档未随 5 型机扩展（测试直注入绕过解锁路径）；子机 `p_difficulty=1.0` 固定，HP/速度不随对局 ramp 与「HP 半」语义脱节 | ✅ unlock_scores 扩 5 档（脚本默认 + json 双写 2500）；子机继承母体 `_difficulty`；enemy_combat_test 解锁路径 + 2.0 档子机 HP 断言 |
| M1 | P1 | `bullet.gd:269-292` | 纯 bug（池状态残留） | `_apply_faction` 复位 scale/modulate 但漏 `_sprite.self_modulate`——laser 黄/Boss 重弹橙/致死红（`bullet.gd:308`/`enemy.gd:575`/`boss_fire.gd:70,104`/`turret_battery.gd:213`）无一复位，laser 高频弹种对局必然复用带旧 tint | ✅ `_apply_faction` 补 `self_modulate = WHITE`；pool_reuse_test 染色→回收→复用断言复位 |
| M2 | P1 | `game_state.gd:1558-1562` × `save_manager.gd:66-72` | 纯 bug | 损坏存档 `.corrupt` 备份被二次隔离删除：`load()` 已 rename 并返回 `{}`，`load_run_data` 空字典档主校验必然不匹配 → `quarantine()` 先删刚生成的备份再 rename 不存在的正本（失败刷伪警告），损坏档彻底消失 | ✅ 损坏分支直接返回（不再做档主校验）；startup_flow_test 断言备份保留 + 正本已隔离 |
| M3 | P1 | `fake_enemies_event.gd:33` × `fake_enemy.gd:78` | 设计目标未达成 | 伪敌机约 75% 出生即销毁——出生 y = 视野顶 − randf(20,260)，出屏销毁余量仅 80px，深度 >80 个体首个物理帧即 queue_free（count=5 实际可见 1-2 只） | ✅ 销毁余量扩至 280（对齐最大出生深度，保留错峰入场设计）；fog_event_test 0.2s 后全部存活断言 |
| M4 | P1 | `boss.gd:653-691` | 设计目标未达成 | 4 型「月蚀」狂暴分档残缺——interval/speed/count 三表无 type4 行（E33 同族遗漏），狂暴参数三档恒定（easy 偏难、hard 偏易） | ✅ `_apply_difficulty_scaling` 补 E4_RING_INTERVAL/SPEED/RELEASE_SPEED/RING_BURST_SPEED × 档位 + E4_RING_COUNT/RELEASE_COUNT 增量；boss_phase_test 场景 6 easy/hard 对比断言 |
| M5 | P1 | `starfield.gd:42,56-57` | 设计目标未达成 | zoom>1 星空不覆盖可见区右/下边缘——星点锚 (0,0) 铺 `[0,1920/zoom]×[0,1080/zoom]`，C07 改尺寸未改锚点（档案 :965「恒覆盖」论证前提被自身破坏） | ✅ 星点锚定 `view_world_rect().position`（origin 缓存 + 回绕同基线）；Starfield 补 origin()/area_size() 访问器；view_zoom_test 覆盖断言 |
| M6 | P1 | `test/user_db_test.gd:18-21` | 应急补丁痕迹（Q23 清扫遗漏） | 测试直接删除 `user://users.json` 且无快照还原——本地跑一次即永久销毁开发者全部账户+排行榜 | ✅ Q23 快照范式补齐（备份/还原 users.json+corrupt+存档） |
| M7 | P1 | `view_zoom/window_size/difficulty/mouse_lock/base_system_test` | 纯 bug（测试副作用） | 5 测试经「部分覆写 profile.json + load_profile」间接清零 pre-login 最高分与高分榜并落盘（L15 只修直写路径；档案称 base_system 已修与 git 事实不符） | ✅ 5 测试 profile 快照还原（含 base_system 高分榜直写段） |
| M8 | P1 | `docs/BALANCE_MAP.md` × `ci.yml` | 文档与代码矛盾（流程反复复发） | BALANCE_MAP 行号漂移第 6 次复发（a8c97a4 后未重跑，125+/125- 纯行号）；E10/K18/L10/Q08/R15 五次同款根因「靠人记」 | ✅ CI 增「生成器重跑零 diff 闸」（改码必同步重跑）；本轮已重跑提交 |

### 低危批量处置（2026-08-06，按主题归并）

| 编号 | 类别 | 位置 | 处置 |
| --- | --- | --- | --- |
| L-a | 状态/逻辑 | `event_manager.gd:154-155` | ✅ `set_run_active(true)` 补 `_fog_cooldown_left=0`（Q12 同族遗漏；fog_event_test 断言） |
| L-b | 状态/逻辑 | `main.gd:560-572` | ✅ `_on_player_died` 清理召唤小窗（disconnect+skip，与返航路径同款——give_up/dock 同帧完成小窗冻结永驻） |
| L-c | 状态/逻辑 | `game_state.gd:592-595` | ✅ 里程碑推进改 while（与 apply_run_save 全补口径一致，跨档加分不漏档） |
| L-d | 状态/逻辑 | `main.gd:340` × `elite_turret_event.gd:136-143` × `formation_strike_event.gd:126-137` | ✅ can_charge 增「遭遇事件进行中禁止蓄力」（L13 互斥只查触发期，事件中召唤母舰清场全额领奖） |
| L-e | 状态/逻辑 | `boss.gd:774-776` × `boss_movement.gd:98-101,138-146` | ✅ 逃跑警告期上飘补三型（绝对 y 赋值走位叠加 `escape_drift_offset()`，增量走位保留直接减） |
| L-f | 状态/逻辑 | `strike_carrier.gd:32,115` × `elite_turret_event.gd:125` | ✅ 航母悬停/炮塔行锚点加 view 基线（D10 同族） |
| L-g | 状态/逻辑 | `mothership.gd:665` | ✅ 加特林弹仅视觉缩放（`b.scale` 改子 Sprite2D——原连带缩放碰撞半径 6→3.6×ws） |
| L-h | 状态/逻辑 | `player_damage.gd:49-52` | 登记不修（维持现状并注释说明）——护盾吸收有意不写 `last_hit_frame`：「每层吸收一次」语义要求同帧多弹逐发吸收（计入 A16 单帧守卫则同帧第二弹被拦截免费，hit_logic_test 同帧连打回归）；「同帧盾+实伤」概率极低，原报告即「登记即可」 |
| L-i | 状态/逻辑 | `game_state.gd:1626` | ✅ missions goal 走 `save_num` 判型（R06/R07 判型族同族遗漏） |
| L-j | 配置/工具链 | `spawn_telegraph.gd:5,18` × `spawner.gd:547-549` | ✅ 预告线视觉寿命改实例级 `duration`（spawner 注入 `telegraph_duration`，两套时钟统一） |
| L-k | 配置/工具链 | `elite_turret_event.gd:110-112,205` | ✅ ammo 条目级判型（难度键缺失/非 Array 回退 medium→内置默认；K14 只判容器） |
| L-l | 配置/工具链 | `bullet_pool.gd:35` | ✅ 口径注释（active_count 全阵营，敌弹 cap ≈ 500−活跃玩家弹，偏差可忽略） |
| L-m | 配置/工具链 | `balance_editor.py:283-292` | ✅ 写盘侧 OSError 兜底（R08 只修读侧；磁盘满/只读友好 400） |
| L-n | 持久化/账号 | `user_db.gd:54-73` | ✅ 头注/`_derive` 注释口径修正（自建 PBKDF2 变体，非标准互通；维持实现防破坏既有账号） |
| L-o | 持久化/账号 | `user_db.gd:150,172-175,192-193,224 等` | ✅ 条目级非 Dictionary 守卫（`_user_record` helper 全消费点收敛，Q17 只守顶层） |
| L-p | 持久化/账号 | `user_db.gd:247-255` | ✅ delete_user 连带清理 `<save>.corrupt` |
| L-q | 文档/翻译 | `README.md:36/136` × `README.en.md` | ✅ 特性清单补迷雾事件系统；45 场景链接改指 `docs/TESTING.md` |
| L-r | 文档/翻译 | `translations.csv` | ✅ 删 START_HAS_SAVE/START_NO_SAVE/START_SUBTITLE/START_TUTORIAL_DONE 4 死键（零引用实证） |
| L-s | 文档/翻译 | `game_state.gd:233` | ✅ 注释 TASK_* → MISSION_* |
| L-t | 文档/翻译 | `AGENTS.md` | ✅ Quick Reference 6→7 服务（补 UserDB） |
| L-u | 文档/翻译 | `ci.yml:4` | ✅ 「仅官方 checkout」→「仅官方 checkout/upload-artifact」 |
| L-v | 文档/翻译 | `CHANGELOG.md` | ✅ 3.23 断档说明（git 无 tag/条目，疑似有意跳号）；新增 [3.28] 审计条目 |
| L-w | 文档/翻译 | `back_navigator.gd:37` | ✅ 注释声明「右键=返回仅 main.tscn 实现，welcome 顶层无效」 |
| L-x | 文档/翻译 | `DESIGN_BASELINE.md:22` | ✅ RP 来源口径修正（仅 Boss 击杀 +5 与任务领取 +3，非 kills/score） |
| L-y | 玩法/数值 | `BOSS_REDESIGN.md:41,88` × `balance.json:613` × `boss.gd:239` | ✅ E2_AIM 0.3→0.35 对齐 G3 telegraph 门限（文档/配置/脚本三处同步；测试标签同步） |
| L-z | 测试规范 | `keybind/esc_navigation/base_system_test` | ✅ 键位快照还原（reset/rebind 自动落盘防开发者键位被重置） |
| L-ag | 测试规范 | `user_session/startup_flow/welcome_flow_test` × `user_db.gd` | ✅ **wipe 后缓存残留根因修复**——GameState._ready 的迁移探测（`_maybe_migrate_legacy_profile`）提前触发 `UserDB._ensure_loaded`，把真实用户表缓存进 `_db`；测试 wipe user:// 后缓存仍非空，「空用户表」起点失效（本机有开发者账户时迁移段/榜单断言失败，CI 干净环境不暴露）；新增 `UserDB.reload()` + `GameState.reload_user_db()`，三个 wipe 型测试 wipe 后显式刷新 |
| L-aa | 测试规范 | `smoke_test.gd:859 等 6 处` | ✅ 结尾改 profile 全量快照还原（原「恢复默认值」覆盖用户原档） |
| L-ab | 测试规范 | `mothership_upgrade_test.gd` | ✅ `_milestone_count` 直写 ×5 改公开 `set_milestone_count`（GameState 补 A7 setter） |
| L-ac | 测试规范 | `boss_phase_test.gd:274-279` | ✅ 场景 5 生成失败 null 守卫——balance.json 恢复无条件执行（原崩溃跳过恢复留仓库损坏态） |
| L-ad | Shell/音频 | `generate_audio.py:254-275` | ✅ 琶音死代码分支删除；BGM 单边 50ms 淡入改首尾互补交叉淡化（圈首 5dB 凹陷 + 回绕跳变消除） |
| L-ae | Shell/音频 | `.agents/shell-scripts.md:9` | ✅ run.sh 实为 `set -e`（非 `-euo pipefail`）口径修正 |
| L-af | Shell/音频 | `release.sh` | ✅ `--help`；GODOT 兜底链断裂诊断；tar/zip 前置检查移导出前 |

## 登记不修（论证后收敛，2026-08-06 批次）

1. **匿名 `savegame.json` 无迁移（game_state.gd:1413-1418,1430-1436）**：账户化前旧玩家进行中进度永不可达——迁移收益低（单机进度价值有限）且改动面大（涉及 B5 迁移链），登记观察；persistence-security.md 已补注 users.json 损坏静默重建口径。
2. **里程碑循环档边界增量倒挂（game_state.gd:696-713）**：80000→84050 增量 4050 仅为前档 40%——疑似有意设计（池拿完后影响有限），SOP「不盲调平衡」，登记待设计拍板。
3. **UserDB `_derive` 保持自建变体**：无实际弱化、改动破坏既有账号验密，口径已修（L-n），登记观察。
4. **enemy_pool.gd:49-51 / explosion.gd:56-57 池化 spawn 侧 reparent 同步执行**：与池自身防护口径矛盾但实测无恙（R04 已双向包裹），登记观察。
5. **fake_enemy.gd `_physics_process` 每帧 sin()×3 未走查表**：量级与 G017 判不修相当（≤4 只幽灵机），登记观察。
6. **game_state.gd:1413-1418 游客无存档路径**：设计语义（游客不落盘），非 bug，登记说明。
7. **死亡清理小窗（L-b）与遭遇互斥（L-d）未加独立测试**：同帧双蓄力/长按注入成本高，由 main 流程回归（fog_event_test 返航段）间接覆盖，登记待办。**✅ 已落地（2026-08-07）**：`encounter_flow_contract_test` T3b——事件进行中蓄力被拒（can_charge 事件互斥）+ 死亡路径清理召唤小窗，13 断言全绿。

## 修复起效记录（回填）

- **改了什么**：31 个生产/工具/测试文件 + 3 数据/文档生成物（balance.json 双写、BALANCE_MAP 重跑、CHANGELOG）——详见上表与报告；另含 `docs/archive/2026-08-06-audit-report.md` 报告（登记时点只读未改）。
- **为什么起效**：H1 按存活 Boss 注册表区分复位（预警窗口无 Boss 才解除占用，Boss 在场保持波次/Boss 门控冻结）；H2 解锁表与机型表等长 + 子机难度继承（深局分裂者子机 HP 与母体同 ramp）；M1/M2 对等重置与损坏分支短路（备份保留）；M3 销毁余量覆盖最大出生深度；M4 分档乘区/增量与 1/3 型同族（easy/hard 不再恒定）；M5 星点锚点随可见区平移；M6/M7 快照还原（Q23 范式推广到全部本地数据污染测试）；M8 生成器零 diff 闸把「改码重跑」从人记变机器强制。
- **如何验证**：五层门禁全绿（gdformat/gdlint/import 0 error/quit-after 300/45 断言场景，见当次提交记录）；新增回归断言 9 处（wave_pacing H1、enemy_combat H2×2、pool_reuse M1、startup_flow M2×2、fog_event M3+fog 冷却、boss_phase M4×6、view_zoom M5×2）；release.sh `--help` + `bash -n`；generate_audio.py `py_compile` + BGM 重生成实跑。

---

# S 系列（2026-08-07，搁置项重启：mobile touch + L17 + 测试待办 + A5 收敛）

> 依据用户指示「查找最新被明确标记为暂缓推进的事务（非 3.28 发布推迟）→ 确认真实未完成项 → 筛选高价值目标 → 书写计划文档 → goal 全量推进」执行。计划/清单：`docs/archive/2026-08-07-deferred-restart-plan.md`。

## 盘点结论（真实未完成项筛选）

| 来源登记 | 事项 | 复核 | 处置 |
| --- | --- | --- | --- |
| ROADMAP Phase 3 | mobile touch（content evolution 唯一剩余 cut） | 真实未完成（输入已全走 Input action 系，注入虚拟输入即可） | T1 重启立项 + 落地 |
| AUDIT_VAULT L17 | 设置页 modes 页溢出 | 真实未完成（裸 VBox 无滚动、面板自适应超屏） | T2 修复 |
| DESIGN_BASELINE §7.1 | A5 残余依赖收敛 | 部分完成（mothership 8 处 hud 组查找） | T4 收敛 |
| R 系列 #9 / 2026-08-06 #7 | 测试待办 2 项 | 真实未完成 | T3a/T3b |
| 竞品 P2-8 / M10 / 2026-08-06 #2 | 俄语 / 人工实机验证 / 里程碑设计拍板 | 用户决策未变 / 非代码任务 / 需设计拍板 | 不推进（维持登记） |

## 发现与落地（2026-08-07 批次）

| 编号 | 严重度 | 位置 | 类别 | 描述 | 处置 |
| --- | --- | --- | --- | --- | --- |
| S01 | P2 | 全仓（新 `scripts/virtual_controls.gd`） | 功能（mobile touch 重启立项） | 触屏虚拟输入层：左摇杆→move_*、右摇杆→aim_*（增量，同手柄语义）、按钮→boost/fine_move/dash/parry；Input.action_press/release 注入（player 读取路径零改动）；触屏瞄准基准=可见世界中心（player.aim_point 分支）；设置「触控」开关（GameState.touch_controls profile 持久化 + `touch_controls_changed` 信号联动 Main）；新增 `test/virtual_controls_test.tscn` 25 断言 | ✅ 落地：虚拟层挂 Main（layer=1 半透明）、EntityManager/GameState 转发、simulate_touch/drag 测试口（绕过窗口→视口坐标变换，MetaHealthFX.set_test_state 同款先例）；测试 25 PASS；禁用时零注入（桌面零回归） |
| S02 | P3 | `scripts/ui_chamfered_panel.gd` × `scripts/settings_ui.gd` | 修复（L17） | 设置页 modes 页溢出（详见 L17 行回填） | ✅ 见 L17 状态表 |
| S03 | P3 | `test/encounter_flow_contract_test.gd` | 测试补齐 | 遭遇契约 + 互斥 + 小窗清理独立断言（详见 R09/2026-08-06#7 回填） | ✅ 13 断言全绿 |
| S04 | P3 | `scripts/mothership.gd` | 架构收敛（A5） | HUD 引用 8 处重复 `get_first_node_in_group("hud")` → `_hud()` 延迟缓存（is_instance_valid 守卫）；welcome/pause_ui/事件类的低频组查找按 R12 先例保留（合理模式） | ✅ 行为零变化；mothership_summon/mothership_upgrade 0 FAIL |

## 修复起效记录（回填）

- **改了什么**：`scripts/virtual_controls.gd`（新）+ `entity_manager.gd`/`game_state.gd`（virtual_controls 转发 + touch_controls 设置/信号/持久化）+ `main.gd`（创建虚拟层 + 开关联动）+ `player.gd`（触屏瞄准基准）+ `settings_ui.gd`（触控段 + L17 滚动/钳制）+ `ui_chamfered_panel.gd`（max_content_height）+ `translations.csv`（SET_TOUCH×3 双列）+ `test/virtual_controls_test.tscn/.gd`（新）+ `test/encounter_flow_contract_test.tscn/.gd`（新）。
- **为什么起效**：S01 输入全走 Input action 注入——player 的 get_vector/is_action_pressed 读取路径零改动，桌面键鼠/手柄零回归（默认关）；触屏瞄准复用 H01 右摇杆虚拟准星增量语义；simulate_* 测试口避开 headless 窗口→视口坐标变换（30×）使断言稳定。S02 面板内容自适应钳制（默认 0=不限不波及他页）+ 滚动容器，面板不再超屏。S03 契约锚点（interval ≥20s）防未来把自动触发窗口调进测试时长。S04 延迟缓存与直接查找等价（hud 为 main.tscn 固定层）。
- **如何验证**：五层门禁——gdformat --check（131 文件）/gdlint 全绿；`--headless --import` 0 error；`--quit-after 300` 0 error；全量 **47** 断言场景 0 FAIL；L17 窗口实测（/tmp/ui_modes.png 1920×1080：面板 754px 居中、底部纯遮罩、滚动 548px 溢出）；virtual_controls_test 25 PASS、encounter_flow_contract_test 13 PASS。

---

# T 系列（2026-08-07，文档重构：去歪曲 + 减绕路）

> 依据用户指示「本次工作发现的文档问题重整——制作真实且可减少 Agent 跑弯路的文档重构」执行（goal 模式）。计划/清单：`docs/archive/2026-08-07-doc-refactor-plan.md`。触发：S 系列落地中触达的文档问题——断言场景数散布 10+ 处靠人记（M8 同根因复发）、AUDIT_VAULT 状态表与 DESIGN_BASELINE 不同步、headless 输入注入坐标陷阱无提示、gdtoolkit 本地安装无 PEP 668 指引。4 路并行只读盘点 + 人工复核。

## 发现与处置（2026-08-07 批次，编号延续计划）

| 编号 | 严重度 | 位置 | 类别 | 描述 | 处置 |
| --- | --- | --- | --- | --- | --- |
| T01 | P2 | `docs/TESTING.md` | 流程（计数漂移复发根因） | 断言场景数散布全仓靠人记（M8 同根因）：TESTING.md 无动态权威计数指引，静态硬编码 47/56 + 内联历史注记 | ✅ 顶部新增「Scene Counts (authoritative)」段：`ls test/*_test.tscn | wc -l` − 1（autoplay 探针）= 47、`ls test/*.tscn | wc -l` = 56；注明 CI 以实际文件为闸、数字仅信息性、其他文档禁止硬编码 |
| T02 | P2 | `.agents/doc-sync.md` | 规则固化 | 无「计数单一事实源」规则 | ✅ 新增规则：断言场景数以 TESTING.md 动态命令为权威，其他文档禁止硬编码；增删 test/*_test.tscn 时同步 TESTING 计数与清单 |
| T03 | P2 | `ci.yml` / `CONTRIBUTING` / `C_SHARP_ASSESSMENT`×5 / `ROADMAP` / `DESIGN_BASELINE` / `README`×2 | 计数过期（歪曲） | 「45 断言场景」等当前流程描述过期（实际 47）；README 徽章 v3.27/45 scenes 过期；README.en:137 与 :127 自相矛盾；ROADMAP:8 A5/A8 open 表述过期 | ✅ 全部统一：ci.yml 步骤名去硬编码（Run assertion scenes，无数字）；CONTRIBUTING/C_SHARP/ROADMAP/DESIGN_BASELINE 改 47 + 权威源指引；README 徽章 v3.28 / 47 scenes；ROADMAP A5/A8 改 all closed；历史时点快照（CHANGELOG/C_SHARP:49）按惯例保留 |
| T04 | P2 | `AUDIT_VAULT` 状态表/详情 | 文档间不同步（歪曲） | A5/A8 状态表仍 ⚠️（DESIGN_BASELINE §7.1 已 ✅）；A4 详情「残留 7 处 match」、A5「未收敛」、A8「视觉未抽」未划线；R 复核 #1（L17 已修未注）、#10（孤儿 ctex 已消解）；C34、L-P3 清单未收口 | ✅ 状态表 A5/A8 回填 ✅ + 引用 S04/:1061；A4/A5/A8 详情补划线注记；R 复核 #1/#10 收口；C34 标「已收口」；L-P3 类别清单补「已收口」注记（去向逐类核对） |
| T05 | P3 | `enemy_pool.gd:47` / `boss.gd:118,334,569` / `back_navigator.gd:19` | 注释编号误标 | 审计编号误标：enemy_pool R07→R04（池化 spawn 侧）、boss:118 R07→R12（死数据删除）、boss:334/569 R07→R06（判型族）、back_navigator R07→R12（CONFIRM_EXIT 删除） | ✅ 四处编号修正（含盘点遗漏的 boss:334/569 判型族属 R06） |
| T06 | P3 | `game_state.gd:103` / `user_db.gd:3` | 路径引用错位 | 注释引用 `docs/2026-08-04-local-accounts-plan.md` 缺 `archive/` 前缀（计划文档已归档） | ✅ 补 `archive/` 前缀 ×2 |
| T07 | P3 | `DESIGN_BASELINE:115` / `ARCHITECTURE:59` | 服务口径冲突 | 「Six non-autoload services」/ 委托清单漏第 7 个 UserDB（与 AGENTS.md 7 服务冲突，2026-08-06 审计已登记 7） | ✅ 改 Seven + 补 UserDB 条目（DESIGN_BASELINE 服务清单、ARCHITECTURE 委托清单） |
| T08 | P3 | `DESIGN_BASELINE:109` / `ELITE_TURRET_EVENT:173` | 节点树注册口径过期 | 「registered to spawner」过期（2026-08-05 起事件注册到 GameEventManager） | ✅ 改口径 ×2 |
| T09 | P3 | `docs/TESTING.md` | 知识缺口（绕路） | ①headless `parse_input_event` 注入鼠标/触摸坐标被窗口→视口变换（实测 30×，S01 调试多轮）；②gdtoolkit PEP 668 说明；③translations.csv 改后重导 `.translation`（gitignored）机制；④模拟输入走公开测试口规范未入测试文档 | ✅ TESTING.md 新增「Headless Test Environment Notes」小节收纳①-④；`.agents/gdscript-lifecycle.md` 补「Tests drive public test ports」约定（A7/C30/Q24 先例） |
| T10 | P3 | `docs/TESTING.md:17-69` | 场景清单遗漏 | 子系统清单漏 `virtual_controls_test` / `encounter_flow_contract_test` | ✅ 补两行 + 注明「清单可能滞后，以 ls 为准」 |

## 修复起效记录（回填）

- **改了什么**：文档 15 文件（TESTING/CONTRIBUTING/ROADMAP/DESIGN_BASELINE/ARCHITECTURE/ELITE_TURRET_EVENT/C_SHARP_ASSESSMENT/README×2/AUDIT_VAULT/doc-sync/CLAUDE 未动）+ `.agents/gdscript-lifecycle.md` + 代码注释 5 文件（enemy_pool/boss×3/back_navigator/game_state/user_db）——全部注释/口径，零逻辑改动。
- **为什么起效**：T01/T02 把断言计数从「散布硬编码 + 靠人记」改为「TESTING.md 动态命令单一事实源 + doc-sync 禁硬编码规则」——M8 同根因（BALANCE_MAP 行号）已用 CI 零 diff 闸根治，计数漂移这次从规则层断根；T03 消除当前流程描述的过期数字；T04 恢复 AUDIT_VAULT 与 DESIGN_BASELINE 的一致性（A5/A8 状态表是文档间不同步的现成反例）；T05/T06 修正会误导审计追溯的编号/路径；T07/T08 消除入口文档服务口径冲突与节点树注册口径矛盾；T09 把 S01 实测的 headless 输入坐标陷阱与公开测试口规范下沉到测试入口文档——后续写测试的 Agent 不再重复调试多轮。
- **如何验证**：残留扫描 0 命中——「45 断言/45 scenes」（排除历史时点快照与精英测试自身断言数）/「Six non-autoload」/「registered to spawner」（docs 顶层）/缺 archive 前缀；五层门禁——gdformat --check（131 文件）/gdlint 全绿、`--headless --import` 0 error、`--quit-after 300` 0 error、全量 47 断言场景 0 FAIL。
