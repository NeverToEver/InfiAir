extends Node
## Boss 狂暴完整序列测试（一型「旋转堡垒」差异化序列，BOSS_REDESIGN §5.1）：
## 触发（锁血 30% + 快照玩家位置 + 玩家减速 ×0.35 + 子弹时间）→ TRANSITION（蓄力抖动，
## 一型悬停原地不滑入轨道）→ ACTIVE（原地每 0.5s 一波 12 向环弹，起始角随波次进动；
## 玩家减速可移动/射击/冲刺）→ RELEASE_HOLD（解血锁/复位移速，蓄力 0.5s 后 8 路重炮齐射）
## → RETURN 飞回战斗位 → 常规「余怒」循环（射速 ×1.3）。
## 另覆盖：序列中到点逃跑中断序列、子弹时间恢复兜底。
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


## 在场狂暴弹幕弹丸总数（快照/ACTIVE 波次/RELEASE 波次共用 laser + enrage_ring 两种 meta）
func _count_enrage_bullets() -> int:
	var n := 0
	for child in get_node("Main").get_children():
		if child is Bullet and not child.is_player_bullet and child.has_meta("bullet_type"):
			var t: StringName = child.get_meta("bullet_type")
			if t == &"laser" or t == &"enrage_ring":
				n += 1
	return n


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
		buff_ui.pick_buff(&"rapid_fire")
	get_tree().paused = false


## 生成 Boss 并压缩序列时长（实例 var 覆盖，不影响 balance.json），加速测试
func _spawn_test_boss() -> Boss:
	var spawner: Node = get_node("Main/Spawner")
	spawner.spawn_boss(1)
	await get_tree().process_frame
	var boss: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss = child
	boss.ENRAGE_DURATION = 1.5
	boss.ENRAGE_TRANSITION_DURATION = 0.2
	boss.ENRAGE_ATTACK_WINDUP = 0.1
	boss.ENRAGE_ATTACK_INTERVAL = 0.4
	boss.ENRAGE_RELEASE_HOLD_DURATION = 0.4
	boss.ENRAGE_RETURN_DURATION = 0.4
	boss.E1_SALVO_CHARGE = 0.2  # 压缩收尾蓄力，适配 0.4s RELEASE_HOLD
	boss.position.y = boss.fight_anchor_y()  # 跳过降入（锚线 = view 顶缘 + FIGHT_Y），下一物理帧进入战斗
	return boss


