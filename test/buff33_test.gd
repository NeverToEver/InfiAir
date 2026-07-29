extends Node
## 3.3 战斗补全测试（Buff/母舰/放弃）：laser_beam 光束、mothership_recall 冷却减半、
## boost_recovery 恢复提升、explosive 抽卡 gating、长按 K 放弃出击。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	GameState.high_score = 0
	GameState.save_profile()
	# give_up 输入映射由 project.godot 提供；缺失时运行时补齐（不影响断言语义）
	if not InputMap.has_action(&"give_up"):
		InputMap.add_action(&"give_up")
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	var main := get_node("Main")
	# 开场欢迎页（首屏）暂停游戏，先关闭进入开始面板
	var welcome: CanvasLayer = get_node("Main/WelcomeScreen")
	if welcome.visible:
		welcome.dismiss()
	# 开场面板自显即暂停（冻结背景），先关闭解除
	var start_panel: CanvasLayer = get_node("Main/StartPanel")
	if start_panel.visible:
		start_panel._on_new_game_pressed()
	var player: Player = get_node("Main/Player")
	var hud: CanvasLayer = get_node("Main/HUD")
	var buff_ui: CanvasLayer = get_node("Main/BuffUI")
	var spawner: Node = get_node("Main/Spawner")
	# 全自动开火会干扰激光断言，测试全程禁用
	player._auto_fire_enabled = false
	player._invincible = 999.0
	spawner.set_process(false)
	await get_tree().process_frame
	await get_tree().process_frame
	for child in main.get_children():
		if child is Enemy or child is Bullet:
			child.queue_free()
	await get_tree().process_frame

	# 1. Buff 候选池：新 buff 入池 + explosive gating（boss_kills>=3 才解锁）
	GameState.boss_kills = 0
	var ids: Array = buff_ui._available_buffs().map(func(b: Dictionary) -> StringName: return b["id"])
	_check(&"laser_beam" in ids, "laser_beam 入抽卡候选池")
	_check(&"mothership_recall" in ids, "mothership_recall 入抽卡候选池")
	_check(&"boost_recovery" in ids, "boost_recovery 入抽卡候选池")
	_check(&"explosive" not in ids, "boss_kills<3 时 explosive 不入候选池")
	GameState.boss_kills = 3
	ids = buff_ui._available_buffs().map(func(b: Dictionary) -> StringName: return b["id"])
	_check(&"explosive" in ids, "boss_kills>=3 时 explosive 入候选池")
	GameState.boss_kills = 0

	# 2. laser_beam：无 buff 不触发；获得后触发光束并穿透伤害直线上 2 个敌人
	var laser: LaserWeapon = player.get_node("LaserWeapon")
	await get_tree().create_timer(0.4).timeout
	_check(not laser._active, "无 laser_beam buff 时激光不触发")
	GameState.add_buff(&"laser_beam")
	# 沿瞄准线布置两个静止高血敌人（不被击毁，避免得分里程碑干扰）
	var aim: Vector2 = (player.get_global_mouse_position() - player.global_position).normalized()
	if aim.length() < 0.5:
		aim = Vector2.UP
	var enemy_scene: PackedScene = load("res://scenes/enemy.tscn")
	var e1 := enemy_scene.instantiate() as Enemy
	e1.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	e1.hp = 9999
	e1.speed = 0.0
	e1.can_shoot = false
	e1.position = player.global_position + aim * 300.0
	main.add_child(e1)
	var e2 := enemy_scene.instantiate() as Enemy
	e2.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	e2.hp = 9999
	e2.speed = 0.0
	e2.can_shoot = false
	e2.position = player.global_position + aim * 600.0
	main.add_child(e2)
	var wait := 0.0
	while not laser._active and wait < 2.0:
		await get_tree().create_timer(0.1).timeout
		wait += 0.1
	_check(laser._active, "获得 laser_beam 后触发光束")
	_check(laser._beam.visible, "光束视觉可见")
	await get_tree().create_timer(0.5).timeout
	_check(is_instance_valid(e1) and e1.hp < 9999, "光束对直线上敌人 1 造成伤害")
	_check(is_instance_valid(e2) and e2.hp < 9999, "光束穿透对直线上敌人 2 造成伤害")
	# 3s 持续结束后进入约 8s 冷却
	await get_tree().create_timer(2.8).timeout
	_check(not laser._active, "3 秒后光束结束")
	_check(laser._cooldown > 6.0, "光束结束后进入约 8s 冷却")
	# 冷却结束可再次触发（测试直接缩短冷却，不真等 8s）
	laser._cooldown = 0.05
	await get_tree().create_timer(0.3).timeout
	_check(laser._active, "冷却结束后激光可再次触发")
	laser._active_time = 0.01
	await get_tree().create_timer(0.2).timeout
	if is_instance_valid(e1):
		e1.queue_free()
	if is_instance_valid(e2):
		e2.queue_free()
	await get_tree().process_frame

	# 3. mothership_recall：每层母舰冷却 ×0.5（基础 60s→30s→15s）
	main._on_mothership_departed(60.0)
	_check(is_equal_approx(main._dock_cooldown, 60.0), "无 recall 时母舰冷却 60s")
	GameState.add_buff(&"mothership_recall")
	main._on_mothership_departed(60.0)
	_check(is_equal_approx(main._dock_cooldown, 30.0), "recall 1 层母舰冷却 30s")
	GameState.add_buff(&"mothership_recall")
	main._on_mothership_departed(60.0)
	_check(is_equal_approx(main._dock_cooldown, 15.0), "recall 2 层母舰冷却 15s")
	_check(main.dock_status_text().contains("母舰冷却"), "母舰状态文本联动冷却值")
	main._dock_cooldown = 0.0

	# 4. boost_recovery：恢复速率每层 ×1.5（乘算），且实际回油生效
	_check(is_equal_approx(player.fuel_regen_rate(), 20.0), "无 buff 燃料恢复 20/s")
	GameState.add_buff(&"boost_recovery")
	_check(is_equal_approx(player.fuel_regen_rate(), 30.0), "boost_recovery 1 层恢复 ×1.5")
	GameState.add_buff(&"boost_recovery")
	_check(is_equal_approx(player.fuel_regen_rate(), 45.0), "boost_recovery 2 层乘算 ×2.25")
	player._fuel = 50.0
	await get_tree().create_timer(1.0).timeout
	_check(player._fuel > 80.0, "提升后的恢复速率实际生效（1s 回 45）")

	# 5. 长按 K 放弃出击：蓄力可取消，蓄满 3s 自毁进死亡结算
	Input.action_press("give_up")
	await get_tree().create_timer(1.0).timeout
	_check(main._give_up_charge > 0.0, "K 蓄力进行中")
	_check(hud._give_up_label.visible, "HUD 显示放弃蓄力进度")
	Input.action_release("give_up")
	await get_tree().create_timer(0.2).timeout
	_check(main._give_up_charge == 0.0 and not hud._give_up_label.visible, "松开 K 取消蓄力")
	_check(GameState.health == GameState.max_health(), "取消蓄力未自毁")
	Input.action_press("give_up")
	await get_tree().create_timer(3.3).timeout
	Input.action_release("give_up")
	await get_tree().process_frame
	_check(GameState.health == 0.0, "长按 K 3s 自毁")
	_check(player._dead, "自毁后玩家死亡")
	_check(get_node("Main/GameOverUI").visible, "自毁进入死亡结算面板")
	_check(get_tree().paused, "结算时游戏暂停")

	print("BUFF33 TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
