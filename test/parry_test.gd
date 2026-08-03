extends Node
## F 键弧光弹反盾测试（2026-08-03 机制四，docs/archive/2026-08-03-combat-fairness-plan.md §5）：
## 组件级：完整时间轴（WINDUP 前摇无判定 → ACTIVE 有效 → RECOVER 后摇）、硬冷却自流程
## 结束起算（完整周期 3.8s）、机身 tint 三阶段。
## 场景级：ACTIVE 弹反属性（转玩家弹/镜面反射 y 取反/×2 速/×1.5 伤）、命中敌机与 Boss、
## 扇区外不弹反、HUD 能量槽（满格/清空/匀速充能）、池回收与二次激活复位、
## 与宽限帧/擦弹正交。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


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


## 清理全部弹丸（含弹反后的玩家弹——防旧弹反弹命中新用例目标）
func _free_all_bullets() -> void:
	for child in get_node("Main").get_children():
		if child is Bullet:
			child.queue_free()
	await get_tree().process_frame


func _make_enemy(config: Dictionary, strategy: StringName = &"straight") -> Enemy:
	var e := (load("res://scenes/enemy.tscn") as PackedScene).instantiate() as Enemy
	e.setup(config, strategy, 1.0)
	e.can_shoot = false
	get_node("Main").add_child(e)
	return e


func _make_boss(p_type: int = 1) -> Boss:
	var boss := (load("res://scenes/boss.tscn") as PackedScene).instantiate() as Boss
	boss.setup(1.0, p_type)
	get_node("Main").add_child(boss)
	boss.set_fire_timer(999.0)  # 屏蔽开火（须在 add_child 后：_ready 会重置开火计时）
	return boss


func _reset_hit_state(player: Player) -> void:
	player.set_invincible(0.0)
	player.set_last_hit_frame(-1)
	player.set_since_damage(999.0)


## 等待弹反冷却就绪（IDLE 且冷却满；最多 timeout 秒），保证后续 try_parry 一定成功
func _await_parry_ready(player: Player, timeout: float = 5.0) -> void:
	var t := 0.0
	while t < timeout and (player.parry_phase() != PlayerParry.ParryPhase.IDLE or player.parry_cooldown_remaining() > 0.0):
		await get_tree().create_timer(0.1).timeout
		t += 0.1


## 启动弹反并等待进入 ACTIVE（最多 1s）
func _await_active(player: Player) -> void:
	player.try_parry()
	for i in 50:
		await get_tree().create_timer(0.02).timeout
		if player.parry_phase() == PlayerParry.ParryPhase.ACTIVE:
			return


