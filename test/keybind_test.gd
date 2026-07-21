extends Node
## 可改键系统测试：改键生效、冲突交换、恢复默认、profile 往返、捕获取消、非法拒绝。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _action_has_key(action: StringName, keycode: int) -> bool:
	for ev in InputMap.action_get_events(action):
		if ev is InputEventKey and (ev.keycode == keycode or ev.physical_keycode == keycode):
			return true
	return false


func _ready() -> void:
	GameState.delete_save()
	GameState.high_score = 0
	GameState.save_profile()
	GameState.reset_key_bindings()

	# 1. 改键生效（InputMap 实际变化）
	_check(GameState.rebind_action(&"dash", KEY_J), "改键 dash→J 返回 true")
	_check(_action_has_key(&"dash", KEY_J), "InputMap 中 dash 已是 J")
	_check(GameState.action_keys_text(&"dash") == "J", "键名显示 J")

	# 2. 冲突交换：boost 也用 J → dash 失去 J
	GameState.rebind_action(&"boost", KEY_J)
	_check(_action_has_key(&"boost", KEY_J), "boost 占用 J")
	_check(not _action_has_key(&"dash", KEY_J), "冲突键从 dash 移除（允许交换）")

	# 3. 恢复默认
	GameState.reset_key_bindings()
	_check(_action_has_key(&"boost", KEY_SHIFT), "恢复默认后 boost 回到 Shift")
	_check(not _action_has_key(&"boost", KEY_J), "恢复默认后 J 已清除")

	# 4. profile 持久化往返
	GameState.rebind_action(&"dash", KEY_K)
	GameState.key_bindings.clear()
	GameState.load_profile()
	_check(GameState.key_bindings.get(&"dash", []) == [KEY_K], "profile 往返保留改键")
	GameState._apply_key_bindings()
	_check(_action_has_key(&"dash", KEY_K), "读档后 InputMap 已应用")

	# 5. 非法动作拒绝
	_check(not GameState.rebind_action(&"restart", KEY_J), "restart 固定不可改")
	_check(not GameState.rebind_action(&"bogus_action", KEY_J), "非法动作拒绝")

	# 6. 捕获态取消（设置 UI 捕获逻辑）
	var settings := CanvasLayer.new()
	settings.set_script(load("res://scripts/settings_ui.gd"))
	add_child(settings)
	settings.show_settings()
	settings._start_capture(&"dock")
	_check(settings._capturing_action == &"dock", "进入捕获态")
	var esc := InputEventKey.new()
	esc.keycode = KEY_ESCAPE
	esc.pressed = true
	settings._unhandled_input(esc)
	_check(settings._capturing_action == &"", "Esc 取消捕获")
	_check(not _action_has_key(&"dock", KEY_ESCAPE), "取消未写入绑定")

	# 7. 捕获态绑定成功
	settings._start_capture(&"dock")
	var j_ev := InputEventKey.new()
	j_ev.keycode = KEY_J
	j_ev.pressed = true
	settings._unhandled_input(j_ev)
	_check(_action_has_key(&"dock", KEY_J), "捕获按键完成绑定")

	# 收尾：恢复默认并落盘，避免污染其他测试/本机 profile
	GameState.reset_key_bindings()

	print("KEYBIND TEST DONE, failures = ", _failures)
	get_tree().quit(_failures)
