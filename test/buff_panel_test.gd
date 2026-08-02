extends Node
## buff 滚动栏测试：收起态单行（最新 4 格 + 溢出 +N）、L 键展开/关闭、
## Esc 经 BackNavigator 优先关栏、改语言刷新。运行：
##   godot --headless --path . res://test/buff_panel_test.tscn

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _press_l() -> void:
	var ev := InputEventKey.new()
	ev.keycode = KEY_L
	ev.pressed = true
	Input.parse_input_event(ev)
	await get_tree().process_frame
	await get_tree().process_frame
	var up := InputEventKey.new()
	up.keycode = KEY_L
	up.pressed = false
	Input.parse_input_event(up)
	await get_tree().process_frame


func _ready() -> void:
	var orig_locale: String = GameState.locale
	GameState.delete_save()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	var main := get_node("Main")
	var hud: CanvasLayer = main.get_node("HUD")
	var nav := main.get_node("BackNavigator")
	var A = nav.BackAction  # 枚举经实例访问为 Variant，不能用 := 推断
	var start_panel: CanvasLayer = main.get_node("StartPanel")
	if start_panel.visible:
		start_panel.press_new_game()
		await get_tree().process_frame
	# 屏蔽里程碑三选一叠屏
	GameState.set_milestone_override(999999999)

	# ---------- 1. 无 buff：标签隐藏，L 不展开 ----------
	_check(not hud.buff_tag().visible, "无 buff：收起态标签隐藏")
	_check(not hud.is_buff_panel_open(), "初始：滚动栏关闭")
	await _press_l()
	_check(not hud.is_buff_panel_open(), "无 buff：按 L 不展开滚动栏")

	# ---------- 2. 3 个 buff：单行全展示，无溢出 ----------
	GameState.add_buff(&"power_shot")
	GameState.add_buff(&"armor")
	GameState.add_buff(&"regen")
	await get_tree().process_frame
	await get_tree().process_frame
	_check(hud.buff_dock().get_child_count() == 3, "3 个 buff：收起态 3 格")
	_check(hud.buff_tag().visible and hud.buff_tag().text == "增益 [L]", "3 个 buff：标签带 [L] 提示")

	# ---------- 3. 6 个 buff：最新 4 格 + 溢出 +2 ----------
	GameState.add_buff(&"piercing")
	GameState.add_buff(&"evasion")
	GameState.add_buff(&"slow_field")
	await get_tree().process_frame
	await get_tree().process_frame
	_check(hud.buff_dock().get_child_count() == 5, "6 个 buff：收起态 4 格 + 溢出格")
	_check(hud.buff_overflow_label() != null and hud.buff_overflow_label().text == "+2", "溢出格计数 +2")

	# ---------- 4. L 展开滚动栏：全量明细 ----------
	await _press_l()
	_check(hud.is_buff_panel_open(), "按 L：滚动栏展开")
	_check(hud.buff_rows().get_child_count() == 6, "滚动栏 6 行明细")

	# ---------- 5. Esc 路由：优先关栏而非打开暂停 ----------
	_check(nav.decide_back_action() == A.CLOSE_BUFF_PANEL, "栏展开：Esc 决策=关栏")
	nav.go_back()
	_check(not hud.is_buff_panel_open(), "Esc 执行：滚动栏关闭")
	_check(not main.get_node("PauseUI").visible, "Esc 关栏后未误开暂停")

	# ---------- 6. L 再次开关 ----------
	await _press_l()
	_check(hud.is_buff_panel_open(), "按 L：再次展开")
	await _press_l()
	_check(not hud.is_buff_panel_open(), "按 L：再次关闭")

	# ---------- 7. 语言切换刷新 ----------
	GameState.set_locale("en")
	await get_tree().process_frame
	_check(hud.buff_tag().text == "BUFFS [L]", "en：标签刷新")
	_check(hud.buff_panel_title().text == "Active Buffs", "en：滚动栏标题刷新")
	GameState.set_locale("zh")
	await get_tree().process_frame

	# ---------- 8. 清理 ----------
	GameState.delete_save()
	GameState.set_locale(orig_locale)
	GameState.save_profile()
	print("[DONE] failures=%d" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)
