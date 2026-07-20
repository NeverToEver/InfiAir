# AGENTS.md

Godot 4.6 GDScript 项目（gl_compatibility 渲染器），Python 游戏 `../airwar-game` 的重制版。

## 运行 / 验证命令

```bash
# 无头导入（验证资源与脚本解析）
~/.local/bin/godot --headless --import --path .
# 无头跑 300 帧（验证无运行时错误）
~/.local/bin/godot --headless --path . --quit-after 300
# 无头冒烟测试（Buff UI / Boss / 结算 / 暂停路径）
~/.local/bin/godot --headless --path . res://test/smoke_test.tscn
# 本地运行
godot --path .
```

注意：无头模式帧率不封顶，`--quit-after N` 的帧数不等于真实秒数；需要时间相关行为时用真实时间等待（参考 `test/smoke_test.gd`）。

## 目录与约定

- 场景放 `scenes/`，同名脚本放 `scripts/`；`autoload/game_state.gd` 是全局状态与信号总线（autoload 名 `GameState`），内含常驻音效池（`GameState.play_sfx()`）与 `screen_shake` 信号。
- `scripts/tools/generate_audio.py` 是一次性音频程序合成脚本（仅 Python 标准库），产物已提交到 `assets/audio/`；需要重做音效时改参数重跑即可。
- BGM 循环只设 `stream.loop_mode = LOOP_FORWARD`；不要显式写 `loop_begin/loop_end` 或在 `_exit_tree` 里 `stop()`，否则退出时播放实例会泄漏（已在无头验证中复现）。
- 碰撞层：1=player，2=player_bullet，3=enemy（含 boss），4=enemy_bullet。子弹负责结算伤害（玩家弹检测 enemy 组，敌弹/敌机撞击检测 `player_hitbox` 组）。
- 玩家/敌弹共用 `scenes/bullet.tscn`，用 `setup()` 区分阵营；爆炸为纯代码构建的 `Explosion`（GPUParticles2D 一次性）。
- 实体 `setup()` 在 `_ready()` 之前被调用，其中不能用 `@onready` 变量，需用 `$节点路径` 访问子节点。
- 暂停类 UI（Buff/结算/暂停）`process_mode = Always`，用 `get_tree().paused` 控制。

## 数值平衡参考

大致参考量级即可，不逐行对齐：

- `../airwar-game/airwar/config/game_config.py`、`config/difficulty_config.py` — 全部平衡数值
- `../airwar-game/airwar/entities/player.py`、`entities/player_components/` — 玩家行为
- `../airwar-game/airwar/entities/enemy/` — 敌机移动模式
- `../airwar-game/airwar/systems/difficulty_manager.py` — 难度乘数公式
- `../airwar-game/airwar/game/buffs/buffs.py` — buff 设计

## GDScript 风格

- 遵循 GDScript 官方风格：Tab 缩进、类型标注、Godot 4 信号语法（`signal_name.emit()` / `signal_name.connect()`）。
- 私有成员加 `_` 前缀；常量用 `CONSTANT_CASE` 并集中在文件头部。
- 不引入外部插件；不改 `project.godot` 的 autoload 与既有输入映射（追加新映射允许，如 2.2 迭代的 `dash`=空格）。
