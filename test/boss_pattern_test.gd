extends Node
## Boss 逐型模式库与差异化狂暴测试（BOSS_REDESIGN §5，阶段 B）：
## 场景1（一型 P2 蓄力重炮）：蓄力辉光 telegraph 先行 → 3 发 700 弹速/21 伤害重弹；
## 场景2（二型 P2 冲刺掠过）：水平瞄准线先行 → 高速横穿 + 路径拖 3 枚减速弹 → 回巡航位；
## 场景3（二型狂暴「猎杀环绕」）：轨道象限点瞬停 + 每点瞄准线 + 单发狙，收尾 12 向慢环；
## 场景4（三型 P2 编队齐射/弹幕墙）：4 小怪横队 0.8s 后齐射；10 槽位墙留 2 相邻缺口
##   且缺口避开自机方位 ±30°；
## 场景5（三型狂暴「倾巢」）：ACTIVE 3 波小怪 + 8 向环弹，收尾 16 向慢环 + 小怪齐射；
## 场景6（难度分档 §4.4）：easy 弹数减/间隔 ×1.15/弹速 ×0.9，hard 反向；HP/伤害不动。
## 一型狂暴（悬停环弹进动 + 8 路重炮齐射）断言在 boss_enrage_test。

var _failures: int = 0


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 真实时间等待（不受 time_scale 影响；process_always 保证暂停时也走时）
func _wait_real(sec: float) -> void:
	await get_tree().create_timer(sec, true, false, true).timeout


## 无 meta 敌弹中指定弹速的弹（重炮/掠过拖弹/齐射/墙弹以此识别；
## 敌机自机狙带 bullet_type meta，Boss 狂暴弹带 laser/enrage_ring meta，互不混淆）
func _bullets_by_speed(p_speed: float) -> Array[Bullet]:
	var out: Array[Bullet] = []
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet and not child.has_meta("bullet_type"):
			if is_equal_approx((child as Bullet).speed, p_speed):
				out.append(child)
	return out


func _count_meta_bullets(p_type: StringName) -> int:
	var n := 0
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet and child.has_meta("bullet_type"):
			if child.get_meta("bullet_type") == p_type:
				n += 1
	return n


func _enemies_alive() -> Array[Enemy]:
	var out: Array[Enemy] = []
	for child in get_node("Main").get_children():
		if child is Enemy:
			out.append(child)
	return out


func _clear_field() -> void:
	for child in get_node("Main").get_children():
		if child is Enemy or (child is Bullet and not child.is_player_bullet):
			child.queue_free()
	await get_tree().process_frame


func _close_buff_ui_if_open() -> void:
	var buff_ui: CanvasLayer = get_node("Main/BuffUI")
	if buff_ui.visible:
		var ev := InputEventMouseButton.new()
		ev.pressed = true
		ev.button_index = MOUSE_BUTTON_LEFT
		buff_ui._on_card_gui_input(ev, &"rapid_fire")
	get_tree().paused = false


## 生成 Boss 并跳过降入；调用方负责击杀/清理
func _spawn_test_boss(p_type: int) -> Boss:
	var spawner: Node = get_node("Main/Spawner")
	spawner._spawn_boss(p_type)
	await get_tree().process_frame
	var boss: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss = child
	boss.position.y = boss.fight_anchor_y()  # 跳过降入（锚线 = view 顶缘 + FIGHT_Y），下一物理帧进入战斗
	return boss


