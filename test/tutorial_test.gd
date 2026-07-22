extends Node
## 教程流程测试：6 阶段推进、锁血、对接、返航开基地、狂暴过关、Esc 退出、profile 写入。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	# 清理持久化状态
	GameState.delete_save()
	GameState.high_score = 0
	GameState.tutorial_done = false
	GameState.save_profile()

	# ---------- 软锁路径 a：玩家死亡 → 显示任务失败提示（独立实例，不影响主流程） ----------
	var tut_a: Node2D = (load("res://scenes/tutorial.tscn") as PackedScene).instantiate()
	add_child(tut_a)
	await get_tree().process_frame
	await get_tree().process_frame
	var player_a: Player = tut_a.get_node("Player")
	player_a._auto_fire_enabled = false
	player_a._invincible = 0.0
	player_a._last_hit_frame = -1
	player_a.take_damage(9999.0)
	await get_tree().process_frame
	_check(player_a._dead, "死亡路径：玩家已死亡")
	_check(tut_a._failed, "死亡路径：教程进入失败态")
	_check(tut_a._title_label.text == tr("TUT_FAIL_TITLE"), "死亡路径：标题显示任务失败（tr 命中）")
	_check(tut_a._objective_label.text == tr("TUT_FAIL_DESC"), "死亡路径：提示 Esc 退出（tr 命中）")
	tut_a.queue_free()
	await get_tree().process_frame
	await get_tree().process_frame

	# ---------- 主流程：6 阶段推进 ----------
	add_child(load("res://scenes/tutorial.tscn").instantiate())
	await get_tree().process_frame
	await get_tree().process_frame
	var tut := get_node("Tutorial")
	var player: Player = tut.get_node("Player")
	player._auto_fire_enabled = false

	# 阶段 1：3 个静止靶机
	_check(tut._stage == 0, "教程进入阶段 1")
	var targets: Array[Enemy] = []
	for c in tut.get_children():
		if c is Enemy:
			targets.append(c)
	_check(targets.size() == 3, "阶段 1 生成 3 个靶机")
	for t in targets:
		t.take_damage(9999)
	await get_tree().create_timer(1.3).timeout
	_check(tut._stage == 1, "阶段 1 → 2（击杀 3 靶过关）")

	# 阶段 2：加速 ×2 + 冲刺 ×2
	_check(GameState.buff_count(&"phase_dash") == 1, "阶段 2 发放 phase_dash")
	for i in 2:
		Input.action_press("boost")
		await get_tree().physics_frame
		Input.action_release("boost")
		await get_tree().physics_frame
	for i in 2:
		player._dash_cooldown = 0.0  # 绕过 4s 冲刺冷却，缩短测试
		Input.action_press("dash")
		await get_tree().physics_frame
		await get_tree().physics_frame
		Input.action_release("dash")
		await get_tree().create_timer(0.4).timeout
	print("[dbg] boost=", tut._boost_count, " dash=", tut._dash_count)
	_check(tut._boost_count == 2 and tut._dash_count == 2, "阶段 2 输入计数")
	await get_tree().create_timer(1.3).timeout
	_check(tut._stage == 2, "阶段 2 → 3")

	# 阶段 3：5 敌 + 锁血不死
	var enemies: Array[Enemy] = []
	for c in tut.get_children():
		if c is Enemy:
			enemies.append(c)
	_check(enemies.size() == 5, "阶段 3 刷 5 只敌机")
	for i in 3:
		player._invincible = 0.0
		player._last_hit_frame = -1
		player.take_damage(10.0)
		await get_tree().physics_frame
	_check(GameState.health > 0.0 and not player._dead, "战斗阶段锁血不死")
	for e in enemies:
		e.take_damage(9999)
	await get_tree().create_timer(1.3).timeout
	_check(tut._stage == 3, "阶段 3 → 4")

	# 阶段 4：自动对接（弹匣加速消耗到自动释放）
	_check(tut._mothership != null, "阶段 4 母舰已召唤")
	var ms: Mothership = tut._mothership
	ms.position = Vector2(960.0, 270.0)  # 到位触发自动对接
	ms._mag_cells = 1  # 加速演示：1 格弹匣 2s 后自动释放
	await get_tree().create_timer(6.0).timeout
	_check(tut._stage == 4, "对接完成 → 阶段 5")

	# 阶段 5：长按 B 打开基地
	Input.action_press("homecoming")
	await get_tree().create_timer(1.8).timeout
	Input.action_release("homecoming")
	await get_tree().create_timer(0.3).timeout
	_check(tut._base_ui != null and get_tree().paused, "返航打开基地界面")
	await get_tree().create_timer(1.5).timeout
	_check(tut._stage == 5, "基地关闭 → 阶段 6")
	_check(not get_tree().paused, "基地关闭后恢复")

	# 阶段 6：Boss 狂暴即过关
	_check(tut._boss != null, "阶段 6 Boss 已生成")
	var boss: Boss = tut._boss
	# 软锁路径 b：Boss 未狂暴逃跑离场 → 重置阶段 6 重刷 Boss
	boss._begin_escape()
	boss.position.y = -300.0
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(not is_instance_valid(boss), "阶段 6：未狂暴 Boss 逃跑离场")
	_check(tut._stage == 5 and not tut._finished, "阶段 6：逃跑后仍在阶段 6 未过关")
	_check(
		tut._boss != null and is_instance_valid(tut._boss) and tut._boss != boss,
		"阶段 6：逃跑后重置重刷 Boss"
	)
	boss = tut._boss
	boss.take_damage(int(boss.max_hp * 0.75))
	await get_tree().create_timer(0.3).timeout
	_check(tut._finished, "Boss 狂暴触发即过关")
	_check(tut._complete_panel != null, "教程完成面板显示")
	_check(GameState.tutorial_done, "tutorial_done 已写 profile")
	_check(Engine.time_scale == 1.0, "time_scale 正常")

	print("TUTORIAL TEST DONE, failures = ", _failures)
	# Esc 退出：触发场景切换（本节点随后被释放），用绑定 SceneTree 的定时器收尾
	var fails := _failures
	get_tree().create_timer(1.0).timeout.connect(get_tree().quit.bind(fails))
	tut._exit_tutorial()
