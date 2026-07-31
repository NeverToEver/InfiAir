extends Node
## 启动链路测试：损坏存档/档案的隔离恢复、欢迎页 → 开始面板键盘-only 链路、
## 主按钮焦点策略（无存档=开始游戏，有存档=继续对局）、损坏提示显隐。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _write_file(path: String, content: String) -> void:
	var f := FileAccess.open(path, FileAccess.WRITE)
	f.store_string(content)
	f.close()


func _cleanup() -> void:
	GameState.delete_save()
	for p in [GameState.SAVE_PATH + ".corrupt", GameState.PROFILE_PATH + ".corrupt"]:
		if FileAccess.file_exists(p):
			DirAccess.remove_absolute(p)


func _ready() -> void:
	_cleanup()
	GameState.reset_run()

	# ---------- 1. 损坏对局存档：隔离 + 标记 + 按无存档处理 ----------
	_write_file(GameState.SAVE_PATH, "{oops not json")
	var data := GameState.load_run_data()
	_check(data.is_empty(), "损坏存档读取返回空")
	_check(GameState.save_corrupt, "损坏存档置 save_corrupt 标记")
	_check(not GameState.has_save(), "损坏存档被移走，has_save 为 false")
	_check(FileAccess.file_exists(GameState.SAVE_PATH + ".corrupt"), "损坏存档备份为 .corrupt")

	# 1b. 正常存档读取后标记复位
	GameState.save_run(50.0, 10.0)
	data = GameState.load_run_data()
	_check(not data.is_empty() and not GameState.save_corrupt, "正常存档读取后 save_corrupt 复位")
	GameState.delete_save()

	# ---------- 2. 损坏档案：备份 + 默认值继续 ----------
	var high_score_before := GameState.high_score
	_write_file(GameState.PROFILE_PATH, "### broken")
	GameState.load_profile()
	_check(GameState.profile_corrupt, "损坏档案置 profile_corrupt 标记")
	_check(GameState.high_score == high_score_before, "损坏档案保留内存默认值")
	_check(FileAccess.file_exists(GameState.PROFILE_PATH + ".corrupt"), "损坏档案备份为 .corrupt")
	DirAccess.remove_absolute(GameState.PROFILE_PATH + ".corrupt")
	GameState.save_profile()  # 重建正常档案
	GameState.load_profile()
	_check(not GameState.profile_corrupt, "正常档案读取后 profile_corrupt 复位")

	# ---------- 3. 键盘-only 链路：欢迎页任意键 → 开始面板 Enter 开新局 ----------
	GameState.delete_save()
	GameState.welcome_seen = false
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame
	var welcome: CanvasLayer = get_node("Main/WelcomeScreen")
	var start_panel: CanvasLayer = get_node("Main/StartPanel")
	_check(welcome.visible, "首次启动欢迎页显示")
	_check(get_tree().paused, "欢迎页显示期间游戏暂停")

	var ev := InputEventKey.new()
	ev.keycode = KEY_ENTER
	ev.pressed = true
	Input.parse_input_event(ev)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(not welcome.visible and start_panel.visible, "任意键关闭欢迎页进入开始面板")
	_check(GameState.welcome_seen, "欢迎页关闭后 welcome_seen 已置位")
	var focused := start_panel.get_viewport().gui_get_focus_owner()
	_check(focused == start_panel.new_button(), "无存档时主按钮（开始游戏）持有焦点")
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

	# ---------- 4. 有存档：主按钮为继续对局 ----------
	GameState.save_run(50.0, 10.0)
	start_panel.show_panel()
	await get_tree().process_frame
	_check(start_panel.continue_button().visible, "有存档时显示继续对局")
	_check(start_panel.get_viewport().gui_get_focus_owner() == start_panel.continue_button(), "有存档时焦点在继续对局")
	start_panel.dismiss()
	GameState.delete_save()

	# ---------- 5. 损坏存档：面板降级为新对局 + 提示可见 ----------
	_write_file(GameState.SAVE_PATH, "{still broken")
	start_panel.show_panel()
	await get_tree().process_frame
	_check(not start_panel.continue_button().visible, "损坏存档不再显示继续对局")
	_check(start_panel.corrupt_label().visible, "损坏存档提示可见")
	_check(start_panel.corrupt_label().text != "START_SAVE_CORRUPT", "损坏提示文案已翻译（tr 命中）")
	_check(start_panel.get_viewport().gui_get_focus_owner() == start_panel.new_button(), "损坏存档焦点落到开始游戏")
	start_panel.dismiss()

	# ---------- 6. F1 回归：有存档 + 欢迎页首显，开始面板不得抢显/抢焦点 ----------
	GameState.save_run(50.0, 10.0)
	GameState.welcome_seen = false
	# 重置 welcome_screen 的进程内 static 兜底（本进程已展示过一次）
	(load("res://scripts/welcome_screen.gd") as GDScript).reset_entry_shown()
	get_node("Main").queue_free()
	await get_tree().process_frame
	await get_tree().process_frame
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame
	var welcome2: CanvasLayer = get_node("Main/WelcomeScreen")
	var start_panel2: CanvasLayer = get_node("Main/StartPanel")
	_check(welcome2.visible, "F1：有存档且首显时欢迎页显示")
	_check(not start_panel2.visible, "F1：开始面板不在欢迎页下抢显")
	_check(start_panel2.get_viewport().gui_get_focus_owner() == null, "F1：欢迎页期间无 GUI 焦点")
	# 模拟 Enter：只许关闭欢迎页，不得触发「继续对局」（恢复运行/读档）
	var continued := [false]
	start_panel2.continue_chosen.connect(func() -> void: continued[0] = true)
	var enter := InputEventKey.new()
	enter.keycode = KEY_ENTER
	enter.pressed = true
	Input.parse_input_event(enter)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(not welcome2.visible and start_panel2.visible, "F1：Enter 关闭欢迎页进入开始面板")
	_check(get_tree().paused, "F1：Enter 未绕过欢迎页恢复游戏运行")
	_check(not continued[0], "F1：Enter 未触发继续对局")
	_check(GameState.has_save(), "F1：Enter 未触发新游戏删档")
	_check(
		start_panel2.get_viewport().gui_get_focus_owner() == start_panel2.continue_button(),
		"F1：dismiss 后面板焦点在继续对局"
	)
	# dismiss 幂等：已隐藏时再调不应报错或改变面板状态
	welcome2.dismiss()
	_check(start_panel2.visible, "F1：dismiss 后开始面板正常显示")

	# ---------- 7. F2：语法合法但结构非法的存档 → 继续对局不崩、异常字段回默认 ----------
	_write_file(GameState.SAVE_PATH, JSON.stringify({
		"version": 2,
		"score": "lots",
		"kills": [1, 2],
		"health": "full",
		"fuel": "half",
		"elapsed": "long",
		"buffs": [1, 2],
		"missions": {"kill_5": "done"},
		"chosen_routes": [3],
		"locked_routes": {"line": 7},
		"rp": "rich",
		"difficulty_multiplier": "high",
	}))
	var bad_data := GameState.load_run_data()
	_check(not bad_data.is_empty(), "F2：结构非法存档可解析（语法合法不隔离）")
	GameState.reset_run()
	GameState.apply_run_save(bad_data)
	_check(GameState.score == 0, "F2：非法 score 回默认 0")
	_check(GameState.buffs.is_empty(), "F2：非法 buffs 回默认空")
	_check(GameState.health == GameState.max_health(), "F2：非法 health 回默认满血")
	_check(GameState.rp == 0, "F2：非法 rp 回默认 0")
	_check(GameState.chosen_routes.is_empty() and GameState.locked_routes.is_empty(), "F2：非法路线字段回默认空")
	_check(int(GameState.missions[&"kill_5"]["progress"]) == 0, "F2：非法 mission 条目回默认进度")
	# main 的继续对局路径（fuel/elapsed 判型）
	var main2 := get_node("Main")
	var player2: Player = get_node("Main/Player")
	main2.continue_run()
	_check(player2.fuel_amount() == player2.fuel_max, "F2：非法 fuel 回默认满燃料")
	_check(get_node("Main/Spawner").elapsed() == 0.0, "F2：非法 elapsed 回默认 0")

	_cleanup()
	GameState.welcome_seen = false
	GameState.save_profile()
	print("[DONE] failures=%d" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)