## 强制进入 P2 并换成指定模式表（绕过血量流程，专注攻击断言）
func _force_p2_patterns(boss: Boss, p2: Array) -> void:
	boss._patterns = {"p1": [{"attack": &"fan5", "waves": 1, "interval": 0.3}], "p2": p2}
	boss._fight_phase = Boss.FightPhase.P2
	boss._pattern_index = 0
	boss._start_pattern()
	boss._fire_timer = 0.1


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	GameState.high_score = 0
	GameState.difficulty = &"medium"  # 场景1-5 弹速/弹数断言基于 medium 基准档
	GameState.save_profile()
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
	player._auto_fire_enabled = false  # 全程禁用全自动开火，避免误杀 Boss/触发里程碑
	player._invincible = 999.0  # 弹幕/掠过期间不被误伤
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性
	player.position = Vector2(960.0, 540.0)

	# ================= 场景 1：一型 P2 蓄力重炮 =================
	var boss1: Boss = await _spawn_test_boss(1)
	_check(boss1 != null, "场景1：Boss 已生成")
	boss1.CANNON_CHARGE = 0.4
	_force_p2_patterns(boss1, [{"attack": &"charged_cannon", "waves": 1, "interval": 1.2}])
	var cannon_started := false
	var cannon_tick := 0
	for i in 40:
		await _wait_real(0.05)
		if not is_instance_valid(boss1):
			break
		if boss1._attacks.cannon_elapsed() >= 0.0:
			cannon_started = true
			cannon_tick = Time.get_ticks_msec()
			break
	_check(cannon_started, "场景1：蓄力重炮蓄力 telegraph 起手")
	_check(_bullets_by_speed(700.0).is_empty(), "场景1：蓄力期间未出弹（telegraph 先行）")
	var heavy_max := 0
	var first_fire_elapsed := -1
	for i in 50:
		await _wait_real(0.05)
		if not is_instance_valid(boss1):
			break
		var n := _bullets_by_speed(700.0).size()
		if n > 0 and first_fire_elapsed < 0:
			first_fire_elapsed = Time.get_ticks_msec() - cannon_tick
		heavy_max = maxi(heavy_max, n)
		if heavy_max >= 3:
			break
	_check(first_fire_elapsed >= 350, "场景1：蓄力 ≥0.35s 后才出弹（实测 %dms）" % first_fire_elapsed)
	_check(heavy_max >= 3, "场景1：3 发高速重弹（700 弹速）")
	var heavy_dmg_ok := true
	for b in _bullets_by_speed(700.0):
		if b.damage != 21:
			heavy_dmg_ok = false
	_check(heavy_dmg_ok, "场景1：重弹伤害 21")
	boss1.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_field()

	# ================= 场景 2：二型 P2 冲刺掠过 =================
	var boss2: Boss = await _spawn_test_boss(2)
	_check(boss2 != null, "场景2：Boss 已生成")
	boss2.SWEEP_AIM = 0.3
	boss2.SWEEP_RETURN_DURATION = 0.3
	_force_p2_patterns(boss2, [{"attack": &"dash_sweep", "waves": 1, "interval": 1.2}])
	var sweep_aimed := false
	for i in 40:
		await _wait_real(0.05)
		if not is_instance_valid(boss2):
			break
		if boss2._attacks.sweep_state() == Boss.SweepState.AIM and boss2._attacks.sweep_line() != null:
			sweep_aimed = true
			break
	_check(sweep_aimed, "场景2：冲刺掠过水平瞄准线 telegraph 先行")
	_check(_bullets_by_speed(150.0).is_empty(), "场景2：瞄准期间未拖弹")
	var dashing := false
	var x0 := 0.0
	for i in 40:
		await _wait_real(0.05)
		if not is_instance_valid(boss2):
			break
		if boss2._attacks.sweep_state() == Boss.SweepState.DASH:
			dashing = true
			x0 = boss2.position.x
			break
	_check(dashing, "场景2：瞄准结束进入高速横穿")
	await _wait_real(0.15)
	if is_instance_valid(boss2):
		_check(absf(boss2.position.x - x0) > 100.0, "场景2：横穿速度 ~900（0.15s 位移 >100px）")
	var drops := 0
	var sweep_done := false
	for i in 80:
		await _wait_real(0.05)
		if not is_instance_valid(boss2):
			break
		drops = maxi(drops, _bullets_by_speed(150.0).size())
		if boss2._attacks.sweep_state() == Boss.SweepState.NONE:
			sweep_done = true
			break
	_check(drops >= 3, "场景2：路径等距拖 3 枚减速弹（150 弹速）")
	var drop_dmg_ok := true
	for b in _bullets_by_speed(150.0):
		if b.damage != 12:
			drop_dmg_ok = false
	_check(drop_dmg_ok, "场景2：减速弹伤害 12")
	_check(sweep_done, "场景2：穿屏后回到巡航流程")
	if is_instance_valid(boss2):
		_check(absf(boss2.position.y - boss2.fight_anchor_y()) < 40.0, "场景2：归位回 FIGHT_Y 战斗位")
	boss2.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_field()

	# ================= 场景 3：二型狂暴「猎杀环绕」 =================
	var boss3: Boss = await _spawn_test_boss(2)
	_check(boss3 != null, "场景3：Boss 已生成")
	boss3.ENRAGE_DURATION = 2.0
	boss3.ENRAGE_TRANSITION_DURATION = 0.2
	boss3.ENRAGE_ATTACK_WINDUP = 0.1
	boss3.E2_POINT_INTERVAL = 0.3
	boss3.E2_AIM = 0.15
	boss3.ENRAGE_RELEASE_HOLD_DURATION = 0.5
	boss3.ENRAGE_RETURN_DURATION = 0.4
	await _wait_real(0.3)
	boss3.take_damage(int(boss3.max_hp * 0.75))
	await get_tree().process_frame
	_check(boss3._enraged, "场景3：血量 <30% 触发狂暴")
	main._bullet_time_left = 0.05
	var active3 := false
	for i in 40:
		await _wait_real(0.1)
		if not is_instance_valid(boss3):
			break
		if boss3._enrage_seq.phase() == Boss.EnragePhase.ACTIVE:
			active3 = true
			break
	_check(active3, "场景3：TRANSITION 结束进入 ACTIVE")
	var aim_seen := false
	var max_index := 0
	var heavy3_max := 0
	var pos_samples: Array[Vector2] = []
	for i in 30:  # ~1.5s 覆盖 ACTIVE
		await _wait_real(0.05)
		if not is_instance_valid(boss3):
			break
		if boss3._enrage_seq.phase() != Boss.EnragePhase.ACTIVE:
			break
		aim_seen = aim_seen or boss3._enrage_seq.aim_line() != null
		max_index = maxi(max_index, boss3._enrage_seq.attack_index())
		heavy3_max = maxi(heavy3_max, _bullets_by_speed(900.0).size())
		pos_samples.append(boss3.global_position)
	_check(aim_seen, "场景3：瞬停点 0.3s 瞄准线 telegraph")
	_check(max_index >= 4, "场景3：轨道象限点依次瞬停（≥4 点，实测 %d）" % max_index)
	var jump_max := 0.0
	for i in pos_samples.size():
		for j in i:
			jump_max = maxf(jump_max, pos_samples[i].distance_to(pos_samples[j]))
	_check(jump_max > 100.0, "场景3：瞬停点分布在轨道上（采样点分散）")
	_check(heavy3_max >= 2, "场景3：每点单发狙（900 弹速重弹，峰值同屏 %d 发）" % heavy3_max)
	var hold3 := false
	for i in 60:
		await _wait_real(0.05)
		if not is_instance_valid(boss3):
			break
		if boss3._enrage_seq.phase() == Boss.EnragePhase.RELEASE_HOLD:
			hold3 = true
			break
	_check(hold3, "场景3：ACTIVE 结束进入 RELEASE_HOLD")
	var ring3_max := 0
	for i in 30:
		await _wait_real(0.05)
		if not is_instance_valid(boss3):
			break
		ring3_max = maxi(ring3_max, _count_meta_bullets(&"enrage_ring"))
		if ring3_max >= 12:
			break
	_check(ring3_max >= 12, "场景3：收尾 12 向慢速环弹")
	if is_instance_valid(boss3):
		boss3.take_damage(9999)
		await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_field()

	# ================= 场景 4：三型 P2 编队齐射 + 弹幕墙 =================
	var boss4: Boss = await _spawn_test_boss(3)
	_check(boss4 != null, "场景4：Boss 已生成")
	boss4.VOLLEY_DELAY = 0.4
	boss4._summon_timer = 999.0  # 屏蔽常规召唤，保持计数纯净
	_force_p2_patterns(boss4, [
		{"attack": &"minion_volley", "waves": 1, "interval": 1.0},
		{"attack": &"bullet_wall", "waves": 1, "interval": 0.8},
	])
	var volley_row := false
	for i in 40:
		await _wait_real(0.05)
		if not is_instance_valid(boss4):
			break
		var marked := 0
		for e in _enemies_alive():
			if e.has_meta("hive_volley"):
				marked += 1
		if marked >= 4:
			volley_row = true
			break
	_check(volley_row, "场景4：编队齐射召唤 4 小怪列横队（meta 标记）")
	var volley_max := 0
	for i in 40:
		await _wait_real(0.05)
		if not is_instance_valid(boss4):
			break
		volley_max = maxi(volley_max, _bullets_by_speed(420.0).size())
		if volley_max >= 4:
			break
	_check(volley_max >= 4, "场景4：0.8s 后小怪齐射一轮自机狙（420 弹速普通敌弹）")
	var volley_dmg_ok := true
	# 伤害随对局进程 ramp（2026-07-29 修订：×enemy_damage_ramp，基准 12）
	var volley_expected := maxi(1, int(roundf(12.0 * GameState.enemy_damage_ramp())))
	for b in _bullets_by_speed(420.0):
		if b.damage != volley_expected:
			volley_dmg_ok = false
	_check(volley_dmg_ok, "场景4：齐射弹伤害随难度 ramp（基准 12，实测期望 %d）" % volley_expected)
	# 弹幕墙：10 槽位留 2 相邻缺口，缺口避开自机方位 ±30°
	var wall: Array[Bullet] = []
	for i in 60:
		await _wait_real(0.05)
		if not is_instance_valid(boss4):
			break
		wall = _bullets_by_speed(220.0)
		if wall.size() >= 8:
			break
	_check(wall.size() == 8, "场景4：弹幕墙 10 槽位出 8 弹（留 2 缺口，实测 %d）" % wall.size())
	if wall.size() == 8:
		var to_player := (player.global_position - boss4.global_position).angle()
		# 槽位占用重建（缺口可能在弧段端部，相邻角差法不可靠）：逐槽比对弹丸方位
		var spacing := deg_to_rad(150.0) / 9.0
		var first_slot := Vector2.DOWN.angle() - deg_to_rad(75.0)
		var filled: Array[bool] = []
		filled.resize(10)
		for b in wall:
			var idx := int(round((b.direction.angle() - first_slot) / spacing))
			if idx >= 0 and idx < 10:
				filled[idx] = true
		var missing: Array[int] = []
		for i in 10:
			if not filled[i]:
				missing.append(i)
		_check(
			missing.size() == 2 and missing[1] == missing[0] + 1,
			"场景4：缺口为 2 个相邻槽位（实测缺失槽 %s）" % str(missing)
		)
		var gap_far := true
		for m in missing:
			var slot_a: float = first_slot + spacing * float(m)
			if absf(angle_difference(slot_a, to_player)) <= deg_to_rad(28.0):
				gap_far = false
		_check(gap_far, "场景4：缺口方位避开自机 ±30°（保证可躲）")
	boss4.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_field()

	# ================= 场景 5：三型狂暴「倾巢」 =================
	var boss5: Boss = await _spawn_test_boss(3)
	_check(boss5 != null, "场景5：Boss 已生成")
	boss5.ENRAGE_DURATION = 2.0
	boss5.ENRAGE_TRANSITION_DURATION = 0.2
	boss5.ENRAGE_ATTACK_WINDUP = 0.1
	boss5.E3_SUMMON_INTERVAL = 0.4
	boss5.E3_RING_INTERVAL = 0.4
	boss5.ENRAGE_RELEASE_HOLD_DURATION = 0.5
	boss5.ENRAGE_RETURN_DURATION = 0.4
	boss5._summon_timer = 999.0
	await _wait_real(0.3)
	boss5.take_damage(int(boss5.max_hp * 0.75))
	await get_tree().process_frame
	_check(boss5._enraged, "场景5：血量 <30% 触发狂暴")
	main._bullet_time_left = 0.05
	var active5 := false
	for i in 40:
		await _wait_real(0.1)
		if not is_instance_valid(boss5):
			break
		if boss5._enrage_seq.phase() == Boss.EnragePhase.ACTIVE:
			active5 = true
			break
	_check(active5, "场景5：TRANSITION 结束进入 ACTIVE")
	var minion_max := 0
	var waves_max := 0
	var ring5_max := 0
	for i in 40:  # ~2s 覆盖 ACTIVE
		await _wait_real(0.05)
		if not is_instance_valid(boss5):
			break
		if boss5._enrage_seq.phase() != Boss.EnragePhase.ACTIVE:
			break
		minion_max = maxi(minion_max, _enemies_alive().size())
		waves_max = maxi(waves_max, boss5._enrage_seq.summon_waves())
		ring5_max = maxi(ring5_max, _count_meta_bullets(&"enrage_ring"))
	_check(waves_max >= 3, "场景5：ACTIVE 共放 3 波小怪（实测 %d 波）" % waves_max)
	_check(minion_max >= 6, "场景5：小怪波次在场（峰值 %d 只）" % minion_max)
	_check(ring5_max >= 8, "场景5：自身每 0.9s 一圈 8 向环弹")
	var hold5 := false
	for i in 60:
		await _wait_real(0.05)
		if not is_instance_valid(boss5):
			break
		if boss5._enrage_seq.phase() == Boss.EnragePhase.RELEASE_HOLD:
			hold5 = true
			break
	_check(hold5, "场景5：ACTIVE 结束进入 RELEASE_HOLD")
	var ring5_total := 0
	var volley5_max := 0
	for i in 30:
		await _wait_real(0.05)
		if not is_instance_valid(boss5):
			break
		ring5_total = maxi(ring5_total, _count_meta_bullets(&"enrage_ring"))
		volley5_max = maxi(volley5_max, _bullets_by_speed(420.0).size())
		if ring5_total >= 16 and volley5_max >= 3:
			break
	_check(ring5_total >= 16, "场景5：收尾一次性 16 向慢速环弹（峰值 %d）" % ring5_total)
	_check(volley5_max >= 3, "场景5：收尾在场小怪齐射一轮（峰值 %d 发）" % volley5_max)
	if is_instance_valid(boss5):
		boss5.take_damage(9999)
		await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_field()

	# ================= 场景 6：难度分档（§4.4） =================
	# 分档在 Boss._ready 配置载入后一次性乘算，改难度必须在生成前；基准值均为 medium 档
	GameState.difficulty = &"easy"
	var boss6e: Boss = await _spawn_test_boss(1)
	_check(boss6e != null, "场景6：easy Boss 已生成")
	_check(boss6e.E1_RING_COUNT == 10, "场景6：easy 狂暴环弹 12-2=10（实测 %d）" % boss6e.E1_RING_COUNT)
	_check(boss6e.CANNON_SHOTS == 2, "场景6：easy 蓄力重炮 3-1=2 发（实测 %d）" % boss6e.CANNON_SHOTS)
	_check(boss6e._attacks.fan_delta == -1 and boss6e._attacks.homing_delta == -1, "场景6：easy 扇形/追踪弹数 -1")
	var p2_interval_e: float = boss6e._patterns["p2"][0]["interval"]
	_check(absf(p2_interval_e - 2.4 * 1.15) < 0.01, "场景6：easy 开火间隔 ×1.15（实测 %.3f）" % p2_interval_e)
	_check(absf(boss6e.FAN_BULLET_SPEED - 380.0 * 0.9) < 0.01, "场景6：easy 弹速 ×0.9（实测 %.1f）" % boss6e.FAN_BULLET_SPEED)
	var hp_e: int = boss6e.max_hp
	boss6e.queue_free()
	await get_tree().process_frame

	GameState.difficulty = &"hard"
	var boss6h: Boss = await _spawn_test_boss(1)
	_check(boss6h != null, "场景6：hard Boss 已生成")
	_check(boss6h.E1_RING_COUNT == 14, "场景6：hard 狂暴环弹 12+2=14（实测 %d）" % boss6h.E1_RING_COUNT)
	_check(boss6h.CANNON_SHOTS == 4, "场景6：hard 蓄力重炮 3+1=4 发（实测 %d）" % boss6h.CANNON_SHOTS)
	_check(boss6h._attacks.fan_delta == 1 and boss6h._attacks.homing_delta == 1, "场景6：hard 扇形/追踪弹数 +1")
	var p2_interval_h: float = boss6h._patterns["p2"][0]["interval"]
	_check(absf(p2_interval_h - 2.4 * 0.85) < 0.01, "场景6：hard 开火间隔 ×0.85（实测 %.3f）" % p2_interval_h)
	_check(absf(boss6h.FAN_BULLET_SPEED - 380.0 * 1.1) < 0.01, "场景6：hard 弹速 ×1.1（实测 %.1f）" % boss6h.FAN_BULLET_SPEED)
	_check(boss6h.max_hp == hp_e * 2, "场景6：HP 随难度分档 ×0.75/×1.5（hard/easy=2.0，实测 %d/%d）" % [boss6h.max_hp, hp_e])
	boss6h.queue_free()
	await get_tree().process_frame
	GameState.difficulty = &"medium"

	_check(is_equal_approx(Engine.time_scale, 1.0), "收尾：退出前 time_scale = 1.0")
	_check(is_equal_approx(player._enrage_slow, 1.0), "收尾：退出前玩家减速已复位")
	await _clear_field()
	await _wait_real(2.0)  # 演出 tween/爆炸序列播完，避免退出时对象泄漏
	print("BOSS PATTERN TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
