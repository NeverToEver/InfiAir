extends Node
## Boss 狂暴完整版测试：子弹时间（time_scale 0.24 → 恢复 1.0）、快照弹幕（4 激光 + 8 环弹）、
## 锁玩家移动 0.5s、以及既有简化版狂暴行为（触发/变红/射速 ×1.5）回归。
## 时序坑：Engine.time_scale 会缩放 create_timer 默认等待，真实时间等待用 ignore_time_scale=true。

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


## 统计在场快照弹幕弹丸：x = 激光数，y = 环弹数（按 boss 快照弹幕打的 meta 区分）
func _count_snapshot_bullets() -> Vector2i:
	var lasers := 0
	var rings := 0
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet:
			if child.has_meta("bullet_type") and child.get_meta("bullet_type") == &"laser":
				lasers += 1
			elif child.has_meta("bullet_type") and child.get_meta("bullet_type") == &"enrage_ring":
				rings += 1
	return Vector2i(lasers, rings)


func _clear_enemy_bullets() -> void:
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet:
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
	player._invincible = 999.0  # 快照弹幕期间不被误伤
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性

	# ================= 场景 1：完整狂暴序列 =================
	spawner._spawn_boss(1)
	await get_tree().process_frame
	var boss: Boss = null
	for child in main.get_children():
		if child is Boss:
			boss = child
	_check(boss != null, "场景1：Boss 已生成")
	boss.position.y = boss.FIGHT_Y  # 跳过降入，下一物理帧进入战斗
	await _wait_real(0.3)
	boss.take_damage(int(boss.max_hp * 0.75))  # 非致死大额伤害：钳到 30% 阈值，触发狂暴
	await get_tree().process_frame
	_check(boss._enraged, "场景1：血量 <30% 触发狂暴（既有行为）")
	_check(boss._base_modulate() != Color.WHITE, "场景1：狂暴贴图变红（既有行为）")
	_check(is_equal_approx(Engine.time_scale, 0.24), "场景1：狂暴瞬间进入子弹时间 time_scale=0.24")
	_check(_count_snapshot_bullets() == Vector2i.ZERO, "场景1：子弹时间期间尚未发快照弹幕")
	boss._fire_timer = 999.0  # 屏蔽常规开火，保证快照弹幕计数纯净
	# 轮询真实时间等快照弹幕（子弹时间 1.2 游戏秒 ≈ 5s 真实 + 0.3s 恢复过渡）
	var counts := Vector2i.ZERO
	var snapshot_seen := false
	for i in 48:  # 最多 ~12s 真实时间
		await _wait_real(0.25)
		counts = _count_snapshot_bullets()
		if counts.x >= boss.ENRAGE_SNAPSHOT_LASERS and counts.y >= boss.ENRAGE_SNAPSHOT_RING:
			snapshot_seen = true
			break
	_check(snapshot_seen, "场景1：子弹时间结束后发出快照弹幕")
	_check(counts.x == boss.ENRAGE_SNAPSHOT_LASERS, "场景1：快照弹幕含 4 道激光向弹")
	_check(counts.y == boss.ENRAGE_SNAPSHOT_RING, "场景1：快照弹幕含 8 方向环形慢弹")
	# 激光向弹：朝玩家方向的高速长弹
	var want_dir: Vector2 = (player.global_position - boss.global_position).normalized()
	var laser_aimed := false
	var ring_dirs: Array[Vector2] = []
	for child in main.get_children():
		if child is Bullet and not child.is_player_bullet:
			if child.has_meta("bullet_type") and child.get_meta("bullet_type") == &"laser":
				if child.speed >= 700.0 and child.direction.dot(want_dir) > 0.99:
					laser_aimed = true
			elif child.has_meta("bullet_type") and child.get_meta("bullet_type") == &"enrage_ring":
				ring_dirs.append(child.direction)
	_check(laser_aimed, "场景1：激光向弹高速且瞄准玩家")
	var ring_slow := true
	var ring_spread := true
	for i in ring_dirs.size():
		if not is_equal_approx(ring_dirs[i].length(), 1.0):
			ring_spread = false
		for j in i:
			if ring_dirs[i].dot(ring_dirs[j]) > 0.99:
				ring_spread = false  # 同向平行即非 8 向分散
	for child in main.get_children():
		if child is Bullet and child.has_meta("bullet_type") and child.get_meta("bullet_type") == &"enrage_ring":
			if child.speed > 400.0:
				ring_slow = false
	_check(ring_slow, "场景1：环形弹为慢速弹")
	_check(ring_dirs.size() == 8 and ring_spread, "场景1：环形弹 8 向均匀分散")
	# 快照弹幕期间锁玩家移动 0.5s（轮询间隔 0.25s 内必仍处锁定期）
	_check(player._input_locked, "场景1：快照弹幕期间玩家输入被锁定")
	_check(is_equal_approx(Engine.time_scale, 1.0), "场景1：子弹时间结束后 time_scale 恢复 1.0")
	await _wait_real(1.0)  # 锁定 0.5 游戏秒（此时 scale=1.0，即 0.5s 真实）
	_check(not player._input_locked, "场景1：0.5s 后玩家输入解锁")
	# 既有狂暴行为回归：射速 ×1.5（计时器流速 ×1.5）
	boss._fire_timer = 10.0
	await get_tree().create_timer(1.0).timeout  # scale 已为 1.0，1s 真实 = 1 游戏秒
	_check(boss._fire_timer < 9.0, "场景1：狂暴射速 ×1.5 保持（既有行为）")
	# 收尾：击杀 Boss，关闭里程碑 Buff UI，清弹幕
	boss.take_damage(9999)
	await get_tree().process_frame
	_close_buff_ui_if_open()
	await _clear_enemy_bullets()

	# ================= 场景 2：子弹时间内击杀 Boss 的边界 =================
	spawner._spawn_boss(1)
	await get_tree().process_frame
	var boss2: Boss = null
	for child in main.get_children():
		if child is Boss:
			boss2 = child
	_check(boss2 != null, "场景2：Boss 已生成")
	# 场景1 狂暴把血条染红（DANGER），新 Boss 开场必须重置回 ACCENT
	_check(get_node("Main/HUD/BossBar").fill_color == UITheme.ACCENT, "场景2：第二只 Boss 开场血条重置为 ACCENT")
	boss2.position.y = boss2.FIGHT_Y
	await _wait_real(0.3)
	boss2.take_damage(int(boss2.max_hp * 0.75))
	await get_tree().process_frame
	_check(boss2._enraged, "场景2：狂暴触发")
	_check(is_equal_approx(Engine.time_scale, 0.24), "场景2：子弹时间启动")
	boss2.take_damage(9999)  # 子弹时间内直接击杀
	await get_tree().process_frame
	_check(not is_instance_valid(boss2), "场景2：Boss 子弹时间内被击杀")
	_close_buff_ui_if_open()  # 击杀得分触发里程碑暂停：关闭后恢复驱动
	# main 统一接管的恢复过渡不受 Boss 死亡影响，仍应回到 1.0
	var restored := false
	for i in 40:  # 最多 ~10s 真实时间
		await _wait_real(0.25)
		if is_equal_approx(Engine.time_scale, 1.0):
			restored = true
			break
	_check(restored, "场景2：Boss 死于子弹时间，time_scale 仍恢复 1.0")
	_check(main._time_scale_ramp < 0.0, "场景2：恢复过渡已结束")
	_check(not player._input_locked, "场景2：未进入快照锁定期，输入未被锁")

	_check(is_equal_approx(Engine.time_scale, 1.0), "收尾：退出前 time_scale = 1.0")
	print("BOSS ENRAGE TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
