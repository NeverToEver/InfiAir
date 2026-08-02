# 运行时架构与配置明细

> 本文是 `AGENTS.md` 的按需读取参考文档：记录 `scenes/main.tscn` 节点树、逐脚本职责、技术栈与关键配置文件、目录职责明细。**行为准则与入口指引见 `AGENTS.md`**；测试命令见 `docs/TESTING.md`。

## 技术栈

- **引擎：** Godot 4.6（标准版即可，无需 .NET）。`project.godot` 当前声明 `4.6` 和 `GL Compatibility` 特性，桌面/移动端均使用 `gl_compatibility`。
- **语言：** 纯 GDScript；`scripts/tools/` 下的 Python 文件是离线工具（数值管理器、文档生成器、资产生成器），仅依赖标准库（贴图生成器另需 PIL），不属于游戏运行时依赖。
- **资源：** `assets/sprites/` PNG、`assets/audio/` WAV、`assets/fonts/NotoSansSC.ttf` UI 字体。
- **数据：** `data/balance.json` 为可调数值源（顶层分区：world_scale、player、enemies、elites、boss、spawner、mothership、buffs、milestones、difficulty、progression、effects、tutorial、elite_turret_event、formation_strike_event）；自 2026-07-31 起为规范 JSON 格式（Tab 缩进、无行内对象），由 `balance_editor.py` 维护落盘。`data/translations.csv` 是中英文本源，`.translation` 文件由 Godot 导入生成并在运行时加载。

## 关键配置文件

| 文件 | 用途 |
| --- | --- |
| `project.godot` | Godot 项目名、入口场景、唯一 autoload、视口/拉伸、输入映射与渲染器。优先用 Godot 编辑器修改。 |
| `data/balance.json` | 玩家、敌机、Boss、刷怪、Buff、母舰、难度、特效、教程等可调参数。Boss 段含 `phases`（阶段模式表/telegraph/各型 P2 攻击参数）、`enrage.type_*`（三型差异化狂暴）与 `difficulty_scaling`（弹数/间隔/弹速三档分档表）。优先用 `scripts/tools/balance_editor.py` 编辑。 |
| `data/translations.csv` | 翻译键及 `zh`、`en` 文本源。 |
| `.gitignore` | 忽略 `.godot/`、导入的 `*.translation`、本地 IDE 文件和导出产物（`builds/` 等；`export_presets.cfg` 自 2026-07-30 起入库）。 |
| `run.sh` / `run.command` / `run.bat` | macOS/Linux/Windows 的本地启动包装。`run.sh`：PATH → `~/.local/bin/godot` → App bundle，低版本仅告警，参数透传（`--editor` 等）。`run.command`（双击）：候选含 `/Applications` 与 `~/Applications` 的 `Godot*.app` 变体名，逐候选验证版本并优先选 4.6+，不用 `exec`——异常退出保留窗口与输出。 |
| `export_presets.cfg` | Linux/X11 与 Windows Desktop 导出预设（嵌入 pck 单文件，x86_64）。需本机安装匹配版本的 Godot 导出模板。 |
| `release.sh` | 发布构建：资源导入 → 双平台导出 → 打包到 `builds/release/`（`VERSION` 环境变量指定版本号）。 |

当前**未发现** `package.json`、`pyproject.toml`、`requirements*.txt`、`Cargo.toml`、`go.mod`、Makefile、Docker/Compose 配置或 CI 工作流。打包发布已重启（2026-07-30）：`export_presets.cfg` 入库（Linux/X11 + Windows Desktop，嵌入 pck 单文件 exe/二进制），根目录 `release.sh` 一键完成导入 → 双平台导出 → 打包（产物 `builds/release/`，版本号由 `VERSION` 环境变量指定）；`packaging/linux/`（用户态 install.sh / uninstall.sh[--purge] / infiair.desktop）与 `packaging/windows/`（per-user install.bat / uninstall.bat[/purge]，开始菜单快捷方式）随包分发。不要虚构 CI/自动部署流程或为常规修改引入第三方插件/依赖。

