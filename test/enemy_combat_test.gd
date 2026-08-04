extends Node
## 敌机/Boss 战斗行为测试：弹种（single/spread/laser）与机型约束、spread 同屏上限、
## aggressive 追踪收敛、敌机 15s 寿命离场、Boss 50s 逃跑无奖励。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 生成测试敌机（默认停火，位置/弹种由调用方覆盖）
func _spawn_test_enemy(config: Dictionary, strategy: StringName) -> Enemy:
	var e := (load("res://scenes/enemy.tscn") as PackedScene).instantiate() as Enemy
	e.setup(config, strategy, 1.0)
	e.can_shoot = false
	get_node("Main").add_child(e)
	return e


## 当前场内敌弹（玩家弹排除）
func _enemy_bullets() -> Array[Bullet]:
	var out: Array[Bullet] = []
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet:
			out.append(child)
	return out


func _free_enemy_bullets() -> void:
	for b in _enemy_bullets():
		b.queue_free()


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.save_profile()
	var main_scene: PackedScene = load("res://scenes/main.tscn")
	GameState.login_guest()  # T4：游客会话直接开局（StartPanel 已退役）
	add_child(main_scene.instantiate())
	var main := get_node("Main")
	# 开场面板自显即暂停（冻结背景），先关闭解除
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false)  # 禁用自动开火，避免误伤与意外得分里程碑
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性
	player.set_invincible(999.0)  # 弹幕流弹不干扰流程
	player.position = Vector2(960.0, 800.0)

	# 1. 弹种配置表约束：普通机仅 single/spread，精英仅 spread/laser
	var normal_pool_ok := true
	for t in spawner.ENEMY_TYPES:
		for bt in t["bullet_types"]:
			if bt != &"single" and bt != &"spread":
				normal_pool_ok = false
	_check(normal_pool_ok, "普通机型弹种池仅 single/spread")
	var elite_pool_ok := true
	for t in spawner.ELITE_TYPES:
		for bt in t["bullet_types"]:
			if bt != &"spread" and bt != &"laser":
				elite_pool_ok = false
	_check(elite_pool_ok, "精英机型弹种池仅 spread/laser")

	# 2. spread 敌机发射五向扇形弹
	var spread_e := _spawn_test_enemy(spawner.ENEMY_TYPES[0], &"straight")
	spread_e.bullet_type = &"spread"
	spread_e.can_shoot = true
	spread_e.fire_interval = 5.0  # 只放一轮
	spread_e.set_fire_timer(0.1)
	spread_e.position = Vector2(960.0, 300.0)
	await get_tree().create_timer(0.5).timeout
	var fan: Array[Bullet] = []
	for b in _enemy_bullets():
		if b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"spread":
			fan.append(b)
	_check(fan.size() == 5, "spread 敌机一次发射 5 向弹")
	if fan.size() == 5:
		var angles: Array[float] = []
		for b in fan:
			angles.append(b.direction.angle())
		angles.sort()
		var fan_ok := true
		for i in 4:
			if absf(angles[i + 1] - angles[i] - spread_e.SPREAD_FAN_STEP) > 0.02:
				fan_ok = false
		_check(fan_ok, "spread 弹向以瞄准方向为中心均匀扇形展开")
		_check(
			is_equal_approx(fan[0].speed, spread_e.SPREAD_BULLET_SPEED) and fan[0].speed < spread_e.ENEMY_BULLET_SPEED, "spread 弹速稍慢于普通弹"
		)
	spread_e.queue_free()
	_free_enemy_bullets()

	# 3. laser 弹：细长高亮快速弹
	var laser_e := _spawn_test_enemy(spawner.ELITE_TYPES[1], &"straight")
	laser_e.bullet_type = &"laser"
	laser_e.can_shoot = true
	laser_e.fire_interval = 5.0
	laser_e.set_fire_timer(0.1)
	laser_e.position = Vector2(960.0, 300.0)
	await get_tree().create_timer(0.4).timeout
	var lasers: Array[Bullet] = []
	for b in _enemy_bullets():
		if b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"laser":
			lasers.append(b)
	_check(lasers.size() == 1, "laser 敌机发射单发弹")
	if lasers.size() == 1:
		_check(lasers[0].speed > laser_e.ENEMY_BULLET_SPEED, "laser 弹速显著更快")
		_check((lasers[0].get_node("Polygon2D") as Polygon2D).scale.x > 1.5, "laser 弹细长化表现")
	laser_e.queue_free()
	_free_enemy_bullets()

	# 4. 抽取约束：普通机只出 single/spread，精英只出 spread/laser
	var normal_pick_ok := true
	for t in spawner.ENEMY_TYPES:
		for i in 20:
			var bt: StringName = spawner.pick_bullet_type(t)
			if bt != &"single" and bt != &"spread":
				normal_pick_ok = false
	_check(normal_pick_ok, "普通机抽取弹种仅 single/spread")
	var elite_pick_ok := true
	for t in spawner.ELITE_TYPES:
		for i in 20:
			var bt2: StringName = spawner.pick_bullet_type(t)
			if bt2 != &"spread" and bt2 != &"laser":
				elite_pick_ok = false
	_check(elite_pick_ok, "精英抽取弹种仅 spread/laser")

	# 5. spread 同屏上限 2：超限退化（普通→single，精英→laser）
	var cap1 := _spawn_test_enemy(spawner.ENEMY_TYPES[0], &"straight")
	cap1.bullet_type = &"spread"
	cap1.position = Vector2(500.0, 300.0)
	var cap2 := _spawn_test_enemy(spawner.ENEMY_TYPES[0], &"straight")
	cap2.bullet_type = &"spread"
	cap2.position = Vector2(1400.0, 300.0)
	await get_tree().process_frame
	_check(spawner.count_spread_enemies() == 2, "同屏 spread 敌机计数")
	var cap_normal_ok := true
	for i in 10:
		if spawner.pick_bullet_type(spawner.ENEMY_TYPES[1]) != &"single":
			cap_normal_ok = false
	_check(cap_normal_ok, "spread 同屏上限 2：普通机退化为 single")
	var cap_elite_ok := true
	for i in 10:
		if spawner.pick_bullet_type(spawner.ELITE_TYPES[2]) != &"laser":
			cap_elite_ok = false
	_check(cap_elite_ok, "spread 同屏上限 2：精英退化为 laser")
	cap1.queue_free()
	cap2.queue_free()

	# 6. aggressive：噪声漂移 + 持续偏向玩家 x 的下行
	player.position = Vector2(400.0, 900.0)
	var agg := _spawn_test_enemy(spawner.ENEMY_TYPES[3], &"aggressive")
	agg.position = Vector2(1400.0, 200.0)
	var agg_x0 := agg.position.x
	var agg_y0 := agg.position.y
	await get_tree().create_timer(2.0).timeout
	_check(agg.position.x < agg_x0 - 150.0, "aggressive 持续偏向玩家 x 收敛")
	_check(agg.position.y > agg_y0, "aggressive 保持下行")
	agg.queue_free()

	# 7. 敌机 15s 寿命：到期向上/侧方加速离场，不给分不计击杀
	var score_before_life := GameState.score
	var kills_before_life := GameState.kills
	var life_e := _spawn_test_enemy(spawner.ENEMY_TYPES[0], &"straight")
	life_e.position = Vector2(960.0, 300.0)
	life_e.set_life_timer(14.8)
	await get_tree().create_timer(0.4).timeout
	_check(life_e.is_exiting(), "敌机 15s 寿命到期进入离场")
	var exit_p0 := life_e.position
	await get_tree().create_timer(0.4).timeout
	_check(life_e.position.y < exit_p0.y - 20.0 or absf(life_e.position.x - exit_p0.x) > 20.0, "离场向上或侧方加速")
	await get_tree().create_timer(3.0).timeout
	_check(not is_instance_valid(life_e), "离场后销毁")
	_check(GameState.score == score_before_life and GameState.kills == kills_before_life, "离场不给分不计击杀")

	# 8. Boss 逃跑：50s 未击杀 → 最后 3s 警告 + 上飘 → 离场无奖励
	spawner.spawn_boss(1)
	await get_tree().process_frame
	var boss: Boss = null
	for child in main.get_children():
		if child is Boss:
			boss = child
	_check(boss != null, "Boss 已生成（逃跑测试）")
	boss.position.y = boss.fight_anchor_y()  # 跳过降入（锚线 = view 顶缘 + FIGHT_Y）
	await get_tree().create_timer(0.3).timeout
	_check(boss.is_in_fight(), "Boss 进入战斗（逃跑计时开始）")
	var kills_before_boss := GameState.boss_kills
	var score_before_boss := GameState.score
	# 钉住时间轴难度档：后续约 4s 真实等待不得跨过 30s 量化边界造成偶发漂移
	GameState.run_time = 0.0
	GameState.recompute_difficulty()
	var diff_before := GameState.difficulty_multiplier
	var escaped_flag := [false]
	boss.escaped.connect(func() -> void: escaped_flag[0] = true)
	boss.set_survival(boss.ESCAPE_TIME - boss.ESCAPE_WARNING - 0.5)  # 距警告 0.5s
	var warn_y0 := boss.position.y
	await get_tree().create_timer(0.8).timeout
	_check(boss.escape_warned(), "逃跑前 3s 触发逃跑警告")
	_check(boss.position.y < warn_y0 - 3.0, "警告期间上飘")
	boss.set_survival(boss.ESCAPE_TIME - 0.05)  # 距逃跑 0.05s
	await get_tree().create_timer(0.3).timeout
	_check(boss.is_escaped, "Boss 50s 未被击杀触发逃跑")
	# G02：逃跑期 take_damage 必须无效（激光/溅射按注册表+距离判定绕碰撞层，防补刀致奖励失真）
	var hp_after_escape := boss.hp
	boss.take_damage(1000, 1.0)
	_check(boss.hp == hp_after_escape, "G02：逃跑期 take_damage 无效（防补刀致死触发击杀奖励）")
	await get_tree().create_timer(2.5).timeout
	_check(escaped_flag[0], "Boss 离场发出 escaped 信号")
	_check(not spawner.is_boss_active(), "Boss 逃跑解除波次/事件占用（可再触发）")
	_check(not is_instance_valid(boss), "Boss 离场销毁")
	_check(GameState.boss_kills == kills_before_boss, "逃跑不加 boss_kills（轮换不推进）")
	_check(GameState.score == score_before_boss, "逃跑无 500 分奖励")
	_check(GameState.difficulty_multiplier == diff_before, "逃跑不升难度")
	_check(not get_node("Main/HUD/BossBar").visible, "逃跑后 Boss 血条隐藏")
	# 轮换计数未推进：下一只仍为同型（boss_kills 未变）
	spawner.spawn_boss()
	await get_tree().process_frame
	var boss_next: Boss = null
	for child in main.get_children():
		if child is Boss:
			boss_next = child
	_check(boss_next != null and boss_next.boss_type == 1, "逃跑后轮换计数未推进（仍为同型 Boss）")
	if boss_next != null:
		boss_next.queue_free()
	_free_enemy_bullets()

	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	# 分裂者（2026-08-04）：死亡分裂 2 小机——缩放 ×0.6 / HP 半 / 无分数 / 不再分裂
	var split_e := _spawn_test_enemy(spawner.ENEMY_TYPES[4], &"straight")
	split_e.position = Vector2(960.0, 400.0)
	await get_tree().process_frame
	var score_before := GameState.score
	split_e.take_damage(9999)
	await get_tree().process_frame
	var minis: Array[Enemy] = []
	for e: Enemy in GameState.enemies:
		if is_instance_valid(e) and e != split_e and e.score_value == 0:
			minis.append(e)
	_check(minis.size() == 2, "分裂者死亡生成 2 小机")
	# 相对断言防难度倍率环境污染（不依赖绝对分数）；子机分数 0 由后续"子机死亡不再计分"覆盖
	_check(GameState.score > score_before, "母体正常计分（子机分数 0 不额外计）")
	for m in minis:
		_check((m.get_node("Sprite2D") as Sprite2D).scale.x < 0.5, "子机缩放 ×0.6")
	_check(not minis.is_empty() and minis[0].hp >= 20 and minis[0].hp <= 50, "子机 HP 减半（约 40-46）")
	var score_after_split := GameState.score
	for m in minis:
		m.take_damage(9999)
	await get_tree().process_frame
	_check(GameState.score == score_after_split, "子机死亡不再计分")
	var left := 0
	for e: Enemy in GameState.enemies:
		if is_instance_valid(e):
			left += 1
	_check(left == 0, "子机死亡不再分裂")

	print("ENEMY COMBAT TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
