extends Node
## 返航过场测试（docs/RETURN_HOME_CINEMATIC.md §5）：直接触发/skip 路径/时序路径，
## 以及 BackNavigator 的 SKIP_RETURN 决策与真实 Esc 注入。全程真实 Timer 等待。
## 输入宽限（SKIP_GRACE 1.2s，防实战按键误触）：开播 1.2s 内任意键/点击/Esc 不跳过，
## 各跳过路径断言前须先等 1.4s 真实时间越过宽限。
## 与开场过场测试的关键差异：finished 后基地 UI 可见且树**保持暂停**（无标题定格）；
## 每轮触发后须先 _on_resume_pressed() 恢复（其白闪 await 约 0.7s，等待真实计时器收尾）。

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


## 从基地整备恢复对局态：_resume_from_base 清敌/子弹并有 ~0.7s 白闪 await，等其收尾
func _restore_from_base(base_ui: CanvasLayer) -> void:
	if base_ui.visible:
		base_ui._on_resume_pressed()
	await get_tree().process_frame
	await get_tree().process_frame
	await get_tree().create_timer(0.8).timeout
	await get_tree().process_frame


func _ready() -> void:
	GameState.delete_save()
	GameState.welcome_seen = true  # 跳过欢迎页；无存档时不显示开始面板，直接在对局态
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	var main := get_node("Main")
	var nav := main.get_node("BackNavigator")
	var base_ui: CanvasLayer = main.get_node("BaseUI")
	var A = nav.BackAction  # 枚举经实例访问为 Variant，不能用 := 推断

	# ---------- 1. 直接触发：过场节点存在、树暂停 ----------
	var timer_baseline := _count_timers(get_tree().root)
	main._play_return_cinematic()
	await get_tree().process_frame
	var ret: ReturnCinematic = main._return
	_check(ret != null, "直接触发：返航过场节点存在")
	_check(get_tree().paused, "过场播放期间树暂停")
	_check(not base_ui.visible, "过场播放期间基地 UI 未显")

	# ---------- 2. 输入宽限 + skip() 路径 ----------
	var finished_fired := [0]
	ret.finished.connect(func() -> void: finished_fired[0] += 1)
	# 输入宽限（开播 1.2s 内，防实战 WASD/Shift 持续按键误触）：任意键/点击不跳过
	var grace_key := InputEventKey.new()
	grace_key.keycode = KEY_A
	grace_key.pressed = true
	Input.parse_input_event(grace_key)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(is_instance_valid(ret) and main._return == ret, "宽限期内任意键：过场不跳过（节点仍在）")
	_check(finished_fired[0] == 0, "宽限期内任意键：finished 未发出")
	var grace_click := InputEventMouseButton.new()
	grace_click.button_index = MOUSE_BUTTON_LEFT
	grace_click.pressed = true
	Input.parse_input_event(grace_click)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(is_instance_valid(ret) and main._return == ret, "宽限期内鼠标点击：过场不跳过")
	# 越过宽限后：销毁、finished 一次、基地 UI 可见且树仍暂停、无 Timer 残留
	await get_tree().create_timer(1.4).timeout
	main._skip_return()
	ret.skip()  # 幂等：重复调用不重复发信号
	await get_tree().process_frame
	await get_tree().process_frame
	_check(finished_fired[0] == 1, "skip：finished 信号发出且仅一次")
	_check(main._return == null and not is_instance_valid(ret), "skip：过场已销毁")
	_check(base_ui.visible, "skip：基地 UI 可见（关键差异：无标题定格，直接落基地）")
	_check(get_tree().paused, "skip：树保持暂停（基地界面本就是暂停态 UI）")
	_check(_count_timers(get_tree().root) == timer_baseline, "skip：无残留 Timer")
	await _restore_from_base(base_ui)

	# ---------- 3. 时序路径：短时长推进 7 镜头，节点创建/销毁与最终 finished ----------
	main._play_return_cinematic()
	var ret2: ReturnCinematic = main._return
	var short_durations: Array[float] = [0.3, 0.3, 0.3, 0.3, 0.3, 0.3, 0.3]
	ret2._shot_durations = short_durations
	var finished2 := [false]
	ret2.finished.connect(func() -> void: finished2[0] = true)
	var seen_shots: Array[String] = []
	for expected in 7:
		var reached := false
		for i in 40:
			await get_tree().create_timer(0.05).timeout
			if not is_instance_valid(ret2) or ret2._shot_index >= expected:
				reached = true
				break
		_check(reached, "时序：推进到镜头 %d" % (expected + 1))
		if reached and is_instance_valid(ret2) and ret2._current_shot != null:
			var shot_name: String = ret2._current_shot.name
			if not seen_shots.has(shot_name):
				seen_shots.append(shot_name)
			_check(
				ret2._shot_root.get_child_count() == 1,
				"时序：镜头 %d 旧节点已销毁（仅当前镜头在场）" % (expected + 1)
			)
			_check(ret2._subtitle.text != "", "时序：镜头 %d 叙事字幕已设置" % (expected + 1))
	# 镜头 7 末尾渐暗后 finished（渐暗含在时长内），等待窗口放宽
	for i in 80:
		await get_tree().create_timer(0.05).timeout
		if finished2[0]:
			break
	await get_tree().process_frame
	await get_tree().process_frame
	_check(finished2[0], "时序：7 镜头播完发出 finished")
	_check(seen_shots.size() == 7, "时序：7 个镜头节点依次创建（Shot1..Shot7）")
	_check(main._return == null, "时序：播完销毁")
	_check(base_ui.visible, "时序：播完基地 UI 可见")
	_check(get_tree().paused, "时序：播完树保持暂停")
	_check(_count_timers(get_tree().root) == timer_baseline, "时序：播完无残留 Timer")
	await _restore_from_base(base_ui)

	# ---------- 4. Esc 路由：播放中决策 = SKIP_RETURN，真实 Esc 注入跳过 ----------
	main._play_return_cinematic()
	await get_tree().process_frame
	var ret3: ReturnCinematic = main._return
	_check(nav.decide_back_action() == A.SKIP_RETURN, "过场播放中：决策 = SKIP_RETURN")
	await get_tree().create_timer(1.4).timeout  # 越过输入宽限后 Esc 才生效
	await _press_esc()
	_check(main._return == null and not is_instance_valid(ret3), "Esc：经 BackNavigator 跳过过场")
	_check(base_ui.visible and get_tree().paused, "Esc 跳过后基地 UI 可见且树仍暂停")
	await _restore_from_base(base_ui)

	# ---------- 5. 任意键跳过（过场自身 _unhandled_input，需越过输入宽限） ----------
	main._play_return_cinematic()
	await get_tree().process_frame
	var ret4: ReturnCinematic = main._return
	await get_tree().create_timer(1.4).timeout
	var ev := InputEventKey.new()
	ev.keycode = KEY_A
	ev.pressed = true
	Input.parse_input_event(ev)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(main._return == null and not is_instance_valid(ret4), "任意键：过场自身捕获跳过")
	_check(base_ui.visible and get_tree().paused, "任意键跳过后基地 UI 可见且树仍暂停")
	await _restore_from_base(base_ui)

	# ---------- 6. 鼠标点击跳过（与任意键同一出口，需越过输入宽限） ----------
	main._play_return_cinematic()
	await get_tree().process_frame
	var ret5: ReturnCinematic = main._return
	await get_tree().create_timer(1.4).timeout
	var click := InputEventMouseButton.new()
	click.button_index = MOUSE_BUTTON_LEFT
	click.pressed = true
	Input.parse_input_event(click)
	await get_tree().process_frame
	await get_tree().process_frame
	_check(main._return == null and not is_instance_valid(ret5), "鼠标点击：过场自身捕获跳过")
	_check(base_ui.visible and get_tree().paused, "点击跳过后基地 UI 可见且树仍暂停")

	GameState.delete_save()
	GameState.save_profile()
	print("[DONE] failures=%d" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)
