extends Node
## 临时冒烟测试：覆盖里程碑 Buff UI、Boss 生成、玩家死亡结算路径。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame

	# 1. 里程碑触发 Buff UI
	GameState.add_score(500)
	await get_tree().process_frame
	var buff_ui: CanvasLayer = get_node("Main/BuffUI")
	_check(buff_ui.visible, "500 分触发 Buff 选择 UI")
	_check(get_tree().paused, "Buff UI 弹出时游戏暂停")

	# 2. 选择 buff 后恢复
	var ev := InputEventMouseButton.new()
	ev.pressed = true
	ev.button_index = MOUSE_BUTTON_LEFT
	buff_ui._on_card_gui_input(ev, &"power_shot")
	_check(not buff_ui.visible and not get_tree().paused, "选择 buff 后关闭并恢复")
	_check(GameState.buff_count(&"power_shot") == 1, "buff 计入 GameState")

	# 3. Boss 生成与弹幕
	var spawner: Node = get_node("Main/Spawner")
	spawner._spawn_boss()
	await get_tree().process_frame
	var boss: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss = child
	_check(boss != null, "Boss 已生成")
	_check(get_node("Main/HUD/BossBar").visible, "Boss 血条显示")
	# 无头模式帧不封顶，240 帧远不足 4 秒真实时间，改用真实时间等待
	await get_tree().create_timer(4.0).timeout
	_check(boss._in_fight, "Boss 进入巡航阶段")
	boss.take_damage(9999)
	await get_tree().process_frame
	_check(GameState.boss_kills == 1, "Boss 击毁计数")
	_check(GameState.difficulty_multiplier == 1.25, "难度乘数按公式更新")
	_check(not get_node("Main/HUD/BossBar").visible, "Boss 血条隐藏")

	# 4. 玩家受击至死 → 结算
	var player: Player = get_node("Main/Player")
	for i in 3:
		player._invincible = 0.0
		player.take_damage()
	await get_tree().process_frame
	_check(get_node("Main/GameOverUI").visible, "Game Over 面板显示")
	_check(get_tree().paused, "Game Over 时游戏暂停")

	# 5. 暂停面板
	get_tree().paused = false
	var pause_ui: CanvasLayer = get_node("Main/PauseUI")
	pause_ui.toggle()
	_check(pause_ui.visible and get_tree().paused, "Esc 暂停面板")
	pause_ui.toggle()
	_check(not pause_ui.visible and not get_tree().paused, "Esc 恢复")

	print("SMOKE TEST DONE, failures = ", _failures)
	get_tree().quit(_failures)
