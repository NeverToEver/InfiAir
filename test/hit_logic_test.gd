extends Node
## 受击/碰撞对齐测试（迭代 3.8，PORTING_PARITY 附录 A）：
## A1 玩家受击小判定点 r=7；A2 Boss 身体撞击（入场降入期跳过，Boss 不掉血）；
## A3 狂暴锁血（非致死钳到 30% 阈值，致死直接击杀）；A4 敌弹按自身 damage 结算。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 直接实例化 Boss（不经 spawner/main，隔离狂暴子弹时间编排与血条联动）
func _make_boss(p_type: int = 1) -> Boss:
	var boss := (load("res://scenes/boss.tscn") as PackedScene).instantiate() as Boss
	boss.setup(1.0, p_type)
	get_node("Main").add_child(boss)
	return boss


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
	await get_tree().process_frame


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	GameState.high_score = 0
	GameState.save_profile()
	add_child((load("res://scenes/main.tscn") as PackedScene).instantiate())
	var main := get_node("Main")
	var player: Player = get_node("Main/Player")
	player._auto_fire_enabled = false  # 禁用自动开火，避免误伤与意外得分里程碑
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性
	for child in main.get_children():
		if child is Enemy or child is Bullet:
			child.queue_free()
	await get_tree().process_frame
	player.position = Vector2(960.0, 800.0)

	# ================= A1：玩家受击小判定点 =================
	var hb := (player.get_node("Hitbox/CollisionShape2D") as CollisionShape2D).shape as CircleShape2D
	_check(hb != null and is_equal_approx(hb.radius, 7.0), "A1：玩家受击判定点半径 r=7")

	# ================= A2：Boss 身体撞击 =================
	# 入场降入期：与玩家重叠也不扣命（Boss 尚未降入战斗位置）
	GameState.lives = 3.0
	player._invincible = 0.0
	player.position = Vector2(960.0, 150.0)  # FIGHT_Y(230) 之上
	var boss_enter := _make_boss(1)
	boss_enter.position = player.position  # 重叠，但仍在降入阶段
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(not boss_enter._in_fight, "A2：Boss 仍处于入场降入阶段")
	_check(GameState.lives == 3.0, "A2：入场降入期撞击不扣命")
	boss_enter.queue_free()
	await get_tree().physics_frame

	# 进入战斗：撞击玩家 -1 命，Boss 不掉血不死
	player.position = Vector2(960.0, 800.0)
	player._invincible = 0.0
	GameState.lives = 3.0
	var boss_fight := _make_boss(1)
	boss_fight._in_fight = true  # 直接置战斗态（重叠事件由传送产生，避开降入时序）
	boss_fight._fire_timer = 999.0  # 屏蔽开火，保证场内无杂弹
	boss_fight.position = player.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(GameState.lives == 2.0, "A2：撞 Boss 身体玩家 -1 命")
	_check(
		is_instance_valid(boss_fight) and boss_fight.hp == boss_fight.max_hp,
		"A2：撞击后 Boss 不掉血不死"
	)
	boss_fight.queue_free()
	await get_tree().physics_frame

	# ================= A3：狂暴锁血 =================
	var boss3 := _make_boss(1)
	boss3._fire_timer = 999.0
	# 非致死大额伤害：应钳到 30% 阈值并触发狂暴（而非打到阈值以下）
	boss3.take_damage(int(boss3.max_hp * 0.75))
	await get_tree().process_frame
	_check(
		is_equal_approx(boss3.hp, boss3.max_hp * boss3.ENRAGE_HP_RATIO),
		"A3：非致死伤害钳到 30% 阈值"
	)
	_check(boss3._enraged, "A3：钳到阈值触发狂暴")
	# 狂暴后不再钳制：小额伤害正常扣血
	boss3.take_damage(1)
	_check(boss3.hp < boss3.max_hp * boss3.ENRAGE_HP_RATIO, "A3：狂暴触发后不再锁血")
	boss3.queue_free()
	await get_tree().process_frame
	# 致死伤害：满血一击直接击杀（不触发狂暴钳制）
	var boss4 := _make_boss(1)
	boss4.take_damage(9999)
	await get_tree().process_frame
	_check(not is_instance_valid(boss4), "A3：致死伤害直接击杀")

	# ================= A4：敌弹 damage 生效 =================
	player.position = Vector2(960.0, 800.0)
	# laser 弹：配置 damage=2
	var laser_e := (load("res://scenes/enemy.tscn") as PackedScene).instantiate() as Enemy
	laser_e.setup(spawner.ELITE_TYPES[1], &"straight", 1.0)
	laser_e.bullet_type = &"laser"
	laser_e.can_shoot = false
	laser_e.position = Vector2(960.0, 300.0)
	main.add_child(laser_e)
	laser_e._fire_at_player()
	var laser_b: Bullet = null
	for b in _enemy_bullets():
		if b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"laser":
			laser_b = b
	_check(laser_b != null and laser_b.damage == 2, "A4：laser 敌弹 damage=2")
	# 命中玩家扣 2 命（传送重叠走真实碰撞管线）
	GameState.lives = 3.0
	player._invincible = 0.0
	if laser_b != null:
		laser_b.speed = 0.0
		laser_b.position = player.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(GameState.lives == 1.0, "A4：laser 敌弹命中 -2 命")
	laser_e.queue_free()
	await _free_enemy_bullets()

	# single 弹：配置 damage=1
	var single_e := (load("res://scenes/enemy.tscn") as PackedScene).instantiate() as Enemy
	single_e.setup(spawner.ENEMY_TYPES[0], &"straight", 1.0)
	single_e.bullet_type = &"single"
	single_e.can_shoot = false
	single_e.position = Vector2(960.0, 300.0)
	main.add_child(single_e)
	single_e._fire_at_player()
	var single_b: Bullet = null
	for b in _enemy_bullets():
		if b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"single":
			single_b = b
	_check(single_b != null and single_b.damage == 1, "A4：single 敌弹 damage=1")
	GameState.lives = 3.0
	player._invincible = 0.0
	if single_b != null:
		single_b.speed = 0.0
		single_b.position = player.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(GameState.lives == 2.0, "A4：single 敌弹命中 -1 命")
	single_e.queue_free()
	await _free_enemy_bullets()

	# Boss 弹种取值：狙击=1，快照激光=2，快照环弹=1
	var boss5 := _make_boss(1)
	boss5.position = Vector2(960.0, 300.0)
	boss5._fire_timer = 999.0  # 屏蔽常规开火，保证弹丸计数纯净
	boss5._fire_sniper()
	var sniper_b: Bullet = null
	for b in _enemy_bullets():
		if not b.has_meta("bullet_type"):
			sniper_b = b
	_check(sniper_b != null and sniper_b.damage == 1, "A4：Boss 狙击弹 damage=1")
	await _free_enemy_bullets()
	boss5.fire_enrage_snapshot()
	var snap_laser_dmg_ok := true
	var snap_ring_dmg_ok := true
	var snap_lasers := 0
	var snap_rings := 0
	for b in _enemy_bullets():
		if b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"laser":
			snap_lasers += 1
			if b.damage != 2:
				snap_laser_dmg_ok = false
		elif b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"enrage_ring":
			snap_rings += 1
			if b.damage != 1:
				snap_ring_dmg_ok = false
	_check(
		snap_lasers == boss5.ENRAGE_SNAPSHOT_LASERS and snap_laser_dmg_ok,
		"A4：Boss 狂暴快照激光 damage=2"
	)
	_check(
		snap_rings == boss5.ENRAGE_SNAPSHOT_RING and snap_ring_dmg_ok,
		"A4：Boss 狂暴快照环弹 damage=1"
	)
	boss5.queue_free()
	await _free_enemy_bullets()
	player._invincible = 999.0

	print("HIT LOGIC TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
