# Godot 最佳实践与语法规范审计报告（2026-08-01）

> **处置状态（2026-08-02 口径统一回填）**：✅ C 系列 35 项已全量处置（2026-08-01，修复提交链 `174b5a0`→`a900018`→`8f134dc`→`c526d79`→`d4f427c`→`0555b19`→`1b5f424`→`832a167`，起效记录回填 `03fa3eb`）。例外（与 AUDIT_VAULT 一致）：C34 ⚠️ 部分完成（含设计确认锚点）、C19 🟦 设计确认不改码、C33 🟦 已核实无风险不修——故严格措辞为「已全量处置」而非「全量修复」。本报告审核时点测试规模为 38 场景 29 断言，现行 40 场景 31 断言。
> 审核维度：**Godot 4.x 最佳实践与 GDScript 语法规范**（区别于既有 A 系列 SOLID 合规、B 系列业务逻辑正确性——本轮为新维度）。
> 方法：按 `docs/AUDIT_REVIEW_SOP.md` 执行——7 分区并行审核 + 主控交叉核验 + 判定分类 + 统一编号。
> 权威文档依据：`AGENTS.md`（本项目开发约定）、Godot 4.x 官方 GDScript 风格指南、Godot 4.6 运行时行为实测。
> 关联档案：本报告 C 系列发现已同步登记至 `docs/AUDIT_VAULT.md`。

---

## 1. 审计范围与方法

### 1.1 范围

- **生产代码**：`scripts/`（76 文件）+ `autoload/game_state.gd`（约 1.8 万行）。
- **场景**：`scenes/*.tscn`（10 个）+ 碰撞层/process_mode 配置核对。
- **测试**：`test/*.gd`（38 场景，29 断言 + 工具）。
- **配置/资源**：`project.godot`、`data/balance.json`、`data/translations.csv`、`assets/shaders/`。

### 1.2 分区

