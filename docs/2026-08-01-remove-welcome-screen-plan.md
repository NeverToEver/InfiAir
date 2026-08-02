# 移除首启欢迎页（WelcomeScreen），启动直达主菜单 — 实施计划

> **执行方式：** 本计划由当前会话按任务顺序 inline 执行，每任务完成即跑对应验证；全部完成后跑全量 29 断言场景并提交推送。
> 计划日期：2026-08-01。

**Goal:** 移除进游戏后的「按任意键或点击出击」首启欢迎页（WelcomeScreen），启动 `main.tscn` 后直接显示开始面板（主菜单），无存档/有存档两种情况下均直达。

**Architecture:** 欢迎页是 main 场景内的一个 CanvasLayer（layer=35），由 `scripts/welcome_screen.gd` 实现：首次启动（profile 的 `welcome_seen` 未置位且进程内未展示过）时显示、暂停游戏、隐藏 StartPanel，任意键/点击后 `dismiss()` 置位 `welcome_seen` 并调 `StartPanel.show_panel()`。移除方案为**整体删除**：删场景节点与脚本、`GameState.welcome_seen` 字段及其 profile 读写、BackNavigator 对欢迎页的引用与分支、测试里所有「跳过/关闭欢迎页」样板；StartPanel 与 main 的既有自显逻辑（无存档 `_ready()` 自显、有存档 `main.gd` 调 `show_panel()`）即构成直达主菜单，无需新增逻辑。

**Tech Stack:** Godot 4.6 + GDScript（gl_compatibility）；测试为 `test/*.tscn` 无头场景自检（`[PASS]`/`[FAIL]` + 退出码）。

## Global Constraints

- 仅删除欢迎页专属翻译键 `WELCOME_SUBTITLE` / `WELCOME_CONTROLS` / `WELCOME_PROMPT`；**保留 `WELCOME_HIGH_SCORE`**（`scripts/start_panel.gd:236` 仍在使用）。
- 历史/审计文档不改：`docs/AUDIT_VAULT.md`（专有档案）、`docs/archive/*`（历史溯源）、`docs/2026-07-22-audit-fix-plan.md`、`docs/2026-08-01-godot-best-practice-audit.md`（历史记录）。
- 不新增用户可见文本；既有文本遵循 `tr()` 中英双语。
- 提交信息遵循仓库风格：`refactor: 主题（2026-08-01）`。
- 行为约束：移除欢迎页后，启动时开始面板必须可见且 `get_tree().paused == true`（主菜单冻结背景）；`BackNavigator` 顶层 Esc 仍走退出确认。
- profile 旧档案残留的 `welcome_seen` 键宽松忽略（`load_profile` 已按缺键回退），**不** bump `PERSIST_VERSION`。

## 文件结构

| 文件 | 动作 | 职责 |
| --- | --- | --- |
| `scenes/main.tscn` | 修改 | 删 `[ext_resource] welcome_screen.gd`（id="16"）与 `[node name="WelcomeScreen"]` 节点 |
| `scripts/welcome_screen.gd` + `.uid` | 删除 | 欢迎页脚本（唯一实现，无复用） |
| `scripts/main.gd` | 修改 | `:121` guard 去掉 `not $WelcomeScreen.visible`，更新注释 |
| `scripts/back_navigator.gd` | 修改 | 删 `_welcome` 引用；`:118` 分支与枚举注释去「欢迎页」 |
| `scripts/start_panel.gd` | 修改 | `:5` 注释去「由 welcome_screen 先行覆盖」 |
| `autoload/game_state.gd` | 修改 | 删 `welcome_seen` 字段（`:163`）、读取（`:949`）、写入（`:988`） |
| `data/translations.csv` | 修改 | 删 3 个 WELCOME_* 键（保留 WELCOME_HIGH_SCORE） |
| `test/startup_flow_test.gd` | 修改 | 第 3 节改「无存档直达」，第 6 节 F1 改「有存档直达」，删欢迎页断言 |
| 其余 21 个 `test/*.gd` | 修改 | 删 `GameState.welcome_seen = ...` 行与 `get_node("Main/WelcomeScreen")` + 隐藏/dismiss 样板（清单见 Task 3） |
| `docs/DESIGN_BASELINE.md` | 修改 | `:119` 页面层级、`:144` 节点清单去 WelcomeScreen |
| `docs/ARCHITECTURE.md` | 修改 | `:40` 节点树、`:64` 脚本职责描述去 welcome_screen |
| `docs/EXIT_FLOW.md` | 修改 | `:17` L0 顶层去「⇐ WelcomeScreen（仅首次启动）」 |
| `docs/2026-08-01-remove-welcome-screen-plan.md` | 创建 | 本计划书 |

---

### Task 1: 游戏侧核心移除

