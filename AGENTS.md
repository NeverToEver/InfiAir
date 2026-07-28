# AGENTS.md

## 项目概览

InfiAir（无限空域）是一个单机 2D 俯视空战射击游戏，使用 **Godot 4.6 + GDScript** 实现，采用 GL Compatibility 渲染器。项目重制自相邻目录的 Python/Pygame 项目 `../airwar-game`；本仓库运行时不依赖该目录。

核心对局循环为：自动射击与波次刷怪 → 分数里程碑 Buff 三选一 → 3 类 Boss 轮换及狂暴阶段 → 母舰补给/火力平台 → 返航基地中场整备 → 回到同一局继续。游戏为纯得分制，没有掉落或拾取物。

- 项目入口：`project.godot` 的 `run/main_scene = res://scenes/main.tscn`。
- 设计视口：1920×1080，`canvas_items` 拉伸，`keep` 宽高比。
- 唯一 autoload：`GameState`（`autoload/game_state.gd`），负责全局状态、信号、数值读取、持久化、音效池与实体注册表。
- 用户界面和主要文档以中文为主；新增游戏文本必须保持中英双语。
- 玩法对齐状态和已知差异见 `docs/PORTING_PARITY.md`；返回/退出行为见 `docs/EXIT_FLOW.md`；未来方向与阶段计划见 `docs/ROADMAP.md`。
- `CLAUDE.md` 只提供入口级概览并声明本文件为权威约定文档；两者冲突时以本文件为准。

## 技术栈、配置与交付现状

### 技术栈

- **引擎：** Godot 4.6（标准版即可，无需 .NET）。`project.godot` 当前声明 `4.6` 和 `GL Compatibility` 特性，桌面/移动端均使用 `gl_compatibility`。
- **语言：** 纯 GDScript；`scripts/tools/` 下的 Python 文件（`generate_audio.py`、`generate_enemy_sprites.py`）是离线资产生成工具，不属于游戏运行时依赖。
- **资源：** `assets/sprites/` PNG、`assets/audio/` WAV、`assets/fonts/NotoSansSC.ttf` UI 字体。
- **数据：** `data/balance.json` 为可调数值源（顶层分区：player、enemies、elites、boss、spawner、mothership、buffs、milestones、difficulty、effects、tutorial、elite_turret_event、formation_strike_event）；`data/translations.csv` 是中英文本源，`.translation` 文件由 Godot 导入生成并在运行时加载。

### 关键配置文件

| 文件 | 用途 |
| --- | --- |
| `project.godot` | Godot 项目名、入口场景、唯一 autoload、视口/拉伸、输入映射与渲染器。优先用 Godot 编辑器修改。 |
| `data/balance.json` | 玩家、敌机、Boss、刷怪、Buff、母舰、难度、特效、教程等可调参数。Boss 段含 `phases`（阶段模式表/telegraph/各型 P2 攻击参数）、`enrage.type_*`（三型差异化狂暴）与 `difficulty_scaling`（弹数/间隔/弹速三档分档表）。 |
| `data/translations.csv` | 翻译键及 `zh`、`en` 文本源。 |
| `.gitignore` | 忽略 `.godot/`、导入的 `*.translation`、本地 IDE 文件、导出预设和导出产物。 |
| `run.sh` / `run.command` / `run.bat` | macOS/Linux/Windows 的本地启动包装；`run.sh` 依次查找 PATH、`~/.local/bin/godot` 和 macOS App bundle，并对低于 4.6 的版本告警（仅警告不阻断）。 |

当前**未发现** `package.json`、`pyproject.toml`、`requirements*.txt`、`Cargo.toml`、`go.mod`、Makefile、Docker/Compose 配置、CI 工作流或 `export_presets.cfg`。因此没有包安装、构建、CI、自动部署或可复现导出流程；打包发布目前暂缓。不要虚构这些流程或为常规修改引入第三方插件/依赖。

## 本地运行与验证

在项目根目录运行。当前开发机可使用 `~/.local/bin/godot`；若 `godot` 已在 PATH 中，也可以直接替换命令。`./run.sh` 会自动定位引擎。

