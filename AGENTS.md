# AGENTS.md

## 项目概述

InfiAir：2D 俯视空战射击游戏，Godot 4.6 + GDScript（gl_compatibility 渲染器，无外部插件），是 Python/Pygame 游戏 `../airwar-game` 的重制版。竖向卷动星空、鼠标瞄准全自动射击、波次敌机、每 500 分里程碑 Buff 三选一（池共 13 种）、周期 Boss 战（3 种轮换 + 狂暴阶段）、母舰补给、返航基地中场整备。纯得分制，无掉落/拾取机制。详细玩法见 `README.md`，移植对齐情况见 `docs/PORTING_PARITY.md`。

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
  - `enemy.gd` — 7 种移动策略；`boss.gd` — 3 种 Boss 轮换与狂暴逻辑（类型由 `boss_kills % 3 + 1` 决定）。
  - `mothership.gd` — 母舰 7 态状态机（DESCEND/HOVER/DOCKING/RESUPPLY/STAY/RELEASE/DEPART）。
  - `buff_select.gd` — Buff 池 `BUFF_POOL`（13 种，含层数上限）与三选一 UI。
  - `base_console.gd` — 返航基地控制台（战机库/武器挂载/维修补给/任务规划 4 模块）。
  - `hud.gd` / `game_over_ui.gd` / `pause_ui.gd` / `start_panel.gd` — UI；`starfield.gd`、`camera_shake.gd`、`explosion.gd`、`spawn_telegraph.gd`、`slow_field_ring.gd` — 表现层。
  - `scripts/tools/generate_audio.py` — 一次性音频程序合成脚本（仅 Python 标准库），产物已提交到 `assets/audio/`；需要重做音效时改参数重跑即可。
- `autoload/game_state.gd` — 分数/生命/buff 层数/RP/任务/天赋路线数据层、存档与最高分持久化。
- `assets/` — `sprites/`（战机贴图，机头朝上）、`audio/`（开火/爆炸/BGM 等 wav）、`fonts/`（msyh.ttc 中文 UI 字体）。
- `test/` — 无头测试场景（见上节命令）。

## 关键约定（改动时必须遵守）

- 碰撞层：1=player，2=player_bullet，3=enemy（含 boss），4=enemy_bullet。子弹负责结算伤害（玩家弹检测 enemy 组，敌弹/敌机撞击检测 `player_hitbox` 组）。
- 玩家/敌弹共用 `scenes/bullet.tscn`，用 `setup()` 区分阵营；爆炸为纯代码构建的 `Explosion`（GPUParticles2D 一次性）。
- 实体 `setup()` 在 `_ready()` 之前被调用，其中不能用 `@onready` 变量，需用 `$节点路径` 访问子节点。
- 暂停类 UI（Buff/结算/暂停）`process_mode = Always`，用 `get_tree().paused` 控制。
- BGM 循环只设 `stream.loop_mode = LOOP_FORWARD`；不要显式写 `loop_begin/loop_end` 或在 `_exit_tree` 里 `stop()`，否则退出时播放实例会泄漏（已在无头验证中复现）。
- 母舰：长按 H 蓄力召唤（main 管理，虚影预告）、对接驻留 20s 弹匣制、长按 H 2s 提前离舰冷却打折；加特林为双塔 80° 扫射压制，弹丸 `score_scale=1/3`（击毁结算向下取整，enemy/boss 的 `take_damage(amount, score_scale)` 链路）；对接序列锁输入用 `player._input_locked`，与暂停/清场逻辑兼容。
- 返航 = 局内中场整备：长按 B 蓄力（main.gd `_process` 计时），「继续出击」轨道打击清屏后返回同一局（Boss 保留）；RP/任务/天赋路线数据层在 game_state.gd（见 `test/base_system_test.gd`）。

## 测试策略

- 测试是无头运行的场景脚本（`test/*.tscn`），用 `print("[PASS]")` / `printerr("[FAIL]")` 自检，非单元测试框架。
- 测试运行会读写 `user://savegame.json` 与 `user://profile.json`，结束后需清理残留（冒烟测试已自行清理）；新测试也应先 `GameState.delete_save()` 保证确定性。
- 改动后至少跑：`--headless --import`、`--quit-after 300`、`smoke_test.tscn`；涉及存档/基地/母舰时加跑 `base_system_test.tscn`。

## GDScript 风格

