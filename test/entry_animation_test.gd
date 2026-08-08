extends Node
## 入场衔接动画测试：开场/继续出击后战机入场（高速冲入→向后缓移），
## 期间仅左右可调/上下锁定，敌机生成延迟，动画结束恢复正常流程与敌机生成。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _wait_physics(frames: int) -> void:
	for i in frames:
		await get_tree().physics_frame


func _ready() -> void:
	GameState.delete_save()
	GameState.reset_run()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame
	await get_tree().process_frame
	await get_tree().process_frame

	var main := get_node("Main")
	var player = main.player()
	var spawner: Node = main.get_node("Spawner")
	var rect := GameState.view_world_rect()
	var land_y: float = rect.position.y + rect.size.y * player.ENTRY_LAND_RATIO

	# ---------- 1. 触发入场序列：动画启动 + 敌机延迟 + 起点在屏下外 ----------
	main.start_entry_sequence()
	_check(player.is_entry_playing(), "入场动画启动")
	_check(not spawner.is_processing(), "入场期间敌机生成暂停（延迟）")
	_check(player.position.y > rect.end.y, "入场起点在屏幕下外")

	# ---------- 2. 阶段 1 冲入：锁输入，tween 驱动定位到下 1/3 ----------
	var x0 = player.position.x
	Input.action_press("move_left")
	Input.action_press("move_up")
	await _wait_physics(10)
	_check(player.position.x == x0, "冲入阶段左右输入无效（锁输入）")
	Input.action_release("move_left")
	Input.action_release("move_up")
	# 轮询进入后撤：y 先抵达定位线邻域（冲入 EASE_OUT 末端单帧即过线）并连续停驻上升。
	# 注意 y >= land_y 在冲入期间（起点屏下外）恒成立，不能作为到达判据；用邻域连续帧确认 phase 2。
	var landed := false
	var settle_frames := 0
	for i in 120:
		await get_tree().physics_frame
		var y = player.position.y
		if y >= land_y - 2.0 and y <= land_y + 40.0:
			settle_frames += 1
			if settle_frames >= 8:
				landed = true
				break
		else:
			settle_frames = 0
	_check(landed, "冲入定位到屏幕下 1/3")
	_check(not player.auto_fire_enabled(), "入场期间自动开火暂停")

	# ---------- 3. 阶段 2 后撤：仅左右可调、上下锁定 ----------
	_check(player.is_entry_playing(), "后撤阶段动画仍在播放")
	var xr0 = player.position.x
	Input.action_press("move_right")
	await _wait_physics(10)
	Input.action_release("move_right")
	_check(player.position.x > xr0 + 5.0, "后撤阶段左右可调")
	var y_before_up = player.position.y
	Input.action_press("move_up")
	await _wait_physics(10)
	Input.action_release("move_up")
	_check(player.position.y > y_before_up, "后撤阶段上下锁定（按上仍自动后移）")

	# ---------- 4. 动画结束：恢复正常流程 + 敌机生成恢复 ----------
	# 轮询等待结束（rush 用 idle tween、retreat 用 physics，起点有帧率漂移，不用固定帧数）
	var end_frames := 0
	while player.is_entry_playing() and end_frames < 200:
		await get_tree().physics_frame
		end_frames += 1
	_check(not player.is_entry_playing(), "入场动画结束")
	_check(spawner.is_processing(), "动画结束后敌机生成恢复")
	_check(player.auto_fire_enabled(), "入场动画结束后自动开火恢复")
	var expected_y: float = land_y + player.ENTRY_RETREAT_SPEED * player.ENTRY_RETREAT_TIME
	_check(absf(player.position.y - expected_y) < 25.0, "入场终点接近正常站位")

	GameState.delete_save()
	print("[DONE] failures=%d" % _failures)
	get_tree().quit(1 if _failures > 0 else 0)