```bash
# 本地运行
./run.sh
godot --path .

# 资源导入与脚本解析
godot --headless --import --path .

# 启动主场景并运行 300 帧
godot --headless --path . --quit-after 300

# 最小必跑主流程冒烟测试
godot --headless --path . res://test/smoke_test.tscn

# 存档、RP、任务、基地整备数据层
godot --headless --path . res://test/base_system_test.tscn
```

推荐的最小验证集为：`--headless --import`、`--quit-after 300`、`smoke_test.tscn`。涉及存档、基地或母舰时额外运行 `base_system_test.tscn`；涉及对应子系统时运行下列专项场景。

```bash
# 对局机制与配置
godot --headless --path . res://test/enemy_combat_test.tscn
godot --headless --path . res://test/buff33_test.tscn
godot --headless --path . res://test/difficulty_test.tscn
godot --headless --path . res://test/boss_enrage_test.tscn
godot --headless --path . res://test/boss_phase_test.tscn
godot --headless --path . res://test/boss_pattern_test.tscn
godot --headless --path . res://test/hit_logic_test.tscn
godot --headless --path . res://test/balance_test.tscn
godot --headless --path . res://test/elite_turret_event_test.tscn
godot --headless --path . res://test/formation_strike_event_test.tscn

# 设置、启动、导航与教程
godot --headless --path . res://test/keybind_test.tscn
godot --headless --path . res://test/i18n_test.tscn
godot --headless --path . res://test/view_zoom_test.tscn
godot --headless --path . res://test/window_size_test.tscn
godot --headless --path . res://test/startup_flow_test.tscn
godot --headless --path . res://test/back_navigation_test.tscn
godot --headless --path . res://test/esc_navigation_test.tscn
godot --headless --path . res://test/intro_cinematic_test.tscn
godot --headless --path . res://test/tutorial_test.tscn

# 对象池与性能
godot --headless --path . res://test/pool_reuse_test.tscn
godot --headless --fixed-fps 1000 --path . res://test/perf_bench.tscn

# 自动游玩异常探针（默认真实时间 480 秒；不是普通断言测试）
godot --headless --path . res://test/autoplay_test.tscn -- --autoplay-seconds=480 --seed=20260722
```

无头模式的帧率不等同于真实时间；依赖计时的测试应等待真实计时器/物理帧，参考现有测试实现。视觉测试不能使用 headless dummy 渲染器：

```bash
# 窗口模式：游戏画面，输出 /tmp/infiair_capture.png
godot --path . res://test/visual_capture.tscn

# 窗口模式：UI 页面，输出 /tmp/ui_*.png
godot --path . res://test/ui_capture.tscn

# 窗口模式：返航过场逐镜头（8s/镜头拉长时轴），输出 /tmp/return_shot*.png
godot --path . res://test/return_capture.tscn
```

## 运行时架构

`scenes/main.tscn` 是主节点树和对局容器：

```text
Main (scripts/main.gd)
├─ Starfield / Camera2D
├─ Player
├─ Spawner
├─ BulletPool / EnemyPool
├─ HUD
├─ BuffUI / PauseUI / SettingsUI / GameOverUI / BaseUI
├─ StartPanel / WelcomeScreen / ExitConfirm
├─ BackNavigator
├─ IntroCinematic（运行时由 main 在新游戏时实例化，layer=35）
├─ ReturnCinematic（运行时由 main 在返航时实例化，layer=35）
└─ EliteTurretEvent（运行时由 main 在 _ready 创建并登记给 spawner 互斥）
└─ FormationStrikeEvent（运行时由 main 在 _ready 创建并登记给 spawner；最低优先级随机事件）
```

