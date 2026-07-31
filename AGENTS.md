# AGENTS.md

## 项目概览

InfiAir（无限空域）是一个单机 2D 俯视空战射击游戏，使用 **Godot 4.6 + GDScript** 实现，采用 GL Compatibility 渲染器。项目早期重制自 Python/Pygame 项目 `airwar-game`，经大规模扩展开发后已脱离原作框架独立演进；原作仅作历史参考（溯源见 `docs/archive/PORTING_PARITY.md`），本仓库运行时不依赖原作目录。

核心对局循环为：自动射击与波次刷怪 → 分数里程碑 Buff 三选一 → 3 类 Boss 轮换及狂暴阶段 → 母舰补给/火力平台 → 返航基地中场整备 → 回到同一局继续。游戏为纯得分制，没有掉落或拾取物。

- 项目入口：`project.godot` 的 `run/main_scene = res://scenes/main.tscn`。
- 设计视口：1920×1080，`canvas_items` 拉伸，`keep` 宽高比。
- 唯一 autoload：`GameState`（`autoload/game_state.gd`），负责全局状态、信号、数值读取、持久化、音效池与实体注册表。
- 用户界面和主要文档以中文为主；新增游戏文本必须保持中英双语。
- 返回/退出行为见 `docs/EXIT_FLOW.md`；未来方向与阶段计划见 `docs/ROADMAP.md`；移植时期的对齐记录与迭代历史已归档为 `docs/archive/PORTING_PARITY.md`（冻结，不再维护）。
- `CLAUDE.md` 只提供入口级概览并声明本文件为权威约定文档；两者冲突时以本文件为准。

## 技术栈、配置与交付现状

### 技术栈

- **引擎：** Godot 4.6（标准版即可，无需 .NET）。`project.godot` 当前声明 `4.6` 和 `GL Compatibility` 特性，桌面/移动端均使用 `gl_compatibility`。
- **语言：** 纯 GDScript；`scripts/tools/` 下的 Python 文件（`generate_audio.py`、`generate_enemy_sprites.py`、`generate_player_sprite.py`、`generate_mothership_sprite.py`）是离线资产生成工具，不属于游戏运行时依赖。
- **资源：** `assets/sprites/` PNG、`assets/audio/` WAV、`assets/fonts/NotoSansSC.ttf` UI 字体。
- **数据：** `data/balance.json` 为可调数值源（顶层分区：world_scale、player、enemies、elites、boss、spawner、mothership、buffs、milestones、difficulty、progression、effects、tutorial、elite_turret_event、formation_strike_event）；`data/translations.csv` 是中英文本源，`.translation` 文件由 Godot 导入生成并在运行时加载。

### 关键配置文件

| 文件 | 用途 |
| --- | --- |
| `project.godot` | Godot 项目名、入口场景、唯一 autoload、视口/拉伸、输入映射与渲染器。优先用 Godot 编辑器修改。 |
| `data/balance.json` | 玩家、敌机、Boss、刷怪、Buff、母舰、难度、特效、教程等可调参数。Boss 段含 `phases`（阶段模式表/telegraph/各型 P2 攻击参数）、`enrage.type_*`（三型差异化狂暴）与 `difficulty_scaling`（弹数/间隔/弹速三档分档表）。 |
| `data/translations.csv` | 翻译键及 `zh`、`en` 文本源。 |
| `.gitignore` | 忽略 `.godot/`、导入的 `*.translation`、本地 IDE 文件和导出产物（`builds/` 等；`export_presets.cfg` 自 2026-07-30 起入库）。 |
| `run.sh` / `run.command` / `run.bat` | macOS/Linux/Windows 的本地启动包装；`run.sh` 依次查找 PATH、`~/.local/bin/godot` 和 macOS App bundle，并对低于 4.6 的版本告警（仅警告不阻断）。 |
| `export_presets.cfg` | Linux/X11 与 Windows Desktop 导出预设（嵌入 pck 单文件，x86_64）。需本机安装匹配版本的 Godot 导出模板。 |
| `release.sh` | 发布构建：资源导入 → 双平台导出 → 打包到 `builds/release/`（`VERSION` 环境变量指定版本号）。 |

