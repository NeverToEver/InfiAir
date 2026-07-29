extends Node
## 受击/碰撞对齐测试（迭代 3.9，PORTING_PARITY 附录 A）：
## A1 玩家受击小判定点 r=7；A2 Boss 身体撞击 30（入场降入期跳过，Boss 不掉血）；
## A3 狂暴锁血；A4 敌弹按弹种 12/10/20 结算；A5 100 HP 伤害模型（上限/封顶）；
## A6 敌机撞击 20 且不自毁；A7/A8 闪避 20% 与护甲 ×0.85 全伤害源两段式（去 bug 统一版）；
## A9 受击清 250px 敌弹；A10 精英碰撞半径与普通同档；A12 爆炸弹 50px/30 固定/主目标吃溅射；
## A13 慢速力场全局敌机移速 ×0.8（敌弹不受影响）；A16 同帧敌弹只结算第一发且其余保留；
## A20 出生保护 1.0s（对齐原作入场动画等效）；A21 Boss 入场期可被弹伤（已核实与原作一致）。

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


func _make_enemy(config: Dictionary, strategy: StringName = &"straight") -> Enemy:
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
	await get_tree().process_frame


## 重置玩家受击状态（无敌/帧标记/被动回血计时），便于逐条断言
func _reset_hit_state(player: Player) -> void:
	player._invincible = 0.0
	player._last_hit_frame = -1
	player._since_damage = 999.0


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

	# ================= A5：100 HP 伤害模型 =================
	_check(GameState.health == 100.0 and GameState.max_health() == 100.0, "A5：初始 100/100 HP")
	_reset_hit_state(player)
	player.take_damage(10.0)
	_check(GameState.health == 90.0, "A5：受击 -10 HP")
	GameState.heal(999.0)
	_check(GameState.health == 100.0, "A5：heal 封顶 max_health")
	GameState.add_buff(&"extra_life")
	_check(GameState.max_health() == 150.0, "A5：extra_life 每层上限 +50")
	GameState.buffs.clear()
	_check(GameState.max_health() == 100.0, "A5：清 buff 后上限回 100")

	# extra_life 选取：上限 +50 且瞬时 +30 HP（对齐原作 EXTRA_LIFE_HEAL）
	GameState.health = 50.0
	var ev := InputEventMouseButton.new()
	ev.pressed = true
	ev.button_index = MOUSE_BUTTON_LEFT
	get_node("Main/BuffUI")._on_card_gui_input(ev, &"extra_life")
	_check(GameState.max_health() == 150.0 and GameState.health == 80.0, "A5：extra_life 选取 +50 上限 +30 HP")
	GameState.buffs.clear()

	# regen buff：二元 +2 HP/s
	GameState.health = 50.0
	GameState.add_buff(&"regen")
	await get_tree().create_timer(0.6).timeout
	_check(GameState.health > 50.5 and GameState.health < 52.5, "A5：regen buff 每秒回 2 HP")
	GameState.buffs.clear()

	# 被动回血：无 buff 时按难度延迟后回复（本测试环境 medium：4s/2HP）
	GameState.health = 50.0
	player._since_damage = 999.0
	await get_tree().create_timer(0.6).timeout
	_check(GameState.health > 50.5 and GameState.health < 52.5, "A5：被动回血按速率回复")
	player._since_damage = 0.0  # 关闭被动回血，避免干扰后续精确断言

	# lifesteal：击毁回复 10% 上限（每帧至多一次）
	GameState.health = 50.0
	GameState.add_buff(&"lifesteal")
	var ls_e := _make_enemy(spawner.ENEMY_TYPES[0])
	ls_e.hp = 1
	ls_e.position = Vector2(960.0, 400.0)
	ls_e.take_damage(9999)
	await get_tree().process_frame
	_check(GameState.health == 60.0, "A5：lifesteal 击毁回 10% 上限")
	GameState.buffs.clear()

	# 存档 v1（3 命制 lives）兼容：血量按满血处理，分数正常恢复
	var f := FileAccess.open(GameState.SAVE_PATH, FileAccess.WRITE)
	f.store_string(JSON.stringify({"version": 1, "lives": 2.0, "score": 123}))
	f.close()
	GameState.apply_run_save(GameState.load_run_data())
	_check(GameState.health == GameState.max_health(), "A5：v1 存档血量按满血处理")
	_check(GameState.score == 123, "A5：v1 存档分数兼容")
	GameState.score = 0
	GameState.delete_save()

	# ================= A2：Boss 身体撞击 30 =================
	# 入场降入期：与玩家重叠也不扣血（Boss 尚未降入战斗位置）
	GameState.health = 100.0
	_reset_hit_state(player)
	player.position = Vector2(960.0, 150.0)  # FIGHT_Y(230) 之上
	var boss_enter := _make_boss(1)
	boss_enter.position = player.position  # 重叠，但仍在降入阶段
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(not boss_enter._in_fight, "A2：Boss 仍处于入场降入阶段")
	_check(GameState.health == 100.0, "A2：入场降入期撞击不扣血")
	boss_enter.queue_free()
	await get_tree().physics_frame

	# 进入战斗：撞击玩家 -30 HP，Boss 不掉血不死
	player.position = Vector2(960.0, 800.0)
	GameState.health = 100.0
	_reset_hit_state(player)
	var boss_fight := _make_boss(1)
	boss_fight._in_fight = true  # 直接置战斗态（重叠事件由传送产生，避开降入时序）
	boss_fight._fire_timer = 999.0  # 屏蔽开火，保证场内无杂弹
	boss_fight.position = player.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(GameState.health == 70.0, "A2：撞 Boss 身体玩家 -30 HP")
	_check(
		is_instance_valid(boss_fight) and boss_fight.hp == boss_fight.max_hp,
		"A2：撞击后 Boss 不掉血不死"
	)
	boss_fight.queue_free()
	await get_tree().physics_frame

	# ================= A6：敌机撞击 20 且不自毁 =================
	GameState.health = 100.0
	_reset_hit_state(player)
	player.position = Vector2(960.0, 800.0)
	var ram_e := _make_enemy(spawner.ENEMY_TYPES[0])
	ram_e.position = player.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(GameState.health == 80.0, "A6：敌机撞击玩家 -20 HP")
	_check(is_instance_valid(ram_e) and ram_e.visible, "A6：撞击后敌机不自毁继续存活")
	_check(GameState.enemies.has(ram_e), "A6：撞击后敌机仍在注册表（未离场）")
	ram_e.queue_free()
	await get_tree().physics_frame

	# ================= A7/A8：闪避 20% / 护甲 ×0.85（全伤害源两段式） =================
	# 护甲：固定 ×0.85，无随机
	GameState.add_buff(&"armor")
	GameState.health = 100.0
	_reset_hit_state(player)
	player.take_damage(20.0)
	_check(is_equal_approx(GameState.health, 83.0), "A8：护甲固定 ×0.85（20→17）")
	GameState.buffs.clear()
	# 闪避：60 次独立判定应至少闪 1 次且不全闪（20% 概率）
	GameState.add_buff(&"evasion")
	var dodges := 0
	for i in 60:
		GameState.health = 100.0
		_reset_hit_state(player)
		if not player.take_damage(10.0):
			dodges += 1
	_check(dodges >= 1, "A7：闪避可触发（60 次至少 1 次）")
	_check(dodges <= 24, "A7：闪避率约 20% 而非全闪")
	GameState.buffs.clear()
	# 子弹伤害同样过闪避/护甲（去 bug 统一版：不再仅限撞击）——由上方 take_damage 直接覆盖

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
	# 锁血期（触发→RELEASE_HOLD 前）：任何伤害不掉血不死
	boss3.take_damage(1)
	_check(
		is_equal_approx(boss3.hp, boss3.max_hp * boss3.ENRAGE_HP_RATIO),
		"A3：锁血期伤害不掉血"
	)
	# 序列中断/RELEASE_HOLD 解锁后：小额伤害正常扣血
	boss3._abort_enrage_sequence()
	boss3.take_damage(1)
	_check(boss3.hp < boss3.max_hp * boss3.ENRAGE_HP_RATIO, "A3：锁血解除后正常扣血")
	boss3.queue_free()
	await get_tree().process_frame
	# 致死伤害：满血一击直接击杀（不触发狂暴钳制）
	var boss4 := _make_boss(1)
	boss4.take_damage(9999)
	await get_tree().process_frame
	_check(not is_instance_valid(boss4), "A3：致死伤害直接击杀")

	# ================= A4：敌弹 damage 按弹种 12/10/20（基准值） =================
	# 伤害随对局进程 ramp（×enemy_damage_ramp），期望按当前 ramp 动态计算（同 boss_pattern 场景4 口径）
	var dmg_ramp := GameState.enemy_damage_ramp()
	var exp10 := maxi(1, int(roundf(10.0 * dmg_ramp)))
	var exp12 := maxi(1, int(roundf(12.0 * dmg_ramp)))
	var exp14 := maxi(1, int(roundf(14.0 * dmg_ramp)))
	var exp20 := maxi(1, int(roundf(20.0 * dmg_ramp)))
	var exp21 := maxi(1, int(roundf(21.0 * dmg_ramp)))
	player.position = Vector2(960.0, 800.0)
	# laser 弹：配置 damage=20
	var laser_e := _make_enemy(spawner.ELITE_TYPES[1])
	laser_e.bullet_type = &"laser"
	laser_e.position = Vector2(960.0, 300.0)
	laser_e._fire_at_player()
	var laser_b: Bullet = null
	for b in _enemy_bullets():
		if b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"laser":
			laser_b = b
	_check(laser_b != null and laser_b.damage == exp20, "A4：laser 敌弹 damage（基准 20，期望 %d）" % exp20)
	# 命中玩家扣血（传送重叠走真实碰撞管线）
	GameState.health = 100.0
	_reset_hit_state(player)
	if laser_b != null:
		laser_b.speed = 0.0
		laser_b.position = player.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(GameState.health == 100.0 - exp20, "A4：laser 敌弹命中 -%d HP" % exp20)
	laser_e.queue_free()
	await _free_enemy_bullets()

	# single 弹：配置 damage=12
	var single_e := _make_enemy(spawner.ENEMY_TYPES[0])
	single_e.bullet_type = &"single"
	single_e.position = Vector2(960.0, 300.0)
	single_e._fire_at_player()
	var single_b: Bullet = null
	for b in _enemy_bullets():
		if b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"single":
			single_b = b
	_check(single_b != null and single_b.damage == exp12, "A4：single 敌弹 damage（基准 12，期望 %d）" % exp12)
	GameState.health = 100.0
	_reset_hit_state(player)
	if single_b != null:
		single_b.speed = 0.0
		single_b.position = player.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(GameState.health == 100.0 - exp12, "A4：single 敌弹命中 -%d HP" % exp12)
	single_e.queue_free()
	await _free_enemy_bullets()

	# spread 弹：配置 damage=10（五向扇形，取任一发）
	var spread_e := _make_enemy(spawner.ENEMY_TYPES[0])
	spread_e.bullet_type = &"spread"
	spread_e.position = Vector2(960.0, 300.0)
	spread_e._fire_at_player()
	var spread_dmg_ok := false
	for b in _enemy_bullets():
		if b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"spread":
			spread_dmg_ok = spread_dmg_ok or b.damage == exp10
	_check(spread_dmg_ok, "A4：spread 敌弹 damage（基准 10，期望 %d）" % exp10)
	spread_e.queue_free()
	await _free_enemy_bullets()

	# Boss 弹种取值（基准）：fan=14，homing=12，狙击=21，cross=12，快照激光=21，快照环弹=12
	var boss5 := _make_boss(1)
	boss5.position = Vector2(960.0, 300.0)
	boss5._fire_timer = 999.0  # 屏蔽常规开火，保证弹丸计数纯净
	boss5._fire_fan()
	var fan_dmg_ok := true
	var fan_count := 0
	for b in _enemy_bullets():
		fan_count += 1
		if b.damage != exp14:
			fan_dmg_ok = false
	_check(fan_count == 5 and fan_dmg_ok, "A4：Boss 扇形弹 damage（基准 14，期望 %d）" % exp14)
	await _free_enemy_bullets()
	boss5._fire_homing()
	var homing_b: Bullet = null
	for b in _enemy_bullets():
		homing_b = b
	_check(homing_b != null and homing_b.damage == exp12 and homing_b.homing, "A4：Boss 追踪弹 damage（基准 12，期望 %d）" % exp12)
	await _free_enemy_bullets()
	boss5._fire_cross()
	var cross_dmg_ok := true
	var cross_count := 0
	for b in _enemy_bullets():
		cross_count += 1
		if b.damage != exp12:
			cross_dmg_ok = false
	_check(cross_count == 4 and cross_dmg_ok, "A4：Boss 十字弹 damage（基准 12，期望 %d）" % exp12)
	await _free_enemy_bullets()
	boss5._fire_sniper()
	var sniper_b: Bullet = null
	for b in _enemy_bullets():
		if not b.has_meta("bullet_type"):
			sniper_b = b
	_check(sniper_b != null and sniper_b.damage == exp21, "A4：Boss 狙击弹 damage（基准 21，期望 %d）" % exp21)
	await _free_enemy_bullets()
	boss5.fire_enrage_snapshot()
	var snap_laser_dmg_ok := true
	var snap_ring_dmg_ok := true
	var snap_lasers := 0
	var snap_rings := 0
	for b in _enemy_bullets():
		if b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"laser":
			snap_lasers += 1
			if b.damage != exp21:
				snap_laser_dmg_ok = false
		elif b.has_meta("bullet_type") and b.get_meta("bullet_type") == &"enrage_ring":
			snap_rings += 1
			if b.damage != exp12:
				snap_ring_dmg_ok = false
	_check(
		snap_lasers == boss5.ENRAGE_SNAPSHOT_LASERS and snap_laser_dmg_ok,
		"A4：Boss 狂暴快照激光 damage（基准 21，期望 %d）" % exp21
	)
	_check(
		snap_rings == boss5.ENRAGE_SNAPSHOT_RING and snap_ring_dmg_ok,
		"A4：Boss 狂暴快照环弹 damage（基准 12，期望 %d）" % exp12
	)
	boss5.queue_free()
	await _free_enemy_bullets()

	# ================= A9：受击清 250px 敌弹 =================
	GameState.health = 100.0
	_reset_hit_state(player)
	player.position = Vector2(960.0, 800.0)
	var near_positions: Array[Vector2] = [Vector2(50.0, 0.0), Vector2(0.0, 100.0), Vector2(-240.0, 0.0)]
	var near_bullets: Array[Bullet] = []
	for off in near_positions:
		var nb := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, false)
		nb.position = player.position + off
		near_bullets.append(nb)
	var far_b := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, false)
	far_b.position = player.position + Vector2(400.0, 0.0)
	# 用一发独立敌弹触发受击
	var hit_b := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, false)
	hit_b.position = player.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	var near_cleared := true
	for nb in near_bullets:
		if nb.visible:
			near_cleared = false
	_check(GameState.health == 90.0, "A9：触发受击 -10 HP")
	_check(near_cleared, "A9：250px 内敌弹全部清除")
	_check(far_b.visible, "A9：250px 外敌弹保留")

	# ================= A16：同帧多敌弹只结算第一发；无敌期敌弹穿过不销毁 =================
	GameState.health = 100.0
	_reset_hit_state(player)
	var b1 := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 12, false)
	b1.position = player.position
	var b2 := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 12, false)
	b2.position = player.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(GameState.health == 88.0, "A16：同帧只结算第一发（-12 而非 -24）")
	# 无敌期内敌弹直接穿过：不结算、不销毁
	var b3 := GameState.bullet_pool.fire(Vector2.DOWN, 300.0, 12, false)
	b3.position = player.position + Vector2(0.0, -60.0)
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(GameState.health == 88.0, "A16：无敌期内敌弹穿过不结算")
	_check(b3.visible, "A16：穿过的敌弹不销毁")
	await _free_enemy_bullets()

	# ================= A10：精英碰撞半径与普通机同档 =================
	var elite_r := _make_enemy(spawner.ELITE_TYPES[0])
	var elite_shape := (elite_r.get_node("CollisionShape2D") as CollisionShape2D).shape as CircleShape2D
	_check(is_equal_approx(elite_shape.radius, 34.0), "A10：精英重甲碰撞半径 34（与普通同档）")
	elite_r.queue_free()
	var elite_r2 := _make_enemy(spawner.ELITE_TYPES[1])
	var elite_shape2 := (elite_r2.get_node("CollisionShape2D") as CollisionShape2D).shape as CircleShape2D
	_check(is_equal_approx(elite_shape2.radius, 30.0), "A10：精英游击碰撞半径 30（与普通同档）")
	elite_r2.queue_free()

	# ================= A12：爆炸弹 50px/固定 30/主目标吃溅射/Boss 不吃 =================
	GameState.add_buff(&"explosive")
	var tgt_a := _make_enemy(spawner.ENEMY_TYPES[0])
	tgt_a.hp = 200
	tgt_a.speed = 0.0
	tgt_a.position = Vector2(960.0, 400.0)
	var tgt_b := _make_enemy(spawner.ENEMY_TYPES[0])
	tgt_b.hp = 200
	tgt_b.speed = 0.0
	tgt_b.position = Vector2(1000.0, 400.0)  # 40px：超出直击判定（30+6）但在爆炸半径 50 内
	var tgt_c := _make_enemy(spawner.ENEMY_TYPES[0])
	tgt_c.hp = 200
	tgt_c.speed = 0.0
	tgt_c.position = Vector2(1120.0, 400.0)  # 160px 外在半径外
	var ex_b := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, true)
	ex_b.explosive = true
	ex_b.position = tgt_a.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(tgt_a.hp == 160, "A12：主目标吃直击 10 + 溅射 30 两段")
	_check(tgt_b.hp == 170, "A12：半径内邻机吃溅射 30")
	_check(tgt_c.hp == 200, "A12：半径外敌机不受影响")
	tgt_a.queue_free()
	tgt_b.queue_free()
	tgt_c.queue_free()
	await get_tree().physics_frame
	# Boss 不吃爆炸 AoE：关碰撞手动触发（Boss r=120 必与子弹重叠，无法走真实碰撞隔离）
	var boss_aoe := _make_boss(1)
	boss_aoe._fire_timer = 999.0
	boss_aoe.position = Vector2(1000.0, 400.0)  # 距爆心 40px 在半径内
	var ex_b2 := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, true)
	ex_b2.explosive = true
	ex_b2.monitoring = false  # 只手动测 AoE，不走碰撞
	ex_b2.position = Vector2(960.0, 400.0)
	ex_b2._explode()
	_check(boss_aoe.hp == boss_aoe.max_hp, "A12：Boss 不吃爆炸 AoE")
	boss_aoe.queue_free()
	ex_b2.queue_free()
	GameState.buffs.clear()
	await get_tree().physics_frame

	# ================= A13：慢速力场全局敌机移速 ×0.8（敌弹不受影响） =================
	var slow_e1 := _make_enemy(spawner.ENEMY_TYPES[0])
	slow_e1.speed = 100.0
	slow_e1.position = Vector2(960.0, 100.0)
	await get_tree().create_timer(0.5).timeout
	var d1: float = slow_e1.position.y - 100.0
	slow_e1.queue_free()
	GameState.add_buff(&"slow_field")
	var slow_e2 := _make_enemy(spawner.ENEMY_TYPES[0])
	slow_e2.speed = 100.0
	slow_e2.position = Vector2(960.0, 100.0)
	await get_tree().create_timer(0.5).timeout
	var d2: float = slow_e2.position.y - 100.0
	slow_e2.queue_free()
	_check(d1 > 20.0 and d2 < d1 * 0.9 and d2 > d1 * 0.6, "A13：力场全局敌机移速 ×0.8")
	# 敌弹不再被减速（力场已迁出子弹侧）
	var eb := GameState.bullet_pool.fire(Vector2.DOWN, 300.0, 10, false)
	eb.position = Vector2(960.0, 200.0)
	await get_tree().create_timer(0.4).timeout
	var bd: float = eb.position.y - 200.0
	_check(bd > 100.0, "A13：力场下敌弹全速不受影响")
	await _free_enemy_bullets()
	GameState.buffs.clear()

	# ================= A20：出生保护 1.0s（对齐原作入场动画等效保护） =================
	_check(is_equal_approx(player.SPAWN_INVINCIBLE_TIME, 1.0), "A20：出生保护 1.0s")
	_check(is_equal_approx(player.INVINCIBLE_TIME, 1.5), "A20：受击无敌 1.5s（90 帧）")
	# 行为级：新实例化的玩家出生即带 1.0s 保护
	var fresh_player := (load("res://scenes/player.tscn") as PackedScene).instantiate() as Player
	main.add_child(fresh_player)
	fresh_player._auto_fire_enabled = false
	await get_tree().process_frame
	_check(fresh_player._invincible > 0.9 and fresh_player._invincible <= 1.0, "A20：出生即带 1.0s 保护")
	fresh_player.queue_free()
	await get_tree().process_frame

	# ================= A5 补充：被动回血受伤重置延迟 =================
	GameState.health = 50.0
	_reset_hit_state(player)
	player.take_damage(10.0)
	await get_tree().create_timer(0.5).timeout
	_check(GameState.health == 40.0, "A5：受击后被动回血延迟重置（0.5s 内不回血）")

	# ================= A5 补充：v2 存档带 extra_life 完整往返 =================
	GameState.buffs.clear()
	GameState.add_buff(&"extra_life")
	GameState.add_buff(&"extra_life")
	GameState.health = 180.0
	GameState.save_run(50.0, 10.0)
	GameState.reset_run()
	_check(GameState.max_health() == 100.0, "A5：reset 后上限回 100")
	GameState.apply_run_save(GameState.load_run_data())
	_check(GameState.max_health() == 200.0, "A5：v2 存档恢复 extra_life 上限")
	_check(GameState.health == 180.0, "A5：v2 存档血量不被旧上限钳制")
	GameState.buffs.clear()
	GameState.health = 100.0
	GameState.delete_save()

	# ================= A21：Boss 入场期可被玩家弹伤（已核实与原作一致） =================
	var boss_early := _make_boss(1)
	boss_early._fire_timer = 999.0
	boss_early.position = Vector2(960.0, 100.0)  # 仍在降入
	var pb := GameState.bullet_pool.fire(Vector2.DOWN, 0.0, 10, true)
	pb.position = boss_early.position
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(
		not boss_early._in_fight and boss_early.hp < boss_early.max_hp,
		"A21：入场降入期玩家弹可伤 Boss（与原作一致）"
	)
	boss_early.queue_free()
	await _free_enemy_bullets()
	player._invincible = 999.0

	print("HIT LOGIC TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