- `scripts/main.gd`：对局编排，串联刷怪、里程碑、Boss、母舰召唤、返航、放弃对局、BGM 与页面流转。
- `autoload/game_state.gd`：全局分数、HP、Buff、难度、RP、任务、路线、设置和信号总线；加载数值/翻译；维护 `GameState.enemies`、`player_ref`、`player_hitbox`、对象池引用；读写本地存档。
- `scripts/player.gd`：WASD 移动、鼠标瞄准、自动开火、燃料加速、微调、相位冲刺和受击处理。
- `scripts/spawner.gd`：普通/精英敌机选择、波次与 Boss 调度。普通波次当前直接实例化 `enemy.tscn`；Boss-3 生成的小怪使用 `GameState.enemy_pool.spawn()`。不要把“所有敌机已经池化”当成当前事实。
- `scripts/enemy.gd`、`mothership.gd`、`bullet.gd`、`laser_weapon.gd`：可实例化战斗实体和武器行为。
- `scripts/boss.gd`：Boss 实体，HP 阶段模式表驱动（P1/P2/ENRAGE，模式表 `boss.phases.typeN` + telegraph 前摇），三型差异化狂暴（`boss.enrage.type_*`，狂暴期玩家减速 ×0.35 而非定身），难度分档在 `_ready` 一次性乘算（`boss.difficulty_scaling`）。设计/实施记录见 `docs/BOSS_REDESIGN.md`。
- `scripts/bullet_pool.gd`、`enemy_pool.gd`、`explosion.gd`、`starfield.gd`、`camera_shake.gd`、`spawn_telegraph.gd`：对象复用与表现层。
- `scripts/hud.gd`、`buff_select.gd`、`base_console.gd`、`settings_ui.gd`、`pause_ui.gd`、`game_over_ui.gd`、`start_panel.gd`、`welcome_screen.gd`、`exit_confirm.gd`：页面和覆盖层。
- `scripts/intro_cinematic.gd`：开场过场导演（6 镜头，新游戏触发，设计文档 `docs/INTRO_CINEMATIC.md`）；播放时树暂停，Esc 经 BackNavigator `SKIP_INTRO` 路由、任意键/点击由过场自身捕获跳过，播完/跳过统一走 `finished` 恢复。
- `scripts/return_cinematic.gd` + `scripts/dawn_station.gd`：返航过场导演（7 镜头，长按 B 返航触发，设计文档 `docs/RETURN_HOME_CINEMATIC.md`）；架构镜像开场，Esc 经 `SKIP_RETURN` 路由，播完/跳过统一走 `finished` 落基地 UI（树保持暂停，镜头 7 渐暗期 BGM 淡出到 -40dB）。`DawnStation` 是「曙光」站体共享静态工厂（毁灭态/全息虚影态），开场镜头 1、返航镜头 2/3/4 与后续基地背景层复用。
- `scripts/elite_turret_event.gd`、`strike_carrier.gd`、`turret_battery.gd`（+ `scenes/turret.tscn`）、`comm_overlay.gd`：精英炮塔事件（设计/实现文档 `docs/ELITE_TURRET_EVENT.md`）——事件状态机与 Boss 互斥（`_boss_frozen`/`_boss_pending`/`_waves_paused` 钩子在 spawner）、打击航母导演、炮台实体（弱锁定索敌，注册 `enemy` 组与 `GameState.enemies`）、左下通讯浮层。
- `scripts/formation_strike_event.gd`、`formation_craft.gd`、`formation_bomb.gd`：轰炸编队事件（设计/实现文档 `docs/FORMATION_STRIKE_EVENT.md`）——最低优先级随机事件（不冻结 Boss、不暂停波次，可被返航 `abort()` 打断）、编队锚点/楔形偏移由事件 `_process` 驱动、编队战机（注册 `enemy` 组与 `GameState.enemies`）、引信制下落炸弹（预警环随引信收缩，AoE 只伤玩家）。
- `scripts/back_navigator.gd`：PC Esc/手柄 `ui_cancel`/Android 返回的统一路由。教程是独立场景 `scenes/tutorial.tscn`，由 `scripts/tutorial.gd` 自己处理返回。

`scenes/` 包含主场景、玩家、普通敌机、Boss、子弹、母舰、开场/返航过场和教程场景；同名行为脚本通常位于 `scripts/`。所有动态对局实体应挂在 Main 下，以便清场逻辑和测试遍历可见。

## 目录职责

| 路径 | 内容与职责 |
| --- | --- |
| `autoload/` | 全局 autoload；当前只有 `game_state.gd`。 |
| `scenes/` | Godot `.tscn` 场景与节点组合。 |
| `scripts/` | GDScript 游戏逻辑、UI、表现和池实现。 |
| `scripts/tools/` | 离线工具；`generate_audio.py` 可重新生成已提交的 WAV，`generate_enemy_sprites.py` 可重新生成敌方单位贴图（PIL，晶体棱镜风格）。 |
| `assets/` | 游戏贴图、音效/BGM 和字体。 |
| `data/` | 运行时数值配置和翻译资源源文件。 |
| `test/` | 以 `.tscn + .gd` 实现的无头场景自检、性能基准、自动游玩和截图工具。 |
| `docs/` | 移植对齐、退出流程、审计计划、路线图（ROADMAP）与截图。 |

