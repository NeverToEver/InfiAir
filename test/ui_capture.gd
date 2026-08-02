extends Node
## UI 样式巡检截图：依次展示 开始面板/设置/Buff 三选一/暂停/基地控制台/结算 六个界面，
## 每屏截图存 /tmp/ui_<name>.png。需窗口模式运行（headless 为 dummy 渲染截不到画面）：
##   godot --path . res://test/ui_capture.tscn
## 结束恢复现场：删除测试产生的存档，profile 原始值（最高分）还原落盘。

const SETTLE_SECONDS := 1.2  # 等 stagger/淡入动效播完（真实时间，暂停中 process_always 仍计时）


func _ready() -> void:
	# 快照 profile 原始值，结束还原（测试要伪造最高分/新纪录）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 12345
	GameState.save_run(50.0, 10.0)  # 伪造存档让开始面板显示「继续对局」形态

	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	# 1. 开始面板（有存档：继续对局 primary + 最高分副信息行）
	var sp: CanvasLayer = get_node("Main/StartPanel")
	if not sp.visible:
		sp.show_panel()
	await _settle()
	_shot("start")

	# 2. 设置页（从开始面板进入，默认控制分区）
	sp.press_settings()
	await _settle()
	_shot("settings")
	var settings: CanvasLayer = get_node("Main/SettingsUI")
	settings.back()
	await get_tree().process_frame

	# 开新局，后续界面在对局内展示
	sp.press_new_game()
	await get_tree().process_frame

	# 3. Buff 三选一（含层数标记：先垫一层 power_shot 候选必含时可见，随缘即可）
	GameState.milestone_reached.emit(100)
	await _settle()
	_shot("buff")
	var buff_ui: CanvasLayer = get_node("Main/BuffUI")
	if buff_ui.visible:
		var ev := InputEventMouseButton.new()
		ev.pressed = true
		ev.button_index = MOUSE_BUTTON_LEFT
		buff_ui.pick_buff(buff_ui.current_available()[0]["id"])
	await get_tree().process_frame

	# 屏蔽后续里程碑触发，避免 Buff UI 与结算叠屏（确定性截图）
	GameState.set_milestone_override(999999999)

	# 4. 暂停面板（继续 primary）
	var pui: CanvasLayer = get_node("Main/PauseUI")
	pui.open()
	await _settle()
	_shot("pause")
	pui.close()
	await get_tree().process_frame

	# 5. 基地控制台（四模块 section header；返航过场直接 skip 落基地，截虚影皮肤）
	GameState.add_rp(10)
	GameState.add_buff(&"spread_shot")
	var main := get_node("Main")
	main.start_homecoming()
	# 跳过返航过场：skip() 有 SKIP_GRACE 输入宽限（开播数秒内忽略），宽限期内每帧重试，
	# 直到过场引用被 _on_return_finished 置空（跳过与自然结束同一出口）
	for i in 600:
		await get_tree().process_frame
		var rc: ReturnCinematic = main.return_cinematic()
		if rc != null and is_instance_valid(rc):
			rc.skip()
		else:
			break
	# 等基地控制台真正可见再截（跳过过场后还有全息启动动效）
	var base_ui: CanvasLayer = get_node("Main/BaseUI")
	for i in 120:
		await get_tree().process_frame
		if base_ui.visible:
			break
	await _settle()  # 等全息启动 0.25s + animate_open 0.2s 播完
	_shot("base")
	base_ui.resume()
	get_tree().paused = false
	# 等轨道打击动画播完，避免叠入后续截图
	if main.strike() != null:
		main.strike().DURATION = 0.3
	while main.strike() != null:
		await get_tree().process_frame

	# 6. 死亡结算（大分数 + 新纪录标记）；先收掉可能被分数再次触发的 Buff UI 避免叠屏
	if buff_ui.visible:
		var ev2 := InputEventMouseButton.new()
		ev2.pressed = true
		ev2.button_index = MOUSE_BUTTON_LEFT
		buff_ui.pick_buff(buff_ui.current_available()[0]["id"])
	GameState.high_score = 100  # 压低原纪录，保证「新纪录」标记可见（结尾还原）
	GameState.add_score(8888)
	GameState.player_died.emit()
	# 等结算面板真正可见再截
	for i in 60:
		await get_tree().process_frame
		if get_node("Main/GameOverUI").visible:
			break
	await _settle()
	_shot("gameover")

	# 恢复现场：删测试存档 + 还原 profile 原始值落盘
	GameState.delete_save()
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("ui capture done")
	get_tree().quit()


func _settle() -> void:
	# 真实时间等待（process_always=true：暂停中也会计时），与帧率无关
	await get_tree().create_timer(SETTLE_SECONDS).timeout


func _shot(name: String) -> void:
	var path := "/tmp/ui_%s.png" % name
	get_viewport().get_texture().get_image().save_png(path)
	print("capture saved: ", path)