**Files:**
- Modify: `scenes/main.tscn:18,185-188`
- Delete: `scripts/welcome_screen.gd`、`scripts/welcome_screen.gd.uid`
- Modify: `scripts/main.gd:118-122`
- Modify: `scripts/back_navigator.gd:19,30,118`
- Modify: `scripts/start_panel.gd:1-8`
- Modify: `autoload/game_state.gd:163,949,988`

- [ ] **Step 1: 删除 main.tscn 中欢迎页资源与节点**

`scenes/main.tscn`：
- 删第 18 行 `[ext_resource type="Script" path="res://scripts/welcome_screen.gd" id="16"]`
- 删 185-188 行节点（`[node name="WelcomeScreen" type="CanvasLayer" parent="."]` 及其 `process_mode`/`layer`/`script` 三行）

- [ ] **Step 2: 删除欢迎页脚本文件**

`git rm scripts/welcome_screen.gd scripts/welcome_screen.gd.uid`（或 `rm` + 之后 `git add -A`）。

- [ ] **Step 3: 简化 main.gd 存档恢复 guard**

`scripts/main.gd:121`：`if GameState.has_save() and not $WelcomeScreen.visible:` → `if GameState.has_save():`
同步更新 118-120 注释：

```gdscript
	# 有存档则显示开始面板；无存档时开始面板由自身逻辑自显（并非"直接开新局"）。
	if GameState.has_save():
		_start_panel.show_panel()
```

- [ ] **Step 4: 移除 BackNavigator 欢迎页引用**

`scripts/back_navigator.gd`：
- 删 `:30` `@onready var _welcome: CanvasLayer = get_parent().get_node("WelcomeScreen")`
- `:118` `if _start_panel.visible or _welcome.visible:` → `if _start_panel.visible:`
- `:19` 枚举注释 `CONFIRM_EXIT,  # 顶层（开始面板/欢迎页）→ 弹出全局退出确认` → `CONFIRM_EXIT,  # 顶层（开始面板）→ 弹出全局退出确认`

- [ ] **Step 5: 更新 start_panel.gd 头部注释**

`scripts/start_panel.gd:5` 删行 `## 每进程首次进游戏由 welcome_screen 先行覆盖，其 dismiss() 会调 show_panel()。`

- [ ] **Step 6: 移除 GameState.welcome_seen**

`autoload/game_state.gd`：
- 删 `:163` `var welcome_seen: bool = false`
- 删 `:949` `welcome_seen = save_bool(parsed.get("welcome_seen", false), false)`
- 删 `:988` `"welcome_seen": welcome_seen,`

- [ ] **Step 7: 语法验证**

Run: `godot --headless --import --path .`
Expected: 导入完成无脚本错误（此时测试文件尚未清理，可能有未删引用报错——见 Task 3 前置说明）。

> 注：GameState/BackNavigator/main.gd 改动后，未清理的测试文件可能触发编译错误（`welcome_seen` 属性不存在、`WelcomeScreen` 节点缺失）。为保持每任务可独立验证，Task 1 Step 7 以「游戏侧无编译错误、报错仅剩 test/ 内已知引用」为准；全部测试清理在 Task 3 完成后统一以 `--import` + 全量测试收口。

---

### Task 2: 翻译键清理

**Files:**
- Modify: `data/translations.csv:133,135,136`

- [ ] **Step 1: 删除欢迎页专属翻译键**

`data/translations.csv` 删除 3 行（**保留 134 行 `WELCOME_HIGH_SCORE`**）：
- `WELCOME_SUBTITLE,"无尽空战 · 出击待命","Endless skies · Awaiting launch"`
- `WELCOME_CONTROLS,"WASD 移动 · 鼠标瞄准 · 空格冲刺 · H 召唤母舰 · B 返航整备",...`
- `WELCOME_PROMPT,"— 按任意键或点击出击 —","— Press any key or click to launch —"`

- [ ] **Step 2: 校验无遗留引用**

Run: `grep -rn "WELCOME_SUBTITLE\|WELCOME_CONTROLS\|WELCOME_PROMPT" scripts/ test/ autoload/ scenes/`
Expected: 无匹配。

---

### Task 3: 测试文件清理（21 个）

**Files（按两类样板）：**

A 类 — 删「跳过后复位 + 隐藏/关闭欢迎页」样板（通常 2-3 行，结构为 `GameState.welcome_seen = true/false` 与/或 `var welcome = get_node(...)` + `if welcome.visible: welcome.dismiss()`）：