## 开发约定

### GDScript 与场景生命周期

- 遵循 Godot 4 官方风格：**Tab 缩进**、类型标注、`CONSTANT_CASE` 常量、私有成员前缀 `_`、`signal_name.emit()` / `signal_name.connect()` 信号语法。
- `setup()` 会在实体被加入场景、执行 `_ready()` 之前调用。此阶段不要依赖 `@onready` 缓存；改用 `$节点路径` 访问子节点。
- 不要修改既有 autoload 或输入映射来完成无关需求。现有输入由 `project.godot` 定义，包括移动、`boost`（Shift）、`fine_move`（Ctrl）、`dash`（Space）、`dock`（H）、`homecoming`（B）、`give_up`（K）和 `restart`（R）。
- 教程进入时会隔离对局状态和存档，离开时必须恢复 `Engine.time_scale = 1`。运行期创建的节点要保存引用，不能依赖 Godot 自动生成的节点名。
- 延迟回调不要 `await get_tree().create_timer()` 或挂起在任何计时器上的协程：进程退出时未完成的协程函数状态会泄漏，并连带其引用的资源（贴图/音频）。改用一次性 `Timer` 节点 + 信号连接（参考 `spawner.gd` 的 `_schedule()`），Timer 随场景树释放。

### 数值与配置

- **可调游戏数值只修改 `data/balance.json`，不要仅修改脚本回退值。** 脚本内的同名默认值用于缺键/损坏 JSON 回退，新增或调整数值时应保持两者一致。
- 统一使用 `GameState.cfg("player.fuel.drain", default)` 查询嵌套配置。高频 `_process`/`_physics_process` 路径必须在 `_ready()` 或初始化阶段读取并缓存，不要每帧查 JSON 字典。
- `GameState` 在启动时加载 `balance.json`，并对缺失或无法解析的配置使用脚本默认值。

### 碰撞、伤害与视角

- 逻辑碰撞层约定：1=`player`、2=`player_bullet`、3=`enemy`（含 Boss）、4=`enemy_bullet`。玩家子弹以 `enemy` 组结算；敌方子弹和敌方实体以 `player_hitbox` 组结算。
- 玩家受击只使用 `Player/Hitbox` 的 Area2D（半径 7）。`CharacterBody2D` 本体的半径 22 圆没有碰撞用途（mask 为 0），不得用于受击判定。
- 子弹使用 `scenes/bullet.tscn`，由 `setup()` 区分阵营。爆炸应使用 `Explosion.spawn_at()`，而非为每次爆炸随意构建新的粒子方案。
- 视角缩放和窗口尺寸是相互独立的 profile 设置。相机固定在 `(960, 540)` 并只调整 `zoom`；所有屏幕边缘、出界、刷怪和可见区域计算必须使用 `GameState.view_world_rect()`，不要硬编码 `0..1920` 或 `0..1080`。

### UI、文本与导航

- 所有用户可见文本使用 `tr("UPPER_SNAKE_CASE_KEY")`。新增键必须同步写入 `data/translations.csv` 的 `zh` 和 `en` 列；让 Godot 重新导入后生成 `.translation`。动态文本使用带 `%d`/`%s` 占位符的翻译键。
- 语言切换必须经 `GameState.set_locale("zh" / "en")`，并使 UI 监听 `locale_changed` 后刷新文本。
- 页面样式使用 `scripts/ui_theme.gd`：色板 token、字号阶梯、`make_label()`、`make_button()`、`make_toggle_button()`、`make_section_header()`、`make_page_shell()` 和开场动画工具；可复用构件还有 `scripts/ui_chamfered_panel.gd`（切角面板）与 `scripts/ui_segmented_bar.gd`（分段条形仪表）。新页面以 `make_page_shell()` 组合，单页最多一个 primary 主按钮；不要散落手写色值和重复 Label/Button 样板。
- Buff、暂停、结算等暂停态 UI 必须设置 `process_mode = Always`，并通过 `get_tree().paused` 管理暂停。
- 返回/退出集中在 `BackNavigator`。除设置页的改键捕获态外，页面不要自行消费 `ui_cancel`；新增页面层级必须在 `decide_back_action()` 中登记，并同步 `docs/EXIT_FLOW.md`。
- BGM 循环只设置 `stream.loop_mode = LOOP_FORWARD`；不要显式设置 `loop_begin`/`loop_end` 或在 `_exit_tree()` 停止 BGM，否则可能造成播放实例泄漏。

