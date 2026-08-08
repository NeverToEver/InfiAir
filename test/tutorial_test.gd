extends Node
## M3b：Enemy 迁 C#，经脚本资源做类型判定（gdlint class-load-variable-name：snake_case）
var _enemy_script := load("res://csharp/godot/Enemy.cs")
## 教程流程测试：6 阶段推进、锁血、对接、返航开基地、狂暴过关、Esc 退出、profile 写入。

const TUTORIAL_SCENE: PackedScene = preload("res://scenes/tutorial.tscn")

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
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.tutorial_done = false
	GameState.save_profile()

	# ---------- 软锁路径 a：玩家死亡 → 显示任务失败提示（独立实例，不影响主流程） ----------
	var tut_a: Node2D = TUTORIAL_SCENE.instantiate()
	add_child(tut_a)
	await get_tree().process_frame
	await get_tree().process_frame
	var player_a = tut_a.get_node("Player")  # M3c：Player 迁 C#，不能作类型注解
	player_a.set_auto_fire(false)
	player_a.set_invincible(0.0)
	player_a.set_last_hit_frame(-1)
	player_a.take_damage(9999.0)
	await get_tree().process_frame
	_check(player_a.is_dead(), "死亡路径：玩家已死亡")
	_check(tut_a.failed(), "死亡路径：教程进入失败态")
	_check(tut_a.title_label().text == tr("TUT_FAIL_TITLE"), "死亡路径：标题显示任务失败（tr 命中）")
	_check(tut_a.objective_label().text == tr("TUT_FAIL_DESC"), "死亡路径：提示 Esc 退出（tr 命中）")
	tut_a.queue_free()
	await get_tree().process_frame
	await get_tree().process_frame

	# ---------- 主流程：6 阶段推进 ----------
	add_child(TUTORIAL_SCENE.instantiate())
	await get_tree().process_frame
	await get_tree().process_frame
	var tut := get_node("Tutorial")
	var player = tut.get_node("Player")  # M3c：Player 迁 C#，不能作类型注解
	player.set_auto_fire(false)

	# 阶段 1：3 个静止靶机
	_check(tut.stage() == 0, "教程进入阶段 1")
	var targets: Array = []  # M3b：Enemy 迁 C#，不能作元素类型注解
	for c in tut.get_children():
		if is_instance_of(c, _enemy_script):  # M3b：Enemy 迁 C#，不能经类名 is 判定
			targets.append(c)
	_check(targets.size() == 3, "阶段 1 生成 3 个靶机")
	for t in targets:
		t.take_damage(9999)
	await get_tree().create_timer(1.3).timeout
	_check(tut.stage() == 1, "阶段 1 → 2（击杀 3 靶过关）")

	# 阶段 2：加速 ×2 + 冲刺 ×2
	_check(GameState.buff_count(&"phase_dash") == 1, "阶段 2 发放 phase_dash")
	for i in 2:
		Input.action_press("boost")
		await get_tree().physics_frame
		Input.action_release("boost")
		await get_tree().physics_frame
	for i in 2:
		player.set_dash_cooldown(0.0)  # 绕过 4s 冲刺冷却，缩短测试
		Input.action_press("dash")
		await get_tree().physics_frame
		await get_tree().physics_frame
		Input.action_release("dash")
		await get_tree().create_timer(0.4).timeout
	_check(tut.boost_count() == 2 and tut.dash_count() == 2, "阶段 2 输入计数")
	await get_tree().create_timer(1.3).timeout
	_check(tut.stage() == 2, "阶段 2 → 3")

	# 阶段 3：5 敌 + 锁血不死
	var enemies: Array = []  # M3b：Enemy 迁 C#，不能作元素类型注解
	for c in tut.get_children():
		if is_instance_of(c, _enemy_script):  # M3b：Enemy 迁 C#，不能经类名 is 判定
			enemies.append(c)
	_check(enemies.size() == 5, "阶段 3 刷 5 只敌机")
	for i in 3:
		player.set_invincible(0.0)
		player.set_last_hit_frame(-1)
		player.take_damage(10.0)
		await get_tree().physics_frame
	_check(GameState.health > 0.0 and not player.is_dead(), "战斗阶段锁血不死")
	# 补刷兜底：先击杀 2 只，其余 3 只未计击杀直接离场（模拟飞出屏幕自毁）→ 自动补足剩余 3 只
	enemies[0].take_damage(9999)
	enemies[1].take_damage(9999)
	await get_tree().physics_frame
	for i in range(2, 5):
		enemies[i].queue_free()
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(tut.stage_kills() == 2, "阶段 3 已计 2 杀（离场不虚增）")
	enemies.clear()
	for c in tut.get_children():
		if is_instance_of(c, _enemy_script):  # M3b：Enemy 迁 C#，不能经类名 is 判定
			enemies.append(c)
	_check(enemies.size() == 3, "阶段 3 敌机离场后自动补足剩余 3 只")
	_check(tut.stage() == 2, "补刷后仍在阶段 3")
	for e in enemies:
		e.take_damage(9999)
	await get_tree().create_timer(1.3).timeout
	_check(tut.stage() == 3, "阶段 3 → 4")

	# 阶段 4：长按 H 蓄力召唤母舰（对齐正局 dock 键），穿梭入场后自动对接（弹匣加速消耗到自动释放）
	_check(tut.mothership() == null, "阶段 4 进场时母舰未召唤（需长按 H）")
	Input.action_press("dock")
	await get_tree().create_timer(tut.DOCK_CHARGE_TIME + 0.3).timeout
	Input.action_release("dock")
	_check(tut.mothership() != null, "阶段 4 蓄力完成后母舰已召唤")
	var ms = tut.mothership()  # M4：Mothership 迁 C#，去类型注解
	ms.set_state_timer(ms.WARP_IN_TIME)  # 快进穿梭入场，到位触发自动对接
	ms.set_mag_cells(1)  # 加速演示：1 格弹匣 2s 后自动释放
	await get_tree().create_timer(6.0).timeout
	_check(tut.stage() == 4, "对接完成 → 阶段 5")

	# 阶段 5：长按 B 打开基地
	Input.action_press("homecoming")
	await get_tree().create_timer(1.8).timeout
	Input.action_release("homecoming")
	await get_tree().create_timer(0.3).timeout
	_check(tut.base_ui() != null and get_tree().paused, "返航打开基地界面")
	await get_tree().create_timer(1.5).timeout
	_check(tut.stage() == 5, "基地关闭 → 阶段 6")
	_check(not get_tree().paused, "基地关闭后恢复")

	# 阶段 6：Boss 狂暴即过关
	_check(tut.boss() != null, "阶段 6 Boss 已生成")
	var boss = tut.boss()  # M3d：Boss 迁 C#，去类型注解
	# 软锁路径 b：Boss 未狂暴逃跑离场 → 重置阶段 6 重刷 Boss
	boss.begin_escape()
	boss.position.y = -300.0
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(not is_instance_valid(boss), "阶段 6：未狂暴 Boss 逃跑离场")
	_check(tut.stage() == 5 and not tut.finished(), "阶段 6：逃跑后仍在阶段 6 未过关")
	_check(tut.boss() != null and is_instance_valid(tut.boss()) and tut.boss() != boss, "阶段 6：逃跑后重置重刷 Boss")
	boss = tut.boss()
	boss.take_damage(int(boss.max_hp * 0.75))
	await get_tree().create_timer(0.3).timeout
	_check(tut.finished(), "Boss 狂暴触发即过关")
	_check(tut.complete_panel() != null, "教程完成面板显示")
	_check(GameState.tutorial_done, "tutorial_done 已写 profile")
	_check(Engine.time_scale == 1.0, "time_scale 正常")

	# K16：教程通关写盘的持久化状态收尾恢复（TESTING.md 约定：结束时清理自己创建的持久化状态；
	# 测试开头已把 tutorial_done 置 false，此处恢复 false 不污染用户 profile）
	GameState.tutorial_done = false
	GameState.save_profile()

	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("TUTORIAL TEST DONE, failures = ", _failures)
	# Esc 退出：触发场景切换（本节点随后被释放），用绑定 SceneTree 的定时器收尾
	# C31：注入 ui_cancel 动作走公开输入路径，不直调私有 _exit_tutorial
	var cancel := InputEventAction.new()
	cancel.action = &"ui_cancel"
	cancel.pressed = true
	Input.parse_input_event(cancel)
	var fails := _failures
	get_tree().create_timer(1.0).timeout.connect(get_tree().quit.bind(fails))