当前**未发现** `package.json`、`pyproject.toml`、`requirements*.txt`、`Cargo.toml`、`go.mod`、Makefile、Docker/Compose 配置或 CI 工作流。打包发布已重启（2026-07-30）：`export_presets.cfg` 入库（Linux/X11 + Windows Desktop，嵌入 pck 单文件 exe/二进制），根目录 `release.sh` 一键完成导入 → 双平台导出 → 打包（产物 `builds/release/`，版本号由 `VERSION` 环境变量指定）；`packaging/linux/`（用户态 install.sh / uninstall.sh[--purge] / infiair.desktop）与 `packaging/windows/`（per-user install.bat / uninstall.bat[/purge]，开始菜单快捷方式）随包分发。不要虚构 CI/自动部署流程或为常规修改引入第三方插件/依赖。

**发布工程现状（2026-07-31 更新）**：导出模板已安装（`~/Library/Application Support/Godot/export_templates/4.6.2.stable/`），`./release.sh` 已跑通，产物在 `builds/release/`（`InfiAir-<版本>-linux-x86_64.tar.gz` / `-windows-x86_64.zip`，均为嵌入 pck 单文件 + 安装/卸载脚本，本机 gitignore）。**产物以 GitHub Releases 附件分发（不入库）**：`gh release create v<版本> builds/release/InfiAir-<版本>-*.{tar.gz,zip}`。macOS 本机无法运行 Linux/Windows 二进制，安装脚本与实机运行需在对应平台验证。模板包若需重下（1.17 GB）：慢网络下可用 16 路 HTTP Range 并行分块 + 断点续传（签名直链 1 小时过期、curl 须带 `--speed-time/--speed-limit` 防挂死）。

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
godot --headless --path . res://test/wave_pacing_test.tscn
godot --headless --path . res://test/buff33_test.tscn
godot --headless --path . res://test/buff_visuals_test.tscn
godot --headless --path . res://test/difficulty_test.tscn
godot --headless --path . res://test/boss_enrage_test.tscn
godot --headless --path . res://test/boss_phase_test.tscn
godot --headless --path . res://test/boss_pattern_test.tscn
godot --headless --path . res://test/hit_logic_test.tscn
godot --headless --path . res://test/balance_test.tscn
godot --headless --path . res://test/elite_turret_event_test.tscn
godot --headless --path . res://test/buff_panel_test.tscn
godot --headless --path . res://test/formation_strike_event_test.tscn
godot --headless --path . res://test/orbital_strike_test.tscn
godot --headless --path . res://test/mothership_summon_test.tscn
godot --headless --path . res://test/meta_health_fx_test.tscn

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

# 窗口模式：开场过场逐镜头（8s/镜头拉长时轴），输出 /tmp/intro_shot*.png
godot --path . res://test/intro_capture.tscn

# 窗口模式：母舰召唤全序列（蓄力/小窗/穿梭门/牵引/驻留），输出 /tmp/summon_*.png
godot --path . res://test/summon_capture.tscn

# 窗口模式：Meta HUD 血量/受击反馈各血量档，输出 /tmp/meta_fx_*.png
godot --path . res://test/meta_fx_capture.tscn

