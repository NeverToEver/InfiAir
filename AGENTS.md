# AGENTS.md

## 项目概述

InfiAir：2D 俯视空战射击游戏，Godot 4.6 + GDScript（gl_compatibility 渲染器，无外部插件），是 Python/Pygame 游戏 `../airwar-game` 的重制版。竖向卷动星空、鼠标瞄准全自动射击、波次敌机、里程碑 Buff 三选一（池共 16 种）、周期 Boss 战（3 种轮换 + 狂暴阶段）、母舰补给、返航基地中场整备。纯得分制，无掉落/拾取机制。详细玩法见 `README.md`，移植对齐情况见 `docs/PORTING_PARITY.md`。

- 主场景 `scenes/main.tscn`，窗口 1920×1080（stretch = canvas_items / keep）。
- 唯一 autoload：`GameState`（`autoload/game_state.gd`），全局状态与信号总线，内含常驻音效池（`GameState.play_sfx()`）与 `screen_shake` 信号。
- 无构建系统、无包管理器、无 CI、无发布流程；唯一依赖是 Godot 4.6 编辑器/命令行。

## 运行 / 验证命令

```bash
# 无头导入（验证资源与脚本解析）
~/.local/bin/godot --headless --import --path .
# 无头跑 300 帧（验证无运行时错误）
~/.local/bin/godot --headless --path . --quit-after 300
# 无头冒烟测试（Buff UI / Boss / 结算 / 暂停路径）
~/.local/bin/godot --headless --path . res://test/smoke_test.tscn
# 基地系统测试（存档 / RP / 任务 / 天赋路线数据层）
~/.local/bin/godot --headless --path . res://test/base_system_test.tscn
# 启动链路测试（损坏存档隔离 / 欢迎页 / 开始面板焦点键盘链路）
~/.local/bin/godot --headless --path . res://test/startup_flow_test.tscn
# 返回/退出状态机测试（decide 全分支 + Esc/Android 返回集成路径）
~/.local/bin/godot --headless --path . res://test/back_navigation_test.tscn
# 对象池复用回归（reparent 触发 _exit_tree 导致 forget 误清的 3.11 修复）
~/.local/bin/godot --headless --path . res://test/pool_reuse_test.tscn
# 模拟人工游玩探针（≥8 分钟真实时间自动游玩 + [ANOMALY] 不变量监控，不以 FAIL 结束；
# 覆盖暂停存档/继续对局读档、对局中切设置（视角/窗口/语言/难度）、基地全模块、
# 母舰蓄力取消/提前离舰/强制弹射、狂暴期冲刺、Buff 种类优先未拥有；监控 Performance
# 对象/孤儿节点/内存/帧耗时、注册表一致性、池规模上界；--seed=N 换随机种子，默认 20260722）
~/.local/bin/godot --headless --path . res://test/autoplay_test.tscn [-- --autoplay-seconds=480] [-- --seed=N]
# 本地运行
godot --path .
```

注意：无头模式帧率不封顶，`--quit-after N` 的帧数不等于真实秒数；需要时间相关行为时用真实时间等待（参考 `test/smoke_test.gd`）。视觉截图用 `test/visual_capture.tscn`，需窗口模式运行（headless 为 dummy 渲染截不到画面），产物在 `/tmp/infiair_capture.png`。

## 目录与代码组织

- `scenes/` 场景（main / player / enemy / boss / mothership / bullet），同名脚本放 `scripts/`。
- `scripts/` 主要模块：
  - `main.gd` — 对局主循环编排：刷怪、里程碑 Buff、Boss 调度、返航/召唤母舰蓄力计时。
  - `player.gd` — 玩家（WASD 移动、鼠标朝向、燃料加速、相位冲刺、受击无敌帧）。
  - `spawner.gd` — 敌机数值集中在 `ENEMY_TYPES` / `ELITE_TYPES`（static var，非 const：含 Vector2i 构造非常量表达式）。
  - `enemy.gd` — 8 种移动策略（straight/sine/zigzag/dive/spiral/noise/aggressive/hover）；`boss.gd` — 3 种 Boss 轮换与狂暴逻辑（类型由 `boss_kills % 3 + 1` 决定；狂暴为完整序列状态机 EnragePhase：TRANSITION→ACTIVE→RELEASE_HOLD→RETURN）。
  - `mothership.gd` — 母舰 7 态状态机（DESCEND/HOVER/DOCKING/RESUPPLY/STAY/RELEASE/DEPART）。
  - `buff_select.gd` — Buff 池 `BUFF_POOL`（16 种，含层数上限）与三选一 UI。
  - `base_console.gd` — 返航基地控制台（战机库/武器挂载/维修补给/任务规划 4 模块）。
  - `welcome_screen.gd` — 进游戏欢迎页（仅装机后首次启动：profile `welcome_seen` 持久化 + static 进程内兜底；任意键进入开始面板）。
  - `back_navigator.gd` — 全局返回/退出状态机：PC Esc / 手柄 B（引擎内置 ui_cancel）与 Android 返回通知统一走 `go_back()`，页面层级与决策表见 `docs/EXIT_FLOW.md`；`exit_confirm.gd` — 全局退出确认窗（normal/battle 双模式，确认后统一清理：profile 落盘、战斗中删档、淡出 0.3s 后 quit）。
  - `hud.gd` / `game_over_ui.gd` / `pause_ui.gd` / `start_panel.gd` — UI；`starfield.gd`、`camera_shake.gd`、`explosion.gd`、`spawn_telegraph.gd` — 表现层。
  - `scripts/tools/generate_audio.py` — 一次性音频程序合成脚本（仅 Python 标准库），产物已提交到 `assets/audio/`；需要重做音效时改参数重跑即可。
