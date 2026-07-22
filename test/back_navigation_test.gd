extends Node
## 返回/退出状态机测试：decide_back_action 全分支覆盖 + 真实 Esc 注入的集成路径。
## 设计文档：docs/EXIT_FLOW.md。TO_MAIN_MENU 分支只断言决策不执行（会重载测试场景）。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _press_esc() -> void:
	var ev := InputEventKey.new()
	ev.keycode = KEY_ESCAPE
	ev.pressed = true
	Input.parse_input_event(ev)
	await get_tree().process_frame
	await get_tree().process_frame
	var up := InputEventKey.new()
	up.keycode = KEY_ESCAPE
	up.pressed = false
	Input.parse_input_event(up)
	await get_tree().process_frame


func _ready() -> void:
	GameState.delete_save()
	GameState.welcome_seen = false
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	var main := get_node("Main")
	var nav := main.get_node("BackNavigator")
	var welcome: CanvasLayer = main.get_node("WelcomeScreen")
	var start_panel: CanvasLayer = main.get_node("StartPanel")
	var pause_ui: CanvasLayer = main.get_node("PauseUI")
	var settings_ui: CanvasLayer = main.get_node("SettingsUI")
	var base_ui: CanvasLayer = main.get_node("BaseUI")
	var buff_ui: CanvasLayer = main.get_node("BuffUI")
	var game_over_ui: CanvasLayer = main.get_node("GameOverUI")
	var exit_confirm: CanvasLayer = main.get_node("ExitConfirm")
	var A = nav.BackAction  # 枚举经实例访问为 Variant，不能用 := 推断

	# ---------- 1. 顶层：欢迎页 / 开始面板 → 退出确认 ----------
	_check(welcome.visible and nav.decide_back_action() == A.CONFIRM_EXIT, "欢迎页（顶层）：决策=退出确认")
	welcome.dismiss()
	_check(start_panel.visible and nav.decide_back_action() == A.CONFIRM_EXIT, "开始面板（顶层）：决策=退出确认")
	await _press_esc()
	_check(exit_confirm.visible and not exit_confirm._battle, "顶层 Esc：弹出退出确认（normal 模式）")
	_check(nav.decide_back_action() == A.CANCEL_EXIT, "确认窗可见：决策=取消退出")
	await _press_esc()
	_check(not exit_confirm.visible and start_panel.visible, "确认窗 Esc：取消退出回到开始面板")
	_check(
		start_panel.get_viewport().gui_get_focus_owner() == start_panel._new_button,
		"取消后焦点还给主按钮",
	)

	# ---------- 2. 对局层：Esc ⇄ 暂停 ----------
	start_panel._on_new_game_pressed()
	await get_tree().process_frame
	_check(nav.decide_back_action() == A.OPEN_PAUSE, "战斗中：决策=打开暂停")
	await _press_esc()
	_check(pause_ui.visible and get_tree().paused, "战斗中 Esc：打开暂停")
	_check(nav.decide_back_action() == A.RESUME_GAME, "暂停中：决策=继续游戏")
	await _press_esc()
	_check(not pause_ui.visible and not get_tree().paused, "暂停中 Esc：恢复游戏")

	# ---------- 3. 设置页：返回 opener + 改键捕获态放行 ----------
	pause_ui.open()
	pause_ui._on_settings_pressed()
	_check(settings_ui.visible and nav.decide_back_action() == A.CLOSE_SETTINGS, "设置页：决策=返回 opener")
	settings_ui._start_capture(&"dash")
	_check(nav.decide_back_action() == A.CAPTURE_PASSTHROUGH, "改键捕获中：决策=放行")
	await _press_esc()
	_check(settings_ui._capturing_action == &"" and settings_ui.visible, "捕获中 Esc：取消捕获留在设置页")
	await _press_esc()
	_check(not settings_ui.visible and pause_ui.visible, "设置页 Esc：返回暂停面板")

	# ---------- 4. 战斗退出链：暂停 → 退出游戏 → battle 确认窗 ----------
	pause_ui._on_quit_pressed()
	_check(exit_confirm.visible and exit_confirm._battle, "暂停「退出游戏」：battle 模式确认窗")
	_check(exit_confirm._msg_label.text == tr("EXIT_BATTLE_MSG"), "battle 模式显示进度损失警告")
	await _press_esc()
	_check(not exit_confirm.visible and pause_ui.visible, "battle 确认窗 Esc：取消回到暂停")
	pause_ui.close()

	# ---------- 5. 覆盖/阻塞态决策（不执行动作，仅断言决策） ----------
	buff_ui.visible = true
	_check(nav.decide_back_action() == A.IGNORE, "Buff 三选一：决策=忽略")
	buff_ui.visible = false
	base_ui.visible = true
	_check(nav.decide_back_action() == A.RESUME_BASE, "基地控制台：决策=继续出击")
	base_ui.visible = false
	main._game_over = true
	game_over_ui.visible = true
	_check(nav.decide_back_action() == A.TO_MAIN_MENU, "结算页：决策=返回主界面")
	game_over_ui.visible = false
	main._game_over = false

	# ---------- 6. 退出前清理副作用 ----------
	GameState.save_run(50.0, 10.0)
	exit_confirm._execute_exit_cleanup(true)
	_check(not GameState.has_save(), "战斗中退出清理：对局存档删除（放弃进度）")
	GameState.save_run(50.0, 10.0)
	exit_confirm._execute_exit_cleanup(false)
	_check(GameState.has_save(), "主界面退出清理：对局存档保留（可继续对局）")

	# ---------- 7. Android 返回手势走同一状态机 ----------
	pause_ui.open()
	nav._notification(NOTIFICATION_WM_GO_BACK_REQUEST)
	_check(not pause_ui.visible and not get_tree().paused, "Android 返回通知：与 Esc 同一路由")

	GameState.delete_save()
	GameState.save_profile()
	print("[DONE] failures=%d" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)
