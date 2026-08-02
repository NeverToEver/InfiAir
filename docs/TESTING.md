# 本地运行、验证与测试

> 本文是 `AGENTS.md` 的按需读取参考文档：完整本地运行命令、专项测试场景、视觉截图工具与测试策略副作用明细。**最小必跑集与行为准则见 `AGENTS.md`**。

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

## 专项测试场景

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
godot --headless --path . res://test/mouse_lock_test.tscn
godot --headless --path . res://test/startup_flow_test.tscn
godot --headless --path . res://test/entry_animation_test.tscn
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

## 视觉截图（窗口模式）

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

## 测试策略与副作用

测试不是单元测试框架；每个 `test/*.tscn` 启动相应 GDScript 场景，并以 `[PASS]`/`[FAIL]` 输出和退出码自检。`test/` 下共 40 个场景：31 个断言场景，外加 `autoplay_test`（探针）、`perf_bench`（性能基准）、`visual_capture` / `ui_capture` / `return_capture` / `intro_capture` / `summon_capture` / `meta_fx_capture` / `hud_capture`（窗口模式截图工具）。

- 测试可能读写 `user://savegame.json` 与 `user://profile.json`。新测试应先 `GameState.delete_save()`，并在结束时清理或恢复自己创建的持久化状态，保证可重复执行。
- `test/balance_test.gd` 会暂时**覆盖项目内** `data/balance.json` 来验证损坏和回退路径，然后恢复原文件。不要在手工编辑该文件时并发运行它，也不要中断它后假设文件仍然完好。
- `test/autoplay_test.tscn` 是长时自动游玩与 `[ANOMALY]` 不变量监控探针，不以常规断言失败形式代表所有问题。注册表一致性按 "enemy" 组集合双向比对（含炮台/编队战机注册者，跳过池化 deferred 回收窗口）；另覆盖 Buff 卡确认动效路径（10% 真实三参选取）、返航过场期豁免的卡死计时、狂暴减速复位、buff 层数封顶与事件/Boss 阶段计数（SUMMARY 输出）。
- `test/perf_bench.tscn` 必须带 `--fixed-fps 1000`；无头默认帧率行为不适合直接比较纯帧耗时。做性能 A/B 时交错运行并使用中位数。
- 修改 UI 后使用窗口模式截图人工核对；headless 不会输出可用游戏截图。
- **既有失败基线**：`smoke_test` 的「母舰击杀 1/3 分」曾偶发失败（重跑可过），2026-08-01 复核已通过、应视为已自愈，若再现先排查近期改动。`hit_logic_test` 的 A21「Boss 入场降入期玩家弹可伤 Boss」曾登记为稳定失败基线（2026-07-31）；2026-08-01 复核通过实为 `user://profile.json` 视角档巧合（medium 档），根因未除——**2026-08-02 已根因修复**：测试硬编码绝对坐标 `(960,100)`，在 `view_zoom=large` 档（可见区顶缘 y=222）下玩家弹被 `view_world_rect(80)` 出界判定销毁、从未命中 Boss；现改按战斗锚线 `fight_anchor_y()` 动态定位，9 组合矩阵（视角档×难度）+ 多轮连跑全绿。根因与修复记录见 `docs/AUDIT_VAULT.md`「既有失败基线处置记录（A21）」。**A21 不再是失败基线**，涉及视角档或 Boss 锚线的改动后应重跑 `hit_logic_test`。