| 文件 | 位置（行号以当前 HEAD 为准，编辑前先 Read） |
| --- | --- |
| `test/meta_health_fx_test.gd` | `:32` |
| `test/boss_phase_test.gd` | `:66-68` |
| `test/view_zoom_test.gd` | `:119-121` |
| `test/mothership_summon_test.gd` | `:21` |
| `test/enemy_combat_test.gd` | `:48-50` |
| `test/elite_turret_event_test.gd` | `:76-78` |
| `test/formation_strike_event_test.gd` | `:81-83` |
| `test/buff_visuals_test.gd` | `:25-27` |
| `test/esc_navigation_test.gd` | `:46-48` |
| `test/smoke_test.gd` | `:25-27` |
| `test/meta_fx_capture.gd` | `:12` |
| `test/return_cinematic_test.gd` | `:61` |
| `test/orbital_strike_test.gd` | `:20` |
| `test/intro_cinematic_test.gd` | `:41` |
| `test/boss_enrage_test.gd` | `:103-105` |
| `test/autoplay_test.gd` | `:265-267` |
| `test/buff33_test.gd` | `:28-30` |

B 类 — 额外的存档/恢复/模式分支需整体处理：

| 文件 | 处理 |
| --- | --- |
| `test/back_navigation_test.gd` | `:32` `welcome_seen=false` 删；`:40` 删 `get_node`；`:51-52` 欢迎页顶层决策断言删除（保留开始面板顶层断言：`:51` 改纯 `_start_panel.visible` 分支，需 Read 后按其上下文改写为「开始面板（顶层）：决策=退出确认」） |
| `test/ui_capture.gd` | `:5` 注释、`:13-14` 保存/置位、`:21-23` 隐藏、`:118` 还原，全部删除（原值保存/还原不需要） |
| `test/hud_capture.gd` | 同上模式（`:5,:11,:13,:18-20,:74`） |
| `test/buff_panel_test.gd` | `:32,:35,:103` 保存/置位/还原删除 |
| `test/visual_capture.gd` | MODE 列表（`:5` 注释）去 `welcome`；`:18-28` 中 `MODE=="welcome"` 分支与 `MODE != "welcome"` 条件删除（`MODE != "start_panel"` 保留）；`:35` 的 `"welcome":` 分支删除 |
| `test/startup_flow_test.gd` | 单独处理，见 Task 4 |

- [ ] **Step 1: 逐文件删除样板**

对 A 类 17 个文件：删除上表所示行（编辑前 Read 确认上下文，删除后保证语法完整）。样板模式示例（以 `test/smoke_test.gd` 为准）：

```gdscript
	var welcome: CanvasLayer = get_node("Main/WelcomeScreen")
	if welcome.visible:
		welcome.dismiss()
```

与 `GameState.welcome_seen = true  # 跳过欢迎页...` 整行删除。

- [ ] **Step 2: 处理 B 类 5 个文件（除 startup_flow_test）**

按上表 Read 后精确编辑。

- [ ] **Step 3: 编译校验**

Run: `godot --headless --import --path . 2>&1 | grep -iE "error|script" | head -20`
Expected: 无 `welcome_seen` / `WelcomeScreen` 相关报错。

---

### Task 4: startup_flow_test 重构

**Files:**
- Modify: `test/startup_flow_test.gd:59-95,116-151,186`

- [ ] **Step 1: 第 3 节改为「无存档启动直达开始面板」**

将 `:59-95` 整节（欢迎页 → 面板）替换为：

```gdscript
	# ---------- 3. 键盘-only 链路：无存档启动直达开始面板，Enter 开新局 ----------
	GameState.delete_save()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame
	var start_panel: CanvasLayer = get_node("Main/StartPanel")
	_check(start_panel.visible, "无存档启动直达开始面板")
	_check(get_tree().paused, "开始面板显示期间游戏暂停")
	_check(start_panel.get_viewport().gui_get_focus_owner() == start_panel.new_button(), "无存档时主按钮（开始游戏）持有焦点")
	_check(not start_panel.corrupt_label().visible, "无损坏存档时提示隐藏")

	# 按钮 action_mode 默认为释放触发：pressed + released 才算一次完整点击
	var accept := InputEventAction.new()
	accept.action = &"ui_accept"
	accept.pressed = true
	Input.parse_input_event(accept)
	await get_tree().process_frame
	var accept_up := InputEventAction.new()
	accept_up.action = &"ui_accept"
	accept_up.pressed = false
	Input.parse_input_event(accept_up)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(not start_panel.visible and not get_tree().paused, "Enter 触发开始游戏并恢复运行")
```

- [ ] **Step 2: 第 6 节 F1 改为「有存档启动直达开始面板」**

将 `:116-151` 整节替换为：

