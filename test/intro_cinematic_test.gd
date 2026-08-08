extends Node
## 开场过场测试（docs/INTRO_CINEMATIC.md §5）：直接触发/skip 路径/时序路径/门禁路径，
## 以及 BackNavigator 的 SKIP_INTRO 决策与真实 Esc 注入。全程真实 Timer 等待。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _count_timers(node: Node) -> int:
	var n := 0
	for c in node.get_children():
		if c is Timer:
			n += 1
		n += _count_timers(c)
	return n


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
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	var main := get_node("Main")
	var nav := main.get_node("BackNavigator")
	var act = nav.back_actions()  # 枚举经实例访问为 Variant，不能用 := 推断

	# ---------- 1. 门禁路径：测试场景（current_scene != Main）实例化不自动播过场 ----------
	await get_tree().process_frame
	_check(main.intro() == null, "门禁：测试场景实例化 main 不播过场")
	_check(not get_tree().paused, "门禁：未残留暂停")

	# ---------- 2. 直接触发：过场节点存在、树暂停 ----------
	var timer_baseline := _count_timers(get_tree().root)
	main.play_intro()
	await get_tree().process_frame
	var intro = main.intro()
	_check(intro != null, "直接触发：过场节点存在")
	_check(get_tree().paused, "过场播放期间树暂停")

	# ---------- 3. skip() 路径：销毁、finished、恢复非暂停、无 Timer 残留 ----------
	var finished_fired := [false]
	intro.Finished.connect(func() -> void: finished_fired[0] = true)
	main.skip_intro()
	intro.skip()  # 幂等：重复调用不重复发信号
	await get_tree().process_frame
	await get_tree().process_frame
	_check(finished_fired[0], "skip：finished 信号发出（且仅一次）")
	_check(main.intro() == null and not is_instance_valid(intro), "skip：过场已销毁")
	_check(not get_tree().paused, "skip：树恢复非暂停")
	_check(_count_timers(get_tree().root) == timer_baseline, "skip：无残留 Timer")

	# ---------- 4. 时序路径：短时长推进 6 镜头，节点创建/销毁与最终 finished ----------
	main.play_intro()
	var intro2 = main.intro()
	var short_durations: Array[float] = [0.3, 0.3, 0.3, 0.3, 0.3, 0.3]
	intro2.set_shot_durations(short_durations)
	var finished2 := [false]
	intro2.Finished.connect(func() -> void: finished2[0] = true)
	var seen_shots: Array[String] = []
	for expected in 6:
		var reached := false
		for i in 40:
			await get_tree().create_timer(0.05).timeout
			if not is_instance_valid(intro2) or intro2.shot_index() >= expected:
				reached = true
				break
		_check(reached, "时序：推进到镜头 %d" % (expected + 1))
		if reached and is_instance_valid(intro2) and intro2.current_shot() != null:
			var shot_name: String = intro2.current_shot().name
			if not seen_shots.has(shot_name):
				seen_shots.append(shot_name)
			_check(intro2.shot_root().get_child_count() == 1, "时序：镜头 %d 旧节点已销毁（仅当前镜头在场）" % (expected + 1))
			_check(intro2.subtitle().text != "", "时序：镜头 %d 叙事字幕已设置" % (expected + 1))
	# 收尾标题定格额外 1.8s（§2），等待窗口放宽
	for i in 80:
		await get_tree().create_timer(0.05).timeout
		if finished2[0]:
			break
	await get_tree().process_frame
	await get_tree().process_frame
	_check(finished2[0], "时序：6 镜头播完发出 finished")
	_check(seen_shots.size() == 6, "时序：6 个镜头节点依次创建（Shot1..Shot6）")
	_check(main.intro() == null and not get_tree().paused, "时序：播完销毁并恢复非暂停")
	_check(_count_timers(get_tree().root) == timer_baseline, "时序：播完无残留 Timer")

	# ---------- 5. Esc 路由：播放中决策 = SKIP_INTRO，真实 Esc 注入跳过 ----------
	main.play_intro()
	await get_tree().process_frame
	var intro3 = main.intro()
	_check(nav.decide_back_action() == act.SKIP_INTRO, "过场播放中：决策 = SKIP_INTRO")
	await _press_esc()
	_check(main.intro() == null and not is_instance_valid(intro3), "Esc：经 BackNavigator 跳过过场")
	_check(not get_tree().paused, "Esc 跳过后树恢复非暂停")

	# ---------- 6. 任意键跳过（过场自身 _unhandled_input） ----------
	main.play_intro()
	await get_tree().process_frame
	var intro4 = main.intro()
	var ev := InputEventKey.new()
	ev.keycode = KEY_A
	ev.pressed = true
	Input.parse_input_event(ev)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(main.intro() == null and not is_instance_valid(intro4), "任意键：过场自身捕获跳过")
	_check(not get_tree().paused, "任意键跳过后树恢复非暂停")

	# ---------- 7. 鼠标点击跳过（与任意键同一出口） ----------
	main.play_intro()
	await get_tree().process_frame
	var intro5 = main.intro()
	var click := InputEventMouseButton.new()
	click.button_index = MOUSE_BUTTON_LEFT
	click.pressed = true
	Input.parse_input_event(click)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(main.intro() == null and not is_instance_valid(intro5), "鼠标点击：过场自身捕获跳过")
	_check(not get_tree().paused, "点击跳过后树恢复非暂停")

	GameState.delete_save()
	GameState.save_profile()
	print("[DONE] failures=%d" % _failures)
	load("res://csharp/godot/TestExit.cs").Quit(1 if _failures > 0 else 0)