### 性能与对象生命周期

- 子弹生产统一使用 `GameState.bullet_pool.fire()`；外部 `queue_free()` 后的池引用清理由子弹退出树逻辑处理。
- 修改对象池时必须保留 `_active` 与 `_repooling` 防护。Godot 4.6 的 `reparent()` 会触发 `_exit_tree()`；回收 reparent 必须由 `_repooling` 包裹，否则 `forget()` 会将对象错误地从空闲池移除。修改后运行 `test/pool_reuse_test.tscn`。
- 敌机存在直接实例化和对象池两条当前路径（见“运行时架构”）。池化实体的 `reactivate()`/`deactivate()` 负责状态重置、注册表和死亡信号；不要把池对象外部随意释放或绕过其生命周期。
- 热路径不能反复 `get_nodes_in_group()`；使用 `GameState.enemies`、`GameState.player_ref` 和 `GameState.player_hitbox` 注册表。`Enemy` 移动计算使用 `Enemy.sin_fast()` / `Enemy.cos_fast()` 的查表实现，避免在 `_physics_process()` 直接调用三角函数。
- HUD 仪表类轮询按约 0.1 秒节流，且只在文本/格子值变化时更新布局；优先通过 `GameState` 信号驱动状态更新。

## 测试策略与副作用

测试不是单元测试框架；每个 `test/*.tscn` 启动相应 GDScript 场景，并以 `[PASS]`/`[FAIL]` 输出和退出码自检。`test/` 下共 28 个场景：23 个断言场景，外加 `autoplay_test`（探针）、`perf_bench`（性能基准）、`visual_capture` / `ui_capture` / `return_capture`（窗口模式截图工具）。

- 测试可能读写 `user://savegame.json` 与 `user://profile.json`。新测试应先 `GameState.delete_save()`，并在结束时清理或恢复自己创建的持久化状态，保证可重复执行。
- `test/balance_test.gd` 会暂时**覆盖项目内** `data/balance.json` 来验证损坏和回退路径，然后恢复原文件。不要在手工编辑该文件时并发运行它，也不要中断它后假设文件仍然完好。
- `test/autoplay_test.tscn` 是长时自动游玩与 `[ANOMALY]` 不变量监控探针，不以常规断言失败形式代表所有问题。
- `test/perf_bench.tscn` 必须带 `--fixed-fps 1000`；无头默认帧率行为不适合直接比较纯帧耗时。做性能 A/B 时交错运行并使用中位数。
- 修改 UI 后使用窗口模式截图人工核对；headless 不会输出可用游戏截图。

## 持久化与安全边界

- 对局存档为 `user://savegame.json`，局外档案为 `user://profile.json`；二者由 `GameState` 管理并带版本字段。profile 保存最高分、难度、键位、语言、视角、窗口尺寸、欢迎页/教程状态等。
- 损坏 JSON 会被隔离为 `<file>.corrupt`，并通过 `save_corrupt`/`profile_corrupt` 标记通知开始界面。不要绕过该恢复流程。
- 当前未发现网络通信、第三方插件、远程服务、密钥或凭据文件。除本地 `user://` 持久化和离线资源生成外，游戏没有外部交互。
- `.gitignore` 排除导入缓存、导出预设和导出目录；若未来增加正式导出/CI，先补齐可审查的预设、构建命令和发布说明，再把它写入本文件。

## 文档同步要求

- 调整玩法、移植差异、对齐状态或验收口径时，更新 `docs/PORTING_PARITY.md` 的对应条目。
- 调整项目方向、阶段计划或暂缓/重启决策时，更新 `docs/ROADMAP.md`（方向类决策的单一事实源）。
- 调整页面返回层级、退出清理或平台返回处理时，更新 `docs/EXIT_FLOW.md` 并运行返回导航测试。
- 修改工程结构、运行命令、测试策略、配置位置或本文件所述约定时，同步维护本 `AGENTS.md`，使其保持面向首次接手项目的代理的真实入口文档。