```gdscript
	# ---------- 6. 有存档启动：直达开始面板，主按钮为继续对局 ----------
	GameState.save_run(50.0, 10.0)
	get_node("Main").queue_free()
	await get_tree().process_frame
	await get_tree().process_frame
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame
	var start_panel2: CanvasLayer = get_node("Main/StartPanel")
	_check(start_panel2.visible, "有存档启动直达开始面板")
	_check(get_tree().paused, "有存档启动后面板暂停游戏")
	_check(
		start_panel2.get_viewport().gui_get_focus_owner() == start_panel2.continue_button(),
		"有存档时焦点在继续对局"
	)
	# Enter 直接触发继续对局（无欢迎页拦截）
	var continued := [false]
	start_panel2.continue_chosen.connect(func() -> void: continued[0] = true)
	var enter := InputEventKey.new()
	enter.keycode = KEY_ENTER
	enter.pressed = true
	Input.parse_input_event(enter)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(not start_panel2.visible, "Enter 触发继续对局并关闭面板")
	_check(get_tree().paused, "继续对局恢复运行")
	_check(continued[0], "Enter 触发 continue_chosen")
	_check(GameState.has_save(), "继续对局未删档")
```

- [ ] **Step 3: 清理文件头注释与结尾**

- `:2-3` 注释改为：`## 启动链路测试：损坏存档/档案的隔离恢复、启动直达开始面板键盘-only 链路、`
  `## 主按钮焦点策略（无存档=开始游戏，有存档=继续对局）、损坏提示显隐。`
- 结尾 `:186` `GameState.welcome_seen = false` 删除（保留 `GameState.save_profile()`）。

- [ ] **Step 4: 单测验证**

Run: `godot --headless --path . res://test/startup_flow_test.tscn 2>&1 | tail -8`
Expected: `[DONE] failures=0`，退出码 0。

---

### Task 5: 文档同步

**Files:**
- Modify: `docs/DESIGN_BASELINE.md:119,144`
- Modify: `docs/ARCHITECTURE.md:40,64`
- Modify: `docs/EXIT_FLOW.md:17`

- [ ] **Step 1: DESIGN_BASELINE**

- `:119` `→ L0 StartPanel⇐WelcomeScreen。` → `→ L0 StartPanel。`
- `:144` `├─ StartPanel / WelcomeScreen / ExitConfirm` → `├─ StartPanel / ExitConfirm`

- [ ] **Step 2: ARCHITECTURE**

- `:40` `├─ StartPanel / WelcomeScreen / ExitConfirm` → `├─ StartPanel / ExitConfirm`
- `:64` 脚本清单去掉 `welcome_screen.gd`：`...start_panel.gd`、`exit_confirm.gd`：页面和覆盖层。...`

- [ ] **Step 3: EXIT_FLOW**

- `:17` `L0 顶层:  StartPanel（主界面/大厅）⇐ WelcomeScreen（仅首次启动）` → `L0 顶层:  StartPanel（主界面/大厅）`

- [ ] **Step 4: 交叉检查**

Run: `grep -rn "welcome" docs/DESIGN_BASELINE.md docs/ARCHITECTURE.md docs/EXIT_FLOW.md`
Expected: 仅剩历史语境条目或无匹配（历史文档 AUDIT_VAULT/archive/计划档案不在此检查内）。

---

### Task 6: 全量验证

- [ ] **Step 1: 导入与启动**

Run: `godot --headless --import --path . && godot --headless --path . --quit-after 300`
Expected: 无错误，退出码 0。

- [ ] **Step 2: 全量断言场景**

Run: `for t in $(ls test/*_test.tscn | grep -v autoplay_test); do godot --headless --path . "res://$t" ...; done`
Expected: 29 场景全部 `failures=0`，退出码 0（重点：startup_flow / back_navigation / smoke / esc_navigation）。

- [ ] **Step 3: 复核**

Run: `grep -rn "welcome_seen\|WelcomeScreen\|welcome_screen" scripts/ autoload/ scenes/ test/`
Expected: 仅 `test/visual_capture.gd:5` 注释等历史语境残留为可接受（若存在则一并清理），脚本/场景/测试代码零引用。

---

### Task 7: 提交并推送

- [ ] **Step 1: 提交**

```bash
git add -A
git commit -m "refactor: 移除首启欢迎页——启动直达主菜单（2026-08-01）"
```

- [ ] **Step 2: 推送**

```bash
git push origin main
```

---

## 自我复核

- **规格覆盖**：Goal（直达主菜单）由 Task 1（游戏侧）+ Task 3/4（测试）+ Task 5（文档）覆盖；「先写计划书归档」= 本文档；「跑验证」= Task 6；「推送」= Task 7。✅
- **占位符扫描**：无 TBD；测试样板与替换代码均给出具体内容。✅
- **类型一致性**：`welcome_seen` / `WelcomeScreen` 在 Task 1-7 中一致为删除对象；`WELCOME_HIGH_SCORE` 保留依据 `start_panel.gd:236` 事实。✅
- **风险**：测试文件多，删除样板后若上下文残留半行会编译失败——Task 3 Step 3 与 Task 6 Step 1 的 import 校验兜底；visual_capture 的 `MODE=="welcome"` 分支需整块处理，已列入 B 类。✅
