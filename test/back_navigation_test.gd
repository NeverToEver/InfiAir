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


## 右键 = 返回/取消（惯例）：模拟按下-释放
func _press_rmb() -> void:
	var ev := InputEventMouseButton.new()
	ev.button_index = MOUSE_BUTTON_RIGHT
	ev.pressed = true
	Input.parse_input_event(ev)
	await get_tree().process_frame
	await get_tree().process_frame
	var up := InputEventMouseButton.new()
	up.button_index = MOUSE_BUTTON_RIGHT
	up.pressed = false
	Input.parse_input_event(up)
	await get_tree().process_frame


func _ready() -> void:
	GameState.delete_save()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	var main := get_node("Main")
	var nav := main.get_node("BackNavigator")
	var pause_ui: CanvasLayer = main.get_node("PauseUI")
	var settings_ui: CanvasLayer = main.get_node("SettingsUI")
	var base_ui: CanvasLayer = main.get_node("BaseUI")
	var buff_ui: CanvasLayer = main.get_node("BuffUI")
	var game_over_ui: CanvasLayer = main.get_node("GameOverUI")
	var exit_confirm: CanvasLayer = main.get_node("ExitConfirm")
	var act = nav.BackAction  # 枚举经实例访问为 Variant，不能用 := 推断

	# ---------- 1. 对局层：Esc ⇄ 暂停（顶层退出确认已由 welcome_flow_test 覆盖） ----------
	await get_tree().process_frame
	_check(nav.decide_back_action() == act.OPEN_PAUSE, "战斗中：决策=打开暂停")
	await _press_esc()
	_check(pause_ui.visible and get_tree().paused, "战斗中 Esc：打开暂停")
	_check(nav.decide_back_action() == act.RESUME_GAME, "暂停中：决策=继续游戏")
	await _press_esc()
	_check(not pause_ui.visible and not get_tree().paused, "暂停中 Esc：恢复游戏")

	# ---------- 2b. 战斗中右键：打开暂停（惯例） ----------
	await _press_rmb()
	_check(pause_ui.visible and get_tree().paused, "战斗中右键：打开暂停")
	await _press_rmb()
	_check(not pause_ui.visible and not get_tree().paused, "暂停中右键：恢复游戏")

	# ---------- 3. 设置页：返回 opener + 改键捕获态放行 ----------
	pause_ui.open()
	pause_ui.open_settings()
	_check(settings_ui.visible and nav.decide_back_action() == act.CLOSE_SETTINGS, "设置页：决策=返回 opener")
	settings_ui.start_capture(&"dash")
	_check(nav.decide_back_action() == act.CAPTURE_PASSTHROUGH, "改键捕获中：决策=放行")
	await _press_esc()
	_check(settings_ui.capturing_action() == &"" and settings_ui.visible, "捕获中 Esc：取消捕获留在设置页")
	await _press_esc()
	_check(not settings_ui.visible and pause_ui.visible, "设置页 Esc：返回暂停面板")

	# ---------- 4. 战斗退出链：暂停 → 退出游戏 → battle 确认窗 ----------
	pause_ui.quit()
	_check(exit_confirm.visible and exit_confirm.battle_mode(), "暂停「退出游戏」：battle 模式确认窗")
	_check(exit_confirm.msg_label().text == tr("EXIT_BATTLE_MSG"), "battle 模式显示进度损失警告")
	await _press_esc()
	_check(not exit_confirm.visible and pause_ui.visible, "battle 确认窗 Esc：取消回到暂停")
	pause_ui.close()

	# ---------- 5. 覆盖/阻塞态决策（不执行动作，仅断言决策） ----------
	main.play_return()
	await get_tree().process_frame
	_check(main.return_cinematic() != null and nav.decide_back_action() == act.SKIP_RETURN, "返航过场：决策=跳过过场")
	# skip() 有 SKIP_GRACE（1.2s）误触宽限期，期内忽略跳过；先等宽限期结束（真实时间，树已暂停）
	await get_tree().create_timer(1.3).timeout
	main.skip_return()
	await get_tree().process_frame
	await get_tree().process_frame
	_check(base_ui.visible and nav.decide_back_action() == act.RESUME_BASE, "返航过场跳过后：基地控制台决策=继续出击")
	base_ui.resume()  # 恢复对局态，避免影响后续分支断言
	await get_tree().process_frame
	await get_tree().process_frame
	buff_ui.visible = true
	_check(nav.decide_back_action() == act.IGNORE, "Buff 三选一：决策=忽略")
	buff_ui.visible = false
	base_ui.visible = true
	_check(nav.decide_back_action() == act.RESUME_BASE, "基地控制台：决策=继续出击")
	base_ui.visible = false
	main.set_game_over(true)
	game_over_ui.visible = true
	_check(nav.decide_back_action() == act.TO_MAIN_MENU, "结算页：决策=返回主界面")
	game_over_ui.visible = false
	main.set_game_over(false)

	# ---------- 6. 退出前清理副作用（游客不存档，切真实用户验证） ----------
	if not GameState.user_exists("nav_user"):
		GameState.create_user("nav_user", "pass123")
	GameState.login_user("nav_user")
	GameState.save_run(50.0, 10.0)
	exit_confirm.execute_exit_cleanup(true)
	_check(not GameState.has_save(), "战斗中退出清理：对局存档删除（放弃进度）")
	GameState.save_run(50.0, 10.0)
	exit_confirm.execute_exit_cleanup(false)
	_check(GameState.has_save(), "主界面退出清理：对局存档保留（可继续对局）")
	GameState.logout_user()

	# ---------- 7. Android 返回手势走同一状态机 ----------
	pause_ui.open()
	nav.go_back()  # C30：走公开路由（_notification 仅一行转发 go_back，语义等价）
	_check(not pause_ui.visible and not get_tree().paused, "Android 返回通知：与 Esc 同一路由")

	GameState.delete_save()
	GameState.save_profile()
	print("[DONE] failures=%d" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)
