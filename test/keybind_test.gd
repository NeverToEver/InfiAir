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
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
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
	GameState.apply_key_bindings()
	_check(_action_has_key(&"dash", KEY_K), "读档后 InputMap 已应用")

	# 5. 非法动作拒绝
	_check(not GameState.rebind_action(&"restart", KEY_J), "restart 固定不可改")
	_check(not GameState.rebind_action(&"bogus_action", KEY_J), "非法动作拒绝")

	# 6. 捕获态取消（设置 UI 捕获逻辑）
	var settings := CanvasLayer.new()
	settings.set_script(load("res://scripts/settings_ui.gd"))
	add_child(settings)
	settings.show_settings()
	settings.start_capture(&"dock")
	_check(settings.capturing_action() == &"dock", "进入捕获态")
	var esc := InputEventKey.new()
	esc.keycode = KEY_ESCAPE
	esc.pressed = true
	Input.parse_input_event(esc)  # C30：走真实输入管线（对齐 esc_navigation_test 黑盒做法）
	await get_tree().process_frame
	var esc_up := InputEventKey.new()
	esc_up.keycode = KEY_ESCAPE
	esc_up.pressed = false
	Input.parse_input_event(esc_up)
	await get_tree().process_frame
	_check(settings.capturing_action() == &"", "Esc 取消捕获")
	_check(not _action_has_key(&"dock", KEY_ESCAPE), "取消未写入绑定")

	# 7. 捕获态绑定成功
	settings.start_capture(&"dock")
	var j_ev := InputEventKey.new()
	j_ev.keycode = KEY_J
	j_ev.pressed = true
	Input.parse_input_event(j_ev)  # C30：走真实输入管线
	await get_tree().process_frame
	var j_up := InputEventKey.new()
	j_up.keycode = KEY_J
	j_up.pressed = false
	Input.parse_input_event(j_up)
	await get_tree().process_frame
	_check(_action_has_key(&"dock", KEY_J), "捕获按键完成绑定")

	# 8. G04：默认绑定冲突——未自定义动作的默认键被占用时解除占用（防同键双动作）
	GameState.reset_key_bindings()
	GameState.rebind_action(&"dock", KEY_SPACE)  # dash 默认 Space 未自定义
	_check(_action_has_key(&"dock", KEY_SPACE), "dock 占用 Space")
	_check(not _action_has_key(&"dash", KEY_SPACE), "G04：默认键被占用后 dash 解除占用（空绑定覆盖默认）")
	GameState.reset_key_bindings()
	_check(_action_has_key(&"dash", KEY_SPACE), "恢复默认后 dash 回到 Space")

	# 收尾：恢复默认并落盘，避免污染其他测试/本机 profile
	GameState.reset_key_bindings()

	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("KEYBIND TEST DONE, failures = ", _failures)
	get_tree().quit(_failures)
