# 2026-07-22 审计修复与 UX 加固计划

来源：2026-07-22 静态审计报告（死代码/死循环/调用错误三类，全部条目已源码复核）。
目标：按优先级清掉审计发现的致命/高危项，再择期清理建议项；每项附验证方式。
完成定义：对应修复合入且全部无头测试 0 FAIL（`--import` / `--quit-after 300` / 全部 `test/*.tscn`）。

## P0 — 立即修（数据丢失 / 崩溃 / 核心玩法缺陷）

### 1. F1 欢迎页 + 有存档时键盘绕过导致误删存档
- 现状：`main.gd` `_ready()` 在 WelcomeScreen 隐藏开始面板后又无条件 `show_panel()` 并 `grab_focus()`；GUI 焦点不看 layer 遮挡，Enter 可绕过欢迎页直接「继续对局」，随后 `dismiss()` 再次 `show_panel()` 覆盖进行中的对局，点「新游戏」误删存档。
- 触发组合：`has_save() && !welcome_seen`（profile 损坏被隔离后即此组合）。
- 修复：`main.gd` 改为 `if GameState.has_save() and not $WelcomeScreen.visible: _start_panel.show_panel()`。
- 验证：`test/startup_flow_test.gd` 新增组合用例——伪造存档 + `welcome_seen=false` 实例化 main：断言开始面板不可见且无焦点，模拟 Enter 不得触发继续对局；`dismiss()` 后面板正常。

### 2. F2 读档恢复对嵌套字段类型零校验
- 现状：`game_state.gd` `apply_run_save()`（buffs/missions/routes）与 `main.gd` `_on_continue_run`（fuel）对 JSON 嵌套字段直接类型收窄，手改存档（如 `"buffs": [1,2]`）即运行时崩溃。
- 修复：逐字段 `is Dictionary`/`is Array`/数值判型，异常字段回默认值（与顶层"损坏即降级"策略一致）。
- 验证：`startup_flow_test` 新增"结构非法但语法合法的存档"用例：继续对局不崩、异常字段回默认。

### 3. H1 Buff 三选一实际只出 2 张卡
- 现状：`buff_select.gd:164` `available.slice(0, 2)`（Godot 4 slice end 排他）→ 只取 2 张。
- 修复：改 `slice(0, 3)`。
- 验证：`smoke_test` 里程碑弹卡处加 `_current_available.size() == 3`（池未满时）断言。

## P1 — 用户可感问题

### 4. H2 教程两条永久软锁路径
- a. 玩家死亡无处理：阶段 4 依赖 `mothership.departed`，死亡时母舰 `queue_free()` 不发信号 → 永久卡死。修复：`tutorial.gd` 监听 `GameState.player_died`，显示「任务失败，Esc 退出」（或重生满血重置当前阶段）。
- b. 阶段 6 Boss 50s 未狂暴逃跑 → 未监听 `escaped`/`died` → 永久卡死。修复：监听 `boss.died`，未狂暴离场则重置阶段 6（重刷 Boss）或判定失败。
- 验证：教程测试（如无则新建 `test/tutorial_test.tscn`）覆盖死亡与 Boss 逃跑两条路径。

### 5. H3 Boss 血条狂暴红不重置
- 修复：`hud.gd` `show_boss_bar()` 首行 `_boss_bar.fill_color = UITheme.ACCENT`。
- 验证：`boss_enrage_test` 加"第二只 Boss 开场血条为 ACCENT"断言。

## P2 — 建议项（择期清理，可一次清扫）

- 死代码删除：`main.gd:21-22` 未用的 `_buff_ui`/`_pause_ui`；`hud.gd:62-64` 恒假分支；`ui_theme.gd make_panel_style()`；`game_state.gd ACTION_LABELS`；四个零 connect 信号（`rp_changed`/`mission_completed`/`route_chosen`/`back_pressed`，确认非预留后连同 emit 删除）；`mothership.gd` HOVER 死分支；`pause_ui.toggle()`（改测试直调 open/close 后删）。
- 母舰 `_start_release()` 幂等守卫（`_state == RELEASE/DEPART` 直接 return），消除警告强制离舰 + 同帧 H 蓄满的二次进入。
- `profile_corrupt` 生产侧消费：开始面板加损坏档案提示，或修正 AGENTS.md 描述。
- `base_console.gd` 四个 `_build_*` 返回类型标注 `PanelContainer` → `Control`。
- 配置信任边界：`boss.gd` `hp_mults` 下标钳制；`DIFFICULTY_DEFS` 改合并式覆盖；里程碑 while 加循环上界。
- 对象池 `_exit_tree` 清空 `GameState.bullet_pool/enemy_pool`（对齐 `camera_ref` 模式）。

## 回归基线（每阶段完成后必跑）

```bash
~/.local/bin/godot --headless --import --path .
~/.local/bin/godot --headless --path . --quit-after 300
for t in smoke_test esc_navigation_test base_system_test startup_flow_test back_navigation_test boss_enrage_test enemy_combat_test buff33_test view_zoom_test; do
  ~/.local/bin/godot --headless --path . res://test/$t.tscn
done
```
