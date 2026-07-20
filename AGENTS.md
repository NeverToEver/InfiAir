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
- 不引入外部插件；不改 `project.godot` 的 autoload 与既有输入映射（追加新映射允许，已追加：`dash`=空格、`dock`=H、`homecoming`=B）。
- 持久化：对局存档 `user://savegame.json`（暂停菜单可写 + 返航自动更新，仅死亡删除）与局外档案 `user://profile.json`（仅最高分；局外天赋系统已在 3.2 移除，旧 talents 字段读取时忽略），逻辑都在 `autoload/game_state.gd`，均带 `version` 字段。测试运行会读写这两个文件，结束后需清理残留（冒烟测试已自行清理）。
- 返航 = 局内中场整备：长按 B 蓄力（main.gd `_process` 计时），`scripts/base_console.gd` 基地控制台（战机库/武器挂载/维修补给/任务规划），「继续出击」轨道打击清屏后返回同一局（Boss 保留）；RP/任务/天赋路线数据层在 game_state.gd（见 base_system_test）。
- 敌机数值集中在 `scripts/spawner.gd` 的 `ENEMY_TYPES` / `ELITE_TYPES`（static var，非 const：含 Vector2i 构造非常量表达式），7 种移动策略在 `scripts/enemy.gd`；Boss 3 种轮换与狂暴逻辑在 `scripts/boss.gd`（类型由 `boss_kills % 3 + 1` 决定）。纯得分制：不要引入任何掉落/拾取机制。
- 母舰（`scripts/mothership.gd`）是 7 态状态机（DESCEND/HOVER/DOCKING/RESUPPLY/STAY/RELEASE/DEPART）：长按 H 蓄力召唤（main 管理，虚影预告）、对接驻留 20s 弹匣制、长按 H 2s 提前离舰冷却打折；加特林为双塔 80° 扫射压制，弹丸 `score_scale=1/3`（击毁结算向下取整，enemy/boss 的 `take_damage(amount, score_scale)` 链路）；对接序列锁输入用 `player._input_locked`，与暂停/清场逻辑兼容。
