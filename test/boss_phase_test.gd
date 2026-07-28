extends Node
## Boss 阶段框架测试（BOSS_REDESIGN §4，阶段 A）：
## 场景1（一型）：P1→P2→ENRAGE 阈值依次到达、模式表循环推进、段切换清计时/锁血语义不变；
## 场景2（二型）：狙击 telegraph 时序（先瞄准线、≥0.3s 后才出弹、3 连发、线用完即毁）；
## 场景3（三型）：旋转 cross + 召唤填表验证；
## 场景4：血条阶段刻度线存在、逃跑倒计时显示与随 Boss 死亡隐藏。

var _failures: int = 0
var _phase_signal: int = -1  # 最近收到的 phase_changed


func _check(cond: bool, label: String) -> void:
	if cond:
		print("[PASS] ", label)
	else:
		_failures += 1
		printerr("[FAIL] ", label)


## 真实时间等待（不受 time_scale 影响；process_always 保证暂停时也走时）
func _wait_real(sec: float) -> void:
	await get_tree().create_timer(sec, true, false, true).timeout


## 在场敌弹（玩家弹排除）
func _enemy_bullets() -> Array[Bullet]:
	var out: Array[Bullet] = []
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet:
			out.append(child)
	return out


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
	boss.position.y = boss.FIGHT_Y  # 跳过降入，下一物理帧进入战斗
	return boss


