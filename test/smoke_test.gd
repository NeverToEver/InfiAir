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
	# 后续各段需长时间真实等待，期间弹幕可能命中玩家：测试窗口内先开无敌
	var player: Player = get_node("Main/Player")
	player._invincible = 999.0

	# 3.1 新移动模式特征
	# spiral：横向振幅 + 整体下压
	var spiral := load("res://scenes/enemy.tscn").instantiate() as Enemy
	spiral.setup(spawner.ENEMY_TYPES[2], &"spiral", 1.0)
	spiral.can_shoot = false
	spiral.position = Vector2(960.0, 200.0)
	get_node("Main").add_child(spiral)
	await get_tree().create_timer(0.8).timeout
	_check(absf(spiral.position.x - 960.0) > 20.0, "spiral 横向振幅")
	_check(spiral.position.y > 200.0, "spiral 整体下压")
	spiral.queue_free()

	# noise：横向速度不规则（采样位移变化量有显著差异）
	var noise := load("res://scenes/enemy.tscn").instantiate() as Enemy
	noise.setup(spawner.ENEMY_TYPES[3], &"noise", 1.0)
	noise.can_shoot = false
	noise.position = Vector2(960.0, 200.0)
	get_node("Main").add_child(noise)
	var xs: Array[float] = []
	for i in 6:
		await get_tree().create_timer(0.3).timeout
		xs.append(noise.position.x)
	var dxs: Array[float] = []
	for i in xs.size() - 1:
		dxs.append(xs[i + 1] - xs[i])
	_check(dxs.max() - dxs.min() > 1.0, "noise 横向飘移不规则")
	noise.queue_free()

	# hover：下行 → 停驻 → 再下行离场
	var hov := load("res://scenes/enemy.tscn").instantiate() as Enemy
	hov.setup(spawner.ENEMY_TYPES[2], &"hover", 1.0)
	hov.can_shoot = false
	hov.position = Vector2(960.0, 250.0)
	get_node("Main").add_child(hov)
	hov._hover_timer = 1.0  # 缩短停驻时间便于测试
	await get_tree().create_timer(1.2).timeout
	_check(hov._hovering, "hover 到达上部 1/3 后停驻")
	var hover_y: float = hov.position.y
	await get_tree().create_timer(0.5).timeout
	_check(absf(hov.position.y - hover_y) < 15.0, "hover 停驻期间位置稳定")
	await get_tree().create_timer(1.2).timeout
	_check(hov.position.y > hover_y + 10.0, "hover 停驻结束后下行离场")
	hov.queue_free()

	# 3.2 Boss 轮换：第 2 只（boss_kills=1）应为游击型
	spawner._spawn_boss()
	await get_tree().process_frame
	var boss2: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss2 = child
	_check(boss2 != null and boss2.boss_type == 2, "Boss 轮换：第 2 只为游击型")
	boss2.take_damage(9999)
	await get_tree().process_frame
	if buff_ui.visible:
		buff_ui._on_card_gui_input(ev, &"rapid_fire")

	# 3.3 Boss-3 母舰型召唤小怪
	spawner._spawn_boss()
	await get_tree().process_frame
	var boss3: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss3 = child
	_check(boss3 != null and boss3.boss_type == 3, "Boss 轮换：第 3 只为母舰型")
	boss3.position.y = Boss.FIGHT_Y  # 跳过降入，下一物理帧进入战斗
	await get_tree().create_timer(7.0).timeout  # 首次召唤在 6s
	var minion_found := false
	for child in get_node("Main").get_children():
		if child is Enemy:
			minion_found = true
	_check(minion_found, "母舰型 Boss 召唤小怪")
	boss3.take_damage(9999)
	await get_tree().process_frame
	if buff_ui.visible:
		buff_ui._on_card_gui_input(ev, &"rapid_fire")
	# 清理小怪与弹幕
	for child in get_node("Main").get_children():
		if child is Enemy or (child is Bullet and not child.is_player_bullet):
			child.queue_free()
	await get_tree().process_frame

	# 3.4 狂暴阶段：血量 <30% 触发，射速 ×1.5
	spawner._spawn_boss(1)
	await get_tree().process_frame
	var boss4: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss4 = child
	boss4.position.y = Boss.FIGHT_Y
	await get_tree().create_timer(0.5).timeout
	boss4.take_damage(int(boss4.max_hp * 0.75))
	await get_tree().process_frame
	_check(boss4._enraged, "Boss 血量 <30% 触发狂暴")
	_check(boss4._base_modulate() != Color.WHITE, "狂暴贴图变红")
	# 狂暴射速：计时器流速 ×1.5 → 0.5s 墙钟消耗 0.75s 计时
	boss4._fire_timer = 1.6
	await get_tree().create_timer(0.5).timeout
	_check(boss4._fire_timer < 1.0, "狂暴后射速提升")
	boss4.take_damage(9999)
	await get_tree().process_frame
	if buff_ui.visible:
		buff_ui._on_card_gui_input(ev, &"rapid_fire")
	get_tree().paused = false
	# 清理弹幕
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet:
			child.queue_free()
	await get_tree().process_frame

	# 3.5 新 buff 抽查：穿透弹 / 爆炸弹作用于玩家子弹
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
	elite.setup(spawner.ELITE_TYPES[0], &"straight", 1.0)
	elite.position = Vector2(960.0, 400.0)
	get_node("Main").add_child(elite)
	var score_before_elite := GameState.score
	elite.take_damage(99)
	await get_tree().process_frame
	_check(
		GameState.score >= score_before_elite + int(spawner.ELITE_TYPES[0]["score"]),
		"精英击毁得分奖励"
	)
	# 得分可能再次触发里程碑，关闭之
	if buff_ui.visible:
		buff_ui._on_card_gui_input(ev, &"rapid_fire")
	get_tree().paused = false

	# 3.10 母舰对接补给：回血 + 回燃料 + 无敌
	# 清理可能残留的敌弹，避免干扰生命断言
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet:
			child.queue_free()
	await get_tree().process_frame
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
