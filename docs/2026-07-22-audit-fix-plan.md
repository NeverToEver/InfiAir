# 2026-07-22 审计修复与 UX 加固计划（执行记录）

来源：2026-07-22 静态审计报告（死代码/死循环/调用错误三类，全部条目已源码复核）。
完成定义：对应修复合入且全部无头测试 0 FAIL（`--import` / `--quit-after 300` / 全部 `test/*.tscn`）。

**执行状态**（2026-07-24 对照代码复核）：P0/P1 五项已全部修复（提交 `b02be46`），验证断言已落地，复跑 0 FAIL；P2 已完成 2 项，其余待办（行号为复核时现状）。

## P0 — 致命/高危（✅ 全部完成）

### 1. F1 欢迎页 + 有存档时键盘绕过导致误删存档 ✅
- 问题：`has_save() && !welcome_seen` 组合（如 profile 损坏被隔离后）下开始面板在欢迎页下抢显并持焦点，Enter 绕过欢迎页直接「继续对局」，随后「新游戏」误删存档。
- 修复：`main.gd:79` 改为 `if GameState.has_save() and not $WelcomeScreen.visible: _start_panel.show_panel()`。
- 验证：`startup_flow_test` F1 用例（伪造存档 + `welcome_seen=false`：面板不抢显、无焦点、Enter 不触发继续对局、不删档；`dismiss()` 后面板正常）。

### 2. F2 读档恢复对嵌套字段类型零校验 ✅
- 问题：`apply_run_save()`（buffs/missions/routes）与 `_on_continue_run`（fuel）对 JSON 嵌套字段直接类型收窄，手改存档（如 `"buffs": [1,2]`）即运行时崩溃。
- 修复：`game_state.gd` 新增 `save_num()` 判型回退 + 逐字段 `is Dictionary` 校验，异常字段回默认值（与顶层"损坏即降级"策略一致）。
- 验证：`startup_flow_test` F2 用例（语法合法但结构非法的存档：继续对局不崩、各字段回默认）。

### 3. H1 Buff 三选一实际只出 2 张卡 ✅
- 问题：`buff_select.gd` `available.slice(0, 2)`（slice end 排他）只取 2 张。
- 修复：改 `slice(0, 3)`（现 `buff_select.gd:157`）。
- 验证：`smoke_test:53` 断言 `_current_available.size() == 3`（池未满时）。

## P1 — 用户可感问题（✅ 全部完成）

### 4. H2 教程两条永久软锁路径 ✅
- a. 玩家死亡：阶段 4 依赖 `mothership.departed`，死亡时母舰 `queue_free()` 不发信号 → 卡死。修复：`tutorial.gd` 监听 `player_died`，显示「任务失败，Esc 退出」。
- b. 阶段 6 Boss 50s 未狂暴逃跑：修复：监听 `boss.died`，未狂暴离场则重置阶段 6 重刷 Boss。
- 验证：`test/tutorial_test.tscn` 覆盖两条路径（死亡进失败态；逃跑后重置重刷）。

### 5. H3 Boss 血条狂暴红不重置 ✅
- 修复：`hud.gd:309` `show_boss_bar()` 首行 `_boss_bar.fill_color = UITheme.ACCENT`。
- 验证：`boss_enrage_test:213` 断言第二只 Boss 开场血条为 ACCENT。

## P2 — 建议项

### 已完成
- `ui_theme.gd make_panel_style()` — 已随 UI 重构（`57c778b`）删除。
- `base_console.gd` 四个 `_build_*` 返回类型 — 已改为 `Control`。

### 待办
- 死代码删除：`main.gd:19-20` 未用的 `_buff_ui`/`_pause_ui`；`hud.gd:65-67` 恒假分支（`_tag_labels` 在 `_build_backplates()` 才赋值，`_ready` 中该判断恒 false）；`game_state.gd:482` `ACTION_LABELS`；四个零 connect 信号 `rp_changed`/`mission_completed`/`route_chosen`（`game_state.gd:11-13`）与 `back_pressed`（`settings_ui.gd:6`）；`mothership.gd:173` HOVER 死分支（已注释"兼容保留"，可删）；`pause_ui.gd:77` `toggle()`（改测试直调 open/close 后删）。
- 母舰 `_start_release()`（`mothership.gd:367`）幂等守卫（`_state == RELEASE/DEPART` 直接 return），消除警告强制离舰 + 同帧 H 蓄满的二次进入。
- `profile_corrupt` 生产侧消费：开始面板只提示 `save_corrupt`，需加损坏档案提示或修正 AGENTS.md 描述。
- 配置信任边界：`boss.gd:127` `hp_mults` 下标无钳制；`game_state.gd:94` `DIFFICULTY_DEFS` 为整体替换，宜改合并式覆盖；`game_state.gd:766` 里程碑 while 无循环上界。
- 对象池 `_exit_tree` 未清空 `GameState.bullet_pool`/`enemy_pool`（对齐 `camera_ref` 模式）。

## 回归基线（每阶段完成后必跑）

```bash
~/.local/bin/godot --headless --import --path .
~/.local/bin/godot --headless --path . --quit-after 300
for t in smoke_test esc_navigation_test base_system_test startup_flow_test back_navigation_test boss_enrage_test enemy_combat_test buff33_test view_zoom_test tutorial_test pool_reuse_test; do
  ~/.local/bin/godot --headless --path . res://test/$t.tscn
done
```

特殊项：`perf_bench.tscn` 需 `--fixed-fps 1000`；`autoplay_test.tscn` 为 ≥8 分钟真实时间探针（用法见 AGENTS.md）。