| 分区 | 覆盖文件 | 审核结果 |
| --- | --- | --- |
| 对局编排与全局状态 | main / game_state / balance_service / save_manager / sfx_player / entity_registry / back_navigator / camera_shake | 发现 C02/C03/C14-C19/C21/C25/C26 等 |
| 玩家与武器 | player / player_damage / player_dash / player_buff_visuals / bullet / bullet_pool / laser_weapon / aim_crosshair / aim_frame_layer | 发现 C04/C05/C20/C23/C24 |
| 刷怪与敌人 | spawner / enemy / enemy_pool / enemy_move_strategy / spawn_telegraph / explosion / starfield | 发现 C06/C07/C18 |
| Boss 体系 | boss / boss_attacks / boss_fire / boss_movement / enrage_sequence | 发现 C08-C11/C14 |
| 事件与演出 | mothership / summon_window / warp_gate / orbital_strike / 事件 / cinematic_fx / intro / return / dawn_station | 发现 C12/C13/C28 |
| UI | hud / buff_select / base_console / settings_ui / pause_ui / game_over_ui / start_panel / welcome_screen / exit_confirm / ui_theme / chamfered / segmented / buff_icons / start_radar / start_backdrop / tutorial | 发现 C01/C17/C19/C26/C27 |
| 测试 | test/*.gd | 发现 C29-C35 |

### 1.3 基线验证（审计证据）

- `godot --headless --import --path .`：**exit 0**，语法全绿。
- `godot --headless --path . --quit-after 300`：**无运行时错误**。
- Godot 3.x 残留 API（`yield`/`export var`/`onready`/字符串 `connect`/`emit_signal`）：**全库 0 处**。
- 热路径 `cfg()` 违规查询：**0 处**（唯一一次 `orbital_strike.gd:70` 属非热路径事件触发）。
- 碰撞层掩码与 AGENTS.md 四层约定：**自洽**。
- i18n：代码 `tr()` 键与 `translations.csv` 对齐（`I18N_NO_SUCH_KEY` 为 i18n 测试探针，属正常）。

---

## 2. 发现统计总览

| 严重度 | 生产代码 | 测试分区 | 合计 |
| --- | --- | --- | --- |
| 🔴 严重 | 2 | 0 | **2** |
| 🟠 中等 | 12 | 3 | **15** |
| 🟡 轻微 | 14 | 4 | **18** |
| **合计** | **28** | **7** | **35** |

按类别：

| 类别 | 数量 | 涉及编号 |
| --- | --- | --- |
| 纯 bug / 健壮性 | 7 | C02 C03 C11 C12 C13 C16 C29 |
| 规范违反（Godot 风格/项目约定） | 8 | C07 C08 C14 C17 C18 C20 C26 C35 |
| 性能隐患 | 8 | C05 C06 C09 C10 C23 C24 C27 C28 |
| 生命周期/信号/节点安全 | 5 | C04 C15 C21 C22 C25 |
| 协程泄漏 | 1 | C01 |
| 测试规范 | 6 | C30 C31 C32 C33 C34 C35 |

---

## 3. 发现清单（C 系列，已交叉核验）

### 3.1 🔴 严重

#### C01. 教程两处 `await get_tree().create_timer()` 协程泄漏（违反项目明令禁令）

- **位置**：`scripts/tutorial.gd:276`（`_pass_stage`）、`scripts/tutorial.gd:338`（`_open_base`）
- **类别**：协程泄漏
- **描述**：两处 `await get_tree().create_timer(1.0/1.2).timeout` 直接违反 `AGENTS.md` 第 196 行禁令（"延迟回调不要 await create_timer，进程退出时未完成的协程函数状态会泄漏"）。教程中途按 Esc `change_scene_to_file` 切换场景时，协程状态挂在树上不释放；若教程节点在等待期间被释放，`_advancing` 无法复位、`_close_base()` 不执行。
- **正确写法参照**：`pause_ui.gd:118-123` 的一次性 `Timer` 节点 + `timeout` 信号；`spawner.gd` 的 `_schedule()`。
- **修复指引**：两处各建一次性 `Timer`（`one_shot=true`），`add_child` 后连接 `timeout` 信号在回调里复位状态/关闭基地；Timer 随场景树释放。
- **验证方式**：`tutorial_test.tscn` + `--quit-after 300`。

#### C02. `load_profile()` 对 key_bindings 无类型守卫，手改档案可崩溃且静默丢字段

- **位置**：`autoload/game_state.gd:920-925`
- **类别**：纯 bug / 健壮性
- **描述**：`var saved_keys: Dictionary = parsed.get("key_bindings", {})` 若手改 profile 的 `key_bindings` 为数组/null，typed 赋值在运行期报错并**提前返回**，其后字段（difficulty/view_zoom/window_size/aim_assist/reduce_flash）全部不加载且 `profile_corrupt` 不置位。同函数其他字段均经 `DIFFICULTY_DEFS.has()`/`VIEW_ZOOM_LEVELS.has()` 等守卫，此处是唯一漏网。
- **修复指引**：`if parsed.get("key_bindings") is Dictionary:` 包裹，且对每 value 判 `is Array` 再遍历；非法则置 `profile_corrupt`。
- **验证方式**：`startup_flow_test`（损坏档案隔离）+ `base_system_test`。

### 3.2 🟠 中等

#### C03. `_apply_balance()` 只校验顶层类型，损坏 JSON 结构缺子键会 KeyError/除零

- **位置**：`autoload/game_state.gd:96-105`
- **类别**：纯 bug / 健壮性
- **描述**：只查 `diff is Dictionary and not diff.is_empty()`，不校验 easy/medium/hard 子键与字段；`milestones.base=[]` 时下游 `milestone_threshold` 除零。与"缺失/损坏时全部回退脚本默认值"的宣称仅一致于"顶层缺键"情形。仓内 balance.json 当前完整，属潜在风险。
- **修复指引**：为 `DIFFICULTY_DEFS` 校验三个子键齐全；`milestones.base` 判数组非空。
- **验证方式**：`balance_test`（损坏回退路径）。

#### C04. 子弹 Area2D 位移写在 `_process`（渲染帧）而非 `_physics_process`

- **位置**：`scripts/bullet.gd:171-208`（`_process` 整段）
- **类别**：生命周期 / 物理
- **描述**：Bullet 是 Area2D（物理对象），移动 `position += direction * speed * delta` 在渲染帧回调；Area2D overlap/`area_entered` 检测在物理步进中求值，渲染帧移动造成碰撞位置滞后。1800 px/s 下每物理步（1/60s）位移约 30px，远超弹/受击判定半径合计（约 5.2px），存在高速穿越判定盒风险。
- **修复指引**：`_process` → `_physics_process`，`activate()/deactivate()` 内 `set_process` → `set_physics_process`（L87/L95）。**注意：属高风险重构**（涉及对象池复用时序），需先跑 `pool_reuse_test` + `hit_logic_test` + `enemy_combat_test` 全量回归，确认命中率/弹幕行为无退化。
- **验证方式**：`pool_reuse_test` + `hit_logic_test` + `enemy_combat_test` + autoplay 短跑。

#### C05. 玩家 `_physics_process` 热路径直调 `sin()`（违反查表约定）

- **位置**：`scripts/player.gd:533,538`（无敌闪烁 alpha、碰撞点脉动）
- **类别**：性能 / 规范违反
- **描述**：`absf(sin(Time.get_ticks_msec() / 1000.0 * 20.0))` 等两处在 `_physics_process` 直调 sin，违反 AGENTS.md"禁止 _physics_process 直接调 sin/cos（用 Enemy.sin_fast/cos_fast 查表）"；且 `Time.get_ticks_msec()` 每帧取两次未缓存。
- **修复指引**：两处改用 `Enemy.sin_fast()` 查表；先缓存 `var t := Time.get_ticks_msec() * 0.001` 再复用。
- **验证方式**：`--quit-after 300` + 视觉截图（无敌闪烁观感不变）。

#### C06. 敌机每帧构造 9 键 ctx Dictionary + 3 次 view_world_rect()

- **位置**：`scripts/enemy.gd:391-401`（及 `:368,389,421`）
- **类别**：性能
- **描述**：每个活跃敌机每物理帧 `var ctx := {...}` 新建 10 字段字典传给策略，屏幕 20+ 敌机每秒上千次字典分配与 GC 压力；且同帧 3 次 `GameState.view_world_rect()`。
- **修复指引**：ctx 收敛为逐具名参数（`_strategy.update(delta, self, mdelta, view, speed)`）；`view_world_rect()` 每帧取一次后复用。
- **验证方式**：`perf_bench`（--fixed-fps 1000）对比 + `enemy_combat_test`。

#### C07. 星空背景硬编码 1920×1080

- **位置**：`scripts/starfield.gd:29-31,38-43`
- **类别**：规范违反
- **描述**：星点范围与回绕阈值写死 1920×1080，违反"一切屏幕边缘/刷怪位置逻辑必须走 view_world_rect()，不得写死分辨率"约定。当前 VIEW_ZOOM_LEVELS 均 ≥1.0（只放大不缩小）故暂不露馅，但未来加 zoom<1 档即露边。
- **修复指引**：用 `GameState.view_world_rect()`（或缓存其 size）生成星点范围与回绕边界。
- **验证方式**：`view_zoom_test` + 视觉截图（三档 zoom 星域无露边）。

#### C08. Boss 逃跑警告硬编码中文，未走 tr()

- **位置**：`scripts/boss.gd:869`
- **类别**：i18n 规范违反
- **描述**：`hud.show_warning("⚠ Boss 试图逃离战场 ⚠")` 硬编码中文绕过 `tr()`，英文环境显示中文横幅，违反"文案一律 tr(KEY)"约定。
- **修复指引**：改 `tr("BOSS_ESCAPE_WARNING")`，在 `translations.csv` 补 zh/en 两列，重新 import。
- **验证方式**：`i18n_test` + 英文环境运行截图。

#### C09. Boss 体系四处 `_physics_process` 直调 sin()

- **位置**：`boss_movement.gd:45`（下压）、`boss_attacks.gd:108,240`（telegraph 闪烁）、`enrage_sequence.gd:230`（猎杀闪烁）
- **类别**：性能 / 规范违反
- **描述**：四处每物理帧直调 `sin()`（EnrageSequence 141 行已正确用 `Enemy.sin_fast`，此处属同约定内漏网）。
- **修复指引**：统一改 `Enemy.sin_fast()`。
- **验证方式**：`boss_pattern_test` + `boss_enrage_test` + 视觉截图。

#### C10. `enrage_sequence._path_center` 每帧构建 5 元素数组

- **位置**：`scripts/enrage_sequence.gd:300-307`
- **类别**：性能
- **描述**：type3 在 ACTIVE 全程每帧 `var points: Array[Vector2] = [...]`，每轮狂暴数百次堆分配产生 GC 压力。
- **修复指引**：提为常量端点或直接对两端点 lerp 免中间数组。
- **验证方式**：`boss_enrage_test` + `perf_bench`。

#### C11. Boss P1→P2 段切换下压偏移残留（纯 bug）

- **位置**：`scripts/boss.gd:711-714`（`_enter_phase`）+ `scripts/boss_movement.gd:38-47`（`_update_press`）
- **类别**：纯 bug
- **描述**：`_update_press` 只在 `fight_phase() == FIGHT_P1` 被调用（boss_movement.gd:28）。若 P1→P2 切换恰落在纵向下压窗口内（1.6/6 ≈ 27% 相位点），`_press_offset` 保留非零值，P2 阶段不再调用 `_update_press`，机身以最大 80px 下压偏移永久留在锚线下方，直到 ENRAGE RETURN 才回到 fight_anchor_y。
- **修复指引**：`_enter_phase` 切换时复位 `_press_offset = 0`（或 `_movement` 提供 `reset_press()` 方法），并对 `position.y` 做一次锚线回正。
- **验证方式**：新增/扩展 `boss_phase_test` 断言 P2 进入后机身 y 与锚线一致。

#### C12. 返航过场镜头 7 面部推近时序错误（纯 bug）

- **位置**：`scripts/return_cinematic.gd:1357-1362`
- **类别**：纯 bug
- **描述**：`push_in := root.create_tween().set_parallel(true)` 下前置 `tween_interval(1.5*u)` 不产生延迟（并行组内 interval 只是组时长锚，不延迟并行成员），推近在镜头一开始即完成，人物尚未躺下。对照同镜头 `blink`（顺序 tween + 前置 interval）才是正确延迟写法。
- **修复指引**：改为顺序 tween 并对 scale/position 两属性用 `.parallel()`，或去掉 `set_parallel(true)` 改用 `.set_delay(1.5*u)`。
- **验证方式**：`return_cinematic_test` + 窗口模式截图逐镜头核对。

#### C13. CommOverlay 淡出 tween 竞态，新台词可能被残留 tween 立即淡没（纯 bug）

- **位置**：`scripts/comm_overlay.gd:80-86`
- **类别**：纯 bug
- **描述**：淡出在 `_hold_left<=0` 时 `create_tween()` 拉低 `_panel.modulate.a`，但 `show_line`（46-54）与 `clear`（58-64）均不 kill 该 tween。新台词恰落"淡出进行中"窗口（每周期约 1s）时，show_line 置 a=1.0 会被残留 tween 拖回 0 并触发 `_panel.hide()`，台词不可见。
- **修复指引**：缓存 `_fade_tween`，在 `show_line`/`clear` 中 kill。
- **验证方式**：`elite_turret_event_test` / `formation_strike_event_test` + autoplay 探针。

#### C14. 硬编码 960.0/±1600 世界坐标绕过 view_world_rect()

- **位置**：`main.gd:113,382`（蓄力虚影/特效）、`boss_attacks.gd:214,228,266`（冲刺预警线）、`boss_movement.gd:66`（strafe 方向）
- **类别**：规范违反
- **描述**：多处写死 960.0（相机中心）或 ±1600（预警线跨度），而 `main.gd:606` 已正确用 `view_world_rect().get_center().x`。当前 zoom=1 功能正确，zoom>1 时 ±1600 无法覆盖加宽可见区，预警线残缺。
- **修复指引**：统一用 `GameState.view_world_rect()` 换算中心与跨度。
- **验证方式**：`view_zoom_test` + 窗口截图三档 zoom。

### 3.3 🟡 轻微

| 编号 | 位置 | 类别 | 描述 | 修复指引 |
| --- | --- | --- | --- | --- |
| C15 | `main.gd:419,425` | 生命周期 | `await get_tree().process_frame` 缺 `is_inside_tree()` 守卫，main 首帧前被释放则 `_start_bgm` 对 freed 实例 add_child 报错 | await 后加 `if not is_inside_tree(): return` |
| C16 | `game_state.gd:886-887,929-930` | 纯 bug | `bool()` 字符串真值陷阱：手改存档写 "false"/"0" 字符串时转 true | 判 `v is bool` 后取值（与 save_num 同款） |
| C17 | `back_navigator.gd:22-31`、`welcome_screen.gd:33,101,121`、`pause_ui.gd:132` | 节点安全 | 多处 `get_parent().get_node("X")` 链式兄弟节点访问，无判空；未用唯一名 `%` | 改 `get_node_or_null` + 判空，或场景标 unique name 用 `%X` |
| C18 | `game_state.gd:67`、`spawner.gd:74`、`boss.gd:81-82`、`enemy.gd:72` | 类型 | 裸 `Array`/`Node` 未标元素/具体类型（milestone_base/UNLOCK_SCORES/STRAFE_SPEEDS/_pool） | 标 `Array[int]`/`EnemyPool` 等具体类型 |
| C19 | `main.gd:10-17`、`game_state.gd:31`、`hud.gd:46-53`、`tutorial.gd:10-11` 及遍布 | 可读性 | CONSTANT_CASE 命名用于可变 var（脚本回退默认值模式），与"UPPERCASE 仅常量"官方约定冲突 | 项目数据模式，**建议保持现状**或加注释说明"可变回退默认值"；不建议大范围改名 |
| C20 | `player_buff_visuals.gd:51`、`bullet.gd:218-233`、`enemy_move_strategy.gd:122` | 类型 | 弱类型返回/参数：裸 Array 返回、Area2D 上调 Enemy 专有方法、Node2D 上访问 Enemy 私有成员 | `-> Array[Node2D]`、`node as Enemy`、参数改 `enemy: Enemy` |
| C21 | `bullet_pool.gd:11-12` | 生命周期 | `_ready` 注册 `GameState.bullet_pool` 无 `_exit_tree` 清空，场景卸载后悬空 | 加 `_exit_tree` 中 `if GameState.bullet_pool == self: GameState.bullet_pool = null` |
| C22 | `player.gd:693-697`、`camera_shake.gd:11` | 信号 | `_exit_tree` 未断信号 / `_ready` 无条件 connect 无 `is_connected` 守卫，重入树会重复连接 | 加 `is_connected` 守卫或显式断开 |
| C23 | `laser_weapon.gd:76`、`boss_attacks.gd:106`、`enrage_sequence.gd:229`、`aim_frame_layer.gd:72` | 性能 | 每帧分配 `PackedVector2Array` / 每帧 `get_node_or_null("CollisionShape2D")` | 预分配 points 改写元素；shape 引用缓存 |
| C24 | `boss_fire.gd:56,84`、`enemy.gd:453-455`、`mothership.gd:561` | 性能 | 每次发射 `get_node("Polygon2D"/"MuzzleFlash")` 字符串节点查找 | `set_meta` 缓存或 `_muzzles` 数组同序缓存 |
| C25 | `main.gd:633-660,510,624` | 生命周期 | 返航/死亡/放弃终局路径不调 `_stop_charging()`，蓄力虚影/特效状态残留（恢复对局后首个 `_process` 自动清理，属瞬态） | 三条终局路径入口补 `_stop_charging()` |
| C26 | `start_panel.gd:122,128,134`、`base_console.gd:373` | i18n | 硬编码中文按钮文案/任务格式串绕过 tr() | 初始化直接 `tr("START_CONTINUE")` 等 key；任务格式提取 `BASE_MISSION_FMT` |
| C27 | `ui_chamfered_panel.gd:34`、`start_radar.gd:18` | 性能 | `_process` 轮询做内容自适应 / 隐藏期仍每帧 `queue_redraw` | 改信号驱动（resized/minimum_size_changed）；首行 `if not is_visible_in_tree(): return` |
| C28 | 演出类 `_process` 每帧重建点集（intro:1375 / warp_gate:150-158 / summon_window:303-314 / orbital_strike:162-190 / mothership:517-529） | 性能 | 短时一次性演出的每帧 `PackedVector2Array`/闭包分配，与 CinematicFx 零分配惯例不一致 | 预建单位点集，帧内仅写 scale/modulate |

### 3.4 测试分区

| 编号 | 位置 | 严重度 | 描述 | 修复指引 |
| --- | --- | --- | --- | --- |
| C29 | `enemy_combat_test.gd:187` | 中等 | 直读 `_exiting` 私有字段，`is_exiting()` 公开接口已存在（A7 清理残留） | 改 `life_e.is_exiting()` |
| C30 | `back_navigation_test.gd:128`、`keybind_test.gd:65,74` | 中等 | 直调 `_notification`/`_unhandled_input` 虚回调，绕过公开路由/输入管线 | 改 `nav.go_back()` / `Input.parse_input_event()`（对齐 esc_navigation_test 黑盒做法） |
| C31 | `tutorial_test.gd:160` | 轻微 | 直调 `_exit_tutorial()` 私有方法 | 注入 `ui_cancel` 动作或补公开 `quit_tutorial()` |
| C32 | `base_system_test.gd:81` | 轻微 | 直调 `_init_missions()`，无干净公开替代（reset_run 副作用过大） | 补公开 `reset_missions()` |
| C33 | `test/*.gd` 约 120 处 | 中等 | `await create_timer` 系统性偏离 AGENTS 协程约定（多数默认参数受 time_scale 影响；部分经 `_wait_real` 正确包装） | 统一收敛到 `_wait_real()` 包装；收尾即 quit 的用 `create_timer().timeout.connect(...)` |
| C34 | 多个测试 | 轻微 | 硬编码 balance.json 数值（改 JSON 漂移不报错）；`view_zoom_test:38` 硬编码 1920×1080 | 改读 `GameState.cfg()`/类常量；视口尺寸从 get_viewport 读取 |
| C35 | `meta_health_fx_test.gd:66-154` | 轻微 | `set_test_state` 字符串键直写私有字段，键名强耦合实现 | set_test_state 用无 `_` 前缀语义键或补公开 setter |

---

## 4. 判定分类（SOP 阶段二：先判类再决定修不修）

### 4.1 判定为"设计意图 / 可接受"（不修，档案回填）

| 项 | 判定 | 理由 |
| --- | --- | --- |
| C19 CONSTANT_CASE 可变 var | 项目数据模式 | CLAUDE.md/AGENTS.md 明文"脚本内同名 var 是回退默认值"，命名与数据模式绑定；大范围改名收益低风险高，**维持现状**，档案注明 |
| `buff_select.gd:157` child.free() | 合理立即释放 | 注释理由成立：stagger_open 紧接遍历 `_cards.get_children()`，queue_free 会新旧卡同帧共存 |
| `enemy.tscn` resource_local_to_scene + _ready duplicate | 约定正确姿势 | AGENTS.md:204 规定，setup 写 radius 有 tscn `resource_local_to_scene=true` 兜底，无共享污染 |
| `mothership.gd` 六处 get_first_node_in_group("hud") | 非热路径 | 全部事件驱动或一次性缓存（DESCEND 到位/进舱/警告/STAY/补给），无逐帧调用 |
| `boss_attacks.gd:70` 组件 boss 参数无类型 | A1/A3 文档化取舍 | 子组件以动态成员访问 Boss，属拆分时登记的取舍；对侧代理判定"可静态化"，主控判为**可优化但非必须**，列入 C20 级低优先 |
| `save_manager.gd` 非原子写 / `sfx_player.gd` 空池 | 可接受防御 | 单机存档依赖 quarantine 兜底；build_pool(6) 恒先调用，空池不可达 |
| 测试大量 create_timer | 泄漏影响有限 | 测试收尾即 quit，实际泄漏影响小；但 C33 仍记录系统性规范偏离，建议收敛 |

### 4.2 判定为"真 bug / 规范违反"（列入修复计划）

全部 C 系列除 4.1 所列外均为**确认修复**项。

---

## 5. 修复计划（SOP 阶段三~四：分批提交 + 即时验证）

> 遵循 `docs/AUDIT_REVIEW_SOP.md`：文档债与代码债分两批；每条修复后跑针对性测试；改动积累后全量回归。

### 批次 1：文档口径（先落地，独立提交）

1. `docs/AUDIT_VAULT.md`：追加 C 系列登记条目（本报告）。
2. `AGENTS.md`：若 C07（starfield 硬编码）、C08（i18n）等修复改变了约定落实范围，同步补记。
3. `translations.csv`：补 `BOSS_ESCAPE_WARNING`、`START_CONTINUE/NEW_GAME/TUTORIAL`、`BASE_MISSION_FMT` 的 zh/en 两列（C08/C26 配套），重新 import。

### 批次 2：严重缺陷（P0）

| 顺序 | 编号 | 文件 | 针对性验证 |
| --- | --- | --- | --- |
| 1 | C01 | tutorial.gd | tutorial_test + --quit-after 300 |
| 2 | C02 | game_state.gd | startup_flow_test + base_system_test |

### 批次 3：中等纯 bug（P1）

| 顺序 | 编号 | 文件 | 针对性验证 |
| --- | --- | --- | --- |
| 3 | C11 | boss.gd + boss_movement.gd | boss_phase_test（新增断言） |
| 4 | C12 | return_cinematic.gd | return_cinematic_test + 截图 |
| 5 | C13 | comm_overlay.gd | elite_turret_event_test + formation_strike_event_test |
| 6 | C03 | game_state.gd | balance_test |
| 7 | C16 | game_state.gd | startup_flow_test |

### 批次 4：中等规范/性能（P2）

| 顺序 | 编号 | 文件 | 针对性验证 |
| --- | --- | --- | --- |
| 8 | C04（高风险） | bullet.gd | pool_reuse + hit_logic + enemy_combat + autoplay |
| 9 | C05/C09 | player.gd + boss 组件 | --quit-after 300 + 截图 |
| 10 | C06 | enemy.gd | perf_bench + enemy_combat |
| 11 | C07 | starfield.gd | view_zoom_test + 截图 |
| 12 | C08/C26 | boss.gd + start_panel + base_console | i18n_test + 双语截图 |
| 13 | C10 | enrage_sequence.gd | boss_enrage_test |
| 14 | C14 | main.gd + boss_attacks + boss_movement | view_zoom_test + 截图 |

### 批次 5：轻微（P3，可分批合提）

- C15/C17/C21/C22/C25（生命周期/节点安全）→ `--quit-after 300` + 相关专项。
- C18/C20（类型）→ `--headless --import` + 全量测试。
- C23/C24/C27/C28（性能）→ perf_bench 对比 + 相关专项。
- C29-C35（测试规范）→ 对应专项测试。

### 批次 6：全量回归（收尾）

```
godot --headless --import --path .
godot --headless --path . --quit-after 300
# 29 个断言场景全绿 0 FAIL
godot --headless --path . res://test/autoplay_test.tscn -- --autoplay-seconds=120
```

---

## 6. 合规亮点（本次审计确认符合规范的部分）

- **Godot 3.x 残留 API 全库清零**：无 `yield`/`export var`/`onready`/字符串 `connect`/`emit_signal`。
- **信号体系规范**：全部 `signal.emit()` Callable 语法、带类型参数（40 个信号，19 个无参语义合理）、无重复连接隐患（除 C22 两处）。
- **对象池防护完整**：`_repooling` 包裹 reparent 防 `_exit_tree` 误清、`_active` 延迟守卫防过期延迟覆盖、`_free.has` 幂等释放、注册表登记/注销成对。
- **热路径合规**：子弹/敌机走对象池与注册表，无每帧 `get_nodes_in_group`（B8 修复生效）、无热路径 `cfg()`、三角函数查表绝大部分覆盖（仅 C05/C09 六处漏网）。
- **world_scale 幂等赋值**：设计值 × 全局缩放，无 `*=` 累乘；`enemy.tscn` 共享 shape 已 local_to_scene。
- **协程纪律**：生产代码零 `create_timer` 协程（C01 为唯一违反，且位于 tutorial）；演出/事件/池全部 Timer 节点 + 信号。
- **B 系列修复未复发**：B1（aim_line 泄漏）、B5（FIRE_INTERVALS 共享污染）、B6（world_scale 回退）、B8（spread 注册表遍历）、B13（abort 清台词）均确认完好。

---

## 7. 结论

本项目 **Godot 4.x 最佳实践与语法规范合规度整体高**：

- 无危险级运行时崩溃/语法错误；基线 `--headless --import` 与 `--quit-after 300` 全绿。
- **2 项严重**（教程协程泄漏 C01、档案类型守卫 C02）、**15 项中等**、**18 项轻微**，共 **35 项**（生产 28 / 测试 7）。
- **纯 bug 6 项**（C02/C03/C11/C12/C13/C16）中，Boss 下压偏移残留（C11）、返航推近时序（C12）、CommOverlay 淡出竞态（C13）为本轮最有价值的运行期缺陷发现，其余为健壮性缺口。
- 性能类发现（C05/C06/C09/C10/C23/C24/C27/C28）均属"每帧小分配/直调 sin"，绝对影响小，但部分违反项目自身零分配/查表约定，建议按 P2 分批收口。
- 测试分区主要残留为白盒私有访问（C29-C32，其中 C29/C30 有明确公开替代）与 create_timer 系统性偏离（C33）。

> **档案联动**：本报告 C 系列已登记至 `docs/AUDIT_VAULT.md`；修复落地后按档案约定回填"修复起效记录"并更新状态总览。

---

*审核人：Claude Code（依据用户指示执行） · 审核时间：2026-08-01*