- `autoload/game_state.gd` — 分数/生命/buff 层数/RP/任务/天赋路线数据层、存档与最高分持久化。
- `assets/` — `sprites/`（战机贴图，机头朝上）、`audio/`（开火/爆炸/BGM 等 wav）、`fonts/`（NotoSansSC.ttf 中文 UI 字体，OFL 开源可分发）。
- `test/` — 无头测试场景（见上节命令）。

## 关键约定（改动时必须遵守）

- 碰撞层：1=player，2=player_bullet，3=enemy（含 boss），4=enemy_bullet。子弹负责结算伤害（玩家弹检测 enemy 组，敌弹/敌机撞击检测 `player_hitbox` 组）。
- 受击判定（3.9 起为 100 HP 制，对齐原作）：玩家 HP 存于 `GameState.health`（上限 `GameState.max_health()` = 基础 100 + extra_life ×50），受击只看 `Hitbox` Area2D（r=7 小判定点，近似原作 10×14）；CharacterBody2D 上的 r=22 圆无碰撞用途（mask=0，勿用于判定）。`player.take_damage(amount) -> bool`：先 20% 闪避（evasion，二元）再护甲 ×0.85（armor，二元），全伤害源两段式（原作分裂语义为疑似 bug 未移植）；返回 false（无敌/单帧已结算/闪避）时敌弹穿过不销毁——单帧至多结算一次受击（帧号标记），命中生效后清 250px 敌弹。敌弹按弹种 12/10/20，Boss 弹 14/12/21/12/21/12，敌机撞击 20（敌机不自毁），Boss 撞击 30（入场降入/逃跑离场不判定）。Boss 狂暴锁血：未狂暴时非致死伤害最多把 HP 钳到 30% 阈值并触发狂暴，致死直接击杀；触发后进入完整狂暴序列（TRANSITION 0.9s 子弹时间蓄力 → ACTIVE 绕触发时玩家位置快照走方形→圆形轨道 + 高速波次开火 → RELEASE_HOLD 0.7s 密集慢速弹幕 → RETURN 0.8s 归位，数值在 `boss.enrage` 段），序列期间（触发→RELEASE_HOLD 前）HP 锁定在 30% 检查点（任何伤害不掉血不死）且冻结玩家移动（`player.movement_locked`，对齐原作 is_controls_locked：定身但可射击；RELEASE_HOLD/逃跑/死亡/离场必解除），RELEASE_HOLD 起解锁可正常击杀，序列结束后保持永久射速 ×1.5/移速 ×1.3。回血：regen buff +2 HP/s，被动回血按难度（受伤重置延迟），lifesteal 击杀回 10% 上限（每帧至多一次），基地维修/母舰补给回满。
- 玩家/敌弹共用 `scenes/bullet.tscn`，用 `setup()` 区分阵营；爆炸为纯代码构建的 `Explosion`（GPUParticles2D 一次性）。
- 实体 `setup()` 在 `_ready()` 之前被调用，其中不能用 `@onready` 变量，需用 `$节点路径` 访问子节点。
- 暂停类 UI（Buff/结算/暂停）`process_mode = Always`，用 `get_tree().paused` 控制。
- 返回/退出路由统一在 `BackNavigator`（`ui_cancel` + Android 返回通知 → `go_back()`，层级见 `docs/EXIT_FLOW.md`）；各 UI 不得再自行处理 `ui_cancel`（settings 改键捕获态除外，navigator 会放行），新增页面要在 `decide_back_action()` 登记层级。
- BGM 循环只设 `stream.loop_mode = LOOP_FORWARD`；不要显式写 `loop_begin/loop_end` 或在 `_exit_tree` 里 `stop()`，否则退出时播放实例会泄漏（已在无头验证中复现）。
- 母舰：长按 H 蓄力 3s 召唤（main 管理，虚影预告）→ 到位**自动点吸附对接**（无区域判定，原作语义）→ 驻留 20s 弹匣制（10 格 × 2s；≤4 格警告，警告 5s 后强制离舰，对齐原作横幅弹射）+ WASD 驾驶母舰；无敌窗口 = 吸附开始→弹射结束（释放后 2s 保护为重制版 QoL）。长按 H 2s 提前离舰：冷却双机制折扣（时长 max(0.6,1-0.4r) + 预填 min(0.3,0.5r)），基础冷却 60s。火力：加特林双塔向上 80° 扫射（仅驻留有目标时）+ 导弹（0.3s/波 ≤5 最近目标、直线定向弹直击 80+溅射 20）；弹丸/导弹 `score_scale=1/3`（击毁结算向下取整，enemy/boss 的 `take_damage(amount, score_scale)` 链路）。补给回满血+燃料为重制版增强（原作母舰无补给）。
- 返航 = 局内中场整备：长按 B 蓄力 1.5s（main.gd `_process` 计时），「继续出击」轨道打击清屏后返回同一局（Boss 保留）；RP/任务/天赋路线数据层在 game_state.gd（见 `test/base_system_test.gd`）。
- 视角缩放三档（`GameState.view_zoom`：small 1.0 / medium 1.35 / large 1.7，默认 medium，profile 持久化）：相机固定在 (960,540) 只设 `zoom`，一切"屏幕边缘/出屏/刷怪位置"逻辑必须走 `GameState.view_world_rect()`（zoom=1 时即全屏 1920×1080），不得再写死 0..1920 / 0..1080。
- 窗口大小三档（`GameState.window_size`：small 1280×720 / medium 1600×900 / large 1920×1080，默认 large，profile 持久化）：`set_window_size()` 立即应用到窗口（仅窗口模式生效，headless 跳过）并发 `window_size_changed`，启动时 `load_profile()` 读档即应用；与视角缩放 `view_zoom` 是两套独立设置（stretch 等比缩放，内容不变形），互不影响。
- UI 样式统一走 `UITheme`（scripts/ui_theme.gd）：色板 token + 字号阶梯（FONT_DISPLAY 72/TITLE 40/HEADER 28/BODY 24/CAPTION 18，字体 `UITheme.FONT`）+ 工厂方法（`make_label`/`make_button(primary)`/`make_toggle_button`/`make_section_header`/`make_page_shell` 返回 {root,panel,title,content}/`animate_open`/`stagger_open`）。新页面用 `make_page_shell` 组装，每页至多一个 primary 主按钮；不要再手写散落的 Label/Button 样板与硬编码色值。视觉核对用 `test/ui_capture.tscn`（窗口模式，产物 /tmp/ui_*.png）。
- 教程场景 `scenes/tutorial.tscn`（`scripts/tutorial.gd`）独立于 main 对局逻辑：进场 `reset_run` + `delete_save` 隔离，出场再 reset 并强制 `Engine.time_scale = 1`；开始面板「教程」按钮进入，Esc 退出。运行期代码创建的节点要取引用保存，不要用 `get_node("ClassName")`（自动名是 `@CanvasLayer@N` 形式）。

