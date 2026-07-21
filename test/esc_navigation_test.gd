extends Node
## Esc 导航真实输入回归：注入真实按键事件走完整输入管线（区别于 smoke 的直接调用）。
## 核心覆盖：树暂停后 Esc 路由必须仍可达（process_mode=Always 的 pause_ui），
## 以及「暂停 → 设置 → 改键 → Esc 逐级返回 → 恢复游戏」全链路（用户报告的卡死路径）。

var _failures := 0
var _frames := 0


func _process(_delta: float) -> void:
	_frames += 1
	if _frames > 600:  # 看门狗：卡死时带状态退出
		printerr("[WATCHDOG] stuck, paused=%s" % str(get_tree().paused))
		get_tree().quit(2)


func _check(cond: bool, msg: String) -> void:
	if cond:
		print("[PASS] " + msg)
	else:
		_failures += 1
		printerr("[FAIL] " + msg)


func _press_key(keycode: Key) -> void:
	var ev := InputEventKey.new()
	ev.keycode = keycode
	ev.pressed = true
	Input.parse_input_event(ev)
	await get_tree().process_frame
	await get_tree().process_frame
	var up := InputEventKey.new()
	up.keycode = keycode
	up.pressed = false
	Input.parse_input_event(up)
	await get_tree().process_frame


func _ready() -> void:
	GameState.delete_save()
	var main: Node2D = load("res://scenes/main.tscn").instantiate() as Node2D
	add_child(main)
	await get_tree().process_frame
	await get_tree().process_frame

	var start_panel: CanvasLayer = main.get_node("StartPanel")
	var pause_ui: CanvasLayer = main.get_node("PauseUI")
	var settings_ui: CanvasLayer = main.get_node("SettingsUI")
	if start_panel.visible:
		start_panel._on_new_game_pressed()
	await get_tree().process_frame

	# 1. Esc → 暂停（未暂停时注入，main/pause 均可收）
	await _press_key(KEY_ESCAPE)
	_check(pause_ui.visible and get_tree().paused, "Esc 打开暂停面板")

	# 2. 暂停中 Esc → 恢复（核心回归：暂停后 INHERIT 节点收不到输入，路由须在 Always 节点）
	await _press_key(KEY_ESCAPE)
	_check(not pause_ui.visible and not get_tree().paused, "暂停中 Esc 恢复游戏")

	# 3. 暂停 → 设置 → 改键 dash=T → Esc 逐级返回（用户报告的卡死路径）
	await _press_key(KEY_ESCAPE)
	pause_ui._on_settings_pressed()
	_check(settings_ui.visible, "设置面板打开")
	settings_ui._start_capture(&"dash")
	await _press_key(KEY_T)
	_check(settings_ui._capturing_action == &"", "绑定后退出捕获态")
	_check(GameState.action_keys_text(&"dash") == "T", "dash 已绑定 T")
	await _press_key(KEY_ESCAPE)
	_check(not settings_ui.visible, "Esc 后设置面板关闭")
	_check(pause_ui.visible, "Esc 后回到暂停面板")
	await _press_key(KEY_ESCAPE)
	_check(not pause_ui.visible and not get_tree().paused, "再 Esc 恢复游戏")

	# 4. 捕获态 Esc 取消 → 再 Esc 逐级退出
	await _press_key(KEY_ESCAPE)
	pause_ui._on_settings_pressed()
	settings_ui._start_capture(&"dash")
	await _press_key(KEY_ESCAPE)  # 取消捕获
	_check(settings_ui.visible and settings_ui._capturing_action == &"", "捕获中 Esc 取消但留在设置页")
	await _press_key(KEY_ESCAPE)  # 退出设置 → 回暂停
	_check(not settings_ui.visible and pause_ui.visible, "取消后再 Esc 回暂停面板")
	await _press_key(KEY_ESCAPE)  # 恢复游戏
	_check(not get_tree().paused, "最终 Esc 恢复游戏")

	GameState.reset_key_bindings()
	GameState.delete_save()
	print("[DONE] failures=%d" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)