func _ready() -> void:
	# ================= 组件级：完整时间轴 =================
	var pp := PlayerParry.new()
	pp.configure(0.8, 0.5, 3.0)
	_check(pp.try_start(), "时间轴：IDLE 可启动（进入 WINDUP）")
	_check(pp.phase == PlayerParry.ParryPhase.WINDUP and not pp.try_start(), "时间轴：流程中不可重复启动")
	pp.tick(0.1)
	_check(pp.phase == PlayerParry.ParryPhase.WINDUP, "时间轴：0.1s 仍在前摇（无判定期）")
	pp.tick(0.06)
	_check(pp.phase == PlayerParry.ParryPhase.ACTIVE, "时间轴：0.16s ≥ 前摇 0.15s 进入 ACTIVE")
	_check(pp.tint_strength() == 1.0, "时间轴：ACTIVE 金色 tint 保持")
	pp.tick(0.25)
	_check(pp.phase == PlayerParry.ParryPhase.ACTIVE, "时间轴：ACTIVE 0.25s 内保持")
	_check(pp.energy_ratio() == 0.0, "能量槽：流程期保持空")
	pp.tick(0.25)
	_check(pp.phase == PlayerParry.ParryPhase.RECOVER, "时间轴：0.5s 有效窗满进入 RECOVER")
	pp.tick(0.05)  # RECOVER 中段（0.05/0.15）
	_check(pp.tint_strength() < 1.0 and pp.tint_strength() > 0.3, "时间轴：RECOVER 后摇金色渐弱")
	pp.tick(0.1)
	_check(pp.phase == PlayerParry.ParryPhase.IDLE, "时间轴：0.15s 后摇满回归 IDLE")
	_check(pp.cooldown_remaining() > 2.9, "时间轴：硬冷却自 RECOVER 完成起算（完整周期 0.8+3.0）")
	_check(not pp.try_start(), "冷却：3s 冷却期内不可再次展开")
	pp.tick(1.5)
	_check(is_equal_approx(pp.energy_ratio(), 0.5), "能量槽：冷却按 3.0s 匀速充能（1.5s 后半格）")
	pp.tick(1.5)
	_check(pp.cooldown_remaining() == 0.0 and pp.energy_ratio() == 1.0, "冷却：满 3s 回满格")
	_check(pp.try_start(), "冷却：满 3s 后可再次展开（完整周期 3.8s）")

	# ================= 场景级环境 =================
	GameState.delete_save()
	# L15：快照用户最高分，结尾还原（high_score setter 自动落盘，不清用户 profile 数据）
	var orig_high_score: int = GameState.high_score
	GameState.high_score = 0
	GameState.save_profile()
	add_child((load("res://scenes/main.tscn") as PackedScene).instantiate())
	var main := get_node("Main")
	var start_panel: CanvasLayer = get_node("Main/StartPanel")
	if start_panel.visible:
		start_panel.press_new_game()
	var player: Player = get_node("Main/Player")
	player.set_auto_fire(false)
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)
	for child in main.get_children():
		if child is Enemy or child is Bullet:
			child.queue_free()
	await get_tree().process_frame
	player.position = Vector2(960.0, 800.0)
	GameState.health = 100.0
	_reset_hit_state(player)

	# ================= 场景级：ACTIVE 弹反属性 =================
	await _await_active(player)
	var pb := GameState.bullet_pool.fire(Vector2.DOWN, 200.0, 10, false)
	pb.position = Vector2(960.0, 770.0)  # 玩家上方 30px（机头前方扇区内）
	await get_tree().create_timer(0.1).timeout
	_check(pb.is_player_bullet, "弹反：ACTIVE 期盾区敌弹被弹反（转玩家弹）")
	_check(pb.direction.y < 0.0, "弹反：方向镜面反射（y 取反，朝上返回）")
	_check(is_equal_approx(pb.speed, 400.0), "弹反：speed ×2.0（200→400）")
	_check(pb.damage == 15, "弹反：damage = 原 ×1.5 四舍五入（10→15）")
	await _free_all_bullets()  # 清掉反弹的玩家弹（防其命中后续用例目标）

	# ================= 弹反命中普通敌机（1.5 倍伤害结算） =================
	await _await_parry_ready(player)
	var target := _make_enemy(spawner.ENEMY_TYPES[0])
	target.hp = 100
	target.position = Vector2(960.0, 300.0)
	await _await_active(player)
	var rb := GameState.bullet_pool.fire(Vector2.DOWN, 300.0, 10, false)
	rb.position = Vector2(960.0, 770.0)
	await get_tree().create_timer(1.2).timeout  # 弹反后 600px/s 飞 470px ≈ 0.78s（余量防偶发）
	_check(is_instance_valid(target) and target.hp == 85, "弹反：命中普通敌机按 15 伤害结算（100→85）")
	target.queue_free()
	await _free_all_bullets()

	# ================= 弹反命中 Boss =================
	await _await_parry_ready(player)
	var boss := _make_boss(1)
	boss.position = Vector2(960.0, 300.0)
	var boss_hp0: float = boss.hp
	await _await_active(player)
	var bb := GameState.bullet_pool.fire(Vector2.DOWN, 300.0, 10, false)
	bb.position = Vector2(960.0, 770.0)
	await get_tree().create_timer(0.08).timeout  # 弹反发生（盾区进入 1-2 物理帧；Boss strafe 漂移大，不做真实飞行命中）
	_check(bb.is_player_bullet and bb.damage == 15, "弹反：Boss 用例弹已被弹反（1.5 倍伤害）")
	bb.position = boss.position  # 传送重叠命中：结算走玩家弹 enemy 组路径（对齐 hit_logic 传送重叠惯例）
	await get_tree().physics_frame
	await get_tree().physics_frame
	_check(is_instance_valid(boss) and is_equal_approx(boss.hp, boss_hp0 - 15.0), "弹反：命中 Boss 同样按 15 伤害结算")
	boss.queue_free()
	await _free_all_bullets()

	# ================= 扇区外（140° 外）不弹反 =================
	await _await_parry_ready(player)
	await _await_active(player)
	var behind := GameState.bullet_pool.fire(Vector2.UP, 100.0, 12, false)
	behind.position = Vector2(960.0, 850.0)  # 玩家正下方（机头前方 140° 扇区外）
	await get_tree().create_timer(0.2).timeout
	_check(not behind.is_player_bullet, "扇区外：后方敌弹不被弹反（保持敌弹）")
	await _free_all_bullets()

	# ================= WINDUP/RECOVER 无判定（场景级复核） =================
	await _await_parry_ready(player)
	player.try_parry()
	var wb := GameState.bullet_pool.fire(Vector2.RIGHT, 400.0, 12, false)
	wb.position = Vector2(930.0, 745.0)  # 前摇期进入盾区（y=745 前方 55px），0.15s 内水平穿出
	await get_tree().create_timer(0.1).timeout
	_check(not wb.is_player_bullet, "前摇：WINDUP 期内盾区弹不弹反")
	await get_tree().create_timer(0.2).timeout  # ACTIVE 已开始；弹已穿出盾区（无进入事件）
	_check(not wb.is_player_bullet, "前摇：弹穿出盾区后不弹反（弹反只在进入时刻）")
	await _free_all_bullets()

	# ================= 硬冷却（场景级）：流程结束起 3s 内不可再展开 =================
	await _await_parry_ready(player)
	player.try_parry()
	await get_tree().create_timer(0.9).timeout  # 流程 0.8s 播完（RECOVER 完成）
	_check(player.parry_phase() == PlayerParry.ParryPhase.IDLE, "冷却：场景级流程结束回归 IDLE")
	_check(not player.try_parry(), "冷却：3s 冷却期内再次展开被拒")
	await get_tree().create_timer(3.0).timeout
	_check(player.try_parry(), "冷却：满 3s 后可再次展开")
	await get_tree().create_timer(0.9).timeout  # 本次流程播完（不干扰后续）

	# ================= HUD 能量槽 =================
	var parry_bar: SegmentedBar = get_node("Main/HUD/ParryBar")
	await _await_parry_ready(player)  # 等冷却结束回满格
	await get_tree().create_timer(0.2).timeout  # HUD 0.1s 节流刷新
	_check(parry_bar.value == 100.0, "能量槽：HUD 满格显示（冷却结束）")
	player.try_parry()
	await get_tree().create_timer(0.15).timeout
	_check(parry_bar.value == 0.0, "能量槽：按下即清空（HUD 联动）")
	await get_tree().create_timer(1.4).timeout  # 流程 0.8s 结束 + 冷却 0.6s
	var ratio: float = player.parry_energy_ratio()
	_check(ratio > 0.15 and ratio < 0.3, "能量槽：流程结束起按 3.0s 匀速充能（约 0.6s ≈ 0.2 格，实测 %.2f）" % ratio)

	# ================= 池回收与二次激活复位 =================
	await _await_parry_ready(player)
	await _await_active(player)
	var r1 := GameState.bullet_pool.fire(Vector2.DOWN, 300.0, 10, false)
	r1.position = Vector2(960.0, 770.0)
	await get_tree().create_timer(1.5).timeout  # 弹反后 600px/s 朝上出界回收
	_check(not r1.is_active(), "池回收：弹反弹出界后按既有路径回收")
	var r2 := GameState.bullet_pool.fire(Vector2.DOWN, 500.0, 20, true)
	_check(r2 == r1, "池复用：回收弹被复用（同一实例）")
	_check(r2.is_player_bullet and r2.damage == 20 and is_equal_approx(r2.speed, 500.0), "池复用：二次激活状态复位（阵营/伤害/速度）")
	r2.queue_free()
	await get_tree().process_frame

	# ================= 与宽限帧/擦弹正交 =================
	GameState.health = 100.0
	_reset_hit_state(player)
	await _await_parry_ready(player)
	await _await_active(player)
	var sweep := GameState.bullet_pool.fire(Vector2.RIGHT, 600.0, 12, false)
	sweep.position = player.position + Vector2(-30.0, 3.0)  # 玩家下方边缘带（扇区外）水平擦过
	await get_tree().create_timer(0.2).timeout
	_check(GameState.health == 100.0, "正交：盾展开期受击宽限帧不受影响（擦过无伤）")
	_check(not sweep.is_player_bullet, "正交：盾区外弹不被弹反（宽限路径照常）")
	await _free_enemy_bullets()

	for child in main.get_children():
		if child is Bullet:
			child.queue_free()
	await get_tree().process_frame
	await get_tree().create_timer(0.6).timeout
	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	print("PARRY TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