## 快进 main 子弹时间并轮询真实时间等 time_scale 恢复 1.0
func _wait_time_scale_restored() -> bool:
	get_node("Main").set_bullet_time(0.05)
	for i in 40:  # 最多 ~4s 真实时间
		await _wait_real(0.1)
		if is_equal_approx(Engine.time_scale, 1.0):
			return true
	return false


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
	player.set_auto_fire(false)  # 全程禁用全自动开火，避免误杀 Boss/触发里程碑
	player.set_invincible(999.0)  # 狂暴弹幕期间不被误伤
	await get_tree().process_frame
	await get_tree().process_frame
	var spawner: Node = get_node("Main/Spawner")
	spawner.set_process(false)  # 停掉自动刷怪/Boss 调度，保证确定性

	# ================= 场景 1：完整狂暴序列 =================
	player.position = Vector2(960.0, 540.0)  # 屏幕中部，轨道半径不被边界钳死
	var boss: Boss = await _spawn_test_boss()
	_check(boss != null, "场景1：Boss 已生成")
	await _wait_real(0.3)
	boss.take_damage(int(boss.max_hp * 0.75))  # 非致死大额伤害：钳到 30% 阈值，触发狂暴
	await get_tree().process_frame
	_check(boss.is_enraged(), "场景1：血量 <30% 触发狂暴")
	_check(boss.base_modulate_color() != Color.WHITE, "场景1：狂暴贴图变红")
	_check(boss.enrage_sequence().phase() == Boss.EnragePhase.TRANSITION, "场景1：触发进入 TRANSITION")
	_check(is_equal_approx(Engine.time_scale, 0.24), "场景1：狂暴瞬间进入子弹时间 time_scale=0.24")
	_check(is_equal_approx(player.enrage_slow(), 0.35), "场景1：触发即施加玩家减速 ×0.35")
	_check(boss.enrage_sequence().snapshot_target().distance_to(player.global_position) < 5.0, "场景1：轨道中心为触发时玩家位置快照")
	# 锁血（触发→RELEASE_HOLD 前）：普通/致死伤害都不掉血不死
	var hp0: float = boss.hp
	boss.take_damage(50)
	_check(is_equal_approx(boss.hp, hp0), "场景1：锁血期普通伤害不掉血")
	boss.take_damage(9999)
	await get_tree().process_frame
	_check(is_instance_valid(boss) and is_equal_approx(boss.hp, hp0), "场景1：锁血期致死伤害也不死")
	# 减速功能验证在 ACTIVE 段进行（等 time_scale 恢复后速度才能爬到上限）
	# 快进子弹时间，等 TRANSITION 结束进入 ACTIVE
	main.set_bullet_time(0.05)
	var active := false
	for i in 40:  # 最多 ~4s 真实时间
		await _wait_real(0.1)
		if not is_instance_valid(boss):
			break
		if boss.enrage_sequence().phase() == Boss.EnragePhase.ACTIVE:
			active = true
			break
	_check(active, "场景1：TRANSITION 结束进入 ACTIVE")
	_check(is_equal_approx(player.enrage_slow(), 0.35), "场景1：ACTIVE 期间玩家保持减速 ×0.35")
	# 减速功能验证：仍可位移，但移速上限 ×0.35（time_scale 已恢复 1.0）
	Input.action_press("move_right")
	await _wait_real(0.6)
	var slow_vx := absf(player.velocity.x)
	Input.action_release("move_right")
	_check(slow_vx > 30.0, "场景1：减速期间方向键输入仍可位移")
	_check(slow_vx <= player.MAX_SPEED * 0.35 + 5.0, "场景1：减速期间移速上限 ×0.35")
	player.position = Vector2(960.0, 540.0)
	player.velocity = Vector2.ZERO
	# 一型「旋转堡垒」：ACTIVE 悬停原地（不绕轨道），每 0.5s 一波环弹且起始角进动
	var samples: Array[Vector2] = []
	for i in 6:
		if is_instance_valid(boss):
			samples.append(boss.global_position)
		await _wait_real(0.12)
	var max_d := 0.0
	for i in samples.size():
		for j in i:
			max_d = maxf(max_d, samples[i].distance_to(samples[j]))
	_check(max_d < 20.0, "场景1：ACTIVE 期 Boss 悬停原地（旋转堡垒）")
	_check(boss.enrage_sequence().attack_index() >= 1 and boss.enrage_sequence().ring_angle() > 0.01, "场景1：环弹波次开火且起始角随波次进动")
	_check(_count_enrage_bullets() > 0, "场景1：ACTIVE 期环弹开火")
	# 等 ACTIVE 计时耗尽进入 RELEASE_HOLD
	var hold := false
	for i in 60:
		await _wait_real(0.1)
		if not is_instance_valid(boss):
			break
		if boss.enrage_sequence().phase() == Boss.EnragePhase.RELEASE_HOLD:
			hold = true
			break
	_check(hold, "场景1：ACTIVE 结束进入 RELEASE_HOLD")
	_check(is_equal_approx(player.enrage_slow(), 1.0), "场景1：RELEASE_HOLD 复位玩家减速")
	# 一型收尾：蓄力 telegraph 后 8 路重炮齐射（700 弹速重弹，一次性）。
	# flake 修复（2026-08-03 CI 门禁，第三次复现后根因确认）：8 路 360° 齐射向上路
	# ~0.27s 即出屏，场上计数依赖「发射时刻 vs 采样开始」竞争，慢 runner 上稳定失败；
	# 改断言发射标记本身（release_salvo_done，RELEASE_HOLD 复位、发射置位且保持）——
	# 不依赖弹在场时序
	var salvo := false
	for i in 60:
		await _wait_real(0.05)
		if not is_instance_valid(boss):
			break
		if boss.enrage_sequence().release_salvo_done():
			salvo = true
			break
	_check(salvo, "场景1：RELEASE_HOLD 蓄力后 8 路重炮齐射")
	var hp1: float = boss.hp
	boss.take_damage(5)
	_check(boss.hp < hp1, "场景1：RELEASE_HOLD 解血锁后可掉血")
	# 等 RETURN 结束回归常规狂暴循环
	var done := false
	for i in 60:
		await _wait_real(0.1)
		if not is_instance_valid(boss):
			break
		if boss.enrage_sequence().phase() == Boss.EnragePhase.NONE:
			done = true
			break
	_check(done, "场景1：RETURN 结束回归常规阶段")
	if is_instance_valid(boss):
		_check(absf(boss.position.y - boss.fight_anchor_y()) < 40.0, "场景1：RETURN 飞回战斗位")
		# 永久「余怒」射速 ×1.3（计时器流速 ×1.3，§5.4）
		boss.set_fire_timer(1.6)
		await get_tree().create_timer(0.5).timeout  # scale 已为 1.0，0.5s 真实 = 0.5 游戏秒
		_check(boss.fire_timer() < 1.0, "场景1：序列后保持余怒射速 ×1.3")
		# 解锁后可击杀
		boss.take_damage(9999)
		await get_tree().process_frame
		_check(not is_instance_valid(boss), "场景1：序列结束后可击杀")
	_close_buff_ui_if_open()  # 击杀得分触发里程碑暂停：关闭后恢复驱动
	await _clear_enemy_bullets()

	# ================= 场景 2：序列中到点逃跑（中断序列 + 子弹时间兜底） =================
	var boss2: Boss = await _spawn_test_boss()
	_check(boss2 != null, "场景2：Boss 已生成")
	# 场景1 狂暴把血条染红（DANGER），新 Boss 开场必须重置回 ACCENT
	_check(get_node("Main/HUD/BossBar").fill_color == UITheme.ACCENT, "场景2：第二只 Boss 开场血条重置为 ACCENT")
	await _wait_real(0.3)
	boss2.take_damage(int(boss2.max_hp * 0.75))
	await get_tree().process_frame
	_check(boss2.is_enraged() and is_equal_approx(player.enrage_slow(), 0.35), "场景2：狂暴触发并减速玩家")
	_check(is_equal_approx(Engine.time_scale, 0.24), "场景2：子弹时间启动")
	boss2.set_survival(boss2.ESCAPE_TIME - 0.02)  # 下一秒到点：序列中照样逃跑
	var escaping := false
	for i in 40:
		await _wait_real(0.1)
		if not is_instance_valid(boss2) or boss2.is_escaping():
			escaping = true
			break
	_check(escaping, "场景2：狂暴序列中到点照样逃跑")
	if is_instance_valid(boss2):
		_check(boss2.enrage_sequence().phase() == Boss.EnragePhase.NONE, "场景2：逃跑中断狂暴序列")
	_check(is_equal_approx(player.enrage_slow(), 1.0), "场景2：逃跑复位玩家减速")
	# main 统一接管的恢复过渡不受 Boss 离场影响，仍应回到 1.0
	_check(await _wait_time_scale_restored(), "场景2：Boss 子弹时间内离场，time_scale 仍恢复 1.0")
	_check(main.time_scale_ramp() < 0.0, "场景2：恢复过渡已结束")

	_check(is_equal_approx(Engine.time_scale, 1.0), "收尾：退出前 time_scale = 1.0")
	await _wait_real(2.0)  # 让场景2 逃跑 Boss 出屏释放、演出 tween 播完，避免退出时对象泄漏
	# L15：还原用户最高分并落盘（收尾不污染用户 profile）
	GameState.high_score = orig_high_score
	GameState.save_profile()
	# ================= 场景 5：四型「月蚀」狂暴（双环反向进动 + 蓄力环阵） =================
	await get_tree().process_frame
	var spawner5: Node = get_node("Main/Spawner")
	spawner5.spawn_boss(4)
	await get_tree().process_frame
	var boss5: Boss = null
	for child in get_node("Main").get_children():
		if child is Boss:
			boss5 = child
	_check(boss5 != null, "场景5：月蚀已生成")
	boss5.ENRAGE_DURATION = 1.5
	boss5.ENRAGE_TRANSITION_DURATION = 0.2
	boss5.ENRAGE_ATTACK_WINDUP = 0.1
	boss5.ENRAGE_ATTACK_INTERVAL = 0.4
	boss5.ENRAGE_RELEASE_HOLD_DURATION = 0.4
	boss5.ENRAGE_RETURN_DURATION = 0.4
	boss5.position.y = boss5.fight_anchor_y()
	player.position = Vector2(960.0, 540.0)
	await _wait_real(0.3)
	boss5.take_damage(int(boss5.max_hp * 0.75))
	await get_tree().process_frame
	_check(boss5.is_enraged(), "场景5：月蚀血量 <30% 触发狂暴")
	var e5 := boss5.enrage_sequence()
	# 狂暴子弹时间（time_scale 0.24）拉伸序列 4 倍：压缩序列 1.5s → 真实 ~6.3s，
	# 采样窗口须覆盖全程（6s 真实 = 1.44s 缩放）
	var double_ring_seen := false
	var release_ring_seen := false
	var phase_seen_release := false
	for i in 120:
		await _wait_real(0.05)
		if not is_instance_valid(boss5):
			break
		var now_rings := _count_enrage_bullets()
		if now_rings >= 20:
			double_ring_seen = true
		if now_rings >= 40:
			release_ring_seen = true
		if e5.phase() == Boss.EnragePhase.RELEASE_HOLD:
			phase_seen_release = true
	_check(double_ring_seen, "场景5：ACTIVE 双环同帧 ≥20 向（正环+反环）")
	_check(phase_seen_release, "场景5：序列推进到 RELEASE_HOLD")
	_check(release_ring_seen, "场景5：收尾蓄力环阵（20 向 + 残留环）")

	print("BOSS ENRAGE TEST DONE, failures = ", _failures)
	GameState.delete_save()
	get_tree().quit(_failures)