- 遵循 GDScript 官方风格：Tab 缩进、类型标注、Godot 4 信号语法（`signal_name.emit()` / `signal_name.connect()`）。
- 私有成员加 `_` 前缀；常量用 `CONSTANT_CASE` 并集中在文件头部。
- 不引入外部插件；不改 `project.godot` 的 autoload 与既有输入映射（追加新映射允许，已追加：`dash`=空格、`dock`=H、`homecoming`=B）。

- 教程场景 `scenes/tutorial.tscn`（`scripts/tutorial.gd`）独立于 main 对局逻辑：进场 `reset_run` + `delete_save` 隔离，出场再 reset 并强制 `Engine.time_scale = 1`；开始面板「教程」按钮进入，Esc 退出。运行期代码创建的节点要取引用保存，不要用 `get_node("ClassName")`（自动名是 `@CanvasLayer@N` 形式）。

## 数值调参

- 所有可调数值集中在 `data/balance.json`（玩家/敌机/精英/Boss/刷怪/母舰/buff/里程碑/难度/效果分层）；**调参只改 JSON，不改脚本常量**。脚本内的同名 var 是回退默认值，必须与 JSON 保持一致。
- 访问统一走 `GameState.cfg("player.fuel.drain" 式路径, 默认值)`；每帧热路径禁止直接 cfg 查询，在 `_ready()` 一次性读进成员变量（参照 player.gd `_load_balance()`）。

## 语言（中英双语）

- 文案一律走 `tr("KEY")`，key 用英文大写蛇形（`UI_SCORE`、`BUFF_POWER_SHOT_NAME`、`TUT_S1_TITLE`）；**新增 UI 文案必须同时在 `data/translations.csv` 加 zh/en 两列**（改后需重新 import 生成 .translation）。
- GameState 启动时手动加载 `translations.zh/en.translation` 并应用 profile 里的 `locale`；切换用 `GameState.set_locale("zh"/"en")`（落盘 + `locale_changed` 信号），各 UI 监听信号刷新文本。
- 动态拼接文本用带 `%d`/`%s` 占位的 key（如 `MS_STAY "驻留 %ds"`）。

## 性能约定（3.4）
- 产弹一律走 `GameState.bullet_pool.fire()`：活跃弹挂 Main 下（清场/测试遍历可见），回收回 BulletPool 节点；外部 queue_free 由子弹 `_exit_tree` 自动 forget，不会污染池。
- 敌机一律走 `GameState.enemy_pool.spawn()`（enemy_pool.gd，模式同子弹池）：`reactivate()` 全状态重置（计时/策略/HP/调制色/died 断连），`deactivate()` 注销注册表并断开 died 监听；`USE_POOL=false` 可回退纯 instantiate/free 做 A/B 对照。直接实例化（测试）走 `_ready` 兼容路径，互不影响。
- 敌机三角函数统一 `Enemy.sin_fast/cos_fast`（2048 项循环表 + 线性插值，静态共享），禁止在 `_physics_process` 直接调 sin/cos。
- 爆炸走 `Explosion.spawn_at`（静态池 ≤24，发射完回收不销毁）。
- 热路径禁止每帧 `get_nodes_in_group`：用 `GameState.enemies` / `GameState.player_ref` 注册表（enemy/boss/player 在 `_ready`/`_exit_tree` 维护）。
- HUD 仪表类轮询 0.1s 节流 + 文本/格子值变化才重排（缓存上次值）；文本走信号。
- 基准：`godot --headless --fixed-fps 1000 --path . res://test/perf_bench.tscn`（无头默认实时锁帧，必须 `--fixed-fps` 才能测出纯帧耗时；本机噪声大，A/B 对照用交错跑取中位数）。

## 持久化与安全注意

- 对局存档 `user://savegame.json`（暂停菜单「保存进度」可写 + 返航自动更新，仅死亡删除）与局外档案 `user://profile.json`（仅最高分；局外天赋系统已移除，旧 talents 字段读取时忽略），逻辑都在 `autoload/game_state.gd`，均带 `version` 字段。
- 无网络代码、无第三方依赖、无密钥；唯一外部交互是上述 user:// 本地文件。

## 数值平衡参考

大致参考量级即可，不逐行对齐：

- `../airwar-game/airwar/config/game_config.py`、`config/difficulty_config.py` — 全部平衡数值
- `../airwar-game/airwar/entities/player.py`、`entities/player_components/` — 玩家行为
- `../airwar-game/airwar/entities/enemy/` — 敌机移动模式
- `../airwar-game/airwar/systems/difficulty_manager.py` — 难度乘数公式
- `../airwar-game/airwar/game/buffs/buffs.py` — buff 设计