## 测试策略

- 测试是无头运行的场景脚本（`test/*.tscn`），用 `print("[PASS]")` / `printerr("[FAIL]")` 自检，非单元测试框架。
- 测试运行会读写 `user://savegame.json` 与 `user://profile.json`，结束后需清理残留（冒烟测试已自行清理）；新测试也应先 `GameState.delete_save()` 保证确定性。
- 改动后至少跑：`--headless --import`、`--quit-after 300`、`smoke_test.tscn`；涉及存档/基地/母舰时加跑 `base_system_test.tscn`。

## GDScript 风格

- 遵循 GDScript 官方风格：Tab 缩进、类型标注、Godot 4 信号语法（`signal_name.emit()` / `signal_name.connect()`）。
- 私有成员加 `_` 前缀；常量用 `CONSTANT_CASE` 并集中在文件头部。
- 不引入外部插件；不改 `project.godot` 的 autoload 与既有输入映射（追加新映射允许，已追加：`dash`=空格、`dock`=H、`homecoming`=B）。

## 数值调参

- 所有可调数值集中在 `data/balance.json`（玩家/敌机/精英/Boss/刷怪/母舰/buff/里程碑/难度/效果/教程分层）；**调参只改 JSON，不改脚本常量**。脚本内的同名 var 是回退默认值，必须与 JSON 保持一致。
- 访问统一走 `GameState.cfg("player.fuel.drain" 式路径, 默认值)`；每帧热路径禁止直接 cfg 查询，在 `_ready()` 一次性读进成员变量（参照 player.gd `_load_balance()`）。

