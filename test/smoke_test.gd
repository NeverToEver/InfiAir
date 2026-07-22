extends Node
## 临时冒烟测试：覆盖里程碑 Buff UI、Boss 生成、玩家死亡结算路径。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 无头模式 warp_mouse 无效：用合成鼠标移动事件把准星放到指定 canvas 坐标
func _move_mouse_to(canvas_pos: Vector2) -> void:
	var win: Vector2 = get_tree().root.get_screen_transform() * canvas_pos
	var mev := InputEventMouseMotion.new()
	mev.position = win
	mev.global_position = win
	Input.parse_input_event(mev)


func _ready() -> void:
	# 清理持久化状态，保证测试确定性（上一轮可能留下存档/最高分）
	GameState.delete_save()
	GameState.high_score = 0
	GameState.save_profile()
	# 固定 easy 档（分数 ×1），保持本测试既有数值断言；结束时恢复 medium
	GameState.set_difficulty(&"easy")
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	add_child(main_scene.instantiate())
	# 欢迎页（进游戏首屏）→ 关闭后进入开始面板
	var welcome: CanvasLayer = get_node("Main/WelcomeScreen")
	if welcome.visible:
		welcome.dismiss()
	# 无存档时开始面板会自显：直接走「开始游戏」关闭之（难度选择见 difficulty_test）
	var start_panel: CanvasLayer = get_node("Main/StartPanel")
	if start_panel.visible:
		start_panel._on_new_game_pressed()
	# 玩家已改全自动开火：测试全程禁用，避免误伤敌机/Boss 或触发意外得分里程碑
	get_node("Main/Player")._auto_fire_enabled = false
	await get_tree().process_frame
	await get_tree().process_frame

	# 1. 里程碑触发 Buff UI（阈值已改曲线，测试用 override 固定 500 保证确定性）
	GameState._set_milestone_override(500)
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
	# 里程碑曲线下后续阈值远高于当前分数，Buff UI 一般不再弹出；若弹出则关闭以便继续测试
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
	boss3.position.y = boss3.FIGHT_Y  # 跳过降入，下一物理帧进入战斗
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
	boss4.position.y = boss4.FIGHT_Y
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

	# 3.6 慢速力场：全局敌机移速 ×0.8（A13，敌弹不受影响）
	var slow_e := load("res://scenes/enemy.tscn").instantiate() as Enemy
	slow_e.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	slow_e.can_shoot = false
	slow_e.speed = 100.0
	slow_e.position = Vector2(960.0, 100.0)
	get_node("Main").add_child(slow_e)
	await get_tree().create_timer(0.5).timeout
	var slow_d1: float = slow_e.position.y - 100.0
	slow_e.queue_free()
	GameState.add_buff(&"slow_field")
	var slow_e2 := load("res://scenes/enemy.tscn").instantiate() as Enemy
	slow_e2.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	slow_e2.can_shoot = false
	slow_e2.speed = 100.0
	slow_e2.position = Vector2(960.0, 100.0)
	get_node("Main").add_child(slow_e2)
	await get_tree().create_timer(0.5).timeout
	var slow_d2: float = slow_e2.position.y - 100.0
	slow_e2.queue_free()
	_check(slow_d1 > 20.0 and slow_d2 < slow_d1 * 0.9, "慢速力场全局敌机移速 ×0.8")

	# 3.7 相位冲刺：触发、无敌、位移、冷却
	GameState.add_buff(&"phase_dash")
	var health_before := GameState.health
	player._since_damage = 0.0  # 冻结被动回血，避免干扰 HP 断言
	var pos_before := player.position
	player._invincible = 0.0
	Input.action_press("dash")
	# 无头模式需等物理帧而非 idle 帧，just_pressed 才可靠到达 _physics_process
	await get_tree().physics_frame
	await get_tree().physics_frame
	Input.action_release("dash")
	_check(player._dashing, "相位冲刺触发")
	player.take_damage()
	_check(GameState.health == health_before, "冲刺期间无敌")
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
	elite.take_damage(9999)
	await get_tree().process_frame
	_check(
		GameState.score >= score_before_elite + int(spawner.ELITE_TYPES[0]["score"]),
		"精英击毁得分奖励"
	)
	# 得分可能再次触发里程碑，关闭之
	if buff_ui.visible:
		buff_ui._on_card_gui_input(ev, &"rapid_fire")
	get_tree().paused = false

	# 3.10 母舰（原作对齐）：蓄力召唤 → 到位自动对接（点吸附）→ 驻留弹匣 → 提前离舰
	# 清理可能残留的敌弹，避免干扰生命断言
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet:
			child.queue_free()
	await get_tree().process_frame
	var main := get_node("Main")
	player._invincible = 0.0
	player._last_hit_frame = -1
	player.take_damage(10.0)
	_check(GameState.health == 90.0, "母舰测试前置：受击 -10 HP")
	player._fuel = 10.0
	# 长按蓄力：1s 松手取消，不进冷却
	Input.action_press("dock")
	await get_tree().create_timer(1.0).timeout
	_check(main._charging and main._mothership == null, "蓄力中未召唤")
	Input.action_release("dock")
	await get_tree().create_timer(0.2).timeout
	_check(not main._charging and main._dock_cooldown <= 0.0, "松手取消蓄力不进冷却")
	# 蓄满 3s 召唤
	Input.action_press("dock")
	await get_tree().create_timer(3.3).timeout
	Input.action_release("dock")
	_check(main._mothership != null, "蓄力满 3s 召唤母舰")
	var ms: Mothership = main._mothership
	# 到位即自动对接（原作无区域判定，点吸附补间）
	ms.position = Vector2(960.0, 269.0)
	var tgt := load("res://scenes/enemy.tscn").instantiate() as Enemy
	tgt.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	tgt.can_shoot = false
	tgt.hp = 9999  # 靶机不死，保证场内始终有目标
	tgt.position = Vector2(960.0, 500.0)
	main.add_child(tgt)
	await get_tree().create_timer(0.5).timeout
	_check(player._input_locked, "对接开始即锁输入")
	_check(player._invincible > 100.0, "对接开始即无敌（无敌窗口前移）")
	# 对接 1.5s + 补给 0.5s 后进入驻留
	await get_tree().create_timer(2.0).timeout
	_check(GameState.health == GameState.max_health(), "补给回满生命")
	_check(player._fuel == player.fuel_max, "补给回满燃料")
	_check(ms._state == Mothership.State.STAY, "进入驻留状态")
	_check(ms._mag_cells == 10, "弹匣初始 10 格")
	# 驻留火力：加特林弹丸（score_scale=1/3）+ 导弹（splash 标记）
	await get_tree().create_timer(0.6).timeout
	var gatling_found := false
	var missile_found := false
	for child in main.get_children():
		if child is Bullet and child.is_player_bullet and child.score_scale < 1.0:
			gatling_found = true
			if child.splash_damage > 0:
				missile_found = true
	_check(gatling_found, "加特林扫射开火")
	_check(missile_found, "导弹齐射开火（A15）")
	# 驻留驾驶：WASD 移动母舰，玩家钉在对接点
	var ms_x_before: float = ms.position.x
	Input.action_press("move_right")
	await get_tree().create_timer(0.5).timeout
	Input.action_release("move_right")
	_check(ms.position.x > ms_x_before + 20.0, "驻留期间 WASD 驾驶母舰")
	_check(
		player.global_position.distance_to(ms.global_position + Vector2(0.0, 140.0)) < 5.0,
		"驾驶时玩家钉在对接点"
	)
	# 驾驶边界钳制：持续左行被钳在视野内（x ≥ 视图左缘 + 130）
	Input.action_press("move_left")
	await get_tree().create_timer(4.5).timeout
	Input.action_release("move_left")
	_check(
		absf(ms.position.x - (GameState.view_world_rect().position.x + 130.0)) < 30.0,
		"母舰驾驶边界钳制"
	)
	if buff_ui.visible:
		buff_ui._on_card_gui_input(ev, &"rapid_fire")
	get_tree().paused = false
	# 弹匣随时间消耗（驻留已累计 >2s）
	_check(ms._mag_cells < 10, "驻留弹匣消耗")
	# ≤4 格警告 + 警告 5s 后强制离舰计时
	ms._mag_cells = 5
	ms._mag_cell_timer = 0.0
	await get_tree().create_timer(2.3).timeout
	_check(ms._mag_cells == 4, "弹匣消耗到 4 格")
	_check(ms._mag_warned, "弹匣 ≤4 弹出警告")
	_check(ms._warn_eject_timer > 0.0, "警告后启动强制离舰计时")
	# 提前离舰：长按 H 2s，冷却双机制折扣（4→3 格 r=0.3：60×0.88×0.85≈44.9）
	Input.action_press("dock")
	await get_tree().create_timer(2.4).timeout
	Input.action_release("dock")
	_check(ms._state >= Mothership.State.RELEASE, "提前离舰触发")
	await get_tree().create_timer(0.6).timeout
	_check(
		player._invincible > 1.0 and player._invincible <= 2.0,
		"释放后 2s 保护（重制版 QoL）"
	)
	await get_tree().create_timer(0.2).timeout
	_check(main._dock_cooldown > 42.5 and main._dock_cooldown < 45.2, "提前离舰冷却双机制折扣")
	_check(not player._input_locked, "脱离后输入解锁")
	if main._mothership != null:
		main._mothership.queue_free()
	# 母舰击杀 1/3 分（100 分敌机 → +33）
	for child in main.get_children():
		if child is Enemy:
			child.queue_free()
	await get_tree().process_frame
	var e33 := load("res://scenes/enemy.tscn").instantiate() as Enemy
	e33.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	e33.hp = 1
	e33.position = Vector2(960.0, 400.0)
	main.add_child(e33)
	var b33 := (load("res://scenes/bullet.tscn") as PackedScene).instantiate() as Bullet
	b33.setup(Vector2.DOWN, 800.0, 1, true)
	b33.score_scale = 1.0 / 3.0
	b33.position = e33.position
	main.add_child(b33)
	var score_before_33 := GameState.score
	await get_tree().create_timer(0.3).timeout
	_check(GameState.score == score_before_33 + 33, "母舰击杀 1/3 分")
	if buff_ui.visible:
		buff_ui._on_card_gui_input(ev, &"rapid_fire")
	get_tree().paused = false
	# 警告横幅播完（5s）强制离舰：第二艘母舰，缩短计时确定性验证
	main._dock_cooldown = 0.0
	main._summon_mothership()
	var ms2: Mothership = main._mothership
	ms2.position = Vector2(960.0, 269.0)
	await get_tree().create_timer(2.5).timeout  # 自动对接 + 补给 → 驻留
	_check(ms2._state == Mothership.State.STAY, "第二艘母舰进入驻留")
	ms2._mag_cells = 5
	ms2._mag_cell_timer = 0.0
	await get_tree().create_timer(2.3).timeout
	_check(ms2._mag_warned, "第二艘母舰弹匣警告")
	ms2._warn_eject_timer = 0.5  # 缩短横幅等待，直接验证强制离舰
	await get_tree().create_timer(1.0).timeout
	_check(ms2._state >= Mothership.State.RELEASE, "警告播完强制离舰（对齐原作）")
	if main._mothership != null:
		main._mothership.queue_free()

	# 3.11 对局存档：写入 → 清空 → 恢复
	var saved_score := GameState.score
	GameState.add_buff(&"power_shot")
	GameState.health = 66.0
	GameState.save_run(55.0, 12.0)
	_check(GameState.has_save(), "存档文件已写入")
	GameState.score = 0
	GameState.health = 100.0
	GameState.buffs.clear()
	GameState.apply_run_save(GameState.load_run_data())
	_check(GameState.score == saved_score, "存档恢复分数")
	_check(GameState.buff_count(&"power_shot") == 2, "存档恢复 buff 层数")
	_check(GameState.health == 66.0, "存档恢复 HP（v2 格式）")

	# 3.12 返航（局内中场整备）：蓄力 → 基地 → 维修 → 继续出击返回同局
	var score_before_hc := GameState.score
	var power_before := GameState.buff_count(&"power_shot")
	GameState.add_rp(5)
	GameState.health = 50.0
	# 蓄力松手取消
	Input.action_press("homecoming")
	await get_tree().create_timer(0.6).timeout
	Input.action_release("homecoming")
	await get_tree().create_timer(0.2).timeout
	_check(not main._homecoming and not get_tree().paused, "返航蓄力松手取消")
	# 蓄满 1.5s 触发
	Input.action_press("homecoming")
	await get_tree().create_timer(1.7).timeout
	Input.action_release("homecoming")
	await get_tree().create_timer(1.5).timeout  # 白屏过场
	_check(main._homecoming, "返航触发")
	_check(main._base_ui.visible and get_tree().paused, "进入基地整备界面")
	# 维修扣 RP 回满（对齐原作 2RP 回满）
	var rp_before := GameState.rp
	main._base_ui._on_repair_pressed()
	_check(GameState.rp == rp_before - 2, "维修扣 2RP")
	_check(GameState.health == GameState.max_health(), "维修回满生命")
	# 放一个敌机验证轨道打击
	var orbit_e := load("res://scenes/enemy.tscn").instantiate() as Enemy
	orbit_e.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	orbit_e.can_shoot = false
	orbit_e.position = Vector2(400.0, 300.0)
	main.add_child(orbit_e)
	# 继续出击 → 返回同一局
	main._base_ui._on_resume_pressed()
	await get_tree().create_timer(0.2).timeout
	_check(not get_tree().paused and not main._homecoming, "继续出击恢复游戏")
	_check(GameState.score == score_before_hc, "返回同一局：分数保留")
	_check(GameState.buff_count(&"power_shot") == power_before, "返回同一局：buff 保留")
	_check(GameState.has_save(), "返航后存档保留")
	var enemy_left := false
	for child in main.get_children():
		if child is Enemy:
			enemy_left = true
	_check(not enemy_left, "轨道打击清屏")
	# 恢复刷怪会干扰后续断言，重新停掉生成器并清场
	spawner.set_process(false)
	for child in main.get_children():
		if child is Enemy or (child is Bullet and not child.is_player_bullet):
			child.queue_free()
	await get_tree().process_frame

	# 3.13 最高分
	GameState.high_score = 0
	_check(GameState.record_score(), "首次破纪录")
	_check(GameState.high_score == GameState.score, "最高分已更新")
	GameState.score = 5
	_check(not GameState.record_score(), "低分不覆盖最高分")
	GameState.score = GameState.high_score

	# 3.14 Shift/Ctrl toggle 模式（按一下切换开/关）
	GameState.set_shift_toggle_mode(true)
	Input.action_press("boost")
	await get_tree().physics_frame
	Input.action_release("boost")
	await get_tree().physics_frame
	_check(player._boost_toggle_on, "toggle 模式按一下开启加速")
	Input.action_press("boost")
	await get_tree().physics_frame
	Input.action_release("boost")
	await get_tree().physics_frame
	_check(not player._boost_toggle_on, "toggle 模式再按一下关闭加速")
	GameState.set_shift_toggle_mode(false)
	GameState.set_ctrl_toggle_mode(true)
	Input.action_press("fine_move")
	await get_tree().physics_frame
	Input.action_release("fine_move")
	await get_tree().physics_frame
	_check(player._fine_toggle_on, "toggle 模式按一下开启微调")
	GameState.set_ctrl_toggle_mode(false)
	player._fine_toggle_on = false

	# 4. 玩家受击至死 → 结算（此时存档存在，死亡应删档）
	player._invincible = 0.0
	player._last_hit_frame = -1
	player.take_damage(9999.0)
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

	# 5.1 设置面板 opener 回归：开始/暂停面板打开设置时必须让位（layer 30 会挡住 16），
	# 返回/Esc 后恢复打开者，而不是一律弹暂停菜单
	var start_panel5: CanvasLayer = get_node("Main/StartPanel")
	var settings_ui: CanvasLayer = get_node("Main/SettingsUI")
	pause_ui.toggle()
	pause_ui._on_settings_pressed()
	_check(not pause_ui.visible and settings_ui.visible, "暂停→设置：暂停面板让位")
	_check(settings_ui._opener == pause_ui, "暂停→设置：opener 记录为暂停面板")
	settings_ui._on_back_pressed()
	_check(pause_ui.visible and not settings_ui.visible, "设置返回：恢复暂停面板")
	pause_ui.toggle()
	_check(not pause_ui.visible, "设置回归：暂停面板已关闭")
	start_panel5.show_panel()
	start_panel5._on_settings_pressed()
	_check(not start_panel5.visible and settings_ui.visible, "开始→设置：开始面板让位不遮挡")
	get_node("Main/BackNavigator").go_back()  # Esc 全局路由已移交 BackNavigator
	_check(start_panel5.visible and not settings_ui.visible, "设置中 Esc：返回开始面板")
	_check(not pause_ui.visible, "设置中 Esc：未误弹暂停菜单")
	start_panel5._dismiss()

	# 6. 迭代 3.3 玩家侧：瞄准辅助 / 冲刺耗燃料 / Ctrl 微调
	# 第 4 节玩家已受击至死：复活以便继续测试（不重开 hitbox，避免杂散碰撞）
	player._dead = false
	player._invincible = 999.0
	player.show()
	player.set_physics_process(true)
	GameState.health = GameState.max_health()
	get_node("Main/GameOverUI").hide()
	get_tree().paused = false

	# 6.1 瞄准辅助：磁吸锁定 → 子弹朝锁定点；甩鼠标脱离
	player.position = Vector2(960.0, 800.0)
	player.velocity = Vector2.ZERO
	var aim_e := load("res://scenes/enemy.tscn").instantiate() as Enemy
	aim_e.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	aim_e.can_shoot = false
	aim_e.hp = 9999  # 防止被测试弹击毁触发里程碑
	aim_e.position = player.position + Vector2(0.0, -400.0)
	main.add_child(aim_e)
	await get_tree().process_frame
	# 准星放到敌机旁 120px（<230px 磁吸半径）
	_move_mouse_to(aim_e.get_global_transform_with_canvas().origin + Vector2(120.0, 0.0))
	# 等输入事件落入 viewport；首帧位移超阈值按甩鼠标处理，需再等一帧才重新磁吸
	await get_tree().process_frame
	await get_tree().physics_frame
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(player._aim_lock_target == aim_e, "瞄准辅助磁吸锁定最近敌人")
	player._auto_fire_enabled = true
	player._fire_cooldown = 0.0
	await get_tree().physics_frame
	await get_tree().physics_frame
	player._auto_fire_enabled = false
	var ab: Bullet = null
	for child in main.get_children():
		if child is Bullet and child.is_player_bullet:
			ab = child
			break
	_check(ab != null, "锁定期间自动开火")
	if ab != null:
		var want: Vector2 = (aim_e.global_position - player.global_position).normalized()
		_check(ab.direction.dot(want) > 0.99, "锁定期间子弹朝目标而非原始鼠标")
		ab.queue_free()
	# 单帧甩鼠标 >90px 到空区 → 脱离锁定
	_move_mouse_to(Vector2(200.0, 950.0))
	await get_tree().process_frame
	await get_tree().physics_frame
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(player._aim_lock_target == null, "快速甩鼠标脱离锁定")
	aim_e.queue_free()
	await get_tree().process_frame

	# 6.2 冲刺耗燃料：消耗满值的 25%，不足时禁用
	player.position = Vector2(960.0, 540.0)
	player._fuel = player.fuel_max
	player._dash_cooldown = 0.0
	Input.action_press("dash")
	await get_tree().physics_frame
	await get_tree().physics_frame
	Input.action_release("dash")
	_check(player._dashing, "燃料充足时冲刺可触发")
	_check(absf(player._fuel - player.fuel_max * 0.75) < 3.0, "冲刺消耗约 25% 燃料")
	await get_tree().create_timer(0.4).timeout  # 等冲刺结束
	player._fuel = player.fuel_max * 0.2
	player._dash_cooldown = 0.0
	Input.action_press("dash")
	await get_tree().physics_frame
	await get_tree().physics_frame
	Input.action_release("dash")
	_check(not player._dashing, "燃料不足 25% 时禁用冲刺")

	# 6.3 Ctrl 微调：移速 ×0.35
	player.position = Vector2(960.0, 540.0)
	Input.action_press("move_right")
	await get_tree().create_timer(0.5).timeout
	var full_speed: float = player.velocity.length()
	Input.action_press("fine_move")
	await get_tree().create_timer(0.5).timeout
	var fine_speed: float = player.velocity.length()
	Input.action_release("fine_move")
	Input.action_release("move_right")
	_check(full_speed > player.MAX_SPEED * 0.9, "无微调时接近满速")
	_check(absf(fine_speed - player.MAX_SPEED * 0.35) < 25.0, "Ctrl 按住移速 ×0.35")

	print("SMOKE TEST DONE, failures = ", _failures)
	GameState.delete_save()
	# 恢复默认难度并落盘，避免污染其他测试进程的 profile
	GameState.set_difficulty(&"medium")
	get_tree().quit(_failures)
