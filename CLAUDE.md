# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**本仓库的权威约定文档是 `AGENTS.md`**（碰撞层、测试策略、性能约定、i18n、调参流程、GDScript 风格），开始任何改动前必读；本文件只提供入口级概览。玩法移植对齐见 `docs/PORTING_PARITY.md`，玩法变更需同步更新该文档对应行。

## 项目概览

InfiAir：2D 俯视空战射击游戏，Godot 4.6 + GDScript（gl_compatibility 渲染器，无外部插件），是 `../airwar-game`（Python/Pygame）的重制版。竖向卷动星空、波次敌机、里程碑 Buff 三选一、周期 Boss 战 + 狂暴、母舰补给、返航中场整备。纯得分制。

- 主场景 `scenes/main.tscn`，窗口 1920×1080（stretch = canvas_items / keep）。
- 唯一 autoload：`GameState`（`autoload/game_state.gd`）——全局状态/信号总线、音效池、`GameState.cfg()` 数值访问、存档持久化。
- 无构建系统、无包管理器、无 CI；唯一依赖是 Godot 4.6 编辑器/命令行。
- 本机环境：godot 4.6.2 标准版（无 .NET，故纯 GDScript），二进制在 `~/.local/bin/godot`（已加入 PATH，裸 `godot` 可用）。

## 常用命令

```bash
# 本地运行
godot --path .
# 无头导入（验证资源与脚本解析）
godot --headless --import --path .
# 无头跑 300 帧（验证无运行时错误）
godot --headless --path . --quit-after 300
# 无头测试（test/*.tscn 场景脚本，[PASS]/[FAIL] 自检，共 369 项断言）
godot --headless --path . res://test/smoke_test.tscn        # 主流程
godot --headless --path . res://test/base_system_test.tscn  # 存档/RP/任务/路线
godot --headless --path . res://test/enemy_combat_test.tscn # 敌机/Boss
godot --headless --path . res://test/buff33_test.tscn       # Buff/母舰
godot --headless --path . res://test/difficulty_test.tscn   # 难度/里程碑/设置
godot --headless --path . res://test/boss_enrage_test.tscn  # Boss 狂暴
godot --headless --path . res://test/balance_test.tscn      # 数值配置中心
godot --headless --path . res://test/keybind_test.tscn      # 可改键
godot --headless --path . res://test/i18n_test.tscn         # 中英双语
godot --headless --path . res://test/tutorial_test.tscn     # 新手教程
godot --headless --path . res://test/view_zoom_test.tscn    # 视角缩放
# 性能基准（必须 --fixed-fps 才能测出纯帧耗时）
godot --headless --fixed-fps 1000 --path . res://test/perf_bench.tscn
```

改动后至少跑：`--headless --import`、`--quit-after 300`、`smoke_test.tscn`；涉及存档/基地/母舰时加跑 `base_system_test.tscn`。视觉截图用 `test/visual_capture.tscn`（窗口模式运行，headless 截不到画面）。

## 架构要点（理解多文件协作的关键）

- `main.gd` 是对局编排核心：刷怪、里程碑 Buff、Boss 调度、母舰蓄力、返航计时都在此串联。
- 数值配置中心：所有可调数值在 `data/balance.json`，统一经 `GameState.cfg("player.fuel.drain" 式路径, 默认值)` 访问；**调参只改 JSON 不改脚本常量**，脚本内同名 var 是回退默认值需与 JSON 保持一致；热路径在 `_ready()` 一次性读入，禁止每帧 cfg 查询。
- 碰撞层：`1=player 2=player_bullet 3=enemy(含boss) 4=enemy_bullet`；子弹侧结算伤害；玩家受击只看 `Hitbox` Area2D（r=7）；敌机/Boss 身体撞击走逐帧 `overlaps_area` 轮询（对齐原作），狂暴前非致死伤害钳到 30% 阈值。
- 实体 `setup()` 在 `_ready()` 之前调用，其中不能用 `@onready` 变量，需用 `$节点路径`。
- 视角缩放三档：相机固定 (960,540) 只设 `zoom`，一切"屏幕边缘/刷怪位置"逻辑必须走 `GameState.view_world_rect()`，不得写死 1920×1080。
- 性能约定：子弹/敌机走对象池（`GameState.bullet_pool.fire()` / `enemy_pool.spawn()`），热路径禁止每帧 `get_nodes_in_group`（用 `GameState.enemies` / `player_ref` 注册表），禁止 `_physics_process` 直接调 sin/cos（用 `Enemy.sin_fast/cos_fast` 查表）。
- 双语：文案一律 `tr("KEY")`，新增 key 必须同时在 `data/translations.csv` 加 zh/en 两列（改后需重新 import）；切换走 `GameState.set_locale()`。
- 暂停类 UI `process_mode = Always`；BGM 循环只设 `loop_mode = LOOP_FORWARD`（不要在 `_exit_tree` 里 `stop()`，会泄漏播放实例）。