# 窗口模式：HUD 常态/极端（全 buff 满层）布局，输出 /tmp/hud_*.png
godot --path . res://test/hud_capture.tscn
```

## 运行时架构

`scenes/main.tscn` 是主节点树和对局容器：

```text
Main (scripts/main.gd)
├─ Starfield / Camera2D
├─ Player
├─ Spawner
├─ BulletPool / EnemyPool
├─ HUD（layer=2：为 MetaHealthFX 让出「世界之上、HUD 之下」的 layer=1）
├─ BuffUI / PauseUI / SettingsUI / GameOverUI / BaseUI
├─ StartPanel / WelcomeScreen / ExitConfirm
├─ BackNavigator
├─ MetaHealthFX（运行时由 main 在 _ready 创建，layer=1，Meta HUD 血量/受击全屏后处理）
├─ AimFrameLayer（运行时由 main 在 _ready 创建，世界坐标，辅助瞄准标记敌 bracket 框覆盖层，登记 GameState.aim_frame_layer）
├─ IntroCinematic（运行时由 main 在新游戏时实例化，layer=35）
├─ ReturnCinematic（运行时由 main 在返航时实例化，layer=35）
├─ OrbitalStrike（运行时由 main 在继续出击时实例化，layer=24，轨道打击清场动画）
├─ MothershipSummonWindow（运行时由 main 在母舰蓄力完成时实例化，layer=24，机库小窗演出）
├─ WarpGate（运行时由 main 在小窗结束时实例化于母舰停驻点，世界坐标，穿梭门特效）
└─ EliteTurretEvent（运行时由 main 在 _ready 创建并登记给 spawner 互斥）
└─ FormationStrikeEvent（运行时由 main 在 _ready 创建并登记给 spawner；最低优先级随机事件）
```

- `scripts/main.gd`：对局编排，串联刷怪、里程碑、Boss、母舰召唤、返航、放弃对局、BGM 与页面流转。
- `autoload/game_state.gd`：全局分数、HP、Buff、难度、RP、任务、路线、设置和信号总线；加载数值/翻译；维护 `GameState.enemies`、`player_ref`、`player_hitbox`、对象池引用；读写本地存档。
- `scripts/player.gd`：WASD 移动、鼠标瞄准、自动开火、燃料加速、微调、相位冲刺和受击处理。Buff 外观反馈由子节点 `scripts/player_buff_visuals.gd`（PlayerBuffVisuals）承担：程序化炮舱/护盾弧/光环/信标与尾焰染色（`engine_tint` 乘区），由 `GameState.buffs_changed` 信号驱动。辅助瞄准（2026-07-30 重设计）：准星构件 `scripts/aim_crosshair.gd`（AimCrosshair，挂 Player，top_level；仅对局活跃时跟随 `aim_point()` 并隐藏系统光标，同一条件驱动两处），敌机按 `player.aim_assist.mark_ratio` 出生掷 `Enemy.aim_marked` 标记，`scripts/aim_frame_layer.gd`（AimFrameLayer，挂 Main）统一画 bracket 框；准星入框时 `player._fire()` 给新弹写入 `Bullet.homing_target` 追踪（档位表 `player.aim_assist.levels`：frame_pad/homing_turn_rate，`homing_time` 追踪时长），未入框朝准星直射。
- `scripts/spawner.gd`：波次化刷怪与特殊槽调度。普通波成组（均分槽位入场、锚点悬停机动）按间隔 ramp 刷新；每 3~4 个普通波一个精英波；Boss/精英/事件占用特殊槽（Boss 激活与事件期间暂停普通波次），精英/Boss 击杀后追加休整波次。普通波次当前直接实例化 `enemy.tscn`；Boss-3 生成的小怪使用 `GameState.enemy_pool.spawn()`。不要把"所有敌机已经池化"当成当前事实。
- `scripts/enemy.gd`、`mothership.gd`、`bullet.gd`、`laser_weapon.gd`：可实例化战斗实体和武器行为。
- `scripts/boss.gd`：Boss 实体，HP 阶段模式表驱动（P1/P2/ENRAGE，模式表 `boss.phases.typeN` + telegraph 前摇），三型差异化狂暴（`boss.enrage.type_*`，狂暴期玩家减速 ×0.35 而非定身），难度分档在 `_ready` 一次性乘算（`boss.difficulty_scaling`）。战斗锚线 `FIGHT_Y` 语义为距 view 顶缘偏移，使用点一律走 `_fight_anchor_y()`（2026-07-30 view 适配）。设计/实施记录见 `docs/BOSS_REDESIGN.md`。
- `scripts/bullet_pool.gd`、`enemy_pool.gd`、`explosion.gd`、`starfield.gd`、`camera_shake.gd`、`spawn_telegraph.gd`：对象复用与表现层。
- `scripts/hud.gd`、`buff_select.gd`、`base_console.gd`、`settings_ui.gd`、`pause_ui.gd`、`game_over_ui.gd`、`start_panel.gd`、`welcome_screen.gd`、`exit_confirm.gd`：页面和覆盖层。
- `scripts/meta_health_fx.gd`（MetaHealthFX）+ `assets/shaders/meta_health.gdshader` + `assets/shaders/crack_field_bake.gdshader`：Meta HUD 血量/受击反馈（设计文档 `docs/META_HUD_DESIGN.md`）——全屏后处理承载受击色差/径向模糊、攻击方向定向波纹、低血裂纹生长/错峰消散（Voronoi 距离场一次性预烘焙：窗口模式 SubViewport GPU 512²、headless CPU 64² 等价回退，两路径公式必须同步）、去饱和/晕影与 DYING 心跳/呼吸/HUD 抖动；满血隐藏全屏 ColorRect + `_process` 早退（常态零 GPU、≈零 CPU），参数上传 epsilon 检测。数值在 `effects.meta_health`，「减少闪光」无障碍开关在设置页「操作模式」。
- `scripts/cinematic_fx.gd`（CinematicFx）：过场/演出共享特效静态工厂——软径向光晕 `soft_glow`（64² 程序生成软点贴图，消除硬边圆点）、带纹理粒子 `particles`（与旧 `_particles(cfg)` 同契约，≤96/发射器）、双层冲击波环 `shockwave`、分层能量束+流光点 `beam`、速度线场 `speed_lines`、径向放射条纹 `radial_streaks`；驱动类 `_process` 全部零堆分配。开场/返航过场与母舰召唤演出共用。
- `scripts/intro_cinematic.gd`：开场过场导演（6 镜头，新游戏触发，设计文档 `docs/INTRO_CINEMATIC.md`）；播放时树暂停，Esc 经 BackNavigator `SKIP_INTRO` 路由、任意键/点击由过场自身捕获跳过，播完/跳过统一走 `finished` 恢复。
- `scripts/return_cinematic.gd` + `scripts/dawn_station.gd`：返航过场导演（7 镜头，长按 B 返航触发，设计文档 `docs/RETURN_HOME_CINEMATIC.md`）；架构镜像开场，Esc 经 `SKIP_RETURN` 路由，播完/跳过统一走 `finished` 落基地 UI（树保持暂停，镜头 7 渐暗期 BGM 淡出到 -40dB）。`DawnStation` 是「曙光」站体共享静态工厂（毁灭态/全息虚影态），开场镜头 1、返航镜头 2/3/4 与后续基地背景层复用。
- `scripts/orbital_strike.gd`：轨道打击清场动画（基地「继续出击」触发）：瞄准具→导弹下落→命中光柱/扩散环，树保持暂停播放；命中帧（`struck`）由 main 做注册表驱动清场（Boss 保留、逐机爆炸）并恢复对局，数值在 `effects.orbital_strike`。
- `scripts/mothership_summon_window.gd` + `scripts/warp_gate.gd`：母舰召唤演出（蓄力完成触发，对局不暂停、演出期玩家锁输入+事件驱动无敌）——蓄力期 main 在停驻点叠加收缩双环/内吸粒子/背光（`_charge_fx`，随进度驱动、松手复位）；机库小窗（充能管线断开→维护臂解除→弹射+穿梭器启动，layer=24，`finished` 信号统一出口，`skip()` 供测试直推）；小窗结束后 main 在停驻点创建穿梭门（世界坐标，软光门心+内旋弧+门缘内吸粒子+前唇遮挡层），母舰 DESCEND 穿出减速（缩放 ease-out，前唇层压过舰体形成穿门读感，行程 `warp_in_drop`），到位释放双环减速带（`Enemy`/`Boss.apply_slow` 短时位移减速乘区）并立即火力掩护，DOCKING 牵引回收玩家进保护舱（光束含 3 枚循环流动捕获环；`player.enter_pod()` 隐藏+关受击判定，RELEASE `exit_pod()` 恢复），之后补给/驻留/离场与原流程一致。数值在 `effects.mothership_summon`。
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
| `scripts/tools/` | 离线工具；`generate_audio.py` 可重新生成已提交的 WAV；`generate_enemy_sprites.py`（敌方单位+航母+炮塔，晶体棱镜风格，`PALETTE_DARK`/`PALETTE_BRIGHT` 双档调色板）、`generate_player_sprite.py`（玩家机，钛灰钢甲+青色能量）、`generate_mothership_sprite.py`（母舰，同玩家体系）可重新生成全部单位贴图（PIL，超采样+光晕双层合成）。 |
| `assets/` | 游戏贴图、音效/BGM、字体和着色器（`assets/shaders/`，Meta HUD 后处理与裂纹场烘焙）。 |
| `data/` | 运行时数值配置和翻译资源源文件。 |
| `test/` | 以 `.tscn + .gd` 实现的无头场景自检、性能基准、自动游玩和截图工具。 |
| `docs/` | 退出流程、审计计划、路线图（ROADMAP）、无限流数值指引（ENDLESS_BALANCE_PLAN）、各机制设计文档与截图；`docs/archive/` 存放冻结的历史档案（移植对齐记录）。 |
| `packaging/` | 发布包随附的安装/卸载脚本：`linux/`（install.sh / uninstall.sh / infiair.desktop）、`windows/`（install.bat / uninstall.bat）。 |

## 开发约定

### GDScript 与场景生命周期

- 遵循 Godot 4 官方风格：**Tab 缩进**、类型标注、`CONSTANT_CASE` 常量、私有成员前缀 `_`、`signal_name.emit()` / `signal_name.connect()` 信号语法。
- `setup()` 会在实体被加入场景、执行 `_ready()` 之前调用。此阶段不要依赖 `@onready` 缓存；改用 `$节点路径` 访问子节点。
- 不要修改既有 autoload 或输入映射来完成无关需求。现有输入由 `project.godot` 定义，包括移动、`boost`（Shift）、`fine_move`（Ctrl）、`dash`（Space）、`dock`（H）、`homecoming`（B）、`give_up`（K）、`buff_panel`（L，展开/收起 buff 滚动栏）和 `restart`（R）。
- 教程进入时会隔离对局状态和存档，离开时必须恢复 `Engine.time_scale = 1`。运行期创建的节点要保存引用，不能依赖 Godot 自动生成的节点名。
- 延迟回调不要 `await get_tree().create_timer()` 或挂起在任何计时器上的协程：进程退出时未完成的协程函数状态会泄漏，并连带其引用的资源（贴图/音频）。改用一次性 `Timer` 节点 + 信号连接（参考 `spawner.gd` 的 `_schedule()`），Timer 随场景树释放。

### 数值与配置

- **可调游戏数值只修改 `data/balance.json`，不要仅修改脚本回退值。** 脚本内的同名默认值用于缺键/损坏 JSON 回退，新增或调整数值时应保持两者一致。
- 统一使用 `GameState.cfg("player.fuel.drain", default)` 查询嵌套配置。高频 `_process`/`_physics_process` 路径必须在 `_ready()` 或初始化阶段读取并缓存，不要每帧查 JSON 字典。
- `GameState` 在启动时加载 `balance.json`，并对缺失或无法解析的配置使用脚本默认值。
- **整体机体缩放只有一个杠杆：`balance.json` 顶层 `world_scale`（当前 1/3），运行时缓存为 `GameState.world_scale`。** 机体尺寸族数值——贴图 scale、碰撞 radius、muzzle/对接/炮位/牵引等机体偏移、子弹/爆炸/穿梭门/激光判定等随机体特效比例——在 json/tscn/脚本回退三处一律存**设计值**（1.0 基准，即 2026-07 缩放前的原始大小），由实体在 `_ready()`/`setup()` 统一乘 `world_scale` 后应用。游戏性范围族（AoE 半径、锁定/清弹半径、减速环）与指示器/过场/UI 不乘。新增尺寸数值时按此归类，不要绕过杠杆直接写运行值。
- 尺寸应用必须是**幂等赋值**（`radius = 设计值 * world_scale`），严禁 `*=` 累乘：场景 CircleShape2D 等 sub_resource 默认被全部实例共享，累乘会逐实例重复缩放；运行时写半径的场景（enemy.tscn）要给 shape 加 `resource_local_to_scene = true`（普通机与精英半径不同档，共享会互相串改）。

### 碰撞、伤害与视角

- 逻辑碰撞层约定：1=`player`、2=`player_bullet`、3=`enemy`（含 Boss）、4=`enemy_bullet`。玩家子弹以 `enemy` 组结算；敌方子弹和敌方实体以 `player_hitbox` 组结算。
- 玩家受击只使用 `Player/Hitbox` 的 Area2D（设计值 r=7 × world_scale，当前运行值 2.33）。`CharacterBody2D` 本体的半径 22 圆没有碰撞用途（mask 为 0），不得用于受击判定。
- 子弹使用 `scenes/bullet.tscn`，由 `setup()` 区分阵营；敌阵营视觉缩放用 `effects.enemy_bullet_visual_scale`、玩家弹用 `effects.bullet_visual_scale`（均设计值 × world_scale），玩家弹可写入 `Bullet.homing_target` 追踪（字段在 `activate()` 重置清单内）。爆炸应使用 `Explosion.spawn_at()`，而非为每次爆炸随意构建新的粒子方案。
- 视角缩放和窗口尺寸是相互独立的 profile 设置。相机固定在 `(960, 540)` 并只调整 `zoom`；所有屏幕边缘、出界、刷怪和可见区域计算必须使用 `GameState.view_world_rect()`，不要硬编码 `0..1920` 或 `0..1080`（Boss 战斗锚线 `_fight_anchor_y()`、敌机悬停带/入场锚点基线均已按此适配）。

### UI、文本与导航

- 所有用户可见文本使用 `tr("UPPER_SNAKE_CASE_KEY")`。新增键必须同步写入 `data/translations.csv` 的 `zh` 和 `en` 列；让 Godot 重新导入后生成 `.translation`。动态文本使用带 `%d`/`%s` 占位符的翻译键。
- 语言切换必须经 `GameState.set_locale("zh" / "en")`，并使 UI 监听 `locale_changed` 后刷新文本。
- 页面样式使用 `scripts/ui_theme.gd`：色板 token、字号阶梯、`make_label()`、`make_button()`、`make_toggle_button()`、`make_section_header()`、`make_page_shell()`、`make_buff_tile()`（HUD buff 图标格：46×46 字形瓦片 + 层数徽标；右下角收起态单行（最新 4 格 + 溢出 +N），L 展开右缘滚动明细栏，Esc 经 BackNavigator 优先收栏）和开场动画工具；可复用构件还有 `scripts/ui_chamfered_panel.gd`（切角面板）、`scripts/ui_segmented_bar.gd`（分段条形仪表，末段按比例部分填充）与 `scripts/ui_buff_icons.gd`（BuffIcons：16 种 buff 程序化字形 + 分类配色，HUD 图标坞与 Buff 三选一卡片共用）。新页面以 `make_page_shell()` 组合，单页最多一个 primary 主按钮；不要散落手写色值和重复 Label/Button 样板。
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

测试不是单元测试框架；每个 `test/*.tscn` 启动相应 GDScript 场景，并以 `[PASS]`/`[FAIL]` 输出和退出码自检。`test/` 下共 38 个场景：29 个断言场景，外加 `autoplay_test`（探针）、`perf_bench`（性能基准）、`visual_capture` / `ui_capture` / `return_capture` / `intro_capture` / `summon_capture` / `meta_fx_capture` / `hud_capture`（窗口模式截图工具）。

- 测试可能读写 `user://savegame.json` 与 `user://profile.json`。新测试应先 `GameState.delete_save()`，并在结束时清理或恢复自己创建的持久化状态，保证可重复执行。
- `test/balance_test.gd` 会暂时**覆盖项目内** `data/balance.json` 来验证损坏和回退路径，然后恢复原文件。不要在手工编辑该文件时并发运行它，也不要中断它后假设文件仍然完好。
- `test/autoplay_test.tscn` 是长时自动游玩与 `[ANOMALY]` 不变量监控探针，不以常规断言失败形式代表所有问题。注册表一致性按 "enemy" 组集合双向比对（含炮台/编队战机注册者，跳过池化 deferred 回收窗口）；另覆盖 Buff 卡确认动效路径（10% 真实三参选取）、返航过场期豁免的卡死计时、狂暴减速复位、buff 层数封顶与事件/Boss 阶段计数（SUMMARY 输出）。
- `test/perf_bench.tscn` 必须带 `--fixed-fps 1000`；无头默认帧率行为不适合直接比较纯帧耗时。做性能 A/B 时交错运行并使用中位数。
- 修改 UI 后使用窗口模式截图人工核对；headless 不会输出可用游戏截图。

## 持久化与安全边界

- 对局存档为 `user://savegame.json`，局外档案为 `user://profile.json`；二者由 `GameState` 管理并带版本字段。profile 保存最高分、难度、键位、语言、视角、窗口尺寸、欢迎页/教程状态等。
- 损坏 JSON 会被隔离为 `<file>.corrupt`，并通过 `save_corrupt`/`profile_corrupt` 标记通知开始界面。不要绕过该恢复流程。
- 当前未发现网络通信、第三方插件、远程服务、密钥或凭据文件。除本地 `user://` 持久化和离线资源生成外，游戏没有外部交互。
- `.gitignore` 排除导入缓存和导出产物（`builds/` 等）；`export_presets.cfg` 已随打包发布重启入库（2026-07-30），修改预设需同步审查 `release.sh` 与 `packaging/`。若未来增加 CI/自动部署，先补齐可审查的工作流与发布说明，再把它写入本文件。

## 文档同步要求

- 调整项目方向、阶段计划或暂缓/重启决策时，更新 `docs/ROADMAP.md`（方向类决策的单一事实源）。
- 调整页面返回层级、退出清理或平台返回处理时，更新 `docs/EXIT_FLOW.md` 并运行返回导航测试。
- 修改工程结构、运行命令、测试策略、配置位置或本文件所述约定时，同步维护本 `AGENTS.md`，使其保持面向首次接手项目的代理的真实入口文档。
