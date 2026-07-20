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
	# 清理持久化状态，保证测试确定性（上一轮可能留下存档/天赋/最高分）
	GameState.delete_save()
	GameState.talents.clear()
	GameState.talent_points = 0
	GameState.high_score = 0
	GameState.save_profile()
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
	# Boss 击杀 +500 分触发 1000 分里程碑，关闭 Buff UI 以便继续测试
	if buff_ui.visible:
		buff_ui._on_card_gui_input(ev, &"rapid_fire")
	_check(not buff_ui.visible and not get_tree().paused, "里程碑 UI 可重复触发并关闭")
	# 停掉生成器并清场（敌机/敌弹），保证后续断言确定性
	spawner.set_process(false)
	for child in get_node("Main").get_children():
		if child is Enemy or (child is Bullet and not child.is_player_bullet):
			child.queue_free()
	await get_tree().process_frame

	# 3.5 新 buff 抽查：穿透弹 / 爆炸弹作用于玩家子弹
	var player: Player = get_node("Main/Player")
	GameState.add_buff(&"piercing")
	GameState.add_buff(&"explosive")
	player._fire(Vector2.DOWN)
	var fired: Bullet = null
	for child in get_node("Main").get_children():
		if child is Bullet and child.is_player_bullet:
			fired = child
	_check(fired != null and fired.pierce == 1 and fired.explosive, "穿透/爆炸弹 buff 作用于子弹")
	if fired != null:
		fired.queue_free()

	# 3.6 慢速力场减速敌弹
	var bullet_scene: PackedScene = load("res://scenes/bullet.tscn")
	var eb := bullet_scene.instantiate() as Bullet
	eb.setup(Vector2.DOWN, 400.0, 1, false)
	eb.position = player.position + Vector2(0.0, 100.0)
	_check(eb._speed_factor() == 1.0, "无力场时敌弹全速")
	GameState.add_buff(&"slow_field")
	get_node("Main").add_child(eb)
	await get_tree().process_frame
	_check(eb._speed_factor() < 1.0, "慢速力场减速敌弹")
	eb.queue_free()

	# 3.7 相位冲刺：触发、无敌、位移、冷却
	GameState.add_buff(&"phase_dash")
	var lives_before := GameState.lives
	var pos_before := player.position
	player._invincible = 0.0
	Input.action_press("dash")
	# 无头模式需等物理帧而非 idle 帧，just_pressed 才可靠到达 _physics_process
	await get_tree().physics_frame
	await get_tree().physics_frame
	Input.action_release("dash")
	_check(player._dashing, "相位冲刺触发")
	player.take_damage()
	_check(GameState.lives == lives_before, "冲刺期间无敌")
	await get_tree().create_timer(0.4).timeout
	_check(player.position.distance_to(pos_before) > 100.0, "冲刺位移约 200px")
	_check(player._dash_cooldown > 0.0, "冲刺进入冷却")

	# 3.8 燃料：加速消耗 / 松手回复
	var fuel_before: float = player._fuel
	Input.action_press("boost")
	Input.action_press("move_up")
	await get_tree().create_timer(0.5).timeout
	Input.action_release("boost")
	Input.action_release("move_up")
	_check(player._fuel < fuel_before, "加速消耗燃料")
	var fuel_after_boost: float = player._fuel
	await get_tree().create_timer(0.5).timeout
	_check(player._fuel > fuel_after_boost, "燃料回复")
	_check(player.fuel_drain_rate() == 35.0, "无高效推进时消耗 35/s")
	GameState.add_buff(&"efficient_boost")
	_check(is_equal_approx(player.fuel_drain_rate(), 35.0 * 0.75), "高效推进消耗 -25%")

	# 3.9 精英击毁：高分奖励（得分制，无掉落物）
	var elite := load("res://scenes/enemy.tscn").instantiate() as Enemy
	elite.setup(
		load("res://assets/sprites/elite_ship_1.png") as Texture2D,
		&"straight", true, 1.0, false
	)
	elite.position = Vector2(960.0, 400.0)
	get_node("Main").add_child(elite)
	var score_before_elite := GameState.score
	elite.take_damage(99)
	await get_tree().process_frame
	_check(GameState.score >= score_before_elite + 300, "精英击毁得分奖励")
	# 得分可能再次触发里程碑，关闭之
	if buff_ui.visible:
		buff_ui._on_card_gui_input(ev, &"rapid_fire")
	get_tree().paused = false

	# 3.10 母舰对接补给：回血 + 回燃料 + 无敌
	player._invincible = 0.0
	player.take_damage()
	_check(GameState.lives == 2.0, "母舰测试前置：受击 -1 命")
	player._fuel = 10.0
	var ms := load("res://scenes/mothership.tscn").instantiate() as Mothership
	ms.position = Vector2(960.0, 270.0)
	get_node("Main").add_child(ms)
	ms._state = Mothership.State.HOVER
	player.position = Vector2(960.0, 410.0)  # 对接区（舰体下方矩形）
	await get_tree().create_timer(0.4).timeout
	_check(GameState.lives == 3.0, "母舰补给回满生命")
	_check(player._fuel == player.fuel_max, "母舰补给回满燃料")
	_check(player._invincible > 0.0, "对接后无敌")
	ms.queue_free()
	await get_tree().process_frame

	# 3.11 对局存档：写入 → 清空 → 恢复
	var saved_score := GameState.score
	GameState.add_buff(&"power_shot")
	GameState.save_run(55.0, 12.0)
	_check(GameState.has_save(), "存档文件已写入")
	GameState.score = 0
	GameState.buffs.clear()
	GameState.apply_run_save(GameState.load_run_data())
	_check(GameState.score == saved_score, "存档恢复分数")
	_check(GameState.buff_count(&"power_shot") == 2, "存档恢复 buff 层数")

	# 3.12 返航天赋点折算
	var stacks := 0
	for v in GameState.buffs.values():
		stacks += int(v)
	_check(
		GameState.calc_homecoming_points() == stacks / 2 + GameState.boss_kills * 2,
		"返航天赋点折算公式"
	)

	# 3.13 天赋购买与持久化
	GameState.talent_points = 10
	_check(GameState.buy_talent(&"hull"), "天赋购买成功")
	GameState.buy_talent(&"hull")
	_check(GameState.talent_points == 6 and GameState.talent_level(&"hull") == 2, "购买扣点并计级")
	GameState.talent_points = 0
	_check(not GameState.buy_talent(&"tank"), "点数不足购买失败")
	GameState.talent_points = 6
	GameState.save_profile()
	GameState.talents.clear()
	GameState.talent_points = 0
	GameState.load_profile()
	_check(GameState.talent_level(&"hull") == 2 and GameState.talent_points == 6, "天赋持久化")
	GameState.talents.clear()
	GameState.talent_points = 0
	GameState.high_score = 0
	GameState.save_profile()

	# 3.14 最高分
	_check(GameState.record_score(), "首次破纪录")
	_check(GameState.high_score == GameState.score, "最高分已更新")
	GameState.score = 5
	_check(not GameState.record_score(), "低分不覆盖最高分")
	GameState.score = GameState.high_score

	# 4. 玩家受击至死 → 结算（此时存档存在，死亡应删档）
	for i in 3:
		player._invincible = 0.0
		player.take_damage()
	await get_tree().process_frame
	_check(not GameState.has_save(), "死亡后删除存档")
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
	GameState.delete_save()
	get_tree().quit(_failures)