func _ready() -> void:
	# 清理持久化状态，保证测试确定性
	GameState.delete_save()
	GameState.high_score = 0
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
	player._invincible = 999.0  # 弹幕期间不被误伤
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性
	player.position = Vector2(960.0, 540.0)

	# ================= 场景 1：一型阶段阈值切换 + 模式表循环 =================
	var boss: Boss = await _spawn_test_boss(1)
	_check(boss != null, "场景1：Boss 已生成")
	boss.phase_changed.connect(func(p: int) -> void: _phase_signal = p)
	# 缩短模式表便于观测循环推进（实例 var 覆盖，不影响 balance.json）
	boss._patterns = {
		"p1": [
			{"attack": &"fan5", "waves": 2, "interval": 0.25},
			{"attack": &"homing", "waves": 1, "interval": 0.25},
		],
		"p2": [{"attack": &"fan7", "waves": 2, "interval": 0.25}],
	}
	boss._pattern_index = 0
	boss._start_pattern()
	await _wait_real(0.3)
	_check(boss._fight_phase == Boss.FightPhase.P1, "场景1：初始为 P1")
	# 模式循环推进：fan5 两波播完应切到 homing（index 0→1）
	var advanced := false
	for i in 20:
		await _wait_real(0.1)
		if not is_instance_valid(boss):
			break
		if boss._pattern_index != 0:
			advanced = true
			break
	_check(advanced, "场景1：模式表波次播完推进到下一模式")
	_check(_enemy_bullets().size() >= 5, "场景1：模式攻击出弹（5 路扇形波次）")
	# P1→P2：打到 65%（≤70% 阈值）
	boss.take_damage(int(boss.max_hp * 0.35))
	await get_tree().process_frame
	_check(boss._fight_phase == Boss.FightPhase.P2, "场景1：HP ≤70% 进入 P2")
	_check(_phase_signal == Boss.FightPhase.P2, "场景1：段切换发出 phase_changed")
	_check(
		is_equal_approx(boss.hp, boss.max_hp * 0.65),
		"场景1：P2 阈值不钳血（锁血仅狂暴 30% 语义不变）"
	)
	_check(not boss._enrage_health_lock, "场景1：P2 段切换不触发锁血")
	_check(boss._pattern_index == 0, "场景1：段切换重置模式表循环")
	# P2→ENRAGE：打到 25%（钳 30% 触发狂暴；一击跨两段狂暴优先）
	boss.take_damage(int(boss.max_hp * 0.4))
	await get_tree().process_frame
	_check(boss._enraged and boss._fight_phase == Boss.FightPhase.ENRAGE, "场景1：HP <30% 进入 ENRAGE")
	_check(boss._enrage_health_lock, "场景1：狂暴锁血语义不变")
	_check(is_equal_approx(player._enrage_slow, 0.35), "场景1：TRANSITION 中玩家减速 ×0.35")
	# 快进 main 子弹时间等恢复
	main._bullet_time_left = 0.05
	for i in 40:
		await _wait_real(0.1)
		if is_equal_approx(Engine.time_scale, 1.0):
			break
	# 序列中断复位减速；击杀后保持 1.0
	boss._abort_enrage_sequence()
	_check(is_equal_approx(player._enrage_slow, 1.0), "场景1：序列中断复位玩家减速")
	boss.take_damage(9999)
	await get_tree().process_frame
	_check(not is_instance_valid(boss), "场景1：解锁后可击杀")
	_check(is_equal_approx(player._enrage_slow, 1.0), "场景1：Boss 被击杀后减速保持复位")
	_close_buff_ui_if_open()
	for b in _enemy_bullets():
		b.queue_free()
	await get_tree().process_frame

	# ================= 场景 2：二型狙击 telegraph 时序 =================
	var boss2: Boss = await _spawn_test_boss(2)
	_check(boss2 != null, "场景2：Boss 已生成")
	boss2._patterns = {"p1": [{"attack": &"sniper3", "waves": 1, "interval": 1.2}], "p2": [{"attack": &"sniper3", "waves": 1, "interval": 1.2}]}
	boss2._pattern_index = 0
	boss2._start_pattern()
	boss2._fire_timer = 0.1  # 立即起手
	var line_appeared := false
	var line_tick := 0
	for i in 30:
		await _wait_real(0.05)
		if not is_instance_valid(boss2):
			break
		if boss2._aim_line != null:
			line_appeared = true
			line_tick = Time.get_ticks_msec()
			break
	_check(line_appeared, "场景2：狙击先出现瞄准线 telegraph")
	_check(_enemy_bullets().is_empty(), "场景2：telegraph 期间未出弹")
	var fire_elapsed := -1
	for i in 40:
		await _wait_real(0.05)
		if not _enemy_bullets().is_empty():
			fire_elapsed = Time.get_ticks_msec() - line_tick
			break
	_check(fire_elapsed >= 300, "场景2：瞄准线出现 ≥0.3s 后才出弹（实测 %dms）" % fire_elapsed)
	_check(boss2._aim_line == null, "场景2：出弹后瞄准线即毁")
	await _wait_real(0.4)  # 3 连发 0.12s 间隔
	_check(_enemy_bullets().size() == 3, "场景2：到点沿线 3 连发出弹")
	boss2.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	for b in _enemy_bullets():
		b.queue_free()
	await get_tree().process_frame

	# ================= 场景 3：三型旋转 cross + 召唤 =================
	var boss3: Boss = await _spawn_test_boss(3)
	_check(boss3 != null, "场景3：Boss 已生成")
	boss3._fire_timer = 0.1
	boss3._summon_timer = 0.3
	await _wait_real(0.2)  # 首波 cross 出弹后立即断言（向上弹 0.6s 内即出屏消失）
	_check(_enemy_bullets().size() >= 4, "场景3：旋转 cross 出弹（一波 4 弹）")
	await _wait_real(0.4)
	var minion_found := false
	for child in get_node("Main").get_children():
		if child is Enemy:
			minion_found = true
	_check(minion_found, "场景3：召唤小怪独立计时保持")
	boss3.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	for child in get_node("Main").get_children():
		if child is Enemy or (child is Bullet and not child.is_player_bullet):
			child.queue_free()
	await get_tree().process_frame

	# ================= 场景 4：血条刻度线 + 逃跑倒计时 =================
	var hud: CanvasLayer = get_node("Main/HUD")
	_check(get_node("Main/HUD/BossBar").get_child_count() >= 1, "场景4：血条有阶段刻度线覆盖层")
	var boss4: Boss = await _spawn_test_boss(1)
	_check(boss4 != null, "场景4：Boss 已生成")
	boss4._fire_timer = 999.0  # 屏蔽开火，保持场内干净
	await _wait_real(0.3)
	_check(not hud._boss_countdown.visible, "场景4：剩余 >10s 不显示倒计时")
	boss4._survival = boss4.ESCAPE_TIME - 5.0  # 剩余 5s ≤ countdown_visible_from(10s)
	await _wait_real(0.3)
	_check(
		hud._boss_countdown.visible and hud._boss_countdown.text != "",
		"场景4：剩余 ≤10s 血条下方显示逃跑倒计时"
	)
	boss4.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	await _wait_real(0.3)
	_check(not hud._boss_countdown.visible, "场景4：Boss 死亡后倒计时隐藏")

	_check(is_equal_approx(Engine.time_scale, 1.0), "收尾：退出前 time_scale = 1.0")
	_check(is_equal_approx(player._enrage_slow, 1.0), "收尾：退出前玩家减速已复位")
	for child in get_node("Main").get_children():
		if child is Bullet:
			child.queue_free()
	await get_tree().process_frame
	await _wait_real(2.0)  # 演出 tween/爆炸序列播完，避免退出时对象泄漏
	print("BOSS PHASE TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