## 语言（中英双语）

- 文案一律走 `tr("KEY")`，key 用英文大写蛇形（`UI_SCORE`、`BUFF_POWER_SHOT_NAME`、`TUT_S1_TITLE`）；**新增 UI 文案必须同时在 `data/translations.csv` 加 zh/en 两列**（改后需重新 import 生成 .translation）。
- GameState 启动时手动加载 `translations.zh/en.translation` 并应用 profile 里的 `locale`；切换用 `GameState.set_locale("zh"/"en")`（落盘 + `locale_changed` 信号），各 UI 监听信号刷新文本。
- 动态拼接文本用带 `%d`/`%s` 占位的 key（如 `MS_STAY "驻留 %ds"`）。

## 性能约定（3.4）
- 产弹一律走 `GameState.bullet_pool.fire()`：活跃弹挂 Main 下（清场/测试遍历可见），回收回 BulletPool 节点；外部 queue_free 由子弹 `_exit_tree` 自动 forget，不会污染池。**池的同帧回收-复用安全**：实体带 `_active` 标记，回收的延迟调用（monitoring=false / reparent）在重激活后自动失效（3.9 修复过期延迟调用覆盖新激活的缺陷，改动池行为时勿破坏该模式）。**注意：4.6 实测 `reparent()` 也会触发 `_exit_tree`**，池回收 reparent 必须用实体的 `_repooling` 标记包住，否则 forget 会把实体误清出 `_free`，池只进不出（3.11 修复，回归测试 `test/pool_reuse_test.tscn`）。
- 敌机一律走 `GameState.enemy_pool.spawn()`（enemy_pool.gd，模式同子弹池）：`reactivate()` 全状态重置（计时/策略/HP/调制色/died 断连），`deactivate()` 注销注册表并断开 died 监听；`USE_POOL=false` 可回退纯 instantiate/free 做 A/B 对照。直接实例化（测试）走 `_ready` 兼容路径，互不影响。
- 敌机三角函数统一 `Enemy.sin_fast/cos_fast`（2048 项循环表 + 线性插值，静态共享），禁止在 `_physics_process` 直接调 sin/cos。
- 爆炸走 `Explosion.spawn_at`（静态池 ≤24，发射完回收不销毁）。
- 热路径禁止每帧 `get_nodes_in_group`：用 `GameState.enemies` / `GameState.player_ref` 注册表（enemy/boss/player 在 `_ready`/`_exit_tree` 维护）。
- HUD 仪表类轮询 0.1s 节流 + 文本/格子值变化才重排（缓存上次值）；文本走信号。
- 基准：`godot --headless --fixed-fps 1000 --path . res://test/perf_bench.tscn`（无头默认实时锁帧，必须 `--fixed-fps` 才能测出纯帧耗时；本机噪声大，A/B 对照用交错跑取中位数）。

## 持久化与安全注意

- 对局存档 `user://savegame.json`（暂停菜单「保存进度」可写 + 返航自动更新；死亡/战斗中确认退出/开新局时删除）与局外档案 `user://profile.json`（最高分/难度/键位/locale/视角/窗口大小/`welcome_seen`/`tutorial_done` 等；局外天赋系统已移除，旧 talents 字段读取时忽略），逻辑都在 `autoload/game_state.gd`，均带 `version` 字段。
- 损坏持久化文件自动隔离：JSON 解析失败时重命名为 `<file>.corrupt` 备份并置 `save_corrupt`/`profile_corrupt` 标记（开始面板据此提示），按无存档/默认档案继续，不留死路径；`--startup-time` CLI 参数（`--` 后传入）可打印启动分段耗时。
- 无网络代码、无第三方依赖、无密钥；唯一外部交互是上述 user:// 本地文件。

## 数值平衡参考

大致参考量级即可，不逐行对齐：

- `../airwar-game/airwar/config/game_config.py`、`config/difficulty_config.py` — 全部平衡数值
- `../airwar-game/airwar/entities/player.py`、`entities/player_components/` — 玩家行为
- `../airwar-game/airwar/entities/enemy/` — 敌机移动模式
- `../airwar-game/airwar/systems/difficulty_manager.py` — 难度乘数公式
- `../airwar-game/airwar/game/buffs/buffs.py` — buff 设计