**发布工程现状（2026-07-31 更新）**：导出模板已安装（`~/Library/Application Support/Godot/export_templates/4.6.2.stable/`），`./release.sh` 已跑通，产物在 `builds/release/`（`InfiAir-<版本>-linux-x86_64.tar.gz` / `-windows-x86_64.zip`，嵌入 pck 单文件 + 安装/卸载脚本，本机 gitignore）。**产物以 GitHub Releases 附件分发（不入库）**：`gh release create v<版本> builds/release/InfiAir-<版本>-*.{tar.gz,zip}`。macOS 本机无法运行 Linux/Windows 二进制，安装脚本与实机运行需在对应平台验证。

## 主节点树

`scenes/main.tscn` 是主节点树和对局容器：

```text
Main (scripts/main.gd)
├─ Starfield / Camera2D
├─ Player
├─ Spawner
├─ BulletPool / EnemyPool
├─ HUD（layer=2：为 MetaHealthFX 让出「世界之上、HUD 之下」的 layer=1）
├─ BuffUI / PauseUI / SettingsUI / GameOverUI / BaseUI
├─ StartPanel / ExitConfirm
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

`scenes/` 包含主场景、玩家、普通敌机、Boss、子弹、母舰、开场/返航过场和教程场景；同名行为脚本通常位于 `scripts/`。所有动态对局实体应挂在 Main 下，以便清场逻辑和测试遍历可见。

## 逐脚本职责

- `scripts/main.gd`：对局编排，串联刷怪、里程碑、Boss、母舰召唤、返航、放弃对局、BGM、页面流转与入场衔接动画（开场/继续出击后 `_start_entry_sequence`）。
- `autoload/game_state.gd`：全局分数、HP、Buff、难度、RP、任务、路线、设置和信号总线门面。数值加载/查询、存档读写/损坏隔离、音效池、实体注册表（`GameState.enemies`、`player_ref`、`player_hitbox`、对象池引用）分别委托 `BalanceService` / `SaveManager` / `SfxPlayer` / `EntityRegistry`（A2），对外 `GameState.*` 语法逐字不变。
- `scripts/player.gd`：WASD 移动、鼠标瞄准、自动开火、燃料加速、微调、相位冲刺和受击处理。Buff 外观反馈由子节点 `scripts/player_buff_visuals.gd`（PlayerBuffVisuals）承担（程序化炮舱/护盾弧/光环/信标 + 尾焰 `engine_tint` 乘区），由 `GameState.buffs_changed` 信号驱动。辅助瞄准（实现细节见 `scripts/aim_crosshair.gd` / `scripts/aim_frame_layer.gd` 与 `player.aim_assist` 档位表）：敌机按 `mark_ratio`（0.25）出生掷 `Enemy.aim_marked` 标记，AimFrameLayer（挂 Main）统一画 bracket 框，AimCrosshair（挂 Player，top_level）跟随 `aim_point()` 并隐藏系统光标；准星入框时 `_fire()` 给新弹写 `Bullet.homing_target` 追踪（追踪时长 `homing_time`，近距直取、中距转向加急，螺旋收敛不环绕），未入框朝准星直射。`aim_point()` 返回平滑瞄准点（入框按 `stick_factor` 弱吸附、框外近距磁吸拉向、框外锥形弱追踪），判定/开火/准星绘制共用同一点，每渲染帧推进一次；磁吸与弱追踪共用距离衰减（`player.aim_assist.falloff`：400px 内全辅助 → 1400px 线性降至 0.3 下限）。
- `scripts/spawner.gd`：波次化刷怪与特殊槽调度。普通波成组按间隔 ramp 刷新，每 3~4 个普通波一个精英波；Boss/精英/事件占用特殊槽（激活期间暂停普通波次），击杀后追加休整波次。普通波次当前直接实例化 `enemy.tscn`；Boss-3 小怪用 `GameState.enemy_pool.spawn()`。不要把"所有敌机已经池化"当成当前事实。
- `scripts/enemy.gd`、`mothership.gd`、`bullet.gd`、`laser_weapon.gd`：可实例化战斗实体和武器行为。
- `scripts/boss.gd`：Boss 实体，HP 阶段模式表驱动（P1/P2/ENRAGE，模式表 `boss.phases.typeN` + telegraph 前摇），三型差异化狂暴（`boss.enrage.type_*`，狂暴期玩家减速 ×0.35 而非定身），难度分档在 `_ready` 一次性乘算（`boss.difficulty_scaling`）。战斗锚线 `FIGHT_Y` 为距 view 顶缘偏移，使用点一律走 `_fight_anchor_y()`。设计见 `docs/BOSS_REDESIGN.md`。
- `scripts/bullet_pool.gd`、`enemy_pool.gd`、`explosion.gd`、`starfield.gd`、`camera_shake.gd`、`spawn_telegraph.gd`：对象复用与表现层。
- `scripts/hud.gd`、`buff_select.gd`、`base_console.gd`、`settings_ui.gd`、`pause_ui.gd`、`game_over_ui.gd`、`start_panel.gd`、`exit_confirm.gd`：页面和覆盖层。开始面板是全遮光独立标题屏（不透出对局画面）：左上品牌区 + 左中切角菜单板 + 右侧装饰雷达 `scripts/start_radar.gd` + 装饰星空 `scripts/start_backdrop.gd`（固定种子静态星点，与对局 Starfield 无关）。
- `scripts/meta_health_fx.gd`（MetaHealthFX）+ `assets/shaders/meta_health.gdshader` + `assets/shaders/crack_field_bake.gdshader`：Meta HUD 血量/受击全屏后处理（设计 `docs/META_HUD_DESIGN.md`）——受击色差/径向模糊、攻击方向定向波纹、低血裂纹生长（Voronoi 距离场一次性预烘焙：窗口模式 SubViewport GPU 512²、headless CPU 64² 等价回退，**两路径公式必须同步**）、去饱和/晕影与 DYING 心跳/呼吸/HUD 抖动；满血隐藏全屏 ColorRect + `_process` 早退（常态零 GPU、≈零 CPU）。数值在 `effects.meta_health`，「减少闪光」开关在设置页「操作模式」。
- `scripts/cinematic_fx.gd`（CinematicFx）：过场/演出共享特效静态工厂——软径向光晕 `soft_glow`、带纹理粒子 `particles`（≤96/发射器）、双层冲击波环 `shockwave`、分层能量束 `beam`、速度线 `speed_lines`、径向条纹 `radial_streaks`；驱动类 `_process` 全部零堆分配。开场/返航过场与母舰召唤演出共用。
- `scripts/intro_cinematic.gd`：开场过场导演（6 镜头，新游戏触发，`docs/INTRO_CINEMATIC.md`）；播放时树暂停，Esc 经 BackNavigator `SKIP_INTRO`、任意键/点击跳过，播完/跳过统一走 `finished` 恢复。
- `scripts/return_cinematic.gd` + `scripts/dawn_station.gd`：返航过场导演（7 镜头，长按 B 触发，`docs/RETURN_HOME_CINEMATIC.md`）；架构镜像开场，Esc 经 `SKIP_RETURN`，播完/跳过统一走 `finished` 落基地 UI（树保持暂停，镜头 7 渐暗期 BGM 淡出 -40dB）。`DawnStation` 是站体共享静态工厂（毁灭态/全息虚影态），开场镜头 1、返航镜头 2/3/4 与基地背景复用。
- `scripts/orbital_strike.gd`：轨道打击清场动画（基地「继续出击」触发）：瞄准具→导弹下落→命中光柱/扩散环，树保持暂停；命中帧（`struck`）由 main 注册表驱动清场（Boss 保留、逐机爆炸）并恢复对局，数值在 `effects.orbital_strike`。
- `scripts/mothership_summon_window.gd` + `scripts/warp_gate.gd`：母舰召唤演出（蓄力完成触发，对局不暂停、演出期玩家锁输入+事件驱动无敌）。蓄力期 main 在停驻点叠加 `_charge_fx`（收缩双环/内吸粒子/背光）；机库小窗（充能管线断开→维护臂解除→弹射+穿梭器启动，layer=24，`finished` 统一出口，`skip()` 供测试直推）；小窗结束 main 在停驻点创建穿梭门（软光门心+内旋弧+内吸粒子+前唇遮挡层），母舰 DESCEND 穿出减速（行程 `warp_in_drop`），到位释放双环减速带（`Enemy`/`Boss.apply_slow` 短时位移减速）并立即火力掩护，DOCKING 牵引回收玩家进保护舱（`player.enter_pod()` 隐藏+关受击，RELEASE `exit_pod()` 恢复），之后补给/驻留/离场与原流程一致。数值在 `effects.mothership_summon`。
- `scripts/elite_turret_event.gd`、`strike_carrier.gd`、`turret_battery.gd`（+ `scenes/turret.tscn`）、`comm_overlay.gd`：精英炮塔事件（`docs/ELITE_TURRET_EVENT.md`）——事件状态机与 Boss 互斥（`_boss_frozen`/`_boss_pending`/`_waves_paused` 钩子在 spawner）、打击航母导演、炮台实体（弱锁定索敌，注册 `enemy` 组与 `GameState.enemies`）、左下通讯浮层。
- `scripts/formation_strike_event.gd`、`formation_craft.gd`、`formation_bomb.gd`：轰炸编队事件（`docs/FORMATION_STRIKE_EVENT.md`）——最低优先级随机事件（不冻结 Boss，但**占用波次槽——运行期间暂停普通波次**，经 spawner `_waves_paused` 钩子，与精英炮塔互斥；可被返航 `abort()` 打断）、编队战机（注册 `enemy` 组与 `GameState.enemies`）、引信制下落炸弹（预警环随引信收缩，AoE 只伤玩家）。
- `scripts/back_navigator.gd`：PC Esc/手柄 `ui_cancel`/Android 返回统一路由。教程是独立场景 `scenes/tutorial.tscn`，由 `tutorial.gd` 自己处理返回；教程与正局逻辑对齐：`_ready` 同样创建 AimFrameLayer（辅助瞄准在教程内有效），阶段 1 强制标记靶机，阶段 4 长按 H 蓄力 → 穿梭门 → 母舰 `begin_warp_in` → 对接补给（略去机库小窗，实体路径同 `main._on_summon_window_finished`）。

## 目录职责明细

| 路径 | 内容与职责 |
| --- | --- |
| `autoload/` | 全局 autoload；当前只有 `game_state.gd`。 |
| `scenes/` | Godot `.tscn` 场景与节点组合。 |
| `scripts/` | GDScript 游戏逻辑、UI、表现和池实现。 |
| `scripts/tools/` | 离线工具（Python 标准库）；`balance_editor.py` 数值管理器（`python3 scripts/tools/balance_editor.py`，浏览器分区编辑 `balance.json`，改动高亮 + 服务端结构/类型校验 + 原子落盘 + 自动备份 `.bak`）——调数值优先用它，改完跑最小验证集；`gen_balance_map.py` 重新生成 `docs/BALANCE_MAP.md`（全部 `cfg()` 调用点索引 + json/脚本双写对齐反查，新增/改名键后必跑）；`generate_audio.py` 可重新生成已提交的 WAV；`generate_enemy_sprites.py`（敌方单位+航母+炮塔，晶体棱镜风格，`PALETTE_DARK`/`PALETTE_BRIGHT` 双档调色板）、`generate_player_sprite.py`（玩家机，钛灰钢甲+青色能量）、`generate_mothership_sprite.py`（母舰，同玩家体系）可重新生成全部单位贴图（PIL，超采样+光晕双层合成）。 |
| `assets/` | 游戏贴图、音效/BGM、字体和着色器（`assets/shaders/`，Meta HUD 后处理与裂纹场烘焙）。 |
| `data/` | 运行时数值配置和翻译资源源文件。 |
| `test/` | 以 `.tscn + .gd` 实现的无头场景自检、性能基准、自动游玩和截图工具。 |
| `docs/` | 退出流程、审计计划、审核-修复 SOP（AUDIT_REVIEW_SOP，并行审核方法论）、路线图（ROADMAP）、无限流数值指引（ENDLESS_BALANCE_PLAN）、数值位置地图（BALANCE_MAP，生成文件）、各机制设计文档与截图；`docs/archive/` 存放冻结的历史档案（移植对齐记录）。 |
| `packaging/` | 发布包随附的安装/卸载脚本：`linux/`（install.sh / uninstall.sh / infiair.desktop）、`windows/`（install.bat / uninstall.bat）。 |
