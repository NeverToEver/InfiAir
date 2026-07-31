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
		start_panel.press_new_game()
	# 玩家已改全自动开火：测试全程禁用，避免误伤敌机/Boss 或触发意外得分里程碑
	get_node("Main/Player").set_auto_fire(false)
	await get_tree().process_frame
	await get_tree().process_frame

	# 1. 里程碑触发 Buff UI（阈值已改曲线，测试用 override 固定 500 保证确定性）
	GameState.set_milestone_override(500)
	GameState.add_score(500)
	await get_tree().process_frame
	var buff_ui: CanvasLayer = get_node("Main/BuffUI")
	_check(buff_ui.visible, "500 分触发 Buff 选择 UI")
	_check(get_tree().paused, "Buff UI 弹出时游戏暂停")
	_check(buff_ui.current_available().size() == 3, "里程碑三选一候选数为 3（池未满）")

	# 2. 选择 buff 后恢复
	var ev := InputEventMouseButton.new()
	ev.pressed = true
	ev.button_index = MOUSE_BUTTON_LEFT
	buff_ui.pick_buff(&"power_shot")
	_check(not buff_ui.visible and not get_tree().paused, "选择 buff 后关闭并恢复")
	_check(GameState.buff_count(&"power_shot") == 1, "buff 计入 GameState")

	# 3. Boss 生成与弹幕
	var spawner: Node = get_node("Main/Spawner")
	spawner.spawn_boss()
	await get_tree().process_frame
	var boss: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss = child
	_check(boss != null, "Boss 已生成")
	_check(get_node("Main/HUD/BossBar").visible, "Boss 血条显示")
	# 无头模式帧不封顶，240 帧远不足 4 秒真实时间，改用真实时间等待
	await get_tree().create_timer(4.0).timeout
	_check(boss.is_in_fight(), "Boss 进入巡航阶段")
	boss.take_damage(9999)
	await get_tree().process_frame
	_check(GameState.boss_kills == 1, "Boss 击毁计数")
	# 进程曲线：1 + 0.5×击杀 + 时间轴每 30s +0.05（去硬顶，2026-07-29 无限段修订）
	var expect_mult := 1.0 + 0.5 + floorf(GameState.run_time / 30.0) * 0.05
	_check(absf(GameState.difficulty_multiplier - expect_mult) < 0.001, "难度乘数按公式更新")
	_check(not get_node("Main/HUD/BossBar").visible, "Boss 血条隐藏")
	# 里程碑曲线下后续阈值远高于当前分数，Buff UI 一般不再弹出；若弹出则关闭以便继续测试
	if buff_ui.visible:
		buff_ui.pick_buff(&"rapid_fire")
	_check(not buff_ui.visible and not get_tree().paused, "里程碑 UI 可重复触发并关闭")
	# 停掉生成器并清场（敌机/敌弹），保证后续断言确定性
	spawner.set_process(false)
	for child in get_node("Main").get_children():
		if child is Enemy or (child is Bullet and not child.is_player_bullet):
			child.queue_free()
	await get_tree().process_frame
	# 后续各段需长时间真实等待，期间弹幕可能命中玩家：测试窗口内先开无敌
	var player: Player = get_node("Main/Player")
	player.set_invincible(999.0)

	# 3.1 新移动模式特征
	# spiral：横向振幅 + 整体下压（机动相位随机，采样窗口取最大偏离）
	var spiral := load("res://scenes/enemy.tscn").instantiate() as Enemy
	spiral.setup(spawner.ENEMY_TYPES[2], &"spiral", 1.0)
	spiral.can_shoot = false
	spiral.position = Vector2(960.0, 200.0)
	get_node("Main").add_child(spiral)
	var max_dev := 0.0
	for i in 8:
		await get_tree().create_timer(0.1).timeout
		max_dev = maxf(max_dev, absf(spiral.position.x - 960.0))
	_check(max_dev > 20.0, "spiral 横向振幅")
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

	# hover：下行 → 到达锚点后停驻机动（不再净下降，直到寿命离场）
	var hov := load("res://scenes/enemy.tscn").instantiate() as Enemy
	hov.setup(spawner.ENEMY_TYPES[2], &"hover", 1.0)
	hov.can_shoot = false
	hov.position = Vector2(960.0, 250.0)
	get_node("Main").add_child(hov)
	var t_hover := 0.0
	while not hov.hovering() and t_hover < 4.0:
		await get_tree().create_timer(0.2).timeout
		t_hover += 0.2
	_check(hov.hovering(), "hover 到达锚点后停驻")
	var hover_y: float = hov.position.y
	await get_tree().create_timer(0.5).timeout
	_check(absf(hov.position.y - hover_y) < 15.0, "hover 停驻期间位置稳定")
	# 停驻期间绕出生槽位水平慢摇摆（相位随机，采样窗口取最大偏离）
	var max_sway := 0.0
	for i in 6:
		await get_tree().create_timer(0.2).timeout
		max_sway = maxf(max_sway, absf(hov.position.x - 960.0))
	_check(max_sway > 10.0, "hover 停驻期间水平摇摆")
	_check(absf(hov.position.y - hov.anchor_y) <= hov.HOVER_BOB_AMP + 1.0, "hover 停驻后不再净下降")
	hov.queue_free()

	# 3.2 Boss 轮换：第 2 只（boss_kills=1）应为游击型
	spawner.spawn_boss()
	await get_tree().process_frame
	var boss2: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss2 = child
	_check(boss2 != null and boss2.boss_type == 2, "Boss 轮换：第 2 只为游击型")
	boss2.take_damage(9999)
	await get_tree().process_frame
	if buff_ui.visible:
		buff_ui.pick_buff(&"rapid_fire")

	# 3.3 Boss-3 母舰型召唤小怪
	spawner.spawn_boss()
	await get_tree().process_frame
	var boss3: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss3 = child
	_check(boss3 != null and boss3.boss_type == 3, "Boss 轮换：第 3 只为母舰型")
	boss3.position.y = boss3.fight_anchor_y()  # 跳过降入（锚线 = view 顶缘 + FIGHT_Y），下一物理帧进入战斗
	await get_tree().create_timer(7.0).timeout  # 首次召唤在 6s
	var minion_found := false
	for child in get_node("Main").get_children():
		if child is Enemy:
			minion_found = true
	_check(minion_found, "母舰型 Boss 召唤小怪")
	boss3.take_damage(9999)
	await get_tree().process_frame
	if buff_ui.visible:
		buff_ui.pick_buff(&"rapid_fire")
	# 清理小怪与弹幕
	for child in get_node("Main").get_children():
		if child is Enemy or (child is Bullet and not child.is_player_bullet):
			child.queue_free()
	await get_tree().process_frame

	# 3.4 狂暴阶段：血量 <30% 触发，序列后射速 ×1.5
	spawner.spawn_boss(1)
	await get_tree().process_frame
	var boss4: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss4 = child
	boss4.position.y = boss4.fight_anchor_y()
	await get_tree().create_timer(0.5).timeout
	boss4.take_damage(int(boss4.max_hp * 0.75))
	await get_tree().process_frame
	_check(boss4.is_enraged(), "Boss 血量 <30% 触发狂暴")
	_check(boss4.base_modulate_color() != Color.WHITE, "狂暴贴图变红")
	# 狂暴完整序列（锁血/冻结玩家/轨道攻击）断言见 boss_enrage_test；
	# 这里中止序列后验证永久射速倍率，并快进 main 子弹时间等 time_scale 恢复
	boss4.abort_enrage_sequence()
	get_node("Main").set_bullet_time(0.05)
	for i in 30:
		await get_tree().create_timer(0.1, true, false, true).timeout
		if is_equal_approx(Engine.time_scale, 1.0):
			break
	# 狂暴射速：计时器流速 ×1.5 → 0.5s 墙钟消耗 0.75s 计时
	boss4.set_fire_timer(1.6)
	await get_tree().create_timer(0.5).timeout
	_check(boss4.fire_timer() < 1.0, "狂暴后射速提升")
	boss4.take_damage(9999)
	await get_tree().process_frame
	if buff_ui.visible:
		buff_ui.pick_buff(&"rapid_fire")
	get_tree().paused = false
	# 清理弹幕
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet:
			child.queue_free()
	await get_tree().process_frame

	# 3.5 新 buff 抽查：穿透弹 / 爆炸弹作用于玩家子弹
	GameState.add_buff(&"piercing")
	GameState.add_buff(&"explosive")
	player.fire(Vector2.DOWN)
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
	player.set_since_damage(0.0  )# 冻结被动回血，避免干扰 HP 断言
	var pos_before := player.position
	player.set_invincible(0.0)
	Input.action_press("dash")
	# 无头模式需等物理帧而非 idle 帧，just_pressed 才可靠到达 _physics_process
	await get_tree().physics_frame
	await get_tree().physics_frame
	Input.action_release("dash")
	_check(player.is_dashing(), "相位冲刺触发")
	player.take_damage()
	_check(GameState.health == health_before, "冲刺期间无敌")
	await get_tree().create_timer(0.4).timeout
	_check(player.position.distance_to(pos_before) > 100.0, "冲刺位移约 200px")
	_check(player.dash_cooldown() > 0.0, "冲刺进入冷却")

	# 3.8 燃料：加速消耗 / 松手回复
	var fuel_before: float = player.fuel_amount()
	Input.action_press("boost")
	Input.action_press("move_up")
	await get_tree().create_timer(0.5).timeout
	Input.action_release("boost")
	Input.action_release("move_up")
	_check(player.fuel_amount() < fuel_before, "加速消耗燃料")
	var fuel_after_boost: float = player.fuel_amount()
	await get_tree().create_timer(0.5).timeout
	_check(player.fuel_amount() > fuel_after_boost, "燃料回复")
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
		buff_ui.pick_buff(&"rapid_fire")
	get_tree().paused = false

	# 3.10 母舰（原作对齐）：蓄力召唤 → 到位自动对接（点吸附）→ 驻留弹匣 → 提前离舰
	# 清理可能残留的敌弹，避免干扰生命断言
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet:
			child.queue_free()
	await get_tree().process_frame
	var main := get_node("Main")
	player.set_invincible(0.0)
	player.set_last_hit_frame(-1)
	player.take_damage(10.0)
	_check(GameState.health == 90.0, "母舰测试前置：受击 -10 HP")
	player.set_fuel(10.0)
	# 长按蓄力：1s 松手取消，不进冷却
	Input.action_press("dock")
	await get_tree().create_timer(1.0).timeout
	_check(main.charging() and main.mothership() == null, "蓄力中未召唤")
	# 蓄力虚影：复用真实母舰场景实例（贴图/尺寸/炮塔一致），仅半透明预告、禁用状态机
	_check(main.charge_ghost().visible, "蓄力中显示母舰虚影")
	_check(
		(main.charge_ghost().get_node("Sprite2D") as Sprite2D).texture.resource_path
			== "res://assets/sprites/mothership.png",
		"虚影贴图 = 真实母舰贴图"
	)
	_check(main.charge_ghost().has_node("TurretL") and main.charge_ghost().has_node("TurretR"), "虚影含双炮塔")
	_check(not main.charge_ghost().is_physics_processing(), "虚影禁用状态机（不移动/不对接）")
	_check(main.charge_ghost().modulate.a < 0.5, "虚影半透明调制")
	Input.action_release("dock")
	await get_tree().create_timer(0.2).timeout
	_check(not main.charging() and main.dock_cooldown() <= 0.0, "松手取消蓄力不进冷却")
	# 蓄满 3s 召唤：弹出机库小窗 → 小窗结束后穿梭门+母舰穿出
	Input.action_press("dock")
	await get_tree().create_timer(3.3).timeout
	Input.action_release("dock")
	_check(main.summon_window() != null, "蓄力满 3s 弹出机库小窗")
	main.summon_window().skip()
	await get_tree().process_frame
	_check(main.mothership() != null, "小窗结束后召唤母舰")
	var ms: Mothership = main.mothership()
	ms.set_state_timer(ms.WARP_IN_TIME  )# 快进穿梭入场（0.8s）
	# 到位即自动对接（无区域判定，点吸附补间）
	var tgt := load("res://scenes/enemy.tscn").instantiate() as Enemy
	tgt.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	tgt.can_shoot = false
	tgt.hp = 9999  # 靶机不死，保证场内始终有目标
	tgt.position = Vector2(960.0, 500.0)
	main.add_child(tgt)
	await get_tree().create_timer(0.5).timeout
	_check(ms.state() == Mothership.State.DOCKING, "穿梭入场到位后自动对接")
	_check(tgt.summon_slow_timer() > 0.0, "减速带命中敌机（短时减速）")
	_check(player.is_input_locked(), "对接开始即锁输入")
	_check(player.invincible_remaining() > 100.0, "对接开始即无敌（无敌窗口前移）")
	# 回收牵引期火力掩护（DOCKING 态即开火，不耗驻留弹匣）
	var dock_fire := false
	for child in main.get_children():
		if child is Bullet and child.is_player_bullet and child.score_scale < 1.0:
			dock_fire = true
	_check(dock_fire, "回收牵引期火力掩护开火")
	# 对接 1.5s + 补给 0.5s 后进入驻留
	await get_tree().create_timer(2.0).timeout
	_check(GameState.health == GameState.max_health(), "补给回满生命")
	_check(player.fuel_amount() == player.fuel_max, "补给回满燃料")
	_check(ms.state() == Mothership.State.STAY, "进入驻留状态")
	_check(ms.mag_cells() == 10, "弹匣初始 10 格")
	_check(not player.visible, "回收完成玩家进入保护舱（隐藏）")
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
		player.global_position.distance_to(ms.global_position + Vector2(0.0, ms.DOCK_OFFSET_Y)) < 5.0,
		"驾驶时玩家钉在对接点"
	)
	# 驾驶边界钳制：持续左行被钳在视野内（x ≥ 视图左缘 + DRIVE_MARGIN_X）
	# small 档（zoom=1.0）视野最宽，1045→43 约 1000px @180px/s 需 ~5.6s，留足余量
	Input.action_press("move_left")
	await get_tree().create_timer(6.5).timeout
	Input.action_release("move_left")
	_check(
		absf(ms.position.x - (GameState.view_world_rect().position.x + ms.DRIVE_MARGIN_X)) < 30.0,
		"母舰驾驶边界钳制"
	)
	if buff_ui.visible:
		buff_ui.pick_buff(&"rapid_fire")
	get_tree().paused = false
	# 弹匣随时间消耗（驻留已累计 >2s）
	_check(ms.mag_cells() < 10, "驻留弹匣消耗")
	# ≤4 格警告 + 警告 5s 后强制离舰计时
	ms.set_mag_cells(5)
	ms.set_mag_cell_timer(0.0)
	await get_tree().create_timer(2.3).timeout
	_check(ms.mag_cells() == 4, "弹匣消耗到 4 格")
	_check(ms.mag_warned(), "弹匣 ≤4 弹出警告")
	_check(ms.warn_eject_timer() > 0.0, "警告后启动强制离舰计时")
	# 提前离舰：长按 H 2s，冷却双机制折扣（4→3 格 r=0.3：60×0.88×0.85≈44.9）
	Input.action_press("dock")
	await get_tree().create_timer(1.0).timeout
	_check(main.hud().early_leave_box().visible, "提前离舰蓄力进度条显示")
	_check(
		main.hud().early_leave_fill().anchor_right > 0.3
		and main.hud().early_leave_fill().anchor_right < 0.7,
		"提前离舰进度条进度 ~50%"
	)
	await get_tree().create_timer(1.4).timeout
	Input.action_release("dock")
	_check(ms.state() >= Mothership.State.RELEASE, "提前离舰触发")
	_check(not main.hud().early_leave_box().visible, "提前离舰后进度条隐藏")
	await get_tree().create_timer(0.6).timeout
	_check(
		player.invincible_remaining() > 1.0 and player.invincible_remaining() <= 2.0,
		"释放后 2s 保护（重制版 QoL）"
	)
	await get_tree().create_timer(0.2).timeout
	_check(main.dock_cooldown() > 42.5 and main.dock_cooldown() < 45.2, "提前离舰冷却双机制折扣")
	_check(not player.is_input_locked(), "脱离后输入解锁")
	_check(player.visible, "释放后玩家出舱恢复显示")
	if main.mothership() != null:
		main.mothership().queue_free()
	# 母舰击杀 1/3 分（100 分敌机 → +33）
	# 组判定清场：FormationCraft/TurretBattery 非 Enemy 子类但注册 enemy 组，
	# 漏清会被 b33 抢先命中造成抖动；在飞流弹一并清掉
	for child in main.get_children():
		if (child.is_in_group("enemy") and not (child is Boss)) or child is Bullet:
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
		buff_ui.pick_buff(&"rapid_fire")
	get_tree().paused = false
	# 警告横幅播完（5s）强制离舰：第二艘母舰，缩短计时确定性验证
	main.set_dock_cooldown(0.0)
	main.summon_mothership()
	main.summon_window().skip()
	await get_tree().process_frame
	var ms2: Mothership = main.mothership()
	ms2.set_state_timer(ms2.WARP_IN_TIME  )# 快进穿梭入场
	await get_tree().create_timer(2.5).timeout  # 自动对接 + 补给 → 驻留
	_check(ms2.state() == Mothership.State.STAY, "第二艘母舰进入驻留")
	ms2.set_mag_cells(5)
	ms2.set_mag_cell_timer(0.0)
	await get_tree().create_timer(2.3).timeout
	_check(ms2.mag_warned(), "第二艘母舰弹匣警告")
	ms2.set_warn_eject_timer(0.5  )# 缩短横幅等待，直接验证强制离舰
	await get_tree().create_timer(1.0).timeout
	_check(ms2.state() >= Mothership.State.RELEASE, "警告播完强制离舰（对齐原作）")
	if main.mothership() != null:
		main.mothership().queue_free()

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
	_check(not main.is_homecoming() and not get_tree().paused, "返航蓄力松手取消")
	# 蓄满 1.5s 触发
	Input.action_press("homecoming")
	await get_tree().create_timer(1.7).timeout
	Input.action_release("homecoming")
	await get_tree().process_frame
	_check(main.is_homecoming(), "返航触发")
	_check(main.return_cinematic() != null and get_tree().paused, "返航过场播放中（树暂停）")
	# 过场本体由 return_cinematic_test 专测；越过 1.2s 输入宽限后跳过直达基地 UI
	await get_tree().create_timer(1.4).timeout
	main.skip_return()
	await get_tree().process_frame
	await get_tree().process_frame
	_check(main.base_ui().visible and get_tree().paused, "进入基地整备界面")
	# 维修扣 RP 回满（对齐原作 2RP 回满）
	var rp_before := GameState.rp
	main.base_ui().repair()
	_check(GameState.rp == rp_before - 2, "维修扣 2RP")
	_check(GameState.health == GameState.max_health(), "维修回满生命")
	# 放一个敌机 + 一枚编队炸弹（长引信不爆）验证轨道打击清场
	var orbit_e := load("res://scenes/enemy.tscn").instantiate() as Enemy
	orbit_e.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	orbit_e.can_shoot = false
	orbit_e.position = Vector2(400.0, 300.0)
	main.add_child(orbit_e)
	var bomb := FormationBomb.new()
	bomb.setup(Vector2(0.0, 300.0), 30.0, 20, 120.0)
	bomb.position = Vector2(700.0, 300.0)
	main.add_child(bomb)
	# 继续出击 → 轨道打击动画清场后返回同一局
	main.base_ui().resume()
	await get_tree().process_frame
	# 动画本体由 orbital_strike_test 专测；此处缩短时轴，等待命中清场并播完
	if main.strike() != null:
		main.strike().DURATION = 0.5
	var t_strike := 0.0
	while main.strike() != null and t_strike < 3.0:
		await get_tree().create_timer(0.1).timeout
		t_strike += 0.1
	_check(not get_tree().paused and not main.is_homecoming(), "继续出击恢复游戏")
	_check(GameState.score == score_before_hc, "返回同一局：分数保留")
	_check(GameState.buff_count(&"power_shot") == power_before, "返回同一局：buff 保留")
	_check(GameState.has_save(), "返航后存档保留")
	# 注册表驱动清场：非 Boss 实体（Enemy/FormationCraft/事件残留）全清
	var enemy_left := false
	for e in GameState.enemies:
		if is_instance_valid(e) and not (e is Boss):
			enemy_left = true
	_check(not enemy_left, "轨道打击清屏（注册表非 Boss 全清）")
	# 弹丸清场：敌弹与编队炸弹全清（FormationBomb 非 Bullet 类，原遍历式清场会漏）
	var bullet_left := false
	for child in main.get_children():
		if (child is Bullet and not child.is_player_bullet) or child is FormationBomb:
			bullet_left = true
	_check(not bullet_left, "轨道打击清弹（含编队炸弹）")
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
	_check(player.boost_toggle_active(), "toggle 模式按一下开启加速")
	Input.action_press("boost")
	await get_tree().physics_frame
	Input.action_release("boost")
	await get_tree().physics_frame
	_check(not player.boost_toggle_active(), "toggle 模式再按一下关闭加速")
	GameState.set_shift_toggle_mode(false)
	GameState.set_ctrl_toggle_mode(true)
	Input.action_press("fine_move")
	await get_tree().physics_frame
	Input.action_release("fine_move")
	await get_tree().physics_frame
	_check(player.fine_toggle_active(), "toggle 模式按一下开启微调")
	GameState.set_ctrl_toggle_mode(false)
	player.set_fine_toggle(false)

	# 4. 玩家受击至死 → 结算（此时存档存在，死亡应删档）
	player.set_invincible(0.0)
	player.set_last_hit_frame(-1)
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
	pause_ui.open_settings()
	_check(not pause_ui.visible and settings_ui.visible, "暂停→设置：暂停面板让位")
	_check(settings_ui.opener() == pause_ui, "暂停→设置：opener 记录为暂停面板")
	settings_ui.back()
	_check(pause_ui.visible and not settings_ui.visible, "设置返回：恢复暂停面板")
	pause_ui.toggle()
	_check(not pause_ui.visible, "设置回归：暂停面板已关闭")
	start_panel5.show_panel()
	start_panel5.press_settings()
	_check(not start_panel5.visible and settings_ui.visible, "开始→设置：开始面板让位不遮挡")
	get_node("Main/BackNavigator").go_back()  # Esc 全局路由已移交 BackNavigator
	_check(start_panel5.visible and not settings_ui.visible, "设置中 Esc：返回开始面板")
	_check(not pause_ui.visible, "设置中 Esc：未误弹暂停菜单")
	start_panel5.dismiss()

	# 6. 迭代 3.3 玩家侧：瞄准辅助 / 冲刺耗燃料 / Ctrl 微调
	# 第 4 节玩家已受击至死：复活以便继续测试（不重开 hitbox，避免杂散碰撞）
	player.set_dead(false)
	player.set_invincible(999.0)
	player.show()
	player.set_physics_process(true)
	GameState.health = GameState.max_health()
	get_node("Main/GameOverUI").hide()
	get_tree().paused = false

	# 6.1 辅助瞄准（P1-1 新语义）：标记敌 + 准星入框 → 出膛弹追踪该敌；未入框 → 朝准星直射
	# （瞄准点用 aim_point_override 注入：相机震动 offset 会让合成鼠标事件的世界落点漂移）
	player.position = Vector2(960.0, 800.0)
	player.velocity = Vector2.ZERO
	var aim_e := load("res://scenes/enemy.tscn").instantiate() as Enemy
	aim_e.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	aim_e.can_shoot = false
	aim_e.hp = 9999  # 防止被测试弹击毁触发里程碑
	aim_e.aim_marked = true  # 出生标记为 40% 随机掷点，测试强制置位保证确定性
	aim_e.position = player.position + Vector2(0.0, -300.0)
	main.add_child(aim_e)
	await get_tree().process_frame
	var frames := GameState.aim_frame_layer
	_check(frames != null, "辅助框覆盖层已登记 GameState")
	# 准星置入标记敌框内（框心偏移 20px，仍在 碰撞半径+frame_pad 内）
	player.aim_point_override = aim_e.global_position + Vector2(20.0, 0.0)
	await get_tree().physics_frame
	_check(frames.marked_target_at(player.aim_point()) == aim_e, "准星入框命中标记敌")
	player.set_auto_fire(true)
	player.reset_fire_cooldown()
	await get_tree().physics_frame
	await get_tree().physics_frame
	player.set_auto_fire(false)
	var ab: Bullet = null
	for child in main.get_children():
		if child is Bullet and child.is_player_bullet:
			ab = child
			break
	_check(ab != null, "入框期间自动开火")
	if ab != null:
		_check(ab.homing_target == aim_e, "入框出膛弹绑定追踪目标")
		var dir0: Vector2 = ab.direction
		await get_tree().physics_frame
		await get_tree().physics_frame
		await get_tree().physics_frame
		await get_tree().physics_frame
		# 直线弹方向恒定；方向发生偏转即追踪转向生效（lerp_angle 恒朝目标）
		_check(absf(angle_difference(ab.direction.angle(), dir0.angle())) > 0.005, "追踪弹出膛后向目标转向")
		ab.queue_free()
	# 准星不在任何标记框内 → 朝准星直射，无追踪绑定
	player.aim_point_override = Vector2(200.0, 950.0)
	await get_tree().physics_frame
	_check(frames.marked_target_at(player.aim_point()) == null, "准星出框无命中目标")
	player.set_auto_fire(true)
	player.reset_fire_cooldown()
	await get_tree().physics_frame
	await get_tree().physics_frame
	player.set_auto_fire(false)
	var ab2: Bullet = null
	for child in main.get_children():
		if child is Bullet and child.is_player_bullet:
			ab2 = child
			break
	_check(ab2 != null and ab2.homing_target == null, "未入框出膛弹无追踪目标")
	if ab2 != null:
		var want2: Vector2 = (player.aim_point() - player.global_position).normalized()
		_check(ab2.direction.dot(want2) > 0.99, "未入框子弹朝准星直射")
		ab2.queue_free()
	player.aim_point_override = Vector2.INF
	aim_e.queue_free()
	await get_tree().process_frame

	# 6.1b 辅助瞄准强度三档：框内边距/追踪速率/入框吸附系数随档位切换，无关闭档（非法档位拒绝）
	var default_pad: float = frames.frame_pad()
	var default_turn: float = player.aim_assist_params()["homing_turn_rate"]
	var default_stick: float = player.aim_assist_params()["stick_factor"]
	GameState.set_aim_assist_level(&"low")
	_check(
		frames.frame_pad() < default_pad and player.aim_assist_params()["homing_turn_rate"] < default_turn and player.aim_assist_params()["stick_factor"] > default_stick,
		"弱档：框内边距与追踪速率降低、吸附减弱"
	)
	GameState.set_aim_assist_level(&"high")
	_check(
		frames.frame_pad() > default_pad and player.aim_assist_params()["homing_turn_rate"] > default_turn and player.aim_assist_params()["stick_factor"] < default_stick,
		"强档：框内边距与追踪速率提高、吸附增强"
	)
	GameState.set_aim_assist_level(&"off")
	_check(GameState.aim_assist_level == &"high", "辅助瞄准无关闭档（非法档位被拒绝）")
	GameState.set_aim_assist_level(&"medium")
	_check(
		is_equal_approx(frames.frame_pad(), default_pad) and is_equal_approx(player.aim_assist_params()["homing_turn_rate"], default_turn)
			and is_equal_approx(player.aim_assist_params()["stick_factor"], default_stick),
		"恢复中档后参数还原"
	)

	# 6.1c 辅助瞄准算法优化（P1-3）：准星磁吸 / 框外锥形弱追踪 / 输入反比 / 距离衰减
	var aim_e2 := load("res://scenes/enemy.tscn").instantiate() as Enemy
	aim_e2.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	aim_e2.can_shoot = false
	aim_e2.hp = 9999
	aim_e2.aim_marked = true
	aim_e2.position = player.position + Vector2(0.0, -300.0)
	main.add_child(aim_e2)
	await get_tree().process_frame
	# 档位梯度：磁吸/锥形参数 low < high，恢复 medium
	GameState.set_aim_assist_level(&"low")
	var pa_low: Dictionary = player.aim_assist_params()
	GameState.set_aim_assist_level(&"high")
	var pa_high: Dictionary = player.aim_assist_params()
	_check(
		pa_low["magnet_range"] < pa_high["magnet_range"]
			and pa_low["magnet_strength"] < pa_high["magnet_strength"]
			and pa_low["cone_angle_deg"] < pa_high["cone_angle_deg"]
			and pa_low["cone_strength"] < pa_high["cone_strength"],
		"弱/强档：磁吸与锥形参数梯度"
	)
	GameState.set_aim_assist_level(&"medium")
	await get_tree().process_frame
	# 磁吸 API（纯函数，合成输入增量；目标在玩家上方 300 < falloff.peak，衰减不干扰）
	var c2: Vector2 = aim_e2.global_position
	var half2: float = frames.frame_half_size(aim_e2)
	_check(frames.magnet_pull(c2 + Vector2(half2 + 40.0, 0.0), Vector2.ZERO) == Vector2.ZERO, "静止输入无磁吸")
	_check(frames.magnet_pull(c2 + Vector2(half2 + 40.0, 0.0), Vector2(1.0, 0.0)) == Vector2.ZERO, "微动低于阈值无磁吸")
	_check(frames.magnet_pull(c2 + Vector2(half2 + 40.0, 0.0), Vector2(60.0, 0.0)) == Vector2.ZERO, "高速输入无磁吸（输入优先）")
	_check(frames.magnet_pull(c2 + Vector2(10.0, 0.0), Vector2(6.0, 0.0)) == Vector2.ZERO, "框内点不触发磁吸（归 stickiness）")
	var pull: Vector2 = frames.magnet_pull(c2 + Vector2(half2 + 40.0, 0.0), Vector2(6.0, 0.0))
	var lim: float = player.aim_assist_params()["magnet_max_speed"]
	_check(pull.length() > 0.5 and pull.length() <= lim, "慢速输入磁吸量级在 (0, 拉速上限]")
	_check(pull.normalized().dot(Vector2.LEFT) > 0.99, "磁吸方向指向框外目标（目标在左侧）")
	var pull_near: Vector2 = frames.magnet_pull(c2 + Vector2(half2 + 10.0, 0.0), Vector2(6.0, 0.0))
	var pull_far: Vector2 = frames.magnet_pull(c2 + Vector2(half2 + 90.0, 0.0), Vector2(6.0, 0.0))
	_check(pull_near.length() > pull_far.length(), "磁吸随框沿距线性衰减")
	# 距离衰减：目标移至玩家上方 1200（falloff≈0.44），同输入与框沿距拉量显著下降
	aim_e2.position = player.position + Vector2(0.0, -1200.0)
	await get_tree().process_frame
	var pull_far_d: Vector2 = frames.magnet_pull(aim_e2.global_position + Vector2(half2 + 40.0, 0.0), Vector2(6.0, 0.0))
	_check(pull_far_d.length() < pull.length() * 0.6, "磁吸随玩家-目标距离衰减")
	_check(
		player.aim_dist_falloff(300.0) == 1.0
			and player.aim_dist_falloff(900.0) < 1.0
			and player.aim_dist_falloff(900.0) > player.aim_dist_falloff(1300.0)
			and player.aim_dist_falloff(1500.0) == player.aim_assist_params()["falloff_min"],
		"距离衰减曲线单调（400 平台 / 1400 下限）"
	)
	# 框外锥形弱追踪：目标复位玩家上方 400（falloff=1.0），准星置框沿外测角距（中档锥角 6°）
	aim_e2.position = player.position + Vector2(0.0, -400.0)
	await get_tree().process_frame
	var full_rate: float = player.aim_assist_params()["homing_turn_rate"]
	# 框沿外 2px（角距 ~4.5° < 6°）→ 弱绑定
	player.aim_point_override = aim_e2.global_position + Vector2(half2 + 2.0, 0.0)
	var wdir: Vector2 = (player.aim_point() - player.global_position).normalized()
	player.reset_fire_cooldown()
	player.fire(wdir)
	var wb: Bullet = null
	for child in main.get_children():
		if child is Bullet and child.is_player_bullet:
			wb = child
			break
	_check(wb != null and wb.homing_target == aim_e2, "框外锥内弱追踪绑定目标")
	var wb_rate: float = wb.homing_turn_rate if wb != null else 0.0
	if wb != null:
		_check(wb_rate > 0.1 and wb_rate < full_rate * 0.75, "弱追踪转向率介于 (0, 全追踪) 之间")
		wb.queue_free()
	await get_tree().process_frame  # queue_free 帧末才删除：等旧弹离场再扫下一发
	# 框沿外 8px（角距 ~5.4°）→ 转向率更低（角距渐变）
	player.aim_point_override = aim_e2.global_position + Vector2(half2 + 8.0, 0.0)
	var wdir2: Vector2 = (player.aim_point() - player.global_position).normalized()
	player.reset_fire_cooldown()
	player.fire(wdir2)
	var wb2: Bullet = null
	for child in main.get_children():
		if child is Bullet and child.is_player_bullet:
			wb2 = child
			break
	if wb2 != null:
		_check(wb2.homing_turn_rate < wb_rate, "锥内转向率随角距渐变（框缘更低）")
		wb2.queue_free()
	await get_tree().process_frame  # 同上：等 wb2 离场
	# 框沿外 30px（角距 ~8.5° > 6°）→ 直射无追踪
	player.aim_point_override = aim_e2.global_position + Vector2(half2 + 30.0, 0.0)
	var wdir3: Vector2 = (player.aim_point() - player.global_position).normalized()
	player.reset_fire_cooldown()
	player.fire(wdir3)
	var wb3: Bullet = null
	for child in main.get_children():
		if child is Bullet and child.is_player_bullet:
			wb3 = child
			break
	_check(wb3 != null and wb3.homing_target == null, "锥外出膛弹无追踪（直射）")
	if wb3 != null:
		wb3.queue_free()
	player.aim_point_override = Vector2.INF
	aim_e2.queue_free()
	await get_tree().process_frame

	# 6.2 冲刺耗燃料：消耗满值的 25%，不足时禁用
	player.position = Vector2(960.0, 540.0)
	player.set_fuel(player.fuel_max)
	player.set_dash_cooldown(0.0)
	Input.action_press("dash")
	await get_tree().physics_frame
	await get_tree().physics_frame
	Input.action_release("dash")
	_check(player.is_dashing(), "燃料充足时冲刺可触发")
	_check(absf(player.fuel_amount() - player.fuel_max * 0.75) < 3.0, "冲刺消耗约 25% 燃料")
	await get_tree().create_timer(0.4).timeout  # 等冲刺结束
	player.set_fuel(player.fuel_max * 0.2)
	player.set_dash_cooldown(0.0)
	Input.action_press("dash")
	await get_tree().physics_frame
	await get_tree().physics_frame
	Input.action_release("dash")
	_check(not player.is_dashing(), "燃料不足 25% 时禁用冲刺")

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
